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
import { BuddyCanonicalZone, BuddyHelpResponse } from "./runtime.js";
import { trimmedString } from "./inputHelpers.js";
import { randomId } from "./sharedUtils.js";
import { writeStudentRecord } from "./studentRecordHelpers.js";

export async function writeBuddyHelpTurn(input: {
  schoolId: string;
  studentId: string;
  sessionId: string;
  callId: string;
  callTurnIndex: number;
  isCallTurn: boolean;
  classId: string;
  areaId: string;
  dialogueTaskId: string;
  conceptId: string;
  grammarPattern: string;
  canonicalZone: BuddyCanonicalZone;
  trigger: string;
  learnerAttempt: string;
  response: BuddyHelpResponse;
  memoryEnabled: boolean;
  usage: Record<string, unknown>;
}) {
  const eventId = randomId();
  await writeStudentRecord(input.schoolId, input.studentId, "buddyConversationTurns", eventId, {
    eventId,
    studentId: input.studentId,
    classId: input.classId,
    schoolId: input.schoolId,
    sessionId: input.sessionId || "buddy-session-unavailable",
    callId: input.callId,
    callTurnIndex: input.callTurnIndex,
    isCallTurn: input.isCallTurn,
    areaId: input.areaId,
    zoneKind: input.canonicalZone,
    learnerMessage: input.learnerAttempt,
    buddyResponse: input.response.learnerText,
    sourceLanguage: input.response.responseLanguage || "en",
    targetLanguage: "en",
    englishRatio: input.canonicalZone === "Town" ? 0.45 : 0.65,
    conversationSkill: input.trigger,
    grammarPattern: input.grammarPattern,
    conceptId: input.conceptId,
    errorCategory: input.response.errorCategory,
    hintLevelShown: input.response.hintLevel,
    phonicsCueKey: input.response.phonicsCueKey,
    phonicsAnchorWord: input.response.phonicsAnchorWord,
    remediationStep: input.response.hintLevel === "micro_lesson" ? "ExampleDrill" : "GuidedRetry",
    buddyAssistMode: input.canonicalZone === "Town" ? "Full" : "Partial",
    safeMemoryTags: input.memoryEnabled ? input.response.safeMemoryTags : [],
    relationshipMemoryCandidates: input.memoryEnabled ? input.response.relationshipMemoryCandidates : [],
    safetyFlags: input.response.safetyFlags,
    openGrimoire: input.response.openGrimoire,
    grimoireConceptId: input.response.grimoireConceptId,
    grimoireHighlightKey: input.response.grimoireHighlightKey,
    callDisposition: input.response.callDisposition,
    teacherNote: input.response.teacherNote,
    buddyContractId: input.canonicalZone === "Town" ? "response_coach" : "adaptive_hint",
    promptTemplateId: "provider_buddy_help_v1",
    policyVersion: "buddy_policy_v2",
    reportable: true,
    responseSeconds: input.response.latencyMs / 1000,
    provider: input.response.provider,
    model: input.response.model,
    trigger: input.trigger,
    buddyStatus: input.response.status,
    buddyFallbackReason: input.response.fallbackReason,
    routerIntent: input.response.routerIntent ?? "",
    routerAction: input.response.routerAction ?? "",
    routerReason: input.response.routerReason ?? "",
    buddyTier: input.response.tier ?? "",
    modelUsage: input.usage,
    createdAtUtc: new Date().toISOString()
  });

  // This is a support/diagnostic event, not a second answer attempt. It lets the
  // learner-state aggregator retain recurring Buddy diagnoses without inflating
  // mastery attempts that were already recorded by the dialogue interaction.
  const diagnosticEventId = `buddy-help-${eventId}`;
  await writeStudentRecord(input.schoolId, input.studentId, "buddyLearningAttempts", diagnosticEventId, {
    eventId: diagnosticEventId,
    sourceRecordType: "buddy_conversation",
    sourceRecordId: eventId,
    sessionId: input.sessionId || "buddy-session-unavailable",
    studentId: input.studentId,
    classId: input.classId,
    schoolId: input.schoolId,
    areaId: input.areaId,
    zoneKind: input.canonicalZone,
    activityType: "buddy_learning_conversation",
    modality: "Conversation",
    inputSource: "buddy_dialogue_support",
    contentId: input.dialogueTaskId,
    dialogueTaskId: input.dialogueTaskId,
    questionPrompt: "",
    submittedResponse: input.learnerAttempt,
    grammarPattern: input.grammarPattern,
    conceptId: input.conceptId || "Unscoped",
    errorTags: input.response.errorCategory ? [input.response.errorCategory] : [],
    countsTowardMastery: false,
    correct: false,
    completedIndependently: false,
    hintCount: 1,
    highestHintLevel: input.response.hintLevel,
    remediationStep: input.response.hintLevel === "micro_lesson" ? "ExampleDrill" : "GuidedRetry",
    buddyAssistMode: input.canonicalZone === "Town" ? "Full" : "Partial",
    routerIntent: input.response.routerIntent ?? "",
    routerAction: input.response.routerAction ?? "",
    routerReason: input.response.routerReason ?? "",
    buddyTier: input.response.tier ?? "",
    responseSeconds: input.response.latencyMs / 1000,
    createdAtUtc: new Date().toISOString()
  });
}

export function uniqueBuddyStrings(values: unknown[], maximum: number) {
  const result: string[] = [];
  for (const value of values) {
    const normalized = trimmedString(value, 64).toLowerCase().replace(/[^a-z0-9_:-]/g, "_");
    if (normalized && !result.includes(normalized)) result.push(normalized);
    if (result.length >= maximum) break;
  }
  return result;
}
