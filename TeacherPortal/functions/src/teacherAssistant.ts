import { HttpsError, onCall } from "firebase-functions/v2/https";
import { assertSameSchool, assertStudentClassBinding, assertTeacherClassAccess, requireRole } from "./authorizationHelpers.js";
import { redactBuddyText } from "./buddyContextHelpers.js";
import { readGeminiApiKey } from "./buddyVoiceHelpers.js";
import { requiredTrimmedString, trimmedString } from "./inputHelpers.js";
import {
  db,
  geminiApiKey,
  teacherAssistantEnabled,
  teacherAssistantMaxOutputTokens,
  teacherAssistantModel,
  teacherAssistantVerifierEnabled,
} from "./runtime.js";
import { assertFirestorePathSegment, assertPayloadSize, cloneRecord, finiteNumber } from "./sharedUtils.js";
import { retrieveSemanticTeacherEvidence, retrieveStructuredTeacherEvidence } from "./teacherIntelligence.js";
import { executeTeacherLearningTools, teacherLearningToolManifest, teacherLearningToolSchemas } from "./teacherLearningTools.js";

const maximumQuestionCharacters = 1200;
const maximumEvidenceItems = 120;
const maximumEvidenceCharacters = 18000;

export interface TeacherAssistantEvidenceItem {
  sourceId: string;
  studentId?: string;
  studentName?: string;
  kind: string;
  summary: Record<string, unknown>;
  createdAtUtc?: string;
}

export interface TeacherAssistantEvidencePack {
  schoolId: string;
  classId: string;
  className: string;
  scope: "class" | "student";
  question: string;
  generatedAtUtc: string;
  roster: Array<{ studentId: string; name: string }>;
  evidence: TeacherAssistantEvidenceItem[];
}

export interface TeacherAssistantResponse {
  answer: string;
  suggestedActions: string[];
  citations: string[];
  agentTrace: string[];
  model: string;
  fallbackReason?: string;
  usage?: Record<string, unknown>;
}

export const askTeacherAssistant = onCall(
  {
    secrets: [geminiApiKey],
    timeoutSeconds: 45,
    memory: "512MiB",
  },
  async request => {
    const caller = requireRole(request.auth, ["admin", "teacher"]);
    const data = cloneRecord(request.data);
    assertPayloadSize(data, 24 * 1024);
    if (!teacherAssistantEnabled) {
      throw new HttpsError("failed-precondition", "Teacher assistant is not enabled.");
    }

    const schoolId = requiredTrimmedString(data.schoolId, "schoolId");
    const classId = requiredTrimmedString(data.classId, "classId");
    const studentId = trimmedString(data.studentId, 160);
    const question = redactBuddyText(requiredTrimmedString(data.question, "question"), maximumQuestionCharacters);
    const dryRun = data.dryRun === true;
    assertFirestorePathSegment(schoolId, "schoolId");
    assertFirestorePathSegment(classId, "classId");
    if (studentId) assertFirestorePathSegment(studentId, "studentId");
    assertSameSchool(caller, schoolId);
    assertTeacherClassAccess(caller, classId);
    if (studentId) await assertStudentClassBinding(schoolId, classId, studentId);

    const evidencePack = await collectTeacherAssistantEvidence({
      schoolId,
      classId,
      studentId,
      question,
    });
    if (dryRun) {
      return fallbackTeacherAssistantResponse(evidencePack, "dry_run_no_model");
    }
    const apiKey = readGeminiApiKey();
    if (!apiKey) {
      return fallbackTeacherAssistantResponse(evidencePack, "provider_unavailable");
    }
    return generateTeacherAssistantResponse(apiKey, evidencePack);
  },
);

export const getTeacherLearningToolManifest = onCall({}, request => {
  requireRole(request.auth, ["admin", "teacher"]);
  return teacherLearningToolManifest;
});

