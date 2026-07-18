import { FieldValue } from "firebase-admin/firestore";
import { FieldValue as FirestoreFieldValue } from "@google-cloud/firestore";
import { onDocumentCreated } from "firebase-functions/v2/firestore";
import { HttpsError, onCall } from "firebase-functions/v2/https";
import { assertSameSchool, assertStudentClassBinding, assertTeacherClassAccess, requireRole } from "./authorizationHelpers.js";
import { readGeminiApiKey } from "./buddyVoiceHelpers.js";
import { requiredTrimmedString, trimmedString } from "./inputHelpers.js";
import {
  db,
  geminiApiKey,
  teacherSemanticEmbeddingDimensions,
  teacherSemanticEmbeddingModel,
  teacherSemanticIndexEnabled,
} from "./runtime.js";
import { assertFirestorePathSegment, cloneRecord, finiteNumber, normalizedStringArray } from "./sharedUtils.js";

const indexedCollections = new Set([
  "runSessions",
  "letterAttempts",
  "wordCastEvents",
  "spokenPhraseEvents",
  "writtenPhraseEvents",
  "grammarBattleEvents",
  "gymAttempts",
  "buddyConversationTurns",
  "recommendations",
]);

const backfillCollectionOrder = [
  "runSessions",
  "letterAttempts",
  "wordCastEvents",
  "spokenPhraseEvents",
  "writtenPhraseEvents",
  "grammarBattleEvents",
  "gymAttempts",
  "buddyConversationTurns",
  "recommendations",
] as const;

export interface TeacherEvidenceRecord {
  sourceId: string;
  schoolId: string;
  classId: string;
  studentId: string;
  eventType: string;
  occurredDateUtc: string;
  occurredAtUtc: string;
  signal: "struggle" | "progress" | "neutral";
  severity: number;
  terms: string[];
  errorTags: string[];
  summary: Record<string, unknown>;
  semanticText: string;
}

// This trigger indexes new telemetry only. Raw student records remain the source of truth,
// while the derived index supports time-bounded and semantic teacher retrieval.
export const indexTeacherLearningEvidence = onDocumentCreated(
  {
    document: "schools/{schoolId}/students/{studentId}/{eventType}/{eventId}",
    secrets: [geminiApiKey],
    timeoutSeconds: 60,
    memory: "512MiB",
  },
  async event => {
    const eventType = String(event.params.eventType ?? "");
    if (!indexedCollections.has(eventType) || !event.data) return;

    const schoolId = String(event.params.schoolId ?? "");
    const studentId = String(event.params.studentId ?? "");
    const data = cloneRecord(event.data.data());
    const studentSnapshot = await db.doc(`schools/${schoolId}/students/${studentId}`).get();
    const classId = trimmedString(data.classId, 160) || trimmedString(studentSnapshot.data()?.classId, 160);
    if (!classId) return;

    const record = normalizeTeacherEvidence({
      sourceId: event.data.ref.path,
      schoolId,
      classId,
      studentId,
      eventType,
      data,
    });
    await writeTeacherEvidenceIndex(record, `${event.params.studentId}_${event.params.eventType}_${event.params.eventId}`);
  },
);

