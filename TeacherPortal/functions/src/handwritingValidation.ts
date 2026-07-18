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
import { StudentPayload, handwritingRawStrokeRetentionDays } from "./runtime.js";
import { trimmedString } from "./inputHelpers.js";
import { assertString, cloneRecord, finiteInteger, finiteNumber } from "./sharedUtils.js";

export function validateLetterAttemptPayload(payload: Record<string, unknown>) {
  assertString(payload.attemptId, "attemptId");
  assertString(payload.letter, "letter");
  if (String(payload.letter).trim().length !== 1 || !/[A-Za-z]/.test(String(payload.letter))) {
    throw new HttpsError("invalid-argument", "letter must be one alphabetic character.");
  }
  const outcome = trimmedString(payload.assessmentOutcome, 24);
  if (outcome && outcome !== "Accept" && outcome !== "Retry" && outcome !== "Reject") {
    throw new HttpsError("invalid-argument", "assessmentOutcome is invalid.");
  }
  validateHandwritingSampleMetadata(payload);
  const points = Array.isArray(payload.points) ? payload.points : [];
  if (points.length > 256) {
    throw new HttpsError("invalid-argument", "Handwriting evidence exceeds the 256 point limit.");
  }
  const coordinateSpace = trimmedString(payload.normalizedCoordinateSpace, 40);
  if (coordinateSpace && coordinateSpace !== "attempt_bounds_v1") {
    throw new HttpsError("invalid-argument", "normalizedCoordinateSpace is invalid.");
  }
  let expectedPointOrder = 0;
  let previousPointTime = 0;
  for (const value of points) {
    const point = cloneRecord(value);
    if (typeof point.x !== "number" || !Number.isFinite(point.x) ||
        typeof point.y !== "number" || !Number.isFinite(point.y)) {
      throw new HttpsError("invalid-argument", "Handwriting evidence contains a non-numeric point.");
    }
    const x = finiteNumber(point.x);
    const y = finiteNumber(point.y);
    const nx = point.nx === undefined ? 0 : finiteNumber(point.nx);
    const ny = point.ny === undefined ? 0 : finiteNumber(point.ny);
    validateRichHandwritingPoint(point);
    const strokeId = finiteInteger(point.strokeId);
    const order = finiteInteger(point.order);
    const pointTime = point.tMs === undefined ? previousPointTime : finiteNumber(point.tMs);
    if (!Number.isFinite(x) || !Number.isFinite(y) || Math.abs(x) > 10000 || Math.abs(y) > 10000 ||
        nx < 0 || nx > 1 || ny < 0 || ny > 1 ||
        strokeId < 0 || strokeId > 64 || order !== expectedPointOrder || order > 255 || pointTime < previousPointTime) {
      throw new HttpsError("invalid-argument", "Handwriting evidence contains an invalid point.");
    }
    expectedPointOrder += 1;
    previousPointTime = pointTime;
  }

  assertBoundedOptionalNumber(payload.inputAssistFraction, "inputAssistFraction", 0, 1);
  assertBoundedOptionalNumber(payload.inputAssistMeanDistance, "inputAssistMeanDistance", 0, 10000);
  assertBoundedOptionalNumber(payload.inputAssistMaxDistance, "inputAssistMaxDistance", 0, 10000);
  assertBoundedOptionalNumber(payload.calibrationSeparation, "calibrationSeparation", -5000, 5000);
  validateHandwritingDiagnostics(payload.handwritingDiagnostics);
}

export function assertBoundedOptionalNumber(
  value: unknown,
  field: string,
  minimum: number,
  maximum: number
) {
  if (value === undefined || value === null)
    return;
  if (typeof value !== "number" || !Number.isFinite(value) || value < minimum || value > maximum) {
    throw new HttpsError("invalid-argument", `${field} is outside its allowed range.`);
  }
}

