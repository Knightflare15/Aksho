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

import { average, formatConfidencePercent, normalizeConfidence, runTime, soundLabel, titleCase } from "./portalCore";
import { colorOutcomeStatus, countingOutcomeStatus, type MiniGameOutcomeStatus } from "./miniGameOutcomes";
import { uniqueValue } from "./portalTail";

export function buildLearningTrajectory(runs: RunSession[]) {
  const ordered = [...runs].sort((a, b) => runTime(b) - runTime(a));
  const recent = ordered.slice(0, 3);
  const previous = ordered.slice(3, 6);
  if (recent.length === 0) {
    return [{
      label: "Waiting",
      title: "No runs yet",
      detail: "Trajectory appears after the first few completed runs.",
      state: "neutral"
    }];
  }

  const recentConfidence = average(recent.map((run) => normalizeConfidence(run.averageConfidence)));
  const previousConfidence = previous.length ? average(previous.map((run) => normalizeConfidence(run.averageConfidence))) : recentConfidence;
  const confidenceDelta = recentConfidence - previousConfidence;
  const recentAttempts = average(recent.map((run) => run.averageAttemptsPerLetter));
  const previousAttempts = previous.length ? average(previous.map((run) => run.averageAttemptsPerLetter)) : recentAttempts;
  const attemptsDelta = previousAttempts - recentAttempts;
  const recentCompletion = average(recent.map((run) => run.completed ? 1 : 0));
  const previousCompletion = previous.length ? average(previous.map((run) => run.completed ? 1 : 0)) : recentCompletion;
  const completionDelta = recentCompletion - previousCompletion;

  return [
    trendItem(
      "Confidence",
      confidenceDelta,
      `${formatConfidencePercent(recentConfidence)} recent average`,
      "Confidence is climbing across recent runs.",
      "Confidence is slipping compared with earlier runs.",
      "Confidence is steady."
    ),
    trendItem(
      "Attempts",
      attemptsDelta,
      `${recentAttempts.toFixed(1)} recent attempts per letter`,
      "Letters are taking fewer tries.",
      "Letters are taking more tries than before.",
      "Attempt count is steady."
    ),
    trendItem(
      "Completion",
      completionDelta,
      `${Math.round(recentCompletion * 100)}% recent completion`,
      "Run completion is improving.",
      "Run completion is dropping.",
      "Completion is steady."
    )
  ];
}

export function buildLetterHeatmap(attempts: LetterAttempt[], practicedLetters: string[]) {
  if (attempts.length === 0) {
    return practicedLetters
      .filter(uniqueValue)
      .map((letter) => ({
        letter,
        level: "low",
        label: "Practiced"
      }));
  }

  const byLetter = new Map<string, LetterAttempt[]>();
  attempts.forEach((attempt) => {
    const letter = attempt.letter?.toUpperCase();
    if (!letter) {
      return;
    }
    byLetter.set(letter, [...(byLetter.get(letter) ?? []), attempt]);
  });

  return Array.from(byLetter.entries())
    .map(([letter, letterAttempts]) => {
      const averageAttempts = average(letterAttempts.map((attempt) => attempt.attempts || 1));
      const issueCount = letterAttempts.filter((attempt) => !attempt.confident || (attempt.handwritingDiagnostics?.severity ?? 0) >= 2).length;
      if (issueCount >= 2 || averageAttempts >= 2.5) {
        return { letter, level: "high", label: "Needs practice" };
      }
      if (issueCount >= 1 || averageAttempts > 1.2) {
        return { letter, level: "medium", label: "Watch" };
      }
      return { letter, level: "low", label: "Strong" };
    })
    .sort((a, b) => heatLevelRank(b.level) - heatLevelRank(a.level) || a.letter.localeCompare(b.letter))
    .slice(0, 8);
}

export function heatLevelRank(level: string) {
  return level === "high" ? 2 : level === "medium" ? 1 : 0;
}

