import {
  BarChart3,
  CalendarDays,
  CheckCircle2,
  ClipboardCheck,
  FileText,
  GraduationCap,
  RefreshCw,
  ShieldCheck,
  UserPlus,
  Users
} from "lucide-react";
import { useEffect, useMemo, useState, type ReactNode } from "react";
import TacticalCombatVisualizer from "../combatLab/TacticalCombatVisualizer";
import PortalFrame, { type PortalNavigationItem } from "../components/PortalFrame";
import TeacherWorkspaceHeader from "../components/TeacherWorkspaceHeader";
import PublicWebsite from "../PublicWebsite";
import { portalAuthProvider } from "../services/authProvider";
import {
  buildDemoDataset,
  createClassroom,
  createStudentRecord,
  getStudentPrivacyStatus,
  getOperationsSummary,
  setStudentPrivacyConsent,
  requestStudentDataDeletion,
  cancelStudentDataDeletion,
  createTeacherAccount,
  loadDailyMission,
  loadWorldGoal,
  loadPortalDataset,
  loadUserProfile,
  makeDemoProfile,
  saveDailyMission,
  saveMissionRange,
  saveSchool,
  setStudentServiceTier,
  saveStudentMissionOverride,
  saveWorldGoal,
  type PortalDataset,
  type StudentPrivacyDraft
} from "../portalData";
import type {
  AcceptedHandwritingTemplate,
  BuddyConversationTurn,
  ColorMiniGameAttempt,
  CountingMiniGameAttempt,
  DailyMissionAssignment,
  EmpathyEventAttempt,
  GrammarBattleEvent,
  GymAttempt,
  HandwritingDiagnosticSummary,
  LetterAttempt,
  MissionType,
  ParentStudentSummary,
  PhoneticSegment,
  PronunciationInsight,
  RunSession,
  SpokenPhraseEvent,
  Student,
  StudentMissionOverride,
  UserProfile,
  WordCast,
  WorldGoalAssignment,
  WorldGoalClaim,
  WrittenPhraseEvent
} from "../types";

