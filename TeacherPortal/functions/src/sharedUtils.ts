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
import { Role, buddyRelationshipMemoryAllowList } from "./runtime.js";


export function cloneRecord(value: unknown): Record<string, unknown> {
  return value && typeof value === "object" && !Array.isArray(value)
    ? { ...(value as Record<string, unknown>) }
    : {};
}

export function cloneNumberRecord(value: unknown): Record<string, number> {
  const source = cloneRecord(value);
  const result: Record<string, number> = {};
  for (const [key, item] of Object.entries(source)) {
    result[key] = finiteInteger(item);
  }
  return result;
}

export function normalizedStringArray(value: unknown, maximum: number) {
  if (!Array.isArray(value)) {
    return [];
  }
  return value
    .filter(item => typeof item === "string" && item.trim().length > 0)
    .slice(0, maximum)
    .map(item => String(item).trim());
}

export function normalizedRelationshipMemory(value: unknown, maximum: number) {
  return normalizedStringArray(value, maximum)
    .map(item => item.toLowerCase())
    .filter(item => buddyRelationshipMemoryAllowList.has(item))
    .slice(0, maximum);
}

export function normalizedAggregateKey(value: unknown, fallback: string) {
  const raw = String(value ?? "").trim();
  const normalized = raw.replace(/[^a-zA-Z0-9_-]+/g, "_").replace(/^_+|_+$/g, "");
  return normalized || fallback;
}

export function finiteNumber(value: unknown) {
  const parsed = typeof value === "number" ? value : Number(value ?? 0);
  return Number.isFinite(parsed) ? parsed : 0;
}

export function finiteInteger(value: unknown) {
  return Math.max(0, Math.floor(finiteNumber(value)));
}

export function safeRatio(numerator: number, denominator: number) {
  return denominator > 0 ? numerator / denominator : 0;
}

export function assertString(value: unknown, field: string): asserts value is string {
  if (typeof value !== "string" || value.trim().length === 0) {
    throw new HttpsError("invalid-argument", `${field} is required.`);
  }
}

export function assertFirestorePathSegment(value: unknown, field: string): asserts value is string {
  assertString(value, field);
  const normalized = value.trim();
  if (normalized.length > 512 || normalized === "." || normalized === ".." || normalized.includes("/")) {
    throw new HttpsError("invalid-argument", `${field} is not a valid document identifier.`);
  }
}

export function assertPayloadSize(value: unknown, maximumBytes: number) {
  let bytes = maximumBytes + 1;
  try {
    bytes = Buffer.byteLength(JSON.stringify(value ?? {}), "utf8");
  } catch {
    throw new HttpsError("invalid-argument", "Request payload is not valid JSON.");
  }
  if (bytes > maximumBytes) {
    throw new HttpsError("invalid-argument", `Request payload exceeds the ${maximumBytes} byte limit.`);
  }
}

export function assertEmail(value: unknown, field: string): asserts value is string {
  assertString(value, field);
  if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(value.trim())) {
    throw new HttpsError("invalid-argument", `${field} must be a valid email address.`);
  }
}

export function assertBoolean(value: unknown, field: string): asserts value is boolean {
  if (typeof value !== "boolean") {
    throw new HttpsError("invalid-argument", `${field} must be a boolean.`);
  }
}

export function assertPassword(value: unknown): asserts value is string {
  assertString(value, "password");
  if (value.length < 6) {
    throw new HttpsError("invalid-argument", "password must be at least 6 characters.");
  }
}

export function assertRole(value: unknown): asserts value is Role {
  if (value !== "admin" && value !== "teacher" && value !== "parent" && value !== "student") {
    throw new HttpsError("invalid-argument", "role is invalid.");
  }
}

export function randomId() {
  return randomBytes(12).toString("hex");
}

export function uniqueStrings(values: string[]) {
  return Array.from(new Set(values.filter((value) => typeof value === "string" && value.trim().length > 0)));
}

export function slugify(value: string) {
  const slug = value.toLowerCase().trim().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "");
  return slug || randomId();
}

export function currentWeekId() {
  const now = new Date();
  const day = now.getUTCDay();
  const start = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate() - day));
  return start.toISOString().slice(0, 10);
}
