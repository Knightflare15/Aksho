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
import { clamp01 } from "./pronunciationProcessingHelpers.js";
import { cloneNumberRecord, cloneRecord, finiteInteger, finiteNumber, normalizedAggregateKey, normalizedStringArray, safeRatio } from "./sharedUtils.js";

export function updateBuddyConceptAggregate(
  current: Record<string, unknown>,
  attempt: Record<string, unknown>,
  conceptId: string
) {
  const attempts = finiteInteger(current.attempts) + 1;
  const correctAttempts = finiteInteger(current.correctAttempts) + (attempt.correct === true ? 1 : 0);
  const independentCorrectAttempts = finiteInteger(current.independentCorrectAttempts) +
    (attempt.completedIndependently === true ? 1 : 0);
  const assistedCorrectAttempts = finiteInteger(current.assistedCorrectAttempts) +
    (attempt.correct === true && attempt.completedIndependently !== true ? 1 : 0);
  const firstAttemptCorrectAttempts = finiteInteger(current.firstAttemptCorrectAttempts) +
    (attempt.correct === true && finiteInteger(attempt.attemptNumber) <= 1 ? 1 : 0);
  const totalHints = finiteInteger(current.totalHints) + Math.max(0, finiteInteger(attempt.hintCount));
  const totalResponseSeconds = finiteNumber(current.totalResponseSeconds) + Math.max(0, finiteNumber(attempt.responseSeconds));
  const errors = cloneNumberRecord(current.errors);
  for (const errorTag of normalizedStringArray(attempt.errorTags, 20)) {
    const key = normalizedAggregateKey(errorTag, "incorrect");
    errors[key] = finiteInteger(errors[key]) + 1;
  }

  const modalities = cloneRecord(current.modalities);
  const modalityKey = normalizedAggregateKey(attempt.modality, "Unknown");
  const currentModality = cloneRecord(modalities[modalityKey]);
  const modalityAttempts = finiteInteger(currentModality.attempts) + 1;
  const modalityCorrect = finiteInteger(currentModality.correctAttempts) + (attempt.correct === true ? 1 : 0);
  const modalityIndependent = finiteInteger(currentModality.independentCorrectAttempts) +
    (attempt.completedIndependently === true ? 1 : 0);
  const modalityResponseSeconds = finiteNumber(currentModality.totalResponseSeconds) +
    Math.max(0, finiteNumber(attempt.responseSeconds));
  modalities[modalityKey] = {
    modality: String(attempt.modality ?? "Unknown"),
    attempts: modalityAttempts,
    correctAttempts: modalityCorrect,
    independentCorrectAttempts: modalityIndependent,
    totalResponseSeconds: modalityResponseSeconds,
    averageResponseSeconds: safeRatio(modalityResponseSeconds, modalityAttempts),
    successRate: safeRatio(modalityCorrect, modalityAttempts),
    independentSuccessRate: safeRatio(modalityIndependent, modalityAttempts),
    lastPracticedAtUtc: String(attempt.createdAtUtc ?? "")
  };

  const successRate = safeRatio(correctAttempts, attempts);
  const independentSuccessRate = safeRatio(independentCorrectAttempts, attempts);
  const hintDependency = clamp01(safeRatio(totalHints, attempts));
  const smoothedSuccess = safeRatio(correctAttempts + 1, attempts + 2);
  const smoothedIndependent = safeRatio(independentCorrectAttempts + 1, attempts + 2);
  const evidenceMastery = clamp01(
    0.55 * smoothedIndependent +
    0.35 * smoothedSuccess +
    0.10 * (1 - hintDependency)
  );
  const evidenceWeight = clamp01(attempts / 8);
  const masteryEstimate = 0.25 + (evidenceMastery - 0.25) * evidenceWeight;
  const support = buddyLanguageSupport(masteryEstimate);
  const review = updateBuddyReviewState(current, attempt);

  return {
    conceptId: String(attempt.conceptId ?? conceptId),
    attempts,
    correctAttempts,
    independentCorrectAttempts,
    assistedCorrectAttempts,
    firstAttemptCorrectAttempts,
    totalHints,
    totalResponseSeconds,
    averageResponseSeconds: safeRatio(totalResponseSeconds, attempts),
    successRate,
    independentSuccessRate,
    assistedSuccessRate: safeRatio(assistedCorrectAttempts, attempts),
    hintDependency,
    masteryEstimate,
    supportBand: support.supportBand,
    recommendedEnglishRatio: support.recommendedEnglishRatio,
    reviewStage: review.reviewStage,
    lapseCount: review.lapseCount,
    consecutiveIndependentCorrect: review.consecutiveIndependentCorrect,
    nextReviewAtUtc: review.nextReviewAtUtc,
    lastIndependentCorrectAtUtc: review.lastIndependentCorrectAtUtc,
    errors,
    modalities,
    lastPracticedAtUtc: String(attempt.createdAtUtc ?? "")
  };
}

