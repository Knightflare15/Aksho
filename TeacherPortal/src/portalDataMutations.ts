import {
  arrayUnion,
  collection,
  doc,
  getDoc,
  getDocs,
  limit,
  orderBy,
  query,
  setDoc,
  where
} from "firebase/firestore";
import { httpsCallable } from "firebase/functions";
import { createUserWithEmailAndPassword, getAuth, signInWithCustomToken, updateProfile } from "firebase/auth";
import { deleteApp, initializeApp } from "firebase/app";
import {
  acceptedHandwritingTemplates as demoTemplates,
  buddyConversationTurns as demoBuddyConversationTurns,
  classroom as demoClassroom,
  colorMiniGameAttempts as demoColorMiniGameAttempts,
  countingMiniGameAttempts as demoCountingMiniGameAttempts,
  empathyEventAttempts as demoEmpathyEventAttempts,
  grammarBattleEvents as demoGrammarBattleEvents,
  gymAttempts as demoGymAttempts,
  initialMission,
  letterAttempts as demoLetterAttempts,
  recommendations as demoRecommendations,
  runSessions as demoRunSessions,
  school as demoSchool,
  spokenPhraseEvents as demoSpokenPhraseEvents,
  students as demoStudents,
  wordCasts as demoWordCasts,
  worldGoals as demoWorldGoals,
  writtenPhraseEvents as demoWrittenPhraseEvents
} from "./demoData";
import { auth, db, firebaseConfig, functions, hasFirebaseConfig, useFunctionsWorkflow } from "./firebase";
import type {
  AcceptedHandwritingTemplate,
  BuddyConversationTurn,
  Classroom,
  ColorMiniGameAttempt,
  CountingMiniGameAttempt,
  DailyMissionAssignment,
  EmpathyEventAttempt,
  GrammarBattleEvent,
  GymAttempt,
  LetterAttempt,
  ParentStudentSummary,
  Recommendation,
  RunSession,
  School,
  SpokenPhraseEvent,
  Student,
  StudentPrivacySettings,
  StudentMissionOverride,
  TeacherProfile,
  UserProfile,
  WordCast,
  WorldGoalAssignment,
  WorldGoalClaim,
  WrittenPhraseEvent
} from "./types";

import type {
  OperationsSummary,
  PortalDataset,
  RedeemCodeResult,
  StudentPrivacyDraft,
  StudentPrivacyStatus,
} from "./portalData";
import { normalizeMission } from "./portalDataLoads";

export async function saveSchool(school: School): Promise<void> {
  if (!db || !hasFirebaseConfig) {
    return;
  }

  await setDoc(doc(db, "schools", school.id), school, { merge: true });
}

export async function saveDailyMission(mission: DailyMissionAssignment): Promise<void> {
  if (!db || !hasFirebaseConfig) {
    return;
  }

  await setDoc(
    doc(db, "schools", mission.schoolId, "classes", mission.classId, "dailyMissions", mission.date),
    mission,
    { merge: true }
  );
}

export async function saveWorldGoal(goal: WorldGoalAssignment): Promise<void> {
  if (!db || !hasFirebaseConfig) {
    return;
  }

  const normalizedGoal = {
    ...goal,
    rewardCoins: Math.max(0, goal.rewardCoins ?? 25),
    schoolTimeZone: goal.schoolTimeZone || "Asia/Kolkata",
    assignedAtUtc: goal.assignedAtUtc || new Date().toISOString()
  };
  const target = goal.studentId
    ? doc(db, "schools", goal.schoolId, "students", goal.studentId, "worldGoals", goal.goalId)
    : doc(db, "schools", goal.schoolId, "classes", goal.classId, "worldGoals", goal.goalId);
  const unityLookupTarget = goal.studentId
    ? doc(db, "schools", goal.schoolId, "students", goal.studentId, "worldGoals", goal.weekStart)
    : doc(db, "schools", goal.schoolId, "classes", goal.classId, "worldGoals", goal.weekStart);

  // Unity's offline-friendly REST client resolves the current weekly goal by
  // weekStart, while the portal uses a stable semantic goalId. Keep both
  // document aliases synchronized so either client can resolve the same goal.
  await Promise.all([
    setDoc(target, normalizedGoal, { merge: true }),
    setDoc(unityLookupTarget, normalizedGoal, { merge: true })
  ]);
}

