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
import { aiCostWarningPercent, auth, boundedInteger, buddyConversationRetentionDays, buddyLatencyAlertMs, buddyUsageRetentionDays, clientDiagnosticDailyLimit, clientDiagnosticRetentionDays, crashDailyAlertCount, db, pronunciationLatencyAlertMs, pronunciationOrphanAudioMaxAgeHours, schoolDailyAiCostLimitMicroUsd, storage } from "./runtime.js";
import { assertSameSchool, requireRole, requireStudentPayload } from "./authorizationHelpers.js";
import { utcDateKey } from "./buddyBudgetHelpers.js";
import { redactDiagnosticText, redactKnownStudentText, safeDiagnosticToken } from "./buddyContextHelpers.js";
import { requiredTrimmedString, trimmedString } from "./inputHelpers.js";
import { deleteExpiredCollectionGroup, emitOperationalAlert, scrubExpiredHandwritingEvidence } from "./operationsHelpers.js";
import { privacyPermissionGranted, readStudentPrivacy } from "./privacyHelpers.js";
import { finiteInteger, finiteNumber, randomId } from "./sharedUtils.js";
import { writeStudentRecord } from "./studentRecordHelpers.js";

export const getBuddyOperationsSummary = onCall(async (request) => {
  const caller = requireRole(request.auth, ["admin", "teacher"]);
  const schoolId = requiredTrimmedString(request.data?.schoolId, "schoolId");
  assertSameSchool(caller, schoolId);
  const requestedDays = boundedInteger(request.data?.days, 7, 1, 31);
  const snapshot = await db.collection(`schools/${schoolId}/buddyOperationsDaily`)
    .orderBy("dateUtc", "desc")
    .limit(requestedDays)
    .get();
  return {
    schoolId,
    days: snapshot.docs.map(document => ({ dateUtc: document.id, ...document.data() }))
  };
});

/** One aggregate-only view for latency, safety, crashes and estimated provider spend. */

export const getOperationsSummary = onCall(async (request) => {
  const caller = requireRole(request.auth, ["admin", "teacher"]);
  const schoolId = requiredTrimmedString(request.data?.schoolId, "schoolId");
  assertSameSchool(caller, schoolId);
  const days = boundedInteger(request.data?.days, 7, 1, 31);
  const readDaily = async (collection: string) => {
    const snapshot = await db.collection(`schools/${schoolId}/${collection}`)
      .orderBy("dateUtc", "desc")
      .limit(days)
      .get();
    return snapshot.docs.map(document => ({ dateUtc: document.id, ...document.data() }));
  };
  const [buddy, buddyStt, pronunciation, diagnostics, safety, costBudget] = await Promise.all([
    readDaily("buddyOperationsDaily"),
    readDaily("buddySttOperationsDaily"),
    readDaily("pronunciationOperationsDaily"),
    readDaily("clientDiagnosticOperationsDaily"),
    readDaily("buddySafetyOperationsDaily"),
    readDaily("aiCostBudgetDaily")
  ]);
  return { schoolId, currency: "USD", costUnit: "micro_USD_estimate", buddy, buddyStt, pronunciation, diagnostics, safety, costBudget };
});

/** Receives a small, redacted diagnostic envelope; never audio, learner answers, or arbitrary attachments. */

