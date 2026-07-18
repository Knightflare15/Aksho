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

import {
  buildParentSummary,
  loadAcceptedTemplates,
  loadBuddyConversationTurns,
  loadColorMiniGameAttempts,
  loadCountingMiniGameAttempts,
  loadEmpathyEventAttempts,
  loadGrammarBattleEvents,
  loadGymAttempts,
  loadLetterAttempts,
  loadParentSummaries,
  loadRecommendations,
  loadRunSessions,
  loadSchoolTeachers,
  loadScopedClassrooms,
  loadScopedStudents,
  loadSpokenPhraseEvents,
  loadStudentMissionOverrides,
  loadTeacherStudents,
  loadWordCasts,
  loadWorldGoalClaims,
  loadWorldGoals,
  loadWrittenPhraseEvents,
  normalizeMission,
} from "./portalDataLoads";

export {
  cancelStudentDataDeletion,
  askTeacherAssistant,
  createClassroom,
  createStudentRecord,
  createTeacherAccount,
  createTeacherInvite,
  getOperationsSummary,
  getStudentPrivacyStatus,
  loadDailyMission,
  loadWorldGoal,
  redeemParentCode,
  redeemTeacherInvite,
  requestStudentDataDeletion,
  saveDailyMission,
  saveMissionRange,
  saveSchool,
  saveStudentMissionOverride,
  saveWorldGoal,
  setStudentPrivacyConsent,
  setStudentServiceTier,
} from "./portalDataMutations";
export type { TeacherAssistantRequest, TeacherAssistantResponse } from "./portalDataMutations";
export { createAccessCode } from "./portalDataLoads";

export type StudentPrivacyDraft = Pick<StudentPrivacySettings,
  "gameplayAnalyticsAllowed" | "buddyAllowed" | "audioProcessingAllowed" |
  "handwritingEvidenceAllowed" | "diagnosticsAllowed">;

export interface StudentPrivacyStatus {
  schoolId: string;
  studentId: string;
  policyVersion: string;
  requiresRenewal: boolean;
  deletionStatus?: string;
  deleteAfterUtc?: string;
  privacy: StudentPrivacySettings;
}

export interface OperationsSummary {
  schoolId: string;
  currency: "USD";
  costUnit: "micro_USD_estimate";
  buddy: Array<Record<string, unknown>>;
  buddyStt: Array<Record<string, unknown>>;
  pronunciation: Array<Record<string, unknown>>;
  diagnostics: Array<Record<string, unknown>>;
  safety: Array<Record<string, unknown>>;
  costBudget: Array<Record<string, unknown>>;
}

export interface PortalDataset {
  school: School;
  classrooms: Classroom[];
  teachers: TeacherProfile[];
  activeClass: Classroom;
  students: Student[];
  mission: DailyMissionAssignment;
  worldGoals: WorldGoalAssignment[];
  worldGoalClaims: WorldGoalClaim[];
  studentMissionOverrides: StudentMissionOverride[];
  runSessions: RunSession[];
  recommendations: Recommendation[];
  letterAttempts: LetterAttempt[];
  acceptedTemplates: AcceptedHandwritingTemplate[];
  wordCasts: WordCast[];
  spokenPhraseEvents: SpokenPhraseEvent[];
  writtenPhraseEvents: WrittenPhraseEvent[];
  grammarBattleEvents: GrammarBattleEvent[];
  buddyConversationTurns: BuddyConversationTurn[];
  gymAttempts: GymAttempt[];
  countingMiniGameAttempts: CountingMiniGameAttempt[];
  colorMiniGameAttempts: ColorMiniGameAttempt[];
  empathyEventAttempts: EmpathyEventAttempt[];
  parentSummaries: ParentStudentSummary[];
}

export interface RedeemCodeResult {
  customToken?: string;
  role?: string;
  schoolId?: string;
  classId?: string;
  studentId?: string;
  classIds?: string[];
}

const today = new Date().toISOString().slice(0, 10);

export function makeDemoProfile(role: UserProfile["role"]): UserProfile {
  return {
    uid: `demo-${role}`,
    email: `${role}@littlelantern.school`,
    displayName: role === "parent" ? "Demo Parent" : role === "student" ? "Aarav" : role === "admin" ? "School Admin" : "Demo Teacher",
    role,
    schoolId: demoSchool.id,
    classIds: [demoClassroom.id],
    studentIds: role === "parent" || role === "student" ? [demoStudents[0].id] : demoStudents.map((student) => student.id),
    studentId: role === "student" ? demoStudents[0].id : undefined,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString()
  };
}

