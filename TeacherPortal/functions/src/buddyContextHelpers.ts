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
import { BuddyCanonicalZone, ChildSafetyDecision } from "./runtime.js";
import { normalizeBuddyText } from "./buddyGemini.js";
import { uniqueBuddyStrings } from "./buddyResponseHelpers.js";
import { buddyLanguageName, normalizeBuddyLanguage } from "./buddySpeechHelpers.js";
import { trimmedString } from "./inputHelpers.js";
import { cloneRecord, finiteInteger, finiteNumber, normalizedStringArray } from "./sharedUtils.js";

export function buildBuddyModelContext(
  task: Record<string, unknown>,
  zone: BuddyCanonicalZone,
  learnerAttempt: string,
  profile: Record<string, unknown>,
  learnerState: Record<string, unknown>,
  recentAttempts: Array<Record<string, unknown>>,
  recentConversationTurns: Array<Record<string, unknown>>,
  call: {
    sessionId: string;
    callId: string;
    callTurnIndex: number;
    isCallTurn: boolean;
    safeRelationshipMemory: string[];
    grimoireExcerpt: string;
  }
) {
  const conceptId = trimmedString(task.conceptId);
  const concepts = cloneRecord(learnerState.concepts);
  const conceptState = cloneRecord(concepts[conceptId]);
  const allowAnswerModel = zone === "Town";
  const homeLanguage = normalizeBuddyLanguage(profile.homeLanguage, "hi");
  const targetLanguage = normalizeBuddyLanguage(profile.targetLanguage, "en");
  const recordedEnglishRatio = finiteNumber(conceptState.recommendedEnglishRatio) || finiteNumber(learnerState.recommendedEnglishRatio) || 0.3;
  const englishFirst = homeLanguage === "en";
  return {
    policy: {
      zone,
      allowAnswerModel,
      learnerAge: "primary-school",
      maximumSentences: 3,
      homeLanguage,
      homeLanguageName: buddyLanguageName(homeLanguage),
      targetLanguage,
      targetLanguageName: buddyLanguageName(targetLanguage),
      allowTransliteration: profile.allowTransliteration === true,
      englishRatio: englishFirst ? 1 : Math.min(recordedEnglishRatio, 0.55),
      homeLanguageFirst: !englishFirst,
      responseLanguageMode: englishFirst ? "simple_english" : "home_language_first_natural_codemix",
      explanationStyle: trimmedString(profile.explanationStyle, 48) || "short_then_expand"
    },
    task: {
      conceptId,
      conceptTitle: trimmedString(task.conceptTitle, 100),
      prompt: redactBuddyText(task.npcLine ?? task.sourceText, 400),
      grammarPattern: trimmedString(task.grammarPattern, 64),
      scaffoldMode: trimmedString(task.scaffoldMode, 64),
      deterministicErrorType: trimmedString(task.malfunctionType, 64),
      expectedResponse: allowAnswerModel ? redactBuddyText(task.expectedResponse, 160) : ""
    },
    learnerAttempt,
    conversation: {
      isCallTurn: call.isCallTurn,
      sessionId: call.sessionId,
      callId: call.callId,
      turnIndex: call.callTurnIndex,
      recentTurns: recentConversationTurns,
      safeRelationshipMemory: call.safeRelationshipMemory
    },
    grimoireReference: {
      conceptId,
      excerpt: call.grimoireExcerpt
    },
    learnerSummary: {
      supportBand: trimmedString(learnerState.supportBand, 32) || "Foundation",
      recurringErrorTags: normalizedStringArray(learnerState.recurringErrorTags, 6),
      conceptMastery: finiteNumber(conceptState.masteryEstimate),
      conceptHintDependency: finiteNumber(conceptState.hintDependency),
      recentAttemptPatterns: recentAttempts
    }
  };
}

export function summarizeBuddyConversationTurnForModel(turn: Record<string, unknown>) {
  return {
    callId: trimmedString(turn.callId, 120),
    turnIndex: Math.max(0, finiteInteger(turn.callTurnIndex)),
    learner: redactBuddyText(turn.learnerMessage, 240),
    buddy: redactBuddyText(turn.buddyResponse, 520),
    hintLevel: trimmedString(turn.hintLevelShown, 32),
    errorCategory: trimmedString(turn.errorCategory, 64),
    conceptId: trimmedString(turn.conceptId, 96),
    dialogueTaskId: trimmedString(turn.dialogueTaskId, 120)
  };
}