export const backfillTeacherLearningEvidence = onCall(
  {
    secrets: [geminiApiKey],
    timeoutSeconds: 540,
    memory: "1GiB",
  },
  async request => {
    const caller = requireRole(request.auth, ["admin", "teacher"]);
    const data = cloneRecord(request.data);
    const schoolId = requiredTrimmedString(data.schoolId, "schoolId");
    const classId = requiredTrimmedString(data.classId, "classId");
    const studentId = trimmedString(data.studentId, 160);
    const startUtc = trimmedString(data.startUtc, 40);
    const endUtc = trimmedString(data.endUtc, 40);
    const dryRun = data.dryRun === true;
    const maxStudents = boundedInputInteger(data.maxStudents, 30, 1, 120);
    const maxRecordsPerCollection = boundedInputInteger(data.maxRecordsPerCollection, 200, 1, 1000);

    assertFirestorePathSegment(schoolId, "schoolId");
    assertFirestorePathSegment(classId, "classId");
    if (studentId) assertFirestorePathSegment(studentId, "studentId");
    assertSameSchool(caller, schoolId);
    assertTeacherClassAccess(caller, classId);
    if (studentId) await assertStudentClassBinding(schoolId, classId, studentId);
    validateOptionalIsoDate(startUtc, "startUtc");
    validateOptionalIsoDate(endUtc, "endUtc");
    if (startUtc && endUtc && startUtc >= endUtc) {
      throw new HttpsError("invalid-argument", "startUtc must be before endUtc.");
    }

    const students = studentId
      ? [{ studentId, classId }]
      : await readBackfillRoster(schoolId, classId, maxStudents);
    let scannedRecordCount = 0;
    let eligibleRecordCount = 0;
    let indexedRecordCount = 0;
    const byCollection: Record<string, { scanned: number; eligible: number; indexed: number }> = {};

    for (const student of students) {
      for (const eventType of backfillCollectionOrder) {
        const collectionStats = byCollection[eventType] ?? { scanned: 0, eligible: 0, indexed: 0 };
        const snapshots = await db.collection(`schools/${schoolId}/students/${student.studentId}/${eventType}`)
          .limit(maxRecordsPerCollection)
          .get()
          .catch(() => ({ docs: [] }));
        for (const doc of snapshots.docs) {
          scannedRecordCount += 1;
          collectionStats.scanned += 1;
          const record = normalizeTeacherEvidence({
            sourceId: doc.ref.path,
            schoolId,
            classId: trimmedString(doc.data().classId, 160) || student.classId || classId,
            studentId: student.studentId,
            eventType,
            data: cloneRecord(doc.data()),
          });
          if (record.classId !== classId || !recordInDateRange(record, startUtc, endUtc)) continue;
          eligibleRecordCount += 1;
          collectionStats.eligible += 1;
          if (!dryRun) {
            await writeTeacherEvidenceIndex(record, `${student.studentId}_${eventType}_${doc.id}`);
            indexedRecordCount += 1;
            collectionStats.indexed += 1;
          }
        }
        byCollection[eventType] = collectionStats;
      }
    }

    return {
      ok: true,
      dryRun,
      schoolId,
      classId,
      studentCount: students.length,
      scannedRecordCount,
      eligibleRecordCount,
      indexedRecordCount,
      byCollection,
      warnings: [
        "Backfill reads historical source records and writes the derived teacherEvidence index used by class-level tools.",
        teacherSemanticIndexEnabled
          ? "TEACHER_SEMANTIC_INDEX_ENABLED is true: sanitized instructional summaries may be embedded with Gemini during this backfill."
          : "TEACHER_SEMANTIC_INDEX_ENABLED is false: this backfill populates structured evidence only and does not call Gemini for embeddings.",
      ],
    };
  },
);

