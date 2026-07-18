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
import { Role, accessCodePepper, auth, db } from "./runtime.js";
import { createCode } from "./accessCodeHelpers.js";
import { assertSameSchool, assertTeacherClassAccess, requireRole } from "./authorizationHelpers.js";
import { requiredTrimmedString, trimmedString } from "./inputHelpers.js";
import { defaultStudentPrivacySettings } from "./privacyHelpers.js";
import { assertEmail, assertFirestorePathSegment, assertPassword, assertRole, assertString, slugify, uniqueStrings } from "./sharedUtils.js";
import { getOrCreateTeacherUser, writeAudit } from "./studentRecordHelpers.js";

export const setUserRole = onCall(async (request) => {
  const caller = requireRole(request.auth, ["admin"]);
  const { uid, role, schoolId, classIds = [], studentIds = [] } = request.data ?? {};
  assertString(uid, "uid");
  assertRole(role);
  assertString(schoolId, "schoolId");
  assertSameSchool(caller, schoolId);

  const targetProfile = await db.doc(`users/${uid}`).get();
  if (!targetProfile.exists || String(targetProfile.data()?.schoolId ?? "") !== schoolId) {
    throw new HttpsError("permission-denied", "Roles can be changed only for an existing user in your school.");
  }

  const claims = { role, schoolId, classIds, studentIds };
  await auth.setCustomUserClaims(uid, claims);
  await db.doc(`users/${uid}`).set({
    uid,
    role,
    schoolId,
    classIds,
    studentIds,
    updatedAt: FieldValue.serverTimestamp()
  }, { merge: true });
  await writeAudit(schoolId, caller.uid, "role.set", `users/${uid}`);
  return { ok: true };
});

export const setStudentServiceTier = onCall(async (request) => {
  const caller = requireRole(request.auth, ["admin"]);
  const schoolId = requiredTrimmedString(request.data?.schoolId, "schoolId");
  const studentId = requiredTrimmedString(request.data?.studentId, "studentId");
  const requestedTier = trimmedString(request.data?.tier, 32).toLowerCase();
  if (requestedTier !== "free" && requestedTier !== "standard" && requestedTier !== "premium") {
    throw new HttpsError("invalid-argument", "tier must be free, standard, or premium.");
  }
  assertSameSchool(caller, schoolId);
  assertFirestorePathSegment(studentId, "studentId");
  const studentRef = db.doc(`schools/${schoolId}/students/${studentId}`);
  const snapshot = await studentRef.get();
  if (!snapshot.exists) {
    throw new HttpsError("not-found", "Student was not found.");
  }
  await studentRef.set({
    subscriptionTier: requestedTier,
    subscriptionTierUpdatedBy: caller.uid,
    subscriptionTierUpdatedAt: FieldValue.serverTimestamp(),
    updatedAt: FieldValue.serverTimestamp()
  }, { merge: true });
  await writeAudit(schoolId, caller.uid, "student.tier.set", studentRef.path);
  return { ok: true, schoolId, studentId, tier: requestedTier };
});

export const createTeacherInvite = onCall({ secrets: [accessCodePepper] }, async (request) => {
  const caller = requireRole(request.auth, ["admin"]);
  const { schoolId, teacherEmail, classIds = [] } = request.data ?? {};
  assertString(schoolId, "schoolId");
  assertString(teacherEmail, "teacherEmail");
  assertSameSchool(caller, schoolId);
  return createCode("teacherInvite", schoolId, caller.uid, { teacherEmail, classIds });
});

