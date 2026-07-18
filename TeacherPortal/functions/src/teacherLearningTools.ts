import { trimmedString } from "./inputHelpers.js";
import { db } from "./runtime.js";
import { finiteNumber, normalizedStringArray } from "./sharedUtils.js";

export type TeacherLearningDomain = "all" | "pronunciation" | "handwriting" | "grammar" | "buddy";
export type TeacherLearningToolName =
  | "getClassPronunciationStruggles"
  | "getStudentStruggleTimeline"
  | "getClassSkillTrend";

export interface TeacherLearningToolCall {
  name: TeacherLearningToolName;
  arguments: {
    domain: TeacherLearningDomain;
    dateRange: DateRangeUtc;
    studentId?: string;
  };
}

export interface DateRangeUtc {
  startUtc: string;
  endUtc: string;
  label: string;
}

export interface TeacherLearningToolResult {
  sourceId: string;
  kind: `tool:${TeacherLearningToolName}`;
  summary: Record<string, unknown>;
  citations: string[];
}

export interface TeacherLearningToolPlan {
  dateRange: DateRangeUtc;
  domain: TeacherLearningDomain;
  calls: TeacherLearningToolCall[];
  trace: string[];
}

export const teacherLearningToolSchemas = [
  {
    name: "getClassPronunciationStruggles",
    description: "Return class-level pronunciation struggle groups for a date range.",
    inputSchema: {
      type: "object",
      properties: {
        classId: { type: "string" },
        dateRange: { type: "object" },
      },
      required: ["classId", "dateRange"],
    },
  },
  {
    name: "getStudentStruggleTimeline",
    description: "Return a student's struggle timeline grouped by day and instructional terms.",
    inputSchema: {
      type: "object",
      properties: {
        studentId: { type: "string" },
        domain: { type: "string" },
        dateRange: { type: "object" },
      },
      required: ["studentId", "domain", "dateRange"],
    },
  },
  {
    name: "getClassSkillTrend",
    description: "Return class-level counts and common terms/error tags for a learning domain.",
    inputSchema: {
      type: "object",
      properties: {
        classId: { type: "string" },
        domain: { type: "string" },
        dateRange: { type: "object" },
      },
      required: ["classId", "domain", "dateRange"],
    },
  },
] as const;

export const teacherLearningToolManifest = {
  name: "teacher-learning-tools",
  version: "0.1.0",
  status: {
    mcpReady: true,
    standaloneMcpServerEndpoint: false,
    transport: "firebase-callable",
    note: "Schemas and execution are MCP-ready, but this deployment does not expose a standalone MCP server endpoint yet.",
  },
  tools: teacherLearningToolSchemas,
} as const;

export function planTeacherLearningTools(input: {
  question: string;
  studentId?: string;
  now?: Date;
}): TeacherLearningToolPlan {
  const question = input.question.toLowerCase();
  const domain = inferDomain(question);
  const dateRange = inferDateRangeUtc(question, input.now ?? new Date());
  const asksWho = /\b(who|which students?|who all)\b/i.test(input.question);
  const asksTimeline = /\b(days?|when|timeline|history|struggled)\b/i.test(input.question);
  const calls: TeacherLearningToolCall[] = [];
  if (domain === "pronunciation" && asksWho && !input.studentId) {
    calls.push({ name: "getClassPronunciationStruggles", arguments: { domain, dateRange } });
  } else if (input.studentId && asksTimeline) {
    calls.push({ name: "getStudentStruggleTimeline", arguments: { domain, dateRange, studentId: input.studentId } });
  } else {
    calls.push({ name: "getClassSkillTrend", arguments: { domain, dateRange, studentId: input.studentId } });
  }
  return {
    dateRange,
    domain,
    calls,
    trace: [
      `Planner agent inferred ${domain} domain.`,
      `Planner agent selected ${dateRange.label} (${dateRange.startUtc.slice(0, 10)} to ${dateRange.endUtc.slice(0, 10)}).`,
      `Planner agent selected tools: ${calls.map(call => call.name).join(", ")}.`,
    ],
  };
}

