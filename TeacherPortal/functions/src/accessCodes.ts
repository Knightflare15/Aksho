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
import { accessCodePepper, auth, db } from "./runtime.js";
import { createCode, enforceAccessCodeRateLimit, findActiveCode, markCodeRedeemed, normalizedCode } from "./accessCodeHelpers.js";
import { assertSameSchool, assertStudentClassBinding, assertTeacherClassAccess, requireRole } from "./authorizationHelpers.js";
import { assertString } from "./sharedUtils.js";
import { writeAudit } from "./studentRecordHelpers.js";

export const createParentAccessCode = onCall({ secrets: [accessCodePepper] }, async (request) => {
  const caller = requireRole(request.auth, ["admin", "teacher"]);
  const { schoolId, classId, studentId } = request.data ?? {};
  assertString(schoolId, "schoolId");
  assertString(classId, "classId");
  assertString(studentId, "studentId");
  assertSameSchool(caller, schoolId);
  assertTeacherClassAccess(caller, classId);
  await assertStudentClassBinding(schoolId, classId, studentId);
  return createCode("parent", schoolId, caller.uid, { classId, studentId });
});

export const createStudentAccessCode = onCall({ secrets: [accessCodePepper] }, async (request) => {
  const caller = requireRole(request.auth, ["admin", "teacher"]);
  const { schoolId, classId, studentId } = request.data ?? {};
  assertString(schoolId, "schoolId");
  assertString(classId, "classId");
  assertString(studentId, "studentId");
  assertSameSchool(caller, schoolId);
  assertTeacherClassAccess(caller, classId);
  await assertStudentClassBinding(schoolId, classId, studentId);
  return createCode("student", schoolId, caller.uid, { classId, studentId });
});

export const redeemParentCode = onCall({ secrets: [accessCodePepper] }, async (request) => {
  await enforceAccessCodeRateLimit("parent", request.auth?.uid ?? request.rawRequest.ip ?? "unknown");
  const code = normalizedCode(request.data?.code);
  const codeDoc = await findActiveCode("parent", code);
  const data = codeDoc.data();
  if (!data?.schoolId || !data?.studentId) {
    throw new HttpsError("failed-precondition", "Parent code is missing required school or student binding.");
  }

  const uid = request.auth?.uid ?? `parent_${codeDoc.id}`;
  const claims = {
    role: "parent",
    schoolId: data.schoolId,
    classIds: data.classId ? [data.classId] : [],
    studentIds: [data.studentId]
  };
  await auth.setCustomUserClaims(uid, claims);
  await db.doc(`users/${uid}`).set({
    uid,
    displayName: "Parent",
    role: "parent",
    schoolId: data.schoolId,
    classIds: claims.classIds,
    studentIds: claims.studentIds,
    updatedAt: FieldValue.serverTimestamp(),
    createdAt: FieldValue.serverTimestamp()
  }, { merge: true });
  await markCodeRedeemed(codeDoc.ref.path, uid);
  await writeAudit(data.schoolId, uid, "code.redeem.parent", codeDoc.ref.path);
  return {
    customToken: await auth.createCustomToken(uid, claims),
    role: "parent",
    schoolId: data.schoolId,
    classId: data.classId,
    studentId: data.studentId
  };
});

