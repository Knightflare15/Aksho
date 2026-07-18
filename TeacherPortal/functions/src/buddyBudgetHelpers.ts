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
import { BuddyBudgetReservation, BuddyHelpResponse, BuddyTier, buddyAbsoluteDailyRequestLimit, buddyInputUsdPerMillionTokens, buddyOutputUsdPerMillionTokens, buddyRequestCooldownMs, buddyRequestLeaseMs, buddyReservedTokensPerRequest, db, sarvamSttUsdPerAudioHour, schoolDailyAiCostLimitMicroUsd } from "./runtime.js";
import { buddyProviderTokenPrices } from "./buddyProviderCore.js";
import { buddyTierModelCallLimit, buddyTierSttSecondsLimit, buddyTierTokenLimit, normalizeBuddyTier } from "./buddyRoutingHelpers.js";
import { trimmedString } from "./inputHelpers.js";
import { finiteInteger } from "./sharedUtils.js";

export async function reserveBuddySttBudget(
  studentPath: string,
  schoolId: string,
  tier: BuddyTier,
  audioSeconds: number
) {
  const dateUtc = utcDateKey();
  const usageRef = db.doc(`${studentPath}/buddySttUsageDaily/${dateUtc}`);
  const operationsRef = db.doc(`schools/${schoolId}/buddySttOperationsDaily/${dateUtc}`);
  const schoolCostRef = db.doc(`schools/${schoolId}/aiCostBudgetDaily/${dateUtc}`);
  const limitSeconds = buddyTierSttSecondsLimit(tier);
  const roundedSeconds = Math.max(1, Math.ceil(audioSeconds));
  return db.runTransaction(async transaction => {
    const [snapshot, schoolCostSnapshot] = await Promise.all([
      transaction.get(usageRef),
      transaction.get(schoolCostRef)
    ]);
    const usedSeconds = Math.max(0, finiteInteger(snapshot.data()?.audioSeconds));
    const estimatedCostMicroUsd = Math.max(0, Math.round(
      roundedSeconds / 3600 * sarvamSttUsdPerAudioHour * 1_000_000));
    const schoolCost = schoolCostSnapshot.data() ?? {};
    const currentSchoolSpend = Math.max(
      Math.max(0, finiteInteger(schoolCost.reservedCostMicroUsd)),
      Math.max(0, finiteInteger(schoolCost.actualEstimatedCostMicroUsd)));
    if (usedSeconds + roundedSeconds > limitSeconds || currentSchoolSpend + estimatedCostMicroUsd > schoolDailyAiCostLimitMicroUsd) {
      transaction.set(usageRef, {
        dateUtc,
        tier,
        audioSeconds: usedSeconds,
        limitSeconds,
        rejectedCount: FieldValue.increment(1),
        updatedAt: FieldValue.serverTimestamp()
      }, { merge: true });
      return { allowed: false, usedSeconds, limitSeconds };
    }

    const nextUsedSeconds = usedSeconds + roundedSeconds;
    transaction.set(usageRef, {
      dateUtc,
      tier,
      audioSeconds: nextUsedSeconds,
      limitSeconds,
      requestCount: FieldValue.increment(1),
      estimatedCostMicroUsd: FieldValue.increment(estimatedCostMicroUsd),
      updatedAt: FieldValue.serverTimestamp()
    }, { merge: true });
    transaction.set(operationsRef, {
      dateUtc,
      requestCount: FieldValue.increment(1),
      audioSeconds: FieldValue.increment(roundedSeconds),
      estimatedCostMicroUsd: FieldValue.increment(estimatedCostMicroUsd),
      updatedAt: FieldValue.serverTimestamp()
    }, { merge: true });
    transaction.set(schoolCostRef, {
      dateUtc,
      actualEstimatedCostMicroUsd: FieldValue.increment(estimatedCostMicroUsd),
      sttEstimatedCostMicroUsd: FieldValue.increment(estimatedCostMicroUsd),
      costLimitMicroUsd: schoolDailyAiCostLimitMicroUsd,
      updatedAt: FieldValue.serverTimestamp()
    }, { merge: true });
    return { allowed: true, usedSeconds: nextUsedSeconds, limitSeconds };
  });
}

