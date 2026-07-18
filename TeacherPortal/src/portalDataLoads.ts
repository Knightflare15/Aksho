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

const today = new Date().toISOString().slice(0, 10);

export async function loadSchoolTeachers(schoolId: string): Promise<TeacherProfile[]> {
  if (!db) {
    return [];
  }

  const snaps = await getDocs(collection(db, "schools", schoolId, "teachers"));
  return snaps.docs
    .map((snap) => ({ uid: snap.id, ...snap.data() } as TeacherProfile))
    .sort((a, b) => a.displayName.localeCompare(b.displayName));
}

export async function createAccessCode(input: {
  type: "parent" | "student";
  schoolId: string;
  classId: string;
  studentId: string;
}): Promise<{ code: string; expiresAt: string }> {
  if (!functions || !hasFirebaseConfig) {
    return {
      code: input.type === "parent" ? "PARENT-DEMO" : "STUDENT-DEMO",
      expiresAt: new Date(Date.now() + 30 * 86400000).toISOString()
    };
  }

  const functionName = input.type === "parent" ? "createParentAccessCode" : "createStudentAccessCode";
  const callable = httpsCallable<typeof input, { code: string; expiresAt: string }>(functions, functionName);
  const result = await callable(input);
  return result.data;
}

export async function loadRunSessions(profile: UserProfile, students: Student[]): Promise<RunSession[]> {
  if (!db) {
    return [];
  }

  const firestore = db;
  const rows = await Promise.all(students.map(async (student) => {
    const snaps = await getDocs(query(
      collection(firestore, "schools", profile.schoolId, "students", student.id, "runSessions"),
      orderBy("endedAtUtc", "desc"),
      limit(12)
    ));
    return snaps.docs.map((snap) => ({ id: snap.id, ...snap.data() } as RunSession));
  }));
  return rows.flat();
}

export async function loadScopedClassrooms(profile: UserProfile): Promise<Classroom[]> {
  if (!db || profile.classIds.length === 0) {
    return [];
  }

  const firestore = db;
  const rows = await Promise.all(profile.classIds.map(async (classId) => {
    const snap = await getDoc(doc(firestore, "schools", profile.schoolId, "classes", classId));
    return snap.exists() ? ({ id: snap.id, ...snap.data() } as Classroom) : null;
  }));
  return rows.filter((room): room is Classroom => room !== null);
}

export async function loadScopedStudents(profile: UserProfile): Promise<Student[]> {
  if (!db || profile.studentIds.length === 0) {
    return [];
  }

  const firestore = db;
  const rows = await Promise.all(profile.studentIds.map(async (studentId) => {
    const snap = await getDoc(doc(firestore, "schools", profile.schoolId, "students", studentId));
    return snap.exists() ? ({ id: snap.id, ...snap.data() } as Student) : null;
  }));
  return rows.filter((student): student is Student => student !== null);
}

export async function loadTeacherStudents(profile: UserProfile): Promise<Student[]> {
  if (!db || profile.classIds.length === 0) {
    return [];
  }

  const firestore = db;
  const rows = await Promise.all(profile.classIds.map(async (classId) => {
    const snaps = await getDocs(query(
      collection(firestore, "schools", profile.schoolId, "students"),
      where("classId", "==", classId)
    ));
    return snaps.docs.map((snap) => ({ id: snap.id, ...snap.data() } as Student));
  }));
  return rows.flat();
}

export async function loadRecommendations(profile: UserProfile, students: Student[]): Promise<Recommendation[]> {
  if (!db) {
    return [];
  }

  const firestore = db;
  const studentIds = students.map((student) => student.id);
  if (studentIds.length === 0) {
    return [];
  }

  const rows = await Promise.all(studentIds.map(async (studentId) => {
    const snaps = await getDocs(query(
      collection(firestore, "schools", profile.schoolId, "students", studentId, "recommendations"),
      orderBy("createdAt", "desc"),
      limit(6)
    ));
    return snaps.docs.map((snap) => ({ id: snap.id, ...snap.data() } as Recommendation));
  }));
  return rows.flat();
}

export async function loadStudentMissionOverrides(profile: UserProfile, students: Student[]): Promise<StudentMissionOverride[]> {
  if (!db) {
    return [];
  }

  const firestore = db;
  const rows = await Promise.all(students.map(async (student) => {
    const snaps = await getDocs(query(
      collection(firestore, "schools", profile.schoolId, "students", student.id, "studentMissionOverrides"),
      orderBy("date", "desc"),
      limit(30)
    ));
    return snaps.docs.map((snap) => normalizeMission({ id: snap.id, ...snap.data() } as StudentMissionOverride) as StudentMissionOverride);
  }));
  return rows.flat();
}

