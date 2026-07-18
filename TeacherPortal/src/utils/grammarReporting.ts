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

import { dateKey, effectivePronunciationInsight, hasPracticeSpeechSegment, isPracticeSpeechSegment } from "./portalAnalytics";
import { humanizeConceptId, humanizeReason, runDateKey } from "./portalCore";
import { uniqueValue } from "./portalTail";

export function buildGrammarMasterySummary(
  spokenPhrases: SpokenPhraseEvent[],
  writtenPhrases: WrittenPhraseEvent[],
  battleEvents: GrammarBattleEvent[],
  gymAttempts: GymAttempt[]
) {
  const mastered = new Set<string>();
  const needsPractice = new Set<string>();
  const collect = (tags: string[] | undefined, accepted: boolean) => {
    for (const tag of normalizeMasteryTags(tags)) {
      if (accepted) {
        mastered.add(tag);
        needsPractice.delete(tag);
      } else if (!mastered.has(tag)) {
        needsPractice.add(tag);
      }
    }
  };

  spokenPhrases.forEach((event) => collect(event.masteryTags, event.accepted));
  writtenPhrases.forEach((event) => collect(event.masteryTags, event.accepted));
  battleEvents.forEach((event) => collect(event.masteryTags, event.accepted));
  gymAttempts.forEach((attempt) => collect(attempt.masteryTags, attempt.passed));

  return {
    mastered: Array.from(mastered).map(masteryTagLabel).slice(0, 8),
    needsPractice: Array.from(needsPractice).map(masteryTagLabel).slice(0, 8)
  };
}

export function buildGrammarVocabularySummary(
  spokenPhrases: SpokenPhraseEvent[],
  writtenPhrases: WrittenPhraseEvent[],
  battleEvents: GrammarBattleEvent[]
) {
  const used = new Set<string>();
  const needsPractice = new Set<string>();
  const collect = (tokens: string[] | undefined, accepted: boolean) => {
    for (const token of normalizeVocabularyTokens(tokens)) {
      if (accepted) {
        used.add(token);
        needsPractice.delete(token);
      } else if (!used.has(token)) {
        needsPractice.add(token);
      }
    }
  };

  spokenPhrases.forEach((event) => collect(event.vocabularyTokens, event.accepted));
  writtenPhrases.forEach((event) => collect(event.vocabularyTokens, event.accepted));
  battleEvents.forEach((event) => collect(event.vocabularyTokens, event.accepted));

  return {
    used: Array.from(used).slice(0, 10),
    needsPractice: Array.from(needsPractice).slice(0, 10)
  };
}

export function buildMostMissedConcepts(
  spokenPhrases: SpokenPhraseEvent[],
  writtenPhrases: WrittenPhraseEvent[],
  battleEvents: GrammarBattleEvent[]
) {
  const counts = new Map<string, number>();
  [...spokenPhrases, ...writtenPhrases, ...battleEvents]
    .filter((event) => !event.accepted)
    .forEach((event) => {
      const key = (event.conceptId ?? "General").trim();
      counts.set(key, (counts.get(key) ?? 0) + 1);
    });

  return Array.from(counts.entries())
    .sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]))
    .slice(0, 6)
    .map(([concept, count]) => `${humanizeConceptId(concept)} (${count})`);
}

export function buildMostCommonErrorCategories(
  spokenPhrases: SpokenPhraseEvent[],
  writtenPhrases: WrittenPhraseEvent[],
  battleEvents: GrammarBattleEvent[]
) {
  const counts = new Map<string, number>();
  [...spokenPhrases, ...writtenPhrases, ...battleEvents]
    .filter((event) => !event.accepted)
    .forEach((event) => {
      const rejectionReason = "rejectionReason" in event ? event.rejectionReason : "";
      const key = (event.errorCategory ?? rejectionReason ?? "response_mismatch").trim();
      counts.set(key, (counts.get(key) ?? 0) + 1);
    });

  return Array.from(counts.entries())
    .sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]))
    .slice(0, 6)
    .map(([reason, count]) => `${humanizeReason(reason)} (${count})`);
}

