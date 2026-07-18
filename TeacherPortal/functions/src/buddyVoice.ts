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
import { BuddyVoiceReservation, auth, buddySttMaximumTurnSeconds, buddyVoiceAgentName, buddyVoiceEnforceAppCheck, buddyVoiceFunctionRegion, buddyVoiceGroundingEnabled, buddyVoiceMaxConcurrentPerSchool, buddyVoiceMaxSessionSeconds, buddyVoiceRoomDepartureTimeoutSeconds, buddyVoiceRoomEmptyTimeoutSeconds, buddyVoiceTokenFunctionConcurrency, buddyVoiceTokenFunctionMaxInstances, db, livekitApiKey, livekitApiSecret, livekitUrl, sarvamApiKey } from "./runtime.js";
import { requireRole, requireStudentAudioPayload, requireStudentPayload } from "./authorizationHelpers.js";
import { estimateBuddyAudioSeconds, reserveBuddySttBudget, utcDateKey } from "./buddyBudgetHelpers.js";
import { composeBuddyGroundingContext, redactTaskAnswerText } from "./buddyContextComposer.js";
import { redactBuddyText } from "./buddyContextHelpers.js";
import { buddyTierVoiceSessionLimit, resolveServiceTier } from "./buddyRoutingHelpers.js";
import { transcribeWithSarvam } from "./buddySarvam.js";
import { normalizeBuddyLanguage } from "./buddySpeechHelpers.js";
import { closeActiveBuddyVoiceRoom, normalizeLiveKitServerUrl, readBuddyVoiceReservation, readLiveKitApiKey, readLiveKitApiSecret, readSarvamApiKey, releaseBuddyVoiceReservation } from "./buddyVoiceHelpers.js";
import { buddyZoneForAssistMode, requiredTrimmedString, trimmedString } from "./inputHelpers.js";
import { privacyPermissionGranted, readStudentPrivacy } from "./privacyHelpers.js";
import { shortError } from "./pronunciationProcessingHelpers.js";
import { assertFirestorePathSegment, finiteInteger, finiteNumber, normalizedRelationshipMemory, normalizedStringArray, randomId } from "./sharedUtils.js";

export const transcribeBuddySpeech = onCall(
  { secrets: [sarvamApiKey], timeoutSeconds: 30 },
  async (request) => {
    const caller = requireRole(request.auth, ["student", "parent"]);
    const payload = requireStudentAudioPayload(caller, request.data ?? {});
    const privacySnapshot = await db.doc(`schools/${payload.schoolId}/students/${payload.studentId}`).get();
    if (!privacySnapshot.exists) throw new HttpsError("not-found", "Student was not found.");
    const student = privacySnapshot.data() ?? {};
    const privacy = readStudentPrivacy(student);
    if (!privacyPermissionGranted(privacy, "buddyAllowed") || !privacyPermissionGranted(privacy, "audioProcessingAllowed")) {
      return {
        status: "blocked",
        fallbackReason: "parental_consent_required",
        transcript: "",
        provider: "",
        model: ""
      };
    }

    const apiKey = readSarvamApiKey();
    if (!apiKey) {
      return {
        status: "fallback",
        fallbackReason: "provider_unavailable",
        transcript: "",
        provider: "",
        model: ""
      };
    }

    const tier = resolveServiceTier(student);
    const audioSeconds = estimateBuddyAudioSeconds(payload.audioBase64);
    if (audioSeconds <= 0 || audioSeconds > buddySttMaximumTurnSeconds) {
      return {
        status: "fallback",
        fallbackReason: audioSeconds > buddySttMaximumTurnSeconds ? "audio_turn_too_long" : "invalid_audio_duration",
        transcript: "",
        provider: "",
        model: ""
      };
    }
    const sttBudget = await reserveBuddySttBudget(
      `schools/${payload.schoolId}/students/${payload.studentId}`,
      payload.schoolId,
      tier,
      audioSeconds);
    if (!sttBudget.allowed) {
      return {
        status: "fallback",
        fallbackReason: "daily_stt_limit",
        transcript: "",
        provider: "",
        model: "",
        remainingAudioSeconds: 0
      };
    }

    const startedAt = Date.now();
    const result = await transcribeWithSarvam(apiKey, payload);
    return {
      ...result,
      latencyMs: Math.max(0, Date.now() - startedAt),
      remainingAudioSeconds: Math.max(0, sttBudget.limitSeconds - sttBudget.usedSeconds)
    };
  }
);

