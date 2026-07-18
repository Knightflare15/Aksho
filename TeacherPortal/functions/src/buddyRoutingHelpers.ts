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
import { BuddyCanonicalZone, BuddyHelpResponse, BuddyIntent, BuddyRouterDecision, BuddyTier, buddyFreeTierDailyModelCalls, buddyFreeTierDailyTokenLimit, buddyMaxOutputTokens, buddyModelEnabled, buddyPremiumTierDailyModelCalls, buddyPremiumTierDailyTokenLimit, buddyStandardTierDailyModelCalls, buddyStandardTierDailyTokenLimit, buddySttFreeTierDailySeconds, buddySttPremiumTierDailySeconds, buddySttStandardTierDailySeconds, buddyVoiceFreeTierDailySessions, buddyVoicePremiumTierDailySessions, buddyVoiceStandardTierDailySessions } from "./runtime.js";
import { normalizeBuddyText } from "./buddyGemini.js";
import { uniqueBuddyStrings } from "./buddyResponseHelpers.js";
import { trimmedString } from "./inputHelpers.js";

export function buildBuddyRouterDecision(input: {
  zone: BuddyCanonicalZone;
  trigger: string;
  learnerAttempt: string;
  task: Record<string, unknown>;
  tier: BuddyTier;
  learnerState: Record<string, unknown>;
  isCallTurn: boolean;
  dailyModelCallCount: number;
  hasGrimoireExcerpt: boolean;
}): BuddyRouterDecision {
  const tier = input.tier;
  const dailyModelCallLimit = buddyTierModelCallLimit(tier);
  const intent = classifyBuddyIntent(input.trigger, input.learnerAttempt, input.task, input.isCallTurn);
  const maxOutputTokens = buddyTierMaxOutputTokens(tier, intent, input.isCallTurn);
  const blocked = input.zone === "Gym";
  const limitReached = input.dailyModelCallCount >= dailyModelCallLimit;
  const simpleLocal = shouldUseLocalBuddyRoute(intent, input.zone, input.trigger, input.learnerAttempt, input.hasGrimoireExcerpt);
  const safetyFlags = intent === "safety_or_unknown" ? ["router_safety_or_unknown"] : [];

  if (!buddyModelEnabled) {
    return {
      intent,
      action: "local",
      tier,
      reason: "model_kill_switch",
      modelAllowed: false,
      dailyModelCallLimit,
      maxOutputTokens,
      forceOpenGrimoire: input.hasGrimoireExcerpt,
      fallbackMessage: localBuddyRouterMessage(input.zone, intent, input.task),
      safetyFlags: ["model_kill_switch", ...safetyFlags]
    };
  }

  if (blocked) {
    return {
      intent,
      action: "block",
      tier,
      reason: "gym_check_blocked",
      modelAllowed: false,
      dailyModelCallLimit,
      maxOutputTokens,
      forceOpenGrimoire: false,
      fallbackMessage: "Gym checks are completed without Buddy help.",
      safetyFlags: ["gym_blocked", ...safetyFlags]
    };
  }

  if (limitReached) {
    return {
      intent,
      action: "local",
      tier,
      reason: "tier_model_call_limit",
      modelAllowed: false,
      dailyModelCallLimit,
      maxOutputTokens,
      forceOpenGrimoire: input.hasGrimoireExcerpt,
      fallbackMessage: input.zone === "Town"
        ? "Buddy has used today's smart help. Read the Grimoire note and try the sentence slowly."
        : "Buddy has used today's smart hints. Use the Grimoire clue and try one small change.",
      safetyFlags: ["tier_model_call_limit", ...safetyFlags]
    };
  }

  if (simpleLocal) {
    return {
      intent,
      action: "local",
      tier,
      reason: "local_curriculum_hint",
      modelAllowed: false,
      dailyModelCallLimit,
      maxOutputTokens,
      forceOpenGrimoire: input.hasGrimoireExcerpt,
      fallbackMessage: localBuddyRouterMessage(input.zone, intent, input.task),
      safetyFlags
    };
  }

  return {
    intent,
    action: "model",
    tier,
    reason: "needs_personalized_hinglish_response",
    modelAllowed: true,
    dailyModelCallLimit,
    maxOutputTokens,
    forceOpenGrimoire: false,
    fallbackMessage: fallbackBuddyText(input.zone),
    safetyFlags
  };
}