export function normalizeTeacherEvidence(input: {
  sourceId: string;
  schoolId: string;
  classId: string;
  studentId: string;
  eventType: string;
  data: Record<string, unknown>;
}): TeacherEvidenceRecord {
  const data = input.data;
  const terms = uniqueTerms([
    trimmedString(data.word, 120),
    trimmedString(data.letter, 20),
    trimmedString(data.phrase, 240),
    trimmedString(data.targetText, 240),
    trimmedString(data.canonicalPhrase, 240),
    trimmedString(data.conversationSkill, 120),
    trimmedString(data.grammarPattern, 120),
    ...normalizedStringArray(data.wordsPracticed, 20),
    ...normalizedStringArray(data.lettersPracticed, 20),
    ...normalizedStringArray(data.needConceptIds, 12),
  ]);
  const errorTags = uniqueTerms([
    trimmedString(data.errorCategory, 120),
    ...normalizedStringArray(data.errorTags, 12),
    ...normalizedStringArray(cloneRecord(data.handwritingDiagnostics).tags, 12),
    trimmedString(cloneRecord(data.pronunciationInsight).errorType, 120),
  ]);
  const signal = learningSignal(data, errorTags);
  const occurredAtUtc = eventTimestamp(data);
  const summary = compactSummary(data, input.eventType, terms, errorTags, signal);
  const semanticText = semanticSummary(input.eventType, terms, errorTags, signal, summary);
  return {
    sourceId: input.sourceId,
    schoolId: input.schoolId,
    classId: input.classId,
    studentId: input.studentId,
    eventType: input.eventType,
    occurredDateUtc: occurredAtUtc.slice(0, 10),
    occurredAtUtc,
    signal,
    severity: learningSeverity(data, errorTags, signal),
    terms,
    errorTags,
    summary,
    semanticText,
  };
}

export async function retrieveStructuredTeacherEvidence(input: {
  schoolId: string;
  classId: string;
  studentId?: string;
  question: string;
  limit?: number;
}): Promise<TeacherEvidenceRecord[]> {
  const asksForStruggle = /struggl|difficult|mistake|error|weak|need help|not pass/i.test(input.question);
  let query = db.collection(`schools/${input.schoolId}/teacherEvidence`)
    .where("classId", "==", input.classId);
  if (input.studentId) query = query.where("studentId", "==", input.studentId);
  if (asksForStruggle) query = query.where("signal", "==", "struggle");
  const snapshots = await query.orderBy("occurredAtUtc", "desc").limit(input.limit ?? 40).get().catch(() => ({ docs: [] }));
  return snapshots.docs.map(doc => recordFromIndex(doc.data())).filter((record): record is TeacherEvidenceRecord => record != null);
}

export async function retrieveSemanticTeacherEvidence(input: {
  schoolId: string;
  classId: string;
  studentId?: string;
  question: string;
  limit?: number;
}): Promise<TeacherEvidenceRecord[]> {
  if (!teacherSemanticIndexEnabled) return [];
  const embedding = await embedTeacherText(input.question, "RETRIEVAL_QUERY");
  if (embedding.length !== teacherSemanticEmbeddingDimensions) return [];
  let query = db.collection(`schools/${input.schoolId}/teacherEvidence`).where("classId", "==", input.classId);
  if (input.studentId) query = query.where("studentId", "==", input.studentId);
  const snapshots = await query.findNearest({
    vectorField: "embedding",
    queryVector: embedding,
    limit: input.limit ?? 16,
    distanceMeasure: "COSINE",
    distanceResultField: "semanticDistance",
  }).get().catch(() => ({ docs: [] }));
  return snapshots.docs.map(doc => recordFromIndex(doc.data())).filter((record): record is TeacherEvidenceRecord => record != null);
}

async function updateDailyTeacherSummary(record: TeacherEvidenceRecord): Promise<void> {
  const ref = db.doc(`schools/${record.schoolId}/teacherDailySummaries/${record.studentId}_${record.occurredDateUtc}`);
  await db.runTransaction(async transaction => {
    const current = transaction.get(ref);
    const snapshot = await current;
    const data = snapshot.data() ?? {};
    const existingTerms = normalizedStringArray(data.struggleTerms, 80);
    const existingErrors = normalizedStringArray(data.errorTags, 80);
    transaction.set(ref, {
      schoolId: record.schoolId,
      classId: record.classId,
      studentId: record.studentId,
      dateUtc: record.occurredDateUtc,
      eventCount: Math.max(0, Number(data.eventCount ?? 0)) + 1,
      struggleCount: Math.max(0, Number(data.struggleCount ?? 0)) + (record.signal === "struggle" ? 1 : 0),
      progressCount: Math.max(0, Number(data.progressCount ?? 0)) + (record.signal === "progress" ? 1 : 0),
      struggleTerms: uniqueTerms(record.signal === "struggle" ? [...existingTerms, ...record.terms] : existingTerms).slice(0, 80),
      errorTags: uniqueTerms([...existingErrors, ...record.errorTags]).slice(0, 80),
      updatedAt: FieldValue.serverTimestamp(),
    }, { merge: true });
  });
}

