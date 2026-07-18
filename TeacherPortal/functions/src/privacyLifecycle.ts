import { randomBytes, createHash } from "node:crypto";
import { initializeApp } from "firebase-admin/app";
import { getAuth } from "firebase-admin/auth";
import { FieldValue, getFirestore, type DocumentReference } from "firebase-admin/firestore";
import { getStorage } from "firebase-admin/storage";
import { onDocumentCreated } from "firebase-functions/v2/firestore";
import { HttpsError, onCall, onRequest } from "firebase-functions/v2/https";
import { defineSecret } from "firebase-functions/params";
import { onSchedule } from "firebase-functions/v2/scheduler";
import { RoomServiceClient, WebhookReceiver } from "livekit-server-sdk";
import {
  createBuddyVoiceAccessToken,
  pruneBuddyVoiceLeases,
  type BuddyVoiceDispatchMetadata,
} from "./buddyVoiceAccess.js";
import {
  assessPronunciationWithAzureRest,
  normalizeAzurePronunciationAssessment,
} from "./azurePronunciationRest.js";
import { StudentPrivacySettings, auth, db, livekitApiKey, livekitApiSecret, privacyPolicyVersion, studentDeletionGraceDays } from "./runtime.js";
import { requireRole } from "./authorizationHelpers.js";
import { closeActiveBuddyVoiceRoom } from "./buddyVoiceHelpers.js";
import { requiredTrimmedString, trimmedString } from "./inputHelpers.js";
import { assertStudentAccess, defaultStudentPrivacySettings, readStudentPrivacy } from "./privacyHelpers.js";
import { shortError } from "./pronunciationProcessingHelpers.js";
import { assertFirestorePathSegment } from "./sharedUtils.js";
import { deleteAuthUserIfExists, deleteStudentStorageObjects } from "./studentCleanupHelpers.js";
import { writeAudit } from "./studentRecordHelpers.js";

export const getStudentPrivacyStatus = onCall(async (request) => {
  const caller = requireRole(request.auth, ["admin", "teacher", "parent", "student"]);
  const schoolId = requiredTrimmedString(request.data?.schoolId, "schoolId");
  const studentId = requiredTrimmedString(request.data?.studentId, "studentId");
  await assertStudentAccess(caller, schoolId, studentId, false);
  const [snapshot, deletionSnapshot] = await Promise.all([
    db.doc(`schools/${schoolId}/students/${studentId}`).get(),
    db.doc(`studentDataDeletionRequests/${schoolId}_${studentId}`).get()
  ]);
  if (!snapshot.exists) throw new HttpsError("not-found", "Student was not found.");
  const privacy = readStudentPrivacy(snapshot.data() ?? {});
  return {
    schoolId,
    studentId,
    policyVersion: privacyPolicyVersion,
    requiresRenewal: privacy.consentStatus === "granted" && privacy.policyVersion !== privacyPolicyVersion,
    deletionStatus: trimmedString(deletionSnapshot.data()?.status, 32),
    deleteAfterUtc: trimmedString(deletionSnapshot.data()?.deleteAfterUtc, 64),
    privacy
  };
});

/** Only a linked parent or school administrator can grant/withdraw sensitive processing. */

export const setStudentPrivacyConsent = onCall(
  { secrets: [livekitApiKey, livekitApiSecret] },
  async (request) => {
  const caller = requireRole(request.auth, ["admin", "parent"]);
  const schoolId = requiredTrimmedString(request.data?.schoolId, "schoolId");
  const studentId = requiredTrimmedString(request.data?.studentId, "studentId");
  await assertStudentAccess(caller, schoolId, studentId, true);
  const granted = request.data?.granted === true;
  const source = caller.token.role === "parent" ? "verified_parent" : "school_authorized_admin";
  const nowUtc = new Date().toISOString();
  const privacy: StudentPrivacySettings = granted ? {
    consentStatus: "granted",
    policyVersion: privacyPolicyVersion,
    gameplayAnalyticsAllowed: request.data?.gameplayAnalyticsAllowed === true,
    buddyAllowed: request.data?.buddyAllowed === true,
    audioProcessingAllowed: request.data?.audioProcessingAllowed === true,
    handwritingEvidenceAllowed: request.data?.handwritingEvidenceAllowed === true,
    diagnosticsAllowed: request.data?.diagnosticsAllowed === true,
    consentedAtUtc: nowUtc,
    consentSource: source
  } : {
    ...defaultStudentPrivacySettings(),
    consentStatus: "withdrawn",
    consentedAtUtc: nowUtc,
    consentSource: source
  };
  const studentRef = db.doc(`schools/${schoolId}/students/${studentId}`);
  const eventRef = studentRef.collection("privacyConsentEvents").doc();
  const batch = db.batch();
  batch.set(studentRef, { privacy, updatedAt: FieldValue.serverTimestamp() }, { merge: true });
  batch.set(eventRef, {
    eventId: eventRef.id,
    studentId,
    schoolId,
    actorUid: caller.uid,
    actorRole: caller.token.role,
    policyVersion: privacyPolicyVersion,
    consentStatus: privacy.consentStatus,
    permissions: {
      gameplayAnalyticsAllowed: privacy.gameplayAnalyticsAllowed,
      buddyAllowed: privacy.buddyAllowed,
      audioProcessingAllowed: privacy.audioProcessingAllowed,
      handwritingEvidenceAllowed: privacy.handwritingEvidenceAllowed,
      diagnosticsAllowed: privacy.diagnosticsAllowed
    },
    createdAtUtc: nowUtc,
    createdAt: FieldValue.serverTimestamp()
  });
  await batch.commit();
  if (!privacy.buddyAllowed || !privacy.audioProcessingAllowed) {
    await closeActiveBuddyVoiceRoom(schoolId, studentId, "consent_withdrawn");
  }
  await writeAudit(schoolId, caller.uid, granted ? "student.privacy.granted" : "student.privacy.withdrawn", studentRef.path);
  return { ok: true, privacy };
});

