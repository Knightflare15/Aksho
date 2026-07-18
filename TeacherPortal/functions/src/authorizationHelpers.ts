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
import { CallableAuth, Role, StudentAudioPayload, StudentPayload, db, maxStudentPayloadBytes } from "./runtime.js";
import { normalizeSarvamLanguageCode } from "./buddySpeechHelpers.js";
import { trimmedString } from "./inputHelpers.js";
import { assertFirestorePathSegment, assertPayloadSize } from "./sharedUtils.js";

export function requireRole(authContext: { uid: string; token: Record<string, unknown> } | undefined, roles: Role[]): CallableAuth {
  if (!authContext) {
    throw new HttpsError("unauthenticated", "Sign in required.");
  }
  const role = authContext.token.role as Role | undefined;
  if (!role || !roles.includes(role)) {
    throw new HttpsError("permission-denied", "This account is not allowed to perform that action.");
  }
  return authContext as CallableAuth;
}

export function assertSameSchool(caller: CallableAuth, schoolId: string) {
  if (caller.token.schoolId !== schoolId) {
    throw new HttpsError("permission-denied", "Cross-school access is not allowed.");
  }
}

export function assertTeacherClassAccess(caller: CallableAuth, classId: string) {
  if (caller.token.role === "admin") {
    return;
  }
  if (!caller.token.classIds?.includes(classId)) {
    throw new HttpsError("permission-denied", "Teacher is not assigned to this class.");
  }
}

export async function assertStudentClassBinding(schoolId: string, classId: string, studentId: string) {
  assertFirestorePathSegment(schoolId, "schoolId");
  assertFirestorePathSegment(classId, "classId");
  assertFirestorePathSegment(studentId, "studentId");
  const snapshot = await db.doc(`schools/${schoolId}/students/${studentId}`).get();
  if (!snapshot.exists || String(snapshot.data()?.classId ?? "") !== classId) {
    throw new HttpsError("permission-denied", "The student is not assigned to that class.");
  }
}

export function requireStudentPayload(caller: CallableAuth, data: Record<string, unknown>): StudentPayload {
  assertPayloadSize(data, maxStudentPayloadBytes);
  const schoolId = String(data?.schoolId ?? "");
  const studentId = String(data?.studentId ?? "");
  assertFirestorePathSegment(schoolId, "schoolId");
  assertFirestorePathSegment(studentId, "studentId");
  assertSameSchool(caller, schoolId);
  if (caller.token.role === "student" && caller.token.studentId !== studentId) {
    throw new HttpsError("permission-denied", "Student token can only submit its own records.");
  }
  if (caller.token.role === "parent" && !caller.token.studentIds?.includes(studentId)) {
    throw new HttpsError("permission-denied", "Parent token can only submit linked child records.");
  }
  return { ...data, schoolId, studentId };
}

export function requireStudentAudioPayload(caller: CallableAuth, data: Record<string, unknown>): StudentAudioPayload {
  assertPayloadSize(data, 2_100_000);
  const schoolId = String(data?.schoolId ?? "");
  const studentId = String(data?.studentId ?? "");
  assertFirestorePathSegment(schoolId, "schoolId");
  assertFirestorePathSegment(studentId, "studentId");
  assertSameSchool(caller, schoolId);
  if (caller.token.role === "student" && caller.token.studentId !== studentId) {
    throw new HttpsError("permission-denied", "Student token can only submit its own records.");
  }
  if (caller.token.role === "parent" && !caller.token.studentIds?.includes(studentId)) {
    throw new HttpsError("permission-denied", "Parent token can only submit linked child records.");
  }
  const audioBase64 = trimmedString(data.audioBase64, 2_000_000);
  if (!audioBase64) throw new HttpsError("invalid-argument", "audioBase64 is required.");
  const mimeType = normalizeBuddyAudioMimeType(data.mimeType);
  return {
    ...data,
    schoolId,
    studentId,
    audioBase64,
    mimeType,
    fileName: trimmedString(data.fileName, 96) || defaultBuddyAudioFileName(mimeType),
    languageCode: normalizeSarvamLanguageCode(data.languageCode)
  };
}

export function normalizeBuddyAudioMimeType(value: unknown) {
  const mimeType = trimmedString(value, 64).toLowerCase();
  if (!mimeType) return "audio/wav";
  if (!/^audio\/[a-z0-9.+-]+$/.test(mimeType) && mimeType !== "video/mp4") {
    throw new HttpsError("invalid-argument", "mimeType must be a supported audio MIME type.");
  }
  return mimeType;
}

export function defaultBuddyAudioFileName(mimeType: string) {
  if (mimeType.includes("mpeg") || mimeType.includes("mp3")) return "buddy-turn.mp3";
  if (mimeType.includes("webm")) return "buddy-turn.webm";
  if (mimeType.includes("ogg")) return "buddy-turn.ogg";
  if (mimeType.includes("mp4")) return "buddy-turn.m4a";
  return "buddy-turn.wav";
}