export function trendItem(
  label: string,
  delta: number,
  title: string,
  growthDetail: string,
  declineDetail: string,
  neutralDetail: string
) {
  const threshold = label === "Attempts" ? 0.35 : 0.08;
  if (delta >= threshold) {
    return { label, title: `Growth: ${title}`, detail: growthDetail, state: "growth" };
  }
  if (delta <= -threshold) {
    return { label, title: `Deterioration: ${title}`, detail: declineDetail, state: "decline" };
  }
  return { label, title, detail: neutralDetail, state: "neutral" };
}

export function buildDevelopmentReviewFlags(
  attempts: LetterAttempt[],
  casts: WordCast[],
  countingAttempts: CountingMiniGameAttempt[],
  colorAttempts: ColorMiniGameAttempt[],
  empathyEvents: EmpathyEventAttempt[]
) {
  const writingFlags = [
    buildWritingPatternFlag(
      "Reading/writing reversal pattern",
      attempts,
      ["mirror", "reversedStroke"],
      "Repeated mirrored or reversed formations can resemble broader early learning-difficulty risk signs when they persist across letters."
    ),
    buildWritingPatternFlag(
      "Letter sequencing pattern",
      attempts,
      ["wrongStrokeOrder", "wrongDirection", "wrongStart"],
      "Repeated start-direction or stroke-order trouble may point to a formation or symbol-sequencing gap."
    ),
    buildWritingPatternFlag(
      "Spatial writing pattern",
      attempts,
      ["spacingDrift", "aboveBaseline", "belowBaseline", "floating", "oversized", "undersized"],
      "Baseline drift, spacing drift, or size instability may need targeted fine-motor and visual spacing practice."
    ),
    buildWritingPatternFlag(
      "Correction fatigue pattern",
      attempts,
      ["repeatedCorrection", "overdrawn", "extraStrokes", "wobbly"],
      "Heavy retracing, extra strokes, or local kinks can show uncertainty even when the final letter is accepted."
    )
  ];

  const speechFlags = buildSpeechPatternFlags(casts);
  const miniGameFlags = [
    ...buildCognitiveMiniGameFlags("Counting", countingAttempts, countingOutcomeStatus),
    ...buildCognitiveMiniGameFlags("Color", colorAttempts, colorOutcomeStatus),
    ...buildEmpathyEventFlags(empathyEvents)
  ];
  const flags = [...writingFlags, ...speechFlags, ...miniGameFlags]
    .sort((a, b) => reviewLevelRank(b.level) - reviewLevelRank(a.level) || b.count - a.count)
    .slice(0, 9);

  return flags.some((flag) => flag.level !== "low")
    ? flags
    : [{
      title: "No persistent review flags yet",
      evidence: "Recent attempts do not show repeated risk patterns.",
      detail: "Continue collecting run data and review again after more writing and speaking samples.",
      level: "low",
      count: 0
    }];
}

export function buildWritingPatternFlag(title: string, attempts: LetterAttempt[], tags: string[], detail: string) {
  const matches = attempts.filter((attempt) =>
    (attempt.handwritingDiagnostics?.tags ?? []).some((tag) => tags.includes(tag))
  );
  const letters = matches.map((attempt) => attempt.letter).filter(uniqueValue).sort();
  const dates = matches.map((attempt) => dateKey(attempt.createdAt)).filter(Boolean).filter(uniqueValue);
  const strongest = matches
    .map((attempt) => attempt.handwritingDiagnostics)
    .filter((diagnostic): diagnostic is HandwritingDiagnosticSummary => Boolean(diagnostic))
    .sort((a, b) => b.severity - a.severity)[0];
  const tagCounts = countValues(matches.flatMap((attempt) =>
    (attempt.handwritingDiagnostics?.tags ?? []).filter((tag) => tags.includes(tag))
  ));
  const topTag = topMapEntry(tagCounts);
  const evidence = matches.length === 0
    ? "No repeated signal"
    : `${matches.length} signal${matches.length === 1 ? "" : "s"} across ${dates.length || 1} day${(dates.length || 1) === 1 ? "" : "s"}${letters.length ? `: ${letters.join(", ")}` : ""}`;
  const topDetail = topTag
    ? `Most common marker: ${humanizeDiagnosticTag(topTag[0])} (${topTag[1]}).`
    : "";

  return {
    title,
    evidence,
    detail: `${detail} ${topDetail} ${strongest?.primaryHint ? `Recent hint: ${strongest.primaryHint}` : ""}`.trim(),
    level: reviewLevel(matches.length, dates.length, letters.length),
    count: matches.length
  };
}