export function summarizeBuddyAttemptForModel(attempt: Record<string, unknown>) {
  return {
    modality: trimmedString(attempt.modality, 32),
    correct: attempt.correct === true,
    completedIndependently: attempt.completedIndependently === true,
    hintCount: Math.max(0, finiteInteger(attempt.hintCount)),
    errorTags: normalizedStringArray(attempt.errorTags, 4),
    responseTimeBand: responseTimeBand(finiteNumber(attempt.responseSeconds))
  };
}

export function responseTimeBand(seconds: number) {
  if (seconds <= 0) return "unknown";
  if (seconds < 4) return "quick";
  if (seconds < 12) return "steady";
  return "slow";
}

export function redactBuddyText(value: unknown, maximum: number) {
  let text = trimmedString(value, maximum);
  text = text.replace(/[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}/gi, "[email]");
  text = text.replace(/(?:\+?\d[\d\s().-]{7,}\d)/g, "[phone]");
  text = text.replace(/\b(my name is|i am called)\s+[a-z][a-z'-]*/gi, "$1 [learner]");
  return text;
}

export function screenChildSafetyText(value: unknown, source: "learner" | "buddy"): ChildSafetyDecision {
  const text = normalizeBuddyText(String(value ?? ""));
  const flags: string[] = [];
  const has = (pattern: RegExp) => pattern.test(text);
  if (has(/\b(email|e-mail|phone|mobile|whatsapp|instagram|snapchat|address|home address|school name|my school is|i study at|mera school|mere school|mera address|mera number)\b/)) flags.push("personal_information");
  if (has(/\b(meet me|meet alone|private chat|send (me )?(a )?photo|send pics|keep (this )?secret|dont tell (your )?(parent|mother|father|teacher)|ghar pe mil|akele mil)\b/)) flags.push("grooming_or_secrecy");
  if (has(/\b(nude|naked photo|sex|sexual|porn)\b/)) flags.push("sexual_content");
  if (has(/\b(kill myself|hurt myself|suicide|self harm|self-harm|marna chahta|marna chahti|khud ko mar)\b/)) flags.push("self_harm_or_crisis");
  if (has(/\b(abuse me|hurting me|touching me|unsafe at home|immediate danger|mujhe maar|mujhe chhu)\b/)) flags.push("abuse_or_immediate_danger");
  if (has(/\b(how (do|can) (i|you) (make|build|use).*(bomb|gun|weapon)|make a bomb|buy a gun|hide a weapon)\b/)) flags.push("dangerous_instructions");
  const uniqueFlags = uniqueBuddyStrings(flags, 8);
  if (uniqueFlags.length === 0) return { blocked: false, flags: [], learnerMessage: "" };
  if (source === "buddy") {
    return {
      blocked: true,
      flags: uniqueBuddyStrings(["unsafe_model_output", ...uniqueFlags], 8),
      learnerMessage: ""
    };
  }
  const crisis = uniqueFlags.includes("self_harm_or_crisis") || uniqueFlags.includes("abuse_or_immediate_danger");
  return {
    blocked: true,
    flags: uniqueFlags,
    learnerMessage: crisis
      ? "Please stop the game and tell a trusted adult near you right now. If you or someone else is in immediate danger, ask that adult to contact local emergency services."
      : "I cannot help with personal details, secrets, private meetings, or unsafe topics. Please talk to a parent, teacher, or another trusted adult."
  };
}

export function redactDiagnosticText(value: unknown, maximum: number) {
  let text = redactBuddyText(value, maximum);
  text = text.replace(/(?:[A-Za-z]:\\|\/Users\/|\/home\/)[^\s\n]+/g, "[local-path]");
  text = text.replace(/\b(?:eyJ|AIza|sk-)[A-Za-z0-9_.-]{12,}\b/g, "[secret]");
  return text;
}

export function redactKnownStudentText(value: string, studentName: string) {
  if (studentName.length < 2) return value;
  const escaped = studentName.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  return value.replace(new RegExp(escaped, "gi"), "[learner]");
}

export function safeDiagnosticToken(value: unknown, maximum: number) {
  return trimmedString(value, maximum).replace(/[^a-zA-Z0-9_.:/-]/g, "_");
}
