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

initializeApp();

export const db = getFirestore();
export const auth = getAuth();
export const storage = getStorage();
export const azureSpeechKey = defineSecret("AZURE_SPEECH_KEY");
export const azureSpeechRegion = process.env.AZURE_SPEECH_REGION ?? "";
export const azureSpeechLanguage = process.env.AZURE_SPEECH_LANGUAGE ?? "en-US";
export const geminiApiKey = defineSecret("GEMINI_API_KEY");
export const sarvamApiKey = defineSecret("SARVAM_API_KEY");
export const livekitApiKey = defineSecret("LIVEKIT_API_KEY");
export const livekitApiSecret = defineSecret("LIVEKIT_API_SECRET");
export const accessCodePepper = defineSecret("ACCESS_CODE_PEPPER");
export const livekitUrl = process.env.LIVEKIT_URL?.trim() ?? "";
export const buddyVoiceAgentName = process.env.LIVEKIT_AGENT_NAME?.trim() || "buddy-voice";
export const buddyVoiceFunctionRegion = process.env.BUDDY_VOICE_FUNCTION_REGION?.trim() || "asia-south1";
export const pronunciationFunctionRegion = process.env.PRONUNCIATION_FUNCTION_REGION?.trim() || "asia-south1";
export const buddyLlmProvider = normalizedProviderName(process.env.BUDDY_LLM_PROVIDER ?? "sarvam");
export const geminiBuddyModel = process.env.GEMINI_BUDDY_MODEL ?? "gemini-3.1-flash-lite";
export const teacherAssistantModel = process.env.TEACHER_ASSISTANT_MODEL ?? "gemini-3.1-flash-lite";
export const teacherAssistantEnabled = environmentBoolean("TEACHER_ASSISTANT_ENABLED", true);
export const teacherAssistantMaxOutputTokens = boundedEnvironmentInteger("TEACHER_ASSISTANT_MAX_OUTPUT_TOKENS", 900, 256, 2048);
export const teacherAssistantVerifierEnabled = environmentBoolean("TEACHER_ASSISTANT_VERIFIER_ENABLED", true);
export const teacherSemanticIndexEnabled = environmentBoolean("TEACHER_SEMANTIC_INDEX_ENABLED", false);
export const teacherSemanticEmbeddingModel = process.env.TEACHER_SEMANTIC_EMBEDDING_MODEL ?? "gemini-embedding-001";
export const teacherSemanticEmbeddingDimensions = boundedEnvironmentInteger("TEACHER_SEMANTIC_EMBEDDING_DIMENSIONS", 768, 128, 2048);
export const sarvamBuddyModel = process.env.SARVAM_BUDDY_MODEL ?? "sarvam-105b";
export const sarvamSttModel = process.env.SARVAM_STT_MODEL ?? "saaras:v3";
export const sarvamSttMode = process.env.SARVAM_STT_MODE ?? "codemix";
export const buddyModel = buddyLlmProvider === "gemini" ? geminiBuddyModel : sarvamBuddyModel;
export const buddyModelEnabled = environmentBoolean("BUDDY_MODEL_ENABLED", true);
export const buddyAllowLegacyCallable = environmentBoolean("BUDDY_ALLOW_LEGACY_CALLABLE", true);
export const azurePronunciationEnabled = environmentBoolean("AZURE_PRONUNCIATION_ENABLED", true);
export const buddyRequestCooldownMs = 3500;
export const buddyRequestLeaseMs = 45000;
export const buddyAbsoluteDailyRequestLimit = boundedEnvironmentInteger("BUDDY_ABSOLUTE_DAILY_REQUEST_LIMIT", 200, 1, 2000);
export const buddyReservedTokensPerRequest = boundedEnvironmentInteger("BUDDY_RESERVED_TOKENS_PER_REQUEST", 700, 100, 4000);
export const buddyMaxOutputTokens = boundedEnvironmentInteger("BUDDY_MAX_OUTPUT_TOKENS", 512, 128, 1024);
export const buddyFreeTierDailyModelCalls = boundedEnvironmentInteger("BUDDY_FREE_TIER_DAILY_MODEL_CALLS", 8, 0, 200);
export const buddyStandardTierDailyModelCalls = boundedEnvironmentInteger("BUDDY_STANDARD_TIER_DAILY_MODEL_CALLS", 40, 1, 500);
export const buddyPremiumTierDailyModelCalls = boundedEnvironmentInteger("BUDDY_PREMIUM_TIER_DAILY_MODEL_CALLS", 120, 1, 1000);
export const buddyFreeTierDailyTokenLimit = boundedEnvironmentInteger("BUDDY_FREE_TIER_DAILY_TOKEN_LIMIT", 6000, 500, 200000);
export const buddyStandardTierDailyTokenLimit = boundedEnvironmentInteger("BUDDY_STANDARD_TIER_DAILY_TOKEN_LIMIT", 24000, 1000, 1000000);
export const buddyPremiumTierDailyTokenLimit = boundedEnvironmentInteger("BUDDY_PREMIUM_TIER_DAILY_TOKEN_LIMIT", 80000, 1000, 2000000);
export const buddySttFreeTierDailySeconds = boundedEnvironmentInteger("BUDDY_STT_FREE_DAILY_SECONDS", 90, 0, 1800);
export const buddySttStandardTierDailySeconds = boundedEnvironmentInteger("BUDDY_STT_STANDARD_DAILY_SECONDS", 360, 30, 3600);
export const buddySttPremiumTierDailySeconds = boundedEnvironmentInteger("BUDDY_STT_PREMIUM_DAILY_SECONDS", 900, 60, 7200);
export const buddySttMaximumTurnSeconds = boundedEnvironmentInteger("BUDDY_STT_MAX_TURN_SECONDS", 20, 3, 60);
export const buddyVoiceMaxSessionSeconds = boundedEnvironmentInteger("BUDDY_VOICE_MAX_SESSION_SECONDS", 600, 60, 1800);
export const buddyVoiceMaxConcurrentPerSchool = boundedEnvironmentInteger("BUDDY_VOICE_MAX_CONCURRENT_PER_SCHOOL", 100, 1, 500);
export const buddyVoiceRoomEmptyTimeoutSeconds = boundedEnvironmentInteger("BUDDY_VOICE_ROOM_EMPTY_TIMEOUT_SECONDS", 45, 10, 300);
export const buddyVoiceRoomDepartureTimeoutSeconds = boundedEnvironmentInteger("BUDDY_VOICE_ROOM_DEPARTURE_TIMEOUT_SECONDS", 20, 5, 120);
export const buddyVoiceTokenFunctionConcurrency = boundedEnvironmentInteger("BUDDY_VOICE_TOKEN_FUNCTION_CONCURRENCY", 40, 1, 80);
export const buddyVoiceTokenFunctionMaxInstances = boundedEnvironmentInteger("BUDDY_VOICE_TOKEN_FUNCTION_MAX_INSTANCES", 10, 1, 100);
export const buddyVoiceEnforceAppCheck = environmentBoolean("BUDDY_VOICE_ENFORCE_APP_CHECK", true);
export const buddyVoiceGroundingEnabled = environmentBoolean("BUDDY_VOICE_GROUNDING_ENABLED", false);
export const buddyVoiceFreeTierDailySessions = boundedEnvironmentInteger("BUDDY_VOICE_FREE_DAILY_SESSIONS", 4, 1, 100);
export const buddyVoiceStandardTierDailySessions = boundedEnvironmentInteger("BUDDY_VOICE_STANDARD_DAILY_SESSIONS", 12, 1, 200);
export const buddyVoicePremiumTierDailySessions = boundedEnvironmentInteger("BUDDY_VOICE_PREMIUM_DAILY_SESSIONS", 30, 1, 500);
export const buddyConversationRetentionDays = boundedEnvironmentInteger("BUDDY_CONVERSATION_RETENTION_DAYS", 30, 7, 365);
export const buddyUsageRetentionDays = boundedEnvironmentInteger("BUDDY_USAGE_RETENTION_DAYS", 400, 30, 1825);
export const pronunciationDailyReviewLimit = boundedEnvironmentInteger("PRONUNCIATION_DAILY_REVIEW_LIMIT", 30, 1, 500);
export const pronunciationDailyAudioSecondsLimit = boundedEnvironmentInteger("PRONUNCIATION_DAILY_AUDIO_SECONDS_LIMIT", 120, 10, 3600);
export const pronunciationFreeTierDailyReviewLimit = boundedEnvironmentInteger("PRONUNCIATION_FREE_TIER_DAILY_REVIEW_LIMIT", 4, 0, 100);
export const pronunciationFreeTierDailyAudioSecondsLimit = boundedEnvironmentInteger("PRONUNCIATION_FREE_TIER_DAILY_AUDIO_SECONDS_LIMIT", 20, 0, 600);
export const pronunciationPremiumTierDailyReviewLimit = boundedEnvironmentInteger("PRONUNCIATION_PREMIUM_TIER_DAILY_REVIEW_LIMIT", 80, 1, 1000);
export const pronunciationPremiumTierDailyAudioSecondsLimit = boundedEnvironmentInteger("PRONUNCIATION_PREMIUM_TIER_DAILY_AUDIO_SECONDS_LIMIT", 360, 10, 7200);
export const pronunciationOrphanAudioMaxAgeHours = boundedEnvironmentInteger("PRONUNCIATION_ORPHAN_AUDIO_MAX_AGE_HOURS", 24, 1, 168);
export const pronunciationFunctionConcurrency = boundedEnvironmentInteger("PRONUNCIATION_FUNCTION_CONCURRENCY", 4, 1, 80);
export const pronunciationFunctionMaxInstances = boundedEnvironmentInteger("PRONUNCIATION_FUNCTION_MAX_INSTANCES", 5, 1, 100);
export const pronunciationRestMaximumAttempts = boundedEnvironmentInteger("PRONUNCIATION_REST_MAX_ATTEMPTS", 3, 1, 5);
export const pronunciationRestTimeoutMs = boundedEnvironmentInteger("PRONUNCIATION_REST_TIMEOUT_MS", 12000, 3000, 30000);
export const pronunciationProcessingLeaseMs = boundedEnvironmentInteger("PRONUNCIATION_PROCESSING_LEASE_MS", 90000, 30000, 300000);
export const handwritingRawStrokeRetentionDays = boundedEnvironmentInteger("HANDWRITING_RAW_STROKE_RETENTION_DAYS", 180, 7, 730);
export const privacyPolicyVersion = process.env.PRIVACY_POLICY_VERSION?.trim() || "open-beta-2026-07";
export const studentDeletionGraceDays = boundedEnvironmentInteger("STUDENT_DELETION_GRACE_DAYS", 7, 1, 30);
export const clientDiagnosticDailyLimit = boundedEnvironmentInteger("CLIENT_DIAGNOSTIC_DAILY_LIMIT", 30, 5, 200);
export const clientDiagnosticRetentionDays = boundedEnvironmentInteger("CLIENT_DIAGNOSTIC_RETENTION_DAYS", 30, 7, 180);
// Prices are deployment configuration, never billing truth. They make usage dashboards
// useful before provider invoices arrive and are stored as integer micro-US-dollars.
export const buddyInputUsdPerMillionTokens = boundedEnvironmentNumber("BUDDY_INPUT_USD_PER_MILLION_TOKENS", 0.042, 0, 100);
export const buddyOutputUsdPerMillionTokens = boundedEnvironmentNumber("BUDDY_OUTPUT_USD_PER_MILLION_TOKENS", 0.168, 0, 100);
export const geminiInputUsdPerMillionTokens = boundedEnvironmentNumber("GEMINI_INPUT_USD_PER_MILLION_TOKENS", 0.25, 0, 100);
export const geminiOutputUsdPerMillionTokens = boundedEnvironmentNumber("GEMINI_OUTPUT_USD_PER_MILLION_TOKENS", 1.50, 0, 100);
export const azurePronunciationUsdPerAudioHour = boundedEnvironmentNumber("AZURE_PRONUNCIATION_USD_PER_AUDIO_HOUR", 1.00, 0, 100);
export const sarvamSttUsdPerAudioHour = boundedEnvironmentNumber("SARVAM_STT_USD_PER_AUDIO_HOUR", 0.36, 0, 100);
export const schoolDailyAiCostLimitMicroUsd = Math.round(
  boundedEnvironmentNumber("SCHOOL_DAILY_AI_COST_LIMIT_USD", 25, 0.10, 10000) * 1_000_000);