export const reportClientDiagnostic = onCall(async (request) => {
  const caller = requireRole(request.auth, ["student", "parent"]);
  const payload = requireStudentPayload(caller, request.data ?? {});
  const studentSnapshot = await db.doc(`schools/${payload.schoolId}/students/${payload.studentId}`).get();
  if (!studentSnapshot.exists) throw new HttpsError("not-found", "Student was not found.");
  if (!privacyPermissionGranted(readStudentPrivacy(studentSnapshot.data() ?? {}), "diagnosticsAllowed")) {
    return { accepted: false, reason: "parental_consent_required" };
  }
  const severity = trimmedString(payload.severity, 16).toLowerCase();
  if (!["error", "exception", "crash"].includes(severity)) {
    throw new HttpsError("invalid-argument", "severity must be error, exception, or crash.");
  }
  const dateUtc = utcDateKey();
  const usageRef = db.doc(`schools/${payload.schoolId}/students/${payload.studentId}/clientDiagnosticUsageDaily/${dateUtc}`);
  const accepted = await db.runTransaction(async transaction => {
    const snapshot = await transaction.get(usageRef);
    const count = Math.max(0, finiteInteger(snapshot.data()?.count));
    if (count >= clientDiagnosticDailyLimit) return false;
    transaction.set(usageRef, {
      dateUtc,
      count: count + 1,
      limit: clientDiagnosticDailyLimit,
      updatedAt: FieldValue.serverTimestamp()
    }, { merge: true });
    return true;
  });
  if (!accepted) return { accepted: false, reason: "daily_diagnostic_limit" };

  const id = randomId();
  const studentName = trimmedString(studentSnapshot.data()?.name, 120);
  await writeStudentRecord(payload.schoolId, payload.studentId, "clientDiagnostics", id, {
    diagnosticId: id,
    severity,
    fingerprint: safeDiagnosticToken(payload.fingerprint, 96),
    category: safeDiagnosticToken(payload.category, 64),
    scene: safeDiagnosticToken(payload.scene, 96),
    build: safeDiagnosticToken(payload.build, 96),
    platform: safeDiagnosticToken(payload.platform, 96),
    message: redactKnownStudentText(redactDiagnosticText(payload.message, 700), studentName),
    stack: redactKnownStudentText(redactDiagnosticText(payload.stack, 2400), studentName),
    createdAtUtc: new Date().toISOString()
  });
  await db.doc(`schools/${payload.schoolId}/clientDiagnosticOperationsDaily/${dateUtc}`).set({
    dateUtc,
    reportCount: FieldValue.increment(1),
    crashCount: FieldValue.increment(severity === "crash" ? 1 : 0),
    exceptionCount: FieldValue.increment(severity === "exception" ? 1 : 0),
    errorCount: FieldValue.increment(severity === "error" ? 1 : 0),
    updatedAt: FieldValue.serverTimestamp()
  }, { merge: true });
  return { accepted: true, id };
});

/**
 * Deletes bounded, already-redacted Buddy records on a predictable schedule.
 * It intentionally leaves curriculum progress intact and never reads learner text
 * into logs.  The job is limited per collection/run so it is safe to retry.
 */

export const purgeExpiredBuddyRecords = onSchedule(
  { schedule: "every day 03:17", timeZone: "Etc/UTC", timeoutSeconds: 540 },
  async () => {
    const conversationCutoff = new Date(Date.now() - buddyConversationRetentionDays * 86400000).toISOString();
    const usageCutoff = new Date(Date.now() - buddyUsageRetentionDays * 86400000).toISOString();
    const deletedConversationTurns = await deleteExpiredCollectionGroup("buddyConversationTurns", "createdAtUtc", conversationCutoff);
    const deletedVoiceSessions = await deleteExpiredCollectionGroup("buddyVoiceSessions", "expiresAtUtc", conversationCutoff);
    const deletedVoiceRoomBindings = await deleteExpiredCollectionGroup("buddyVoiceRoomBindings", "expiresAtUtc", conversationCutoff);
    const deletedLearningDiagnostics = await deleteExpiredCollectionGroup("buddyLearningAttempts", "createdAtUtc", conversationCutoff, "sourceRecordType", "buddy_conversation");
    const deletedStudentUsage = await deleteExpiredCollectionGroup("buddyUsageDaily", "dateUtc", usageCutoff);
    const deletedStudentSttUsage = await deleteExpiredCollectionGroup("buddySttUsageDaily", "dateUtc", usageCutoff);
    const deletedStudentVoiceUsage = await deleteExpiredCollectionGroup("buddyVoiceUsageDaily", "dateUtc", usageCutoff);
    const pronunciationUsageCutoff = new Date(Date.now() - 45 * 86400000).toISOString();
    const deletedPronunciationUsage = await deleteExpiredCollectionGroup(
      "pronunciationUsageDaily",
      "dateUtc",
      pronunciationUsageCutoff
    );
    const deletedAggregationReceipts = await deleteExpiredCollectionGroup(
      "buddyAggregationReceipts",
      "createdAtUtc",
      conversationCutoff
    );
    const diagnosticCutoff = new Date(Date.now() - clientDiagnosticRetentionDays * 86400000).toISOString();
    const deletedClientDiagnostics = await deleteExpiredCollectionGroup("clientDiagnostics", "createdAtUtc", diagnosticCutoff);
    const deletedDiagnosticUsage = await deleteExpiredCollectionGroup("clientDiagnosticUsageDaily", "dateUtc", diagnosticCutoff);
    const handwritingCutoff = new Date().toISOString();
    const scrubbedRejectedHandwriting = await scrubExpiredHandwritingEvidence("letterAttempts", handwritingCutoff);
    const scrubbedAcceptedHandwriting = await scrubExpiredHandwritingEvidence("acceptedHandwritingTemplates", handwritingCutoff);
    console.info("[BuddyOps]", JSON.stringify({
      event: "buddy_retention_purge",
      deletedConversationTurns,
      deletedVoiceSessions,
      deletedVoiceRoomBindings,
      deletedLearningDiagnostics,
      deletedStudentUsage,
      deletedStudentSttUsage,
      deletedStudentVoiceUsage,
      deletedPronunciationUsage,
      deletedAggregationReceipts,
      deletedClientDiagnostics,
      deletedDiagnosticUsage,
      scrubbedRejectedHandwriting,
      scrubbedAcceptedHandwriting
    }));
  }
);