/**
 * Issues one short-lived, room-scoped LiveKit token per authenticated learner.
 * The room name, participant identity, agent dispatch and lesson metadata are
 * all server-authored; the Unity client receives no LiveKit service secret.
 */

export const createBuddyVoiceSession = onCall(
  {
    secrets: [livekitApiKey, livekitApiSecret],
    region: buddyVoiceFunctionRegion,
    timeoutSeconds: 15,
    concurrency: buddyVoiceTokenFunctionConcurrency,
    maxInstances: buddyVoiceTokenFunctionMaxInstances,
    enforceAppCheck: buddyVoiceEnforceAppCheck,
  },
  async request => {
    const caller = requireRole(request.auth, ["student", "parent"]);
    const payload = requireStudentPayload(caller, request.data ?? {});
    const dialogueTaskId = requiredTrimmedString(payload.dialogueTaskId, "dialogueTaskId");
    const clientRequestId = requiredTrimmedString(payload.clientRequestId, "clientRequestId");
    const requestedTrigger = trimmedString(payload.trigger, 32).toLowerCase();
    const openingTrigger = requestedTrigger === "wrong_answer"
      ? "wrong_answer" as const
      : requestedTrigger === "word_meaning" ? "word_meaning" as const : "ask" as const;
    const openingLearnerAttempt = openingTrigger !== "ask"
      ? redactBuddyText(payload.learnerAttempt, 240)
      : "";
    if (!/^[a-zA-Z0-9_-]{8,96}$/.test(clientRequestId)) {
      throw new HttpsError("invalid-argument", "clientRequestId must be an opaque 8-96 character identifier.");
    }

    const configuredLiveKitUrl = normalizeLiveKitServerUrl(livekitUrl);
    const configuredApiKey = readLiveKitApiKey();
    const configuredApiSecret = readLiveKitApiSecret();
    if (!configuredLiveKitUrl || !configuredApiKey || !configuredApiSecret) {
      throw new HttpsError("failed-precondition", "Buddy voice is not configured.");
    }

    const studentBase = `schools/${payload.schoolId}/students/${payload.studentId}`;
    const [taskSnapshot, profileSnapshot, learnerStateSnapshot] = await Promise.all([
      db.doc(`gameContentDialogueTasks/${dialogueTaskId}`).get(),
      db.doc(`${studentBase}/buddyLearnerProfiles/current`).get(),
      db.doc(`${studentBase}/buddyLearnerState/current`).get(),
    ]);
    if (!taskSnapshot.exists) {
      throw new HttpsError("not-found", "Buddy voice is unavailable because this dialogue task has not been seeded.");
    }
    const task = taskSnapshot.data() ?? {};
    const canonicalZone = buddyZoneForAssistMode(String(task.assistMode ?? "Off"));
    if (canonicalZone === "Gym") {
      throw new HttpsError("failed-precondition", "Buddy voice is not available during Gym checks.");
    }
    const taskConceptId = trimmedString(task.conceptId, 96);
    const taskContractId = trimmedString(task.buddyContractId, 96);
    const shouldUseGrounding = buddyVoiceGroundingEnabled;
    const [grimoireSnapshot, scaffoldSnapshot, contractSnapshot] = await Promise.all([
      shouldUseGrounding && taskConceptId ? db.doc(`gameContentGrimoirePages/${taskConceptId}`).get() : Promise.resolve(null),
      shouldUseGrounding ? db.doc(`gameContentPracticeScaffolds/${dialogueTaskId}`).get() : Promise.resolve(null),
      shouldUseGrounding && taskContractId ? db.doc(`gameContentBuddyContracts/${taskContractId}`).get() : Promise.resolve(null),
    ]);

    const now = Date.now();
    const proposed: BuddyVoiceReservation = {
      voiceSessionId: randomId(),
      clientRequestId,
      dialogueTaskId,
      roomName: `buddy-${randomId()}`,
      participantIdentity: `learner-${randomBytes(10).toString("hex")}`,
      expiresAtEpochMs: now + buddyVoiceMaxSessionSeconds * 1000,
    };
    const capacityRef = db.doc(`schools/${payload.schoolId}/serviceLeases/buddyVoice`);
    const currentLeaseRef = db.doc(`${studentBase}/buddyVoiceLease/current`);
    const studentRef = db.doc(studentBase);
    const usageDateUtc = utcDateKey();
    const usageRef = db.doc(`${studentBase}/buddyVoiceUsageDaily/${usageDateUtc}`);

    const reservation = await db.runTransaction(async transaction => {
      const [studentSnapshot, capacitySnapshot, currentLeaseSnapshot, usageSnapshot] = await Promise.all([
        transaction.get(studentRef),
        transaction.get(capacityRef),
        transaction.get(currentLeaseRef),
        transaction.get(usageRef),
      ]);
      if (!studentSnapshot.exists) throw new HttpsError("not-found", "Student was not found.");
      const studentData = studentSnapshot.data() ?? {};
      if (trimmedString(studentData.accountStatus, 32).toLowerCase() === "deletion_pending") {
        throw new HttpsError("failed-precondition", "Buddy voice is unavailable while learner data deletion is pending.");
      }
      const privacy = readStudentPrivacy(studentData);
      if (!privacyPermissionGranted(privacy, "buddyAllowed") ||
          !privacyPermissionGranted(privacy, "audioProcessingAllowed")) {
        throw new HttpsError("permission-denied", "Buddy voice requires parent audio-processing permission.");
      }

      const leases = pruneBuddyVoiceLeases(capacitySnapshot.data()?.leases, now);
      const existing = readBuddyVoiceReservation(currentLeaseSnapshot.data());
      if (existing && existing.expiresAtEpochMs > now) {
        if (existing.clientRequestId !== clientRequestId || existing.dialogueTaskId !== dialogueTaskId) {
          throw new HttpsError("resource-exhausted", "This learner already has an active Buddy voice session.");
        }
        leases[existing.voiceSessionId] = { expiresAtEpochMs: existing.expiresAtEpochMs };
        transaction.set(capacityRef, {
          leases,
          activeLeaseCount: Object.keys(leases).length,
          maximumLeaseCount: buddyVoiceMaxConcurrentPerSchool,
          updatedAt: FieldValue.serverTimestamp(),
        }, { merge: true });
        transaction.set(db.doc(`buddyVoiceRoomBindings/${existing.roomName}`), {
          schoolId: payload.schoolId,
          studentId: payload.studentId,
          voiceSessionId: existing.voiceSessionId,
          expiresAtUtc: new Date(existing.expiresAtEpochMs).toISOString(),
          updatedAt: FieldValue.serverTimestamp(),
        }, { merge: true });
        return existing;
      }

      if (Object.keys(leases).length >= buddyVoiceMaxConcurrentPerSchool) {
        throw new HttpsError("resource-exhausted", "Buddy voice is at this school's current capacity. Try again shortly.");
      }
      const tier = resolveServiceTier(studentData);
      const dailySessionLimit = buddyTierVoiceSessionLimit(tier);
      const dailySessionCount = finiteInteger(usageSnapshot.data()?.sessionCount);
      if (dailySessionCount >= dailySessionLimit) {
        throw new HttpsError("resource-exhausted", "This learner has reached today's Buddy voice session allowance.");
      }
      leases[proposed.voiceSessionId] = { expiresAtEpochMs: proposed.expiresAtEpochMs };
      transaction.set(capacityRef, {
        leases,
        activeLeaseCount: Object.keys(leases).length,
        maximumLeaseCount: buddyVoiceMaxConcurrentPerSchool,
        updatedAt: FieldValue.serverTimestamp(),
      }, { merge: true });
      transaction.set(currentLeaseRef, {
        ...proposed,
        createdAt: FieldValue.serverTimestamp(),
        updatedAt: FieldValue.serverTimestamp(),
      });
      transaction.set(usageRef, {
        dateUtc: usageDateUtc,
        tier,
        sessionCount: dailySessionCount + 1,
        sessionLimit: dailySessionLimit,
        lastSessionId: proposed.voiceSessionId,
        updatedAt: FieldValue.serverTimestamp(),
      }, { merge: true });
      transaction.set(db.doc(`${studentBase}/buddyVoiceSessions/${proposed.voiceSessionId}`), {
        voiceSessionId: proposed.voiceSessionId,
        clientRequestId,
        dialogueTaskId,
        roomName: proposed.roomName,
        zone: canonicalZone,
        status: "reserved",
        expiresAtEpochMs: proposed.expiresAtEpochMs,
        expiresAtUtc: new Date(proposed.expiresAtEpochMs).toISOString(),
        createdAtUtc: new Date(now).toISOString(),
        createdAt: FieldValue.serverTimestamp(),
        updatedAt: FieldValue.serverTimestamp(),
      });
      transaction.set(db.doc(`buddyVoiceRoomBindings/${proposed.roomName}`), {
        schoolId: payload.schoolId,
        studentId: payload.studentId,
        voiceSessionId: proposed.voiceSessionId,
        expiresAtUtc: new Date(proposed.expiresAtEpochMs).toISOString(),
        createdAt: FieldValue.serverTimestamp(),
        updatedAt: FieldValue.serverTimestamp(),
      });
      return proposed;
    });

    const profile = profileSnapshot.data() ?? {};
    const learnerState = learnerStateSnapshot.data() ?? {};
    const memoryEnabled = profile.learningMemoryEnabled !== false;
    const practiceScaffold = scaffoldSnapshot?.data() ?? {};
    const grounding = shouldUseGrounding
      ? composeBuddyGroundingContext({
        task,
        profile,
        learnerState,
        grimoirePage: grimoireSnapshot?.data() ?? undefined,
        practiceScaffold: practiceScaffold,
        buddyContract: contractSnapshot?.data() ?? undefined,
      })
      : { groundingContext: "", groundingSourceIds: [], estimatedTokens: 0 };
    const metadata: BuddyVoiceDispatchMetadata = {
      schemaVersion: 1,
      voiceSessionId: reservation.voiceSessionId,
      participantIdentity: reservation.participantIdentity,
      expiresAtEpochMs: reservation.expiresAtEpochMs,
      maxSessionSeconds: buddyVoiceMaxSessionSeconds,
      homeLanguage: normalizeBuddyLanguage(profile.homeLanguage, "hi"),
      conceptId: taskConceptId,
      taskPrompt: redactTaskAnswerText(task, practiceScaffold, task.npcLine ?? task.sourceText, 400),
      conceptTitle: trimmedString(task.conceptTitle, 100) || trimmedString(task.conceptId, 100),
      grammarPattern: trimmedString(task.grammarPattern, 64),
      zone: canonicalZone,
      allowAnswerModel: canonicalZone === "Town",
      openingTrigger,
      openingLearnerAttempt,
      explanationStyle: trimmedString(profile.explanationStyle, 48) || "short_then_expand",
      allowTransliteration: profile.allowTransliteration === true,
      recommendedEnglishRatio: Math.max(0, Math.min(1, finiteNumber(learnerState.recommendedEnglishRatio) || 0.3)),
      supportBand: trimmedString(learnerState.supportBand, 32) || "Foundation",
      strengthConceptIds: normalizedStringArray(learnerState.strengthConceptIds, 3)
        .map(value => trimmedString(value, 96)),
      needConceptIds: normalizedStringArray(learnerState.needConceptIds, 3)
        .map(value => trimmedString(value, 96)),
      recurringErrorTags: normalizedStringArray(learnerState.recurringErrorTags, 5)
        .map(value => trimmedString(value, 64)),
      safeRelationshipMemory: memoryEnabled
        ? normalizedRelationshipMemory(payload.safeRelationshipMemory, 12)
        : [],
      groundingContext: grounding.groundingContext,
      groundingSourceIds: grounding.groundingSourceIds,
    };

    try {
      const tokenTtlSeconds = Math.max(
        60,
        Math.ceil((reservation.expiresAtEpochMs - Date.now()) / 1000) + 60,
      );
      const token = await createBuddyVoiceAccessToken({
        apiKey: configuredApiKey,
        apiSecret: configuredApiSecret,
        agentName: buddyVoiceAgentName,
        roomName: reservation.roomName,
        participantIdentity: reservation.participantIdentity,
        metadata,
        tokenTtlSeconds,
        roomEmptyTimeoutSeconds: buddyVoiceRoomEmptyTimeoutSeconds,
        roomDepartureTimeoutSeconds: buddyVoiceRoomDepartureTimeoutSeconds,
      });
      await db.doc(`${studentBase}/buddyVoiceSessions/${reservation.voiceSessionId}`).set({
        status: "issued",
        lastIssuedAtUtc: new Date().toISOString(),
        updatedAt: FieldValue.serverTimestamp(),
      }, { merge: true });
      return {
        schemaVersion: 1,
        voiceSessionId: reservation.voiceSessionId,
        serverUrl: configuredLiveKitUrl,
        token,
        expiresAtUtc: new Date(reservation.expiresAtEpochMs).toISOString(),
        maxSessionSeconds: buddyVoiceMaxSessionSeconds,
      };
    } catch (error) {
      await releaseBuddyVoiceReservation(
        payload.schoolId,
        payload.studentId,
        reservation.voiceSessionId,
        "token_issue_failed",
      ).catch(() => undefined);
      console.error("[BuddyVoice] token issuance failed", { schoolId: payload.schoolId, error: shortError(error) });
      throw new HttpsError("unavailable", "Buddy voice could not start. Try again shortly.");
    }
  },
);

