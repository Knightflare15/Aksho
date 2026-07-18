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
import { buildDevelopmentReviewFlags, buildLearningTrajectory, buildLetterHeatmap, dateKey, effectivePronunciationInsight, firstPracticeSpeechSegment, hasPracticeSpeechSegment, humanizeDiagnosticTag, isPracticeSpeechSegment } from "../utils/portalAnalytics";
import { buildAcceptedAfterRetryWords, buildAcceptedVocabularyEvidence, buildErrorSourceBreakdown, buildGrammarMasterySummary, buildGrammarVocabularySummary, buildMostCommonErrorCategories, buildMostMissedConcepts, buildWorldGoalStudentProgress, masteryTagLabel, mergeVocabularyTokens, normalizeMasteryTags, normalizeVocabularyTokens } from "../utils/grammarReporting";
import { buildMiniGameOutcomeCounts, colorOutcomeStatus, countingOutcomeStatus, miniGameOutcomeLabel, type MiniGameOutcomeStatus } from "../utils/miniGameOutcomes";
import { joinOrFallback, shortDate, uniqueValue } from "../utils/portalTail";
import { ClockIcon, Header, LetterPicker, Metric, RecommendationList, SegmentedNav, SignalGroup, StrokePreview, StudentRow } from "../components/PortalUi";


export function HandwritingReview(props: {
  students: Student[];
  letterAttempts: LetterAttempt[];
  templates: AcceptedHandwritingTemplate[];
  wordCasts: WordCast[];
  selectedStudentId: string;
  setSelectedStudentId: (id: string) => void;
}) {
  const selectedStudent = props.students.find((student) => student.id === props.selectedStudentId) ?? props.students[0];
  const templates = props.templates.filter((template) => template.studentId === selectedStudent?.id);
  const attempts = props.letterAttempts.filter((attempt) => attempt.studentId === selectedStudent?.id);
  const wordCasts = props.wordCasts.filter((cast) => cast.studentId === selectedStudent?.id);
  const patterns = useMemo(
    () => summarizeDiagnostics(attempts, templates),
    [attempts, templates]
  );

  return (
    <div className="pageStack">
      <StudentRow students={props.students} selectedStudentId={props.selectedStudentId} onSelect={props.setSelectedStudentId} />
      <section className="panel">
        <div className="sectionHeader">
          <div>
            <h2>Handwriting Patterns</h2>
            <p>Teacher-only formation signals from recent attempts and accepted samples.</p>
          </div>
        </div>
        <div className="diagnosticGrid">
          {patterns.map((pattern) => (
            <article className="diagnosticCard" key={pattern.key} data-severity={pattern.severity}>
              <strong>{pattern.title}</strong>
              <span>{pattern.count} signal{pattern.count === 1 ? "" : "s"} across {pattern.letters}</span>
              <p>{pattern.hint}</p>
            </article>
          ))}
          {patterns.length === 0 && <p className="muted">No handwriting diagnostics uploaded for this student yet.</p>}
        </div>
      </section>
      <section className="panel">
        <div className="sectionHeader">
          <div>
            <h2>Pronunciation & Cast Phonetics</h2>
            <p>Accepted and rejected voice casts with expected and heard sound segments.</p>
          </div>
        </div>
        <div className="voiceCastGrid">
          {wordCasts.map((cast) => {
            const practiceSegment = firstPracticeSpeechSegment(cast);
            const insight = cast.serverPronunciationInsight ?? cast.pronunciationInsight;
            return (
              <article className="voiceCastCard" key={cast.id} data-success={cast.success ? "true" : "false"}>
                <div className="templateMeta">
                  <strong>{cast.word}</strong>
                  <span>{cast.success ? "accepted" : "needs retry"} - {cast.responseSeconds.toFixed(1)}s</span>
                </div>
                <p>
                  Heard: <b>{insight?.rawRecognizedText || "not captured"}</b>
                  {insight?.voskConfirmedWord ? " - Vosk accepted" : ""}
                  {cast.serverPronunciationInsight ? " - server reviewed" : ""}
                </p>
                <AnalysisStatus
                  mode={cast.analysisMode}
                  status={cast.serverAnalysisStatus}
                  provider={cast.onDeviceAnalysisProvider}
                />
                <WavLmPhonemeDiagnostics insight={insight} />
                <PhoneticSegments segments={insight?.segments ?? []} />
                {practiceSegment && (
                  <p className="phoneticFocus">
                    Practice {soundLabel(practiceSegment)}
                    {practiceSegment.heardSound
                      ? ` instead of ${practiceSegment.heardSound}`
                      : ""}
                  </p>
                )}
              </article>
            );
          })}
          {wordCasts.length === 0 && <p className="muted">No pronunciation cast events uploaded for this student yet.</p>}
        </div>
      </section>
      <section className="panel">
        <div className="sectionHeader">
          <div>
            <h2>Accepted Handwriting Samples</h2>
            <p>Teacher-only review of accepted student drawings. These samples are not added back into recognition templates.</p>
          </div>
        </div>
        <div className="templateGrid">
          {templates.map((template) => (
            <article className="templateCard" key={template.id}>
              <div className="templateMeta">
                <strong>{template.letter}</strong>
                <span>{template.targetWord} - {template.attemptsForLetter} attempt{template.attemptsForLetter === 1 ? "" : "s"}</span>
              </div>
              <StrokePreview points={template.points} />
              <p>
                Score {template.recognitionScore.toFixed(1)}
                {template.isAmbiguous ? " - review suggested" : " - accepted"}
              </p>
              <RecognitionBlendStatus
                neuralName={template.neuralRecognizedName ?? template.handwritingDiagnostics?.neuralRecognizedName}
                neuralConfidence={template.neuralConfidence ?? template.handwritingDiagnostics?.neuralConfidence}
                combinedConfidence={template.combinedConfidence ?? template.handwritingDiagnostics?.combinedConfidence}
                agreement={template.recognizerAgreement ?? template.handwritingDiagnostics?.recognizerAgreement}
                decision={template.recognitionDecision ?? template.handwritingDiagnostics?.recognitionDecision}
              />
              <AnalysisStatus
                mode={template.analysisMode}
                status={template.serverAnalysisStatus}
                provider={template.onDeviceAnalysisProvider}
              />
              <DiagnosticTags diagnostics={template.handwritingDiagnostics} />
            </article>
          ))}
          {templates.length === 0 && <p className="muted">No accepted samples uploaded for this student yet.</p>}
        </div>
      </section>
    </div>
  );
}