export const createTeacherAccount = onCall(async (request) => {
  const caller = requireRole(request.auth, ["admin"]);
  const { schoolId, teacherEmail, displayName, password, classIds = [] } = request.data ?? {};
  assertString(schoolId, "schoolId");
  assertEmail(teacherEmail, "teacherEmail");
  assertString(displayName, "displayName");
  assertPassword(password);
  assertSameSchool(caller, schoolId);

  const normalizedEmail = String(teacherEmail).trim().toLowerCase();
  const assignedClassIds = uniqueStrings(Array.isArray(classIds) ? classIds.map(String) : []);
  const user = await getOrCreateTeacherUser(
    normalizedEmail,
    String(displayName).trim(),
    String(password),
    schoolId);
  const claims = {
    role: "teacher" as Role,
    schoolId,
    classIds: assignedClassIds,
    studentIds: []
  };

  await auth.setCustomUserClaims(user.uid, claims);
  await db.doc(`users/${user.uid}`).set({
    uid: user.uid,
    email: normalizedEmail,
    displayName: String(displayName).trim(),
    role: "teacher",
    schoolId,
    classIds: assignedClassIds,
    studentIds: [],
    updatedAt: FieldValue.serverTimestamp(),
    createdAt: FieldValue.serverTimestamp()
  }, { merge: true });
  await db.doc(`schools/${schoolId}/teachers/${user.uid}`).set({
    uid: user.uid,
    schoolId,
    email: normalizedEmail,
    displayName: String(displayName).trim(),
    classIds: assignedClassIds,
    updatedAt: FieldValue.serverTimestamp(),
    createdAt: FieldValue.serverTimestamp()
  }, { merge: true });
  await writeAudit(schoolId, caller.uid, "teacher.create", `schools/${schoolId}/teachers/${user.uid}`);

  return {
    uid: user.uid,
    email: normalizedEmail,
    displayName: String(displayName).trim(),
    schoolId,
    classIds: assignedClassIds
  };
});

export const refreshTeacherClaims = onCall(async (request) => {
  if (!request.auth) {
    throw new HttpsError("unauthenticated", "Sign in required.");
  }
  const uid = request.auth.uid;
  const profileSnapshot = await db.doc(`users/${uid}`).get();
  if (!profileSnapshot.exists) {
    throw new HttpsError("permission-denied", "Teacher profile was not found.");
  }
  const profile = profileSnapshot.data() ?? {};
  if (String(profile.role ?? "") !== "teacher") {
    throw new HttpsError("permission-denied", "Only teacher profiles can refresh teacher claims.");
  }
  const schoolId = trimmedString(profile.schoolId, 160);
  const classIds = uniqueStrings(Array.isArray(profile.classIds) ? profile.classIds.map(String) : []);
  if (!schoolId || classIds.length === 0) {
    throw new HttpsError("failed-precondition", "Teacher profile is missing school or class assignment.");
  }
  const teacherSnapshot = await db.doc(`schools/${schoolId}/teachers/${uid}`).get();
  if (!teacherSnapshot.exists) {
    throw new HttpsError("permission-denied", "Teacher roster record was not found.");
  }
  const teacher = teacherSnapshot.data() ?? {};
  const authUser = await auth.getUser(uid);
  const profileEmail = trimmedString(profile.email, 320).toLowerCase();
  const teacherEmail = trimmedString(teacher.email, 320).toLowerCase();
  const authEmail = (authUser.email ?? "").toLowerCase();
  if ((profileEmail && authEmail && profileEmail !== authEmail) ||
      (teacherEmail && authEmail && teacherEmail !== authEmail)) {
    throw new HttpsError("permission-denied", "Teacher email does not match the signed-in account.");
  }
  const claims = {
    role: "teacher" as Role,
    schoolId,
    classIds,
    studentIds: []
  };
  await auth.setCustomUserClaims(uid, claims);
  await db.doc(`users/${uid}`).set({
    uid,
    role: "teacher",
    schoolId,
    classIds,
    studentIds: [],
    claimsRefreshedAt: FieldValue.serverTimestamp(),
    updatedAt: FieldValue.serverTimestamp()
  }, { merge: true });
  await writeAudit(schoolId, uid, "teacher.claims.refresh", `users/${uid}`);
  return { ok: true, schoolId, classIds };
});