export function buildAcceptedAfterRetryWords(
  spokenPhrases: SpokenPhraseEvent[],
  writtenPhrases: WrittenPhraseEvent[],
  battleEvents: GrammarBattleEvent[]
) {
  const failed = new Set<string>();
  const recovered = new Set<string>();
  [...spokenPhrases, ...writtenPhrases, ...battleEvents].forEach((event) => {
    const targetPhrase = "targetPhrase" in event ? event.targetPhrase : "";
    const phrase = "phrase" in event ? event.phrase : event.playerPhrase;
    const target = (targetPhrase ?? event.correctedResponse ?? phrase ?? "").trim().toUpperCase();
    if (!target) {
      return;
    }
    if (event.accepted) {
      if (failed.has(target)) {
        recovered.add(target);
      }
    } else {
      failed.add(target);
    }
  });
  return Array.from(recovered).slice(0, 8);
}

export function buildErrorSourceBreakdown(
  spokenPhrases: SpokenPhraseEvent[],
  writtenPhrases: WrittenPhraseEvent[],
  battleEvents: GrammarBattleEvent[]
) {
  let pronunciation = 0;
  let handwriting = 0;
  let grammar = 0;
  [...spokenPhrases, ...writtenPhrases, ...battleEvents]
    .filter((event) => !event.accepted)
    .forEach((event) => {
      const rejectionReason = "rejectionReason" in event ? event.rejectionReason : "";
      const key = (event.errorCategory ?? rejectionReason ?? "").toLowerCase();
      if (key.startsWith("pronunciation")) {
        pronunciation += 1;
      } else if (key.startsWith("handwriting")) {
        handwriting += 1;
      } else {
        grammar += 1;
      }
    });

  return { pronunciation, handwriting, grammar };
}

export function buildAcceptedVocabularyEvidence(
  spokenPhrases: SpokenPhraseEvent[],
  writtenPhrases: WrittenPhraseEvent[],
  battleEvents: GrammarBattleEvent[]
) {
  const buckets = new Map<string, {
    word: string;
    spoken: number;
    written: number;
    battle: number;
    patterns: Set<string>;
  }>();

  const ensure = (word: string) => {
    const normalized = word.trim().toUpperCase();
    let bucket = buckets.get(normalized);
    if (!bucket) {
      bucket = { word: normalized, spoken: 0, written: 0, battle: 0, patterns: new Set<string>() };
      buckets.set(normalized, bucket);
    }
    return bucket;
  };

  const collect = (
    tokens: string[] | undefined,
    grammarPattern: string | undefined,
    channel: "spoken" | "written" | "battle"
  ) => {
    for (const token of normalizeVocabularyTokens(tokens)) {
      const bucket = ensure(token);
      bucket[channel] += 1;
      if (grammarPattern) {
        bucket.patterns.add(grammarPattern);
      }
    }
  };

  spokenPhrases
    .filter((event) => event.accepted)
    .forEach((event) => collect(event.vocabularyTokens, event.grammarPattern, "spoken"));
  writtenPhrases
    .filter((event) => event.accepted)
    .forEach((event) => collect(event.vocabularyTokens, event.grammarPattern, "written"));
  battleEvents
    .filter((event) => event.accepted)
    .forEach((event) => collect(event.vocabularyTokens, event.grammarPattern, "battle"));

  return Array.from(buckets.values())
    .map((bucket) => ({
      word: bucket.word,
      spoken: bucket.spoken,
      written: bucket.written,
      battle: bucket.battle,
      total: bucket.spoken + bucket.written + bucket.battle,
      patterns: Array.from(bucket.patterns).slice(0, 3)
    }))
    .sort((a, b) => b.total - a.total || a.word.localeCompare(b.word));
}

