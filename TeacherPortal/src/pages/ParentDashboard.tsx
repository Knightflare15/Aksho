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


export function ParentDashboard(props: { dataset: PortalDataset }) {
  const [selectedStudentId, setSelectedStudentId] = useState(props.dataset.students[0]?.id ?? "");
  const fallbackStudent = props.dataset.students.find(student => student.id === selectedStudentId) ?? props.dataset.students[0];
  const summary = props.dataset.parentSummaries.find(item => item.studentId === fallbackStudent?.id);
  const [privacyDraft, setPrivacyDraft] = useState<StudentPrivacyDraft>({
    gameplayAnalyticsAllowed: true,
    buddyAllowed: true,
    audioProcessingAllowed: true,
    handwritingEvidenceAllowed: false,
    diagnosticsAllowed: true
  });
  const [privacyStatus, setPrivacyStatus] = useState("Loading privacy choices...");
  const [deletionPending, setDeletionPending] = useState(false);

  useEffect(() => {
    if (!fallbackStudent) return;
    setDeletionPending(false);
    getStudentPrivacyStatus({ schoolId: fallbackStudent.schoolId, studentId: fallbackStudent.id })
      .then(result => {
        const privacy = result.privacy;
        const pendingDeletion = result.deletionStatus === "pending" || result.deletionStatus === "processing";
        setDeletionPending(pendingDeletion);
        setPrivacyDraft({
          gameplayAnalyticsAllowed: privacy.gameplayAnalyticsAllowed,
          buddyAllowed: privacy.buddyAllowed,
          audioProcessingAllowed: privacy.audioProcessingAllowed,
          handwritingEvidenceAllowed: privacy.handwritingEvidenceAllowed,
          diagnosticsAllowed: privacy.diagnosticsAllowed
        });
        setPrivacyStatus(pendingDeletion
          ? `Deletion is scheduled${result.deleteAfterUtc ? ` for ${new Date(result.deleteAfterUtc).toLocaleString()}` : ""}. You can cancel while it is pending.`
          : result.requiresRenewal
          ? "The privacy policy changed. Please review and renew permission."
          : privacy.consentStatus === "granted" ? "Permission is active." : "Sensitive features are currently off.");
      })
      .catch(error => setPrivacyStatus(error instanceof Error ? error.message : "Privacy choices could not be loaded."));
  }, [fallbackStudent?.id, fallbackStudent?.schoolId]);

  if (!summary && !fallbackStudent) {
    return (
      <section className="panel">
        <h2>No linked student</h2>
        <p className="muted">Redeem a parent access code to link a child summary.</p>
      </section>
    );
  }

  const displaySummary = summary ?? {
    id: `${fallbackStudent.id}_fallback`,
    schoolId: fallbackStudent.schoolId,
    studentId: fallbackStudent.id,
    classId: fallbackStudent.classId,
    studentName: fallbackStudent.name,
    weekStart: new Date().toISOString().slice(0, 10),
    minutesPracticed: 0,
    lettersPracticed: [],
    wordsPracticed: [],
    bestLetter: "A",
    needsPracticeLetter: "C",
    averageConfidence: 0,
    averageAttemptsPerLetter: 0,
    trendLabel: "Waiting for first practice session",
    updatedAt: new Date().toISOString()
  };

  const updatePrivacyChoice = (key: keyof StudentPrivacyDraft, value: boolean) => {
    setPrivacyDraft(current => ({ ...current, [key]: value }));
  };

  const savePrivacy = async (granted: boolean) => {
    if (!fallbackStudent) return;
    setPrivacyStatus(granted ? "Saving permission..." : "Withdrawing permission...");
    try {
      const result = await setStudentPrivacyConsent({
        schoolId: fallbackStudent.schoolId,
        studentId: fallbackStudent.id,
        granted,
        ...privacyDraft
      });
      setPrivacyStatus(result.privacy.consentStatus === "granted"
        ? "Permission saved. Only the selected features are enabled."
        : "Permission withdrawn. Buddy, cloud audio, handwriting evidence, and diagnostics are off.");
    } catch (error) {
      setPrivacyStatus(error instanceof Error ? error.message : "Privacy choices could not be saved.");
    }
  };

  const requestDeletion = async () => {
    if (!fallbackStudent || !window.confirm(`Schedule deletion of ${fallbackStudent.name}'s account and learner data? You will have a grace period to cancel.`)) return;
    setPrivacyStatus("Scheduling deletion...");
    try {
      const result = await requestStudentDataDeletion({ schoolId: fallbackStudent.schoolId, studentId: fallbackStudent.id });
      setDeletionPending(true);
      setPrivacyStatus(`Deletion is scheduled for ${new Date(result.deleteAfterUtc).toLocaleString()}. You can cancel before then.`);
    } catch (error) {
      setPrivacyStatus(error instanceof Error ? error.message : "Deletion could not be scheduled.");
    }
  };

  const cancelDeletion = async () => {
    if (!fallbackStudent) return;
    try {
      await cancelStudentDataDeletion({ schoolId: fallbackStudent.schoolId, studentId: fallbackStudent.id });
      setDeletionPending(false);
      setPrivacyStatus("Deletion cancelled. The account remains active.");
    } catch (error) {
      setPrivacyStatus(error instanceof Error ? error.message : "Deletion could not be cancelled.");
    }
  };

  return (
    <div className="pageStack parentWidth">
      <Header title="Parent Summary" subtitle={`Weekly progress for ${displaySummary.studentName}`} />
      {props.dataset.students.length > 1 && (
        <section className="panel">
          <label>
            Child
            <select value={fallbackStudent?.id ?? ""} onChange={event => setSelectedStudentId(event.target.value)}>
              {props.dataset.students.map(student => <option key={student.id} value={student.id}>{student.name}</option>)}
            </select>
          </label>
        </section>
      )}
      <section className="panel parentPanel">
        <div className="sectionHeader">
          <div>
            <h2>{displaySummary.studentName}'s Practice</h2>
            <p>{displaySummary.minutesPracticed} minutes practiced this week</p>
          </div>
          <CheckCircle2 size={30} />
        </div>
        <p className="parentCopy">
          {displaySummary.studentName} practiced {joinOrFallback(displaySummary.lettersPracticed, "new letters")}.
          {displaySummary.bestLetter} is becoming strong. {displaySummary.needsPracticeLetter} needs more practice.
          Current trend: {displaySummary.trendLabel}.
        </p>
        <div className="metricGrid parentMetrics">
          <Metric label="Confidence" value={formatConfidencePercent(displaySummary.averageConfidence)} />
          <Metric label="Attempts" value={displaySummary.averageAttemptsPerLetter.toFixed(1)} />
        </div>
        <div className="tokenRow">
          {displaySummary.wordsPracticed.map((word) => <span className="wordToken" key={word}>{word}</span>)}
        </div>
      </section>
      <section className="panel parentPanel">
        <div className="sectionHeader">
          <div>
            <h2>Privacy & Parent Permission</h2>
            <p>Choose which optional cloud features may process {displaySummary.studentName}'s learning data.</p>
          </div>
          <ShieldCheck size={30} />
        </div>
        <div className="privacyChoiceGrid">
          {([
            ["gameplayAnalyticsAllowed", "Learning progress", "Stores attempts and progress so lessons can adapt."],
            ["buddyAllowed", "AI Buddy", "Sends the current task and a short, redacted learner message to the Buddy service."],
            ["audioProcessingAllowed", "Cloud pronunciation", "Uploads a short WAV for assessment, then deletes it immediately."],
            ["handwritingEvidenceAllowed", "Handwriting evidence", "Retains bounded stroke points for review; otherwise only derived scores are kept."],
            ["diagnosticsAllowed", "Crash diagnostics", "Sends redacted error details with a strict daily limit; never sends audio."]
          ] as Array<[keyof StudentPrivacyDraft, string, string]>).map(([key, label, description]) => (
            <label className="privacyChoice" key={key}>
              <input type="checkbox" checked={privacyDraft[key]} onChange={event => updatePrivacyChoice(key, event.target.checked)} />
              <span><strong>{label}</strong><small>{description}</small></span>
            </label>
          ))}
        </div>
        <p className="muted">{privacyStatus}</p>
        <p className="muted"><a href="/privacy.html" target="_blank" rel="noreferrer">Privacy policy</a> · <a href="/child-safety.html" target="_blank" rel="noreferrer">Child-safety policy</a></p>
        <div className="buttonRow">
          <button className="primaryButton" onClick={() => savePrivacy(true)}>Save selected permissions</button>
          <button className="secondaryButton" onClick={() => savePrivacy(false)}>Withdraw optional permissions</button>
        </div>
        <div className="privacyDangerZone">
          <strong>Account deletion</strong>
          <p className="muted">Deletion removes the learner account, progress, conversations, handwriting records, diagnostics, and stored files after a cancellable grace period.</p>
          {deletionPending
            ? <button className="secondaryButton" onClick={cancelDeletion}>Cancel scheduled deletion</button>
            : <button className="dangerButton" onClick={requestDeletion}>Schedule account deletion</button>}
        </div>
      </section>
    </div>
  );
}
