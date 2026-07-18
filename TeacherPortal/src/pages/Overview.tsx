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

import type { TeacherTab } from "./TeacherDashboard";

export function Overview(props: {
  mission: DailyMissionAssignment;
  worldGoals: WorldGoalAssignment[];
  worldGoalClaims: WorldGoalClaim[];
  classStats: { completed: number; confidence: number; attempts: number; areas: number };
  students: Student[];
  runSessions: RunSession[];
  spokenPhraseEvents: SpokenPhraseEvent[];
  writtenPhraseEvents: WrittenPhraseEvent[];
  grammarBattleEvents: GrammarBattleEvent[];
  buddyConversationTurns: BuddyConversationTurn[];
  gymAttempts: GymAttempt[];
  recommendations: { id: string; priority: string; title: string; detail: string }[];
  setTab: (tab: TeacherTab) => void;
}) {
  const activeGoal = props.worldGoals[0];
  const spokenAccepted = props.spokenPhraseEvents.filter((event) => event.accepted).length;
  const writtenAccepted = props.writtenPhraseEvents.filter((event) => event.accepted).length;
  const battleAccepted = props.grammarBattleEvents.filter((event) => event.accepted).length;
  const gymPasses = props.gymAttempts.filter((attempt) => attempt.passed).length;
  const acceptedVocabularyEvidence = buildAcceptedVocabularyEvidence(
    props.spokenPhraseEvents,
    props.writtenPhraseEvents,
    props.grammarBattleEvents
  );
  const acceptedSpokenWords = mergeVocabularyTokens(
    acceptedVocabularyEvidence.filter((item) => item.spoken > 0).map((item) => item.word),
    ...props.runSessions.map((session) => session.acceptedSpokenVocabulary)
  ).slice(0, 12);
  const acceptedWrittenWords = mergeVocabularyTokens(
    acceptedVocabularyEvidence.filter((item) => item.written > 0).map((item) => item.word),
    ...props.runSessions.map((session) => session.acceptedWrittenVocabulary)
  ).slice(0, 12);
  const acceptedBattleWords = mergeVocabularyTokens(
    acceptedVocabularyEvidence.filter((item) => item.battle > 0).map((item) => item.word),
    ...props.runSessions.map((session) => session.acceptedBattleVocabulary)
  ).slice(0, 12);
  const sessionMasteryTags = props.runSessions
    .flatMap((session) => normalizeMasteryTags(session.masteryTagsPracticed))
    .filter(uniqueValue)
    .map(masteryTagLabel)
    .slice(0, 12);
  const targetGymPasses = activeGoal
    ? props.gymAttempts.filter((attempt) => attempt.passed && attempt.gymId === activeGoal.targetGymId)
    : [];
  const targetGymStudents = targetGymPasses.map((attempt) => attempt.studentId).filter(uniqueValue).length;
  const goalProgressRows = activeGoal
    ? buildWorldGoalStudentProgress(
        props.students,
        activeGoal,
        props.spokenPhraseEvents,
        props.writtenPhraseEvents,
        props.grammarBattleEvents,
        props.runSessions,
        props.gymAttempts
      )
    : [];

  return (
    <div className="pageStack">
      <div className="metricGrid">
        <Metric label="Practice completion" value={`${props.classStats.completed}/${props.students.length}`} />
        <Metric label="Avg confidence" value={formatConfidencePercent(props.classStats.confidence)} />
        <Metric label="Avg attempts" value={props.classStats.attempts.toFixed(1)} />
        <Metric label="Areas cleared" value={String(props.classStats.areas)} />
      </div>
      <section className="panel">
        <div className="sectionHeader">
          <div>
            <h2>Class Focus</h2>
            <p>{activeGoal ? `Clear ${humanizeAreaId(activeGoal.targetGymId)} by ${activeGoal.dueDate} for +${activeGoal.rewardCoins ?? 25} coins.` : "No weekly goal assigned."}</p>
          </div>
          <button className="secondaryButton" onClick={() => props.setTab("mission")}>
            <ClipboardCheck size={18} />
            Plan
          </button>
        </div>
        {activeGoal && (
          <>
            <div className="tokenRow">
              {activeGoal.focusGrammarPatterns.map((pattern) => <span className="wordToken" key={pattern}>{humanizePattern(pattern)}</span>)}
            </div>
            <div className="tokenRow">
              {activeGoal.focusVocabulary.map((word) => <span className="letterToken" key={word}>{word}</span>)}
            </div>
          </>
        )}
        <div className="metricGrid compactMetrics">
          <Metric label="Spoken phrases" value={String(spokenAccepted)} />
          <Metric label="Written phrases" value={String(writtenAccepted)} />
          <Metric label="Battle commands" value={String(battleAccepted)} />
          <Metric label="Focus gym clears" value={activeGoal ? `${targetGymStudents}/${props.students.length}` : String(gymPasses)} />
        </div>
        <div className="signalGrid">
          <SignalGroup title="Accepted spoken words" values={acceptedSpokenWords} kind="word" />
          <SignalGroup title="Accepted written words" values={acceptedWrittenWords} kind="word" />
          <SignalGroup title="Accepted battle words" values={acceptedBattleWords} kind="word" />
          <SignalGroup title="Session mastery" values={sessionMasteryTags} kind="word" />
        </div>
        {activeGoal && (
          <div className="voiceCastGrid">
            {goalProgressRows.map((row) => (
              <article className="voiceCastCard" key={row.student.id} data-success={row.targetCleared ? "true" : "false"}>
                <div className="templateMeta">
                  <strong>{row.student.name}</strong>
                  <span>{row.targetCleared ? "checkpoint cleared" : `next checkpoint: ${humanizeAreaId(activeGoal.targetGymId)}`}</span>
                </div>
                <p>
                  Latest evidence: {row.latestArea ? humanizeAreaId(row.latestArea) : "No Grammar RPG evidence yet"}.
                  {" "}{row.spokenAccepted} spoken, {row.writtenAccepted} written, {row.battleAccepted} battle command{row.battleAccepted === 1 ? "" : "s"}.
                  {" "}Buddy: {humanizeBuddyAssistMode(row.latestBuddyAssistMode)}.
                </p>
                <p className="muted">
                  Focus coverage: {row.focusPatternsUsed}/{activeGoal.focusGrammarPatterns.length} patterns,
                  {" "}{row.focusVocabularyUsed}/{activeGoal.focusVocabulary.length} vocabulary words.
                </p>
                <p className="muted">
                  Reward: {formatGoalClaim(props.worldGoalClaims.find(claim => claim.goalId === activeGoal.goalId && claim.studentId === row.student.id))}
                </p>
                <div className="tokenRow">
                  {row.usedPatterns.map((pattern) => <span className="wordToken" key={`pattern-${pattern}`}>{humanizePattern(pattern)}</span>)}
                  {row.usedMasteryTags.map((tag) => <span className="wordToken" key={`tag-${tag}`}>{masteryTagLabel(tag)}</span>)}
                  {row.usedVocabulary.map((word) => <span className="letterToken" key={`word-${word}`}>{word}</span>)}
                </div>
              </article>
            ))}
          </div>
        )}
      </section>
      <section className="panel">
        <div className="sectionHeader">
          <div>
            <h2>Practice Calendar</h2>
            <p>{missionTypeLabel(props.mission.missionType)} - {formatDuration(props.mission.missionDurationSeconds)}. Sets the repeatable run length and review pool beneath the current class focus.</p>
          </div>
          <button className="secondaryButton" onClick={() => props.setTab("mission")}>
            <CalendarDays size={18} />
            Edit
          </button>
        </div>
        <div className="tokenRow">
          {props.mission.lettersForToday.map((letter) => <span className="letterToken" key={letter}>{letter}</span>)}
        </div>
        <div className="tokenRow">
          {props.mission.wordsForToday.map((word) => <span className="wordToken" key={word}>{word}</span>)}
        </div>
      </section>
      <section className="panel">
        <h2>Recommendations</h2>
        <RecommendationList recommendations={props.recommendations} />
      </section>
    </div>
  );
}