export function validateHandwritingDiagnostics(value: unknown) {
  if (value === undefined || value === null)
    return;
  const diagnostics = cloneRecord(value);
  const tags = Array.isArray(diagnostics.tags) ? diagnostics.tags : [];
  const evidence = Array.isArray(diagnostics.evidence) ? diagnostics.evidence : [];
  if (tags.length > 24 || evidence.length > 24) {
    throw new HttpsError("invalid-argument", "Handwriting diagnostics contain too many findings.");
  }
  for (const tag of tags) {
    if (typeof tag !== "string" || tag.length === 0 || tag.length > 40) {
      throw new HttpsError("invalid-argument", "Handwriting diagnostics contain an invalid tag.");
    }
  }
  for (const itemValue of evidence) {
    const item = cloneRecord(itemValue);
    if (typeof item.tag !== "string" || item.tag.length === 0 || item.tag.length > 40 ||
        typeof item.source !== "string" || item.source.length > 64 ||
        typeof item.actionable !== "boolean") {
      throw new HttpsError("invalid-argument", "Handwriting diagnostic evidence is malformed.");
    }
    assertBoundedOptionalNumber(item.confidence, "diagnostic evidence confidence", 0, 1);
  }
  assertBoundedOptionalNumber(diagnostics.assessmentConfidence, "assessmentConfidence", 0, 1);
  assertBoundedOptionalNumber(diagnostics.diagnosticReliability, "diagnosticReliability", 0, 1);
  assertBoundedOptionalNumber(diagnostics.primaryHintConfidence, "primaryHintConfidence", 0, 1);
  assertBoundedOptionalNumber(diagnostics.mirrorConfidence, "mirrorConfidence", 0, 1);
  assertBoundedOptionalNumber(diagnostics.inputAssistFraction, "diagnostic inputAssistFraction", 0, 1);
  assertBoundedOptionalNumber(diagnostics.inputAssistMeanDistance, "diagnostic inputAssistMeanDistance", 0, 10000);
  assertBoundedOptionalNumber(diagnostics.inputAssistMaxDistance, "diagnostic inputAssistMaxDistance", 0, 10000);
  assertBoundedOptionalNumber(diagnostics.calibrationSeparation, "diagnostic calibrationSeparation", -5000, 5000);
}

export function validateAcceptedHandwritingTemplatePayload(payload: Record<string, unknown>) {
  assertString(payload.templateId, "templateId");
  assertString(payload.letter, "letter");
  validateHandwritingSampleMetadata(payload);
  const points = Array.isArray(payload.points) ? payload.points : [];
  if (points.length === 0 || points.length > 512) {
    throw new HttpsError("invalid-argument", "Accepted handwriting evidence must contain 1 to 512 points.");
  }
  if (trimmedString(payload.normalizedCoordinateSpace, 40) !== "attempt_bounds_v1") {
    throw new HttpsError("invalid-argument", "Accepted handwriting must use attempt_bounds_v1 coordinates.");
  }
  validateHandwritingPoints(points, 511);
}

export function validateHandwritingPoints(points: unknown[], maximumOrder: number) {
  let expectedOrder = 0;
  let previousTime = 0;
  for (const value of points) {
    const point = cloneRecord(value);
    if (typeof point.x !== "number" || !Number.isFinite(point.x) ||
        typeof point.y !== "number" || !Number.isFinite(point.y)) {
      throw new HttpsError("invalid-argument", "Handwriting evidence contains a non-numeric point.");
    }
    const x = finiteNumber(point.x);
    const y = finiteNumber(point.y);
    const nx = point.nx === undefined ? 0 : finiteNumber(point.nx);
    const ny = point.ny === undefined ? 0 : finiteNumber(point.ny);
    validateRichHandwritingPoint(point);
    const strokeId = finiteInteger(point.strokeId);
    const order = finiteInteger(point.order);
    const pointTime = point.tMs === undefined ? previousTime : finiteNumber(point.tMs);
    if (Math.abs(x) > 10000 || Math.abs(y) > 10000 || nx < 0 || nx > 1 || ny < 0 || ny > 1 ||
        strokeId < 0 || strokeId > 64 || order !== expectedOrder || order > maximumOrder || pointTime < previousTime) {
      throw new HttpsError("invalid-argument", "Handwriting evidence contains an invalid point.");
    }
    expectedOrder += 1;
    previousTime = pointTime;
  }
}