export const buddyLatencyAlertMs = boundedEnvironmentInteger("BUDDY_LATENCY_ALERT_MS", 8000, 500, 60000);
export const pronunciationLatencyAlertMs = boundedEnvironmentInteger("PRONUNCIATION_LATENCY_ALERT_MS", 12000, 500, 120000);
export const crashDailyAlertCount = boundedEnvironmentInteger("CRASH_DAILY_ALERT_COUNT", 5, 1, 1000);
export const aiCostWarningPercent = boundedEnvironmentInteger("AI_COST_WARNING_PERCENT", 80, 10, 100);
export const maxStudentPayloadBytes = 128 * 1024;
export const cosmeticShopCatalog: Record<string, { kind: "skin" | "companion"; price: number }> = {
  skin_default: { kind: "skin", price: 0 },
  skin_azure: { kind: "skin", price: 25 },
  skin_rose: { kind: "skin", price: 30 },
  skin_forest: { kind: "skin", price: 35 },
  skin_starlight: { kind: "skin", price: 45 },
  companion_none: { kind: "companion", price: 0 },
  companion_spark: { kind: "companion", price: 40 },
  companion_moon: { kind: "companion", price: 55 },
  companion_pebble: { kind: "companion", price: 65 }
};
export const buddyMemoryTagAllowList = new Set([
  "prefers_short_examples",
  "needs_slow_step_by_step",
  "benefits_from_hindi_support",
  "needs_pronunciation_retry",
  "needs_writing_practice",
  "needs_speaking_practice"
]);
export const buddyRelationshipMemoryAllowList = new Set([
  "interest:pirates",
  "interest:space",
  "interest:dinosaurs",
  "interest:animals",
  "interest:sports",
  "interest:music",
  "interest:drawing",
  "interest:magic",
  "interest:cars",
  "interest:stories",
  "style:playful",
  "style:short",
  "style:examples"
]);