export function buildWorldGoalStudentProgress(
  students: Student[],
  goal: WorldGoalAssignment,
  spokenPhrases: SpokenPhraseEvent[],
  writtenPhrases: WrittenPhraseEvent[],
  battleEvents: GrammarBattleEvent[],
  runSessions: RunSession[],
  gymAttempts: GymAttempt[]
) {
  const focusPatterns = new Set(goal.focusGrammarPatterns.map((pattern) => pattern.trim()).filter(Boolean));
  const focusVocabulary = new Set(goal.focusVocabulary.map((word) => word.trim().toUpperCase()).filter(Boolean));

  return students.map((student) => {
    const spoken = spokenPhrases.filter((event) => event.studentId === student.id);
    const written = writtenPhrases.filter((event) => event.studentId === student.id);
    const battles = battleEvents.filter((event) => event.studentId === student.id);
    const gyms = gymAttempts.filter((attempt) => attempt.studentId === student.id);
    const evidence = [...spoken, ...written, ...battles, ...gyms].sort((a, b) => evidenceTime(b) - evidenceTime(a));
    const sessions = runSessions.filter((session) => session.studentId === student.id);
    const acceptedPhraseEvents = [...spoken, ...written].filter((event) => event.accepted);
    const acceptedBattles = battles.filter((event) => event.accepted);
    const usedPatterns = [
      ...acceptedPhraseEvents.map((event) => event.grammarPattern),
      ...acceptedBattles.map((event) => event.grammarPattern),
      ...sessions.flatMap((session) => session.grammarPatternsPracticed ?? [])
    ]
      .filter((pattern) => focusPatterns.size === 0 || focusPatterns.has(pattern))
      .filter(uniqueValue);
    const usedVocabulary = [
      ...[...acceptedPhraseEvents, ...acceptedBattles].flatMap((event) => normalizeVocabularyTokens(event.vocabularyTokens)),
      ...sessions.flatMap((session) => normalizeVocabularyTokens(session.vocabularyTokens ?? session.wordsPracticed)),
      ...sessions.flatMap((session) => normalizeVocabularyTokens(session.acceptedSpokenVocabulary)),
      ...sessions.flatMap((session) => normalizeVocabularyTokens(session.acceptedWrittenVocabulary)),
      ...sessions.flatMap((session) => normalizeVocabularyTokens(session.acceptedBattleVocabulary))
    ]
      .filter((token) => focusVocabulary.size === 0 || focusVocabulary.has(token))
      .filter(uniqueValue);
    const usedMasteryTags = [
      ...[...acceptedPhraseEvents, ...acceptedBattles].flatMap((event) => normalizeMasteryTags(event.masteryTags)),
      ...gyms.flatMap((attempt) => normalizeMasteryTags(attempt.masteryTags)),
      ...sessions.flatMap((session) => normalizeMasteryTags(session.masteryTagsPracticed))
    ].filter(uniqueValue);
    const latestArea = evidence[0]?.areaId ?? "";
    const latestBuddyAssistMode = evidence[0]?.buddyAssistMode ?? "";

    return {
      student,
      targetCleared: gyms.some((attempt) => attempt.passed && attempt.gymId === goal.targetGymId),
      latestArea,
      latestBuddyAssistMode,
      spokenAccepted: spoken.filter((event) => event.accepted).length,
      writtenAccepted: written.filter((event) => event.accepted).length,
      battleAccepted: acceptedBattles.length,
      focusPatternsUsed: usedPatterns.length,
      focusVocabularyUsed: usedVocabulary.length,
      usedPatterns: usedPatterns.slice(0, 4),
      usedMasteryTags: usedMasteryTags.slice(0, 4),
      usedVocabulary: usedVocabulary.slice(0, 6)
    };
  });
}

export function evidenceTime(event: { createdAtUtc?: string; createdAt?: string; startedAtUtc?: string; startedAt?: string; endedAtUtc?: string; endedAt?: string }) {
  const value = event.createdAtUtc ?? event.createdAt ?? event.endedAtUtc ?? event.endedAt ?? event.startedAtUtc ?? event.startedAt ?? "";
  const millis = Date.parse(value);
  return Number.isFinite(millis) ? millis : 0;
}

export function normalizeVocabularyTokens(tokens?: string[]) {
  return (tokens ?? [])
    .map((token) => token.trim().toUpperCase())
    .filter(Boolean)
    .filter(uniqueValue);
}

export function mergeVocabularyTokens(...groups: Array<string[] | undefined>) {
  return groups
    .flatMap((group) => normalizeVocabularyTokens(group))
    .filter(uniqueValue);
}

export function normalizeMasteryTags(tags?: string[]) {
  return (tags ?? [])
    .map((tag) => tag.trim())
    .filter(Boolean)
    .filter(uniqueValue);
}

export function masteryTagLabel(tag: string) {
  return tag
    .split("-")
    .filter(Boolean)
    .map(titleCase)
    .join(" ");
}

export function clampChestCount(value: string | number, fallback: number) {
  const numeric = typeof value === "number" ? value : Number(value);
  if (!Number.isFinite(numeric)) {
    return fallback;
  }
  return Math.max(0, Math.min(2, Math.round(numeric)));
}

export function titleCase(value: string) {
  if (!value) {
    return "";
  }
  return value.slice(0, 1).toUpperCase() + value.slice(1).toLowerCase();
}
