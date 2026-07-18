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

import { addDays, alphabet, buildStudentOverride, durationPresets, findWorldGoalDraft, grammarRegionOptions, splitCsvValues, splitWords } from "../utils/missionPlanning";
import { average, computeClassStats, formatConfidencePercent, formatDuration, formatGoalClaim, humanizeAreaId, humanizeBuddyAssistMode, humanizeConceptId, humanizePattern, humanizeReason, missionTypeLabel, normalizeConfidence, runDateKey, runTime } from "../utils/portalCore";
import { buildDevelopmentReviewFlags, buildLearningTrajectory, buildLetterHeatmap, dateKey, effectivePronunciationInsight, hasPracticeSpeechSegment, humanizeDiagnosticTag, isPracticeSpeechSegment } from "../utils/portalAnalytics";
import { buildAcceptedAfterRetryWords, buildAcceptedVocabularyEvidence, buildErrorSourceBreakdown, buildGrammarMasterySummary, buildGrammarVocabularySummary, buildMostCommonErrorCategories, buildMostMissedConcepts, buildWorldGoalStudentProgress, masteryTagLabel, mergeVocabularyTokens, normalizeMasteryTags } from "../utils/grammarReporting";
import { buildMiniGameOutcomeCounts, colorOutcomeStatus, countingOutcomeStatus, miniGameOutcomeLabel, type MiniGameOutcomeStatus } from "../utils/miniGameOutcomes";
import { joinOrFallback, shortDate, uniqueValue } from "../utils/portalTail";
import { ClockIcon, Header, LetterPicker, Metric, RecommendationList, SegmentedNav, SignalGroup, StrokePreview, StudentRow } from "../components/PortalUi";


export function StudentDashboard(props: { dataset: PortalDataset }) {
  const student = props.dataset.students[0];
  const runs = props.dataset.runSessions.filter((run) => run.studentId === student?.id);
  const latest = runs[0];
  const attempts = props.dataset.letterAttempts.filter((attempt) => attempt.studentId === student?.id);
  const wordCasts = props.dataset.wordCasts.filter((cast) => cast.studentId === student?.id);
  const weakLetters = attempts
    .filter((attempt) => !attempt.confident || attempt.attempts > 1 || (attempt.handwritingDiagnostics?.severity ?? 0) >= 2)
    .map((attempt) => attempt.letter)
    .filter((letter, index, letters) => letters.indexOf(letter) === index)
    .slice(0, 5);
  const strongLetters = attempts
    .filter((attempt) => attempt.confident && attempt.attempts <= 1)
    .map((attempt) => attempt.letter)
    .filter((letter, index, letters) => letters.indexOf(letter) === index)
    .slice(0, 5);
  const practiceWords = wordCasts
    .filter((cast) => !cast.success)
    .map((cast) => cast.word)
    .filter((word, index, words) => words.indexOf(word) === index)
    .slice(0, 5);

  if (!student) {
    return (
      <section className="panel">
        <h2>No student profile</h2>
        <p className="muted">Ask your teacher to check that this login is linked to a student account.</p>
      </section>
    );
  }

  return (
    <div className="pageStack parentWidth">
      <Header title={`${student.name}'s Practice`} subtitle="Your teacher can see the same run history, strengths, and practice needs." />
      <div className="metricGrid parentMetrics">
        <Metric label="Latest confidence" value={formatConfidencePercent(latest?.averageConfidence)} />
        <Metric label="Attempts" value={(latest?.averageAttemptsPerLetter ?? 0).toFixed(1)} />
      </div>
      <section className="panel">
        <div className="sectionHeader">
          <div>
            <h2>Strengths</h2>
            <p>Letters and words that are becoming comfortable.</p>
          </div>
          <CheckCircle2 size={28} />
        </div>
        <div className="tokenRow">
          {(strongLetters.length ? strongLetters : latest?.lettersPracticed ?? []).map((letter) => <span className="letterToken" key={letter}>{letter}</span>)}
          {latest?.wordsPracticed.map((word) => <span className="wordToken" key={word}>{word}</span>)}
        </div>
      </section>
      <section className="panel">
        <h2>Practice Next</h2>
        <div className="tokenRow">
          {(weakLetters.length ? weakLetters : latest?.lettersPracticed?.slice(0, 2) ?? []).map((letter) => <span className="letterToken" key={letter}>{letter}</span>)}
          {practiceWords.map((word) => <span className="wordToken" key={word}>{word}</span>)}
        </div>
      </section>
    </div>
  );
}
