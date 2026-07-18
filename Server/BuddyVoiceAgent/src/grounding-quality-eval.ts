import { initializeLogger, llm } from '@livekit/agents';
import { performance } from 'node:perf_hooks';
import { loadEnvironment, readBuddyVoiceConfig } from './config.js';
import { createSarvamLlm } from './providers.js';

interface EvalCase {
  id: string;
  learnerInput: string;
  baselineContext: string;
  groundingContext: string;
  expectedSignals: RegExp[];
  leakageSignals: RegExp[];
}

interface EvalRun {
  variant: 'baseline' | 'grounded';
  caseId: string;
  reply: string;
  firstTokenMs: number;
  totalMs: number;
  estimatedInputTokens: number;
  estimatedOutputTokens: number;
  qualityScore: number;
  leakedAnswer: boolean;
}

loadEnvironment();
initializeLogger({ pretty: false, level: 'warn' });
const config = readBuddyVoiceConfig();

const cases: EvalCase[] = [
  {
    id: 'article-an-route-clue',
    learnerInput: 'I said a owl. Buddy, what should I change?',
    baselineContext: [
      'Concept: Articles.',
      'Grammar pattern: DeterminerNoun.',
      'Current task prompt: Use an before a vowel sound. Say: [current answer].',
      'Zone: Route. Never reveal, spell, complete, reorder, or quote the exact task answer. Give only a clue or micro-example.',
      'Learner support band: Foundation; target English ratio: 0.3.',
      'Recurring error patterns: article_vowel_sound_confusion.',
    ].join(' '),
    groundingContext: [
      'Curriculum: Articles; Use a before consonant sounds, an before vowel sounds, and the for a specific known noun.',
      'Examples: a rat; [current answer]; the dog.',
      'Common mistakes: a owl -> [current answer]; an rat -> a rat.',
      'Task scaffold: FillInBlank; ____ owl.',
      'Buddy contract: give a hint without over-solving; must not validate_answers_directly, bypass_gym_restrictions.',
      'Learner pattern: concept attempts: 11, mastery: 0.38, hint dependency: 0.64; recurring errors: article_vowel_sound_confusion.',
    ].join(' '),
    expectedSignals: [/vowel/i, /sound/i, /before/i],
    leakageSignals: [/\ban owl\b/i],
  },
  {
    id: 'preposition-word-meaning',
    learnerInput: 'What does behind mean here?',
    baselineContext: [
      'Concept: Basic Prepositions.',
      'Grammar pattern: FullSentence.',
      'Current task prompt: The route transcript lost the place word: rat run ____ the rock.',
      'Zone: Town. You may give the answer only after first giving a clue.',
      'Learner support band: Guided; target English ratio: 0.45.',
    ].join(' '),
    groundingContext: [
      'Curriculum: Basic Prepositions; Use the location word that matches the scene: in, on, under, behind, near, beside, or over.',
      'Examples: dog behind the rock; cat near the rock; rat beside the wall.',
      'Common mistakes: behind rock -> behind the rock; beside and behind are different positions.',
      'Task scaffold: FillInBlank; rat run ____ the rock.',
      'Learner pattern: recurring errors: missing_preposition_object.',
    ].join(' '),
    expectedSignals: [/back|पीछे|behind/i, /rock|object|thing/i, /example|जैसे/i],
    leakageSignals: [],
  },
  {
    id: 'verb-form-route-clue',
    learnerInput: 'I wrote the rat bite. Is it okay?',
    baselineContext: [
      'Concept: Verbs and Present Actions.',
      'Grammar pattern: NounVerbPresent.',
      'Current task prompt: A rat can bite. Say the whole action without me giving the full phrase.',
      'Zone: Route. Never reveal, spell, complete, reorder, or quote the exact task answer. Give only a clue or micro-example.',
      'Learner support band: Guided; target English ratio: 0.45.',
      'Recurring error patterns: third_person_s.',
    ].join(' '),
    groundingContext: [
      'Curriculum: Verbs and Present Actions; Use the action that fits the subject. One noun usually adds s or es.',
      'Examples: [current answer]; The dog runs; They bite.',
      'Common mistakes: the rat bite -> [current answer]; he run -> he runs.',
      'Task scaffold: AuthoredSubtitle; A rat can bite. Say the whole action.',
      'Buddy contract: guide toward a correct English answer using friendly local-language support; must not validate_answers_directly.',
      'Learner pattern: recurring errors: third_person_s.',
    ].join(' '),
    expectedSignals: [/one noun|singular|एक/i, /\bs\b|\bes\b|add/i, /verb|action/i],
    leakageSignals: [/\brat bites\b/i],
  },
];

const runs: EvalRun[] = [];
for (const evalCase of cases) {
  runs.push(await runCase(evalCase, 'baseline'));
  runs.push(await runCase(evalCase, 'grounded'));
}

const grouped = cases.map(evalCase => {
  const baseline = runs.find(run => run.caseId === evalCase.id && run.variant === 'baseline')!;
  const grounded = runs.find(run => run.caseId === evalCase.id && run.variant === 'grounded')!;
  return {
    caseId: evalCase.id,
    baseline: summarizeRun(baseline),
    grounded: summarizeRun(grounded),
    delta: {
      qualityScore: grounded.qualityScore - baseline.qualityScore,
      inputTokens: grounded.estimatedInputTokens - baseline.estimatedInputTokens,
      totalMs: Math.round(grounded.totalMs - baseline.totalMs),
    },
  };
});

