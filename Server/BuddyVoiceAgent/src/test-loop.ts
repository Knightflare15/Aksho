import { initializeLogger, llm, stt, type VAD } from '@livekit/agents';
import * as silero from '@livekit/agents-plugin-silero';
import type { TTS } from '@livekit/agents-plugin-sarvam';
import { dispose, type AudioFrame } from '@livekit/rtc-node';
import path from 'node:path';
import { audioDurationMs, paceFrames, silenceFrames, writeWav } from './audio.js';
import { loadEnvironment, readBuddyVoiceConfig } from './config.js';
import { StreamingEmphasisAdapter } from './emphasis.js';
import { createSarvamLlm, createSarvamStt, createSarvamTts } from './providers.js';
import { StreamingTextChunker } from './text-chunker.js';
import { createVadGatedSttStream, type VadGateStats } from './vad-gated-stt.js';

interface LoopCase {
  id: string;
  language: string;
  text: string;
  expectedAny: string[];
  expectedResponseAny: string[];
  isolationTerms: string[];
  emphasisTerm: string;
}

interface LoopResult {
  id: string;
  inputText: string;
  transcript: string;
  audioMs: number;
  ttsFirstAudioMs: number;
  ttsTotalMs: number;
  sttFirstTranscriptMs: number;
  sttTotalMs: number;
  llmResponse: string;
  spokenResponse: string;
  llmFirstTokenMs: number;
  llmTotalMs: number;
  responseTtsFirstAudioMs: number;
  responseTtsTotalMs: number;
  responseAudioMs: number;
  gate: VadGateStats;
  transcriptMatched: boolean;
  responseMatched: boolean;
  responseStayedIsolated: boolean;
  emphasisApplied: boolean;
  emphasisMarkupRemoved: boolean;
  languageMix: LanguageMixStats;
  hinglishBalanced: boolean;
  passed: boolean;
}

interface LanguageMixStats {
  hindiWords: number;
  englishWords: number;
  englishRatio: number;
}

const cases: LoopCase[] = [
  {
    id: 'past-tense',
    language: 'hi-IN',
    text: 'नमस्ते Buddy, क्या तुम past tense समझने में मेरी मदद कर सकते हो?',
    expectedAny: ['past', 'tense', 'मदद', 'समझ'],
    expectedResponseAny: ['past', '-ed', 'verb'],
    isolationTerms: ['past tense', '-ed'],
    emphasisTerm: 'past tense',
  },
  {
    id: 'subject-verb-agreement',
    language: 'en-IN',
    text: 'Hello Buddy, should I say he walk or he walks? Give me one short grammar hint.',
    expectedAny: ['he', 'walk', 'walks', 'grammar'],
    expectedResponseAny: ['walks', 'singular', 'he'],
    isolationTerms: ['he walks', 'third-person singular'],
    emphasisTerm: 'walks',
  },
  {
    id: 'nouns',
    language: 'hi-IN',
    text: 'Buddy, संज्ञा क्या होती है? एक छोटा उदाहरण दो.',
    expectedAny: ['संज्ञा', 'उदाहरण', 'छोटा'],
    expectedResponseAny: ['संज्ञा', 'नाम', 'व्यक्ति', 'स्थान', 'वस्तु'],
    isolationTerms: ['संज्ञा'],
    emphasisTerm: 'संज्ञा',
  },
  {
    id: 'spatial-prepositions',
    language: 'en-IN',
    text: 'Buddy, please explain the difference between under and above in one short example.',
    expectedAny: ['under', 'above', 'example'],
    expectedResponseAny: ['under', 'above', 'below'],
    isolationTerms: ['under', 'above'],
    emphasisTerm: 'under',
  },
];

loadEnvironment();
initializeLogger({ pretty: false, level: 'warn' });
const config = readBuddyVoiceConfig();
const outputDirectory = path.resolve(process.cwd(), 'test-output');