export async function executeTeacherLearningTools(input: {
  schoolId: string;
  classId: string;
  studentId?: string;
  question: string;
  roster: Array<{ studentId: string; name: string }>;
}): Promise<{ plan: TeacherLearningToolPlan; results: TeacherLearningToolResult[] }> {
  const plan = planTeacherLearningTools({ question: input.question, studentId: input.studentId });
  const results: TeacherLearningToolResult[] = [];
  for (const call of plan.calls) {
    if (call.name === "getClassPronunciationStruggles") {
      results.push(await getClassPronunciationStruggles({ ...input, dateRange: call.arguments.dateRange, roster: input.roster }));
    } else if (call.name === "getStudentStruggleTimeline") {
      results.push(await getStudentStruggleTimeline({
        ...input,
        studentId: call.arguments.studentId || input.studentId || "",
        domain: call.arguments.domain,
        dateRange: call.arguments.dateRange,
        roster: input.roster,
      }));
    } else {
      results.push(await getClassSkillTrend({
        ...input,
        domain: call.arguments.domain,
        dateRange: call.arguments.dateRange,
        roster: input.roster,
      }));
    }
  }
  return { plan, results };
}

async function getClassPronunciationStruggles(input: {
  schoolId: string;
  classId: string;
  dateRange: DateRangeUtc;
  roster: Array<{ studentId: string; name: string }>;
}): Promise<TeacherLearningToolResult> {
  const records = await queryTeacherEvidence({
    schoolId: input.schoolId,
    classId: input.classId,
    signal: "struggle",
    dateRange: input.dateRange,
    limit: 240,
  });
  const pronunciationRecords = records.filter(record => recordMatchesDomain(record, "pronunciation"));
  const grouped = groupByStudent(pronunciationRecords, input.roster);
  return {
    sourceId: `tool:getClassPronunciationStruggles:${input.classId}:${input.dateRange.startUtc.slice(0, 10)}:${input.dateRange.endUtc.slice(0, 10)}`,
    kind: "tool:getClassPronunciationStruggles",
    summary: {
      classId: input.classId,
      dateRange: input.dateRange,
      domain: "pronunciation",
      studentCount: grouped.length,
      struggleEventCount: pronunciationRecords.length,
      students: grouped.slice(0, 12),
    },
    citations: pronunciationRecords.slice(0, 12).map(record => record.sourceId),
  };
}

async function getStudentStruggleTimeline(input: {
  schoolId: string;
  classId: string;
  studentId: string;
  domain: TeacherLearningDomain;
  dateRange: DateRangeUtc;
  roster: Array<{ studentId: string; name: string }>;
}): Promise<TeacherLearningToolResult> {
  const records = await queryTeacherEvidence({
    schoolId: input.schoolId,
    classId: input.classId,
    studentId: input.studentId,
    signal: "struggle",
    dateRange: input.dateRange,
    limit: 160,
  });
  const filtered = records.filter(record => recordMatchesDomain(record, input.domain));
  const byDate = new Map<string, IndexedEvidence[]>();
  for (const record of filtered) {
    const date = record.occurredDateUtc || record.occurredAtUtc.slice(0, 10);
    byDate.set(date, [...(byDate.get(date) ?? []), record]);
  }
  const days = Array.from(byDate.entries())
    .sort((a, b) => b[0].localeCompare(a[0]))
    .slice(0, 14)
    .map(([dateUtc, items]) => ({
      dateUtc,
      struggleEventCount: items.length,
      terms: topStrings(items.flatMap(item => item.terms), 8),
      errorTags: topStrings(items.flatMap(item => item.errorTags), 8),
      citations: items.slice(0, 4).map(item => item.sourceId),
    }));
  return {
    sourceId: `tool:getStudentStruggleTimeline:${input.studentId}:${input.dateRange.startUtc.slice(0, 10)}:${input.dateRange.endUtc.slice(0, 10)}`,
    kind: "tool:getStudentStruggleTimeline",
    summary: {
      studentId: input.studentId,
      studentName: input.roster.find(student => student.studentId === input.studentId)?.name,
      dateRange: input.dateRange,
      domain: input.domain,
      struggleEventCount: filtered.length,
      days,
    },
    citations: filtered.slice(0, 12).map(record => record.sourceId),
  };
}

