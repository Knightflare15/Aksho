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
import { AnalysisJob, AzurePronunciationJson, azurePronunciationEnabled, azureSpeechKey, azureSpeechLanguage, azureSpeechRegion, db, pronunciationFunctionConcurrency, pronunciationFunctionMaxInstances, pronunciationFunctionRegion, pronunciationProcessingLeaseMs, pronunciationRestMaximumAttempts, pronunciationRestTimeoutMs, storage } from "./runtime.js";
import { readAzureSpeechKey } from "./buddyVoiceHelpers.js";
import { trimmedString } from "./inputHelpers.js";
import { recordPronunciationOperations } from "./operationsHelpers.js";
import { privacyPermissionGranted, readStudentPrivacy } from "./privacyHelpers.js";
import { assertOwnedPronunciationStorageObject, buildAzurePronunciationInsight, settlePronunciationJobIfOwned, shortError, splitStoragePath, updateSourceRecord } from "./pronunciationProcessingHelpers.js";
import { finiteInteger, finiteNumber, randomId } from "./sharedUtils.js";

export const dispatchAnalysisJob = onDocumentCreated(
  {
    document: "schools/{schoolId}/students/{studentId}/analysisJobs/{jobId}",
    region: pronunciationFunctionRegion,
    secrets: [azureSpeechKey],
    retry: true,
    timeoutSeconds: 75,
    memory: "256MiB",
    concurrency: pronunciationFunctionConcurrency,
    maxInstances: pronunciationFunctionMaxInstances,
  },
  async (event) => {
    const created = event.data;
    const job = created?.data() as AnalysisJob | undefined;
    if (!created || !job || job.analysisKind !== "pronunciation") return;

    const jobRef = created.ref;
    const leaseOwner = randomId();
    const claimStartedAt = Date.now();
    const minimumLeaseMs = pronunciationRestTimeoutMs * pronunciationRestMaximumAttempts + 30000;
    const leaseExpiresAtEpochMs = claimStartedAt + Math.max(pronunciationProcessingLeaseMs, minimumLeaseMs);
    const claimedJob = await db.runTransaction(async transaction => {
      const snapshot = await transaction.get(jobRef);
      if (!snapshot.exists) return null;
      const current = snapshot.data() as AnalysisJob & Record<string, unknown>;
      const status = trimmedString(current.status, 32);
      const currentLeaseExpiry = finiteNumber(current.processingLeaseExpiresAtEpochMs);
      if (status === "processing" && currentLeaseExpiry > claimStartedAt) {
        // Eventarc is at-least-once. Failing this duplicate delivery keeps it
        // retryable until the first owner completes or its lease expires.
        throw new Error("Pronunciation job is already held by an active processing lease.");
      }
      if (status !== "pending" && !(status === "processing" && currentLeaseExpiry <= claimStartedAt)) {
        return null;
      }
      transaction.set(jobRef, {
        status: "processing",
        providerName: "Azure Pronunciation Assessment",
        providerTransport: "short_audio_rest",
        processingLeaseOwner: leaseOwner,
        processingLeaseExpiresAtEpochMs: leaseExpiresAtEpochMs,
        processingAttemptCount: finiteInteger(current.processingAttemptCount) + 1,
        processingStartedAtUtc: new Date(claimStartedAt).toISOString(),
        updatedAt: FieldValue.serverTimestamp(),
      }, { merge: true });
      return current;
    });
    if (!claimedJob) return;

    const fullJob: AnalysisJob = {
      ...claimedJob,
      schoolId: event.params.schoolId,
      studentId: event.params.studentId,
      jobId: event.params.jobId,
    };
    let storageObject: { bucketName: string; objectName: string } | null = null;
    const startedAt = Date.now();
    let operationStatus = "failed";
    let mayDeleteAudio = false;

    try {
      if (!fullJob.audioStoragePath) {
        throw new Error("No audioStoragePath was attached to this pronunciation analysis job.");
      }
      if (!fullJob.targetText) {
        throw new Error("No targetText was attached to this pronunciation analysis job.");
      }
      if (fullJob.targetText.length > 240) {
        throw new Error("Pronunciation target text exceeds the 240 character limit.");
      }
      storageObject = splitStoragePath(fullJob.audioStoragePath);
      assertOwnedPronunciationStorageObject(
        storageObject.objectName,
        fullJob.schoolId ?? "",
        fullJob.studentId ?? "");

      if (!azurePronunciationEnabled) {
        mayDeleteAudio = await settlePronunciationJobIfOwned(jobRef, leaseOwner, {
          status: "disabled",
          reason: "azure_pronunciation_disabled",
        });
        if (mayDeleteAudio) {
          await updateSourceRecord(fullJob, {
            serverAnalysisStatus: "not_configured",
            serverAnalysisReason: "azure_pronunciation_disabled",
            updatedAt: FieldValue.serverTimestamp(),
          });
        }
        operationStatus = "disabled";
        return;
      }

      const studentSnapshot = await db.doc(
        `schools/${event.params.schoolId}/students/${event.params.studentId}`,
      ).get();
      const student = studentSnapshot.data() ?? {};
      const privacy = readStudentPrivacy(student);
      const processingAllowed = studentSnapshot.exists &&
        trimmedString(student.accountStatus, 32).toLowerCase() !== "deletion_pending" &&
        privacyPermissionGranted(privacy, "audioProcessingAllowed");
      if (!processingAllowed) {
        mayDeleteAudio = await settlePronunciationJobIfOwned(jobRef, leaseOwner, {
          status: "blocked",
          reason: "audio_processing_consent_unavailable",
        });
        if (mayDeleteAudio) {
          await updateSourceRecord(fullJob, {
            serverAnalysisStatus: "blocked",
            serverAnalysisReason: "audio_processing_consent_unavailable",
            updatedAt: FieldValue.serverTimestamp(),
          });
        }
        operationStatus = "blocked";
        return;
      }

      const configuredAzureSpeechKey = readAzureSpeechKey();
      if (!configuredAzureSpeechKey || !azureSpeechRegion) {
        throw new Error("Azure Speech is not configured. Set AZURE_SPEECH_KEY and AZURE_SPEECH_REGION.");
      }
      const [audioBytes] = await storage.bucket(storageObject.bucketName)
        .file(storageObject.objectName)
        .download();
      const azureJson = await assessPronunciationWithAzureRest(audioBytes, {
        subscriptionKey: configuredAzureSpeechKey,
        region: azureSpeechRegion,
        language: azureSpeechLanguage,
        referenceText: fullJob.targetText,
        timeoutMs: pronunciationRestTimeoutMs,
        maximumAttempts: pronunciationRestMaximumAttempts,
      }) as AzurePronunciationJson;
      const serverInsight = buildAzurePronunciationInsight(fullJob, azureJson);
      const result = {
        providerName: "Azure Pronunciation Assessment",
        providerTransport: "short_audio_rest",
        serverPronunciationInsight: serverInsight,
      };

      mayDeleteAudio = await settlePronunciationJobIfOwned(jobRef, leaseOwner, {
        status: "complete",
        providerName: "Azure Pronunciation Assessment",
        providerTransport: "short_audio_rest",
        result,
      });
      if (!mayDeleteAudio) {
        operationStatus = "lease_lost";
        return;
      }
      await updateSourceRecord(fullJob, {
        serverAnalysisStatus: "complete",
        serverPronunciationInsight: serverInsight,
        updatedAt: FieldValue.serverTimestamp(),
      });
      operationStatus = "complete";
    } catch (error) {
      const message = shortError(error);
      mayDeleteAudio = await settlePronunciationJobIfOwned(jobRef, leaseOwner, {
        status: "failed",
        error: message,
      });
      if (mayDeleteAudio) {
        await updateSourceRecord(fullJob, {
          serverAnalysisStatus: "failed",
          serverAnalysisError: message,
          updatedAt: FieldValue.serverTimestamp(),
        });
      } else {
        operationStatus = "lease_lost";
      }
    } finally {
      if (mayDeleteAudio && storageObject) {
        await storage.bucket(storageObject.bucketName).file(storageObject.objectName).delete({ ignoreNotFound: true }).catch(() => undefined);
      }
      await recordPronunciationOperations(fullJob, operationStatus, Date.now() - startedAt);
    }
  }
);
