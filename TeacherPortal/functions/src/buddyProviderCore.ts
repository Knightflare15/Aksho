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
import { GeminiBuddyResult, buddyInputUsdPerMillionTokens, buddyLlmProvider, buddyMaxOutputTokens, buddyOutputUsdPerMillionTokens, geminiBuddyModel, geminiInputUsdPerMillionTokens, geminiOutputUsdPerMillionTokens, normalizedProviderName, sarvamBuddyModel } from "./runtime.js";
import { buddyUsageTokens } from "./buddyBudgetHelpers.js";
import { generateGeminiBuddyHelp } from "./buddyGemini.js";
import { generateSarvamBuddyHelp } from "./buddySarvam.js";
import { buddyLanguageNames } from "./buddySpeechHelpers.js";
import { readGeminiApiKey, readSarvamApiKey } from "./buddyVoiceHelpers.js";

export async function generateBuddyProviderHelp(context: Record<string, unknown>, maxOutputTokens = buddyMaxOutputTokens): Promise<GeminiBuddyResult> {
  const geminiKey = readGeminiApiKey();
  if (buddyLlmProvider === "gemini") {
    if (!geminiKey)
      return { usage: {}, safetyFlags: [], reason: "provider_unavailable", provider: "gemini", model: geminiBuddyModel };
    return { ...(await generateGeminiBuddyHelp(geminiKey, context, maxOutputTokens)), provider: "gemini", model: geminiBuddyModel };
  }

  const sarvamKey = readSarvamApiKey();
  const primary = sarvamKey
    ? await generateSarvamBuddyHelp(sarvamKey, context, maxOutputTokens)
    : { usage: {}, safetyFlags: [], reason: "provider_unavailable" } as GeminiBuddyResult;
  if (primary.output)
    return { ...primary, provider: "sarvam", model: sarvamBuddyModel };
  if (!geminiKey)
    return { ...primary, provider: "sarvam", model: sarvamBuddyModel };

  console.warn("[Buddy] falling back from Sarvam to Gemini", { reason: primary.reason ?? "unknown" });
  const fallback = await generateGeminiBuddyHelp(geminiKey, context, maxOutputTokens);
  return {
    ...fallback,
    usage: mergeBuddyUsage(primary.usage, fallback.usage),
    provider: "gemini",
    model: geminiBuddyModel,
    primaryFailureReason: primary.reason ?? "provider_error"
  };
}

export function mergeBuddyUsage(first: Record<string, unknown>, second: Record<string, unknown>) {
  const a = buddyUsageTokens(first);
  const b = buddyUsageTokens(second);
  return {
    promptTokenCount: a.input + b.input,
    candidatesTokenCount: a.output + b.output,
    totalTokenCount: a.total + b.total,
    includesFallbackProviderUsage: true
  };
}

export function buddyProviderTokenPrices(provider: string) {
  return normalizedProviderName(provider) === "gemini"
    ? { input: geminiInputUsdPerMillionTokens, output: geminiOutputUsdPerMillionTokens }
    : { input: buddyInputUsdPerMillionTokens, output: buddyOutputUsdPerMillionTokens };
}

export function buddyJsonSchemaInstruction() {
  return [
    "Return exactly one JSON object and no markdown.",
    "Required keys: learnerText, speechText, speechSegments, responseLanguage, phonicsCueKey, phonicsAnchorWord, hintLevel, errorCategory, teacherNote, safeMemoryTags, relationshipMemoryCandidates, openGrimoire, grimoireConceptId, grimoireHighlightKey, callDisposition, safetyFlags.",
    "learnerText and speechText are Buddy's coaching response to the learner; never copy learnerAttempt into either field as if it were Buddy's reply.",
    "speechSegments must be an ordered array of continuous same-language runs with exactly language and text. Use lowercase base language codes from the schema, keep the first segment in responseLanguage, and make speechText equal the segment texts joined with single spaces.",
    "Do not put SSML, HTML, markdown, language tags, IPA, or slash notation in any speech segment.",
    "responseLanguage must be the lowercase base language code actually used for the response.",
    "Never put IPA, slash notation, or a bare letter-name approximation in speechText. For phonics, describe the sound with a familiar anchor word and use phonicsCueKey plus phonicsAnchorWord.",
    "hintLevel must be one of: translation, rule_hint, clue, micro_lesson.",
    "callDisposition must be continue or end.",
    "safeMemoryTags, relationshipMemoryCandidates, and safetyFlags must be arrays of strings.",
    "openGrimoire must be a boolean."
  ].join(" ");
}

export function buddySarvamResponseSchema() {
  const speechLanguageCodes = Object.keys(buddyLanguageNames);
  return {
    type: "object",
    additionalProperties: false,
    properties: {
      learnerText: { type: "string" },
      speechText: { type: "string" },
      speechSegments: {
        type: "array",
        minItems: 1,
        maxItems: 8,
        items: {
          type: "object",
          additionalProperties: false,
          properties: {
            language: { type: "string", enum: speechLanguageCodes },
            text: { type: "string" }
          },
          required: ["language", "text"]
        }
      },
      responseLanguage: { type: "string" },
      phonicsCueKey: { type: "string", enum: ["", "short_a", "short_e", "short_i", "short_o", "short_u", "long_a", "long_e", "long_i", "long_o", "long_u", "sound_b", "sound_d", "sound_f", "sound_g", "sound_h", "sound_j", "sound_k", "sound_l", "sound_m", "sound_n", "sound_p", "sound_r", "sound_s", "sound_t", "sound_v", "sound_w", "sound_y", "sound_z", "sound_ch", "sound_sh", "sound_th"] },
      phonicsAnchorWord: { type: "string" },
      hintLevel: { type: "string", enum: ["translation", "rule_hint", "clue", "micro_lesson"] },
      errorCategory: { type: "string" },
      teacherNote: { type: "string" },
      safeMemoryTags: { type: "array", items: { type: "string" } },
      relationshipMemoryCandidates: { type: "array", items: { type: "string" } },
      openGrimoire: { type: "boolean" },
      grimoireConceptId: { type: "string" },
      grimoireHighlightKey: { type: "string" },
      callDisposition: { type: "string", enum: ["continue", "end"] },
      safetyFlags: { type: "array", items: { type: "string" } }
    },
    required: ["learnerText", "speechText", "speechSegments", "responseLanguage", "phonicsCueKey", "phonicsAnchorWord", "hintLevel", "errorCategory", "teacherNote", "safeMemoryTags", "relationshipMemoryCandidates", "openGrimoire", "grimoireConceptId", "grimoireHighlightKey", "callDisposition", "safetyFlags"]
  };
}