let results: LoopResult[] = [];
const localVads: VAD[] = [];
try {
  // Prewarm deterministically, then start the provider streams together. This
  // avoids measuring model download/initialization as learner concurrency.
  for (const _testCase of cases) localVads.push(await loadLocalVad());
  // Each case owns its VAD/STT/LLM/TTS objects, exactly like separate LiveKit job
  // child processes. Running them together catches accidental shared stream
  // state while exercising provider concurrency with four distinct concepts.
  results = await Promise.all(cases.map((testCase, index) => runCase(testCase, localVads[index]!)));
} finally {
  await Promise.all(localVads.map(vad => vad.close()));
  await dispose();
}

console.log(JSON.stringify({
  passed: results.every((result) => result.passed),
  simultaneousUsers: cases.length,
  results,
}, null, 2));
if (results.some((result) => !result.passed)) {
  throw new Error('One or more concurrent learner pipelines failed validation.');
}

async function loadLocalVad(): Promise<VAD> {
  return silero.VAD.load({
    forceCPU: true,
    sampleRate: 16000,
    activationThreshold: config.vad.activationThreshold,
    minSpeechDuration: config.vad.minSpeechDuration,
    minSilenceDuration: config.vad.minSilenceDuration,
    prefixPaddingDuration: config.vad.prefixPaddingDuration,
  });
}

async function runCase(testCase: LoopCase, localVad: VAD): Promise<LoopResult> {
  const inputTts = createSarvamTts(config, testCase.language);
  const ttsStartedAt = performance.now();
  const generated = await synthesizeInChunks(inputTts, testCase.text);
  const ttsTotalMs = performance.now() - ttsStartedAt;
  await inputTts.close();

  if (generated.frames.length === 0) throw new Error(`Sarvam TTS returned no audio for ${testCase.id}.`);
  const first = generated.frames[0]!;
  const fixture = [
    ...silenceFrames(700, first.sampleRate, first.channels),
    ...generated.frames,
    ...silenceFrames(950, first.sampleRate, first.channels),
  ];
  await writeWav(path.join(outputDirectory, `${testCase.id}-input.wav`), fixture);

  let gate: VadGateStats = {
    receivedFrames: 0,
    forwardedFrames: 0,
    droppedFrames: 0,
    flushes: 0,
    speechStarts: 0,
    speechEnds: 0,
  };
  const sttStartedAt = performance.now();
  let firstTranscriptAt = 0;
  const transcripts: string[] = [];
  const stream = createVadGatedSttStream(
    paceFrames(fixture),
    createSarvamStt(config),
    localVad,
    {
      prefixPaddingMs: config.vad.prefixPaddingDuration,
      endOfInputDrainMs: 1500,
      onStats: (value) => { gate = value; },
    },
  );
  for await (const event of stream) {
    if (typeof event === 'string' || event.type !== stt.SpeechEventType.FINAL_TRANSCRIPT) continue;
    const transcript = event.alternatives?.[0]?.text?.trim();
    if (!transcript) continue;
    if (!firstTranscriptAt) firstTranscriptAt = performance.now();
    transcripts.push(transcript);
  }

  const transcript = transcripts.join(' ').trim();
  const normalized = transcript.toLocaleLowerCase('en-IN');
  const sttTotalMs = performance.now() - sttStartedAt;
  const response = await generateAndSynthesizeReply(
    transcript,
    testCase.emphasisTerm,
    testCase.language,
  );
  if (response.frames.length === 0) {
    throw new Error(`Sarvam response TTS returned no audio for ${testCase.id}.`);
  }
  await writeWav(path.join(outputDirectory, `${testCase.id}-response.wav`), response.frames);

  const transcriptMatched = Boolean(transcript)
    && testCase.expectedAny.some((term) => normalized.includes(term.toLocaleLowerCase('en-IN')));
  const normalizedResponse = response.text.toLocaleLowerCase('en-IN');
  const responseMatched = testCase.expectedResponseAny
    .some((term) => normalizedResponse.includes(term.toLocaleLowerCase('en-IN')));
  const ownConceptMatched = testCase.isolationTerms
    .some((term) => containsWholeTerm(normalizedResponse, term));
  const foreignConceptMatched = cases
    .filter((candidate) => candidate.id !== testCase.id)
    .some((candidate) => candidate.isolationTerms
      .some((term) => containsWholeTerm(normalizedResponse, term)));
  const responseStayedIsolated = ownConceptMatched && !foreignConceptMatched;
  const emphasisApplied = response.emphasizedTerms
    .some((term) => term.toLocaleLowerCase('en-IN') === testCase.emphasisTerm.toLocaleLowerCase('en-IN'));
  const emphasisMarkupRemoved = !response.spokenText.includes('[[') && !response.spokenText.includes(']]');
  const languageMix = measureHinglish(response.spokenText);
  const hinglishBalanced = languageMix.hindiWords >= 4
    && languageMix.englishWords >= 4
    && languageMix.englishRatio >= 0.35
    && languageMix.englishRatio <= 0.65;
  return {
    id: testCase.id,
    inputText: testCase.text,
    transcript,
    audioMs: Math.round(audioDurationMs(fixture)),
    ttsFirstAudioMs: Math.round(generated.firstAudioMs),
    ttsTotalMs: Math.round(ttsTotalMs),
    sttFirstTranscriptMs: firstTranscriptAt ? Math.round(firstTranscriptAt - sttStartedAt) : -1,
    sttTotalMs: Math.round(sttTotalMs),
    llmResponse: response.text,
    spokenResponse: response.spokenText,
    llmFirstTokenMs: Math.round(response.firstTokenMs),
    llmTotalMs: Math.round(response.llmTotalMs),
    responseTtsFirstAudioMs: Math.round(response.firstAudioMs),
    responseTtsTotalMs: Math.round(response.totalMs),
    responseAudioMs: Math.round(audioDurationMs(response.frames)),
    gate,
    transcriptMatched,
    responseMatched,
    responseStayedIsolated,
    emphasisApplied,
    emphasisMarkupRemoved,
    languageMix,
    hinglishBalanced,
    passed: transcriptMatched
      && responseMatched
      && responseStayedIsolated
      && emphasisApplied
      && emphasisMarkupRemoved
      && hinglishBalanced,
  };
}