export const createClass = onCall(async (request) => {
  const caller = requireRole(request.auth, ["admin", "teacher"]);
  const { schoolId, name } = request.data ?? {};
  assertString(schoolId, "schoolId");
  assertString(name, "name");
  assertSameSchool(caller, schoolId);

  const classRef = db.collection(`schools/${schoolId}/classes`).doc(slugify(String(name)));
  await classRef.set({
    id: classRef.id,
    schoolId,
    name,
    studentIds: [],
    createdAt: FieldValue.serverTimestamp(),
    updatedAt: FieldValue.serverTimestamp()
  }, { merge: true });

  let customToken: string | undefined;
  if (caller.token.role === "teacher") {
    const classIds = uniqueStrings([...(caller.token.classIds ?? []), classRef.id]);
    const claims = {
      role: "teacher" as Role,
      schoolId,
      classIds,
      studentIds: []
    };
    await auth.setCustomUserClaims(caller.uid, claims);
    await db.doc(`users/${caller.uid}`).set({
      uid: caller.uid,
      role: "teacher",
      schoolId,
      classIds,
      studentIds: [],
      updatedAt: FieldValue.serverTimestamp()
    }, { merge: true });
    await db.doc(`schools/${schoolId}/teachers/${caller.uid}`).set({
      uid: caller.uid,
      schoolId,
      classIds,
      updatedAt: FieldValue.serverTimestamp()
    }, { merge: true });
    customToken = await auth.createCustomToken(caller.uid, claims);
  }

  await writeAudit(schoolId, caller.uid, "class.create", classRef.path);
  return { id: classRef.id, schoolId, name, studentIds: [], customToken };
});

export const createStudent = onCall(async (request) => {
  const caller = requireRole(request.auth, ["admin", "teacher"]);
  const { schoolId, classId, name, email, parentEmail, password, avatarColor = "#7fc8ff" } = request.data ?? {};
  assertString(schoolId, "schoolId");
  assertString(classId, "classId");
  assertString(name, "name");
  assertEmail(email, "email");
  assertEmail(parentEmail, "parentEmail");
  assertPassword(password);
  assertSameSchool(caller, schoolId);
  assertTeacherClassAccess(caller, classId);

  const studentRef = db.collection(`schools/${schoolId}/students`).doc();
  const uid = `student_${studentRef.id}`;
  const claims = {
    role: "student" as Role,
    schoolId,
    classIds: [classId],
    studentIds: [studentRef.id],
    studentId: studentRef.id
  };
  const normalizedEmail = String(email).trim().toLowerCase();
  const normalizedParentEmail = String(parentEmail).trim().toLowerCase();

  try {
    await auth.createUser({
      uid,
      email: normalizedEmail,
      password: String(password),
      displayName: String(name).trim(),
      emailVerified: false
    });
  } catch (error) {
    const code = typeof error === "object" && error !== null && "code" in error ? String((error as { code?: unknown }).code) : "";
    if (code === "auth/email-already-exists") {
      throw new HttpsError("already-exists", "A Firebase account already exists for that student email.");
    }
    throw error;
  }

  await auth.setCustomUserClaims(uid, claims);
  await studentRef.set({
    id: studentRef.id,
    schoolId,
    classId,
    name,
    authUid: uid,
    email: normalizedEmail,
    parentEmail: normalizedParentEmail,
    avatarColor,
    subscriptionTier: "free",
    privacy: defaultStudentPrivacySettings(),
    createdAt: FieldValue.serverTimestamp(),
    updatedAt: FieldValue.serverTimestamp()
  });
  await db.doc(`users/${uid}`).set({
    uid,
    email: normalizedEmail,
    displayName: String(name).trim(),
    role: "student",
    schoolId,
    classIds: claims.classIds,
    studentIds: claims.studentIds,
    studentId: studentRef.id,
    parentEmail: normalizedParentEmail,
    createdAt: FieldValue.serverTimestamp(),
    updatedAt: FieldValue.serverTimestamp()
  });
  await db.doc(`schools/${schoolId}/classes/${classId}`).set({
    studentIds: FieldValue.arrayUnion(studentRef.id),
    updatedAt: FieldValue.serverTimestamp()
  }, { merge: true });
  await writeAudit(schoolId, caller.uid, "student.create", studentRef.path);
  return {
    id: studentRef.id,
    schoolId,
    classId,
    name,
    authUid: uid,
    email: normalizedEmail,
    parentEmail: normalizedParentEmail,
    avatarColor,
    subscriptionTier: "free",
    privacy: defaultStudentPrivacySettings()
  };
});

/** Returns policy state without exposing any learner activity or conversation text. */