export async function collectTeacherAssistantEvidence(input: {
  schoolId: string;
  classId: string;
  studentId?: string;
  question: string;
}): Promise<TeacherAssistantEvidencePack> {
  const classSnapshot = await db.doc(`schools/${input.schoolId}/classes/${input.classId}`).get();
  const classData = classSnapshot.data() ?? {};
  const fullRoster = input.studentId
    ? [await readStudent(input.schoolId, input.studentId)]
    : await readClassRoster(input.schoolId, input.classId);
  const inferredStudentId = input.studentId || studentIdMentionedInQuestion(input.question, fullRoster);
  const roster = inferredStudentId
    ? fullRoster.filter(student => student.studentId === inferredStudentId)
    : fullRoster;
  const evidence: TeacherAssistantEvidenceItem[] = [];

  evidence.push({
    sourceId: `schools/${input.schoolId}/classes/${input.classId}`,
    kind: "classProfile",
    summary: {
      name: trimmedString(classData.name, 120) || input.classId,
      rosterCount: roster.length,
      requestedScope: inferredStudentId ? "student" : "class",
    },
  });
  for (const student of roster.slice(0, 30)) {
    evidence.push({
      sourceId: `schools/${input.schoolId}/students/${student.studentId}`,
      studentId: student.studentId,
      studentName: student.name,
      kind: "studentProfile",
      summary: {
        name: student.name,
        classId: input.classId,
      },
    });
  }
  const toolRun = await executeTeacherLearningTools({
    schoolId: input.schoolId,
    classId: input.classId,
    studentId: inferredStudentId,
    question: input.question,
    roster,
  });
  evidence.push({
    sourceId: `tool:planner:${input.classId}:${toolRun.plan.dateRange.startUtc.slice(0, 10)}:${toolRun.plan.domain}`,
    kind: "tool:planner",
    summary: {
      domain: toolRun.plan.domain,
      dateRange: toolRun.plan.dateRange,
      calls: toolRun.plan.calls.map(call => call.name),
      trace: toolRun.plan.trace,
    },
  });
  for (const result of toolRun.results) {
    evidence.push({
      sourceId: result.sourceId,
      kind: result.kind,
      summary: {
        ...result.summary,
        citations: result.citations,
      },
    });
  }
  const indexedEvidence = await retrieveStructuredTeacherEvidence({
    schoolId: input.schoolId,
    classId: input.classId,
    studentId: inferredStudentId,
    question: input.question,
    limit: inferredStudentId ? 60 : 40,
  });
  const semanticEvidence = await retrieveSemanticTeacherEvidence({
    schoolId: input.schoolId,
    classId: input.classId,
    studentId: inferredStudentId,
    question: input.question,
    limit: 16,
  });
  for (const item of uniqueIndexedEvidence([...indexedEvidence, ...semanticEvidence])) {
    const student = fullRoster.find(candidate => candidate.studentId === item.studentId);
    evidence.push({
      sourceId: item.sourceId,
      studentId: item.studentId,
      studentName: student?.name,
      kind: `indexed:${item.eventType}`,
      summary: {
        occurredDateUtc: item.occurredDateUtc,
        signal: item.signal,
        severity: item.severity,
        terms: item.terms,
        errorTags: item.errorTags,
        ...item.summary,
      },
      createdAtUtc: item.occurredAtUtc,
    });
  }
  await pushClassMissions(evidence, input.schoolId, input.classId);
  await Promise.all(roster.map(student => pushStudentEvidence(evidence, input.schoolId, student, Boolean(inferredStudentId))));

  const boundedEvidence = evidence
    .filter(item => item.sourceId && item.kind)
    .slice(0, maximumEvidenceItems);
  return clampEvidencePack({
    schoolId: input.schoolId,
    classId: input.classId,
    className: trimmedString(classData.name, 120) || input.classId,
    scope: inferredStudentId ? "student" : "class",
    question: input.question,
    generatedAtUtc: new Date().toISOString(),
    roster: roster.map(student => ({ studentId: student.studentId, name: student.name })),
    evidence: boundedEvidence,
  });
}

function studentIdMentionedInQuestion(question: string, roster: Array<{ studentId: string; name: string }>) {
  const normalizedQuestion = question.toLowerCase().replace(/\s+/g, " ").trim();
  if (!normalizedQuestion) return "";
  const match = roster.find(student => {
    const normalizedName = student.name.toLowerCase().replace(/\s+/g, " ").trim();
    return normalizedName.length >= 3 && normalizedQuestion.includes(normalizedName);
  });
  return match?.studentId ?? "";
}

function uniqueIndexedEvidence<T extends { sourceId: string }>(items: T[]) {
  return [...new Map(items.map(item => [item.sourceId, item])).values()];
}

