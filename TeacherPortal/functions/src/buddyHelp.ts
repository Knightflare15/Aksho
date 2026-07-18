import { HttpsError, onCall } from "firebase-functions/v2/https";
import { requireRole, requireStudentPayload } from "./authorizationHelpers.js";
import { recordBuddyOperations, releaseBuddyRequest, reserveBuddyBudget, reserveBuddyRequest, utcDateKey } from "./buddyBudgetHelpers.js";
import { buildBuddyModelContext, redactBuddyText, screenChildSafetyText, summarizeBuddyAttemptForModel, summarizeBuddyConversationTurnForModel } from "./buddyContextHelpers.js";
import { extractGrimoireHighlightKeys, normalizeBuddyModelResponse } from "./buddyGemini.js";
import { generateBuddyProviderHelp } from "./buddyProviderCore.js";
import { uniqueBuddyStrings, writeBuddyHelpTurn } from "./buddyResponseHelpers.js";
import { attachBuddyRouter, buddyBlockedResponse, buddyFallbackResponse, buildBuddyRouterDecision, fallbackBuddyText, resolveServiceTier } from "./buddyRoutingHelpers.js";
import { buddyZoneForAssistMode, normalizedBuddyZone, requiredTrimmedString, trimmedString } from "./inputHelpers.js";
import { recordBuddySafetyAggregate } from "./operationsHelpers.js";
import { privacyPermissionGranted, readStudentPrivacy } from "./privacyHelpers.js";
import {
  buddyAllowLegacyCallable,
  buddyLlmProvider,
  buddyMemoryTagAllowList,
  buddyModel,
  buddyRelationshipMemoryAllowList,
  db,
  geminiApiKey,
  sarvamApiKey,
} from "./runtime.js";
import { cloneRecord, finiteInteger, normalizedRelationshipMemory } from "./sharedUtils.js";

