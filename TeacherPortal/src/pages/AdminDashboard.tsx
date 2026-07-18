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

type AdminTab = "setup" | "students" | "audit";

export function AdminDashboard(props: {
  profile: UserProfile;
  dataset: PortalDataset;
  setDataset: (dataset: PortalDataset) => void;
}) {
  const [tab, setTab] = useState<AdminTab>("setup");
  const [selectedStudentId, setSelectedStudentId] = useState(props.dataset.students[0]?.id ?? "");
  const [className, setClassName] = useState("LKG A");
  const [studentName, setStudentName] = useState("");
  const [studentEmail, setStudentEmail] = useState("");
  const [parentEmail, setParentEmail] = useState("");
  const [studentPassword, setStudentPassword] = useState("");
  const [teacherName, setTeacherName] = useState("");
  const [teacherEmail, setTeacherEmail] = useState("");
  const [teacherPassword, setTeacherPassword] = useState("");
  const [teacherClassIds, setTeacherClassIds] = useState<string[]>([props.dataset.activeClass.id]);
  const [selectedClassId, setSelectedClassId] = useState(props.dataset.activeClass.id);
  const [schoolName, setSchoolName] = useState(props.dataset.school.name);
  const [academicYear, setAcademicYear] = useState(props.dataset.school.academicYear ?? "2026-2027");
  const [setupStatus, setSetupStatus] = useState("");

  const selectedStudent = props.dataset.students.find((student) => student.id === selectedStudentId) ?? props.dataset.students[0];
  const selectedClassStudents = props.dataset.students.filter((student) => student.classId === selectedClassId);

  const saveSchoolDetails = async () => {
    if (!schoolName.trim()) {
      setSetupStatus("Enter a school name.");
      return;
    }

    setSetupStatus("Saving school...");
    const school = {
      ...props.dataset.school,
      id: props.profile.schoolId,
      name: schoolName.trim(),
      academicYear: academicYear.trim()
    };
    await saveSchool(school);
    props.setDataset({
      ...props.dataset,
      school
    });
    setSetupStatus("School saved.");
  };

  const createClass = async () => {
    setSetupStatus("Creating class...");
    const classroom = await createClassroom({ schoolId: props.profile.schoolId, name: className });
    props.setDataset({
      ...props.dataset,
      classrooms: [...props.dataset.classrooms.filter((room) => room.id !== classroom.id), classroom],
      activeClass: classroom
    });
    setSelectedClassId(classroom.id);
    setTeacherClassIds([classroom.id]);
    setSetupStatus("Class created.");
  };

  const createStudent = async () => {
    if (!studentName.trim()) {
      setSetupStatus("Enter a student name.");
      return;
    }
    if (!studentEmail.trim() || !parentEmail.trim() || studentPassword.length < 6) {
      setSetupStatus("Enter student email, parent email, and a 6+ character password.");
      return;
    }
    setSetupStatus("Creating student account...");
    const student = await createStudentRecord({
      schoolId: props.profile.schoolId,
      classId: selectedClassId,
      name: studentName.trim(),
      email: studentEmail.trim(),
      parentEmail: parentEmail.trim(),
      password: studentPassword,
      avatarColor: "#7fc8ff"
    });
    props.setDataset({
      ...props.dataset,
      students: [...props.dataset.students, student]
    });
    setSelectedStudentId(student.id);
    setStudentName("");
    setStudentEmail("");
    setParentEmail("");
    setStudentPassword("");
    setSetupStatus(`Student account created for ${student.email}. Share the password with the student or parent.`);
  };

  const createTeacher = async () => {
    if (!teacherName.trim() || !teacherEmail.trim() || teacherPassword.length < 6) {
      setSetupStatus("Enter teacher name, email, and a 6+ character temporary password.");
      return;
    }
    const validTeacherClassIds = teacherClassIds.filter((classId) =>
      props.dataset.classrooms.some((room) => room.id === classId)
    );
    const assignedClassIds = validTeacherClassIds.length > 0
      ? validTeacherClassIds
      : selectedClassId
        ? [selectedClassId]
        : [];

    if (assignedClassIds.length === 0) {
      setSetupStatus("Assign at least one class to the teacher.");
      return;
    }

    setSetupStatus("Creating teacher account...");
    const teacher = await createTeacherAccount({
      schoolId: props.profile.schoolId,
      teacherEmail: teacherEmail.trim(),
      displayName: teacherName.trim(),
      password: teacherPassword,
      classIds: assignedClassIds
    });
    props.setDataset({
      ...props.dataset,
      teachers: [
        ...props.dataset.teachers.filter((item) => item.uid !== teacher.uid),
        teacher
      ].sort((a, b) => a.displayName.localeCompare(b.displayName))
    });
    setTeacherName("");
    setTeacherEmail("");
    setTeacherPassword("");
    setSetupStatus(`Teacher account ready for ${teacher.email}.`);
  };

  const updateStudentTier = async (tier: "free" | "standard" | "premium") => {
    if (!selectedStudent) return;
    setSetupStatus(`Updating ${selectedStudent.name}'s service tier...`);
    try {
      await setStudentServiceTier({
        schoolId: props.profile.schoolId,
        studentId: selectedStudent.id,
        tier
      });
      props.setDataset({
        ...props.dataset,
        students: props.dataset.students.map((student) => student.id === selectedStudent.id
          ? { ...student, subscriptionTier: tier }
          : student)
      });
      setSetupStatus(`${selectedStudent.name} now uses the ${tier} tier.`);
    } catch (error) {
      setSetupStatus(error instanceof Error ? error.message : "Tier could not be updated.");
    }
  };

  const toggleTeacherClass = (classId: string) => {
    setTeacherClassIds((classIds) => classIds.includes(classId)
      ? classIds.filter((id) => id !== classId)
      : [...classIds, classId]);
  };

  return (
    <div className="pageStack">
      <Header title="Admin Workspace" subtitle="Create teacher accounts, classes, and student login accounts." />
      <SegmentedNav
        items={[
          ["setup", "School Setup", <GraduationCap size={17} />],
          ["students", "Student Logins", <Users size={17} />],
          ["audit", "Audit", <ClipboardCheck size={17} />]
        ]}
        active={tab}
        onChange={(value) => setTab(value as AdminTab)}
      />
      {tab === "setup" && (
        <div className="pageStack">
          <section className="panel plannerGrid">
            <label>
              School name
              <input value={schoolName} onChange={(event) => setSchoolName(event.target.value)} />
            </label>
            <label>
              Academic year
              <input value={academicYear} onChange={(event) => setAcademicYear(event.target.value)} />
            </label>
            <button className="secondaryButton" onClick={saveSchoolDetails}>
              <CheckCircle2 size={17} />
              Save School
            </button>
            <span className="finePrint">School ID: {props.profile.schoolId}</span>
          </section>
          <div className="metricGrid">
            <Metric label="School" value={props.dataset.school.name} />
            <Metric label="Teachers" value={String(props.dataset.teachers.length)} />
            <Metric label="Classes" value={String(props.dataset.classrooms.length)} />
            <Metric label="Students" value={String(props.dataset.students.length)} />
          </div>
          <section className="panel">
            <div className="sectionHeader">
              <div>
                <h2>{props.dataset.school.name}</h2>
                <p>{props.dataset.school.academicYear ?? "Pilot"} - teachers under this school</p>
              </div>
            </div>
            <div className="teacherRoster">
              {props.dataset.teachers.map((teacher) => (
                <article className="teacherRosterItem" key={teacher.uid}>
                  <div>
                    <strong>{teacher.displayName}</strong>
                    <span>{teacher.email}</span>
                  </div>
                  <small>{teacher.classIds.map((classId) => props.dataset.classrooms.find((room) => room.id === classId)?.name ?? classId).join(", ") || "No classes assigned"}</small>
                </article>
              ))}
              {props.dataset.teachers.length === 0 && <p className="muted">No teacher accounts created yet.</p>}
            </div>
          </section>
          <section className="panel plannerGrid">
            <label>
              Class name
              <input value={className} onChange={(event) => setClassName(event.target.value)} />
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
            <span className="finePrint">{setupStatus}</span>
          </section>
          <section className="panel plannerGrid">
            <label>
              Teacher name
              <input value={teacherName} onChange={(event) => setTeacherName(event.target.value)} />
            </label>
            <label>
              Teacher email
              <input value={teacherEmail} onChange={(event) => setTeacherEmail(event.target.value)} type="email" />
            </label>
            <label>
              Temporary password
              <input value={teacherPassword} onChange={(event) => setTeacherPassword(event.target.value)} type="password" />
            </label>
            <button className="secondaryButton" onClick={createTeacher}>
              <UserPlus size={17} />
              Create Teacher Account
            </button>
            <div className="classCheckList">
              {props.dataset.classrooms.map((room) => (
                <label key={room.id} className="checkRow">
                  <input type="checkbox" checked={teacherClassIds.includes(room.id)} onChange={() => toggleTeacherClass(room.id)} />
                  {room.name}
                </label>
              ))}
            </div>
          </section>
          <section className="panel plannerGrid">
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
            <button className="secondaryButton" onClick={createStudent}>
              <UserPlus size={17} />
              Create Student Account
            </button>
          </section>
        </div>
      )}
      {tab === "students" && (
        <section className="panel plannerGrid widePlanner">
          <label>
            Student
            <select value={selectedStudentId} onChange={(event) => setSelectedStudentId(event.target.value)}>
              {(selectedClassStudents.length ? selectedClassStudents : props.dataset.students).map((student) => <option key={student.id} value={student.id}>{student.name}</option>)}
            </select>
          </label>
          <div className="generatedCode">
            <span>Login email</span>
            <strong>{selectedStudent?.email || "Not set"}</strong>
            <small>{selectedStudent?.parentEmail ? `Parent reset email: ${selectedStudent.parentEmail}` : "Create a student account to add login details."}</small>
          </div>
          <label>
            AI service tier
            <select
              value={selectedStudent?.subscriptionTier ?? "free"}
              disabled={!selectedStudent}
              onChange={(event) => updateStudentTier(event.target.value as "free" | "standard" | "premium")}
            >
              <option value="free">Free</option>
              <option value="standard">Standard</option>
              <option value="premium">Premium</option>
            </select>
          </label>
          <span className="finePrint">{setupStatus}</span>
        </section>
      )}
      {tab === "audit" && (
        <div className="pageStack">
          <OperationsSummaryPanel schoolId={props.profile.schoolId} />
          <section className="panel">
            <h2>Audit Trail</h2>
            <p className="muted">Production audit events are written by server functions for role changes, consent/deletion changes, code creation/redemption, and goal or practice edits.</p>
            <div className="auditList">
              <span>student.privacy.granted</span>
              <span>student.deletion.requested</span>
              <span>code.create</span>
              <span>mission.update</span>
              <span>role.set</span>
              <span>code.redeem</span>
            </div>
          </section>
        </div>
      )}
    </div>
  );
}

