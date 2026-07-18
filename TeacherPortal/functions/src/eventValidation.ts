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
import "./runtime.js";
import { assertBoolean, assertString } from "./sharedUtils.js";

export function validatePhraseEvidencePayload(payload: Record<string, unknown>) {
  assertString(payload.areaId, "areaId");
  assertString(payload.zoneKind, "zoneKind");
  assertString(payload.grammarPattern, "grammarPattern");
  assertBoolean(payload.accepted, "accepted");
}

export function validateGrammarBattlePayload(payload: Record<string, unknown>) {
  assertString(payload.areaId, "areaId");
  assertString(payload.zoneKind, "zoneKind");
  assertString(payload.grammarPattern, "grammarPattern");
  assertString(payload.playerPhrase, "playerPhrase");
  assertBoolean(payload.accepted, "accepted");
}

export function validateBuddyConversationPayload(payload: Record<string, unknown>) {
  assertString(payload.areaId, "areaId");
  assertString(payload.zoneKind, "zoneKind");
  assertString(payload.learnerMessage, "learnerMessage");
  assertString(payload.buddyResponse, "buddyResponse");
}

export function validateBuddyLearningAttemptPayload(payload: Record<string, unknown>) {
  assertString(payload.eventId, "eventId");
  assertString(payload.sessionId, "sessionId");
  assertString(payload.activityType, "activityType");
  assertString(payload.modality, "modality");
  assertString(payload.inputSource, "inputSource");
  assertString(payload.contentId, "contentId");
  assertString(payload.conceptId, "conceptId");
  assertBoolean(payload.countsTowardMastery, "countsTowardMastery");
  assertBoolean(payload.correct, "correct");
  assertBoolean(payload.completedIndependently, "completedIndependently");
}

export function validateBuddyLearningSessionPayload(payload: Record<string, unknown>) {
  assertString(payload.sessionId, "sessionId");
  assertString(payload.homeLanguage, "homeLanguage");
  assertString(payload.targetLanguage, "targetLanguage");
}

export function validateBuddyLearnerProfilePayload(payload: Record<string, unknown>) {
  assertString(payload.profileId, "profileId");
  assertString(payload.homeLanguage, "homeLanguage");
  assertString(payload.targetLanguage, "targetLanguage");
  assertBoolean(payload.learningMemoryEnabled, "learningMemoryEnabled");
}

export function validateGymAttemptPayload(payload: Record<string, unknown>) {
  assertString(payload.areaId, "areaId");
  assertString(payload.gymId, "gymId");
  assertString(payload.zoneKind, "zoneKind");
  assertBoolean(payload.passed, "passed");
}
