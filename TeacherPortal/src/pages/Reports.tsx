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
import { buildDevelopmentReviewFlags, buildLearningTrajectory, buildLetterHeatmap, dateKey, effectivePronunciationInsight, empathyOutcomeLabel, empathyOutcomeStatus, hasPracticeSpeechSegment, humanizeDiagnosticTag, isPracticeSpeechSegment } from "../utils/portalAnalytics";
import { buildAcceptedAfterRetryWords, buildAcceptedVocabularyEvidence, buildErrorSourceBreakdown, buildGrammarMasterySummary, buildGrammarVocabularySummary, buildMostCommonErrorCategories, buildMostMissedConcepts, buildWorldGoalStudentProgress, evidenceTime, masteryTagLabel, mergeVocabularyTokens, normalizeMasteryTags, titleCase } from "../utils/grammarReporting";
import { buildMiniGameOutcomeCounts, colorOutcomeStatus, countingOutcomeStatus, miniGameOutcomeLabel, type MiniGameOutcomeStatus } from "../utils/miniGameOutcomes";
import { joinOrFallback, shortDate, uniqueValue } from "../utils/portalTail";
import { ClockIcon, Header, LetterPicker, Metric, RecommendationList, SegmentedNav, SignalGroup, StrokePreview, StudentRow } from "../components/PortalUi";
import { AnalysisStatus, MasteryTags, PhoneticSegments, VocabularyTokens, WavLmPhonemeDiagnostics } from "./HandwritingReview";


