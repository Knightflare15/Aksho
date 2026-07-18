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
import { StudentPayload, StudentPrivacySettings, auth, azurePronunciationEnabled, azurePronunciationUsdPerAudioHour, db, pronunciationDailyAudioSecondsLimit, pronunciationDailyReviewLimit, pronunciationFreeTierDailyAudioSecondsLimit, pronunciationFreeTierDailyReviewLimit, pronunciationPremiumTierDailyAudioSecondsLimit, pronunciationPremiumTierDailyReviewLimit, schoolDailyAiCostLimitMicroUsd, storage } from "./runtime.js";
import { resolveServiceTier } from "./buddyRoutingHelpers.js";
import { trimmedString } from "./inputHelpers.js";
import { defaultStudentPrivacySettings, privacyPermissionGranted, readStudentPrivacy } from "./privacyHelpers.js";
import { assertOwnedPronunciationStorageObject, splitStoragePath } from "./pronunciationProcessingHelpers.js";
import { assertFirestorePathSegment, currentWeekId, finiteInteger, finiteNumber, normalizedStringArray, uniqueStrings } from "./sharedUtils.js";

export async function writeStudentRecord(
  schoolId: string,
  studentId: string,
  collectionName: string,
  id: string,
  payload: Record<string, unknown>,
  mutable = false
) {
  assertFirestorePathSegment(schoolId, "schoolId");
  assertFirestorePathSegment(studentId, "studentId");
  assertFirestorePathSegment(collectionName, "collectionName");
  assertFirestorePathSegment(id, "recordId");
  const reference = db.doc(`schools/${schoolId}/students/${studentId}/${collectionName}/${id}`);
  const studentSnapshot = await db.doc(`schools/${schoolId}/students/${studentId}`).get();
  const privacy = studentSnapshot.exists ? readStudentPrivacy(studentSnapshot.data() ?? {}) : defaultStudentPrivacySettings();
  const requiredPermission: keyof StudentPrivacySettings = collectionName.startsWith("buddy")
    ? "buddyAllowed"
    : collectionName === "clientDiagnostics" ? "diagnosticsAllowed" : "gameplayAnalyticsAllowed";
  if (!privacyPermissionGranted(privacy, requiredPermission))
    return;
  let safePayload = payload;
  if ((collectionName === "letterAttempts" || collectionName === "acceptedHandwritingTemplates") && Array.isArray(payload.points)) {
    const mayRetain = privacyPermissionGranted(privacy, "handwritingEvidenceAllowed");
    if (!mayRetain) {
      const { points: _discardedPoints, strokes: _discardedStrokes, ...derivedOnly } = payload;
      safePayload = {
        ...derivedOnly,
        rawStrokeCaptured: false,
        rawStrokePointCount: 0,
        handwritingEvidenceRetention: "derived_metrics_only_no_parental_consent"
      };
    }
  }
  const record = {
    ...safePayload,
    createdAt: safePayload.createdAt ?? FieldValue.serverTimestamp(),
    receivedAt: FieldValue.serverTimestamp()
  };
  if (mutable) {
    await reference.set(record, { merge: true });
    return;
  }

  try {
    await reference.create(record);
  } catch (error) {
    const code = typeof error === "object" && error !== null && "code" in error
      ? String((error as { code?: unknown }).code)
      : "";
    if (code === "6" || code === "already-exists")
      return;
    throw error;
  }
}