export async function generateTeacherAssistantResponse(
  apiKey: string,
  evidencePack: TeacherAssistantEvidencePack,
): Promise<TeacherAssistantResponse> {
  const startedAt = Date.now();
  const systemInstruction = [
    "You are a teacher-facing learning analyst for an Indian primary-school English learning game.",
    "Answer only for the authorized class/student evidence in the JSON pack.",
    "Do not invent facts, diagnoses, disabilities, or parent-sensitive claims. Say when evidence is thin.",
    "Use concise professional English for a busy teacher.",
    "Ground every important claim in sourceIds from the evidence pack.",
    "When asked for planning, suggest practical classroom actions, mission overrides, or grouping ideas.",
    "Prefer tool:* evidence for class-level count, date-range, and grouping questions.",
    "Never expose raw child secrets or personal contact details. Student names from the roster are allowed.",
  ].join(" ");
  const schema = {
    type: "object",
    properties: {
      answer: { type: "string" },
      suggestedActions: {
        type: "array",
        minItems: 1,
        maxItems: 6,
        items: { type: "string" },
      },
      citations: {
        type: "array",
        minItems: 1,
        maxItems: 10,
        items: { type: "string" },
      },
      agentTrace: {
        type: "array",
        minItems: 3,
        maxItems: 5,
        items: { type: "string" },
      },
    },
    required: ["answer", "suggestedActions", "citations", "agentTrace"],
  };
  const requestBody = {
    systemInstruction: { parts: [{ text: systemInstruction }] },
    contents: [{
      role: "user",
      parts: [{ text: `Available MCP-style learning tools:\n${JSON.stringify(teacherLearningToolSchemas)}\n\nTeacher question and retrieved evidence:\n${JSON.stringify(evidencePack)}` }],
    }],
    generationConfig: {
      temperature: 0.15,
      maxOutputTokens: teacherAssistantMaxOutputTokens,
      responseMimeType: "application/json",
      responseSchema: schema,
      thinkingConfig: { thinkingBudget: 0 },
    },
    safetySettings: [
      { category: "HARM_CATEGORY_HARASSMENT", threshold: "BLOCK_LOW_AND_ABOVE" },
      { category: "HARM_CATEGORY_HATE_SPEECH", threshold: "BLOCK_LOW_AND_ABOVE" },
      { category: "HARM_CATEGORY_SEXUALLY_EXPLICIT", threshold: "BLOCK_LOW_AND_ABOVE" },
      { category: "HARM_CATEGORY_DANGEROUS_CONTENT", threshold: "BLOCK_LOW_AND_ABOVE" },
    ],
  };

  const endpoint = `https://generativelanguage.googleapis.com/v1beta/models/${encodeURIComponent(teacherAssistantModel)}:generateContent`;
  let response: Response;
  let raw = "";
  try {
    response = await fetch(endpoint, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "x-goog-api-key": apiKey,
      },
      body: JSON.stringify(requestBody),
    });
    raw = await response.text();
  } catch {
    return fallbackTeacherAssistantResponse(evidencePack, "provider_error");
  }
  if (!response.ok) {
    console.warn("[TeacherAssistant] Gemini request failed", { status: response.status, body: raw.slice(0, 300) });
    return fallbackTeacherAssistantResponse(evidencePack, response.status === 429 ? "provider_rate_limited" : "provider_error");
  }

  const payload = parseJsonRecord(raw);
  const candidates = Array.isArray(payload.candidates) ? payload.candidates : [];
  const candidate = cloneRecord(candidates[0]);
  const content = cloneRecord(candidate.content);
  const parts = Array.isArray(content.parts) ? content.parts : [];
  const responseText = parts
    .map(part => trimmedString(cloneRecord(part).text, 5000))
    .filter(Boolean)
    .join("\n");
  const output = parseTeacherAssistantJson(responseText);
  if (!output) {
    return fallbackTeacherAssistantResponse(evidencePack, "invalid_provider_response");
  }
  const citations = normalizeCitationList(output.citations, evidencePack);
  const analystResponse: TeacherAssistantResponse = {
    answer: redactBuddyText(output.answer, 1800) || "I found evidence, but could not form a reliable answer.",
    suggestedActions: normalizeTextList(output.suggestedActions, 6, 240),
    citations,
    agentTrace: normalizeTextList(output.agentTrace, 5, 160),
    model: teacherAssistantModel,
    usage: {
      analyst: cloneRecord(payload.usageMetadata),
      latencyMs: Date.now() - startedAt,
    },
  };
  if (!teacherAssistantVerifierEnabled) return analystResponse;
  return verifyTeacherAssistantResponse(apiKey, evidencePack, analystResponse, startedAt);
}