export function validateHandwritingSampleMetadata(payload: Record<string, unknown>) {
  const schemaVersion = trimmedString(payload.sampleSchemaVersion, 48);
  if (schemaVersion && schemaVersion !== "handwriting_sample_v1") {
    throw new HttpsError("invalid-argument", "Unsupported handwriting sample schema.");
  }
  if (schemaVersion) {
    for (const required of [
      "sampleId", "writerId", "captureSessionId", "captureSource", "captureStartedAtUtc",
      "rawCoordinateSystem", "normalizedCoordinateSystem"
    ] as const)
      assertString(payload[required], required);
    if (!Number.isFinite(Date.parse(String(payload.captureStartedAtUtc)))) {
      throw new HttpsError("invalid-argument", "captureStartedAtUtc must be an ISO timestamp.");
    }
  }
  for (const [field, maximum] of [
    ["sampleId", 80], ["writerId", 128], ["captureSessionId", 128],
    ["captureSource", 64], ["captureStartedAtUtc", 48],
    ["rawCoordinateSystem", 48], ["normalizedCoordinateSystem", 48],
    ["writerAgeBand", 24], ["handedness", 16],
    ["collectionCohort", 80], ["consentReference", 128]
  ] as const) {
    const value = payload[field];
    if (value !== undefined && (typeof value !== "string" || value.length > maximum)) {
      throw new HttpsError("invalid-argument", `${field} is invalid.`);
    }
  }
  const handedness = trimmedString(payload.handedness, 16);
  if (handedness && !["unknown", "left", "right", "mixed"].includes(handedness)) {
    throw new HttpsError("invalid-argument", "handedness is invalid.");
  }
  const normalizedCoordinates = trimmedString(payload.normalizedCoordinateSystem, 48);
  if (normalizedCoordinates && normalizedCoordinates !== "unit_square_bottom_left_y_up") {
    throw new HttpsError("invalid-argument", "normalizedCoordinateSystem is invalid.");
  }
  const rawCoordinates = trimmedString(payload.rawCoordinateSystem, 48);
  if (rawCoordinates && !["canvas_center_y_up", "canvas_top_left_y_down"].includes(rawCoordinates)) {
    throw new HttpsError("invalid-argument", "rawCoordinateSystem is invalid.");
  }
  assertBoundedOptionalNumber(payload.captureDurationMs, "captureDurationMs", 0, 3600000);
  if (payload.captureDevice !== undefined && payload.captureDevice !== null) {
    const device = cloneRecord(payload.captureDevice);
    for (const field of ["platform", "operatingSystem", "deviceModel", "deviceType"] as const) {
      if (device[field] !== undefined && (typeof device[field] !== "string" || device[field].length > 160)) {
        throw new HttpsError("invalid-argument", `captureDevice.${field} is invalid.`);
      }
    }
    assertBoundedOptionalNumber(device.screenWidth, "captureDevice.screenWidth", 0, 20000);
    assertBoundedOptionalNumber(device.screenHeight, "captureDevice.screenHeight", 0, 20000);
    assertBoundedOptionalNumber(device.screenDpi, "captureDevice.screenDpi", 0, 2000);
    assertBoundedOptionalNumber(device.canvasWidth, "captureDevice.canvasWidth", 0, 20000);
    assertBoundedOptionalNumber(device.canvasHeight, "captureDevice.canvasHeight", 0, 20000);
    for (const field of ["pressureObserved", "tiltObserved"] as const) {
      if (device[field] !== undefined && typeof device[field] !== "boolean") {
        throw new HttpsError("invalid-argument", `captureDevice.${field} must be boolean.`);
      }
    }
  }
}

export function validateRichHandwritingPoint(point: Record<string, unknown>) {
  for (const field of ["nx", "ny"] as const) {
    if (point[field] !== undefined && (typeof point[field] !== "number" || !Number.isFinite(point[field]))) {
      throw new HttpsError("invalid-argument", `point.${field} must be numeric.`);
    }
  }
  for (const [field, minimum, maximum] of [
    ["canvasX", 0, 1], ["canvasY", 0, 1],
    ["tMs", 0, 3600000], ["deltaMs", 0, 60000],
    ["pressure", -1, 1], ["altitudeAngle", -1, 7], ["azimuthAngle", -1, 7],
    ["pointerId", -1, 64]
  ] as const)
    assertBoundedOptionalNumber(point[field], `point.${field}`, minimum, maximum);
  for (const field of ["tMs", "deltaMs", "pointerId"] as const) {
    if (point[field] !== undefined && !Number.isInteger(point[field])) {
      throw new HttpsError("invalid-argument", `point.${field} must be an integer.`);
    }
  }
  const inputType = trimmedString(point.inputType, 16);
  if (inputType && !["mouse", "touch", "stylus", "unknown"].includes(inputType)) {
    throw new HttpsError("invalid-argument", "Handwriting point inputType is invalid.");
  }
}

export function withHandwritingRetention(payload: StudentPayload): StudentPayload {
  if (payload.rawStrokeCaptured !== true || !Array.isArray(payload.points) || payload.points.length === 0)
    return payload;
  return {
    ...payload,
    rawStrokeCaptured: true,
    rawStrokePointCount: payload.points.length,
    rawStrokeExpiresAtUtc: new Date(Date.now() + handwritingRawStrokeRetentionDays * 86400000).toISOString(),
    rawStrokeRetentionPolicy: `school_evidence_${handwritingRawStrokeRetentionDays}_days`
  };
}
