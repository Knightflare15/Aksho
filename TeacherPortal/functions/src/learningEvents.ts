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
import { auth } from "./runtime.js";
import { requireRole, requireStudentPayload } from "./authorizationHelpers.js";
import { validateGymAttemptPayload } from "./eventValidation.js";
import { validateAcceptedHandwritingTemplatePayload, withHandwritingRetention } from "./handwritingValidation.js";
import { studentPrivacyPermissionGranted } from "./privacyHelpers.js";
import { countingTargetText } from "./pronunciationProcessingHelpers.js";
import { randomId } from "./sharedUtils.js";
import { gymAttemptTargetText, maybeCreateAnalysisJob, writeStudentRecord } from "./studentRecordHelpers.js";

export const submitGymAttempt = onCall(async (request) => {
  const caller = requireRole(request.auth, ["student", "parent"]);
  const payload = requireStudentPayload(caller, request.data);
  validateGymAttemptPayload(payload);
  const id = typeof payload.attemptId === "string" ? payload.attemptId : randomId();
  await writeStudentRecord(payload.schoolId, payload.studentId, "gymAttempts", id, payload);
  await maybeCreateAnalysisJob(payload, "pronunciation", "gymAttempts", id, gymAttemptTargetText(payload));
  return { id };
});

export const submitAcceptedTemplate = onCall(async (request) => {
  const caller = requireRole(request.auth, ["student", "parent"]);
  const payload = requireStudentPayload(caller, request.data);
  if (!await studentPrivacyPermissionGranted(payload.schoolId, payload.studentId, "buddyAllowed")) {
    return {
      schemaVersion: 1,
      generatedAtUtc: new Date().toISOString(),
      studentId: payload.studentId,
      profile: null,
      learnerState: null,
      clientLearnerState: null,
      recentAttempts: [],
      privacyReason: "parental_consent_required"
    };
  }
  validateAcceptedHandwritingTemplatePayload(payload);
  const id = typeof payload.templateId === "string" ? payload.templateId : randomId();
  await writeStudentRecord(
    payload.schoolId,
    payload.studentId,
    "acceptedHandwritingTemplates",
    id,
    withHandwritingRetention(payload));
  await maybeCreateAnalysisJob(payload, "handwriting", "acceptedHandwritingTemplates", id, String(payload.letter ?? ""));
  return { id };
});

export const submitCountingMiniGameAttempt = onCall(async (request) => {
  const caller = requireRole(request.auth, ["student", "parent"]);
  const payload = requireStudentPayload(caller, request.data);
  const id = typeof payload.attemptId === "string" ? payload.attemptId : randomId();
  await writeStudentRecord(payload.schoolId, payload.studentId, "countingMiniGameAttempts", id, payload);
  await maybeCreateAnalysisJob(payload, "pronunciation", "countingMiniGameAttempts", id, countingTargetText(payload.selectedCount));
  return { id };
});

export const submitColorMiniGameAttempt = onCall(async (request) => {
  const caller = requireRole(request.auth, ["student", "parent"]);
  const payload = requireStudentPayload(caller, request.data);
  const id = typeof payload.attemptId === "string" ? payload.attemptId : randomId();
  await writeStudentRecord(payload.schoolId, payload.studentId, "colorMiniGameAttempts", id, payload);
  await maybeCreateAnalysisJob(payload, "pronunciation", "colorMiniGameAttempts", id, String(payload.selectedColor ?? ""));
  return { id };
});

export const submitEmpathyEvent = onCall(async (request) => {
  const caller = requireRole(request.auth, ["student", "parent"]);
  const payload = requireStudentPayload(caller, request.data);
  const id = typeof payload.eventId === "string" ? payload.eventId : randomId();
  await writeStudentRecord(payload.schoolId, payload.studentId, "empathyEvents", id, payload);
  return { id };
});