export function estimateBuddyAudioSeconds(audioBase64: string) {
  let audio: Buffer;
  try {
    audio = Buffer.from(audioBase64, "base64");
  } catch {
    return 0;
  }
  if (audio.length < 44 || audio.toString("ascii", 0, 4) !== "RIFF") return 0;
  const channels = audio.readUInt16LE(22);
  const sampleRate = audio.readUInt32LE(24);
  const bitsPerSample = audio.readUInt16LE(34);
  if (channels <= 0 || sampleRate <= 0 || bitsPerSample <= 0) return 0;
  let offset = 12;
  while (offset + 8 <= audio.length) {
    const chunkId = audio.toString("ascii", offset, offset + 4);
    const chunkSize = audio.readUInt32LE(offset + 4);
    if (chunkId === "data") {
      return chunkSize / (sampleRate * channels * (bitsPerSample / 8));
    }
    offset += 8 + chunkSize + (chunkSize % 2);
  }
  return 0;
}

export async function reserveBuddyRequest(studentPath: string) {
  const guard = db.doc(`${studentPath}/buddyRequestGuards/current`);
  const now = Date.now();
  return db.runTransaction(async transaction => {
    const snapshot = await transaction.get(guard);
    const current = snapshot.exists ? snapshot.data() ?? {} : {};
    const lastRequestedAt = finiteInteger(current.lastRequestedAt);
    const inFlightUntil = finiteInteger(current.inFlightUntil);
    if (inFlightUntil > now || now - lastRequestedAt < buddyRequestCooldownMs) {
      return false;
    }
    transaction.set(guard, {
      lastRequestedAt: now,
      inFlightUntil: now + buddyRequestLeaseMs,
      updatedAt: FieldValue.serverTimestamp()
    }, { merge: true });
    return true;
  });
}

export async function releaseBuddyRequest(studentPath: string) {
  await db.doc(`${studentPath}/buddyRequestGuards/current`).set({
    inFlightUntil: 0,
    completedAt: FieldValue.serverTimestamp()
  }, { merge: true });
}