export function OperationsSummaryPanel(props: { schoolId: string }) {
  const [summary, setSummary] = useState<Awaited<ReturnType<typeof getOperationsSummary>> | null>(null);
  const [status, setStatus] = useState("Loading operational health...");

  const refresh = async () => {
    setStatus("Loading operational health...");
    try {
      const next = await getOperationsSummary({ schoolId: props.schoolId, days: 7 });
      setSummary(next);
      setStatus("Estimated costs are guardrails, not provider invoices.");
    } catch (error) {
      setStatus(error instanceof Error ? error.message : "Operational health could not be loaded.");
    }
  };

  useEffect(() => { void refresh(); }, [props.schoolId]);

  const buddy = summary?.buddy[0] ?? {};
  const buddyStt = summary?.buddyStt[0] ?? {};
  const pronunciation = summary?.pronunciation[0] ?? {};
  const diagnostics = summary?.diagnostics[0] ?? {};
  const safety = summary?.safety[0] ?? {};
  const cost = summary?.costBudget[0] ?? {};
  const number = (row: Record<string, unknown>, key: string) => Number(row[key] ?? 0) || 0;
  const buddyRequests = number(buddy, "requestCount");
  const buddySttSeconds = number(buddyStt, "audioSeconds");
  const pronunciationRequests = number(pronunciation, "requestCount");
  const buddyLatency = buddyRequests > 0 ? number(buddy, "totalLatencyMs") / buddyRequests : 0;
  const pronunciationLatency = pronunciationRequests > 0 ? number(pronunciation, "totalLatencyMs") / pronunciationRequests : 0;
  const reservedCost = number(cost, "reservedCostMicroUsd") / 1_000_000;
  const costLimit = number(cost, "costLimitMicroUsd") / 1_000_000;

  return (
    <section className="panel">
      <div className="sectionHeader">
        <div><h2>Operational Health</h2><p>Today's aggregate latency, safety, crash, and AI-budget signals.</p></div>
        <button className="secondaryButton fitButton" onClick={refresh}><RefreshCw size={16} /> Refresh</button>
      </div>
      <div className="metricGrid">
        <Metric label="Buddy calls" value={String(buddyRequests)} detail={`${Math.round(buddyLatency)} ms average`} />
        <Metric label="Buddy speech input" value={`${Math.round(buddySttSeconds / 60)} min`} detail={`$${(number(buddyStt, "estimatedCostMicroUsd") / 1_000_000).toFixed(3)} estimated`} />
        <Metric label="Pronunciation" value={String(pronunciationRequests)} detail={`${Math.round(pronunciationLatency)} ms average`} />
        <Metric label="Managed crashes" value={String(number(diagnostics, "crashCount"))} detail={`${number(diagnostics, "reportCount")} diagnostic reports`} />
        <Metric label="Safety intercepts" value={String(number(safety, "interceptCount"))} detail="Raw intercepted text is not stored" />
        <Metric label="AI budget reserved" value={`$${reservedCost.toFixed(3)}`} detail={costLimit > 0 ? `of $${costLimit.toFixed(2)} daily cap` : "No paid requests today"} />
      </div>
      <p className="muted">{status}</p>
    </section>
  );
}