export async function verifyTeacherAssistantResponse(
  apiKey: string,
  evidencePack: TeacherAssistantEvidencePack,
  analystResponse: TeacherAssistantResponse,
  startedAt = Date.now(),
): Promise<TeacherAssistantResponse> {
  const allowedCitationSet = new Set(evidencePack.evidence.map(item => item.sourceId));
  const citedEvidence = evidencePack.evidence.filter(item => analystResponse.citations.includes(item.sourceId)).slice(0, 16);
  const compactEvidence = citedEvidence.length ? citedEvidence : evidencePack.evidence.slice(0, 16);
  const schema = {
    type: "object",
    properties: {
      approved: { type: "boolean" },
      answer: { type: "string" },
      suggestedActions: {
        type: "array",
        minItems: 1,
        maxItems: 6,
        items: { type: "string" },
      },
      citations: {
        type: "array",
        minItems: 1,
        maxItems: 10,
        items: { type: "string" },
      },
      verificationNotes: {
        type: "array",
        minItems: 1,
        maxItems: 4,
        items: { type: "string" },
      },
    },
    required: ["approved", "answer", "suggestedActions", "citations", "verificationNotes"],
  };
  const requestBody = {
    systemInstruction: {
      parts: [{
        text: [
          "You are a verifier agent for a teacher assistant.",
          "Check whether the draft answer is fully supported by the provided evidence.",
          "Remove unsupported claims, keep useful supported claims, and cite only allowed sourceIds.",
          "If evidence is thin, say that plainly.",
          "Do not add new facts beyond the evidence.",
        ].join(" "),
      }],
    },
    contents: [{
      role: "user",
      parts: [{
        text: JSON.stringify({
          question: evidencePack.question,
          draft: {
            answer: analystResponse.answer,
            suggestedActions: analystResponse.suggestedActions,
            citations: analystResponse.citations,
          },
          evidence: compactEvidence,
          allowedCitationIds: Array.from(allowedCitationSet).slice(0, 140),
        }),
      }],
    }],
    generationConfig: {
      temperature: 0,
      maxOutputTokens: Math.min(teacherAssistantMaxOutputTokens, 700),
      responseMimeType: "application/json",
      responseSchema: schema,
      thinkingConfig: { thinkingBudget: 0 },
    },
  };
  const endpoint = `https://generativelanguage.googleapis.com/v1beta/models/${encodeURIComponent(teacherAssistantModel)}:generateContent`;
  let raw = "";
  try {
    const response = await fetch(endpoint, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "x-goog-api-key": apiKey,
      },
      body: JSON.stringify(requestBody),
    });
    raw = await response.text();
    if (!response.ok) {
      return {
        ...analystResponse,
        agentTrace: appendTrace(analystResponse.agentTrace, "Verifier agent was unavailable; analyst answer kept with citation filtering."),
      };
    }
  } catch {
    return {
      ...analystResponse,
      agentTrace: appendTrace(analystResponse.agentTrace, "Verifier agent failed; analyst answer kept with citation filtering."),
    };
  }
  const payload = parseJsonRecord(raw);
  const candidate = cloneRecord((Array.isArray(payload.candidates) ? payload.candidates : [])[0]);
  const content = cloneRecord(candidate.content);
  const responseText = (Array.isArray(content.parts) ? content.parts : [])
    .map(part => trimmedString(cloneRecord(part).text, 5000))
    .filter(Boolean)
    .join("\n");
  const verified = parseTeacherAssistantJson(responseText);
  if (!verified) {
    return {
      ...analystResponse,
      agentTrace: appendTrace(analystResponse.agentTrace, "Verifier agent returned an invalid shape; analyst answer kept with citation filtering."),
    };
  }
  const citations = normalizeCitationList(verified.citations, evidencePack);
  const answer = redactBuddyText(verified.answer, 1800) || analystResponse.answer;
  const suggestedActions = normalizeTextList(verified.suggestedActions, 6, 240);
  const notes = normalizeTextList(verified.verificationNotes, 4, 160);
  return {
    answer,
    suggestedActions: suggestedActions.length ? suggestedActions : analystResponse.suggestedActions,
    citations,
    agentTrace: appendTrace([
      ...analystResponse.agentTrace,
      ...notes.map(note => `Verifier agent: ${note}`),
    ], verified.approved === false ? "Verifier agent revised unsupported claims." : "Verifier agent approved grounded answer."),
    model: `${teacherAssistantModel}+verifier`,
    usage: {
      ...cloneRecord(analystResponse.usage),
      verifier: cloneRecord(payload.usageMetadata),
      latencyMs: Date.now() - startedAt,
    },
  };
}