async function writeTeacherEvidenceIndex(record: TeacherEvidenceRecord, indexId: string): Promise<void> {
  const indexRef = db.doc(`schools/${record.schoolId}/teacherEvidence/${indexId}`);
  const existingIndex = await indexRef.get();
  const indexData: Record<string, unknown> = {
    ...record,
    indexedAt: FieldValue.serverTimestamp(),
    embeddingModel: "",
  };

  // Only a sanitized instructional summary is embedded. Conversation transcripts,
  // raw handwriting strokes, audio, and contact data never enter this index.
  if (teacherSemanticIndexEnabled && record.semanticText) {
    const embedding = await embedTeacherEvidence(record.semanticText);
    if (embedding.length === teacherSemanticEmbeddingDimensions) {
      indexData.embedding = FirestoreFieldValue.vector(embedding);
      indexData.embeddingModel = teacherSemanticEmbeddingModel;
    }
  }
  await indexRef.set(indexData, { merge: true });
  if (!existingIndex.exists) {
    await updateDailyTeacherSummary(record);
  }
}

async function readBackfillRoster(schoolId: string, classId: string, maximum: number) {
  const snapshots = await db.collection(`schools/${schoolId}/students`)
    .where("classId", "==", classId)
    .limit(maximum)
    .get();
  return snapshots.docs.map(doc => ({ studentId: doc.id, classId }));
}

function recordInDateRange(record: TeacherEvidenceRecord, startUtc: string, endUtc: string) {
  return (!startUtc || record.occurredAtUtc >= startUtc) && (!endUtc || record.occurredAtUtc < endUtc);
}

function validateOptionalIsoDate(value: string, fieldName: string) {
  if (!value) return;
  if (!/^\d{4}-\d{2}-\d{2}T/.test(value) || Number.isNaN(Date.parse(value))) {
    throw new HttpsError("invalid-argument", `${fieldName} must be an ISO timestamp.`);
  }
}

function boundedInputInteger(value: unknown, fallback: number, minimum: number, maximum: number) {
  const parsed = Number(value ?? fallback);
  if (!Number.isFinite(parsed)) return fallback;
  return Math.max(minimum, Math.min(maximum, Math.floor(parsed)));
}

async function embedTeacherEvidence(text: string): Promise<number[]> {
  return embedTeacherText(text, "RETRIEVAL_DOCUMENT");
}

async function embedTeacherText(text: string, taskType: "RETRIEVAL_DOCUMENT" | "RETRIEVAL_QUERY"): Promise<number[]> {
  const apiKey = readGeminiApiKey();
  if (!apiKey) return [];
  try {
    const response = await fetch(
      `https://generativelanguage.googleapis.com/v1beta/models/${encodeURIComponent(teacherSemanticEmbeddingModel)}:embedContent`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json", "x-goog-api-key": apiKey },
        body: JSON.stringify({
          taskType,
          outputDimensionality: teacherSemanticEmbeddingDimensions,
          content: { parts: [{ text }] },
        }),
      },
    );
    if (!response.ok) return [];
    const payload = cloneRecord(await response.json());
    const embedding = cloneRecord(payload.embedding);
    const values = Array.isArray(embedding.values) ? embedding.values.map(value => finiteNumber(value)) : [];
    return values.length === teacherSemanticEmbeddingDimensions ? values : [];
  } catch {
    return [];
  }
}