export async function loadWorldGoal(input: {
  schoolId: string;
  classId: string;
  weekStart: string;
  teacherId: string;
}): Promise<WorldGoalAssignment> {
  const goalId = buildWorldGoalId(input.classId, input.weekStart);
  if (!db || !hasFirebaseConfig) {
    const demoGoal = demoWorldGoals.find((goal) => (
      goal.classId === input.classId &&
      goal.weekStart === input.weekStart &&
      !goal.studentId
    ));
    return demoGoal ?? buildDefaultWorldGoal(input.schoolId, input.classId, input.weekStart, input.teacherId);
  }

  const snap = await getDoc(doc(db, "schools", input.schoolId, "classes", input.classId, "worldGoals", goalId));
  return snap.exists()
    ? ({ goalId: snap.id, ...snap.data() } as WorldGoalAssignment)
    : buildDefaultWorldGoal(input.schoolId, input.classId, input.weekStart, input.teacherId);
}

export async function loadDailyMission(input: {
  schoolId: string;
  classId: string;
  date: string;
}): Promise<DailyMissionAssignment> {
  if (!db || !hasFirebaseConfig) {
    return normalizeMission({ ...initialMission, schoolId: input.schoolId, classId: input.classId, date: input.date, id: input.date });
  }

  const snap = await getDoc(doc(db, "schools", input.schoolId, "classes", input.classId, "dailyMissions", input.date));
  return snap.exists()
    ? normalizeMission({ id: snap.id, ...snap.data() } as DailyMissionAssignment)
    : normalizeMission({ ...initialMission, schoolId: input.schoolId, classId: input.classId, date: input.date, id: input.date });
}

export async function saveStudentMissionOverride(override: StudentMissionOverride): Promise<void> {
  if (!db || !hasFirebaseConfig) {
    return;
  }

  await setDoc(
    doc(db, "schools", override.schoolId, "students", override.studentId, "studentMissionOverrides", override.date),
    override,
    { merge: true }
  );
}

export async function saveMissionRange(mission: DailyMissionAssignment, days: number): Promise<void> {
  if (!db || !hasFirebaseConfig) {
    return;
  }

  const start = new Date(`${mission.date}T00:00:00`);
  const writes: Promise<void>[] = [];
  for (let index = 0; index < days; index++) {
    const date = new Date(start);
    date.setDate(start.getDate() + index);
    const dateKey = date.toISOString().slice(0, 10);
    writes.push(saveDailyMission({
      ...mission,
      id: dateKey,
      date: dateKey
    }));
  }

  await Promise.all(writes);
}

export async function redeemParentCode(code: string): Promise<RedeemCodeResult> {
  if (!functions || !hasFirebaseConfig) {
    return { role: "parent", schoolId: demoSchool.id, studentId: demoStudents[0].id };
  }

  const callable = httpsCallable<{ code: string }, RedeemCodeResult>(functions, "redeemParentCode");
  const result = await callable({ code });
  return result.data;
}

export async function redeemTeacherInvite(code: string): Promise<RedeemCodeResult> {
  if (!functions || !hasFirebaseConfig) {
    return { role: "teacher", schoolId: demoSchool.id, classIds: [demoClassroom.id] };
  }

  const callable = httpsCallable<{ code: string }, RedeemCodeResult>(functions, "redeemTeacherInvite");
  const result = await callable({ code });
  return result.data;
}