export async function loadWorldGoals(profile: UserProfile, students: Student[], classId: string): Promise<WorldGoalAssignment[]> {
  if (!db) {
    return [];
  }

  const firestore = db;
  const classSnaps = classId
    ? await getDocs(query(
      collection(firestore, "schools", profile.schoolId, "classes", classId, "worldGoals"),
      orderBy("weekStart", "desc"),
      limit(8)
    ))
    : { docs: [] };
  const classGoals = classSnaps.docs.map((snap) => ({ goalId: snap.id, ...snap.data() } as WorldGoalAssignment));
  const studentRows = await Promise.all(students.map(async (student) => {
    const snaps = await getDocs(query(
      collection(firestore, "schools", profile.schoolId, "students", student.id, "worldGoals"),
      orderBy("weekStart", "desc"),
      limit(4)
    ));
    return snaps.docs.map((snap) => ({ goalId: snap.id, ...snap.data(), studentId: student.id } as WorldGoalAssignment));
  }));
  return [...classGoals, ...studentRows.flat()];
}

export async function loadAcceptedTemplates(profile: UserProfile, students: Student[]): Promise<AcceptedHandwritingTemplate[]> {
  if (!db) {
    return [];
  }

  const firestore = db;
  const rows = await Promise.all(students.map(async (student) => {
    const snaps = await getDocs(query(
      collection(firestore, "schools", profile.schoolId, "students", student.id, "acceptedHandwritingTemplates"),
      orderBy("createdAtUtc", "desc"),
      limit(24)
    ));
    return snaps.docs.map((snap) => ({ id: snap.id, ...snap.data() } as AcceptedHandwritingTemplate));
  }));
  return rows.flat();
}

export async function loadLetterAttempts(profile: UserProfile, students: Student[]): Promise<LetterAttempt[]> {
  if (!db) {
    return [];
  }

  const firestore = db;
  const rows = await Promise.all(students.map(async (student) => {
    const snaps = await getDocs(query(
      collection(firestore, "schools", profile.schoolId, "students", student.id, "letterAttempts"),
      orderBy("createdAtUtc", "desc"),
      limit(48)
    ));
    return snaps.docs.map((snap) => ({ id: snap.id, ...snap.data() } as LetterAttempt));
  }));
  return rows.flat();
}

export async function loadWordCasts(profile: UserProfile, students: Student[]): Promise<WordCast[]> {
  if (!db) {
    return [];
  }

  const firestore = db;
  const rows = await Promise.all(students.map(async (student) => {
    const snaps = await getDocs(query(
      collection(firestore, "schools", profile.schoolId, "students", student.id, "wordCastEvents"),
      orderBy("createdAtUtc", "desc"),
      limit(48)
    ));
    return snaps.docs.map((snap) => ({ id: snap.id, ...snap.data() } as WordCast));
  }));
  return rows.flat();
}

export async function loadSpokenPhraseEvents(profile: UserProfile, students: Student[]): Promise<SpokenPhraseEvent[]> {
  return loadStudentEvidence<SpokenPhraseEvent>(profile, students, "spokenPhraseEvents");
}

export async function loadWrittenPhraseEvents(profile: UserProfile, students: Student[]): Promise<WrittenPhraseEvent[]> {
  return loadStudentEvidence<WrittenPhraseEvent>(profile, students, "writtenPhraseEvents");
}

export async function loadBuddyConversationTurns(profile: UserProfile, students: Student[]): Promise<BuddyConversationTurn[]> {
  return loadStudentEvidence<BuddyConversationTurn>(profile, students, "buddyConversationTurns");
}

export async function loadGrammarBattleEvents(profile: UserProfile, students: Student[]): Promise<GrammarBattleEvent[]> {
  return loadStudentEvidence<GrammarBattleEvent>(profile, students, "grammarBattleEvents");
}

export async function loadGymAttempts(profile: UserProfile, students: Student[]): Promise<GymAttempt[]> {
  return loadStudentEvidence<GymAttempt>(profile, students, "gymAttempts");
}

export async function loadWorldGoalClaims(profile: UserProfile, students: Student[]): Promise<WorldGoalClaim[]> {
  if (!db) return [];
  const firestore = db;
  const rows = await Promise.all(students.map(async student => {
    const snaps = await getDocs(collection(
      firestore,
      "schools", profile.schoolId,
      "students", student.id,
      "worldGoalClaims"
    ));
    return snaps.docs.map(snap => ({ id: snap.id, ...snap.data() } as WorldGoalClaim));
  }));
  return rows.flat();
}