export const redeemTeacherInvite = onCall({ secrets: [accessCodePepper] }, async (request) => {
  if (!request.auth) {
    throw new HttpsError("unauthenticated", "Create or sign in to a teacher account first.");
  }

  await enforceAccessCodeRateLimit("teacherInvite", request.auth.uid);

  const code = normalizedCode(request.data?.code);
  const codeDoc = await findActiveCode("teacherInvite", code);
  const data = codeDoc.data();
  if (!data?.schoolId || !data?.teacherEmail) {
    throw new HttpsError("failed-precondition", "Teacher invite is missing required bindings.");
  }

  const user = await auth.getUser(request.auth.uid);
  if (!user.email || user.email.toLowerCase() !== String(data.teacherEmail).toLowerCase()) {
    throw new HttpsError("permission-denied", "This invite is for a different teacher email.");
  }

  const classIds = Array.isArray(data.classIds) ? data.classIds : [];
  const claims = {
    role: "teacher",
    schoolId: data.schoolId,
    classIds,
    studentIds: []
  };
  await auth.setCustomUserClaims(request.auth.uid, claims);
  await db.doc(`users/${request.auth.uid}`).set({
    uid: request.auth.uid,
    email: user.email,
    displayName: user.displayName ?? user.email,
    role: "teacher",
    schoolId: data.schoolId,
    classIds,
    studentIds: [],
    updatedAt: FieldValue.serverTimestamp(),
    createdAt: FieldValue.serverTimestamp()
  }, { merge: true });
  await db.doc(`schools/${data.schoolId}/teachers/${request.auth.uid}`).set({
    uid: request.auth.uid,
    schoolId: data.schoolId,
    email: user.email,
    displayName: user.displayName ?? user.email,
    classIds,
    updatedAt: FieldValue.serverTimestamp(),
    createdAt: FieldValue.serverTimestamp()
  }, { merge: true });
  await markCodeRedeemed(codeDoc.ref.path, request.auth.uid);
  await writeAudit(data.schoolId, request.auth.uid, "code.redeem.teacher", codeDoc.ref.path);
  return {
    customToken: await auth.createCustomToken(request.auth.uid, claims),
    role: "teacher",
    schoolId: data.schoolId,
    classIds
  };
});

export const getLinkedChildSession = onCall(async (request) => {
  const caller = requireRole(request.auth, ["parent", "student"]);
  const schoolId = String(caller.token.schoolId ?? "");
  assertString(schoolId, "schoolId");

  const studentId = caller.token.role === "student"
    ? String(caller.token.studentId ?? "")
    : String(caller.token.studentIds?.[0] ?? "");
  assertString(studentId, "studentId");

  const studentSnap = await db.doc(`schools/${schoolId}/students/${studentId}`).get();
  if (!studentSnap.exists) {
    throw new HttpsError("not-found", "Linked child was not found.");
  }

  const student = studentSnap.data() ?? {};
  return {
    role: caller.token.role,
    schoolId,
    classId: student.classId,
    studentId,
    studentName: student.name
  };
});

export const redeemStudentCode = onCall({ secrets: [accessCodePepper] }, async (request) => {
  await enforceAccessCodeRateLimit("student", request.auth?.uid ?? request.rawRequest.ip ?? "unknown");
  const code = normalizedCode(request.data?.code);
  const codeDoc = await findActiveCode("student", code);
  const data = codeDoc.data();
  if (!data?.schoolId || !data?.studentId) {
    throw new HttpsError("failed-precondition", "Student code is missing required school or student binding.");
  }

  const uid = `student_${data.studentId}`;
  const claims = {
    role: "student",
    schoolId: data.schoolId,
    classIds: data.classId ? [data.classId] : [],
    studentIds: [data.studentId],
    studentId: data.studentId
  };
  await auth.setCustomUserClaims(uid, claims);
  await db.doc(`users/${uid}`).set({
    uid,
    displayName: "Student",
    role: "student",
    schoolId: data.schoolId,
    classIds: claims.classIds,
    studentIds: claims.studentIds,
    updatedAt: FieldValue.serverTimestamp(),
    createdAt: FieldValue.serverTimestamp()
  }, { merge: true });
  await markCodeRedeemed(codeDoc.ref.path, uid);
  await writeAudit(data.schoolId, uid, "code.redeem.student", codeDoc.ref.path);
  return {
    customToken: await auth.createCustomToken(uid, claims),
    role: "student",
    schoolId: data.schoolId,
    classId: data.classId,
    studentId: data.studentId
  };
});