const baselineRuns = runs.filter(run => run.variant === 'baseline');
const groundedRuns = runs.filter(run => run.variant === 'grounded');
console.log(JSON.stringify({
  model: config.llmModel,
  cases: grouped,
  averages: {
    baseline: averageSummary(baselineRuns),
    grounded: averageSummary(groundedRuns),
    delta: {
      qualityScore: average(groundedRuns.map(run => run.qualityScore)) - average(baselineRuns.map(run => run.qualityScore)),
      inputTokens: average(groundedRuns.map(run => run.estimatedInputTokens)) - average(baselineRuns.map(run => run.estimatedInputTokens)),
      totalMs: average(groundedRuns.map(run => run.totalMs)) - average(baselineRuns.map(run => run.totalMs)),
    },
  },
}, null, 2));

async function runCase(evalCase: EvalCase, variant: EvalRun['variant']): Promise<EvalRun> {
  const context = variant === 'grounded'
    ? `${evalCase.baselineContext} Grounding context: ${evalCase.groundingContext}`
    : evalCase.baselineContext;
  const prompt = [
    'You are Buddy, a warm voice-first grammar tutor for primary-school learners in India.',
    'Reply in one or two short natural Hinglish sentences.',
    'Use English for grammar terms and examples, Hindi for warmth and explanation.',
    'Do not use markdown, labels, lists, emoji, SSML, IPA, or stage directions.',
    'When Zone is Route, do not reveal, quote, spell, or complete the exact task answer.',
    'For Route corrections, do not say the corrected phrase after "try saying", "say", or similar wording.',
    'For Route corrections, point to the rule and use a different-example micro-example when helpful.',
    context,
  ].join(' ');
  const chatContext = new llm.ChatContext();
  chatContext.addMessage({ role: 'system', content: prompt });
  chatContext.addMessage({ role: 'user', content: evalCase.learnerInput });

  const model = createSarvamLlm(config);
  const startedAt = performance.now();
  let firstTokenMs = -1;
  let reply = '';
  try {
    for await (const chunk of model.chat({ chatCtx: chatContext })) {
      const content = chunk.delta?.content;
      if (!content) continue;
      if (firstTokenMs < 0) firstTokenMs = performance.now() - startedAt;
      reply += content;
    }
  } finally {
    await model.aclose();
  }
  const totalMs = performance.now() - startedAt;
  return {
    variant,
    caseId: evalCase.id,
    reply: reply.trim(),
    firstTokenMs: Math.round(firstTokenMs),
    totalMs: Math.round(totalMs),
    estimatedInputTokens: estimateTokens(`${prompt} ${evalCase.learnerInput}`),
    estimatedOutputTokens: estimateTokens(reply),
    qualityScore: scoreReply(reply, evalCase),
    leakedAnswer: evalCase.leakageSignals.some(pattern => pattern.test(reply)),
  };
}

function scoreReply(reply: string, evalCase: EvalCase): number {
  const signalScore = evalCase.expectedSignals.reduce((score, pattern) => score + (pattern.test(reply) ? 1 : 0), 0);
  const concise = estimateTokens(reply) <= 70 ? 1 : 0;
  const noLeakage = evalCase.leakageSignals.some(pattern => pattern.test(reply)) ? 0 : 1;
  const noMarkdown = /[*#`\n]/.test(reply) ? 0 : 1;
  return signalScore + concise + noLeakage + noMarkdown;
}

function summarizeRun(run: EvalRun) {
  return {
    qualityScore: run.qualityScore,
    leakedAnswer: run.leakedAnswer,
    firstTokenMs: run.firstTokenMs,
    totalMs: run.totalMs,
    estimatedInputTokens: run.estimatedInputTokens,
    estimatedOutputTokens: run.estimatedOutputTokens,
    reply: run.reply,
  };
}

function averageSummary(values: EvalRun[]) {
  return {
    qualityScore: round(average(values.map(value => value.qualityScore))),
    firstTokenMs: Math.round(average(values.map(value => value.firstTokenMs))),
    totalMs: Math.round(average(values.map(value => value.totalMs))),
    estimatedInputTokens: Math.round(average(values.map(value => value.estimatedInputTokens))),
    estimatedOutputTokens: Math.round(average(values.map(value => value.estimatedOutputTokens))),
    leakageCount: values.filter(value => value.leakedAnswer).length,
  };
}

function average(values: number[]): number {
  return values.length ? values.reduce((sum, value) => sum + value, 0) / values.length : 0;
}

function round(value: number): number {
  return Math.round(value * 100) / 100;
}

function estimateTokens(value: string): number {
  const normalized = value.trim();
  if (!normalized) return 0;
  const latinWords = normalized.match(/[A-Za-z0-9]+/g)?.length ?? 0;
  const indicRuns = normalized.match(/[\u0900-\u097F]+/g)?.length ?? 0;
  const punctuation = normalized.match(/[.,:;!?()[\]{}]/g)?.length ?? 0;
  return Math.ceil(latinWords + indicRuns * 1.4 + punctuation * 0.25);
}