export function RecognitionBlendStatus(props: {
  neuralName?: string;
  neuralConfidence?: number;
  combinedConfidence?: number;
  agreement?: boolean;
  decision?: string;
}) {
  if (!props.neuralName && props.combinedConfidence === undefined) {
    return null;
  }

  const confidence = props.neuralConfidence === undefined ? "" : `NN ${(props.neuralConfidence * 100).toFixed(0)}%`;
  const combined = props.combinedConfidence === undefined ? "" : `Combined ${(props.combinedConfidence * 100).toFixed(0)}%`;
  const agreement = props.agreement === undefined ? "" : props.agreement ? "agreement" : "review";
  return (
    <p className="muted">
      {props.neuralName || "NN"} {confidence}{confidence && combined ? " - " : ""}{combined}
      {(confidence || combined) && agreement ? " - " : ""}{agreement}
      {props.decision ? ` - ${props.decision}` : ""}
    </p>
  );
}

export function AnalysisStatus(props: { mode?: string; status?: string; provider?: string }) {
  const mode = props.mode ?? "OnDeviceOnly";
  const status = props.status ?? "not_requested";
  const provider = props.provider || "on-device recognizer";
  const label = status === "pending"
    ? "Server analysis pending"
    : status === "complete"
      ? "Server analysis complete"
      : status === "failed"
        ? "Server analysis failed"
        : "On-device analysis only";

  return <p className="muted">{label} - {mode} - {provider}</p>;
}

export function WavLmPhonemeDiagnostics(props: { insight?: PronunciationInsight }) {
  const insight = props.insight;
  if (!insight) {
    return null;
  }

  const expected = insight.expectedPhonemes?.join(" ") ?? "";
  const observed = insight.observedPhonemes?.join(" ") ?? "";
  const confidence = typeof insight.modelConfidence === "number"
    ? `Model ${(insight.modelConfidence * 100).toFixed(0)}%`
    : "";

  if (!expected && !observed && !confidence) {
    return null;
  }

  return (
    <p className="phoneticFocus">
      {expected ? `Expected ${expected}` : ""}
      {expected && observed ? " - " : ""}
      {observed ? `Heard ${observed}` : ""}
      {(expected || observed) && confidence ? " - " : ""}
      {confidence}
    </p>
  );
}

export function PhoneticSegments(props: { segments: PhoneticSegment[] }) {
  if (props.segments.length === 0) {
    return <p className="muted">No phonetic segment detail captured.</p>;
  }

  return (
    <div className="phoneticSegments">
      {props.segments.map((segment, index) => (
        <span className="phoneticSegment" data-status={segment.status} key={`${segment.spelling}-${index}`}>
          <b>{soundLabel(segment)}</b>
          {segment.heardSound && segment.heardSound !== soundLabel(segment) ? <em>{segment.heardSound}</em> : null}
        </span>
      ))}
    </div>
  );
}