export const requestBuddyHelp = onCall(
  { secrets: [geminiApiKey, sarvamApiKey], timeoutSeconds: 30 },
  async request => {
    if (!buddyAllowLegacyCallable) {
      throw new HttpsError("failed-precondition", "Turn-based Buddy is retired. Start a realtime Buddy voice session.");
    }

    const caller = requireRole(request.auth, ["student", "parent"]);
    const payload = requireStudentPayload(caller, request.data ?? {});
    const dialogueTaskId = requiredTrimmedString(payload.dialogueTaskId, "dialogueTaskId");
    const trigger = payload.trigger === "wrong_answer"
      ? "wrong_answer"
      : payload.trigger === "follow_up" ? "follow_up" : "ask";
    const submittedZone = normalizedBuddyZone(payload.zoneKind);
    const learnerAttempt = redactBuddyText(payload.learnerAttempt, 240);
    const studentPath = `schools/${payload.schoolId}/students/${payload.studentId}`;
    const sessionId = trimmedString(payload.sessionId, 120);
    const callId = trimmedString(payload.callId, 120);
    const callTurnIndex = Math.max(0, Math.min(100, finiteInteger(payload.callTurnIndex)));
    const isCallTurn = payload.isCallTurn === true && Boolean(callId);
    const safeRelationshipMemory = normalizedRelationshipMemory(payload.safeRelationshipMemory, 12);

    const privacySnapshot = await db.doc(studentPath).get();
    if (!privacySnapshot.exists) throw new HttpsError("not-found", "Student was not found.");
    const privacy = readStudentPrivacy(privacySnapshot.data() ?? {});
    if (!privacyPermissionGranted(privacy, "buddyAllowed")) {
      const blocked = buddyBlockedResponse("Buddy needs parent permission first. You can keep playing with text and game clues.");
      blocked.fallbackReason = "parental_consent_required";
      blocked.routerAction = "block";
      blocked.routerReason = "parental_consent_required";
      blocked.safetyFlags = ["parental_consent_required"];
      return blocked;
    }

    const safetyDecision = screenChildSafetyText(learnerAttempt, "learner");
    if (safetyDecision.blocked) {
      const blocked = buddyBlockedResponse(safetyDecision.learnerMessage);
      blocked.fallbackReason = "child_safety_intercept";
      blocked.routerIntent = "safety_or_unknown";
      blocked.routerAction = "block";
      blocked.routerReason = "child_safety_intercept";
      blocked.safetyFlags = safetyDecision.flags;
      await recordBuddySafetyAggregate(payload.schoolId, safetyDecision.flags);
      return blocked;
    }

    const taskSnapshot = await db.doc(`gameContentDialogueTasks/${dialogueTaskId}`).get();
    if (!taskSnapshot.exists) {
      throw new HttpsError("not-found", "Buddy help is unavailable because this dialogue task has not been seeded.");
    }

    const task = taskSnapshot.data() ?? {};
    const assistMode = String(task.assistMode ?? "Off");
    const canonicalZone = buddyZoneForAssistMode(assistMode);
    if (canonicalZone === "Gym" || submittedZone === "Gym") {
      const blocked = buddyBlockedResponse("Gym checks are completed without Buddy help.");
      blocked.routerIntent = "wrong_answer_coach";
      blocked.routerAction = "block";
      blocked.routerReason = "gym_check_blocked";
      blocked.tier = "free";
      return blocked;
    }

    const reservation = await reserveBuddyRequest(studentPath);
    if (!reservation) {
      return buddyFallbackResponse(canonicalZone, "cooldown", "Buddy is taking a short breath. Try again in a moment.");
    }

    const startedAt = Date.now();
    let budget: Awaited<ReturnType<typeof reserveBuddyBudget>> | undefined;
    const taskConceptId = String(task.conceptId ?? "");
    const attemptsQuery = taskConceptId
      ? db.collection(`${studentPath}/buddyLearningAttempts`)
        .where("conceptId", "==", taskConceptId)
        .orderBy("createdAtUtc", "desc")
        .limit(8)
      : db.collection(`${studentPath}/buddyLearningAttempts`)
        .orderBy("createdAtUtc", "desc")
        .limit(8);
    const conversationQuery = sessionId
      ? db.collection(`${studentPath}/buddyConversationTurns`)
        .where("sessionId", "==", sessionId)
        .orderBy("createdAtUtc", "desc")
        .limit(10)
      : db.collection(`${studentPath}/buddyConversationTurns`)
        .orderBy("createdAtUtc", "desc")
        .limit(1);
    const dateUtc = utcDateKey();
    const usageRef = db.doc(`${studentPath}/buddyUsageDaily/${dateUtc}`);

    let snapshots: any[];
    try {
      snapshots = await Promise.all([
        db.doc(studentPath).get(),
        db.doc(`${studentPath}/buddyLearnerProfiles/current`).get(),
        db.doc(`${studentPath}/buddyLearnerState/current`).get(),
        attemptsQuery.get(),
        conversationQuery.get(),
        usageRef.get(),
      ]);
    } catch (error) {
      await releaseBuddyRequest(studentPath).catch(() => undefined);
      throw error;
    }

    const [studentSnapshot, profileSnapshot, stateSnapshot, attemptsSnapshot, conversationSnapshot, usageSnapshot] = snapshots;
    try {
      const profile = profileSnapshot.exists ? profileSnapshot.data() ?? {} : {};
      const learnerState = stateSnapshot.exists ? stateSnapshot.data() ?? {} : {};
      const recentAttempts = attemptsSnapshot.docs
        .map((document: any) => document.data())
        .reverse()
        .map(summarizeBuddyAttemptForModel);
      const recentConversationTurns = conversationSnapshot.docs
        .map((document: any) => document.data())
        .filter(() => Boolean(sessionId))
        .reverse()
        .map(summarizeBuddyConversationTurnForModel);
      const student = studentSnapshot.exists ? studentSnapshot.data() ?? {} : {};
      const trustedTier = resolveServiceTier(student);
      const trustedConceptId = String(task.conceptId ?? "");
      const submittedConceptId = trimmedString(payload.conceptId, 96);
      const grimoireExcerpt = submittedConceptId === trustedConceptId
        ? redactBuddyText(payload.grimoireExcerpt, 900)
        : "";
      const router = buildBuddyRouterDecision({
        zone: canonicalZone,
        trigger,
        learnerAttempt,
        task,
        tier: trustedTier,
        learnerState,
        isCallTurn,
        dailyModelCallCount: Math.max(0, finiteInteger(usageSnapshot.data()?.modelResponseCount)),
        hasGrimoireExcerpt: Boolean(grimoireExcerpt),
      });

      if (router.action === "local") {
        const local = attachBuddyRouter(buddyFallbackResponse(canonicalZone, router.reason, router.fallbackMessage), router);
        local.latencyMs = Math.max(0, Date.now() - startedAt);
        await writeBuddyHelpTurn({
          schoolId: payload.schoolId,
          studentId: payload.studentId,
          sessionId,
          callId,
          callTurnIndex,
          isCallTurn,
          classId: String(student.classId ?? payload.classId ?? ""),
          areaId: trimmedString(payload.areaId),
          dialogueTaskId,
          conceptId: taskConceptId,
          grammarPattern: String(task.grammarPattern ?? ""),
          canonicalZone,
          trigger,
          learnerAttempt,
          response: local,
          memoryEnabled: profile.learningMemoryEnabled !== false,
          usage: {},
        });
        await recordBuddyOperations({
          schoolId: payload.schoolId,
          studentId: payload.studentId,
          dateUtc,
          response: local,
          usage: {},
          latencyMs: local.latencyMs,
        });
        return local;
      }

      budget = await reserveBuddyBudget(studentPath, router.tier, payload.schoolId);
      if (!budget.allowed) {
        const limited = attachBuddyRouter(buddyFallbackResponse(canonicalZone, budget.reason, "Buddy has reached today's practice limit. Read the clue, then try again tomorrow."), {
          ...router,
          action: "local",
          reason: budget.reason,
          modelAllowed: false,
          fallbackMessage: "Buddy has reached today's practice limit. Read the clue, then try again tomorrow.",
          safetyFlags: uniqueBuddyStrings([...router.safetyFlags, budget.reason], 10),
        });
        limited.latencyMs = Math.max(0, Date.now() - startedAt);
        await recordBuddyOperations({
          schoolId: payload.schoolId,
          studentId: payload.studentId,
          dateUtc: budget.dateUtc,
          response: limited,
          usage: {},
          latencyMs: limited.latencyMs,
          budget,
        });
        return limited;
      }

      const context = buildBuddyModelContext(task, canonicalZone, learnerAttempt, profile, learnerState, recentAttempts, recentConversationTurns, {
        sessionId,
        callId,
        callTurnIndex,
        isCallTurn,
        safeRelationshipMemory: profile.learningMemoryEnabled === false ? [] : safeRelationshipMemory,
        grimoireExcerpt,
      });
      const contextRecord = context as Record<string, unknown> & { policy?: Record<string, unknown> };
      contextRecord.policy = {
        ...cloneRecord(contextRecord.policy),
        routerIntent: router.intent,
        routerReason: router.reason,
        buddyTier: router.tier,
        maxOutputTokens: router.maxOutputTokens,
      };

      let response = buddyFallbackResponse(canonicalZone, "provider_unavailable", fallbackBuddyText(canonicalZone));
      let modelUsage: Record<string, unknown> = {};
      let modelSafetyFlags: string[] = [];
      const generated = await generateBuddyProviderHelp(context, router.maxOutputTokens);
      modelUsage = generated.usage;
      modelSafetyFlags = generated.safetyFlags;
      if (generated.output) {
        response = normalizeBuddyModelResponse(generated.output, canonicalZone, String(task.expectedResponse ?? ""), learnerAttempt, trustedConceptId, isCallTurn, extractGrimoireHighlightKeys(grimoireExcerpt));
      } else if (generated.reason) {
        response = buddyFallbackResponse(canonicalZone, generated.reason, fallbackBuddyText(canonicalZone));
      }
      response.provider = response.status === "ok" ? generated.provider ?? buddyLlmProvider : "deterministic_fallback";
      response.model = response.status === "ok" ? generated.model ?? buddyModel : "";
      response.callId = callId;
      response.callTurnIndex = callTurnIndex;
      response.latencyMs = Math.max(0, Date.now() - startedAt);
      response.safetyFlags = uniqueBuddyStrings([...response.safetyFlags, ...modelSafetyFlags], 10);
      response = attachBuddyRouter(response, router);

      const memoryEnabled = profile.learningMemoryEnabled !== false;
      response.safeMemoryTags = memoryEnabled
        ? response.safeMemoryTags.filter(tag => buddyMemoryTagAllowList.has(tag))
        : [];
      response.relationshipMemoryCandidates = memoryEnabled
        ? response.relationshipMemoryCandidates.filter(tag => buddyRelationshipMemoryAllowList.has(tag))
        : [];

      await writeBuddyHelpTurn({
        schoolId: payload.schoolId,
        studentId: payload.studentId,
        sessionId,
        callId,
        callTurnIndex,
        isCallTurn,
        classId: String(student.classId ?? payload.classId ?? ""),
        areaId: trimmedString(payload.areaId),
        dialogueTaskId,
        conceptId: taskConceptId,
        grammarPattern: String(task.grammarPattern ?? ""),
        canonicalZone,
        trigger,
        learnerAttempt,
        response,
        memoryEnabled,
        usage: modelUsage,
      });
      await recordBuddyOperations({
        schoolId: payload.schoolId,
        studentId: payload.studentId,
        dateUtc: budget.dateUtc,
        response,
        usage: modelUsage,
        latencyMs: response.latencyMs,
        budget,
      });
      return response;
    } catch (error) {
      console.error("[Buddy] request failed", error);
      const fallback = buddyFallbackResponse(canonicalZone, "provider_error", fallbackBuddyText(canonicalZone));
      await recordBuddyOperations({
        schoolId: payload.schoolId,
        studentId: payload.studentId,
        dateUtc: budget?.dateUtc ?? utcDateKey(),
        response: fallback,
        usage: {},
        latencyMs: Math.max(0, Date.now() - startedAt),
        budget,
      });
      return fallback;
    } finally {
      try {
        await releaseBuddyRequest(studentPath);
      } catch (error) {
        console.warn("[Buddy] request guard could not be released", error);
      }
    }
  },
);
