import assert from "node:assert/strict";
import test from "node:test";
import { normalizeTeacherEvidence } from "../teacherIntelligence.js";

test("normalizes dated pronunciation struggles into exact instructional terms", () => {
  const record = normalizeTeacherEvidence({
    sourceId: "schools/school-a/students/s1/wordCastEvents/w1",
    schoolId: "school-a",
    classId: "lkg-a",
    studentId: "s1",
    eventType: "wordCastEvents",
    data: {
      word: "three",
      success: false,
      createdAtUtc: "2026-03-12T09:15:00.000Z",
      pronunciationInsight: { errorType: "TH_substitution", score: 42 },
    },
  });

  assert.equal(record.signal, "struggle");
  assert.equal(record.occurredDateUtc, "2026-03-12");
  assert.ok(record.terms.includes("three"));
  assert.ok(record.errorTags.includes("TH_substitution"));
  assert.match(record.semanticText, /three/i);
});

test("semantic evidence never stores raw learner or buddy conversation text", () => {
  const record = normalizeTeacherEvidence({
    sourceId: "schools/school-a/students/s1/buddyConversationTurns/t1",
    schoolId: "school-a",
    classId: "lkg-a",
    studentId: "s1",
    eventType: "buddyConversationTurns",
    data: {
      learnerMessage: "My home address is 42 Secret Lane.",
      buddyResponse: "Thanks for telling me your address.",
      teacherNote: "Practise plural nouns with picture cards.",
      conversationSkill: "plural_nouns",
      errorCategory: "plural_s_missing",
      createdAtUtc: "2026-03-13T10:00:00.000Z",
    },
  });

  assert.doesNotMatch(record.semanticText, /Secret Lane|address/i);
  assert.match(record.semanticText, /plural_nouns/i);
  assert.match(record.semanticText, /plural_s_missing/i);
});
