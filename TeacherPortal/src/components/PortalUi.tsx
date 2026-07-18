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

import { alphabet } from "../utils/missionPlanning";

export function StudentRow(props: {
  students: Student[];
  selectedStudentId: string;
  onSelect: (id: string) => void;
}) {
  return (
    <section className="panel">
      <div className="studentRow">
        {props.students.map((student) => (
          <button
            key={student.id}
            className={student.id === props.selectedStudentId ? "studentChip active" : "studentChip"}
            onClick={() => props.onSelect(student.id)}
          >
            <span style={{ background: student.avatarColor }} />
            {student.name}
          </button>
        ))}
      </div>
    </section>
  );
}

export function StrokePreview(props: { points: { x: number; y: number; strokeId: number; order: number }[] }) {
  const strokes = Array.from(new Set(props.points.map((point) => point.strokeId)))
    .map((strokeId) => props.points
      .filter((point) => point.strokeId === strokeId)
      .sort((a, b) => a.order - b.order));

  return (
    <svg className="strokePreview" viewBox="0 0 240 260" role="img" aria-label="Accepted handwriting sample">
      <rect x="12" y="12" width="216" height="236" rx="8" />
      {strokes.map((stroke, index) => (
        <polyline key={index} points={stroke.map((point) => `${point.x},${point.y}`).join(" ")} />
      ))}
    </svg>
  );
}

export function RecommendationList(props: { recommendations: { id: string; priority: string; title: string; detail: string }[] }) {
  if (props.recommendations.length === 0) {
    return <p className="muted">No recommendations yet. New suggestions are generated from submitted run data.</p>;
  }

  return (
    <div className="recommendationList">
      {props.recommendations.map((recommendation) => (
        <article className="recommendation" data-priority={recommendation.priority} key={recommendation.id}>
          <strong>{recommendation.title}</strong>
          <span>{recommendation.detail}</span>
        </article>
      ))}
    </div>
  );
}

export function SignalGroup(props: { title: string; values: string[]; kind: "letter" | "word" }) {
  return (
    <article className="signalGroup">
      <strong>{props.title}</strong>
      <div className="tokenRow">
        {props.values.length === 0 && <span className="finePrint">Waiting for more run data.</span>}
        {props.values.map((value) => (
          <span className={props.kind === "letter" ? "letterToken" : "wordToken"} key={value}>{value}</span>
        ))}
      </div>
    </article>
  );
}

export function SegmentedNav(props: {
  items: [string, string, ReactNode][];
  active: string;
  onChange: (value: string) => void;
}) {
  return (
    <div className="segmentedNav">
      {props.items.map(([value, label, icon]) => (
        <button key={value} className={props.active === value ? "segment active" : "segment"} onClick={() => props.onChange(value)}>
          {icon}
          {label}
        </button>
      ))}
    </div>
  );
}

export function Header(props: { title: string; subtitle: string }) {
  return (
    <header className="pageHeader">
      <div>
        <h1>{props.title}</h1>
        <p>{props.subtitle}</p>
      </div>
    </header>
  );
}

export function Metric(props: { label: string; value: string; detail?: string }) {
  return (
    <section className="metric">
      <span>{props.label}</span>
      <strong>{props.value}</strong>
      {props.detail && <small>{props.detail}</small>}
    </section>
  );
}

export function LetterPicker(props: {
  selected: string[];
  source?: string[];
  max: number;
  onChange: (letters: string[]) => void;
}) {
  const source = props.source?.length ? props.source : alphabet;
  const toggle = (letter: string) => {
    if (props.selected.includes(letter)) {
      props.onChange(props.selected.filter((value) => value !== letter));
      return;
    }
    if (props.selected.length >= props.max) {
      props.onChange([...props.selected.slice(1), letter]);
      return;
    }
    props.onChange([...props.selected, letter]);
  };

  return (
    <div className="letterPicker">
      {source.map((letter) => (
        <button
          key={letter}
          className={props.selected.includes(letter) ? "letterOption selected" : "letterOption"}
          onClick={() => toggle(letter)}
        >
          {letter}
        </button>
      ))}
    </div>
  );
}

export function ClockIcon() {
  return <CalendarDays size={17} />;
}