export function updateBuddyReviewState(current: Record<string, unknown>, attempt: Record<string, unknown>) {
  const reviewIntervalsMs = [
    10 * 60 * 1000,
    24 * 60 * 60 * 1000,
    3 * 24 * 60 * 60 * 1000,
    7 * 24 * 60 * 60 * 1000,
    14 * 24 * 60 * 60 * 1000,
    30 * 24 * 60 * 60 * 1000
  ];
  let reviewStage = finiteInteger(current.reviewStage);
  let lapseCount = finiteInteger(current.lapseCount);
  let consecutiveIndependentCorrect = finiteInteger(current.consecutiveIndependentCorrect);
  let lastIndependentCorrectAtUtc = String(current.lastIndependentCorrectAtUtc ?? "");
  const parsedOccurredAt = Date.parse(String(attempt.createdAtUtc ?? ""));
  const occurredAt = Number.isFinite(parsedOccurredAt) ? parsedOccurredAt : Date.now();

  if (attempt.completedIndependently === true) {
    consecutiveIndependentCorrect += 1;
    reviewStage = Math.min(reviewIntervalsMs.length, reviewStage + 1);
    lastIndependentCorrectAtUtc = new Date(occurredAt).toISOString();
  } else if (attempt.correct !== true) {
    lapseCount += 1;
    consecutiveIndependentCorrect = 0;
    reviewStage = Math.max(0, reviewStage - 2);
  } else {
    consecutiveIndependentCorrect = 0;
  }

  const intervalIndex = Math.max(0, Math.min(reviewIntervalsMs.length - 1, reviewStage));
  const intervalMs = attempt.correct === true ? reviewIntervalsMs[intervalIndex] : reviewIntervalsMs[0];
  return {
    reviewStage,
    lapseCount,
    consecutiveIndependentCorrect,
    nextReviewAtUtc: new Date(occurredAt + intervalMs).toISOString(),
    lastIndependentCorrectAtUtc
  };
}

export function updateBuddyConceptDiagnostics(
  current: Record<string, unknown>,
  attempt: Record<string, unknown>,
  conceptId: string
) {
  const errors = cloneNumberRecord(current.errors);
  for (const errorTag of normalizedStringArray(attempt.errorTags, 20)) {
    const key = normalizedAggregateKey(errorTag, "diagnostic_issue");
    errors[key] = finiteInteger(errors[key]) + 1;
  }
  return {
    ...current,
    conceptId: String(attempt.conceptId ?? current.conceptId ?? conceptId),
    errors,
    lastDiagnosticAtUtc: String(attempt.createdAtUtc ?? "")
  };
}

export function summarizeBuddyConcepts(concepts: Record<string, unknown>) {
  const practiced = Object.values(concepts)
    .map(value => cloneRecord(value))
    .filter(value => finiteInteger(value.attempts) > 0)
    .sort((a, b) => finiteNumber(b.masteryEstimate) - finiteNumber(a.masteryEstimate));
  const strengthConceptIds = practiced
    .filter(value => finiteInteger(value.attempts) >= 2)
    .slice(0, 3)
    .map(value => String(value.conceptId ?? "Unscoped"));
  const needConceptIds = [...practiced]
    .reverse()
    .slice(0, 3)
    .map(value => String(value.conceptId ?? "Unscoped"));

  const combinedErrors: Record<string, number> = {};
  let weightedMastery = 0;
  let totalAttempts = 0;
  for (const concept of practiced) {
    const attempts = finiteInteger(concept.attempts);
    totalAttempts += attempts;
    weightedMastery += finiteNumber(concept.masteryEstimate) * attempts;
    const errors = cloneNumberRecord(concept.errors);
    for (const [tag, count] of Object.entries(errors)) {
      combinedErrors[tag] = finiteInteger(combinedErrors[tag]) + finiteInteger(count);
    }
  }
  const recurringErrorTags = Object.entries(combinedErrors)
    .sort((a, b) => b[1] - a[1])
    .slice(0, 5)
    .map(([tag]) => tag);
  const support = buddyLanguageSupport(totalAttempts > 0 ? weightedMastery / totalAttempts : 0);
  return {
    strengthConceptIds,
    needConceptIds,
    recurringErrorTags,
    supportBand: support.supportBand,
    recommendedEnglishRatio: support.recommendedEnglishRatio
  };
}

export function buddyLanguageSupport(mastery: number) {
  if (mastery < 0.35) {
    return { supportBand: "Foundation", recommendedEnglishRatio: 0.3 };
  }
  if (mastery < 0.55) {
    return { supportBand: "Guided", recommendedEnglishRatio: 0.45 };
  }
  if (mastery < 0.75) {
    return { supportBand: "Growing", recommendedEnglishRatio: 0.65 };
  }
  return { supportBand: "Independent", recommendedEnglishRatio: 0.85 };
}
