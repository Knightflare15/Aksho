import {
  BarChart3,
  CalendarDays,
  CheckCircle2,
  ClipboardCheck,
  FileText,
  GraduationCap,
  RefreshCw,
  ShieldCheck,
  Sparkles,
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

import { Overview } from "./Overview";
import { ClassManager } from "./ClassManager";
import { MissionPlanner } from "./MissionPlanner";
import { Reports } from "./Reports";
import { HandwritingReview } from "./HandwritingReview";
import { TeacherAssistant } from "./TeacherAssistant";

export type TeacherTab = "overview" | "classes" | "mission" | "reports" | "handwriting" | "assistant";

export function TeacherDashboard(props: {
  profile: UserProfile;
  dataset: PortalDataset;
  setDataset: (dataset: PortalDataset) => void;
  tab: TeacherTab;
  setTab: (tab: TeacherTab) => void;
}) {
  const [selectedStudentId, setSelectedStudentId] = useState(props.dataset.students[0]?.id ?? "");

  const classStats = useMemo(
    () => computeClassStats(props.dataset.runSessions, props.dataset.mission.date),
    [props.dataset.runSessions, props.dataset.mission.date]
  );
  const activeGoal = props.dataset.worldGoals[0];
  const priorityReviews = props.dataset.recommendations.filter((item) => item.priority === "high").length;

  return (
    <div className="pageStack">
      <TeacherWorkspaceHeader
        teacherName={props.profile.displayName}
        className={props.dataset.activeClass.name}
        completedLearners={classStats.completed}
        learnerCount={props.dataset.students.length}
        practiceDuration={formatDuration(props.dataset.mission.missionDurationSeconds)}
        priorityReviews={priorityReviews}
        destination={activeGoal ? humanizeAreaId(activeGoal.targetAreaId) : "Not set"}
        onPlanLearning={() => props.setTab("mission")}
        onReviewLearners={() => props.setTab("reports")}
      />
      <div className="mobileWorkspaceNav">
        <SegmentedNav
          items={[
            ["overview", "Pulse", <Users size={17} />],
            ["classes", "Learners", <GraduationCap size={17} />],
            ["mission", "Plan", <CalendarDays size={17} />],
            ["reports", "Reports", <BarChart3 size={17} />],
            ["handwriting", "Evidence", <FileText size={17} />],
            ["assistant", "Assistant", <Sparkles size={17} />]
          ]}
          active={props.tab}
          onChange={(value) => props.setTab(value as TeacherTab)}
        />
      </div>
      {props.tab === "overview" && (
        <Overview
          mission={props.dataset.mission}
          worldGoals={props.dataset.worldGoals}
          worldGoalClaims={props.dataset.worldGoalClaims}
          classStats={classStats}
          students={props.dataset.students}
          runSessions={props.dataset.runSessions}
          spokenPhraseEvents={props.dataset.spokenPhraseEvents}
          writtenPhraseEvents={props.dataset.writtenPhraseEvents}
          grammarBattleEvents={props.dataset.grammarBattleEvents}
          buddyConversationTurns={props.dataset.buddyConversationTurns}
          gymAttempts={props.dataset.gymAttempts}
          recommendations={props.dataset.recommendations}
          setTab={props.setTab}
        />
      )}
      {props.tab === "classes" && (
        <ClassManager
          profile={props.profile}
          dataset={props.dataset}
          setDataset={props.setDataset}
          selectedStudentId={selectedStudentId}
          setSelectedStudentId={setSelectedStudentId}
          setTab={props.setTab}
        />
      )}
      {props.tab === "mission" && (
        <MissionPlanner
          profile={props.profile}
          dataset={props.dataset}
          setDataset={props.setDataset}
        />
      )}
      {props.tab === "reports" && (
        <Reports
          students={props.dataset.students}
          runSessions={props.dataset.runSessions}
          letterAttempts={props.dataset.letterAttempts}
          wordCasts={props.dataset.wordCasts}
          spokenPhraseEvents={props.dataset.spokenPhraseEvents}
          writtenPhraseEvents={props.dataset.writtenPhraseEvents}
          grammarBattleEvents={props.dataset.grammarBattleEvents}
          buddyConversationTurns={props.dataset.buddyConversationTurns}
          gymAttempts={props.dataset.gymAttempts}
          countingMiniGameAttempts={props.dataset.countingMiniGameAttempts}
          colorMiniGameAttempts={props.dataset.colorMiniGameAttempts}
          empathyEventAttempts={props.dataset.empathyEventAttempts}
          recommendations={props.dataset.recommendations}
          selectedStudentId={selectedStudentId}
          setSelectedStudentId={setSelectedStudentId}
        />
      )}
      {props.tab === "handwriting" && (
        <HandwritingReview
          students={props.dataset.students}
          letterAttempts={props.dataset.letterAttempts}
          templates={props.dataset.acceptedTemplates}
          wordCasts={props.dataset.wordCasts}
          selectedStudentId={selectedStudentId}
          setSelectedStudentId={setSelectedStudentId}
        />
      )}
      {props.tab === "assistant" && (
        <TeacherAssistant
          profile={props.profile}
          dataset={props.dataset}
        />
      )}
    </div>
  );
}
