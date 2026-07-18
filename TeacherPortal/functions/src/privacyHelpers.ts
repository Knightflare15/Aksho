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
import { CallableAuth, StudentPrivacySettings, db, privacyPolicyVersion } from "./runtime.js";
import { assertSameSchool } from "./authorizationHelpers.js";
import { trimmedString } from "./inputHelpers.js";
import { assertFirestorePathSegment, cloneRecord } from "./sharedUtils.js";

export function defaultStudentPrivacySettings(): StudentPrivacySettings {
  return {
    consentStatus: "pending",
    policyVersion: privacyPolicyVersion,
    gameplayAnalyticsAllowed: false,
    buddyAllowed: false,
    audioProcessingAllowed: false,
    handwritingEvidenceAllowed: false,
    diagnosticsAllowed: false,
    consentedAtUtc: "",
    consentSource: ""
  };
}

export function readStudentPrivacy(student: Record<string, unknown>): StudentPrivacySettings {
  const value = cloneRecord(student.privacy);
  const status = trimmedString(value.consentStatus, 24);
  return {
    consentStatus: status === "granted" ? "granted" : status === "withdrawn" ? "withdrawn" : "pending",
    policyVersion: trimmedString(value.policyVersion, 96),
    gameplayAnalyticsAllowed: value.gameplayAnalyticsAllowed === true,
    buddyAllowed: value.buddyAllowed === true,
    audioProcessingAllowed: value.audioProcessingAllowed === true,
    handwritingEvidenceAllowed: value.handwritingEvidenceAllowed === true,
    diagnosticsAllowed: value.diagnosticsAllowed === true,
    consentedAtUtc: trimmedString(value.consentedAtUtc, 64),
    consentSource: trimmedString(value.consentSource, 64)
  };
}

export function privacyPermissionGranted(privacy: StudentPrivacySettings, permission: keyof StudentPrivacySettings) {
  return privacy.consentStatus === "granted" &&
    privacy.policyVersion === privacyPolicyVersion &&
    privacy[permission] === true;
}

export async function studentPrivacyPermissionGranted(
  schoolId: string,
  studentId: string,
  permission: keyof StudentPrivacySettings
) {
  const snapshot = await db.doc(`schools/${schoolId}/students/${studentId}`).get();
  return snapshot.exists && privacyPermissionGranted(readStudentPrivacy(snapshot.data() ?? {}), permission);
}

export async function assertStudentAccess(caller: CallableAuth, schoolId: string, studentId: string, managingPrivacy: boolean) {
  assertFirestorePathSegment(schoolId, "schoolId");
  assertFirestorePathSegment(studentId, "studentId");
  assertSameSchool(caller, schoolId);
  const role = caller.token.role;
  if (managingPrivacy && role !== "admin" && role !== "parent") {
    throw new HttpsError("permission-denied", "Only a linked parent or school administrator can manage privacy.");
  }
  if (role === "student" && caller.token.studentId !== studentId) {
    throw new HttpsError("permission-denied", "Student token can only access its own privacy status.");
  }
  if (role === "parent" && !caller.token.studentIds?.includes(studentId)) {
    throw new HttpsError("permission-denied", "Parent token can only access a linked child.");
  }
  if (role === "teacher") {
    const snapshot = await db.doc(`schools/${schoolId}/students/${studentId}`).get();
    if (!snapshot.exists || !caller.token.classIds?.includes(String(snapshot.data()?.classId ?? ""))) {
      throw new HttpsError("permission-denied", "Teacher is not assigned to this student's class.");
    }
  }
}