export type Role = "admin" | "teacher" | "parent" | "student";
export type CodeType = "parent" | "student" | "teacherInvite";
export type StudentPayload = Record<string, unknown> & { schoolId: string; studentId: string };
export type StudentAudioPayload = StudentPayload & {
  audioBase64: string;
  mimeType: string;
  fileName: string;
  languageCode: string;
};
export type PhoneticSegmentStatus = "Matched" | "NeedsPractice" | "Missing" | "Unknown";

export interface AnalysisJob {
  jobId?: string;
  studentId?: string;
  classId?: string;
  schoolId?: string;
  missionId?: string;
  analysisKind?: string;
  status?: string;
  sourceCollection?: string;
  sourceRecordId?: string;
  targetText?: string;
  audioStoragePath?: string;
  audioDurationSeconds?: number;
}

export interface BuddyVoiceReservation {
  voiceSessionId: string;
  clientRequestId: string;
  dialogueTaskId: string;
  roomName: string;
  participantIdentity: string;
  expiresAtEpochMs: number;
}

export interface StudentPrivacySettings {
  consentStatus: "pending" | "granted" | "withdrawn";
  policyVersion: string;
  gameplayAnalyticsAllowed: boolean;
  buddyAllowed: boolean;
  audioProcessingAllowed: boolean;
  handwritingEvidenceAllowed: boolean;
  diagnosticsAllowed: boolean;
  consentedAtUtc: string;
  consentSource: string;
}