export function parseTeacherAssistantJson(value: string): Record<string, unknown> | null {
  const candidates = [
    value,
    value.replace(/^```(?:json)?\s*/i, "").replace(/\s*```$/i, "").trim(),
    extractFirstJsonObject(value),
  ];
  for (const candidate of candidates) {
    if (!candidate) continue;
    try {
      const parsed = JSON.parse(candidate);
      if (parsed && typeof parsed === "object" && !Array.isArray(parsed)) {
        return parsed as Record<string, unknown>;
      }
    } catch {
      // Try the next tolerated model-output shape.
    }
  }
  return null;
}

export function fallbackTeacherAssistantResponse(
  evidencePack: TeacherAssistantEvidencePack,
  fallbackReason: string,
): TeacherAssistantResponse {
  const studentCount = evidencePack.roster.length;
  const highPriority = evidencePack.evidence.filter(item => (
    item.kind === "recommendation" &&
    String(item.summary.priority ?? "") === "high"
  ));
  const needs = topValues(evidencePack.evidence.flatMap(item => valuesFrom(item.summary.needConceptIds)));
  const errors = topValues(evidencePack.evidence.flatMap(item => valuesFrom(item.summary.recurringErrorTags || item.summary.errorTags)));
  const citations = evidencePack.evidence.slice(0, 6).map(item => item.sourceId);
  const answer = [
    evidencePack.scope === "student"
      ? `I reviewed the available evidence for ${evidencePack.roster[0]?.name ?? "this student"}.`
      : `I reviewed ${studentCount} learner${studentCount === 1 ? "" : "s"} in ${evidencePack.className}.`,
    highPriority.length ? `${highPriority.length} high-priority recommendation${highPriority.length === 1 ? "" : "s"} need attention.` : "There are no high-priority recommendations in the retrieved window.",
    needs.length ? `Most visible concept needs: ${needs.join(", ")}.` : "",
    errors.length ? `Recurring error tags: ${errors.join(", ")}.` : "",
  ].filter(Boolean).join(" ");
  return {
    answer,
    suggestedActions: [
      needs[0] ? `Plan a short reteach group around ${needs[0]}.` : "Review the latest recommendations before changing tomorrow's mission.",
      errors[0] ? `Use two examples that isolate ${errors[0]} and collect one new exit-ticket attempt.` : "Ask for one more evidence sample if the pattern is unclear.",
    ],
    citations,
    agentTrace: [
      "Scope agent verified the requested class/student boundary.",
      "Planner agent selected learning-intelligence tools for the question.",
      "Retrieval agent collected tool, learner, recommendation, run, and skill evidence.",
      "Analyst agent summarized visible needs from bounded evidence.",
      "Critic agent limited claims to available source IDs.",
    ],
    model: "deterministic-fallback",
    fallbackReason,
  };
}

async function readStudent(schoolId: string, studentId: string): Promise<{ studentId: string; name: string }> {
  const snapshot = await db.doc(`schools/${schoolId}/students/${studentId}`).get();
  const data = snapshot.data() ?? {};
  return {
    studentId,
    name: trimmedString(data.name, 120) || studentId,
  };
}

async function readClassRoster(schoolId: string, classId: string): Promise<Array<{ studentId: string; name: string }>> {
  const snapshots = await db.collection(`schools/${schoolId}/students`)
    .where("classId", "==", classId)
    .limit(30)
    .get();
  return snapshots.docs.map(doc => {
    const data = doc.data();
    return {
      studentId: doc.id,
      name: trimmedString(data.name, 120) || doc.id,
    };
  });
}