function learningSignal(data: Record<string, unknown>, errorTags: string[]): TeacherEvidenceRecord["signal"] {
  if (data.accepted === false || data.success === false || data.passed === false || data.confident === false || errorTags.length) return "struggle";
  if (data.accepted === true || data.success === true || data.passed === true || data.completed === true) return "progress";
  return "neutral";
}

function learningSeverity(data: Record<string, unknown>, errorTags: string[], signal: TeacherEvidenceRecord["signal"]) {
  if (signal !== "struggle") return 0;
  const diagnostic = cloneRecord(data.handwritingDiagnostics);
  return Math.min(3, Math.max(1, Math.round(finiteNumber(diagnostic.severity)), errorTags.length ? 2 : 1));
}

function compactSummary(
  data: Record<string, unknown>,
  eventType: string,
  terms: string[],
  errorTags: string[],
  signal: TeacherEvidenceRecord["signal"],
) {
  return {
    eventType,
    signal,
    terms: terms.slice(0, 12),
    errorTags: errorTags.slice(0, 12),
    conceptId: trimmedString(data.conceptId, 120),
    grammarPattern: trimmedString(data.grammarPattern, 120),
    score: finiteNumber(data.score || cloneRecord(data.pronunciationInsight).score),
    attempts: finiteNumber(data.attempts || data.attemptCount),
  };
}

function semanticSummary(eventType: string, terms: string[], errorTags: string[], signal: TeacherEvidenceRecord["signal"], summary: Record<string, unknown>) {
  const pieces = [
    `Learning event: ${eventType}.`,
    `Outcome: ${signal}.`,
    terms.length ? `Instructional terms: ${terms.join(", ")}.` : "",
    errorTags.length ? `Observed error tags: ${errorTags.join(", ")}.` : "",
    trimmedString(summary.conceptId, 120) ? `Concept: ${trimmedString(summary.conceptId, 120)}.` : "",
    trimmedString(summary.grammarPattern, 120) ? `Grammar pattern: ${trimmedString(summary.grammarPattern, 120)}.` : "",
  ];
  return pieces.filter(Boolean).join(" ").slice(0, 1200);
}

function eventTimestamp(data: Record<string, unknown>) {
  for (const value of [data.createdAtUtc, data.endedAtUtc, data.occurredAtUtc, data.createdAt, data.endedAt, data.receivedAt]) {
    if (typeof value === "string" && /^\d{4}-\d{2}-\d{2}/.test(value)) return value.slice(0, 32);
    if (value && typeof value === "object" && "toDate" in value && typeof (value as { toDate?: unknown }).toDate === "function") {
      return (value as { toDate: () => Date }).toDate().toISOString();
    }
  }
  return new Date().toISOString();
}

function uniqueTerms(values: string[]) {
  return [...new Set(values.flatMap(value => value.split(/[;,]/)).map(value => value.trim()).filter(Boolean))].slice(0, 80);
}

function recordFromIndex(data: Record<string, unknown>): TeacherEvidenceRecord | null {
  const sourceId = trimmedString(data.sourceId, 512);
  const schoolId = trimmedString(data.schoolId, 160);
  const classId = trimmedString(data.classId, 160);
  const studentId = trimmedString(data.studentId, 160);
  if (!sourceId || !schoolId || !classId || !studentId) return null;
  const signal = data.signal === "struggle" || data.signal === "progress" ? data.signal : "neutral";
  return {
    sourceId, schoolId, classId, studentId,
    eventType: trimmedString(data.eventType, 80),
    occurredDateUtc: trimmedString(data.occurredDateUtc, 10),
    occurredAtUtc: trimmedString(data.occurredAtUtc, 40),
    signal,
    severity: finiteNumber(data.severity),
    terms: normalizedStringArray(data.terms, 80),
    errorTags: normalizedStringArray(data.errorTags, 80),
    summary: cloneRecord(data.summary),
    semanticText: trimmedString(data.semanticText, 1200),
  };
}
