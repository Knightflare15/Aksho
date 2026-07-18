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

export type MiniGameOutcomeStatus =
  | "seen_ignored"
  | "opened_wrong_answer"
  | "opened_correct_pronunciation_failed"
  | "opened_correct";

export function buildMiniGameOutcomeCounts(statuses: MiniGameOutcomeStatus[]) {
  return {
    seen_ignored: statuses.filter((status) => status === "seen_ignored").length,
    opened_wrong_answer: statuses.filter((status) => status === "opened_wrong_answer").length,
    opened_correct_pronunciation_failed: statuses.filter((status) => status === "opened_correct_pronunciation_failed").length,
    opened_correct: statuses.filter((status) => status === "opened_correct").length
  };
}

export function countingOutcomeStatus(attempt: CountingMiniGameAttempt): MiniGameOutcomeStatus {
  if (isMiniGameOutcomeStatus(attempt.outcomeStatus)) {
    return attempt.outcomeStatus;
  }
  if ((attempt.selectedCount ?? 0) <= 0) {
    return "seen_ignored";
  }
  if (!attempt.countCorrect) {
    return "opened_wrong_answer";
  }
  return attempt.speechProofSucceeded ? "opened_correct" : "opened_correct_pronunciation_failed";
}

export function colorOutcomeStatus(attempt: ColorMiniGameAttempt): MiniGameOutcomeStatus {
  if (isMiniGameOutcomeStatus(attempt.outcomeStatus)) {
    return attempt.outcomeStatus;
  }
  if (!attempt.selectedColor) {
    return "seen_ignored";
  }
  if (!attempt.colorCorrect) {
    return "opened_wrong_answer";
  }
  return attempt.speechProofSucceeded ? "opened_correct" : "opened_correct_pronunciation_failed";
}

export function isMiniGameOutcomeStatus(value?: string): value is MiniGameOutcomeStatus {
  return value === "seen_ignored" ||
    value === "opened_wrong_answer" ||
    value === "opened_correct_pronunciation_failed" ||
    value === "opened_correct";
}

export function miniGameOutcomeLabel(status: MiniGameOutcomeStatus) {
  switch (status) {
    case "seen_ignored":
      return "Seen but ignored";
    case "opened_wrong_answer":
      return "Opened, wrong answer";
    case "opened_correct_pronunciation_failed":
      return "Opened, correct answer, pronunciation failed";
    case "opened_correct":
      return "Opened, correct answer";
  }
}