export function Reports(props: {
  students: Student[];
  runSessions: RunSession[];
  letterAttempts: LetterAttempt[];
  wordCasts: WordCast[];
  spokenPhraseEvents: SpokenPhraseEvent[];
  writtenPhraseEvents: WrittenPhraseEvent[];
  grammarBattleEvents: GrammarBattleEvent[];
  buddyConversationTurns: BuddyConversationTurn[];
  gymAttempts: GymAttempt[];
  countingMiniGameAttempts: CountingMiniGameAttempt[];
  colorMiniGameAttempts: ColorMiniGameAttempt[];
  empathyEventAttempts: EmpathyEventAttempt[];
  recommendations: { id: string; priority: string; title: string; detail: string }[];
  selectedStudentId: string;
  setSelectedStudentId: (id: string) => void;
}) {
  const selectedStudent = props.students.find((student) => student.id === props.selectedStudentId) ?? props.students[0];
  const runs = props.runSessions.filter((run) => run.studentId === selectedStudent?.id);
  const latest = runs[0];
  const attempts = props.letterAttempts.filter((attempt) => attempt.studentId === selectedStudent?.id);
  const casts = props.wordCasts.filter((cast) => cast.studentId === selectedStudent?.id);
  const spokenPhrases = props.spokenPhraseEvents.filter((event) => event.studentId === selectedStudent?.id);
  const writtenPhrases = props.writtenPhraseEvents.filter((event) => event.studentId === selectedStudent?.id);
  const battleEvents = props.grammarBattleEvents.filter((event) => event.studentId === selectedStudent?.id);
  const buddyTurns = props.buddyConversationTurns.filter((turn) => turn.studentId === selectedStudent?.id);
  const gymAttempts = props.gymAttempts.filter((attempt) => attempt.studentId === selectedStudent?.id);
  const countingAttempts = props.countingMiniGameAttempts.filter((attempt) => attempt.studentId === selectedStudent?.id);
  const colorAttempts = props.colorMiniGameAttempts.filter((attempt) => attempt.studentId === selectedStudent?.id);
  const empathyEvents = props.empathyEventAttempts.filter((event) => event.studentId === selectedStudent?.id);
  const countingOutcomeCounts = buildMiniGameOutcomeCounts(countingAttempts.map(countingOutcomeStatus));
  const colorOutcomeCounts = buildMiniGameOutcomeCounts(colorAttempts.map(colorOutcomeStatus));
  const writingNeeds = attempts
    .filter((attempt) => !attempt.confident || attempt.attempts > 1 || (attempt.handwritingDiagnostics?.severity ?? 0) >= 2)
    .map((attempt) => attempt.letter)
    .filter(uniqueValue)
    .slice(0, 6);
  const writingStrengths = attempts
    .filter((attempt) => attempt.confident && attempt.attempts <= 1 && !writingNeeds.includes(attempt.letter))
    .map((attempt) => attempt.letter)
    .filter(uniqueValue)
    .slice(0, 6);
  const speakingNeeds = casts
    .filter((cast) => !cast.success || hasPracticeSpeechSegment(cast))
    .map((cast) => cast.word)
    .filter(uniqueValue)
    .slice(0, 6);
  const speakingStrengths = casts
    .filter((cast) => cast.success && !hasPracticeSpeechSegment(cast) && !speakingNeeds.includes(cast.word))
    .map((cast) => cast.word)
    .filter(uniqueValue)
    .slice(0, 6);
  const trajectory = buildLearningTrajectory(runs);
  const reviewFlags = buildDevelopmentReviewFlags(attempts, casts, countingAttempts, colorAttempts, empathyEvents);
  const letterHeatmap = buildLetterHeatmap(attempts, latest?.lettersPracticed ?? []);
  const masterySummary = buildGrammarMasterySummary(spokenPhrases, writtenPhrases, battleEvents, gymAttempts);
  const vocabularySummary = buildGrammarVocabularySummary(spokenPhrases, writtenPhrases, battleEvents);
  const acceptedVocabularyEvidence = buildAcceptedVocabularyEvidence(spokenPhrases, writtenPhrases, battleEvents);
  const mostMissedConcepts = buildMostMissedConcepts(spokenPhrases, writtenPhrases, battleEvents);
  const commonErrorCategories = buildMostCommonErrorCategories(spokenPhrases, writtenPhrases, battleEvents);
  const acceptedAfterRetryWords = buildAcceptedAfterRetryWords(spokenPhrases, writtenPhrases, battleEvents);
  const errorSourceBreakdown = buildErrorSourceBreakdown(spokenPhrases, writtenPhrases, battleEvents);
  const acceptedSpokenWords = mergeVocabularyTokens(
    acceptedVocabularyEvidence.filter((item) => item.spoken > 0).map((item) => item.word),
    latest?.acceptedSpokenVocabulary
  ).slice(0, 10);
  const acceptedWrittenWords = mergeVocabularyTokens(
    acceptedVocabularyEvidence.filter((item) => item.written > 0).map((item) => item.word),
    latest?.acceptedWrittenVocabulary
  ).slice(0, 10);
  const acceptedBattleWords = mergeVocabularyTokens(
    acceptedVocabularyEvidence.filter((item) => item.battle > 0).map((item) => item.word),
    latest?.acceptedBattleVocabulary
  ).slice(0, 10);
  const latestGrammarEvidence = [...spokenPhrases, ...writtenPhrases, ...battleEvents, ...buddyTurns, ...gymAttempts]
    .sort((a, b) => evidenceTime(b) - evidenceTime(a))[0];

  return (
    <div className="pageStack">
      <section className="panel plannerGrid">
        <label>
          Student
          <select value={props.selectedStudentId} onChange={(event) => props.setSelectedStudentId(event.target.value)}>
            {props.students.map((student) => <option key={student.id} value={student.id}>{student.name}</option>)}
          </select>
        </label>
        <Metric label="Confidence" value={formatConfidencePercent(latest?.averageConfidence)} />
        <Metric label="Attempts" value={(latest?.averageAttemptsPerLetter ?? 0).toFixed(1)} />
        <Metric label="Areas" value={String(latest?.subarenasCleared ?? 0)} />
        <Metric label="Spoken phrases" value={String(latest?.spokenPhraseCount ?? spokenPhrases.length)} />
        <Metric label="Written phrases" value={String(latest?.writtenPhraseCount ?? writtenPhrases.length)} />
        <Metric label="Battle commands" value={String(latest?.grammarBattleCount ?? battleEvents.length)} />
      </section>
      <StudentRow students={props.students} selectedStudentId={props.selectedStudentId} onSelect={props.setSelectedStudentId} />
      <section className="panel">
        <div className="sectionHeader">
          <div>
            <h2>Learning Trajectory</h2>
            <p>Recent changes across confidence, attempts, completion, writing signals, and speech sound attempts.</p>
          </div>
        </div>
        <div className="trajectoryGrid">
          {trajectory.map((item) => (
            <article className="trendCard" data-state={item.state} key={item.title}>
              <span>{item.label}</span>
              <strong>{item.title}</strong>
              <p>{item.detail}</p>
            </article>
          ))}
        </div>
      </section>
      <section className="panel">
        <div className="sectionHeader">
          <div>
            <h2>Development Review Signals</h2>
            <p>Patterns that may merit teacher review, parent conversation, or specialist screening if they persist.</p>
          </div>
        </div>
        <div className="reviewFlagGrid">
          {reviewFlags.map((flag) => (
            <article className="reviewFlag" data-level={flag.level} key={flag.title}>
              <strong>{flag.title}</strong>
              <span>{flag.evidence}</span>
              <p>{flag.detail}</p>
            </article>
          ))}
        </div>
      </section>
      <section className="panel">
        <h2>Weak-Letter Heatmap</h2>
        <div className="heatmap">
          {letterHeatmap.length === 0 && <p className="muted">No uploaded letter evidence for this student yet.</p>}
          {letterHeatmap.map((item) => (
            <div className="heatCell" data-level={item.level} key={item.letter}>
              <strong>{item.letter}</strong>
              <span>{item.label}</span>
            </div>
          ))}
        </div>
      </section>
      <section className="panel">
        <div className="sectionHeader">
          <div>
            <h2>{selectedStudent?.name ?? "Student"} Profile</h2>
            <p>Writing and speaking signals from recent runs.</p>
          </div>
        </div>
        <div className="profileSignalGrid">
          <SignalGroup title="Writing strengths" values={writingStrengths} kind="letter" />
          <SignalGroup title="Writing practice" values={writingNeeds} kind="letter" />
          <SignalGroup title="Speaking strengths" values={speakingStrengths} kind="word" />
          <SignalGroup title="Speaking practice" values={speakingNeeds} kind="word" />
        </div>
      </section>
      <section className="panel">
        <div className="sectionHeader">
          <div>
            <h2>Grammar RPG Evidence</h2>
            <p>Accepted phrases, rejected grammar forms, battle commands, and gym attempts.</p>
          </div>
        </div>
        <div className="metricGrid">
          <Metric label="Spoken accepted" value={String(spokenPhrases.filter((event) => event.accepted).length)} />
          <Metric label="Written accepted" value={String(writtenPhrases.filter((event) => event.accepted).length)} />
          <Metric label="Battle commands" value={String(battleEvents.filter((event) => event.accepted).length)} />
          <Metric label="Buddy coaching" value={String(buddyTurns.filter((turn) => turn.reportable !== false).length)} />
          <Metric label="Gym clears" value={String(gymAttempts.filter((attempt) => attempt.passed).length)} />
          <Metric label="Latest area" value={latestGrammarEvidence?.areaId ? humanizeAreaId(latestGrammarEvidence.areaId) : "No evidence"} />
          <Metric label="Buddy level" value={humanizeBuddyAssistMode(latestGrammarEvidence?.buddyAssistMode)} />
          <Metric label="Pronunciation issues" value={String(errorSourceBreakdown.pronunciation)} />
          <Metric label="Handwriting issues" value={String(errorSourceBreakdown.handwriting)} />
          <Metric label="Grammar structure issues" value={String(errorSourceBreakdown.grammar)} />
        </div>
        <div className="profileSignalGrid">
          <SignalGroup title="Mastery evidence" values={masterySummary.mastered} kind="word" />
          <SignalGroup title="Needs practice" values={masterySummary.needsPractice} kind="word" />
          <SignalGroup title="Most-missed concepts" values={mostMissedConcepts} kind="word" />
          <SignalGroup title="Common error categories" values={commonErrorCategories} kind="word" />
          <SignalGroup title="Accepted after retry" values={acceptedAfterRetryWords} kind="word" />
          <SignalGroup title="Accepted spoken words" values={acceptedSpokenWords} kind="word" />
          <SignalGroup title="Accepted written words" values={acceptedWrittenWords} kind="word" />
          <SignalGroup title="Accepted battle words" values={acceptedBattleWords} kind="word" />
          <SignalGroup title="Vocabulary to revisit" values={vocabularySummary.needsPractice} kind="word" />
          <SignalGroup title="Session patterns" values={(latest?.grammarPatternsPracticed ?? []).map(humanizePattern).slice(0, 8)} kind="word" />
          <SignalGroup title="Session mastery" values={(latest?.masteryTagsPracticed ?? []).map(masteryTagLabel).slice(0, 8)} kind="word" />
          <SignalGroup title="Session vocabulary" values={(latest?.vocabularyTokens ?? latest?.wordsPracticed ?? []).slice(0, 10)} kind="word" />
        </div>
        <div className="voiceCastGrid">
          {acceptedVocabularyEvidence.slice(0, 8).map((item) => (
            <article className="voiceCastCard" key={item.word} data-success="true">
              <div className="templateMeta">
                <strong>{item.word}</strong>
                <span>{item.total} accepted use{item.total === 1 ? "" : "s"}</span>
              </div>
              <p>
                Spoken {item.spoken}, written {item.written}, battle {item.battle}.
              </p>
              {item.patterns.length > 0 && (
                <div className="tokenRow">
                  {item.patterns.map((pattern) => <span className="wordToken" key={pattern}>{humanizePattern(pattern)}</span>)}
                </div>
              )}
            </article>
          ))}
        </div>
        <div className="voiceCastGrid">
          {[...spokenPhrases, ...writtenPhrases].slice(0, 8).map((event) => {
            const insight = "pronunciationInsight" in event
              ? event.pronunciationInsight as PronunciationInsight | undefined
              : undefined;
            return (
            <article className="voiceCastCard" key={`${event.id}-${event.phrase}`} data-success={event.accepted ? "true" : "false"}>
              <div className="templateMeta">
                <strong>{event.phrase}</strong>
                <span>{humanizePattern(event.grammarPattern)} - {event.zoneKind} - {humanizeBuddyAssistMode(event.buddyAssistMode)}</span>
              </div>
              {(event.targetPhrase || event.submittedPhrase) && event.targetPhrase !== event.submittedPhrase && (
                <p className="muted">
                  Target: {event.targetPhrase || event.phrase} | Supplied: {event.submittedPhrase || event.phrase}
                </p>
              )}
              <p>{event.accepted ? "Accepted" : `Rejected: ${humanizeReason(event.rejectionReason)}`}</p>
              {(event.conceptId || event.errorCategory) && (
                <p className="muted">
                  {event.conceptId ? `${humanizeConceptId(event.conceptId)}` : "Grammar"}
                  {event.errorCategory ? ` | ${humanizeReason(event.errorCategory)}` : ""}
                </p>
              )}
              <VocabularyTokens tokens={event.vocabularyTokens} />
              <MasteryTags tags={event.masteryTags} />
              <WavLmPhonemeDiagnostics insight={insight} />
              <PhoneticSegments segments={insight?.segments ?? []} />
            </article>
            );
          })}
          {battleEvents.slice(0, 4).map((event) => (
            <article className="voiceCastCard" key={event.id} data-success={event.accepted ? "true" : "false"}>
              <div className="templateMeta">
                <strong>{event.playerPhrase}</strong>
                <span>{humanizePattern(event.grammarPattern)} - curse {event.activeCurse} - {humanizeBuddyAssistMode(event.buddyAssistMode)}</span>
              </div>
              <p>{event.outcome} with {event.actionVerb || "command"}.</p>
              {(event.commandPreposition || event.commandConjunction) && (
                <p className="muted">
                  {event.commandPreposition ? `Preposition: ${event.commandPreposition}` : "No tactical preposition"}
                  {event.commandConjunction ? ` | Conjunction: ${event.commandConjunction}` : ""}
                </p>
              )}
              {(event.conceptId || event.errorCategory) && (
                <p className="muted">
                  {event.conceptId ? `${humanizeConceptId(event.conceptId)}` : "Grammar"}
                  {event.errorCategory ? ` | ${humanizeReason(event.errorCategory)}` : ""}
                </p>
              )}
              {(event.enemyGrammarCommand || event.enemyActionVerb || event.enemyNounFamily) && (
                <p className="muted">
                  Enemy pressure: {event.enemyGrammarCommand || [event.enemyNounFamily, event.enemyActionVerb].filter(Boolean).join(" ")}
                  {event.enemyGrammarPattern ? ` (${humanizePattern(event.enemyGrammarPattern)})` : ""}
              </p>
              )}
              <VocabularyTokens tokens={event.vocabularyTokens} />
              <MasteryTags tags={event.masteryTags} />
              <WavLmPhonemeDiagnostics insight={event.pronunciationInsight} />
              <PhoneticSegments segments={event.pronunciationInsight?.segments ?? []} />
            </article>
          ))}
          {buddyTurns.filter((turn) => turn.reportable !== false).slice(0, 4).map((turn) => (
            <article className="voiceCastCard" key={turn.id} data-success={turn.errorCategory ? "false" : "true"}>
              <div className="templateMeta">
                <strong>{turn.learnerMessage}</strong>
                <span>
                  Buddy conversation - {humanizeBuddyAssistMode(turn.buddyAssistMode)}
                  {typeof turn.englishRatio === "number" ? ` - ${Math.round(turn.englishRatio * 100)}% English` : ""}
                </span>
              </div>
              <p>{turn.buddyResponse}</p>
              {(turn.wordChoiceIssue || turn.formationIssue || turn.correctedResponse) && (
                <p className="muted">
                  {turn.wordChoiceIssue ? `Word choice: ${humanizeReason(turn.wordChoiceIssue)}` : ""}
                  {turn.formationIssue ? `${turn.wordChoiceIssue ? " | " : ""}Formation: ${humanizeReason(turn.formationIssue)}` : ""}
                  {turn.correctedResponse ? ` | Try: ${turn.correctedResponse}` : ""}
                </p>
              )}
              {(turn.conceptId || turn.errorCategory || turn.conversationSkill) && (
                <p className="muted">
                  {turn.conceptId ? `${humanizeConceptId(turn.conceptId)}` : "Conversation"}
                  {turn.errorCategory ? ` | ${humanizeReason(turn.errorCategory)}` : ""}
                  {turn.conversationSkill ? ` | ${humanizeReason(turn.conversationSkill)}` : ""}
                </p>
              )}
              {(turn.provider || turn.trigger || turn.buddyStatus) && (
                <p className="muted">
                  {turn.provider ? `Provider: ${turn.provider}` : "Provider: deterministic"}
                  {turn.model ? ` (${turn.model})` : ""}
                  {turn.trigger ? ` | Trigger: ${humanizeReason(turn.trigger)}` : ""}
                  {turn.buddyStatus ? ` | ${humanizeReason(turn.buddyStatus)}` : ""}
                  {turn.buddyFallbackReason ? `: ${humanizeReason(turn.buddyFallbackReason)}` : ""}
                </p>
              )}
              {turn.teacherNote && <p className="muted">{turn.teacherNote}</p>}
              <VocabularyTokens tokens={turn.vocabularyTokens} />
              <MasteryTags tags={turn.masteryTags} />
            </article>
          ))}
          {gymAttempts.slice(0, 4).map((attempt) => (
            <article className="voiceCastCard" key={attempt.id} data-success={attempt.passed ? "true" : "false"}>
              <div className="templateMeta">
                <strong>{humanizeAreaId(attempt.gymId)}</strong>
                <span>{attempt.passed ? "gym cleared" : "gym attempt"} - {humanizeBuddyAssistMode(attempt.buddyAssistMode)}</span>
              </div>
              <p>
                {attempt.spokenPhraseCount} spoken, {attempt.writtenPhraseCount} written, {attempt.grammarErrors} grammar issue{attempt.grammarErrors === 1 ? "" : "s"}.
              </p>
              <MasteryTags tags={attempt.masteryTags} />
            </article>
          ))}
          {spokenPhrases.length + writtenPhrases.length + battleEvents.length + buddyTurns.length + gymAttempts.length === 0 && (
            <p className="muted">No grammar RPG phrase evidence uploaded for this student yet.</p>
          )}
        </div>
      </section>
      <section className="panel">
        <div className="sectionHeader">
          <div>
            <h2>Counting Chest Evidence</h2>
            <p>Treasure chest counting and spoken-number proof from recent runs.</p>
          </div>
        </div>
        <div className="metricGrid">
          <Metric label="Seen ignored" value={String(countingOutcomeCounts.seen_ignored)} />
          <Metric label="Wrong answer" value={String(countingOutcomeCounts.opened_wrong_answer)} />
          <Metric label="Correct + speech fail" value={String(countingOutcomeCounts.opened_correct_pronunciation_failed)} />
          <Metric label="Correct" value={String(countingOutcomeCounts.opened_correct)} />
        </div>
        <div className="voiceCastGrid">
          {countingAttempts.slice(0, 6).map((attempt) => {
            const outcome = countingOutcomeStatus(attempt);
            const insight = attempt.serverPronunciationInsight ?? attempt.pronunciationInsight;
            return (
            <article className="voiceCastCard" key={attempt.id} data-success={outcome === "opened_correct" ? "true" : "false"}>
              <div className="templateMeta">
                <strong>{attempt.targetCount} coin{attempt.targetCount === 1 ? "" : "s"}</strong>
                <span>{miniGameOutcomeLabel(outcome)}</span>
              </div>
              <p>
                {outcome === "seen_ignored"
                  ? "Child entered range but did not open the chest."
                  : `Selected ${attempt.selectedCount}; said ${attempt.spokenNumber || "not captured"}.`}
                {attempt.hintUsed ? " Hint used." : ""}
              </p>
              <AnalysisStatus
                mode={attempt.analysisMode}
                status={attempt.serverAnalysisStatus}
                provider={attempt.onDeviceAnalysisProvider}
              />
              <WavLmPhonemeDiagnostics insight={insight} />
              <PhoneticSegments segments={insight?.segments ?? []} />
            </article>
            );
          })}
          {countingAttempts.length === 0 && <p className="muted">No counting chest attempts uploaded for this student yet.</p>}
        </div>
      </section>
      <section className="panel">
        <div className="sectionHeader">
          <div>
            <h2>Color Chest Evidence</h2>
            <p>Treasure chest color matching and spoken-color proof from recent runs.</p>
          </div>
        </div>
        <div className="metricGrid">
          <Metric label="Seen ignored" value={String(colorOutcomeCounts.seen_ignored)} />
          <Metric label="Wrong answer" value={String(colorOutcomeCounts.opened_wrong_answer)} />
          <Metric label="Correct + speech fail" value={String(colorOutcomeCounts.opened_correct_pronunciation_failed)} />
          <Metric label="Correct" value={String(colorOutcomeCounts.opened_correct)} />
        </div>
        <div className="voiceCastGrid">
          {colorAttempts.slice(0, 6).map((attempt) => {
            const outcome = colorOutcomeStatus(attempt);
            const insight = attempt.serverPronunciationInsight ?? attempt.pronunciationInsight;
            return (
            <article className="voiceCastCard" key={attempt.id} data-success={outcome === "opened_correct" ? "true" : "false"}>
              <div className="templateMeta">
                <strong>{attempt.targetColor ? titleCase(attempt.targetColor) : "Color chest"}</strong>
                <span>{miniGameOutcomeLabel(outcome)}</span>
              </div>
              <p>
                {outcome === "seen_ignored"
                  ? "Child entered range but did not open the chest."
                  : `Selected ${titleCase(attempt.selectedColor)}; said ${attempt.spokenColor || "not captured"}.`}
                {attempt.hintUsed ? " Hint used." : ""}
              </p>
              <AnalysisStatus
                mode={attempt.analysisMode}
                status={attempt.serverAnalysisStatus}
                provider={attempt.onDeviceAnalysisProvider}
              />
              <WavLmPhonemeDiagnostics insight={insight} />
              <PhoneticSegments segments={insight?.segments ?? []} />
            </article>
            );
          })}
          {colorAttempts.length === 0 && <p className="muted">No color chest attempts uploaded for this student yet.</p>}
        </div>
      </section>
      <section className="panel">
        <div className="sectionHeader">
          <div>
            <h2>Empathy Event Evidence</h2>
            <p>Social-emotional prompt engagement, choices, and reflection follow-through.</p>
          </div>
        </div>
        <div className="metricGrid">
          <Metric label="Seen ignored" value={String(empathyEvents.filter((event) => empathyOutcomeStatus(event) === "seen_ignored").length)} />
          <Metric label="Needs support" value={String(empathyEvents.filter((event) => empathyOutcomeStatus(event) === "needs_support").length)} />
          <Metric label="Reflection needed" value={String(empathyEvents.filter((event) => empathyOutcomeStatus(event) === "supportive_choice_reflection_needed").length)} />
          <Metric label="Supportive" value={String(empathyEvents.filter((event) => empathyOutcomeStatus(event) === "supportive_choice").length)} />
        </div>
        <div className="voiceCastGrid">
          {empathyEvents.slice(0, 6).map((event) => {
            const outcome = empathyOutcomeStatus(event);
            return (
              <article className="voiceCastCard" key={event.id} data-success={outcome === "supportive_choice" ? "true" : "false"}>
                <div className="templateMeta">
                  <strong>{titleCase(event.empathySkill || event.eventCategory || "Empathy event")}</strong>
                  <span>{empathyOutcomeLabel(outcome)}</span>
                </div>
                <p>
                  {event.prompt || "Prompt not captured."}
                  {event.selectedResponse ? ` Selected: ${event.selectedResponse}.` : ""}
                </p>
              </article>
            );
          })}
          {empathyEvents.length === 0 && <p className="muted">No empathy events uploaded for this student yet.</p>}
        </div>
      </section>
      <section className="panel">
        <h2>Recommendations</h2>
        <RecommendationList recommendations={props.recommendations} />
      </section>
    </div>
  );
}
