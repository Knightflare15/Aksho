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
import { redactBuddyText } from "./buddyContextHelpers.js";
import { validateBuddyConversationPayload, validateGrammarBattlePayload, validatePhraseEvidencePayload } from "./eventValidation.js";
import { validateLetterAttemptPayload, withHandwritingRetention } from "./handwritingValidation.js";
import { studentPrivacyPermissionGranted } from "./privacyHelpers.js";
import { randomId } from "./sharedUtils.js";
import { grammarBattleTargetText, maybeCreateAnalysisJob, spokenPhraseTargetText, upsertParentSummary, writeStudentRecord } from "./studentRecordHelpers.js";

export const submitRunSession = onCall(async (request) => {
  const caller = requireRole(request.auth, ["student", "parent"]);
  const payload = requireStudentPayload(caller, request.data);
  const id = typeof payload.sessionId === "string" ? payload.sessionId : randomId();
  await writeStudentRecord(payload.schoolId, payload.studentId, "runSessions", id, payload);
  await upsertParentSummary(payload, id);
  return { id };
});

export const submitLetterAttempt = onCall(async (request) => {
  const caller = requireRole(request.auth, ["student", "parent"]);
  const payload = requireStudentPayload(caller, request.data);
  validateLetterAttemptPayload(payload);
  const id = typeof payload.attemptId === "string" ? payload.attemptId : randomId();
  await writeStudentRecord(
    payload.schoolId,
    payload.studentId,
    "letterAttempts",
    id,
    withHandwritingRetention(payload));
  return { id };
});

export const submitWordCast = onCall(async (request) => {
  const caller = requireRole(request.auth, ["student", "parent"]);
  const payload = requireStudentPayload(caller, request.data);
  const id = typeof payload.eventId === "string" ? payload.eventId : randomId();
  await writeStudentRecord(payload.schoolId, payload.studentId, "wordCastEvents", id, payload);
  await maybeCreateAnalysisJob(payload, "pronunciation", "wordCastEvents", id, String(payload.word ?? ""));
  return { id };
});

export const submitSpokenPhraseEvent = onCall(async (request) => {
  const caller = requireRole(request.auth, ["student", "parent"]);
  const payload = requireStudentPayload(caller, request.data);
  validatePhraseEvidencePayload(payload);
  const id = typeof payload.eventId === "string" ? payload.eventId : randomId();
  await writeStudentRecord(payload.schoolId, payload.studentId, "spokenPhraseEvents", id, payload);
  await maybeCreateAnalysisJob(payload, "pronunciation", "spokenPhraseEvents", id, spokenPhraseTargetText(payload));
  return { id };
});

export const submitWrittenPhraseEvent = onCall(async (request) => {
  const caller = requireRole(request.auth, ["student", "parent"]);
  const payload = requireStudentPayload(caller, request.data);
  validatePhraseEvidencePayload(payload);
  const id = typeof payload.eventId === "string" ? payload.eventId : randomId();
  await writeStudentRecord(payload.schoolId, payload.studentId, "writtenPhraseEvents", id, payload);
  return { id };
});

export const submitGrammarBattleEvent = onCall(async (request) => {
  const caller = requireRole(request.auth, ["student", "parent"]);
  const payload = requireStudentPayload(caller, request.data);
  validateGrammarBattlePayload(payload);
  const id = typeof payload.eventId === "string" ? payload.eventId : randomId();
  await writeStudentRecord(payload.schoolId, payload.studentId, "grammarBattleEvents", id, payload);
  await maybeCreateAnalysisJob(payload, "pronunciation", "grammarBattleEvents", id, grammarBattleTargetText(payload));
  return { id };
});

export const submitBuddyConversationTurn = onCall(async (request) => {
  const caller = requireRole(request.auth, ["student", "parent"]);
  const payload = requireStudentPayload(caller, request.data);
  validateBuddyConversationPayload(payload);
  if (!await studentPrivacyPermissionGranted(payload.schoolId, payload.studentId, "buddyAllowed"))
    return { id: "", skipped: true, reason: "parental_consent_required" };
  const id = typeof payload.eventId === "string" ? payload.eventId : randomId();
  await writeStudentRecord(payload.schoolId, payload.studentId, "buddyConversationTurns", id, {
    ...payload,
    learnerMessage: redactBuddyText(payload.learnerMessage, 520),
    buddyResponse: redactBuddyText(payload.buddyResponse, 520),
    teacherNote: redactBuddyText(payload.teacherNote, 240)
  });
  return { id };
});
