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
import { auth, cosmeticShopCatalog, db } from "./runtime.js";
import { requireRole, requireStudentPayload } from "./authorizationHelpers.js";
import { dateKeyInTimeZone } from "./buddyBudgetHelpers.js";
import { requiredTrimmedString, trimmedString } from "./inputHelpers.js";
import { finiteInteger, uniqueStrings } from "./sharedUtils.js";

export const claimWorldGoalReward = onCall(async (request) => {
  const caller = requireRole(request.auth, ["student"]);
  const payload = requireStudentPayload(caller, request.data ?? {});
  const goalId = requiredTrimmedString(payload.goalId, "goalId");
  const submittedTargetGymId = requiredTrimmedString(payload.targetGymId, "targetGymId");
  const studentPath = `schools/${payload.schoolId}/students/${payload.studentId}`;
  const studentRef = db.doc(studentPath);
  const studentSnapshot = await studentRef.get();
  if (!studentSnapshot.exists) {
    throw new HttpsError("not-found", "Student profile was not found.");
  }

  const classId = requiredTrimmedString(studentSnapshot.data()?.classId, "student.classId");
  const studentGoalRef = db.doc(`${studentPath}/worldGoals/${goalId}`);
  const classGoalRef = db.doc(`schools/${payload.schoolId}/classes/${classId}/worldGoals/${goalId}`);
  const [studentGoalSnapshot, classGoalSnapshot] = await Promise.all([studentGoalRef.get(), classGoalRef.get()]);
  const goalRef = studentGoalSnapshot.exists ? studentGoalRef : classGoalSnapshot.exists ? classGoalRef : null;
  if (!goalRef) {
    throw new HttpsError("not-found", "The active world goal was not found.");
  }

  const resolvedGoal = (studentGoalSnapshot.exists ? studentGoalSnapshot : classGoalSnapshot).data() ?? {};
  const resolvedTargetGymId = requiredTrimmedString(resolvedGoal.targetGymId, "goal.targetGymId");
  if (resolvedTargetGymId !== submittedTargetGymId) {
    throw new HttpsError("failed-precondition", "The completed gym does not match this goal.");
  }
  const claimRef = db.doc(`${studentPath}/worldGoalClaims/${goalId}`);
  const walletRef = db.doc(`${studentPath}/wallet/current`);
  const preexistingClaim = await claimRef.get();
  let completionEvidenceId = "";
  let completionReceivedAt: Date | undefined;
  if (!preexistingClaim.exists) {
    const completionSnapshot = await db.collection(`${studentPath}/gymAttempts`)
      .where("gymId", "==", resolvedTargetGymId)
      .where("goalId", "==", goalId)
      .where("passed", "==", true)
      .orderBy("receivedAt", "asc")
      .limit(1)
      .get();
    if (completionSnapshot.empty) {
      // The Unity analytics submission can finish just after the area-complete
      // callback. The client retains this idempotent claim and retries it.
      throw new HttpsError("failed-precondition", "A server-recorded passing gym attempt is required before claiming this reward.");
    }
    const completionEvidence = completionSnapshot.docs[0];
    completionEvidenceId = completionEvidence.id;
    completionReceivedAt = completionEvidence.data().receivedAt?.toDate?.() as Date | undefined;
    if (!completionReceivedAt || !Number.isFinite(completionReceivedAt.getTime())) {
      throw new HttpsError("failed-precondition", "The passing gym attempt has no server completion timestamp.");
    }
  }

  return db.runTransaction(async transaction => {
    const [goalSnapshot, existingClaim, walletSnapshot] = await Promise.all([
      transaction.get(goalRef),
      transaction.get(claimRef),
      transaction.get(walletRef)
    ]);
    if (!goalSnapshot.exists) {
      throw new HttpsError("not-found", "The active world goal was not found.");
    }

    const goal = goalSnapshot.data() ?? {};
    const targetGymId = requiredTrimmedString(goal.targetGymId, "goal.targetGymId");
    if (targetGymId !== submittedTargetGymId) {
      throw new HttpsError("failed-precondition", "The completed gym does not match this goal.");
    }

    if (existingClaim.exists) {
      const previous = existingClaim.data() ?? {};
      return {
        ok: true,
        goalId,
        status: String(previous.status ?? "reward_claimed"),
        targetGymId,
        rewardCoins: finiteInteger(previous.rewardCoins),
        // The wallet may have changed since this idempotent claim was first
        // processed. Return its current value so Unity never mirrors a stale
        // balance after a reconnect or scene reload.
        walletBalance: Math.max(0, finiteInteger(walletSnapshot.data()?.balance)),
        completedAtUtc: String(previous.completedAtUtc ?? ""),
        claimedAtUtc: String(previous.claimedAtUtc ?? ""),
        alreadyClaimed: true
      };
    }

    if (!completionReceivedAt) {
      throw new HttpsError("failed-precondition", "A server-recorded passing gym attempt is required before claiming this reward.");
    }

    const configuredReward = typeof goal.rewardCoins === "number" || typeof goal.rewardCoins === "string"
      ? finiteInteger(goal.rewardCoins)
      : 25;
    const rewardCoins = Math.max(0, Math.min(10000, configuredReward));
    const dueDate = requiredTrimmedString(goal.dueDate, "goal.dueDate");
    const schoolTimeZone = trimmedString(goal.schoolTimeZone, 80) || "Asia/Kolkata";
    // Completion and deadline decisions are server-owned. The client may keep
    // its reported timestamp for diagnostics, but it cannot decide rewards.
    const completedAtUtc = completionReceivedAt.toISOString();
    const reportedCompletedAtUtc = trimmedString(payload.completedAtUtc, 60);
    const late = dateKeyInTimeZone(new Date(completedAtUtc), schoolTimeZone) > dueDate;
    const currentBalance = Math.max(0, finiteInteger(walletSnapshot.data()?.balance));
    const awarded = late ? 0 : rewardCoins;
    const walletBalance = currentBalance + awarded;
    const claimedAtUtc = new Date().toISOString();
    const status = late ? "completed_late" : "reward_claimed";

    transaction.set(claimRef, {
      goalId,
      schoolId: payload.schoolId,
      classId,
      studentId: payload.studentId,
      targetGymId,
      completionEvidenceId,
      status,
      onTime: !late,
      rewardCoins: awarded,
      configuredRewardCoins: rewardCoins,
      walletBalance,
      completedAtUtc,
      reportedCompletedAtUtc,
      claimedAtUtc,
      createdAt: FieldValue.serverTimestamp()
    });
    transaction.set(walletRef, {
      balance: walletBalance,
      lifetimeCoinsEarned: FieldValue.increment(awarded),
      updatedAt: FieldValue.serverTimestamp()
    }, { merge: true });

    return {
      ok: true,
      goalId,
      status,
      targetGymId,
      rewardCoins: awarded,
      walletBalance,
      completedAtUtc,
      claimedAtUtc,
      alreadyClaimed: false
    };
  });
});

