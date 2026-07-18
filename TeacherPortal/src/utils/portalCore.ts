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

export function soundLabel(segment: PhoneticSegment): string {
  return segment.friendlySound || segment.spelling.toLowerCase();
}

export function dateKey(value?: string) {
  if (!value) {
    return "";
  }
  const parsed = Date.parse(value);
  return Number.isFinite(parsed) ? new Date(parsed).toISOString().slice(0, 10) : "";
}

export function titleCase(value: string) {
  if (!value) {
    return "";
  }
  return value.slice(0, 1).toUpperCase() + value.slice(1).toLowerCase();
}

export function runTime(run: RunSession) {
  const maybeRun = run as RunSession & { endedAtUtc?: string; startedAtUtc?: string };
  return Date.parse(run.endedAt || maybeRun.endedAtUtc || run.startedAt || maybeRun.startedAtUtc || "") || 0;
}

export function runDateKey(run: RunSession) {
  const maybeRun = run as RunSession & { endedAtUtc?: string; startedAtUtc?: string };
  return dateKey(run.endedAt || maybeRun.endedAtUtc || run.startedAt || maybeRun.startedAtUtc);
}

export function computeClassStats(runSessions: RunSession[], missionDate: string) {
  const completedToday = new Set(runSessions
    .filter((run) => run.completed && runDateKey(run) === missionDate)
    .map((run) => run.studentId));
  const confidence = average(runSessions.map((run) => normalizeConfidence(run.averageConfidence)));
  const attempts = average(runSessions.map((run) => run.averageAttemptsPerLetter));
  const areas = runSessions.reduce((total, run) => total + run.subarenasCleared, 0);
  return { completed: completedToday.size, confidence, attempts, areas };
}

export function average(values: number[]) {
  if (values.length === 0) {
    return 0;
  }
  return values.reduce((total, value) => total + value, 0) / values.length;
}

export function normalizeConfidence(value?: number) {
  const numeric = Number(value ?? 0);
  if (!Number.isFinite(numeric)) {
    return 0;
  }
  const normalized = numeric > 1.5 ? numeric / 100 : numeric;
  return Math.max(0, Math.min(1, normalized));
}

export function formatConfidencePercent(value?: number) {
  return `${Math.round(normalizeConfidence(value) * 100)}%`;
}

export function formatDuration(seconds: number) {
  const rounded = Math.max(0, Math.round(seconds));
  return `${Math.floor(rounded / 60)}:${String(rounded % 60).padStart(2, "0")}`;
}

export function missionTypeLabel(type: MissionType) {
  switch (type) {
    case "revision":
      return "Revision";
    case "test":
      return "Test";
    default:
      return "Practice";
  }
}

export function humanizeAreaId(areaId?: string) {
  if (!areaId) {
    return "Not set";
  }
  const topicLabels: Record<string, string> = {
    GREETINGSANDSURVIVALENGLISH: "Greetings",
    ALPHABET: "Alphabet",
    VOWELSANDCONSONANTS: "Vowels and Consonants",
    SENTENCESTARTANDFULLSTOP: "Sentence Start and Full Stop",
    NOUNS: "Nouns",
    VERBS: "Verbs",
    ARTICLES: "Articles",
    PRONOUNS: "Pronouns",
    PLURALS: "Plurals",
    ADJECTIVES: "Adjectives",
    BASICPREPOSITIONS: "Basic Prepositions"
  };
  const normalizeTopicKey = (part: string) => {
    const upper = part.toUpperCase();
    return upper.endsWith("ROUTE") ? upper.slice(0, -5) : upper;
  };
  return areaId
    .split(":")
    .filter(Boolean)
    .map((part) => topicLabels[normalizeTopicKey(part)] ?? titleCase(part.replace(/ROUTE$/i, " Route").replace(/([A-Z])([A-Z][a-z])/g, "$1 $2").replace(/([a-z])([A-Z])/g, "$1 $2")))
    .join(" / ");
}

export function formatGoalClaim(claim?: WorldGoalClaim) {
  if (!claim) return "not claimed";
  if (claim.status === "completed_late") return "completed late - no bonus";
  return `+${claim.rewardCoins} coins paid`;
}

export function humanizeBuddyAssistMode(mode?: string) {
  switch ((mode ?? "").trim().toLowerCase()) {
    case "full":
      return "Full Buddy";
    case "partial":
      return "Partial Buddy";
    case "off":
      return "No Buddy";
    default:
      return "Not recorded";
  }
}

export function humanizePattern(pattern?: string) {
  if (!pattern) {
    return "Grammar";
  }
  return pattern
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .replace(/Verb/g, "Verb ")
    .replace(/\s+/g, " ")
    .trim();
}

export function humanizeReason(reason?: string) {
  if (!reason) {
    return "needs retry";
  }
  return reason.replace(/_/g, " ");
}

export function humanizeConceptId(conceptId?: string) {
  if (!conceptId) {
    return "General";
  }
  return conceptId
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .replace(/_/g, " ")
    .trim();
}