export function soundLabel(segment: PhoneticSegment): string {
  return segment.friendlySound || segment.spelling.toLowerCase();
}

export function DiagnosticTags(props: { diagnostics?: HandwritingDiagnosticSummary }) {
  const tags = props.diagnostics?.tags ?? [];
  if (tags.length === 0) {
    return null;
  }

  return (
    <div className="diagnosticTags">
      {tags.map((tag) => (
        <span className="diagnosticTag" key={tag}>{diagnosticLabel(tag)}</span>
      ))}
    </div>
  );
}

export function MasteryTags(props: { tags?: string[] }) {
  const tags = normalizeMasteryTags(props.tags);
  if (tags.length === 0) {
    return null;
  }

  return (
    <div className="diagnosticTags">
      {tags.map((tag) => (
        <span className="diagnosticTag" key={tag}>{masteryTagLabel(tag)}</span>
      ))}
    </div>
  );
}

export function VocabularyTokens(props: { tokens?: string[] }) {
  const tokens = normalizeVocabularyTokens(props.tokens).slice(0, 8);
  if (tokens.length === 0) {
    return null;
  }

  return (
    <div className="diagnosticTags">
      {tokens.map((token) => (
        <span className="diagnosticTag" key={token}>{token}</span>
      ))}
    </div>
  );
}

export function summarizeDiagnostics(
  attempts: LetterAttempt[],
  templates: AcceptedHandwritingTemplate[]
): Array<{ key: string; title: string; count: number; letters: string; hint: string; severity: number }> {
  const buckets = new Map<string, {
    count: number;
    letters: Set<string>;
    hint: string;
    severity: number;
  }>();

  const collect = (diagnostics?: HandwritingDiagnosticSummary) => {
    if (!diagnostics?.tags?.length) {
      return;
    }

    diagnostics.tags.forEach((tag) => {
      const existing = buckets.get(tag) ?? {
        count: 0,
        letters: new Set<string>(),
        hint: diagnostics.primaryHint || diagnosticLabel(tag),
        severity: 0
      };
      existing.count += 1;
      existing.letters.add(diagnostics.letter);
      if (diagnostics.primaryHint && diagnostics.severity >= existing.severity) {
        existing.hint = diagnostics.primaryHint;
      }
      existing.severity = Math.max(existing.severity, diagnostics.severity);
      buckets.set(tag, existing);
    });
  };

  attempts.forEach((attempt) => collect(attempt.handwritingDiagnostics));
  templates.forEach((template) => collect(template.handwritingDiagnostics));

  return Array.from(buckets.entries())
    .map(([key, value]) => ({
      key,
      title: diagnosticPatternTitle(key, value.letters),
      count: value.count,
      letters: Array.from(value.letters).sort().join(", "),
      hint: value.hint,
      severity: value.severity
    }))
    .sort((a, b) => b.severity - a.severity || b.count - a.count)
    .slice(0, 6);
}

export function diagnosticPatternTitle(tag: string, letters: Set<string>): string {
  const letterText = Array.from(letters).sort().join("/");
  switch (tag) {
    case "mirror":
      return `Mirrors ${letterText}`;
    case "reversedStroke":
      return `Reverses strokes in ${letterText}`;
    case "wrongStrokeOrder":
      return `Starts ${letterText} out of order`;
    case "oversized":
      return `Writes ${letterText} beyond the lines`;
    case "aboveBaseline":
    case "floating":
      return `Writes ${letterText} above baseline`;
    case "belowBaseline":
      return `Drops ${letterText} below baseline`;
    case "spacingDrift":
      return `Drifts across letter spaces`;
    case "extraStrokes":
    case "overdrawn":
      return `Retraces ${letterText} heavily`;
    case "wobbly":
      return `Local kinks in ${letterText}`;
    default:
      return diagnosticLabel(tag);
  }
}

export function diagnosticLabel(tag: string): string {
  const labels: Record<string, string> = {
    mirror: "Mirror",
    reversedStroke: "Reversed stroke",
    wrongStrokeOrder: "Stroke order",
    oversized: "Too tall",
    undersized: "Too small",
    aboveBaseline: "Above baseline",
    belowBaseline: "Below baseline",
    floating: "Floating",
    spacingDrift: "Spacing drift",
    extraStrokes: "Extra strokes",
    missingStroke: "Missing stroke",
    overdrawn: "Overdrawn",
    openLoop: "Open loop",
    closedTooSoon: "Closed too soon",
    wrongStart: "Wrong start",
    wrongDirection: "Wrong direction",
    repeatedCorrection: "Repeated corrections",
    slanted: "Slanted",
    wobbly: "Wobbly stroke"
  };
  return labels[tag] ?? tag;
}