export const closeBuddyVoiceSession = onCall(
  {
    timeoutSeconds: 10,
    region: buddyVoiceFunctionRegion,
    concurrency: buddyVoiceTokenFunctionConcurrency,
    maxInstances: buddyVoiceTokenFunctionMaxInstances,
    enforceAppCheck: buddyVoiceEnforceAppCheck,
  },
  async request => {
    const caller = requireRole(request.auth, ["student", "parent"]);
    const payload = requireStudentPayload(caller, request.data ?? {});
    const voiceSessionId = requiredTrimmedString(payload.voiceSessionId, "voiceSessionId");
    if (!/^[a-f0-9]{24}$/i.test(voiceSessionId)) {
      throw new HttpsError("invalid-argument", "voiceSessionId is invalid.");
    }
    await releaseBuddyVoiceReservation(payload.schoolId, payload.studentId, voiceSessionId, "closed");
    return { voiceSessionId, closed: true };
  },
);

export const resetBuddyVoiceSession = onCall(
  {
    secrets: [livekitApiKey, livekitApiSecret],
    timeoutSeconds: 15,
    region: buddyVoiceFunctionRegion,
    concurrency: buddyVoiceTokenFunctionConcurrency,
    maxInstances: buddyVoiceTokenFunctionMaxInstances,
    enforceAppCheck: buddyVoiceEnforceAppCheck,
  },
  async request => {
    const caller = requireRole(request.auth, ["student", "parent"]);
    const payload = requireStudentPayload(caller, request.data ?? {});
    await closeActiveBuddyVoiceRoom(payload.schoolId, payload.studentId, "reset_by_client");
    let dailyUsageReset = false;
    if (request.data?.resetDailyUsage === true) {
      const origin = request.rawRequest.get("origin") ?? "";
      if (!isLocalBuddyVoiceHarnessOrigin(origin)) {
        throw new HttpsError("permission-denied", "Daily Buddy voice usage reset is only available from the local test harness.");
      }
      await db.doc(`schools/${payload.schoolId}/students/${payload.studentId}/buddyVoiceUsageDaily/${utcDateKey()}`).delete();
      dailyUsageReset = true;
    }
    return { reset: true, dailyUsageReset };
  },
);