export async function reserveBuddyBudget(studentPath: string, tier: BuddyTier, schoolId: string): Promise<BuddyBudgetReservation> {
  const dateUtc = utcDateKey();
  const budgetRef = db.doc(`${studentPath}/buddyUsageDaily/${dateUtc}`);
  const schoolCostRef = db.doc(`schools/${schoolId}/aiCostBudgetDaily/${dateUtc}`);
  const requestLimit = Math.min(buddyAbsoluteDailyRequestLimit, buddyTierModelCallLimit(tier));
  const tokenLimit = buddyTierTokenLimit(tier);
  const reservedCostMicroUsd = Math.max(1, Math.round(
    buddyReservedTokensPerRequest * Math.max(buddyInputUsdPerMillionTokens, buddyOutputUsdPerMillionTokens)));
  return db.runTransaction(async transaction => {
    const [snapshot, schoolCostSnapshot] = await Promise.all([
      transaction.get(budgetRef),
      transaction.get(schoolCostRef)
    ]);
    const current = snapshot.exists ? snapshot.data() ?? {} : {};
    const requestCount = Math.max(0, finiteInteger(current.requestCount));
    const reservedTokenCount = Math.max(0, finiteInteger(current.reservedTokenCount));
    const schoolReservedCost = Math.max(0, finiteInteger(schoolCostSnapshot.data()?.reservedCostMicroUsd));
    const requestLimitReached = requestCount >= requestLimit;
    const tokenLimitReached = reservedTokenCount + buddyReservedTokensPerRequest > tokenLimit;
    const schoolCostLimitReached = schoolReservedCost + reservedCostMicroUsd > schoolDailyAiCostLimitMicroUsd;
    if (requestLimitReached || tokenLimitReached || schoolCostLimitReached) {
      const reason = requestLimitReached ? "daily_request_limit" : tokenLimitReached ? "daily_token_limit" : "school_daily_cost_limit";
      transaction.set(budgetRef, {
        dateUtc,
        tier,
        requestLimit,
        tokenLimit,
        budgetMode: "capped",
        lastBudgetRejectedReason: reason,
        lastBudgetRejectedAt: FieldValue.serverTimestamp(),
        updatedAt: FieldValue.serverTimestamp()
      }, { merge: true });
      return {
        allowed: false,
        reason,
        dateUtc,
        requestCount,
        reservedTokenCount,
        tier,
        requestLimit,
        tokenLimit,
        reservedCostMicroUsd: 0
      };
    }

    const nextRequestCount = requestCount + 1;
    const nextReservedTokenCount = reservedTokenCount + buddyReservedTokensPerRequest;
    transaction.set(budgetRef, {
      dateUtc,
      requestCount: nextRequestCount,
      reservedTokenCount: nextReservedTokenCount,
      tier,
      requestLimit,
      tokenLimit,
      budgetMode: "capped",
      updatedAt: FieldValue.serverTimestamp()
    }, { merge: true });
    transaction.set(schoolCostRef, {
      dateUtc,
      reservedCostMicroUsd: schoolReservedCost + reservedCostMicroUsd,
      costLimitMicroUsd: schoolDailyAiCostLimitMicroUsd,
      buddyReservationCount: FieldValue.increment(1),
      updatedAt: FieldValue.serverTimestamp()
    }, { merge: true });
    return {
      allowed: true,
      reason: "",
      dateUtc,
      requestCount: nextRequestCount,
      reservedTokenCount: nextReservedTokenCount,
      tier,
      requestLimit,
      tokenLimit,
      reservedCostMicroUsd
    };
  });
}