export interface ChildSafetyDecision {
  blocked: boolean;
  flags: string[];
  learnerMessage: string;
}

export interface AzurePronunciationAssessment {
  AccuracyScore?: number;
  FluencyScore?: number;
  CompletenessScore?: number;
  PronScore?: number;
  ProsodyScore?: number;
  ErrorType?: string;
  NBestPhonemes?: Array<{ Phoneme?: string; Score?: number }>;
}

export interface AzurePhoneme {
  Phoneme?: string;
  AccuracyScore?: number;
  ErrorType?: string;
  NBestPhonemes?: Array<{ Phoneme?: string; Score?: number }>;
  PronunciationAssessment?: AzurePronunciationAssessment;
}

export interface AzureSyllable {
  Syllable?: string;
  AccuracyScore?: number;
  PronunciationAssessment?: AzurePronunciationAssessment;
}

export interface AzureWord {
  Word?: string;
  AccuracyScore?: number;
  ErrorType?: string;
  Syllables?: AzureSyllable[];
  Phonemes?: AzurePhoneme[];
  PronunciationAssessment?: AzurePronunciationAssessment;
}

export interface AzureNBest {
  Lexical?: string;
  Display?: string;
  AccuracyScore?: number;
  FluencyScore?: number;
  CompletenessScore?: number;
  PronScore?: number;
  Words?: AzureWord[];
  PronunciationAssessment?: AzurePronunciationAssessment;
}

