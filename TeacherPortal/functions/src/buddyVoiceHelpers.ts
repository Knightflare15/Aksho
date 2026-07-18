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
import { BuddyVoiceReservation, azureSpeechKey, buddyVoiceMaxConcurrentPerSchool, db, geminiApiKey, livekitApiKey, livekitApiSecret, livekitUrl, sarvamApiKey } from "./runtime.js";
import { trimmedString } from "./inputHelpers.js";
import { shortError } from "./pronunciationProcessingHelpers.js";
import { assertFirestorePathSegment, finiteNumber } from "./sharedUtils.js";

export function readGeminiApiKey() {
  try {
    return sanitizeSecretValue(geminiApiKey.value() || process.env.GEMINI_API_KEY || "");
  } catch {
    return sanitizeSecretValue(process.env.GEMINI_API_KEY || "");
  }
}

export function readSarvamApiKey() {
  try {
    return sanitizeSecretValue(sarvamApiKey.value() || process.env.SARVAM_API_KEY || "");
  } catch {
    return sanitizeSecretValue(process.env.SARVAM_API_KEY || "");
  }
}

export function readAzureSpeechKey() {
  try {
    return sanitizeSecretValue(azureSpeechKey.value() || process.env.AZURE_SPEECH_KEY || "");
  } catch {
    return sanitizeSecretValue(process.env.AZURE_SPEECH_KEY || "");
  }
}

export function readLiveKitApiKey() {
  try {
    return sanitizeSecretValue(livekitApiKey.value() || process.env.LIVEKIT_API_KEY || "");
  } catch {
    return sanitizeSecretValue(process.env.LIVEKIT_API_KEY || "");
  }
}

export function readLiveKitApiSecret() {
  try {
    return sanitizeSecretValue(livekitApiSecret.value() || process.env.LIVEKIT_API_SECRET || "");
  } catch {
    return sanitizeSecretValue(process.env.LIVEKIT_API_SECRET || "");
  }
}

export function normalizeLiveKitServerUrl(value: unknown) {
  const raw = trimmedString(value, 512);
  if (!raw) return "";
  try {
    const parsed = new URL(raw);
    const localDevelopment = parsed.protocol === "ws:" &&
      (parsed.hostname === "localhost" || parsed.hostname === "127.0.0.1" || parsed.hostname === "::1");
    if (parsed.protocol !== "wss:" && !localDevelopment) return "";
    if (parsed.username || parsed.password || parsed.search || parsed.hash) return "";
    if (parsed.pathname !== "/" && parsed.pathname !== "") return "";
    return parsed.toString().replace(/\/$/, "");
  } catch {
    return "";
  }
}

export function readBuddyVoiceReservation(value: unknown): BuddyVoiceReservation | null {
  if (typeof value !== "object" || value === null || Array.isArray(value)) return null;
  const data = value as Record<string, unknown>;
  const reservation: BuddyVoiceReservation = {
    voiceSessionId: trimmedString(data.voiceSessionId, 64),
    clientRequestId: trimmedString(data.clientRequestId, 96),
    dialogueTaskId: trimmedString(data.dialogueTaskId, 160),
    roomName: trimmedString(data.roomName, 96),
    participantIdentity: trimmedString(data.participantIdentity, 96),
    expiresAtEpochMs: Math.round(finiteNumber(data.expiresAtEpochMs)),
  };
  if (!/^[a-f0-9]{24}$/i.test(reservation.voiceSessionId) ||
      !/^[a-zA-Z0-9_-]{8,96}$/.test(reservation.clientRequestId) ||
      !reservation.dialogueTaskId ||
      !/^buddy-[a-f0-9]{24}$/i.test(reservation.roomName) ||
      !/^learner-[a-f0-9]{20}$/i.test(reservation.participantIdentity) ||
      reservation.expiresAtEpochMs <= 0) {
    return null;
  }
  return reservation;
}

export async function releaseBuddyVoiceReservation(
  schoolId: string,
  studentId: string,
  voiceSessionId: string,
  status: string,
) {
  assertFirestorePathSegment(schoolId, "schoolId");
  assertFirestorePathSegment(studentId, "studentId");
  if (!/^[a-f0-9]{24}$/i.test(voiceSessionId)) throw new Error("voiceSessionId is invalid.");
  const now = Date.now();
  const studentBase = `schools/${schoolId}/students/${studentId}`;
  const capacityRef = db.doc(`schools/${schoolId}/serviceLeases/buddyVoice`);
  const currentLeaseRef = db.doc(`${studentBase}/buddyVoiceLease/current`);
  const sessionRef = db.doc(`${studentBase}/buddyVoiceSessions/${voiceSessionId}`);
  await db.runTransaction(async transaction => {
    const [capacitySnapshot, currentLeaseSnapshot, sessionSnapshot] = await Promise.all([
      transaction.get(capacityRef),
      transaction.get(currentLeaseRef),
      transaction.get(sessionRef),
    ]);
    const leases = pruneBuddyVoiceLeases(capacitySnapshot.data()?.leases, now);
    delete leases[voiceSessionId];
    transaction.set(capacityRef, {
      leases,
      activeLeaseCount: Object.keys(leases).length,
      maximumLeaseCount: buddyVoiceMaxConcurrentPerSchool,
      updatedAt: FieldValue.serverTimestamp(),
    }, { merge: true });
    const current = readBuddyVoiceReservation(currentLeaseSnapshot.data());
    if (current?.voiceSessionId === voiceSessionId) transaction.delete(currentLeaseRef);
    const roomName = current?.roomName ?? trimmedString(sessionSnapshot.data()?.roomName, 96);
    if (/^buddy-[a-f0-9]{24}$/i.test(roomName)) {
      transaction.delete(db.doc(`buddyVoiceRoomBindings/${roomName}`));
    }
    transaction.set(sessionRef, {
      status: trimmedString(status, 64) || "closed",
      endedAtUtc: new Date(now).toISOString(),
      updatedAt: FieldValue.serverTimestamp(),
    }, { merge: true });
  });
}

export async function closeActiveBuddyVoiceRoom(schoolId: string, studentId: string, status: string) {
  const snapshot = await db.doc(`schools/${schoolId}/students/${studentId}/buddyVoiceLease/current`).get();
  const reservation = readBuddyVoiceReservation(snapshot.data());
  if (!reservation) return;
  const configuredUrl = normalizeLiveKitServerUrl(livekitUrl);
  const apiKey = readLiveKitApiKey();
  const apiSecret = readLiveKitApiSecret();
  if (configuredUrl && apiKey && apiSecret) {
    try {
      const endpoint = new URL(configuredUrl);
      endpoint.protocol = endpoint.protocol === "wss:" ? "https:" : "http:";
      const rooms = new RoomServiceClient(endpoint.toString().replace(/\/$/, ""), apiKey, apiSecret);
      await rooms.deleteRoom(reservation.roomName);
    } catch (error) {
      console.error("[BuddyVoice] active room termination failed", {
        schoolId,
        voiceSessionId: reservation.voiceSessionId,
        error: shortError(error),
      });
    }
  }
  await releaseBuddyVoiceReservation(schoolId, studentId, reservation.voiceSessionId, status);
}

export function sanitizeSecretValue(value: unknown) {
  let text = String(value ?? "").trim();
  if (text.length >= 2) {
    const first = text[0];
    const last = text[text.length - 1];
    if ((first === "\"" && last === "\"") || (first === "'" && last === "'")) {
      text = text.slice(1, -1).trim();
    }
  }
  return text;
}
