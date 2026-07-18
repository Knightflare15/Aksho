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
import { BuddyCanonicalZone } from "./runtime.js";


export function requiredTrimmedString(value: unknown, field: string) {
  const result = trimmedString(value);
  if (!result) {
    throw new HttpsError("invalid-argument", `${field} is required.`);
  }
  return result;
}

export function trimmedString(value: unknown, maximum = 160) {
  return typeof value === "string" ? value.trim().slice(0, maximum) : "";
}

export function normalizedBuddyZone(value: unknown): BuddyCanonicalZone {
  const normalized = trimmedString(value, 24).toLowerCase();
  if (normalized === "town") return "Town";
  if (normalized === "route") return "Route";
  return "Gym";
}

export function buddyZoneForAssistMode(assistMode: string): BuddyCanonicalZone {
  if (assistMode === "Full") return "Town";
  if (assistMode === "Partial") return "Route";
  return "Gym";
}
