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
import { db } from "./runtime.js";
import { summarizeBuddyConcepts, updateBuddyConceptAggregate, updateBuddyConceptDiagnostics } from "./buddyLearningState.js";
import { cloneRecord, finiteInteger, normalizedAggregateKey, normalizedStringArray } from "./sharedUtils.js";

export const aggregateBuddyLearningAttempt = onDocumentCreated(
  "schools/{schoolId}/students/{studentId}/buddyLearningAttempts/{eventId}",
  async (event) => {
    const attempt = event.data?.data() as Record<string, unknown> | undefined;
    if (!attempt) {
      return;
    }

    const schoolId = event.params.schoolId;
    const studentId = event.params.studentId;
    const stateRef = db.doc(`schools/${schoolId}/students/${studentId}/buddyLearnerState/current`);
    const receiptRef = db.doc(
      `schools/${schoolId}/students/${studentId}/buddyAggregationReceipts/${event.params.eventId}`
    );
    await db.runTransaction(async transaction => {
      const [snapshot, receiptSnapshot] = await Promise.all([
        transaction.get(stateRef),
        transaction.get(receiptRef)
      ]);
      if (receiptSnapshot.exists)
        return;

      const existing = snapshot.exists ? snapshot.data() ?? {} : {};
      const concepts = cloneRecord(existing.concepts);
      const countsTowardMastery = attempt.countsTowardMastery !== false;
      const conceptId = normalizedAggregateKey(attempt.conceptId, "Unscoped");

      if (countsTowardMastery) {
        concepts[conceptId] = updateBuddyConceptAggregate(
          cloneRecord(concepts[conceptId]),
          attempt,
          conceptId
        );
      } else if (normalizedStringArray(attempt.errorTags, 20).length > 0) {
        concepts[conceptId] = updateBuddyConceptDiagnostics(
          cloneRecord(concepts[conceptId]),
          attempt,
          conceptId
        );
      }

      const sourceEventCount = finiteInteger(existing.sourceEventCount) + 1;
      const totalHints = finiteInteger(existing.totalHints) + Math.max(0, finiteInteger(attempt.hintCount));
      const correctAttemptCount = finiteInteger(existing.correctAttemptCount) +
        (countsTowardMastery && attempt.correct === true ? 1 : 0);
      const independentCorrectAttemptCount = finiteInteger(existing.independentCorrectAttemptCount) +
        (countsTowardMastery && attempt.completedIndependently === true ? 1 : 0);
      const summaries = summarizeBuddyConcepts(concepts);

      transaction.create(receiptRef, {
        eventId: event.params.eventId,
        schoolId,
        studentId,
        createdAtUtc: new Date().toISOString(),
        createdAt: FieldValue.serverTimestamp()
      });
      transaction.set(stateRef, {
        schemaVersion: 1,
        studentId,
        schoolId,
        sourceEventCount,
        correctAttemptCount,
        independentCorrectAttemptCount,
        totalHints,
        concepts,
        strengthConceptIds: summaries.strengthConceptIds,
        needConceptIds: summaries.needConceptIds,
        recurringErrorTags: summaries.recurringErrorTags,
        supportBand: summaries.supportBand,
        recommendedEnglishRatio: summaries.recommendedEnglishRatio,
        lastEventId: String(attempt.eventId ?? event.params.eventId),
        lastEventAtUtc: String(attempt.createdAtUtc ?? ""),
        updatedAt: FieldValue.serverTimestamp()
      }, { merge: true });
    });
  }
);