export async function createClassroom(input: {
  schoolId: string;
  name: string;
}): Promise<Classroom> {
  if (!hasFirebaseConfig || !db) {
    return { ...demoClassroom, id: input.name.toLowerCase().replace(/\s+/g, "-"), name: input.name };
  }

  if (useFunctionsWorkflow && functions) {
    const callable = httpsCallable<typeof input, Classroom>(functions, "createClass");
    const result = await callable(input);
    if (result.data.customToken && auth) {
      await signInWithCustomToken(auth, result.data.customToken);
    }
    const { customToken: _customToken, ...classroom } = result.data;
    return classroom;
  }

  const now = new Date().toISOString();
  const classroom: Classroom = {
    id: slugify(input.name),
    schoolId: input.schoolId,
    name: input.name.trim(),
    studentIds: []
  };
  await setDoc(doc(db, "schools", input.schoolId, "classes", classroom.id), {
    ...classroom,
    createdAt: now,
    updatedAt: now
  }, { merge: true });
  return classroom;
}

export async function createStudentRecord(input: {
  schoolId: string;
  classId: string;
  name: string;
  email: string;
  parentEmail: string;
  password: string;
  avatarColor: string;
}): Promise<Student> {
  if (!hasFirebaseConfig || !db) {
    return {
      id: `demo-student-${Date.now()}`,
      schoolId: input.schoolId,
      classId: input.classId,
      name: input.name,
      email: input.email,
      parentEmail: input.parentEmail,
      avatarColor: input.avatarColor,
      subscriptionTier: "free"
    };
  }

  if (useFunctionsWorkflow && functions) {
    const callable = httpsCallable<typeof input, Student>(functions, "createStudent");
    const result = await callable(input);
    return result.data;
  }

  const now = new Date().toISOString();
  const studentRef = doc(collection(db, "schools", input.schoolId, "students"));
  const account = await createAuthAccount(input.email, input.password, input.name);
  const student: Student = {
    id: studentRef.id,
    schoolId: input.schoolId,
    classId: input.classId,
    name: input.name,
    authUid: account.uid,
    email: input.email.trim().toLowerCase(),
    parentEmail: input.parentEmail.trim().toLowerCase(),
    avatarColor: input.avatarColor,
    subscriptionTier: "free"
  };

  await setDoc(studentRef, {
    ...student,
    createdAt: now,
    updatedAt: now
  });
  await setDoc(doc(db, "users", account.uid), {
    uid: account.uid,
    email: student.email,
    displayName: input.name,
    role: "student",
    schoolId: input.schoolId,
    classIds: [input.classId],
    studentIds: [studentRef.id],
    studentId: studentRef.id,
    parentEmail: student.parentEmail,
    createdAt: now,
    updatedAt: now
  });
  await setDoc(doc(db, "schools", input.schoolId, "classes", input.classId), {
    studentIds: arrayUnion(studentRef.id),
    updatedAt: now
  }, { merge: true });
  return student;
}

export async function getStudentPrivacyStatus(input: { schoolId: string; studentId: string }): Promise<StudentPrivacyStatus> {
  if (!functions || !useFunctionsWorkflow) {
    return {
      ...input,
      policyVersion: "demo",
      requiresRenewal: false,
      deletionStatus: "",
      deleteAfterUtc: "",
      privacy: {
        consentStatus: "pending",
        policyVersion: "demo",
        gameplayAnalyticsAllowed: false,
        buddyAllowed: false,
        audioProcessingAllowed: false,
        handwritingEvidenceAllowed: false,
        diagnosticsAllowed: false,
        consentedAtUtc: "",
        consentSource: ""
      }
    };
  }
  const callable = httpsCallable<typeof input, StudentPrivacyStatus>(functions, "getStudentPrivacyStatus");
  return (await callable(input)).data;
}