async function getClassSkillTrend(input: {
  schoolId: string;
  classId: string;
  studentId?: string;
  domain: TeacherLearningDomain;
  dateRange: DateRangeUtc;
  roster: Array<{ studentId: string; name: string }>;
}): Promise<TeacherLearningToolResult> {
  const records = await queryTeacherEvidence({
    schoolId: input.schoolId,
    classId: input.classId,
    studentId: input.studentId,
    dateRange: input.dateRange,
    limit: 260,
  });
  const filtered = records.filter(record => recordMatchesDomain(record, input.domain));
  const struggles = filtered.filter(record => record.signal === "struggle");
  const progress = filtered.filter(record => record.signal === "progress");
  return {
    sourceId: `tool:getClassSkillTrend:${input.classId}:${input.domain}:${input.dateRange.startUtc.slice(0, 10)}:${input.dateRange.endUtc.slice(0, 10)}`,
    kind: "tool:getClassSkillTrend",
    summary: {
      classId: input.classId,
      studentId: input.studentId,
      domain: input.domain,
      dateRange: input.dateRange,
      eventCount: filtered.length,
      struggleCount: struggles.length,
      progressCount: progress.length,
      activeStudentCount: new Set(filtered.map(record => record.studentId)).size,
      commonTerms: topStrings(struggles.flatMap(record => record.terms), 10),
      commonErrorTags: topStrings(struggles.flatMap(record => record.errorTags), 10),
      students: groupByStudent(struggles, input.roster).slice(0, 10),
    },
    citations: filtered.slice(0, 12).map(record => record.sourceId),
  };
}

async function queryTeacherEvidence(input: {
  schoolId: string;
  classId: string;
  studentId?: string;
  signal?: "struggle" | "progress" | "neutral";
  dateRange: DateRangeUtc;
  limit: number;
}): Promise<IndexedEvidence[]> {
  let query = db.collection(`schools/${input.schoolId}/teacherEvidence`).where("classId", "==", input.classId);
  if (input.studentId) query = query.where("studentId", "==", input.studentId);
  if (input.signal) query = query.where("signal", "==", input.signal);
  const snapshots = await query
    .orderBy("occurredAtUtc", "desc")
    .limit(input.limit)
    .get()
    .catch(() => ({ docs: [] }));
  return snapshots.docs
    .map(doc => indexedEvidenceFromData(doc.data()))
    .filter((record): record is IndexedEvidence => record != null)
    .filter(record => record.occurredAtUtc >= input.dateRange.startUtc && record.occurredAtUtc < input.dateRange.endUtc);
}

interface IndexedEvidence {
  sourceId: string;
  studentId: string;
  eventType: string;
  occurredAtUtc: string;
  occurredDateUtc: string;
  signal: "struggle" | "progress" | "neutral";
  severity: number;
  terms: string[];
  errorTags: string[];
}

function indexedEvidenceFromData(data: Record<string, unknown>): IndexedEvidence | null {
  const sourceId = trimmedString(data.sourceId, 512);
  const studentId = trimmedString(data.studentId, 160);
  const occurredAtUtc = trimmedString(data.occurredAtUtc, 40);
  if (!sourceId || !studentId || !occurredAtUtc) return null;
  const signal = data.signal === "struggle" || data.signal === "progress" ? data.signal : "neutral";
  return {
    sourceId,
    studentId,
    eventType: trimmedString(data.eventType, 80),
    occurredAtUtc,
    occurredDateUtc: trimmedString(data.occurredDateUtc, 10) || occurredAtUtc.slice(0, 10),
    signal,
    severity: finiteNumber(data.severity),
    terms: normalizedStringArray(data.terms, 80),
    errorTags: normalizedStringArray(data.errorTags, 80),
  };
}

function inferDomain(question: string): TeacherLearningDomain {
  if (/pronounc|pronunci|speak|spoken|word|sound/.test(question)) return "pronunciation";
  if (/handwrit|writing|letter|stroke/.test(question)) return "handwriting";
  if (/grammar|article|sentence|phrase|tense|plural/.test(question)) return "grammar";
  if (/buddy|conversation|chat|llm/.test(question)) return "buddy";
  return "all";
}