export function buildSpeechPatternFlags(casts: WordCast[]) {
  const confusionCounts = new Map<string, {
    count: number;
    words: Set<string>;
    dates: Set<string>;
    expected: string;
    heard: string;
  }>();
  let failedProofs = 0;
  const failedDates = new Set<string>();

  casts.forEach((cast) => {
    const castDate = dateKey(cast.createdAtUtc || cast.createdAt);
    const insight = effectivePronunciationInsight(cast);
    if (!cast.success || hasPracticeSpeechSegment(cast)) {
      failedProofs++;
      if (castDate) {
        failedDates.add(castDate);
      }
    }

    (insight?.segments ?? []).forEach((segment) => {
      if (!isPracticeSpeechSegment(segment)) {
        return;
      }
      const expected = soundLabel(segment);
      const heard = (segment.heardSound || "missing").trim().toLowerCase();
      const key = `${expected}->${heard}`;
      const current = confusionCounts.get(key) ?? {
        count: 0,
        words: new Set<string>(),
        dates: new Set<string>(),
        expected,
        heard
      };
      current.count += 1;
      current.words.add(cast.word);
      if (castDate) {
        current.dates.add(castDate);
      }
      confusionCounts.set(key, current);
    });
  });

  const repeatedConfusions = Array.from(confusionCounts.values())
    .sort((a, b) => b.count - a.count)
    .slice(0, 2)
    .map((confusion) => ({
      title: confusion.heard === "missing"
        ? `Speech sound missing: ${confusion.expected}`
        : `Speech sound substitution: ${confusion.expected} -> ${confusion.heard}`,
      evidence: `${confusion.count} signal${confusion.count === 1 ? "" : "s"} across ${confusion.dates.size || 1} day${(confusion.dates.size || 1) === 1 ? "" : "s"} in ${Array.from(confusion.words).slice(0, 4).join(", ")}`,
      detail: "Repeated missing or substituted sound segments should be reviewed as pronunciation practice evidence, not a diagnosis.",
      level: reviewLevel(confusion.count, confusion.dates.size, confusion.words.size),
      count: confusion.count
    }));

  const broadSpeechFlag = {
    title: "Speech proof persistence",
    evidence: failedProofs === 0
      ? "No repeated signal"
      : `${failedProofs} failed or focused speech proof signal${failedProofs === 1 ? "" : "s"} across ${failedDates.size || 1} day${(failedDates.size || 1) === 1 ? "" : "s"}`,
    detail: "Repeated speech proof failures over time may indicate a sound-production pattern worth teacher or specialist review if it persists.",
    level: reviewLevel(failedProofs, failedDates.size, 1),
    count: failedProofs
  };

  return [...repeatedConfusions, broadSpeechFlag];
}