/** Authenticated purchases and equipment changes share the server wallet transaction. */

export const purchaseCosmetic = onCall(async (request) => {
  const caller = requireRole(request.auth, ["student"]);
  const payload = requireStudentPayload(caller, request.data ?? {});
  const itemId = requiredTrimmedString(payload.itemId, "itemId");
  const item = cosmeticShopCatalog[itemId];
  if (!item) {
    throw new HttpsError("not-found", "Cosmetic item is not available.");
  }

  const studentPath = `schools/${payload.schoolId}/students/${payload.studentId}`;
  const walletRef = db.doc(`${studentPath}/wallet/current`);
  const inventoryRef = db.doc(`${studentPath}/cosmetics/current`);
  return db.runTransaction(async transaction => {
    const [walletSnapshot, inventorySnapshot] = await Promise.all([
      transaction.get(walletRef),
      transaction.get(inventoryRef)
    ]);
    const inventory = inventorySnapshot.exists ? inventorySnapshot.data() ?? {} : {};
    const unlockedSkinIds = uniqueStrings(Array.isArray(inventory.unlockedSkinIds) ? inventory.unlockedSkinIds.map(String) : ["skin_default"]);
    const unlockedCompanionIds = uniqueStrings(Array.isArray(inventory.unlockedCompanionIds) ? inventory.unlockedCompanionIds.map(String) : ["companion_none"]);
    if (!unlockedSkinIds.includes("skin_default")) unlockedSkinIds.push("skin_default");
    if (!unlockedCompanionIds.includes("companion_none")) unlockedCompanionIds.push("companion_none");
    const unlocked = item.kind === "skin" ? unlockedSkinIds : unlockedCompanionIds;
    const alreadyOwned = unlocked.includes(itemId);
    const charged = alreadyOwned ? 0 : item.price;
    const currentBalance = Math.max(0, finiteInteger(walletSnapshot.data()?.balance));
    if (currentBalance < charged) {
      throw new HttpsError("failed-precondition", "Not enough coins.");
    }
    if (!alreadyOwned) unlocked.push(itemId);
    const walletBalance = currentBalance - charged;

    transaction.set(walletRef, {
      balance: walletBalance,
      lifetimeCoinsSpent: FieldValue.increment(charged),
      updatedAt: FieldValue.serverTimestamp()
    }, { merge: true });
    transaction.set(inventoryRef, {
      studentId: payload.studentId,
      schoolId: payload.schoolId,
      unlockedSkinIds,
      unlockedCompanionIds,
      equippedSkinId: item.kind === "skin" ? itemId : String(inventory.equippedSkinId ?? "skin_default"),
      equippedCompanionId: item.kind === "companion" ? itemId : String(inventory.equippedCompanionId ?? "companion_none"),
      updatedAt: FieldValue.serverTimestamp()
    }, { merge: true });
    return { ok: true, itemId, kind: item.kind, charged, alreadyOwned, walletBalance };
  });
});

/** Safe, aggregate-only support view. Raw Buddy text is intentionally excluded. */
