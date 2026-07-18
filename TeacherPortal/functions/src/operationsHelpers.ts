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
import { AnalysisJob, azurePronunciationUsdPerAudioHour, db, schoolDailyAiCostLimitMicroUsd } from "./runtime.js";
import { utcDateKey } from "./buddyBudgetHelpers.js";
import { uniqueBuddyStrings } from "./buddyResponseHelpers.js";
import { trimmedString } from "./inputHelpers.js";
import { finiteNumber } from "./sharedUtils.js";

export async function deleteExpiredCollectionGroup(
  collectionName: string,
  timestampField: string,
  cutoff: string,
  additionalField = "",
  additionalValue: unknown = undefined
) {
  let query = db.collectionGroup(collectionName).where(timestampField, "<", cutoff).limit(400);
  if (additionalField) {
    query = query.where(additionalField, "==", additionalValue);
  }
  const snapshot = await query.get();
  if (snapshot.empty) return 0;
  const batch = db.batch();
  for (const document of snapshot.docs) batch.delete(document.ref);
  await batch.commit();
  return snapshot.size;
}

export async function recordPronunciationOperations(job: AnalysisJob, status: string, latencyMsValue: number) {
  const schoolId = trimmedString(job.schoolId, 160);
  const studentId = trimmedString(job.studentId, 160);
  if (!schoolId || !studentId) return;
  const dateUtc = utcDateKey();
  const audioSeconds = Math.max(0, Math.min(30, finiteNumber(job.audioDurationSeconds)));
  const estimatedCostMicroUsd = Math.max(0, Math.round(
    audioSeconds / 3600 * azurePronunciationUsdPerAudioHour * 1_000_000));
  const latencyMs = Math.max(0, Math.round(latencyMsValue));
  try {
    const aggregate = {
      dateUtc,
      requestCount: FieldValue.increment(1),
      completedCount: FieldValue.increment(status === "complete" ? 1 : 0),
      failedCount: FieldValue.increment(status === "failed" ? 1 : 0),
      disabledCount: FieldValue.increment(status === "disabled" ? 1 : 0),
      analyzedAudioSeconds: FieldValue.increment(status === "complete" ? audioSeconds : 0),
      estimatedCostMicroUsd: FieldValue.increment(status === "complete" ? estimatedCostMicroUsd : 0),
      totalLatencyMs: FieldValue.increment(latencyMs),
      latencyLt3000Count: FieldValue.increment(latencyMs < 3000 ? 1 : 0),
      latency3000To7999Count: FieldValue.increment(latencyMs >= 3000 && latencyMs < 8000 ? 1 : 0),
      latencyGte8000Count: FieldValue.increment(latencyMs >= 8000 ? 1 : 0),
      updatedAt: FieldValue.serverTimestamp()
    };
    const batch = db.batch();
    batch.set(db.doc(`schools/${schoolId}/pronunciationOperationsDaily/${dateUtc}`), aggregate, { merge: true });
    batch.set(db.doc(`schools/${schoolId}/aiCostBudgetDaily/${dateUtc}`), {
      dateUtc,
      actualEstimatedCostMicroUsd: FieldValue.increment(status === "complete" ? estimatedCostMicroUsd : 0),
      costLimitMicroUsd: schoolDailyAiCostLimitMicroUsd,
      updatedAt: FieldValue.serverTimestamp()
    }, { merge: true });
    batch.set(db.doc(`schools/${schoolId}/students/${studentId}/pronunciationUsageDaily/${dateUtc}`), {
      providerCompletedCount: FieldValue.increment(status === "complete" ? 1 : 0),
      providerFailedCount: FieldValue.increment(status === "failed" ? 1 : 0),
      estimatedCostMicroUsd: FieldValue.increment(status === "complete" ? estimatedCostMicroUsd : 0),
      lastProviderStatus: status,
      lastProviderLatencyMs: latencyMs,
      updatedAt: FieldValue.serverTimestamp()
    }, { merge: true });
    await batch.commit();
  } catch (error) {
    console.error("[PronunciationOps] telemetry write failed", error);
  }
}

export async function emitOperationalAlert(
  schoolId: string,
  dateUtc: string,
  category: string,
  details: Record<string, unknown>
) {
  const reference = db.doc(`schools/${schoolId}/operationsAlertState/${dateUtc}_${category}`);
  const shouldEmit = await db.runTransaction(async transaction => {
    const snapshot = await transaction.get(reference);
    const lastAlert = Date.parse(String(snapshot.data()?.lastAlertAtUtc ?? ""));
    if (Number.isFinite(lastAlert) && Date.now() - lastAlert < 60 * 60 * 1000) return false;
    transaction.set(reference, {
      dateUtc,
      category,
      lastAlertAtUtc: new Date().toISOString(),
      details,
      updatedAt: FieldValue.serverTimestamp()
    }, { merge: true });
    return true;
  });
  if (shouldEmit) console.warn("[OpsAlert]", JSON.stringify({ schoolId, dateUtc, category, ...details }));
}

export async function scrubExpiredHandwritingEvidence(collectionName: string, cutoffUtc: string) {
  const snapshot = await db.collectionGroup(collectionName)
    .where("rawStrokeExpiresAtUtc", "<", cutoffUtc)
    .limit(300)
    .get();
  if (snapshot.empty) return 0;
  const batch = db.batch();
  for (const document of snapshot.docs) {
    batch.set(document.ref, {
      points: FieldValue.delete(),
      rawStrokeCaptured: false,
      rawStrokePointCount: 0,
      rawStrokeExpiresAtUtc: FieldValue.delete(),
      rawStrokeRetentionPolicy: "purged_after_retention",
      rawStrokePurgedAt: FieldValue.serverTimestamp()
    }, { merge: true });
  }
  await batch.commit();
  return snapshot.size;
}

export async function recordBuddySafetyAggregate(schoolId: string, flags: string[]) {
  try {
    const increments: Record<string, unknown> = {
      dateUtc: utcDateKey(),
      interceptCount: FieldValue.increment(1),
      updatedAt: FieldValue.serverTimestamp()
    };
    for (const flag of uniqueBuddyStrings(flags, 8)) increments[`flags.${flag}`] = FieldValue.increment(1);
    await db.doc(`schools/${schoolId}/buddySafetyOperationsDaily/${utcDateKey()}`).set(increments, { merge: true });
  } catch (error) {
    console.error("[BuddySafety] aggregate write failed", error);
  }
}