export function buildCognitiveMiniGameFlags<T>(
  label: "Counting" | "Color",
  attempts: T[],
  statusFor: (attempt: T) => MiniGameOutcomeStatus
) {
  const statuses = attempts.map(statusFor);
  const ignored = attempts.filter((attempt) => statusFor(attempt) === "seen_ignored");
  const wrong = attempts.filter((attempt) => statusFor(attempt) === "opened_wrong_answer");
  const speechFail = attempts.filter((attempt) => statusFor(attempt) === "opened_correct_pronunciation_failed");
  const ignoredDates = new Set(ignored.map(miniGameAttemptDate).filter(Boolean));
  const wrongDates = new Set(wrong.map(miniGameAttemptDate).filter(Boolean));
  const speechFailDates = new Set(speechFail.map(miniGameAttemptDate).filter(Boolean));

  return [
    {
      title: `${label} engagement pattern`,
      evidence: ignored.length === 0
        ? "No repeated signal"
        : `${ignored.length} seen-but-ignored signal${ignored.length === 1 ? "" : "s"} across ${ignoredDates.size || 1} day${(ignoredDates.size || 1) === 1 ? "" : "s"}`,
      detail: `${label} chests were noticed but not opened. This can be an engagement, attention, confidence, or task-avoidance review signal if it persists.`,
      level: reviewLevel(ignored.length, ignoredDates.size, 1),
      count: ignored.length
    },
    {
      title: `${label} concept practice pattern`,
      evidence: wrong.length === 0
        ? "No repeated signal"
        : `${wrong.length} wrong-answer signal${wrong.length === 1 ? "" : "s"} across ${wrongDates.size || 1} day${(wrongDates.size || 1) === 1 ? "" : "s"}`,
      detail: `${label} answers were attempted but missed. Review whether this clusters around quantity comparison, one-to-one counting, color discrimination, or instruction comprehension.`,
      level: reviewLevel(wrong.length, wrongDates.size, 1),
      count: wrong.length
    },
    {
      title: `${label} answer-known speech pattern`,
      evidence: speechFail.length === 0
        ? "No repeated signal"
        : `${speechFail.length} correct-answer speech-fail signal${speechFail.length === 1 ? "" : "s"} across ${speechFailDates.size || 1} day${(speechFailDates.size || 1) === 1 ? "" : "s"}`,
      detail: `The child selected the correct ${label.toLowerCase()} answer but speech proof failed. This separates knowledge from pronunciation or confidence with spoken proof.`,
      level: reviewLevel(speechFail.length, speechFailDates.size, 1),
      count: speechFail.length
    }
  ].filter((flag) => flag.count > 0);

  function miniGameAttemptDate(attempt: T) {
    const dated = attempt as { createdAtUtc?: string; createdAt?: string };
    return dateKey(dated.createdAtUtc || dated.createdAt);
  }
}

export function buildEmpathyEventFlags(events: EmpathyEventAttempt[]) {
  const ignored = events.filter((event) => empathyOutcomeStatus(event) === "seen_ignored");
  const needsSupport = events.filter((event) => empathyOutcomeStatus(event) === "needs_support");
  const reflectionSupport = events.filter((event) => empathyOutcomeStatus(event) === "supportive_choice_reflection_needed");
  const skillCounts = countValues(needsSupport.map((event) => event.empathySkill || event.eventCategory).filter(Boolean));
  const topSkill = topMapEntry(skillCounts);
  const ignoredDates = new Set(ignored.map((event) => dateKey(event.createdAtUtc || event.createdAt)).filter(Boolean));
  const supportDates = new Set(needsSupport.map((event) => dateKey(event.createdAtUtc || event.createdAt)).filter(Boolean));
  const reflectionDates = new Set(reflectionSupport.map((event) => dateKey(event.createdAtUtc || event.createdAt)).filter(Boolean));

  return [
    {
      title: "Empathy event engagement pattern",
      evidence: ignored.length === 0
        ? "No repeated signal"
        : `${ignored.length} seen-but-ignored event${ignored.length === 1 ? "" : "s"} across ${ignoredDates.size || 1} day${(ignoredDates.size || 1) === 1 ? "" : "s"}`,
      detail: "Empathy prompts were shown but skipped. If this persists, review whether the child needs simpler social prompts, more time, or teacher modeling.",
      level: reviewLevel(ignored.length, ignoredDates.size, 1),
      count: ignored.length
    },
    {
      title: "Empathy choice support pattern",
      evidence: needsSupport.length === 0
        ? "No repeated signal"
        : `${needsSupport.length} support-needed choice${needsSupport.length === 1 ? "" : "s"} across ${supportDates.size || 1} day${(supportDates.size || 1) === 1 ? "" : "s"}`,
      detail: `Empathy choices may need support${topSkill ? `, especially around ${topSkill[0]} (${topSkill[1]} signal${topSkill[1] === 1 ? "" : "s"})` : ""}. Treat this as teacher-observation evidence, not a social-emotional diagnosis.`,
      level: reviewLevel(needsSupport.length, supportDates.size, skillCounts.size),
      count: needsSupport.length
    },
    {
      title: "Empathy reflection follow-through",
      evidence: reflectionSupport.length === 0
        ? "No repeated signal"
        : `${reflectionSupport.length} reflection-needed signal${reflectionSupport.length === 1 ? "" : "s"} across ${reflectionDates.size || 1} day${(reflectionDates.size || 1) === 1 ? "" : "s"}`,
      detail: "The child selected a supportive response but did not complete the reflection step. This can guide teacher follow-up questions after play.",
      level: reviewLevel(reflectionSupport.length, reflectionDates.size, 1),
      count: reflectionSupport.length
    }
  ].filter((flag) => flag.count > 0);
}

