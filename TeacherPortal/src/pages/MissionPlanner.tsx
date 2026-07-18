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

import { addDays, alphabet, buildStudentOverride, buildWorldGoalId, durationPresets, findWorldGoalDraft, grammarRegionOptions, splitCsvValues, splitWords } from "../utils/missionPlanning";
import { average, computeClassStats, formatConfidencePercent, formatDuration, formatGoalClaim, humanizeAreaId, humanizeBuddyAssistMode, humanizeConceptId, humanizePattern, humanizeReason, missionTypeLabel, normalizeConfidence, runDateKey, runTime } from "../utils/portalCore";
import { buildDevelopmentReviewFlags, buildLearningTrajectory, buildLetterHeatmap, dateKey, effectivePronunciationInsight, hasPracticeSpeechSegment, humanizeDiagnosticTag, isPracticeSpeechSegment } from "../utils/portalAnalytics";
import { buildAcceptedAfterRetryWords, buildAcceptedVocabularyEvidence, buildErrorSourceBreakdown, buildGrammarMasterySummary, buildGrammarVocabularySummary, buildMostCommonErrorCategories, buildMostMissedConcepts, buildWorldGoalStudentProgress, clampChestCount, masteryTagLabel, mergeVocabularyTokens, normalizeMasteryTags } from "../utils/grammarReporting";
import { buildMiniGameOutcomeCounts, colorOutcomeStatus, countingOutcomeStatus, miniGameOutcomeLabel, type MiniGameOutcomeStatus } from "../utils/miniGameOutcomes";
import { joinOrFallback, shortDate, uniqueValue } from "../utils/portalTail";
import { ClockIcon, Header, LetterPicker, Metric, RecommendationList, SegmentedNav, SignalGroup, StrokePreview, StudentRow } from "../components/PortalUi";


