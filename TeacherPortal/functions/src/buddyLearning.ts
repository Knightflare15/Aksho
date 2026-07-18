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
import { StudentPayload, auth, db } from "./runtime.js";
import { requireRole, requireStudentPayload } from "./authorizationHelpers.js";
import { redactBuddyText } from "./buddyContextHelpers.js";
import { validateBuddyLearnerProfilePayload, validateBuddyLearningAttemptPayload, validateBuddyLearningSessionPayload } from "./eventValidation.js";
import { trimmedString } from "./inputHelpers.js";
import { studentPrivacyPermissionGranted } from "./privacyHelpers.js";
import { cloneNumberRecord, cloneRecord, finiteInteger, randomId } from "./sharedUtils.js";
import { writeStudentRecord } from "./studentRecordHelpers.js";

export const submitBuddyLearningAttempt = onCall(async (request) => {
  const caller = requireRole(request.auth, ["student", "parent"]);
  const payload = requireStudentPayload(caller, request.data);
  validateBuddyLearningAttemptPayload(payload);
  if (!await studentPrivacyPermissionGranted(payload.schoolId, payload.studentId, "buddyAllowed"))
    return { id: "", skipped: true, reason: "parental_consent_required" };
  const id = typeof payload.eventId === "string" ? payload.eventId : randomId();
  await writeStudentRecord(payload.schoolId, payload.studentId, "buddyLearningAttempts", id, {
    ...payload,
    submittedResponse: redactBuddyText(payload.submittedResponse, 520),
    questionPrompt: redactBuddyText(payload.questionPrompt, 520),
    correctedResponse: redactBuddyText(payload.correctedResponse, 520)
  });
  return { id };
});

export const submitBuddyLearningSession = onCall(async (request) => {
  const caller = requireRole(request.auth, ["student", "parent"]);
  const payload = requireStudentPayload(caller, request.data);
  validateBuddyLearningSessionPayload(payload);
  if (!await studentPrivacyPermissionGranted(payload.schoolId, payload.studentId, "buddyAllowed"))
    return { id: "", skipped: true, reason: "parental_consent_required" };
  const id = typeof payload.sessionId === "string" ? payload.sessionId : randomId();
  await writeStudentRecord(payload.schoolId, payload.studentId, "buddyLearningSessions", id, payload, true);
  return { id };
});

export const submitBuddyLearnerProfile = onCall(async (request) => {
  const caller = requireRole(request.auth, ["student", "parent"]);
  const payload = requireStudentPayload(caller, request.data);
  validateBuddyLearnerProfilePayload(payload);
  if (!await studentPrivacyPermissionGranted(payload.schoolId, payload.studentId, "buddyAllowed"))
    return { id: "", skipped: true, reason: "parental_consent_required" };
  const id = typeof payload.profileId === "string" ? payload.profileId : "current";
  const safeProfile: StudentPayload = {
    schoolId: payload.schoolId,
    studentId: payload.studentId,
    profileId: id,
    classId: trimmedString(payload.classId, 160),
    homeLanguage: trimmedString(payload.homeLanguage, 16),
    targetLanguage: trimmedString(payload.targetLanguage, 16),
    allowTransliteration: payload.allowTransliteration === true,
    learningMemoryEnabled: payload.learningMemoryEnabled !== false,
    explanationStyle: trimmedString(payload.explanationStyle, 64),
    updatedAtUtc: trimmedString(payload.updatedAtUtc, 64)
  };
  await writeStudentRecord(payload.schoolId, payload.studentId, "buddyLearnerProfiles", id, safeProfile, true);
  return { id };
});

export const getBuddyLearnerContext = onCall(async (request) => {
  const caller = requireRole(request.auth, ["student", "parent"]);
  const payload = requireStudentPayload(caller, request.data);
  const conceptId = typeof payload.conceptId === "string" ? payload.conceptId.trim() : "";
  const requestedLimit = Math.max(1, Math.min(20, finiteInteger(payload.recentAttemptLimit) || 8));
  const basePath = `schools/${payload.schoolId}/students/${payload.studentId}`;
  const attemptQuery = conceptId
    ? db.collection(`${basePath}/buddyLearningAttempts`)
      .where("conceptId", "==", conceptId)
      .orderBy("createdAtUtc", "desc")
      .limit(requestedLimit)
    : db.collection(`${basePath}/buddyLearningAttempts`)
      .orderBy("createdAtUtc", "desc")
      .limit(requestedLimit);
  const [profileSnapshot, stateSnapshot, attemptSnapshot] = await Promise.all([
    db.doc(`${basePath}/buddyLearnerProfiles/current`).get(),
    db.doc(`${basePath}/buddyLearnerState/current`).get(),
    attemptQuery.get()
  ]);
  const recentAttempts = attemptSnapshot.docs
    .map(document => ({ id: document.id, ...document.data() }) as Record<string, unknown> & { id: string })
    .reverse();
  const learnerState = stateSnapshot.exists ? stateSnapshot.data() ?? {} : {};
  const conceptMap = cloneRecord(learnerState.concepts);
  const clientConcepts = Object.entries(conceptMap).map(([conceptId, value]) => {
    const concept = cloneRecord(value);
    const errorCounts = Object.entries(cloneNumberRecord(concept.errors)).map(([errorTag, count]) => ({
      errorTag,
      count,
      lastSeenAtUtc: String(concept.lastPracticedAtUtc ?? "")
    }));
    const modalityStates = Object.values(cloneRecord(concept.modalities)).map(modality => cloneRecord(modality));
    return {
      ...concept,
      conceptId,
      errorCounts,
      modalityStates
    };
  });
  return {
    schemaVersion: 1,
    generatedAtUtc: new Date().toISOString(),
    studentId: payload.studentId,
    profile: profileSnapshot.exists ? profileSnapshot.data() : null,
    learnerState: stateSnapshot.exists ? learnerState : null,
    // Unity's JsonUtility cannot deserialize Firestore's concept-id map. Keep
    // the canonical map above and expose an additive array view for clients.
    clientLearnerState: stateSnapshot.exists ? {
      ...learnerState,
      concepts: clientConcepts,
      recentAttempts,
      updatedAtUtc: String(learnerState.lastEventAtUtc ?? "")
    } : null,
    relevantRecentAttempts: recentAttempts
  };
});

/**
 * Produces constrained, curriculum-grounded Buddy help. The client deliberately
 * never supplies an answer key; the seeded dialogue task is the source of truth.
 */
