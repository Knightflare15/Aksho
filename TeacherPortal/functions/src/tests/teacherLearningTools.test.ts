import assert from "node:assert/strict";
import test from "node:test";
import { inferDateRangeUtc, planTeacherLearningTools } from "../teacherLearningTools.js";

test("planner routes class pronunciation struggle questions to the class pronunciation tool", () => {
  const plan = planTeacherLearningTools({
    question: "Who all were messing up their pronunciations last week from my class?",
    now: new Date("2026-07-16T12:00:00.000Z"),
  });
  assert.equal(plan.domain, "pronunciation");
  assert.equal(plan.dateRange.label, "last 7 days");
  assert.deepEqual(plan.calls.map(call => call.name), ["getClassPronunciationStruggles"]);
});

test("date parser returns bounded UTC windows for last week", () => {
  const range = inferDateRangeUtc("last week", new Date("2026-07-16T12:00:00.000Z"));
  assert.equal(range.startUtc, "2026-07-09T00:00:00.000Z");
  assert.equal(range.endUtc, "2026-07-16T00:00:00.000Z");
});