export function MissionPlanner(props: {
  profile: UserProfile;
  dataset: PortalDataset;
  setDataset: (dataset: PortalDataset) => void;
}) {
  const [saveState, setSaveState] = useState("");
  const [selectedClassId, setSelectedClassId] = useState(props.dataset.activeClass.id);
  const [selectedDate, setSelectedDate] = useState(props.dataset.mission.date);
  const [mission, setMission] = useState(props.dataset.mission);
  const [worldGoal, setWorldGoal] = useState(() => findWorldGoalDraft(
    props.dataset.worldGoals,
    props.profile.schoolId,
    selectedClassId,
    selectedDate,
    props.profile.uid
  ));
  const classStudents = props.dataset.students.filter((student) => student.classId === selectedClassId);
  const [selectedStudentId, setSelectedStudentId] = useState(classStudents[0]?.id ?? "");
  const selectedStudent = classStudents.find((student) => student.id === selectedStudentId) ?? classStudents[0];
  const existingOverride = props.dataset.studentMissionOverrides.find((override) => (
    override.classId === selectedClassId &&
    override.date === selectedDate &&
    override.studentId === selectedStudent?.id
  ));
  const [overrideDraft, setOverrideDraft] = useState<StudentMissionOverride | null>(null);
  const weekDates = useMemo(() => Array.from({ length: 7 }, (_, index) => addDays(selectedDate, index)), [selectedDate]);

  useEffect(() => {
    let alive = true;
    setSaveState("Loading class plan...");
    Promise.all([
      loadDailyMission({ schoolId: props.profile.schoolId, classId: selectedClassId, date: selectedDate }),
      loadWorldGoal({
        schoolId: props.profile.schoolId,
        classId: selectedClassId,
        weekStart: selectedDate,
        teacherId: props.profile.uid
      })
    ])
      .then(([loadedMission, loadedGoal]) => {
        if (!alive) {
          return;
        }
        setMission({ ...loadedMission, id: selectedDate, date: selectedDate, classId: selectedClassId });
        setWorldGoal({
          ...loadedGoal,
          goalId: loadedGoal.goalId || buildWorldGoalId(selectedClassId, selectedDate),
          schoolId: props.profile.schoolId,
          classId: selectedClassId,
          weekStart: selectedDate,
          rewardCoins: loadedGoal.rewardCoins ?? 25,
          schoolTimeZone: loadedGoal.schoolTimeZone || "Asia/Kolkata",
          assignedAtUtc: loadedGoal.assignedAtUtc || new Date().toISOString(),
          createdByTeacherId: loadedGoal.createdByTeacherId || props.profile.uid
        });
        setSaveState("");
      })
      .catch((error) => {
        if (alive) {
          setSaveState(error instanceof Error ? error.message : "Could not load class plan.");
        }
      });
    return () => {
      alive = false;
    };
  }, [props.profile.schoolId, selectedClassId, selectedDate]);

  useEffect(() => {
    if (!selectedStudent && classStudents[0]) {
      setSelectedStudentId(classStudents[0].id);
    }
  }, [classStudents, selectedStudent]);

  useEffect(() => {
    setOverrideDraft(selectedStudent
      ? existingOverride ?? buildStudentOverride(mission, selectedStudent.id, props.profile.uid)
      : null);
  }, [existingOverride, mission, selectedStudent, props.profile.uid]);

  const updateMission = (update: Partial<DailyMissionAssignment>) => {
    setMission({
      ...mission,
      ...update,
      id: update.date ?? mission.date,
      classId: selectedClassId,
      createdByTeacherId: props.profile.uid
    });
  };

  const updateWorldGoal = (update: Partial<WorldGoalAssignment>) => {
    setWorldGoal({
      ...worldGoal,
      ...update,
      goalId: update.goalId ?? worldGoal.goalId,
      schoolId: props.profile.schoolId,
      classId: selectedClassId,
      weekStart: selectedDate,
      rewardCoins: Math.max(0, worldGoal.rewardCoins ?? 25),
      schoolTimeZone: worldGoal.schoolTimeZone || "Asia/Kolkata",
      assignedAtUtc: worldGoal.assignedAtUtc || new Date().toISOString(),
      createdByTeacherId: props.profile.uid
    });
  };

  const saveMission = async () => {
    setSaveState("Saving class calendar plan...");
    await saveDailyMission(mission);
    props.setDataset({
      ...props.dataset,
      mission,
      activeClass: props.dataset.classrooms.find((room) => room.id === selectedClassId) ?? props.dataset.activeClass
    });
    setSaveState("Class plan saved.");
  };

  const saveMissionDays = async (days: number) => {
    setSaveState(`Saving ${days} days...`);
    await saveMissionRange(mission, days);
    setSaveState(days >= 28 ? "Month plan saved." : "Week plan saved.");
  };

  const saveGoal = async () => {
    const goal = {
      ...worldGoal,
      goalId: worldGoal.goalId || buildWorldGoalId(selectedClassId, selectedDate),
      schoolId: props.profile.schoolId,
      classId: selectedClassId,
      weekStart: selectedDate,
      createdByTeacherId: props.profile.uid
    };
    setSaveState("Saving class focus...");
    await saveWorldGoal(goal);
    props.setDataset({
      ...props.dataset,
      worldGoals: [
        ...props.dataset.worldGoals.filter((existing) => !(
          existing.goalId === goal.goalId &&
          existing.classId === goal.classId &&
          !existing.studentId
        )),
        goal
      ],
      activeClass: props.dataset.classrooms.find((room) => room.id === selectedClassId) ?? props.dataset.activeClass
    });
    setWorldGoal(goal);
    setSaveState("Class focus saved.");
  };

  const updateOverride = (update: Partial<StudentMissionOverride>) => {
    if (!overrideDraft || !selectedStudent) {
      return;
    }
    setOverrideDraft({
      ...overrideDraft,
      ...update,
      id: update.date ?? overrideDraft.date,
      schoolId: props.profile.schoolId,
      classId: selectedClassId,
      studentId: selectedStudent.id,
      baseMissionId: mission.id,
      createdByTeacherId: props.profile.uid
    });
  };

  const resetOverrideDraft = () => {
    if (!selectedStudent) {
      return;
    }
    setOverrideDraft(buildStudentOverride(mission, selectedStudent.id, props.profile.uid));
  };

  const saveOverride = async () => {
    if (!overrideDraft || !selectedStudent) {
      setSaveState("Select a student first.");
      return;
    }
    setSaveState(`Saving custom plan for ${selectedStudent.name}...`);
    await saveStudentMissionOverride(overrideDraft);
    props.setDataset({
      ...props.dataset,
      studentMissionOverrides: [
        ...props.dataset.studentMissionOverrides.filter((override) => !(
          override.studentId === overrideDraft.studentId &&
          override.date === overrideDraft.date
        )),
        overrideDraft
      ]
    });
    setSaveState(`Custom plan saved for ${selectedStudent.name}.`);
  };

  return (
    <div className="pageStack">
      <section className="panel plannerGrid">
        <label>
          Class
          <select value={selectedClassId} onChange={(event) => setSelectedClassId(event.target.value)}>
            {props.dataset.classrooms.map((room) => <option key={room.id} value={room.id}>{room.name}</option>)}
          </select>
        </label>
        <label>
          Date
          <input
            type="date"
            value={selectedDate}
            onChange={(event) => {
              setSelectedDate(event.target.value);
              updateMission({ date: event.target.value, id: event.target.value });
            }}
          />
        </label>
        <label>
          Practice type
          <select
            value={mission.missionType}
            onChange={(event) => updateMission({ missionType: event.target.value as MissionType })}
          >
            <option value="practice">Practice</option>
            <option value="revision">Revision</option>
            <option value="test">Test</option>
          </select>
        </label>
        <label>
          Duration
          <input
            type="range"
            min={3}
            max={20}
            value={Math.round(mission.missionDurationSeconds / 60)}
            onChange={(event) => updateMission({ missionDurationSeconds: Number(event.target.value) * 60 })}
          />
          <span className="rangeValue">{formatDuration(mission.missionDurationSeconds)}</span>
        </label>
        <label>
          Counting chests
          <input
            type="number"
            min={0}
            max={2}
            value={mission.countingChestCount}
            onChange={(event) => updateMission({ countingChestCount: clampChestCount(event.target.value, 1) })}
          />
        </label>
        <label>
          Color chests
          <input
            type="number"
            min={0}
            max={2}
            value={mission.colorChestCount}
            onChange={(event) => updateMission({ colorChestCount: clampChestCount(event.target.value, 0) })}
          />
        </label>
      </section>
      <section className="panel">
        <div className="buttonRow">
          {weekDates.map((date) => (
            <button
              className={date === selectedDate ? "statusButton unlocked" : "statusButton"}
              key={date}
              onClick={() => setSelectedDate(date)}
            >
              <CalendarDays size={17} />
              {shortDate(date)}
            </button>
          ))}
        </div>
      </section>
      <section className="panel">
        <div className="buttonRow">
          {durationPresets.map((minutes) => (
            <button className="secondaryButton" key={minutes} onClick={() => updateMission({ missionDurationSeconds: minutes * 60 })}>
              <ClockIcon />
              {minutes} min
            </button>
          ))}
          <button className="primaryButton fitButton" onClick={saveMission}>
            <CheckCircle2 size={17} />
            Save Practice Plan
          </button>
          <button className="secondaryButton" onClick={() => saveMissionDays(5)}>
            <CalendarDays size={17} />
            Save Week
          </button>
          <button className="secondaryButton" onClick={() => saveMissionDays(30)}>
            <CalendarDays size={17} />
            Save Month
          </button>
        </div>
        {saveState && <p className="finePrint">{saveState}</p>}
      </section>
      <section className="panel">
        <div className="sectionHeader">
          <div>
            <h2>Class Focus</h2>
            <p>Set the suggested Grammar RPG pacing for this class: town, route practice, gym checkpoint, focus patterns, and vocabulary.</p>
          </div>
          <span className="modePill">Pacing guide</span>
        </div>
        <div className="plannerGrid">
          <label>
            Suggested region
            <select
              value={worldGoal.targetGymId}
              onChange={(event) => {
                const option = grammarRegionOptions.find((region) => region.targetGymId === event.target.value) ?? grammarRegionOptions[0];
                updateWorldGoal({
                  targetAreaId: option.targetAreaId,
                  targetGymId: option.targetGymId,
                  focusGrammarPatterns: option.focusGrammarPatterns,
                  focusVocabulary: option.focusVocabulary
                });
              }}
            >
              {grammarRegionOptions.map((region) => (
                <option key={region.targetGymId} value={region.targetGymId}>{region.label}</option>
              ))}
            </select>
          </label>
          <label>
            Week starts
            <input
              type="date"
              value={worldGoal.weekStart}
              onChange={(event) => {
                setSelectedDate(event.target.value);
                updateMission({ date: event.target.value, id: event.target.value });
                updateWorldGoal({
                  goalId: buildWorldGoalId(selectedClassId, event.target.value),
                  weekStart: event.target.value,
                  dueDate: addDays(event.target.value, 6)
                });
              }}
            />
          </label>
          <label>
            Due date
            <input
              type="date"
              value={worldGoal.dueDate}
              onChange={(event) => updateWorldGoal({ dueDate: event.target.value })}
            />
          </label>
          <label>
            On-time coin reward
            <input
              type="number"
              min="0"
              max="10000"
              value={worldGoal.rewardCoins ?? 25}
              onChange={(event) => updateWorldGoal({ rewardCoins: Math.max(0, Number.parseInt(event.target.value || "0", 10)) })}
            />
          </label>
          <label>
            Suggested gym checkpoint
            <input
              value={worldGoal.targetGymId}
              onChange={(event) => updateWorldGoal({ targetGymId: event.target.value.trim() })}
            />
          </label>
        </div>
        <div className="overrideGrid">
          <label>
            Focus grammar patterns
            <textarea
              value={worldGoal.focusGrammarPatterns.join(", ")}
              onChange={(event) => updateWorldGoal({ focusGrammarPatterns: splitCsvValues(event.target.value) })}
            />
          </label>
          <label>
            Focus vocabulary
            <textarea
              value={worldGoal.focusVocabulary.join(", ")}
              onChange={(event) => updateWorldGoal({ focusVocabulary: splitWords(event.target.value) })}
            />
          </label>
          <div>
            <p className="smallLabel">Focus summary</p>
            <div className="tokenRow">
              <span className="wordToken">{humanizeAreaId(worldGoal.targetAreaId)}</span>
              <span className="wordToken">{humanizeAreaId(worldGoal.targetGymId)}</span>
            </div>
          </div>
          <button className="primaryButton fitButton" onClick={saveGoal}>
            <CheckCircle2 size={17} />
            Save Class Focus
          </button>
        </div>
      </section>
      <section className="panel">
        <div className="sectionHeader">
          <div>
            <h2>Practice Pool</h2>
            <p>Optional letter and word review pool used by repeatable world goal practice.</p>
          </div>
        </div>
        <LetterPicker
          selected={mission.lettersForToday}
          source={alphabet}
          max={alphabet.length}
          onChange={(letters) => updateMission({ lettersForToday: letters })}
        />
        <label>
          Practice words
          <textarea
            value={mission.wordsForToday.join(", ")}
            onChange={(event) => updateMission({ wordsForToday: splitWords(event.target.value) })}
          />
        </label>
        <label>
          Revision letters
          <textarea
            value={mission.revisionLetters.join(", ")}
            onChange={(event) => updateMission({ revisionLetters: splitWords(event.target.value).map((word) => word[0]).filter(Boolean) })}
          />
        </label>
      </section>
      <section className="panel">
        <div className="sectionHeader">
          <div>
            <h2>Student-Specific Change</h2>
            <p>Use this when one learner needs a different set of letters, words, duration, or revision focus for the same date.</p>
          </div>
          {existingOverride && <span className="modePill">Custom plan exists</span>}
        </div>
        <div className="plannerGrid">
          <label>
            Student
            <select value={selectedStudent?.id ?? ""} onChange={(event) => setSelectedStudentId(event.target.value)}>
              {classStudents.map((student) => <option key={student.id} value={student.id}>{student.name}</option>)}
            </select>
          </label>
          <label>
            Custom duration
            <input
              type="range"
              min={3}
              max={20}
              value={Math.round((overrideDraft?.missionDurationSeconds ?? mission.missionDurationSeconds) / 60)}
              onChange={(event) => updateOverride({ missionDurationSeconds: Number(event.target.value) * 60 })}
            />
            <span className="rangeValue">{formatDuration(overrideDraft?.missionDurationSeconds ?? mission.missionDurationSeconds)}</span>
          </label>
          <label>
            Counting chests
            <input
              type="number"
              min={0}
              max={2}
              value={overrideDraft?.countingChestCount ?? mission.countingChestCount}
              onChange={(event) => updateOverride({ countingChestCount: clampChestCount(event.target.value, 1) })}
            />
          </label>
          <label>
            Color chests
            <input
              type="number"
              min={0}
              max={2}
              value={overrideDraft?.colorChestCount ?? mission.colorChestCount}
              onChange={(event) => updateOverride({ colorChestCount: clampChestCount(event.target.value, 0) })}
            />
          </label>
          <button className="secondaryButton" onClick={resetOverrideDraft}>
            <RefreshCw size={17} />
            Copy Class Plan
          </button>
          <button className="primaryButton fitButton" onClick={saveOverride}>
            <CheckCircle2 size={17} />
            Save Student Plan
          </button>
        </div>
        {overrideDraft && (
          <div className="overrideGrid">
            <div>
              <p className="smallLabel">Custom letters</p>
              <LetterPicker
                selected={overrideDraft.lettersForToday}
                source={alphabet}
                max={alphabet.length}
                onChange={(letters) => updateOverride({ lettersForToday: letters })}
              />
            </div>
            <label>
              Custom words
              <textarea
                value={overrideDraft.wordsForToday.join(", ")}
                onChange={(event) => updateOverride({ wordsForToday: splitWords(event.target.value) })}
              />
            </label>
            <label>
              Custom revision letters
              <textarea
                value={overrideDraft.revisionLetters.join(", ")}
                onChange={(event) => updateOverride({ revisionLetters: splitWords(event.target.value).map((word) => word[0]).filter(Boolean) })}
              />
            </label>
            <label>
              Teacher note
              <textarea
                value={overrideDraft.note ?? ""}
                onChange={(event) => updateOverride({ note: event.target.value })}
                placeholder="Why this student needs a different practice plan"
              />
            </label>
          </div>
        )}
      </section>
    </div>
  );
}