export interface AzurePronunciationJson {
  DisplayText?: string;
  NBest?: AzureNBest[];
}

export interface PhoneticSegmentRecord {
  spelling: string;
  friendlySound: string;
  heardSound: string;
  beatIndex: number;
  status: PhoneticSegmentStatus;
  confidence: number;
}

export type BuddyCanonicalZone = "Town" | "Route" | "Gym";
export type BuddyIntent =
  | "wrong_answer_coach"
  | "grammar_explanation"
  | "translation_help"
  | "pronunciation_help"
  | "navigation_help"
  | "social_chat"
  | "safety_or_unknown";
export type BuddyRouteAction = "block" | "local" | "model";
export type BuddyTier = "free" | "standard" | "premium";

export interface BuddyRouterDecision {
  intent: BuddyIntent;
  action: BuddyRouteAction;
  tier: BuddyTier;
  reason: string;
  modelAllowed: boolean;
  dailyModelCallLimit: number;
  maxOutputTokens: number;
  forceOpenGrimoire: boolean;
  fallbackMessage: string;
  safetyFlags: string[];
}

export interface BuddyHelpResponse {
  status: "ok" | "fallback" | "blocked";
  fallbackReason: string;
  learnerText: string;
  speechText: string;
  speechSegments: BuddySpeechSegment[];
  responseLanguage: string;
  phonicsCueKey: string;
  phonicsAnchorWord: string;
  hintLevel: string;
  errorCategory: string;
  teacherNote: string;
  safeMemoryTags: string[];
  relationshipMemoryCandidates: string[];
  safetyFlags: string[];
  openGrimoire: boolean;
  grimoireConceptId: string;
  grimoireHighlightKey: string;
  callDisposition: "continue" | "end";
  callId: string;
  callTurnIndex: number;
  provider: string;
  model: string;
  latencyMs: number;
  routerIntent?: string;
  routerAction?: string;
  routerReason?: string;
  tier?: string;
}

export interface BuddySpeechSegment {
  language: string;
  text: string;
}

export interface GeminiBuddyResult {
  output?: Record<string, unknown>;
  usage: Record<string, unknown>;
  safetyFlags: string[];
  reason?: string;
  provider?: "sarvam" | "gemini";
  model?: string;
  primaryFailureReason?: string;
}

export interface BuddyBudgetReservation {
  allowed: boolean;
  reason: string;
  dateUtc: string;
  requestCount: number;
  reservedTokenCount: number;
  tier: BuddyTier;
  requestLimit: number;
  tokenLimit: number;
  reservedCostMicroUsd?: number;
}

export interface PhonemeAlignmentRecord {
  expected: string;
  observed: string;
  status: string;
  confidence: number;
}

export interface CallableAuth {
  uid: string;
  token: {
    role?: Role;
    schoolId?: string;
    classIds?: string[];
    studentIds?: string[];
    studentId?: string;
  } & Record<string, unknown>;
}
export function boundedEnvironmentInteger(name: string, fallback: number, minimum: number, maximum: number) {
  return boundedInteger(process.env[name], fallback, minimum, maximum);
}

export function boundedEnvironmentNumber(name: string, fallback: number, minimum: number, maximum: number) {
  const parsed = Number(process.env[name]);
  if (!Number.isFinite(parsed)) return fallback;
  return Math.max(minimum, Math.min(maximum, parsed));
}

export function normalizedProviderName(value: unknown) {
  const normalized = String(value ?? "").trim().toLowerCase();
  return normalized === "gemini" ? "gemini" : "sarvam";
}

export function environmentBoolean(name: string, fallback: boolean) {
  const value = process.env[name]?.trim().toLowerCase();
  if (!value) return fallback;
  if (["1", "true", "yes", "on"].includes(value)) return true;
  if (["0", "false", "no", "off"].includes(value)) return false;
  return fallback;
}

export function boundedInteger(value: unknown, fallback: number, minimum: number, maximum: number) {
  const numeric = typeof value === "number" ? value : Number(value ?? 0);
  if (!Number.isFinite(numeric)) return fallback;
  const parsed = Math.max(0, Math.floor(numeric));
  if (parsed <= 0) return fallback;
  return Math.max(minimum, Math.min(maximum, parsed));
}