export async function loadStudentEvidence<T>(profile: UserProfile, students: Student[], collectionName: string): Promise<T[]> {
  if (!db) {
    return [];
  }

  const firestore = db;
  const rows = await Promise.all(students.map(async (student) => {
    const snaps = await getDocs(query(
      collection(firestore, "schools", profile.schoolId, "students", student.id, collectionName),
      orderBy("createdAtUtc", "desc"),
      limit(48)
    ));
    return snaps.docs.map((snap) => ({ id: snap.id, ...snap.data() } as T));
  }));
  return rows.flat();
}

export async function loadCountingMiniGameAttempts(profile: UserProfile, students: Student[]): Promise<CountingMiniGameAttempt[]> {
  if (!db) {
    return [];
  }

  const firestore = db;
  const rows = await Promise.all(students.map(async (student) => {
    const snaps = await getDocs(query(
      collection(firestore, "schools", profile.schoolId, "students", student.id, "countingMiniGameAttempts"),
      orderBy("createdAtUtc", "desc"),
      limit(48)
    ));
    return snaps.docs.map((snap) => ({ id: snap.id, ...snap.data() } as CountingMiniGameAttempt));
  }));
  return rows.flat();
}

export async function loadColorMiniGameAttempts(profile: UserProfile, students: Student[]): Promise<ColorMiniGameAttempt[]> {
  if (!db) {
    return [];
  }

  const firestore = db;
  const rows = await Promise.all(students.map(async (student) => {
    const snaps = await getDocs(query(
      collection(firestore, "schools", profile.schoolId, "students", student.id, "colorMiniGameAttempts"),
      orderBy("createdAtUtc", "desc"),
      limit(48)
    ));
    return snaps.docs.map((snap) => ({ id: snap.id, ...snap.data() } as ColorMiniGameAttempt));
  }));
  return rows.flat();
}

export async function loadEmpathyEventAttempts(profile: UserProfile, students: Student[]): Promise<EmpathyEventAttempt[]> {
  if (!db) {
    return [];
  }

  const firestore = db;
  const rows = await Promise.all(students.map(async (student) => {
    const snaps = await getDocs(query(
      collection(firestore, "schools", profile.schoolId, "students", student.id, "empathyEvents"),
      orderBy("createdAtUtc", "desc"),
      limit(48)
    ));
    return snaps.docs.map((snap) => ({ id: snap.id, ...snap.data() } as EmpathyEventAttempt));
  }));
  return rows.flat();
}

export function normalizeMission(mission: DailyMissionAssignment): DailyMissionAssignment {
  return {
    ...mission,
    countingChestCount: clampChestCount(mission.countingChestCount, 1),
    colorChestCount: clampChestCount(mission.colorChestCount, 0)
  };
}

export function clampChestCount(value: unknown, fallback: number) {
  const numeric = typeof value === "number" ? value : Number(value);
  if (!Number.isFinite(numeric)) {
    return fallback;
  }
  return Math.max(0, Math.min(2, Math.round(numeric)));
}

export async function loadParentSummaries(profile: UserProfile): Promise<ParentStudentSummary[]> {
  if (!db || profile.studentIds.length === 0) {
    return [];
  }

  const firestore = db;
  const rows = await Promise.all(profile.studentIds.map(async (studentId) => {
    const snaps = await getDocs(query(
      collection(firestore, "schools", profile.schoolId, "students", studentId, "parentSummaries"),
      where("studentId", "==", studentId),
      orderBy("updatedAt", "desc"),
      limit(1)
    ));
    return snaps.docs.map((snap) => ({ id: snap.id, ...snap.data() } as ParentStudentSummary));
  }));
  return rows.flat();
}

export function buildParentSummary(student: Student, runs: RunSession[]): ParentStudentSummary {
  const studentRuns = runs.filter((run) => run.studentId === student.id);
  const latest = studentRuns[0];
  return {
    id: `${student.id}_${today}`,
    schoolId: student.schoolId,
    studentId: student.id,
    studentName: student.name,
    classId: student.classId,
    weekStart: today,
    minutesPracticed: Math.round((latest?.actualDurationSeconds ?? 0) / 60),
    lettersPracticed: latest?.lettersPracticed ?? [],
    wordsPracticed: latest?.wordsPracticed ?? [],
    bestLetter: latest?.lettersPracticed?.[0] ?? "A",
    needsPracticeLetter: latest?.lettersPracticed?.[1] ?? "C",
    averageConfidence: latest?.averageConfidence ?? 0,
    averageAttemptsPerLetter: latest?.averageAttemptsPerLetter ?? 0,
    trendLabel: "Steady progress",
    updatedAt: new Date().toISOString()
  };
}
