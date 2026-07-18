import assert from "node:assert/strict";
import test from "node:test";
import {
  fallbackTeacherAssistantResponse,
  parseTeacherAssistantJson,
  type TeacherAssistantEvidencePack,
} from "../teacherAssistant.js";

function evidencePack(): TeacherAssistantEvidencePack {
  return {
    schoolId: "school-a",
    classId: "lkg-a",
    className: "LKG A",
    scope: "class",
    question: "Who needs help with articles?",
    generatedAtUtc: "2026-07-16T00:00:00.000Z",
    roster: [
      { studentId: "s1", name: "Aarav" },
      { studentId: "s2", name: "Meera" },
    ],
    evidence: [
      {
        sourceId: "schools/school-a/students/s1/buddyLearnerState/current",
        studentId: "s1",
        studentName: "Aarav",
        kind: "buddyLearnerState",
        summary: {
          needConceptIds: ["Articles"],
          recurringErrorTags: ["article_vowel_sound_confusion"],
        },
      },
      {
        sourceId: "schools/school-a/students/s1/recommendations/r1",
        studentId: "s1",
        studentName: "Aarav",
        kind: "recommendation",
        summary: {
          priority: "high",
          title: "Review articles",
          detail: "Aarav is mixing a and an.",
        },
      },
    ],
  };
}

test("teacher assistant parser accepts fenced provider JSON", () => {
  const parsed = parseTeacherAssistantJson("```json\n{\"answer\":\"Use article practice\",\"citations\":[\"a\"]}\n```");
  assert.equal(parsed?.answer, "Use article practice");
  assert.deepEqual(parsed?.citations, ["a"]);
});

test("teacher assistant fallback stays evidence-grounded", () => {
  const response = fallbackTeacherAssistantResponse(evidencePack(), "provider_unavailable");
  assert.match(response.answer, /2 learners/i);
  assert.match(response.answer, /Articles/i);
  assert.match(response.answer, /article_vowel_sound_confusion/i);
  assert.equal(response.fallbackReason, "provider_unavailable");
  assert.ok(response.citations.includes("schools/school-a/students/s1/buddyLearnerState/current"));
  assert.ok(response.agentTrace.some(step => /Retrieval agent/i.test(step)));
});