async function pushClassMissions(evidence: TeacherAssistantEvidenceItem[], schoolId: string, classId: string): Promise<void> {
  const snapshots = await db.collection(`schools/${schoolId}/classes/${classId}/dailyMissions`)
    .orderBy("date", "desc")
    .limit(3)
    .get()
    .catch(() => ({ docs: [] }));
  for (const doc of snapshots.docs) {
    const data = cloneRecord(doc.data());
    evidence.push({
      sourceId: `schools/${schoolId}/classes/${classId}/dailyMissions/${doc.id}`,
      kind: "dailyMission",
      summary: pickFields(data, ["date", "missionType", "missionDurationSeconds", "lettersForToday", "wordsForToday", "revisionLetters"]),
    });
  }
}

async function pushStudentEvidence(
  evidence: TeacherAssistantEvidenceItem[],
  schoolId: string,
  student: { studentId: string; name: string },
  rich: boolean,
): Promise<void> {
  const basePath = `schools/${schoolId}/students/${student.studentId}`;
  const stateSnapshot = await db.doc(`${basePath}/buddyLearnerState/current`).get().catch(() => null);
  if (stateSnapshot?.exists) {
    const data = stateSnapshot.data() ?? {};
    evidence.push({
      sourceId: `${basePath}/buddyLearnerState/current`,
      studentId: student.studentId,
      studentName: student.name,
      kind: "buddyLearnerState",
      summary: {
        supportBand: data.supportBand,
        recommendedEnglishRatio: data.recommendedEnglishRatio,
        sourceEventCount: data.sourceEventCount,
        strengthConceptIds: valuesFrom(data.strengthConceptIds).slice(0, 5),
        needConceptIds: valuesFrom(data.needConceptIds).slice(0, 5),
        recurringErrorTags: valuesFrom(data.recurringErrorTags).slice(0, 6),
      },
      createdAtUtc: trimmedString(data.lastEventAtUtc, 80),
    });
  }

  await Promise.all([
    pushRecentCollection(evidence, basePath, student, "recommendations", "createdAt", rich ? 5 : 2, ["priority", "title", "detail", "createdAt"]),
    pushRecentCollection(evidence, basePath, student, "runSessions", "endedAtUtc", rich ? 6 : 3, ["missionId", "actualDurationSeconds", "completed", "averageConfidence", "averageAttemptsPerLetter", "grammarErrors", "pronunciationRetries", "lettersPracticed", "wordsPracticed", "endedAt"]),
    pushRecentCollection(evidence, basePath, student, "spokenPhraseEvents", "createdAtUtc", rich ? 8 : 2, ["phrase", "accepted", "grammarPattern", "conceptId", "errorCategory", "correctedResponse", "pronunciationInsight", "createdAtUtc"]),
    pushRecentCollection(evidence, basePath, student, "writtenPhraseEvents", "createdAtUtc", rich ? 8 : 2, ["phrase", "accepted", "grammarPattern", "conceptId", "errorCategory", "handwritingDiagnostics", "createdAtUtc"]),
    pushRecentCollection(evidence, basePath, student, "grammarBattleEvents", "createdAtUtc", rich ? 8 : 2, ["playerPhrase", "accepted", "grammarPattern", "conceptId", "outcome", "errorCategory", "createdAtUtc"]),
    pushRecentCollection(evidence, basePath, student, "gymAttempts", "createdAtUtc", rich ? 8 : 2, ["conceptId", "passed", "score", "attemptCount", "errorTags", "createdAtUtc"]),
    pushRecentCollection(evidence, basePath, student, "wordCastEvents", "createdAtUtc", rich ? 8 : 2, ["word", "success", "pronunciationInsight", "createdAtUtc"]),
    pushRecentCollection(evidence, basePath, student, "letterAttempts", "createdAtUtc", rich ? 8 : 2, ["letter", "confident", "attempts", "handwritingDiagnostics", "createdAtUtc"]),
    pushRecentCollection(evidence, basePath, student, "buddyConversationTurns", "createdAtUtc", rich ? 6 : 1, ["learnerMessage", "buddyResponse", "teacherNote", "conversationSkill", "errorCategory", "createdAtUtc"]),
  ]);
}