export async function setStudentPrivacyConsent(input: {
  schoolId: string;
  studentId: string;
  granted: boolean;
} & StudentPrivacyDraft): Promise<{ ok: boolean; privacy: StudentPrivacySettings }> {
  if (!functions || !useFunctionsWorkflow) {
    return {
      ok: true,
      privacy: {
        consentStatus: input.granted ? "granted" : "withdrawn",
        policyVersion: "demo",
        gameplayAnalyticsAllowed: input.granted && input.gameplayAnalyticsAllowed,
        buddyAllowed: input.granted && input.buddyAllowed,
        audioProcessingAllowed: input.granted && input.audioProcessingAllowed,
        handwritingEvidenceAllowed: input.granted && input.handwritingEvidenceAllowed,
        diagnosticsAllowed: input.granted && input.diagnosticsAllowed,
        consentedAtUtc: new Date().toISOString(),
        consentSource: "demo_parent"
      }
    };
  }
  const callable = httpsCallable<typeof input, { ok: boolean; privacy: StudentPrivacySettings }>(functions, "setStudentPrivacyConsent");
  return (await callable(input)).data;
}

export async function requestStudentDataDeletion(input: { schoolId: string; studentId: string }) {
  if (!functions || !useFunctionsWorkflow) {
    return { ok: true, requestId: `demo_${input.studentId}`, deleteAfterUtc: new Date(Date.now() + 7 * 86400000).toISOString() };
  }
  const callable = httpsCallable<typeof input, { ok: boolean; requestId: string; deleteAfterUtc: string }>(functions, "requestStudentDataDeletion");
  return (await callable(input)).data;
}

export async function cancelStudentDataDeletion(input: { schoolId: string; studentId: string }) {
  if (!functions || !useFunctionsWorkflow) return { ok: true };
  const callable = httpsCallable<typeof input, { ok: boolean }>(functions, "cancelStudentDataDeletion");
  return (await callable(input)).data;
}

export async function getOperationsSummary(input: { schoolId: string; days?: number }): Promise<OperationsSummary> {
  if (!functions || !useFunctionsWorkflow) {
    return { schoolId: input.schoolId, currency: "USD", costUnit: "micro_USD_estimate", buddy: [], buddyStt: [], pronunciation: [], diagnostics: [], safety: [], costBudget: [] };
  }
  const callable = httpsCallable<typeof input, OperationsSummary>(functions, "getOperationsSummary");
  return (await callable(input)).data;
}

export interface TeacherAssistantRequest {
  schoolId: string;
  classId: string;
  studentId?: string;
  question: string;
}

export interface TeacherAssistantResponse {
  answer: string;
  suggestedActions: string[];
  citations: string[];
  agentTrace: string[];
  model: string;
  fallbackReason?: string;
  usage?: Record<string, unknown>;
}

export async function askTeacherAssistant(
  input: TeacherAssistantRequest,
  localDataset?: PortalDataset
): Promise<TeacherAssistantResponse> {
  if (!functions || !useFunctionsWorkflow) {
    return buildLocalTeacherAssistantResponse(input, localDataset);
  }
  const callable = httpsCallable<TeacherAssistantRequest, TeacherAssistantResponse>(functions, "askTeacherAssistant");
  return (await callable(input)).data;
}

export async function setStudentServiceTier(input: {
  schoolId: string;
  studentId: string;
  tier: "free" | "standard" | "premium";
}): Promise<void> {
  if (!hasFirebaseConfig || !functions) return;
  const callable = httpsCallable<typeof input, { ok: boolean }>(functions, "setStudentServiceTier");
  await callable(input);
}

export async function createTeacherInvite(input: {
  schoolId: string;
  teacherEmail: string;
  classIds: string[];
}): Promise<{ code: string; expiresAt: string }> {
  if (!functions || !hasFirebaseConfig) {
    return {
      code: "TEACH-DEMO",
      expiresAt: new Date(Date.now() + 30 * 86400000).toISOString()
    };
  }

  const callable = httpsCallable<typeof input, { code: string; expiresAt: string }>(functions, "createTeacherInvite");
  const result = await callable(input);
  return result.data;
}