/** Queues deletion with a short, cancellable grace period to prevent accidental loss. */

export const requestStudentDataDeletion = onCall(
  { secrets: [livekitApiKey, livekitApiSecret] },
  async (request) => {
  const caller = requireRole(request.auth, ["admin", "parent"]);
  const schoolId = requiredTrimmedString(request.data?.schoolId, "schoolId");
  const studentId = requiredTrimmedString(request.data?.studentId, "studentId");
  await assertStudentAccess(caller, schoolId, studentId, true);
  const studentRef = db.doc(`schools/${schoolId}/students/${studentId}`);
  const snapshot = await studentRef.get();
  if (!snapshot.exists) throw new HttpsError("not-found", "Student was not found.");
  const deleteAfterUtc = new Date(Date.now() + studentDeletionGraceDays * 86400000).toISOString();
  const requestId = `${schoolId}_${studentId}`;
  await db.doc(`studentDataDeletionRequests/${requestId}`).set({
    requestId,
    schoolId,
    studentId,
    classId: trimmedString(snapshot.data()?.classId, 160),
    authUid: trimmedString(snapshot.data()?.authUid, 160),
    requestedByUid: caller.uid,
    requestedByRole: caller.token.role,
    status: "pending",
    requestedAtUtc: new Date().toISOString(),
    deleteAfterUtc,
    updatedAt: FieldValue.serverTimestamp()
  }, { merge: true });
  await studentRef.set({ accountStatus: "deletion_pending", deletionRequestedAtUtc: new Date().toISOString() }, { merge: true });
  await closeActiveBuddyVoiceRoom(schoolId, studentId, "deletion_pending");
  await writeAudit(schoolId, caller.uid, "student.deletion.requested", studentRef.path);
  return { ok: true, requestId, deleteAfterUtc };
});

export const cancelStudentDataDeletion = onCall(async (request) => {
  const caller = requireRole(request.auth, ["admin", "parent"]);
  const schoolId = requiredTrimmedString(request.data?.schoolId, "schoolId");
  const studentId = requiredTrimmedString(request.data?.studentId, "studentId");
  await assertStudentAccess(caller, schoolId, studentId, true);
  const requestRef = db.doc(`studentDataDeletionRequests/${schoolId}_${studentId}`);
  const snapshot = await requestRef.get();
  if (!snapshot.exists || snapshot.data()?.status !== "pending") {
    throw new HttpsError("failed-precondition", "There is no pending deletion request to cancel.");
  }
  const batch = db.batch();
  batch.set(requestRef, { status: "cancelled", cancelledByUid: caller.uid, cancelledAtUtc: new Date().toISOString(), updatedAt: FieldValue.serverTimestamp() }, { merge: true });
  batch.set(db.doc(`schools/${schoolId}/students/${studentId}`), { accountStatus: "active", deletionRequestedAtUtc: "", updatedAt: FieldValue.serverTimestamp() }, { merge: true });
  await batch.commit();
  await writeAudit(schoolId, caller.uid, "student.deletion.cancelled", `schools/${schoolId}/students/${studentId}`);
  return { ok: true };
});

export const processStudentDataDeletionRequests = onSchedule(
  { schedule: "every day 04:07", timeZone: "Etc/UTC", timeoutSeconds: 540 },
  async () => {
    const due = await db.collection("studentDataDeletionRequests")
      .where("status", "==", "pending")
      .where("deleteAfterUtc", "<=", new Date().toISOString())
      .orderBy("deleteAfterUtc", "asc")
      .limit(25)
      .get();
    for (const document of due.docs) {
      const item = document.data();
      const schoolId = trimmedString(item.schoolId, 160);
      const studentId = trimmedString(item.studentId, 160);
      const classId = trimmedString(item.classId, 160);
      const authUid = trimmedString(item.authUid, 160);
      try {
        assertFirestorePathSegment(schoolId, "schoolId");
        assertFirestorePathSegment(studentId, "studentId");
        await document.ref.set({ status: "processing", processingAtUtc: new Date().toISOString(), updatedAt: FieldValue.serverTimestamp() }, { merge: true });
        await deleteStudentStorageObjects(schoolId, studentId);
        await db.recursiveDelete(db.doc(`schools/${schoolId}/students/${studentId}`));
        if (classId) {
          await db.doc(`schools/${schoolId}/classes/${classId}`).set({ studentIds: FieldValue.arrayRemove(studentId), updatedAt: FieldValue.serverTimestamp() }, { merge: true });
        }
        if (authUid) {
          await db.doc(`users/${authUid}`).delete().catch(() => undefined);
          await deleteAuthUserIfExists(authUid);
        }
        await document.ref.set({
          status: "completed",
          completedAtUtc: new Date().toISOString(),
          authUid: FieldValue.delete(),
          requestedByUid: FieldValue.delete(),
          updatedAt: FieldValue.serverTimestamp()
        }, { merge: true });
        console.info("[Privacy]", JSON.stringify({ event: "student_deletion_completed", schoolId, requestId: document.id }));
      } catch (error) {
        await document.ref.set({ status: "pending", lastError: shortError(error), lastAttemptAtUtc: new Date().toISOString(), updatedAt: FieldValue.serverTimestamp() }, { merge: true });
        console.error("[Privacy] deletion failed", { requestId: document.id, error });
      }
    }
  }
);