export async function recordBuddyOperations(input: {
  schoolId: string;
  studentId: string;
  dateUtc: string;
  response: BuddyHelpResponse;
  usage: Record<string, unknown>;
  latencyMs: number;
  budget?: BuddyBudgetReservation;
}) {
  try {
    const tokenUsage = buddyUsageTokens(input.usage);
    const providerPrices = buddyProviderTokenPrices(input.response.provider);
    const estimatedCostMicroUsd = Math.max(0, Math.round(
      tokenUsage.input * providerPrices.input +
      tokenUsage.output * providerPrices.output));
    const latencyMs = Math.max(0, Math.round(input.latencyMs));
    const safeStatus = input.response.status === "ok" ? "ok" : input.response.status === "blocked" ? "blocked" : "fallback";
    const operationRef = db.doc(`schools/${input.schoolId}/buddyOperationsDaily/${input.dateUtc}`);
    const studentUsageRef = db.doc(`schools/${input.schoolId}/students/${input.studentId}/buddyUsageDaily/${input.dateUtc}`);
    const aggregate = {
      dateUtc: input.dateUtc,
      modelResponseCount: FieldValue.increment(safeStatus === "ok" ? 1 : 0),
      fallbackCount: FieldValue.increment(safeStatus === "fallback" ? 1 : 0),
      blockedCount: FieldValue.increment(safeStatus === "blocked" ? 1 : 0),
      budgetRejectedCount: FieldValue.increment(input.response.fallbackReason.includes("limit") ? 1 : 0),
      inputTokenCount: FieldValue.increment(tokenUsage.input),
      outputTokenCount: FieldValue.increment(tokenUsage.output),
      totalTokenCount: FieldValue.increment(tokenUsage.total),
      estimatedCostMicroUsd: FieldValue.increment(estimatedCostMicroUsd),
      totalLatencyMs: FieldValue.increment(latencyMs),
      latencyLt1000Count: FieldValue.increment(latencyMs < 1000 ? 1 : 0),
      latency1000To2999Count: FieldValue.increment(latencyMs >= 1000 && latencyMs < 3000 ? 1 : 0),
      latency3000To7999Count: FieldValue.increment(latencyMs >= 3000 && latencyMs < 8000 ? 1 : 0),
      latencyGte8000Count: FieldValue.increment(latencyMs >= 8000 ? 1 : 0),
      updatedAt: FieldValue.serverTimestamp()
    };
    const batch = db.batch();
    batch.set(operationRef, {
      ...aggregate,
      requestCount: FieldValue.increment(1)
    }, { merge: true });
    batch.set(db.doc(`schools/${input.schoolId}/aiCostBudgetDaily/${input.dateUtc}`), {
      dateUtc: input.dateUtc,
      actualEstimatedCostMicroUsd: FieldValue.increment(estimatedCostMicroUsd),
      costLimitMicroUsd: schoolDailyAiCostLimitMicroUsd,
      updatedAt: FieldValue.serverTimestamp()
    }, { merge: true });
    batch.set(studentUsageRef, {
      ...aggregate,
      completedRequestCount: FieldValue.increment(1),
      studentId: input.studentId,
      schoolId: input.schoolId,
      tier: input.budget?.tier ?? normalizeBuddyTier(input.response.tier),
      requestLimit: input.budget?.requestLimit ?? buddyTierModelCallLimit(normalizeBuddyTier(input.response.tier)),
      tokenLimit: input.budget?.tokenLimit ?? buddyTierTokenLimit(normalizeBuddyTier(input.response.tier)),
      budgetMode: "capped",
      lastStatus: safeStatus,
      lastFallbackReason: trimmedString(input.response.fallbackReason, 64),
      lastProvider: trimmedString(input.response.provider, 32),
      lastModel: trimmedString(input.response.model, 96),
      lastRequestAtUtc: new Date().toISOString(),
      lastBudgetRequestCount: input.budget?.requestCount ?? 0,
      lastBudgetReservedTokenCount: input.budget?.reservedTokenCount ?? 0
    }, { merge: true });
    await batch.commit();
    console.info("[BuddyOps]", JSON.stringify({
      event: "buddy_request",
      schoolId: input.schoolId,
      dateUtc: input.dateUtc,
      status: safeStatus,
      reason: trimmedString(input.response.fallbackReason, 64),
      provider: trimmedString(input.response.provider, 32),
      latencyMs,
      totalTokens: tokenUsage.total,
      estimatedCostMicroUsd
    }));
  } catch (error) {
    // Observability must never turn a learner-facing fallback into a failed lesson.
    console.error("[BuddyOps] telemetry write failed", error);
  }
}

export function buddyUsageTokens(usage: Record<string, unknown>) {
  const input = Math.max(0, finiteInteger(usage.promptTokenCount ?? usage.promptTokens));
  const output = Math.max(0, finiteInteger(usage.candidatesTokenCount ?? usage.outputTokenCount ?? usage.outputTokens));
  const total = Math.max(input + output, finiteInteger(usage.totalTokenCount ?? usage.totalTokens));
  return { input, output, total };
}

export function utcDateKey() {
  return new Date().toISOString().slice(0, 10);
}

export function dateKeyInTimeZone(value: Date, timeZone: string) {
  try {
    const parts = new Intl.DateTimeFormat("en-CA", {
      timeZone,
      year: "numeric",
      month: "2-digit",
      day: "2-digit"
    }).formatToParts(value);
    const part = (type: string) => parts.find(item => item.type === type)?.value ?? "";
    return `${part("year")}-${part("month")}-${part("day")}`;
  } catch {
    return value.toISOString().slice(0, 10);
  }
}