/** Emits de-identified Cloud Logging alarms with a one-hour per-school/category cooldown. */

export const monitorOperationsHealth = onSchedule(
  { schedule: "every 30 minutes", timeZone: "Etc/UTC", timeoutSeconds: 300 },
  async () => {
    const schools = await db.collection("schools").limit(200).get();
    const dateUtc = utcDateKey();
    for (const school of schools.docs) {
      const schoolId = school.id;
      const [buddy, pronunciation, diagnostics, cost] = await Promise.all([
        db.doc(`schools/${schoolId}/buddyOperationsDaily/${dateUtc}`).get(),
        db.doc(`schools/${schoolId}/pronunciationOperationsDaily/${dateUtc}`).get(),
        db.doc(`schools/${schoolId}/clientDiagnosticOperationsDaily/${dateUtc}`).get(),
        db.doc(`schools/${schoolId}/aiCostBudgetDaily/${dateUtc}`).get()
      ]);
      const buddyData = buddy.data() ?? {};
      const buddyRequests = Math.max(0, finiteInteger(buddyData.requestCount));
      const buddyAverageLatency = buddyRequests > 0 ? finiteNumber(buddyData.totalLatencyMs) / buddyRequests : 0;
      if (buddyAverageLatency >= buddyLatencyAlertMs)
        await emitOperationalAlert(schoolId, dateUtc, "buddy_latency", { averageLatencyMs: Math.round(buddyAverageLatency), thresholdMs: buddyLatencyAlertMs });

      const pronunciationData = pronunciation.data() ?? {};
      const pronunciationRequests = Math.max(0, finiteInteger(pronunciationData.requestCount));
      const pronunciationAverageLatency = pronunciationRequests > 0 ? finiteNumber(pronunciationData.totalLatencyMs) / pronunciationRequests : 0;
      if (pronunciationAverageLatency >= pronunciationLatencyAlertMs)
        await emitOperationalAlert(schoolId, dateUtc, "pronunciation_latency", { averageLatencyMs: Math.round(pronunciationAverageLatency), thresholdMs: pronunciationLatencyAlertMs });

      const crashes = Math.max(0, finiteInteger(diagnostics.data()?.crashCount));
      if (crashes >= crashDailyAlertCount)
        await emitOperationalAlert(schoolId, dateUtc, "crash_spike", { crashCount: crashes, threshold: crashDailyAlertCount });

      const costData = cost.data() ?? {};
      const reservedCost = Math.max(0, finiteNumber(costData.reservedCostMicroUsd));
      const costLimit = Math.max(1, finiteNumber(costData.costLimitMicroUsd) || schoolDailyAiCostLimitMicroUsd);
      const usedPercent = reservedCost / costLimit * 100;
      if (usedPercent >= aiCostWarningPercent)
        await emitOperationalAlert(schoolId, dateUtc, "ai_cost_budget", { usedPercent: Math.round(usedPercent), warningPercent: aiCostWarningPercent });
    }
  }
);

/** Safety net for uploads whose callable never arrived; normal analysis deletes audio immediately. */

export const purgeOrphanedPronunciationAudio = onSchedule(
  { schedule: "every day 02:37", timeZone: "Etc/UTC", timeoutSeconds: 540 },
  async () => {
    const bucket = storage.bucket();
    const cutoff = Date.now() - pronunciationOrphanAudioMaxAgeHours * 60 * 60 * 1000;
    let pageToken: string | undefined;
    let deleted = 0;
    do {
      const [files, nextQuery] = await bucket.getFiles({ prefix: "schools/", maxResults: 500, pageToken });
      for (const file of files) {
        if (deleted >= 1000) break;
        if (!file.name.includes("/pronunciationAudio/")) continue;
        const created = Date.parse(String(file.metadata.timeCreated ?? ""));
        if (Number.isFinite(created) && created < cutoff) {
          await file.delete({ ignoreNotFound: true });
          deleted++;
        }
      }
      pageToken = deleted >= 1000 ? undefined : nextQuery?.pageToken;
    } while (pageToken);
    console.info("[Privacy]", JSON.stringify({ event: "orphaned_pronunciation_audio_purge", deleted }));
  }
);