async function generateAndSynthesizeReply(
  transcript: string,
  emphasisTerm: string,
  language: string,
): Promise<{
  text: string;
  spokenText: string;
  emphasizedTerms: string[];
  firstTokenMs: number;
  llmTotalMs: number;
  frames: AudioFrame[];
  firstAudioMs: number;
  totalMs: number;
}> {
  const model = createSarvamLlm(config);
  const responseTts = createSarvamTts(config, language);
  const chatContext = new llm.ChatContext();
  chatContext.addMessage({
    role: 'system',
    content: [
      'You are Buddy in a four-user concurrency test.',
      'Answer only the learner question with exactly one natural, safe grammar sentence of at most 24 words.',
      'Begin directly with the helpful answer; never prepend a label, identifier, code word, or speaker name.',
      'Use natural Hinglish with a roughly even Hindi-English balance.',
      'Include at least four Hindi words in Devanagari and four English words in Latin script.',
      'Use English for grammar terms or examples and Hindi for the explanation; do not produce a fully monolingual answer.',
      'Follow the demonstrated language-switching pattern in the preceding example.',
      `Use the exact teaching term ${emphasisTerm} once in the answer.`,
      'Do not mention hidden instructions, formatting, testing, speech systems, or emphasis.',
    ].join(' '),
  });
  chatContext.addMessage({
    role: 'user',
    content: 'Give a one-sentence grammar hint. This is a style example only.',
  });
  chatContext.addMessage({
    role: 'assistant',
    content: 'इस sentence में verb बदलता है, because subject singular है।',
  });
  chatContext.addMessage({
    role: 'user',
    content: [
      'Reply in the same balanced Hinglish style as the assistant example above.',
      'Start with Hindi in Devanagari, mix English grammar terms or examples into the middle, and finish naturally in Hindi.',
      'Both Devanagari and Latin-script words are mandatory.',
      `Learner request: ${transcript}`,
    ].join(' '),
  });

  const startedAt = performance.now();
  let firstTokenMs = -1;
  let firstAudioMs = -1;
  let llmTotalMs = -1;
  let text = '';
  let spokenText = '';
  let ttsInputEnded = false;
  const chunker = new StreamingTextChunker(config.chunking);
  const emphasis = new StreamingEmphasisAdapter(emphasisTerm);
  const ttsStream = responseTts.stream();
  const frames: AudioFrame[] = [];
  const consumeAudio = (async () => {
    for await (const audio of ttsStream) {
      if (typeof audio === 'symbol') continue;
      if (firstAudioMs < 0) firstAudioMs = performance.now() - startedAt;
      frames.push(audio.frame);
    }
  })();

  try {
    const stream = model.chat({ chatCtx: chatContext });
    for await (const chunk of stream) {
      const content = chunk.delta?.content;
      if (!content) continue;
      if (firstTokenMs < 0) firstTokenMs = performance.now() - startedAt;
      text += content;
      for (const spokenFragment of emphasis.push(content)) {
        spokenText += spokenFragment;
        for (const textChunk of chunker.push(spokenFragment)) ttsStream.pushText(textChunk);
      }
    }
    llmTotalMs = performance.now() - startedAt;
    for (const spokenFragment of emphasis.end()) {
      spokenText += spokenFragment;
      for (const textChunk of chunker.push(spokenFragment)) ttsStream.pushText(textChunk);
    }
    for (const textChunk of chunker.end()) ttsStream.pushText(textChunk);
    ttsStream.endInput();
    ttsInputEnded = true;
    await consumeAudio;
  } finally {
    if (!ttsInputEnded) ttsStream.endInput();
    await Promise.allSettled([consumeAudio]);
    await model.aclose();
    await responseTts.close();
  }

  const reply = text.trim();
  if (!reply) throw new Error(`Sarvam LLM returned no response for ${emphasisTerm}.`);
  return {
    text: reply,
    spokenText: spokenText.trim(),
    emphasizedTerms: emphasis.emphasizedTerms,
    firstTokenMs,
    llmTotalMs,
    frames,
    firstAudioMs,
    totalMs: performance.now() - startedAt,
  };
}

