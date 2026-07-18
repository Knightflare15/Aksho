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
import { AnalysisJob, AzurePhoneme, AzurePronunciationJson, PhoneticSegmentRecord, PhoneticSegmentStatus, db } from "./runtime.js";
import { assertFirestorePathSegment } from "./sharedUtils.js";

export async function settlePronunciationJobIfOwned(
  jobRef: DocumentReference,
  leaseOwner: string,
  payload: Record<string, unknown>,
) {
  return db.runTransaction(async transaction => {
    const snapshot = await transaction.get(jobRef);
    if (!snapshot.exists || snapshot.data()?.processingLeaseOwner !== leaseOwner) return false;
    transaction.set(jobRef, {
      ...payload,
      processingLeaseOwner: FieldValue.delete(),
      processingLeaseExpiresAtEpochMs: FieldValue.delete(),
      processingFinishedAtUtc: new Date().toISOString(),
      updatedAt: FieldValue.serverTimestamp(),
    }, { merge: true });
    return true;
  });
}

export function splitStoragePath(storagePath: string) {
  if (!storagePath.startsWith("gs://")) {
    throw new Error("audioStoragePath must be a gs:// bucket path.");
  }

  const withoutScheme = storagePath.slice("gs://".length);
  const slashIndex = withoutScheme.indexOf("/");
  if (slashIndex <= 0 || slashIndex >= withoutScheme.length - 1) {
    throw new Error("audioStoragePath must include a bucket and object name.");
  }

  return {
    bucketName: withoutScheme.slice(0, slashIndex),
    objectName: withoutScheme.slice(slashIndex + 1)
  };
}

export function assertOwnedPronunciationStorageObject(objectName: string, schoolId: string, studentId: string) {
  assertFirestorePathSegment(schoolId, "schoolId");
  assertFirestorePathSegment(studentId, "studentId");
  const expectedPrefix = `schools/${schoolId}/students/${studentId}/pronunciationAudio/`;
  const fileName = objectName.startsWith(expectedPrefix) ? objectName.slice(expectedPrefix.length) : "";
  if (!fileName || fileName.includes("/") || !/^[a-zA-Z0-9_-]+\.wav$/i.test(fileName)) {
    throw new Error("audioStoragePath must reference this student's pronunciationAudio WAV object.");
  }
}

export function buildAzurePronunciationInsight(job: AnalysisJob, azure: AzurePronunciationJson) {
  const best = azure.NBest?.[0] ?? {};
  const assessment = normalizeAzurePronunciationAssessment(best);
  const word = best.Words?.[0];
  const wordAssessment = normalizeAzurePronunciationAssessment(word);
  const targetWord = normalizeWord(job.targetText ?? word?.Word ?? "");
  const rawRecognizedText = azure.DisplayText ?? best.Display ?? best.Lexical ?? "";
  const phonemes = word?.Phonemes ?? [];
  const segments = phonemes.map((phoneme, index) => buildAzureSegment(phoneme, index));
  const focusSegment = segments.find((segment) => segment.status !== "Matched") ?? segments[0] ?? null;
  const score = clamp01((assessment.PronScore ?? assessment.AccuracyScore ?? wordAssessment.AccuracyScore ?? 0) / 100);
  const phonemeAlignment = segments.map((segment) => ({
    expected: segment.spelling,
    observed: segment.heardSound,
    status: segment.status === "Matched" ? "matched" : segment.status === "Missing" ? "missing" : "substituted",
    confidence: segment.confidence
  }));
  const phonemeIssues = phonemeAlignment.filter((item) => item.status !== "matched");
  const syllableBeats = (word?.Syllables ?? [])
    .map((syllable) => syllable.Syllable ?? "")
    .filter((value) => value.trim().length > 0);

  return {
    providerName: "Azure Pronunciation Assessment",
    targetWord,
    confirmedWord: normalizeWord(best.Lexical ?? word?.Word ?? ""),
    rawRecognizedText,
    voskConfirmedWord: false,
    attemptedTarget: score > 0 || segments.length > 0,
    score,
    modelConfidence: clamp01((assessment.AccuracyScore ?? 0) / 100),
    hintKey: pickHintKey(segments, score),
    message: buildAzureMessage(score, focusSegment),
    focusSegment,
    segments,
    syllableBeats,
    expectedPhonemes: segments.map((segment) => segment.spelling).filter(Boolean),
    observedPhonemes: segments.map((segment) => segment.heardSound).filter(Boolean),
    phonemeIssues,
    phonemeAlignment
  };
}

export function countingTargetText(value: unknown) {
  const number = typeof value === "number" ? value : Number(value);
  const words = ["", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten"];
  return Number.isInteger(number) && number >= 1 && number < words.length
    ? words[number]
    : String(value ?? "");
}

export function buildAzureSegment(phoneme: AzurePhoneme, index: number): PhoneticSegmentRecord {
  const assessment = normalizeAzurePronunciationAssessment(phoneme);
  const expected = phoneme.Phoneme ?? "";
  const heard = assessment.NBestPhonemes?.[0]?.Phoneme ?? "";
  const confidence = clamp01((assessment.AccuracyScore ?? 0) / 100);
  return {
    spelling: expected,
    friendlySound: expected,
    heardSound: heard,
    beatIndex: index,
    status: statusForConfidence(confidence),
    confidence
  };
}

export function statusForConfidence(confidence: number): PhoneticSegmentStatus {
  if (confidence >= 0.75) {
    return "Matched";
  }
  if (confidence >= 0.45) {
    return "NeedsPractice";
  }
  return "Missing";
}

export function pickHintKey(segments: PhoneticSegmentRecord[], score: number) {
  if (score >= 0.84 && segments.every((segment) => segment.status === "Matched")) {
    return "GreatTry";
  }
  const weakIndex = segments.findIndex((segment) => segment.status !== "Matched");
  if (weakIndex < 0) {
    return score >= 0.65 ? "GreatTry" : "TryAgain";
  }
  if (segments.filter((segment) => segment.status !== "Matched").length > 1) {
    return "TryAllBeats";
  }
  if (weakIndex === 0) {
    return "TryFirstSound";
  }
  if (weakIndex === segments.length - 1) {
    return "TryLastSound";
  }
  return score < 0.35 ? "TrySlower" : "TryAgain";
}

export function buildAzureMessage(score: number, focus: PhoneticSegmentRecord | null) {
  if (score >= 0.84 && (!focus || focus.status === "Matched")) {
    return "Azure pronunciation assessment matched the expected sounds.";
  }
  if (focus && focus.spelling) {
    return `Azure pronunciation assessment suggests practicing ${focus.spelling}.`;
  }
  return "Azure pronunciation assessment completed.";
}

export async function updateSourceRecord(job: AnalysisJob, payload: Record<string, unknown>) {
  if (!job.schoolId || !job.studentId || !job.sourceCollection || !job.sourceRecordId) {
    return;
  }

  await db.doc(`schools/${job.schoolId}/students/${job.studentId}/${job.sourceCollection}/${job.sourceRecordId}`).set(payload, { merge: true });
}

export function normalizeWord(value: string) {
  return Array.from(value ?? "")
    .filter((char) => /[a-z]/i.test(char))
    .join("")
    .toUpperCase();
}

export function clamp01(value: number) {
  if (!Number.isFinite(value)) {
    return 0;
  }
  return Math.max(0, Math.min(1, value));
}

export function shortError(error: unknown) {
  const message = error instanceof Error ? error.message : String(error);
  return message.length > 500 ? `${message.slice(0, 497)}...` : message;
}
