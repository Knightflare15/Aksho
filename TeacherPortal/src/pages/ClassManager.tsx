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

export function ClassManager(props: {
  profile: UserProfile;
  dataset: PortalDataset;
  setDataset: (dataset: PortalDataset) => void;
  selectedStudentId: string;
  setSelectedStudentId: (id: string) => void;
  setTab: (tab: TeacherTab) => void;
}) {
  const [className, setClassName] = useState("");
  const [selectedClassId, setSelectedClassId] = useState(props.dataset.activeClass.id);
  const [studentName, setStudentName] = useState("");
  const [studentEmail, setStudentEmail] = useState("");
  const [parentEmail, setParentEmail] = useState("");
  const [studentPassword, setStudentPassword] = useState("");
  const [status, setStatus] = useState("");

  const selectedClass = props.dataset.classrooms.find((room) => room.id === selectedClassId) ?? props.dataset.activeClass;
  const classStudents = props.dataset.students.filter((student) => student.classId === selectedClass.id);

  const createClass = async () => {
    if (!className.trim()) {
      setStatus("Enter a class name.");
      return;
    }

    setStatus("Creating class...");
    try {
      const classroom = await createClassroom({ schoolId: props.profile.schoolId, name: className.trim() });
      props.setDataset({
        ...props.dataset,
        classrooms: [...props.dataset.classrooms.filter((room) => room.id !== classroom.id), classroom],
        activeClass: classroom
      });
      setSelectedClassId(classroom.id);
      setClassName("");
      setStatus("Class created. You can add students now.");
    } catch (error) {
      setStatus(error instanceof Error ? error.message : "Class could not be created.");
    }
  };

  const createStudent = async () => {
    if (!studentName.trim()) {
      setStatus("Enter a student name.");
      return;
    }
    if (!studentEmail.trim() || !parentEmail.trim() || studentPassword.length < 6) {
      setStatus("Enter student email, parent email, and a 6+ character password.");
      return;
    }

    setStatus("Creating student account...");
    try {
      const student = await createStudentRecord({
        schoolId: props.profile.schoolId,
        classId: selectedClass.id,
        name: studentName.trim(),
        email: studentEmail.trim(),
        parentEmail: parentEmail.trim(),
        password: studentPassword,
        avatarColor: "#7fc8ff"
      });
      props.setDataset({
        ...props.dataset,
        students: [...props.dataset.students, student],
        classrooms: props.dataset.classrooms.map((room) => room.id === selectedClass.id
          ? { ...room, studentIds: [...new Set([...(room.studentIds ?? []), student.id])] }
          : room)
      });
      props.setSelectedStudentId(student.id);
      setStudentName("");
      setStudentEmail("");
      setParentEmail("");
      setStudentPassword("");
      setStatus(`Student account created for ${student.email}.`);
    } catch (error) {
      setStatus(error instanceof Error ? error.message : "Student account could not be created.");
    }
  };

  return (
    <div className="pageStack">
      <section className="panel plannerGrid">
        <label>
          New class name
          <input value={className} onChange={(event) => setClassName(event.target.value)} placeholder="LKG B" />
        </label>
        <button className="secondaryButton" onClick={createClass}>
          <GraduationCap size={17} />
          Create Class
        </button>
        <label>
          Active class
          <select value={selectedClassId} onChange={(event) => setSelectedClassId(event.target.value)}>
            {props.dataset.classrooms.map((room) => <option key={room.id} value={room.id}>{room.name}</option>)}
          </select>
        </label>
        <span className="finePrint">{status}</span>
      </section>
      <section className="panel plannerGrid accountGrid">
        <label>
          Student name
          <input value={studentName} onChange={(event) => setStudentName(event.target.value)} />
        </label>
        <label>
          Student login email
          <input value={studentEmail} onChange={(event) => setStudentEmail(event.target.value)} type="email" />
        </label>
        <label>
          Parent email
          <input value={parentEmail} onChange={(event) => setParentEmail(event.target.value)} type="email" />
        </label>
        <label>
          Initial password
          <input value={studentPassword} onChange={(event) => setStudentPassword(event.target.value)} type="password" />
        </label>
        <button className="primaryButton fitButton" onClick={createStudent}>
          <UserPlus size={17} />
          Create Student Account
        </button>
      </section>
      <section className="panel">
        <div className="sectionHeader">
          <div>
            <h2>{selectedClass.name}</h2>
            <p>{classStudents.length} student accounts</p>
          </div>
        </div>
        <div className="studentRow">
          {classStudents.map((student) => (
            <button
              key={student.id}
              className={student.id === props.selectedStudentId ? "studentChip active" : "studentChip"}
              onClick={() => {
                props.setSelectedStudentId(student.id);
                props.setTab("reports");
              }}
            >
              <span style={{ background: student.avatarColor }} />
              {student.name}
            </button>
          ))}
        </div>
        {props.selectedStudentId && (
          <div className="buttonRow">
            <button className="secondaryButton" onClick={() => props.setTab("reports")}>
              <BarChart3 size={17} />
              View Reports
            </button>
            <button className="secondaryButton" onClick={() => props.setTab("handwriting")}>
              <FileText size={17} />
              View Samples
            </button>
          </div>
        )}
      </section>
    </div>
  );
}