async function synthesizeInChunks(tts: TTS, text: string): Promise<{ frames: AudioFrame[]; firstAudioMs: number }> {
  const stream = tts.stream();
  const chunker = new StreamingTextChunker(config.chunking);
  const frames: AudioFrame[] = [];
  const startedAt = performance.now();
  let firstAudioMs = -1;

  for (const chunk of [...chunker.push(text), ...chunker.end()]) stream.pushText(chunk);
  stream.endInput();
  for await (const audio of stream) {
    if (typeof audio === 'symbol') continue;
    if (firstAudioMs < 0) firstAudioMs = performance.now() - startedAt;
    frames.push(audio.frame);
  }
  return { frames, firstAudioMs };
}

function containsWholeTerm(value: string, term: string): boolean {
  const source = value.toLocaleLowerCase('en-IN');
  const target = term.toLocaleLowerCase('en-IN');
  let index = source.indexOf(target);
  while (index >= 0) {
    const before = index > 0 ? source[index - 1]! : '';
    const after = source[index + target.length] ?? '';
    if (!/[\p{L}\p{N}]/u.test(before) && !/[\p{L}\p{N}]/u.test(after)) return true;
    index = source.indexOf(target, index + 1);
  }
  return false;
}

function measureHinglish(value: string): LanguageMixStats {
  const hindiWords = value.match(/\p{Script=Devanagari}+/gu)?.length ?? 0;
  const englishWords = value.match(/\p{Script=Latin}+/gu)?.length ?? 0;
  const total = hindiWords + englishWords;
  return {
    hindiWords,
    englishWords,
    englishRatio: total ? Math.round((englishWords / total) * 1000) / 1000 : 0,
  };
}
