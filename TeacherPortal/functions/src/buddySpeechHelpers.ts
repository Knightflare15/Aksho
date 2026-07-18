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
import { BuddySpeechSegment } from "./runtime.js";
import { redactBuddyText } from "./buddyContextHelpers.js";
import { trimmedString } from "./inputHelpers.js";

export function normalizeSarvamLanguageCode(value: unknown) {
  const code = trimmedString(value, 24);
  return code || "unknown";
}

export const buddyLanguageNames: Record<string, string> = {
  as: "Assamese", bn: "Bengali", brx: "Bodo", doi: "Dogri", en: "English",
  gu: "Gujarati", hi: "Hindi", kn: "Kannada", kok: "Konkani", ks: "Kashmiri",
  mai: "Maithili", ml: "Malayalam", mni: "Manipuri", mr: "Marathi", ne: "Nepali",
  od: "Odia", pa: "Punjabi", sa: "Sanskrit", sat: "Santali", sd: "Sindhi",
  ta: "Tamil", te: "Telugu", ur: "Urdu"
};

export const maximumBuddySpeechSegments = 8;

export const maximumBuddySpeechCharacters = 520;

export function normalizeBuddySpeechSegments(
  value: unknown,
  fallbackTextValue: unknown,
  fallbackLanguageValue: unknown
): { valid: boolean; segments: BuddySpeechSegment[]; speechText: string } {
  const fallbackLanguage = normalizeBuddyLanguage(fallbackLanguageValue, "en");
  const fallbackText = redactBuddyText(fallbackTextValue, maximumBuddySpeechCharacters);

  // During rollout, tolerate an older provider response that has only speechText.
  if (value === undefined || value === null) {
    return fallbackText
      ? { valid: true, segments: [{ language: fallbackLanguage, text: fallbackText }], speechText: fallbackText }
      : { valid: false, segments: [], speechText: "" };
  }
  if (!Array.isArray(value) || value.length === 0 || value.length > maximumBuddySpeechSegments) {
    return { valid: false, segments: [], speechText: "" };
  }

  const segments: BuddySpeechSegment[] = [];
  for (const candidate of value) {
    if (!candidate || typeof candidate !== "object" || Array.isArray(candidate)) {
      return { valid: false, segments: [], speechText: "" };
    }
    const record = candidate as Record<string, unknown>;
    const rawLanguage = trimmedString(record.language, 24).toLowerCase().replace("_", "-").split("-")[0];
    const normalizedLanguage = rawLanguage === "or" ? "od" : rawLanguage;
    if (!buddyLanguageNames[normalizedLanguage]) {
      return { valid: false, segments: [], speechText: "" };
    }
    const rawText = typeof record.text === "string" ? record.text.trim() : "";
    if (!rawText || rawText.length > 240) {
      return { valid: false, segments: [], speechText: "" };
    }
    const text = redactBuddyText(rawText, 240);
    if (!text || containsUnsupportedBuddySpeechMarkup(text)) {
      return { valid: false, segments: [], speechText: "" };
    }
    segments.push({ language: normalizedLanguage, text });
  }

  const speechText = segments.map(segment => segment.text).join(" ").trim();
  if (!speechText || speechText.length > maximumBuddySpeechCharacters) {
    return { valid: false, segments: [], speechText: "" };
  }
  return { valid: true, segments, speechText };
}

export function containsUnsupportedBuddySpeechMarkup(value: string) {
  const markup = /<[^>]+>|```|\*\*|__|~~|\[[^\]]+\]\([^)]+\)/u;
  const slashNotation = /\/[a-z\u0250-\u02af\u1d00-\u1d7f\u02c8\u02cc\u02d0.\s]{1,18}\//iu;
  return markup.test(value) || slashNotation.test(value);
}

export function normalizeBuddyLanguage(value: unknown, fallback: string) {
  const raw = trimmedString(value, 24).toLowerCase().replace("_", "-");
  const base = raw.split("-")[0];
  const normalized = base === "or" ? "od" : base;
  return buddyLanguageNames[normalized] ? normalized : fallback;
}

export function buddyLanguageName(code: string) {
  return buddyLanguageNames[code] ?? buddyLanguageNames.en;
}

export const buddyPhonicsAnchors: Record<string, string> = {
  short_a: "apple", short_e: "egg", short_i: "ink", short_o: "hot", short_u: "cup",
  long_a: "ape", long_e: "ear", long_i: "ice", long_o: "boat", long_u: "use",
  sound_b: "bat", sound_d: "dog", sound_f: "fish", sound_g: "goat", sound_h: "hat",
  sound_j: "jam", sound_k: "cat", sound_l: "lion", sound_m: "man", sound_n: "nest",
  sound_p: "pig", sound_r: "rat", sound_s: "sun", sound_t: "top", sound_v: "van",
  sound_w: "wig", sound_y: "yak", sound_z: "zip", sound_ch: "chair", sound_sh: "ship",
  sound_th: "thank"
};

export function normalizeBuddyPhonicsCue(keyValue: unknown, anchorValue: unknown) {
  const key = trimmedString(keyValue, 32).toLowerCase();
  if (!buddyPhonicsAnchors[key]) return { key: "", anchorWord: "" };
  const requestedAnchor = trimmedString(anchorValue, 32).toLowerCase().replace(/[^a-z'-]/g, "");
  return { key, anchorWord: requestedAnchor || buddyPhonicsAnchors[key] };
}