async function pushRecentCollection(
  evidence: TeacherAssistantEvidenceItem[],
  basePath: string,
  student: { studentId: string; name: string },
  collectionName: string,
  orderField: string,
  maximum: number,
  fields: string[],
): Promise<void> {
  const snapshots = await db.collection(`${basePath}/${collectionName}`)
    .orderBy(orderField, "desc")
    .limit(maximum)
    .get()
    .catch(() => ({ docs: [] }));
  for (const doc of snapshots.docs) {
    const data = cloneRecord(doc.data());
    evidence.push({
      sourceId: `${basePath}/${collectionName}/${doc.id}`,
      studentId: student.studentId,
      studentName: student.name,
      kind: collectionName.replace(/s$/, ""),
      summary: summarizeFields(data, fields),
      createdAtUtc: trimmedString(data.createdAtUtc || data.createdAt || data.endedAtUtc || data.endedAt, 80),
    });
  }
}

function summarizeFields(data: Record<string, unknown>, fields: string[]): Record<string, unknown> {
  const result = pickFields(data, fields);
  if (cloneRecord(result.pronunciationInsight).segments) {
    const insight = cloneRecord(result.pronunciationInsight);
    result.pronunciationInsight = {
      summary: insight.summary,
      errorType: insight.errorType,
      score: finiteNumber(insight.score || insight.pronScore || insight.accuracyScore),
      segments: Array.isArray(insight.segments) ? insight.segments.slice(0, 4) : [],
    };
  }
  if (cloneRecord(result.handwritingDiagnostics).tags) {
    const diagnostic = cloneRecord(result.handwritingDiagnostics);
    result.handwritingDiagnostics = {
      severity: finiteNumber(diagnostic.severity),
      tags: valuesFrom(diagnostic.tags).slice(0, 5),
      primaryHint: trimmedString(diagnostic.primaryHint, 160),
    };
  }
  return result;
}

function pickFields(data: Record<string, unknown>, fields: string[]): Record<string, unknown> {
  const result: Record<string, unknown> = {};
  for (const field of fields) {
    if (data[field] !== undefined && data[field] !== null && data[field] !== "") {
      result[field] = data[field];
    }
  }
  return result;
}

function clampEvidencePack(pack: TeacherAssistantEvidencePack): TeacherAssistantEvidencePack {
  let evidence = pack.evidence;
  while (JSON.stringify({ ...pack, evidence }).length > maximumEvidenceCharacters && evidence.length > 20) {
    evidence = evidence.slice(0, Math.floor(evidence.length * 0.85));
  }
  return { ...pack, evidence };
}

function parseJsonRecord(value: string): Record<string, unknown> {
  try {
    const parsed = JSON.parse(value);
    return cloneRecord(parsed);
  } catch {
    return {};
  }
}

function extractFirstJsonObject(value: string) {
  const start = value.indexOf("{");
  const end = value.lastIndexOf("}");
  if (start < 0 || end <= start) return "";
  return value.slice(start, end + 1).trim();
}

function normalizeTextList(value: unknown, maximum: number, maximumCharacters: number): string[] {
  if (!Array.isArray(value)) return [];
  return value
    .map(item => redactBuddyText(item, maximumCharacters))
    .filter(Boolean)
    .slice(0, maximum);
}

function normalizeCitationList(value: unknown, evidencePack: TeacherAssistantEvidencePack): string[] {
  const allowed = new Set(evidencePack.evidence.map(item => item.sourceId));
  const citations = normalizeTextList(value, 10, 260).filter(item => allowed.has(item));
  return citations.length ? citations : evidencePack.evidence.slice(0, 3).map(item => item.sourceId);
}

function appendTrace(trace: string[], item: string) {
  return [...trace, item].filter(Boolean).slice(-6);
}

function valuesFrom(value: unknown): string[] {
  return Array.isArray(value)
    ? value.map(item => trimmedString(item, 120)).filter(Boolean)
    : [];
}

function topValues(values: string[]): string[] {
  const counts = new Map<string, number>();
  for (const value of values) {
    counts.set(value, (counts.get(value) ?? 0) + 1);
  }
  return Array.from(counts.entries())
    .sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]))
    .slice(0, 5)
    .map(([value]) => value);
}