export const durationPresets = [5, 8, 10, 15];
export const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".split("");
export const grammarRegionOptions = [
  {
    label: "Welcome Village",
    targetAreaId: "TOWN:GREETINGSANDSURVIVALENGLISH:1",
    targetGymId: "GYM:GREETINGSANDSURVIVALENGLISH:1",
    focusGrammarPatterns: ["LetterOnly", "FullSentence"],
    focusVocabulary: ["HELLO", "YES", "NO"]
  },
  {
    label: "Alphabet Acres",
    targetAreaId: "TOWN:ALPHABET:2",
    targetGymId: "GYM:ALPHABET:2",
    focusGrammarPatterns: ["LetterOnly"],
    focusVocabulary: ["A", "B", "C", "D", "E"]
  },
  {
    label: "Vowel Valley",
    targetAreaId: "TOWN:VOWELSANDCONSONANTS:3",
    targetGymId: "GYM:VOWELSANDCONSONANTS:3",
    focusGrammarPatterns: ["LetterOnly"],
    focusVocabulary: ["A", "E", "I", "O", "U", "B", "M"]
  },
  {
    label: "Sentence Square",
    targetAreaId: "TOWN:SENTENCESTARTANDFULLSTOP:4",
    targetGymId: "GYM:SENTENCESTARTANDFULLSTOP:4",
    focusGrammarPatterns: ["FullSentence"],
    focusVocabulary: ["I", "AM", "READY", "WE", "PLAY"]
  },
  {
    label: "Nounfield Town",
    targetAreaId: "TOWN:NOUNS:5",
    targetGymId: "GYM:NOUNS:5",
    focusGrammarPatterns: ["NounOnly"],
    focusVocabulary: ["RAT", "OWL", "CAT", "DOG", "BOX", "SHOP"]
  },
  {
    label: "Verb Village",
    targetAreaId: "TOWN:VERBS:6",
    targetGymId: "GYM:VERBS:6",
    focusGrammarPatterns: ["VerbOnly"],
    focusVocabulary: ["BITE", "RUN", "JUMP", "SCRATCH"]
  },
  {
    label: "Article Arcade",
    targetAreaId: "TOWN:ARTICLES:7",
    targetGymId: "GYM:ARTICLES:7",
    focusGrammarPatterns: ["DeterminerNoun"],
    focusVocabulary: ["A", "AN", "THE", "RAT", "OWL", "CAT"]
  },
  {
    label: "Pronoun Port",
    targetAreaId: "TOWN:PRONOUNS:8",
    targetGymId: "GYM:PRONOUNS:8",
    focusGrammarPatterns: ["PronounVerbPresent"],
    focusVocabulary: ["I", "YOU", "HE", "SHE", "THEY", "BITE"]
  },
  {
    label: "Plural Plains",
    targetAreaId: "TOWN:PLURALS:9",
    targetGymId: "GYM:PLURALS:9",
    focusGrammarPatterns: ["FullSentence"],
    focusVocabulary: ["RATS", "BOXES", "PUPPIES"]
  },
  {
    label: "Adjective Grove",
    targetAreaId: "TOWN:ADJECTIVES:10",
    targetGymId: "GYM:ADJECTIVES:10",
    focusGrammarPatterns: ["AdjectiveNoun", "DeterminerAdjectiveNoun"],
    focusVocabulary: ["A", "THE", "BIG", "SMALL", "RAT", "CAT", "DOG"]
  },
  {
    label: "Preposition Park",
    targetAreaId: "TOWN:BASICPREPOSITIONS:11",
    targetGymId: "GYM:BASICPREPOSITIONS:11",
    focusGrammarPatterns: ["FullSentence"],
    focusVocabulary: ["IN", "ON", "UNDER", "BEHIND", "RAT", "BOX", "ROOF"]
  }
];

export function splitWords(value: string) {
  return value
    .split(",")
    .map((word) => word.trim().toUpperCase())
    .filter(Boolean);
}

export function splitCsvValues(value: string) {
  return value
    .split(",")
    .map((word) => word.trim())
    .filter(Boolean);
}

export function buildWorldGoalId(classId: string, weekStart: string) {
  return `${classId}_${weekStart}_world`;
}

export function findWorldGoalDraft(
  goals: WorldGoalAssignment[],
  schoolId: string,
  classId: string,
  weekStart: string,
  teacherId: string
): WorldGoalAssignment {
  const existing = goals.find((goal) => (
    goal.classId === classId &&
    goal.weekStart === weekStart &&
    !goal.studentId
  ));
  if (existing) {
    return existing;
  }

  const preset = grammarRegionOptions[grammarRegionOptions.length - 1];
  return {
    goalId: buildWorldGoalId(classId, weekStart),
    schoolId,
    classId,
    weekStart,
    targetAreaId: preset.targetAreaId,
    targetGymId: preset.targetGymId,
    focusGrammarPatterns: preset.focusGrammarPatterns,
    focusVocabulary: preset.focusVocabulary,
    dueDate: addDays(weekStart, 6),
    rewardCoins: 25,
    schoolTimeZone: "Asia/Kolkata",
    assignedAtUtc: new Date().toISOString(),
    createdByTeacherId: teacherId
  };
}

export function buildStudentOverride(
  mission: DailyMissionAssignment,
  studentId: string,
  teacherUid: string
): StudentMissionOverride {
  return {
    ...mission,
    id: mission.date,
    studentId,
    baseMissionId: mission.id,
    createdByTeacherId: teacherUid,
    note: ""
  };
}

export function addDays(dateKey: string, days: number) {
  const start = new Date(`${dateKey}T00:00:00`);
  start.setDate(start.getDate() + days);
  return start.toISOString().slice(0, 10);
}