export function buildDemoDataset(profile: UserProfile = makeDemoProfile("teacher")): PortalDataset {
  const allowedStudentIds = profile.role === "parent" || profile.role === "student" ? profile.studentIds : demoStudents.map((student) => student.id);
  const students = demoStudents.filter((student) => allowedStudentIds.includes(student.id));
  const runSessions = demoRunSessions.filter((run) => allowedStudentIds.includes(run.studentId));
  const acceptedTemplates = profile.role === "parent"
    ? []
    : demoTemplates.filter((template) => allowedStudentIds.includes(template.studentId));
  const letterAttempts = profile.role === "parent"
    ? []
    : demoLetterAttempts.filter((attempt) => allowedStudentIds.includes(attempt.studentId));
  const wordCasts = profile.role === "parent"
    ? []
    : demoWordCasts.filter((cast) => allowedStudentIds.includes(cast.studentId));
  const spokenPhraseEvents = profile.role === "parent"
    ? []
    : demoSpokenPhraseEvents.filter((event) => allowedStudentIds.includes(event.studentId));
  const writtenPhraseEvents = profile.role === "parent"
    ? []
    : demoWrittenPhraseEvents.filter((event) => allowedStudentIds.includes(event.studentId));
  const grammarBattleEvents = profile.role === "parent"
    ? []
    : demoGrammarBattleEvents.filter((event) => allowedStudentIds.includes(event.studentId));
  const buddyConversationTurns = profile.role === "parent"
    ? []
    : demoBuddyConversationTurns.filter((turn) => allowedStudentIds.includes(turn.studentId));
  const gymAttempts = profile.role === "parent"
    ? []
    : demoGymAttempts.filter((attempt) => allowedStudentIds.includes(attempt.studentId));
  const countingMiniGameAttempts = profile.role === "parent"
    ? []
    : demoCountingMiniGameAttempts.filter((attempt) => allowedStudentIds.includes(attempt.studentId));
  const colorMiniGameAttempts = profile.role === "parent"
    ? []
    : demoColorMiniGameAttempts.filter((attempt) => allowedStudentIds.includes(attempt.studentId));
  const empathyEventAttempts = profile.role === "parent"
    ? []
    : demoEmpathyEventAttempts.filter((attempt) => allowedStudentIds.includes(attempt.studentId));

  return {
    school: demoSchool,
    classrooms: [demoClassroom],
    teachers: [{
      uid: "demo-teacher",
      schoolId: demoSchool.id,
      email: "teacher@littlelantern.school",
      displayName: "Demo Teacher",
      classIds: [demoClassroom.id],
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString()
    }],
    activeClass: demoClassroom,
    students,
    mission: initialMission,
    worldGoals: demoWorldGoals,
    worldGoalClaims: [],
    studentMissionOverrides: [],
    runSessions,
    recommendations: demoRecommendations,
    letterAttempts,
    acceptedTemplates,
    wordCasts,
    spokenPhraseEvents,
    writtenPhraseEvents,
    grammarBattleEvents,
    buddyConversationTurns,
    gymAttempts,
    countingMiniGameAttempts,
    colorMiniGameAttempts,
    empathyEventAttempts,
    parentSummaries: students.map((student) => buildParentSummary(student, runSessions))
  };
}

export async function loadUserProfile(uid: string): Promise<UserProfile | null> {
  if (!db) {
    return null;
  }

  const snap = await getDoc(doc(db, "users", uid));
  return snap.exists() ? ({ uid, ...snap.data() } as UserProfile) : null;
}