export async function createTeacherAccount(input: {
  schoolId: string;
  teacherEmail: string;
  displayName: string;
  password: string;
  classIds: string[];
}): Promise<{
  uid: string;
  email: string;
  displayName: string;
  schoolId: string;
  classIds: string[];
}> {
  if (!hasFirebaseConfig || !db) {
    return {
      uid: `demo-teacher-${Date.now()}`,
      email: input.teacherEmail,
      displayName: input.displayName,
      schoolId: input.schoolId,
      classIds: input.classIds
    };
  }

  if (useFunctionsWorkflow && functions) {
    const callable = httpsCallable<typeof input, {
      uid: string;
      email: string;
      displayName: string;
      schoolId: string;
      classIds: string[];
    }>(functions, "createTeacherAccount");
    const result = await callable(input);
    return result.data;
  }

  const now = new Date().toISOString();
  const account = await createAuthAccount(input.teacherEmail, input.password, input.displayName);
  const teacher = {
    uid: account.uid,
    email: input.teacherEmail.trim().toLowerCase(),
    displayName: input.displayName,
    schoolId: input.schoolId,
    classIds: input.classIds
  };

  await setDoc(doc(db, "users", account.uid), {
    ...teacher,
    role: "teacher",
    studentIds: [],
    createdAt: now,
    updatedAt: now
  }, { merge: true });
  await setDoc(doc(db, "schools", input.schoolId, "teachers", account.uid), {
    ...teacher,
    createdAt: now,
    updatedAt: now
  }, { merge: true });
  return teacher;
}

function buildLocalTeacherAssistantResponse(
  input: TeacherAssistantRequest,
  dataset?: PortalDataset
): TeacherAssistantResponse {
  if (!dataset) {
    return {
      answer: "Teacher Assistant is ready, but cloud Functions are not enabled in this environment. Connect Firebase Functions to use the Gemini-backed evidence assistant.",
      suggestedActions: ["Open Reports for the selected class and review the highest-priority recommendation."],
      citations: [],
      agentTrace: [
        "Scope agent used the local portal session.",
        "Retrieval agent could not access cloud evidence.",
        "Analyst agent returned a configuration note."
      ],
      model: "local-fallback",
      fallbackReason: "functions_unavailable"
    };
  }

  const selectedStudents = input.studentId
    ? dataset.students.filter((student) => student.id === input.studentId)
    : dataset.students.filter((student) => student.classId === input.classId);
  const selectedIds = new Set(selectedStudents.map((student) => student.id));
  const recommendations = dataset.recommendations.filter((item) => !item.studentId || selectedIds.has(item.studentId));
  const highPriority = recommendations.filter((item) => item.priority === "high");
  const spoken = dataset.spokenPhraseEvents.filter((event) => selectedIds.has(event.studentId));
  const written = dataset.writtenPhraseEvents.filter((event) => selectedIds.has(event.studentId));
  const battles = dataset.grammarBattleEvents.filter((event) => selectedIds.has(event.studentId));
  const runs = dataset.runSessions.filter((run) => selectedIds.has(run.studentId));
  const weakConcepts = topLocalValues([
    ...spoken.filter((event) => !event.accepted).map((event) => event.conceptId || event.grammarPattern || event.errorCategory || ""),
    ...written.filter((event) => !event.accepted).map((event) => event.conceptId || event.grammarPattern || event.errorCategory || ""),
    ...battles.filter((event) => !event.accepted).map((event) => event.conceptId || event.grammarPattern || event.errorCategory || "")
  ]);
  const averageConfidence = runs.length
    ? Math.round((runs.reduce((sum, run) => sum + (run.averageConfidence || 0), 0) / runs.length) * 100)
    : 0;
  const names = selectedStudents.map((student) => student.name).slice(0, 4).join(", ");
  const citations = [
    ...recommendations.slice(0, 3).map((item) => `local/recommendations/${item.id}`),
    ...runs.slice(0, 2).map((item) => `local/runSessions/${item.id}`),
    ...spoken.slice(0, 2).map((item) => `local/spokenPhraseEvents/${item.id}`)
  ];

  return {
    answer: [
      input.studentId
        ? `For ${selectedStudents[0]?.name ?? "this learner"}, I found ${recommendations.length} recommendation${recommendations.length === 1 ? "" : "s"} and ${spoken.length + written.length + battles.length} grammar evidence item${spoken.length + written.length + battles.length === 1 ? "" : "s"} in the loaded portal data.`
        : `For ${dataset.activeClass.name}, I found ${selectedStudents.length} learner${selectedStudents.length === 1 ? "" : "s"}${names ? ` (${names}${selectedStudents.length > 4 ? ", ..." : ""})` : ""}.`,
      highPriority.length ? `${highPriority.length} high-priority item${highPriority.length === 1 ? "" : "s"} should be reviewed first.` : "No high-priority recommendations are visible in the loaded dataset.",
      weakConcepts.length ? `The clearest practice pattern is ${weakConcepts.join(", ")}.` : "",
      averageConfidence ? `Recent average confidence is about ${averageConfidence}%.` : ""
    ].filter(Boolean).join(" "),
    suggestedActions: [
      weakConcepts[0] ? `Create a short group review for ${weakConcepts[0]}.` : "Review the latest report evidence before changing the mission.",
      highPriority[0]?.detail || "Collect one fresh spoken and one written attempt for the focus concept.",
      "Use the Reports tab to verify individual evidence before parent-facing notes."
    ].slice(0, 3),
    citations,
    agentTrace: [
      "Scope agent used the selected class/student in the current portal dataset.",
      "Retrieval agent scanned loaded recommendations, runs, and grammar evidence.",
      "Analyst agent counted visible priorities and weak concepts.",
      "Critic agent marked the response as local fallback evidence, not cloud retrieval."
    ],
    model: "local-fallback",
    fallbackReason: "functions_unavailable"
  };
}

