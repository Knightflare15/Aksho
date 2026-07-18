import assert from "node:assert/strict";
import { performance } from "node:perf_hooks";
import test from "node:test";
import {
  composeBuddyGroundingContext,
  estimatePromptTokens,
  redactTaskAnswerText,
  type BuddyContextComposerInput,
} from "../buddyContextComposer.js";

function syntheticInput(): BuddyContextComposerInput {
  return {
    task: {
      id: "article-an",
      conceptId: "Articles",
      conceptTitle: "Articles",
      npcLine: "Use an before a vowel sound. Say: an owl.",
      displayTranscript: "____ owl",
      grammarPattern: "DeterminerNoun",
      buddyContractId: "adaptive_hint",
      scaffoldMode: "FillInBlank",
    },
    profile: {
      homeLanguage: "hi",
      targetLanguage: "en",
    },
    learnerState: {
      supportBand: "Foundation",
      recommendedEnglishRatio: 0.3,
      recurringErrorTags: ["article_vowel_sound_confusion", "drops_plural_s"],
      concepts: {
        Articles: {
          attempts: 11,
          masteryEstimate: 0.38,
          hintDependency: 0.64,
        },
      },
    },
    grimoirePage: {
      id: "Articles",
      conceptId: "Articles",
      title: "Articles",
      rule: "Use a before consonant sounds, an before vowel sounds, and the for a specific known noun.",
      examples: ["a rat", "an owl", "the dog"],
      commonGoofs: ["a owl -> an owl", "an rat -> a rat", "forgetting that article choice follows sound"],
    },
    practiceScaffold: {
      id: "article-an",
      taskId: "article-an",
      mode: "FillInBlank",
      prompt: "____ owl",
    },
    buddyContract: {
      id: "adaptive_hint",
      instruction: "Use deterministic scaffold, expected answer, and concept data to give a hint without over-solving gym checks.",
      forbiddenActions: ["validate_answers_directly", "bypass_gym_restrictions", "store_sensitive_personal_data"],
    },
  };
}

test("synthetic context improves grounding while staying within token budget", () => {
  const input = syntheticInput();
  const baseline = [
    input.task.conceptTitle,
    input.task.grammarPattern,
    input.task.npcLine,
    `Learner support band: ${input.learnerState.supportBand}`,
  ].join(" ");
  const grounded = composeBuddyGroundingContext(input);

  assert.match(grounded.groundingContext, /vowel sounds/i);
  assert.match(grounded.groundingContext, /Common mistakes/i);
  assert.match(grounded.groundingContext, /article_vowel_sound_confusion/i);
  assert.match(grounded.groundingContext, /must not/i);
  assert.deepEqual(grounded.groundingSourceIds, [
    "gameContentDialogueTasks/article-an",
    "gameContentGrimoirePages/Articles",
    "gameContentPracticeScaffolds/article-an",
    "gameContentBuddyContracts/adaptive_hint",
  ]);
  assert.ok(grounded.groundingContext.length <= 900);
  assert.ok(grounded.estimatedTokens <= 170);
  assert.ok(groundingQualityScore(grounded.groundingContext) > groundingQualityScore(baseline));
});

test("synthetic composer has negligible local CPU overhead", () => {
  const input = syntheticInput();
  const iterations = 2000;
  const startedAt = performance.now();
  for (let index = 0; index < iterations; index++) {
    composeBuddyGroundingContext(input);
  }
  const elapsedMs = performance.now() - startedAt;
  const averageMs = elapsedMs / iterations;

  assert.ok(averageMs < 0.25, `average composer time was ${averageMs.toFixed(4)} ms`);
});

test("partial-assist context redacts exact task answers before grounding", () => {
  const input = syntheticInput();
  input.task.assistMode = "Partial";
  input.task.expectedResponse = "An owl";
  input.grimoirePage = {
    ...input.grimoirePage,
    examples: ["a rat", "an owl"],
    commonGoofs: ["a owl -> an owl"],
  };
  const grounded = composeBuddyGroundingContext(input);
  assert.doesNotMatch(grounded.groundingContext, /\ban owl\b/i);
  assert.match(grounded.groundingContext, /\[current answer\]/);
});

test("partial-assist task prompt redacts exact task answers before metadata signing", () => {
  const input = syntheticInput();
  input.task.assistMode = "Partial";
  input.task.expectedResponse = "An owl";

  const prompt = redactTaskAnswerText(
    input.task,
    input.practiceScaffold ?? {},
    input.task.npcLine,
    400,
  );

  assert.doesNotMatch(prompt, /\ban owl\b/i);
  assert.match(prompt, /\[current answer\]/);
});

test("token estimator scales with concise and verbose context", () => {
  const concise = "Articles: use an before owl.";
  const verbose = `${concise} ${"example ".repeat(120)}`;
  assert.ok(estimatePromptTokens(concise) < estimatePromptTokens(verbose));
  assert.ok(estimatePromptTokens(verbose) < 150);
});

function groundingQualityScore(value: string): number {
  const checks = [
    /rule|vowel sound/i,
    /example|an owl/i,
    /common mistake|a owl/i,
    /scaffold|____ owl/i,
    /recurring errors|article_vowel_sound_confusion/i,
    /must not|forbidden/i,
    /gameContent[A-Za-z]+\/[A-Za-z0-9_-]+/,
  ];
  return checks.reduce((score, pattern) => score + (pattern.test(value) ? 1 : 0), 0);
}