export function inferDateRangeUtc(question: string, now: Date): DateRangeUtc {
  const dayStart = startOfUtcDay(now);
  if (/\btoday\b/i.test(question)) {
    return rangeFromStart(dayStart, 1, "today");
  }
  if (/\byesterday\b/i.test(question)) {
    return rangeFromStart(addUtcDays(dayStart, -1), 1, "yesterday");
  }
  if (/\blast week\b/i.test(question)) {
    return { startUtc: addUtcDays(dayStart, -7).toISOString(), endUtc: dayStart.toISOString(), label: "last 7 days" };
  }
  const lastDays = /\blast\s+(\d{1,2})\s+days?\b/i.exec(question);
  if (lastDays) {
    const days = Math.max(1, Math.min(60, Number(lastDays[1])));
    return { startUtc: addUtcDays(dayStart, -days).toISOString(), endUtc: addUtcDays(dayStart, 1).toISOString(), label: `last ${days} days` };
  }
  if (/\bthis week\b/i.test(question)) {
    const day = dayStart.getUTCDay();
    const mondayOffset = day === 0 ? -6 : 1 - day;
    return { startUtc: addUtcDays(dayStart, mondayOffset).toISOString(), endUtc: addUtcDays(dayStart, 1).toISOString(), label: "this week" };
  }
  return { startUtc: addUtcDays(dayStart, -30).toISOString(), endUtc: addUtcDays(dayStart, 1).toISOString(), label: "last 30 days" };
}

function rangeFromStart(start: Date, days: number, label: string): DateRangeUtc {
  return { startUtc: start.toISOString(), endUtc: addUtcDays(start, days).toISOString(), label };
}

function startOfUtcDay(date: Date) {
  return new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate()));
}

function addUtcDays(date: Date, days: number) {
  const next = new Date(date);
  next.setUTCDate(next.getUTCDate() + days);
  return next;
}

function recordMatchesDomain(record: IndexedEvidence, domain: TeacherLearningDomain) {
  if (domain === "all") return true;
  if (domain === "pronunciation") {
    return ["wordCastEvents", "spokenPhraseEvents"].includes(record.eventType) ||
      record.errorTags.some(tag => /pronunciation|phoneme|sound/i.test(tag));
  }
  if (domain === "handwriting") {
    return ["letterAttempts", "writtenPhraseEvents"].includes(record.eventType) ||
      record.errorTags.some(tag => /handwriting|stroke|letter/i.test(tag));
  }
  if (domain === "grammar") {
    return ["grammarBattleEvents", "gymAttempts", "spokenPhraseEvents", "writtenPhraseEvents"].includes(record.eventType) ||
      record.terms.some(term => /article|tense|plural|grammar/i.test(term));
  }
  return record.eventType === "buddyConversationTurns";
}

function groupByStudent(records: IndexedEvidence[], roster: Array<{ studentId: string; name: string }>) {
  const byStudent = new Map<string, IndexedEvidence[]>();
  for (const record of records) {
    byStudent.set(record.studentId, [...(byStudent.get(record.studentId) ?? []), record]);
  }
  return Array.from(byStudent.entries())
    .map(([studentId, items]) => ({
      studentId,
      studentName: roster.find(student => student.studentId === studentId)?.name,
      struggleEventCount: items.length,
      dates: topStrings(items.map(item => item.occurredDateUtc), 6),
      terms: topStrings(items.flatMap(item => item.terms), 8),
      errorTags: topStrings(items.flatMap(item => item.errorTags), 8),
      citations: items.slice(0, 4).map(item => item.sourceId),
    }))
    .sort((a, b) => b.struggleEventCount - a.struggleEventCount || String(a.studentName ?? a.studentId).localeCompare(String(b.studentName ?? b.studentId)));
}

function topStrings(values: string[], maximum: number) {
  const counts = new Map<string, number>();
  for (const value of values.map(value => trimmedString(value, 120)).filter(Boolean)) {
    counts.set(value, (counts.get(value) ?? 0) + 1);
  }
  return Array.from(counts.entries())
    .sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]))
    .slice(0, maximum)
    .map(([value]) => value);
}