function topLocalValues(values: string[]): string[] {
  const counts = new Map<string, number>();
  for (const raw of values) {
    const value = raw.trim();
    if (!value) continue;
    counts.set(value, (counts.get(value) ?? 0) + 1);
  }
  return Array.from(counts.entries())
    .sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]))
    .slice(0, 4)
    .map(([value]) => value);
}

async function createAuthAccount(email: string, password: string, displayName: string) {
  const secondaryApp = initializeApp(firebaseConfig, `account-create-${Date.now()}-${Math.random().toString(36).slice(2)}`);
  const secondaryAuth = getAuth(secondaryApp);
  try {
    const credential = await createUserWithEmailAndPassword(secondaryAuth, email.trim().toLowerCase(), password);
    await updateProfile(credential.user, { displayName: displayName.trim() });
    return {
      uid: credential.user.uid,
      email: credential.user.email ?? email.trim().toLowerCase()
    };
  } finally {
    await deleteApp(secondaryApp);
  }
}

function slugify(value: string) {
  const slug = value.toLowerCase().trim().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "");
  return slug || `class-${Date.now()}`;
}

function buildWorldGoalId(classId: string, weekStart: string) {
  return `${classId}_${weekStart}_world`;
}

function buildDefaultWorldGoal(
  schoolId: string,
  classId: string,
  weekStart: string,
  teacherId: string
): WorldGoalAssignment {
  return {
    goalId: buildWorldGoalId(classId, weekStart),
    schoolId,
    classId,
    weekStart,
    targetAreaId: "TOWN:BASICPREPOSITIONS:11",
    targetGymId: "GYM:BASICPREPOSITIONS:11",
    focusGrammarPatterns: ["FullSentence"],
    focusVocabulary: ["IN", "ON", "UNDER", "BEHIND", "RAT", "BOX", "ROOF"],
    dueDate: addDays(weekStart, 6),
    rewardCoins: 25,
    schoolTimeZone: "Asia/Kolkata",
    assignedAtUtc: new Date().toISOString(),
    createdByTeacherId: teacherId
  };
}

function addDays(dateKey: string, days: number) {
  const start = new Date(`${dateKey}T00:00:00`);
  start.setDate(start.getDate() + days);
  return start.toISOString().slice(0, 10);
}