function isLocalBuddyVoiceHarnessOrigin(origin: string): boolean {
  if (!origin) return false;
  try {
    const parsed = new URL(origin);
    return parsed.protocol === "http:" &&
      (parsed.hostname === "127.0.0.1" || parsed.hostname === "localhost") &&
      parsed.port === "4177";
  } catch {
    return false;
  }
}

/**
 * Verified LiveKit lifecycle events release abandoned leases promptly. LiveKit
 * may deliver a webhook more than once, so the release path is idempotent.
 */

export const receiveBuddyVoiceWebhook = onRequest(
  {
    secrets: [livekitApiKey, livekitApiSecret],
    region: buddyVoiceFunctionRegion,
    timeoutSeconds: 15,
    concurrency: 80,
    maxInstances: buddyVoiceTokenFunctionMaxInstances,
  },
  async (request, response) => {
    if (request.method !== "POST") {
      response.status(405).send("method_not_allowed");
      return;
    }
    const apiKey = readLiveKitApiKey();
    const apiSecret = readLiveKitApiSecret();
    if (!apiKey || !apiSecret) {
      response.status(503).send("livekit_not_configured");
      return;
    }
    const rawBody = request.rawBody?.toString("utf8") ?? "";
    let event;
    try {
      event = await new WebhookReceiver(apiKey, apiSecret).receive(
        rawBody,
        request.get("Authorization") ?? "",
      );
    } catch (error) {
      console.warn("[BuddyVoice] rejected unverified webhook", { error: shortError(error) });
      response.status(401).send("invalid_webhook");
      return;
    }
    try {
      if (event.event !== "room_finished") {
        response.status(200).send("ignored");
        return;
      }
      const roomName = trimmedString(event.room?.name, 96);
      if (!/^buddy-[a-f0-9]{24}$/i.test(roomName)) {
        response.status(200).send("ignored");
        return;
      }
      const bindingRef = db.doc(`buddyVoiceRoomBindings/${roomName}`);
      const bindingSnapshot = await bindingRef.get();
      const binding = bindingSnapshot.data() ?? {};
      const schoolId = trimmedString(binding.schoolId, 160);
      const studentId = trimmedString(binding.studentId, 160);
      const voiceSessionId = trimmedString(binding.voiceSessionId, 64);
      if (!bindingSnapshot.exists || !/^[a-f0-9]{24}$/i.test(voiceSessionId)) {
        response.status(200).send("already_released");
        return;
      }
      assertFirestorePathSegment(schoolId, "schoolId");
      assertFirestorePathSegment(studentId, "studentId");
      await releaseBuddyVoiceReservation(schoolId, studentId, voiceSessionId, "room_finished");
      response.status(200).send("released");
    } catch (error) {
      console.error("[BuddyVoice] verified webhook processing failed", { error: shortError(error) });
      response.status(500).send("webhook_processing_failed");
    }
  },
);