export function normalizeBuddyTier(value: unknown): BuddyTier {
  const normalized = trimmedString(value, 32).toLowerCase();
  if (normalized === "premium" || normalized === "pro" || normalized === "plus") return "premium";
  if (normalized === "standard" || normalized === "paid" || normalized === "school") return "standard";
  return "free";
}

export function resolveServiceTier(student: Record<string, unknown>): BuddyTier {
  return normalizeBuddyTier(student.subscriptionTier ?? student.plan ?? student.buddyTier);
}

export function buddyTierModelCallLimit(tier: BuddyTier) {
  if (tier === "premium") return buddyPremiumTierDailyModelCalls;
  if (tier === "standard") return buddyStandardTierDailyModelCalls;
  return buddyFreeTierDailyModelCalls;
}

export function buddyTierTokenLimit(tier: BuddyTier) {
  if (tier === "premium") return buddyPremiumTierDailyTokenLimit;
  if (tier === "standard") return buddyStandardTierDailyTokenLimit;
  return buddyFreeTierDailyTokenLimit;
}

export function buddyTierSttSecondsLimit(tier: BuddyTier) {
  if (tier === "premium") return buddySttPremiumTierDailySeconds;
  if (tier === "standard") return buddySttStandardTierDailySeconds;
  return buddySttFreeTierDailySeconds;
}

export function buddyTierVoiceSessionLimit(tier: BuddyTier) {
  if (tier === "premium") return buddyVoicePremiumTierDailySessions;
  if (tier === "standard") return buddyVoiceStandardTierDailySessions;
  return buddyVoiceFreeTierDailySessions;
}

export function buddyTierMaxOutputTokens(tier: BuddyTier, intent: BuddyIntent, isCallTurn: boolean) {
  const cap = tier === "premium" ? buddyMaxOutputTokens : tier === "standard" ? Math.min(buddyMaxOutputTokens, 384) : Math.min(buddyMaxOutputTokens, 256);
  if (intent === "social_chat" && !isCallTurn) return Math.min(cap, 180);
  if (intent === "navigation_help") return Math.min(cap, 160);
  return cap;
}

export function classifyBuddyIntent(trigger: string, learnerAttempt: string, task: Record<string, unknown>, isCallTurn: boolean): BuddyIntent {
  if (trigger === "wrong_answer") return "wrong_answer_coach";
  const text = normalizeBuddyText(learnerAttempt);
  const conceptId = normalizeBuddyText(String(task.conceptId ?? ""));
  if (/\b(pronounce|pronunciation|sound|bol|bolo|kaise bol|kaise bolu|kaise bolun)\b/.test(text)) return "pronunciation_help";
  if (/\b(meaning|translate|matlab|hindi|english me|english mein|kaise kah)\b/.test(text)) return "translation_help";
  if (/\b(grammar|rule|why|kyu|kyun|explain|samjhao|samjha)\b/.test(text) || conceptId.length > 0) return "grammar_explanation";
  if (/\b(where|go|next|map|kidhar|kahan|kaun sa|route|gym|town)\b/.test(text)) return "navigation_help";
  if (isCallTurn || /\b(hi|hello|buddy|friend|kaise ho|thanks|thank you)\b/.test(text)) return "social_chat";
  return "safety_or_unknown";
}