export async function maybeCreateAnalysisJob(
  payload: StudentPayload,
  analysisKind: "pronunciation" | "handwriting",
  sourceCollection: string,
  sourceRecordId: string,
  targetText: string
) {
  if (payload.serverAnalysisStatus !== "pending" || typeof payload.serverAnalysisJobId !== "string") {
    return;
  }

  const jobId = payload.serverAnalysisJobId;
  assertFirestorePathSegment(jobId, "serverAnalysisJobId");
  if (analysisKind !== "pronunciation") {
    await db.doc(`schools/${payload.schoolId}/students/${payload.studentId}/${sourceCollection}/${sourceRecordId}`).set({
      serverAnalysisStatus: "not_configured",
      serverAnalysisReason: "handwriting_server_analyzer_unavailable",
      serverAnalysisUpdatedAt: FieldValue.serverTimestamp()
    }, { merge: true });
    return;
  }

  if (!azurePronunciationEnabled) {
    await db.doc(`schools/${payload.schoolId}/students/${payload.studentId}/${sourceCollection}/${sourceRecordId}`).set({
      serverAnalysisStatus: "not_configured",
      serverAnalysisReason: "azure_pronunciation_disabled",
      serverAnalysisUpdatedAt: FieldValue.serverTimestamp()
    }, { merge: true });
    const disabledAudio = splitStoragePath(trimmedString(payload.audioStoragePath, 1024));
    assertOwnedPronunciationStorageObject(disabledAudio.objectName, payload.schoolId, payload.studentId);
    await storage.bucket(disabledAudio.bucketName).file(disabledAudio.objectName)
      .delete({ ignoreNotFound: true })
      .catch(error => console.warn("[Pronunciation] Could not delete disabled-provider audio", { sourceRecordId, error }));
    return;
  }

  const audioStoragePath = trimmedString(payload.audioStoragePath, 1024);
  let storageObject: { bucketName: string; objectName: string } | null = null;
  if (analysisKind === "pronunciation") {
    storageObject = splitStoragePath(audioStoragePath);
    assertOwnedPronunciationStorageObject(storageObject.objectName, payload.schoolId, payload.studentId);
  }

  const studentBase = `schools/${payload.schoolId}/students/${payload.studentId}`;
  const jobRef = db.doc(`${studentBase}/analysisJobs/${jobId}`);
  const sourceRef = db.doc(`${studentBase}/${sourceCollection}/${sourceRecordId}`);
  const dateUtc = new Date().toISOString().slice(0, 10);
  const usageRef = db.doc(`${studentBase}/pronunciationUsageDaily/${dateUtc}`);
  const studentRef = db.doc(studentBase);
  const audioDurationSeconds = Math.max(0, Math.min(30, finiteNumber(payload.audioDurationSeconds)));
  const reservedCostMicroUsd = Math.max(0, Math.round(
    audioDurationSeconds / 3600 * azurePronunciationUsdPerAudioHour * 1_000_000));
  const schoolCostRef = db.doc(`schools/${payload.schoolId}/aiCostBudgetDaily/${dateUtc}`);
  let analysisAccepted = true;

  await db.runTransaction(async transaction => {
    const [jobSnapshot, usageSnapshot, studentSnapshot, schoolCostSnapshot] = await Promise.all([
      transaction.get(jobRef),
      transaction.get(usageRef),
      transaction.get(studentRef),
      transaction.get(schoolCostRef)
    ]);
    if (jobSnapshot.exists)
      return;

    const usage = usageSnapshot.data() ?? {};
    const student = studentSnapshot.data() ?? {};
    const privacy = readStudentPrivacy(student);
    if (!studentSnapshot.exists ||
        !privacyPermissionGranted(privacy, "gameplayAnalyticsAllowed") ||
        !privacyPermissionGranted(privacy, "audioProcessingAllowed")) {
      analysisAccepted = false;
      if (studentSnapshot.exists && privacyPermissionGranted(privacy, "gameplayAnalyticsAllowed")) {
        transaction.set(sourceRef, {
          serverAnalysisStatus: "skipped_consent",
          serverAnalysisReason: "parental_consent_required",
          serverAnalysisUpdatedAt: FieldValue.serverTimestamp()
        }, { merge: true });
      }
      return;
    }
    const tier = resolveServiceTier(student);
    const reviewLimit = tier === "premium"
      ? pronunciationPremiumTierDailyReviewLimit
      : tier === "standard" ? pronunciationDailyReviewLimit : pronunciationFreeTierDailyReviewLimit;
    const audioSecondsLimit = tier === "premium"
      ? pronunciationPremiumTierDailyAudioSecondsLimit
      : tier === "standard" ? pronunciationDailyAudioSecondsLimit : pronunciationFreeTierDailyAudioSecondsLimit;
    const reviewCount = Math.max(0, finiteInteger(usage.reviewCount));
    const audioSeconds = Math.max(0, finiteNumber(usage.audioSeconds));
    const schoolReservedCost = Math.max(0, finiteInteger(schoolCostSnapshot.data()?.reservedCostMicroUsd));
    if (schoolReservedCost + reservedCostMicroUsd > schoolDailyAiCostLimitMicroUsd) {
      analysisAccepted = false;
      transaction.set(sourceRef, {
        serverAnalysisStatus: "skipped_budget",
        serverAnalysisReason: "school_daily_ai_cost_limit",
        serverAnalysisUpdatedAt: FieldValue.serverTimestamp()
      }, { merge: true });
      return;
    }
    if (reviewCount >= reviewLimit || audioSeconds + audioDurationSeconds > audioSecondsLimit) {
      analysisAccepted = false;
      transaction.set(sourceRef, {
        serverAnalysisStatus: "skipped_budget",
        serverAnalysisReason: "daily_pronunciation_budget",
        serverAnalysisUpdatedAt: FieldValue.serverTimestamp()
      }, { merge: true });
      return;
    }

    transaction.set(usageRef, {
      schoolId: payload.schoolId,
      studentId: payload.studentId,
      dateUtc,
      reviewCount: reviewCount + 1,
      audioSeconds: audioSeconds + audioDurationSeconds,
      tier,
      reviewLimit,
      audioSecondsLimit,
      updatedAt: FieldValue.serverTimestamp()
    }, { merge: true });
    transaction.set(jobRef, {
      jobId,
      studentId: payload.studentId,
      classId: trimmedString(payload.classId, 160),
      schoolId: payload.schoolId,
      missionId: trimmedString(payload.missionId, 160),
      analysisKind,
      status: "pending",
      sourceCollection,
      sourceRecordId,
      targetText,
      audioStoragePath,
      audioDurationSeconds,
      onDeviceAnalysisProvider: payload.onDeviceAnalysisProvider ?? "",
      analysisMode: payload.analysisMode ?? "HybridServerAssist",
      createdAtUtc: payload.createdAtUtc ?? new Date().toISOString(),
      createdAt: FieldValue.serverTimestamp(),
      updatedAt: FieldValue.serverTimestamp()
    }, { merge: true });
  });

  if (!analysisAccepted && storageObject != null) {
    await storage.bucket(storageObject.bucketName).file(storageObject.objectName)
      .delete({ ignoreNotFound: true })
      .catch(error => console.warn("[Pronunciation] Could not delete budget-skipped audio", { jobId, error }));
  }
}