export async function loadPortalDataset(profile: UserProfile): Promise<PortalDataset> {
  if (!db || !hasFirebaseConfig) {
    return buildDemoDataset(profile);
  }

  const schoolSnap = await getDoc(doc(db, "schools", profile.schoolId));
  const school = schoolSnap.exists()
    ? ({ id: schoolSnap.id, ...schoolSnap.data() } as School)
    : demoSchool;

  const classrooms = profile.role === "admin"
    ? (await getDocs(collection(db, "schools", profile.schoolId, "classes"))).docs
      .map((snap) => ({ id: snap.id, ...snap.data() } as Classroom))
    : await loadScopedClassrooms(profile);
  const hasAccessibleClass = classrooms.length > 0;
  const activeClass = hasAccessibleClass
    ? classrooms[0]
    : { ...demoClassroom, schoolId: profile.schoolId, id: profile.classIds[0] ?? "", name: "No class assigned", studentIds: [] };
  const teachers = profile.role === "admin"
    ? await loadSchoolTeachers(profile.schoolId)
    : [];

  const students = profile.role === "parent" || profile.role === "student"
    ? await loadScopedStudents(profile)
    : profile.role === "admin"
      ? (await getDocs(collection(db, "schools", profile.schoolId, "students"))).docs
      .map((snap) => ({ id: snap.id, ...snap.data() } as Student))
      : await loadTeacherStudents(profile);

  const missionSnap = hasAccessibleClass
    ? await getDoc(doc(db, "schools", profile.schoolId, "classes", activeClass.id, "dailyMissions", today))
    : null;
  const mission = missionSnap?.exists()
    ? normalizeMission({ id: missionSnap.id, ...missionSnap.data() } as DailyMissionAssignment)
    : normalizeMission({ ...initialMission, schoolId: profile.schoolId, classId: activeClass.id, date: today, id: today });
  const studentMissionOverrides = profile.role === "parent" ? [] : await safeLoad("student mission overrides", () => loadStudentMissionOverrides(profile, students), []);

  const runSessions = profile.role === "parent" ? [] : await safeLoad("run sessions", () => loadRunSessions(profile, students), []);
  const worldGoals = profile.role === "parent" ? [] : await safeLoad("world goals", () => loadWorldGoals(profile, students, activeClass.id), []);
  const worldGoalClaims = profile.role === "parent" ? [] : await safeLoad("world goal claims", () => loadWorldGoalClaims(profile, students), []);
  const recommendations = profile.role === "parent" ? [] : await safeLoad("recommendations", () => loadRecommendations(profile, students), []);
  const letterAttempts = profile.role === "parent" ? [] : await safeLoad("letter attempts", () => loadLetterAttempts(profile, students), []);
  const acceptedTemplates = profile.role === "parent" ? [] : await safeLoad("handwriting templates", () => loadAcceptedTemplates(profile, students), []);
  const wordCasts = profile.role === "parent" ? [] : await safeLoad("word casts", () => loadWordCasts(profile, students), []);
  const spokenPhraseEvents = profile.role === "parent" ? [] : await safeLoad("spoken phrase events", () => loadSpokenPhraseEvents(profile, students), []);
  const writtenPhraseEvents = profile.role === "parent" ? [] : await safeLoad("written phrase events", () => loadWrittenPhraseEvents(profile, students), []);
  const grammarBattleEvents = profile.role === "parent" ? [] : await safeLoad("grammar battle events", () => loadGrammarBattleEvents(profile, students), []);
  const buddyConversationTurns = profile.role === "parent" ? [] : await safeLoad("buddy conversation turns", () => loadBuddyConversationTurns(profile, students), []);
  const gymAttempts = profile.role === "parent" ? [] : await safeLoad("gym attempts", () => loadGymAttempts(profile, students), []);
  const countingMiniGameAttempts = profile.role === "parent" ? [] : await safeLoad("counting attempts", () => loadCountingMiniGameAttempts(profile, students), []);
  const colorMiniGameAttempts = profile.role === "parent" ? [] : await safeLoad("color attempts", () => loadColorMiniGameAttempts(profile, students), []);
  const empathyEventAttempts = profile.role === "parent" ? [] : await safeLoad("empathy events", () => loadEmpathyEventAttempts(profile, students), []);
  const parentSummaries = profile.role === "parent"
    ? await loadParentSummaries(profile)
    : students.map((student) => buildParentSummary(student, runSessions));

  return {
    school,
    classrooms,
    teachers,
    activeClass,
    students,
    mission,
    worldGoals,
    worldGoalClaims,
    studentMissionOverrides,
    runSessions,
    recommendations,
    letterAttempts,
    acceptedTemplates,
    wordCasts,
    spokenPhraseEvents,
    writtenPhraseEvents,
    grammarBattleEvents,
    buddyConversationTurns,
    gymAttempts,
    countingMiniGameAttempts,
    colorMiniGameAttempts,
    empathyEventAttempts,
    parentSummaries
  };
}

async function safeLoad<T>(label: string, load: () => Promise<T>, fallback: T): Promise<T> {
  try {
    return await load();
  } catch (error) {
    console.warn(`[portalData] Could not load ${label}.`, error);
    return fallback;
  }
}