export function shouldUseLocalBuddyRoute(intent: BuddyIntent, zone: BuddyCanonicalZone, trigger: string, learnerAttempt: string, hasGrimoireExcerpt: boolean) {
  const wordCount = normalizeBuddyText(learnerAttempt).split(/\s+/).filter(Boolean).length;
  if (intent === "navigation_help") return true;
  if (intent === "pronunciation_help" && hasGrimoireExcerpt) return true;
  if (zone === "Route" && trigger === "wrong_answer" && hasGrimoireExcerpt) return true;
  if (intent === "grammar_explanation" && hasGrimoireExcerpt && wordCount <= 4) return true;
  return false;
}

export function localBuddyRouterMessage(zone: BuddyCanonicalZone, intent: BuddyIntent, task: Record<string, unknown>) {
  const conceptTitle = trimmedString(task.conceptTitle, 80) || trimmedString(task.conceptId, 80) || "this grammar idea";
  if (intent === "navigation_help") {
    return "Check the glowing path and the current objective. Town teaches, Route practices, and Gym checks without Buddy answers.";
  }
  if (intent === "pronunciation_help") {
    return `Open the Grimoire pronunciation guide for ${conceptTitle}. Say it slowly once, then try the game line again.`;
  }
  if (zone === "Route") {
    return `Look at the ${conceptTitle} clue. Change one small grammar part, then try again.`;
  }
  return `Open the Grimoire page for ${conceptTitle}. Read the rule, then say the sentence slowly.`;
}

export function attachBuddyRouter(response: BuddyHelpResponse, router: BuddyRouterDecision): BuddyHelpResponse {
  response.routerIntent = router.intent;
  response.routerAction = router.action;
  response.routerReason = router.reason;
  response.tier = router.tier;
  response.safetyFlags = uniqueBuddyStrings([...response.safetyFlags, ...router.safetyFlags], 10);
  if (router.forceOpenGrimoire && response.status !== "blocked") {
    response.openGrimoire = true;
    if (!response.grimoireHighlightKey)
      response.grimoireHighlightKey = "rule";
  }
  return response;
}

export function buddyBlockedResponse(message: string): BuddyHelpResponse {
  return {
    status: "blocked",
    fallbackReason: "gym_blocked",
    learnerText: message,
    speechText: message,
    speechSegments: [{ language: "en", text: message }],
    responseLanguage: "en",
    phonicsCueKey: "",
    phonicsAnchorWord: "",
    hintLevel: "none",
    errorCategory: "",
    teacherNote: "Buddy was correctly blocked for a Gym check.",
    safeMemoryTags: [],
    relationshipMemoryCandidates: [],
    safetyFlags: ["gym_blocked"],
    openGrimoire: false,
    grimoireConceptId: "",
    grimoireHighlightKey: "",
    callDisposition: "end",
    callId: "",
    callTurnIndex: 0,
    provider: "",
    model: "",
    latencyMs: 0
  };
}

export function buddyFallbackResponse(zone: BuddyCanonicalZone, reason: string, message: string): BuddyHelpResponse {
  return {
    status: "fallback",
    fallbackReason: reason,
    learnerText: message,
    speechText: message,
    speechSegments: [{ language: "en", text: message }],
    responseLanguage: "en",
    phonicsCueKey: "",
    phonicsAnchorWord: "",
    hintLevel: zone === "Town" ? "rule_hint" : "clue",
    errorCategory: "",
    teacherNote: `Buddy used deterministic fallback: ${reason}.`,
    safeMemoryTags: [],
    relationshipMemoryCandidates: [],
    safetyFlags: reason === "provider_unavailable" || reason === "provider_error" ? [reason] : [],
    openGrimoire: false,
    grimoireConceptId: "",
    grimoireHighlightKey: "",
    callDisposition: "continue",
    callId: "",
    callTurnIndex: 0,
    provider: "deterministic_fallback",
    model: "",
    latencyMs: 0
  };
}

export function fallbackBuddyText(zone: BuddyCanonicalZone) {
  return zone === "Town"
    ? "Buddy is offline for a moment. Read the teaching note, then try the English sentence slowly."
    : "Buddy is offline for a moment. Look at the grammar clue and change one small part before trying again.";
}