export function spokenPhraseTargetText(payload: Record<string, unknown>) {
  return trimmedString(payload.targetText, 240)
    || trimmedString(payload.fullResponse, 240)
    || trimmedString(payload.canonicalPhrase, 240)
    || trimmedString(payload.submittedResponse, 240)
    || trimmedString(payload.transcript, 240);
}

export function grammarBattleTargetText(payload: Record<string, unknown>) {
  return trimmedString(payload.targetText, 240)
    || trimmedString(payload.canonicalPhrase, 240)
    || trimmedString(payload.playerPhrase, 240);
}

export function gymAttemptTargetText(payload: Record<string, unknown>) {
  return trimmedString(payload.targetText, 240)
    || trimmedString(payload.fullResponse, 240)
    || trimmedString(payload.submittedResponse, 240);
}

export async function upsertParentSummary(payload: Record<string, unknown>, sessionId: string) {
  const schoolId = String(payload.schoolId);
  const studentId = String(payload.studentId);
  const summaryId = currentWeekId();
  const summaryRef = db.doc(`schools/${schoolId}/students/${studentId}/parentSummaries/${summaryId}`);
  await db.runTransaction(async transaction => {
    const snapshot = await transaction.get(summaryRef);
    const current = snapshot.data() ?? {};
    const includedSessionIds = normalizedStringArray(current.includedSessionIds, 200);
    if (includedSessionIds.includes(sessionId))
      return;

    const previousCount = Math.max(0, finiteInteger(current.sessionCount));
    const sessionCount = previousCount + 1;
    const durationSeconds = Math.max(0, finiteNumber(payload.actualDurationSeconds));
    const totalDurationSeconds = Math.max(0, finiteNumber(current.totalDurationSeconds)) + durationSeconds;
    const averageConfidence =
      (finiteNumber(current.averageConfidence) * previousCount + finiteNumber(payload.averageConfidence)) / sessionCount;
    const averageAttemptsPerLetter =
      (finiteNumber(current.averageAttemptsPerLetter) * previousCount + finiteNumber(payload.averageAttemptsPerLetter)) / sessionCount;
    const lettersPracticed = uniqueStrings([
      ...normalizedStringArray(current.lettersPracticed, 100),
      ...normalizedStringArray(payload.lettersPracticed, 100)
    ]).slice(0, 100);
    const wordsPracticed = uniqueStrings([
      ...normalizedStringArray(current.wordsPracticed, 200),
      ...normalizedStringArray(payload.wordsPracticed, 200)
    ]).slice(0, 200);

    transaction.set(summaryRef, {
      schoolId,
      studentId,
      classId: payload.classId,
      weekStart: summaryId,
      sessionCount,
      includedSessionIds: [...includedSessionIds, sessionId].slice(-200),
      totalDurationSeconds,
      minutesPracticed: Math.round(totalDurationSeconds / 60),
      lettersPracticed,
      wordsPracticed,
      bestLetter: trimmedString(current.bestLetter, 8) || lettersPracticed[0] || "",
      needsPracticeLetter: trimmedString(current.needsPracticeLetter, 8) || lettersPracticed[1] || "",
      averageConfidence,
      averageAttemptsPerLetter,
      trendLabel: `Weekly total across ${sessionCount} mission${sessionCount === 1 ? "" : "s"}`,
      updatedAt: FieldValue.serverTimestamp()
    }, { merge: true });
  });
}

export async function writeAudit(schoolId: string, actorUid: string, action: string, targetPath: string) {
  await db.collection("auditEvents").add({
    schoolId,
    actorUid,
    action,
    targetPath,
    createdAt: FieldValue.serverTimestamp()
  });
}

export async function getOrCreateTeacherUser(email: string, displayName: string, password: string, schoolId: string) {
  try {
    const existing = await auth.getUserByEmail(email);
    const existingProfile = await db.doc(`users/${existing.uid}`).get();
    const profile = existingProfile.data() ?? {};
    if (!existingProfile.exists || String(profile.schoolId ?? "") !== schoolId || String(profile.role ?? "") !== "teacher") {
      throw new HttpsError("already-exists", "That email already belongs to another account and cannot be reassigned.");
    }
    await auth.updateUser(existing.uid, {
      displayName,
      password,
      disabled: false
    });
    return auth.getUser(existing.uid);
  } catch (error) {
    const code = typeof error === "object" && error !== null && "code" in error ? String((error as { code?: unknown }).code) : "";
    if (code !== "auth/user-not-found") {
      throw error;
    }
    return auth.createUser({
      email,
      password,
      displayName,
      emailVerified: false,
      disabled: false
    });
  }
}
