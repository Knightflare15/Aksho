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
import { CodeType, accessCodePepper, db } from "./runtime.js";
import { assertString, finiteInteger, finiteNumber } from "./sharedUtils.js";
import { writeAudit } from "./studentRecordHelpers.js";

export async function createCode(type: CodeType, schoolId: string, createdByUid: string, bindings: Record<string, unknown>) {
  // 80 random bits keeps one-time classroom codes resistant to online and
  // offline guessing while remaining practical to paste or scan as a QR code.
  const code = `${type === "parent" ? "PARENT" : type === "student" ? "STUDENT" : "TEACH"}-${randomBytes(10).toString("hex").toUpperCase()}`;
  const expiresAt = new Date(Date.now() + 30 * 86400000);
  const docRef = db.collection("accessCodes").doc();
  await docRef.set({
    type,
    status: "active",
    schoolId,
    ...bindings,
    codeHash: hashCode(code),
    expiresAt,
    createdByUid,
    createdAt: FieldValue.serverTimestamp()
  });
  await writeAudit(schoolId, createdByUid, `code.create.${type}`, docRef.path);
  return { code, expiresAt: expiresAt.toISOString() };
}

export async function findActiveCode(type: CodeType, code: string) {
  const snap = await db.collection("accessCodes")
    .where("type", "==", type)
    .where("status", "==", "active")
    .where("codeHash", "==", hashCode(code))
    .limit(1)
    .get();

  if (snap.empty) {
    throw new HttpsError("not-found", "Access code is invalid or expired.");
  }

  const doc = snap.docs[0];
  const expiresAt = doc.data().expiresAt?.toDate?.() as Date | undefined;
  if (expiresAt && expiresAt.getTime() < Date.now()) {
    await doc.ref.update({ status: "expired" });
    throw new HttpsError("deadline-exceeded", "Access code has expired.");
  }

  return doc;
}

export async function markCodeRedeemed(path: string, redeemedByUid: string) {
  const ref = db.doc(path);
  await db.runTransaction(async transaction => {
    const snapshot = await transaction.get(ref);
    if (!snapshot.exists) {
      throw new HttpsError("not-found", "Access code no longer exists.");
    }
    const data = snapshot.data() ?? {};
    if (data.status === "redeemed" && data.redeemedByUid === redeemedByUid)
      return;
    if (data.status !== "active") {
      throw new HttpsError("failed-precondition", "Access code has already been used.");
    }
    const expiresAt = data.expiresAt?.toDate?.() as Date | undefined;
    if (expiresAt && expiresAt.getTime() < Date.now()) {
      transaction.update(ref, { status: "expired", updatedAt: FieldValue.serverTimestamp() });
      throw new HttpsError("deadline-exceeded", "Access code has expired.");
    }
    transaction.update(ref, {
      status: "redeemed",
      redeemedByUid,
      redeemedAt: FieldValue.serverTimestamp(),
      updatedAt: FieldValue.serverTimestamp()
    });
  });
}

export async function enforceAccessCodeRateLimit(type: CodeType, principal: string) {
  const windowMs = 15 * 60 * 1000;
  const maximumAttempts = 8;
  const now = Date.now();
  const key = createHash("sha256")
    .update(`${type}:${principal}`)
    .digest("hex")
    .slice(0, 40);
  const ref = db.doc(`accessCodeRateLimits/${key}`);
  await db.runTransaction(async transaction => {
    const snapshot = await transaction.get(ref);
    const data = snapshot.exists ? snapshot.data() ?? {} : {};
    const previousStart = finiteNumber(data.windowStartedAtMs);
    const inWindow = previousStart > 0 && now - previousStart < windowMs;
    const attempts = inWindow ? finiteInteger(data.attempts) : 0;
    if (attempts >= maximumAttempts) {
      throw new HttpsError("resource-exhausted", "Too many access-code attempts. Try again later.");
    }
    transaction.set(ref, {
      type,
      windowStartedAtMs: inWindow ? previousStart : now,
      attempts: attempts + 1,
      expiresAtMs: now + windowMs,
      updatedAt: FieldValue.serverTimestamp()
    }, { merge: true });
  });
}

export function normalizedCode(value: unknown) {
  assertString(value, "code");
  return String(value).trim().toUpperCase();
}

export function hashCode(code: string) {
  return createHash("sha256").update(`${readAccessCodePepper()}:${code}`).digest("hex");
}

export function readAccessCodePepper() {
  try {
    const value = accessCodePepper.value() || process.env.ACCESS_CODE_PEPPER || "";
    if (value) return value;
  } catch {
    if (process.env.ACCESS_CODE_PEPPER) return process.env.ACCESS_CODE_PEPPER;
  }
  throw new HttpsError("failed-precondition", "Access-code service is not configured.");
}