export function empathyOutcomeStatus(event: EmpathyEventAttempt) {
  if (event.outcomeStatus === "seen_ignored" ||
    event.outcomeStatus === "needs_support" ||
    event.outcomeStatus === "supportive_choice_reflection_needed" ||
    event.outcomeStatus === "supportive_choice") {
    return event.outcomeStatus;
  }
  if (!event.selectedResponse) {
    return "seen_ignored";
  }
  if (event.targetResponse && event.selectedResponse !== event.targetResponse) {
    return "needs_support";
  }
  if (!event.reflectionText) {
    return "supportive_choice_reflection_needed";
  }
  return "supportive_choice";
}

export function empathyOutcomeLabel(status: string) {
  switch (status) {
    case "seen_ignored":
      return "Seen but ignored";
    case "needs_support":
      return "Needs support";
    case "supportive_choice_reflection_needed":
      return "Supportive choice, reflection needed";
    case "supportive_choice":
      return "Supportive choice";
    default:
      return titleCase(status);
  }
}

export function reviewLevel(count: number, dateCount: number, varietyCount: number) {
  if (count >= 5 && (dateCount >= 2 || varietyCount >= 2)) {
    return "high";
  }
  if (count >= 2) {
    return "medium";
  }
  return "low";
}

export function reviewLevelRank(level: string) {
  return level === "high" ? 2 : level === "medium" ? 1 : 0;
}

export function countValues(values: string[]) {
  const counts = new Map<string, number>();
  values.forEach((value) => counts.set(value, (counts.get(value) ?? 0) + 1));
  return counts;
}

export function topMapEntry(counts: Map<string, number>) {
  return Array.from(counts.entries()).sort((a, b) => b[1] - a[1])[0];
}

export function dateKey(value?: string) {
  if (!value) {
    return "";
  }
  const parsed = Date.parse(value);
  return Number.isFinite(parsed) ? new Date(parsed).toISOString().slice(0, 10) : "";
}

export function isPracticeSpeechSegment(segment: PhoneticSegment) {
  return segment.status === "Missing" ||
    segment.status === "NeedsPractice" ||
    Boolean(segment.heardSound && segment.heardSound.trim() && segment.heardSound.trim().toLowerCase() !== soundLabel(segment));
}

export function firstPracticeSpeechSegment(cast: WordCast) {
  const insight = effectivePronunciationInsight(cast);
  return (insight?.segments ?? []).find(isPracticeSpeechSegment)
    ?? (insight?.focusSegment && isPracticeSpeechSegment(insight.focusSegment)
      ? insight.focusSegment
      : undefined);
}

export function hasPracticeSpeechSegment(cast: WordCast) {
  return Boolean(firstPracticeSpeechSegment(cast));
}

export function effectivePronunciationInsight(cast: WordCast) {
  return cast.serverPronunciationInsight ?? cast.pronunciationInsight;
}

export function humanizeDiagnosticTag(tag: string) {
  return {
    mirror: "Mirror/reversal",
    reversedStroke: "Reversed stroke",
    wrongStrokeOrder: "Stroke order",
    wrongDirection: "Stroke direction",
    wrongStart: "Start point",
    spacingDrift: "Spacing drift",
    aboveBaseline: "Above baseline",
    belowBaseline: "Below baseline",
    floating: "Floating",
    oversized: "Oversized",
    undersized: "Undersized",
    repeatedCorrection: "Repeated correction",
    overdrawn: "Overdrawn",
    extraStrokes: "Extra strokes",
    wobbly: "Local kinks"
  }[tag] ?? titleCase(tag);
}
