import { initializeLogger, llm, stt, type VAD } from '@livekit/agents';
import * as silero from '@livekit/agents-plugin-silero';
import type { TTS } from '@livekit/agents-plugin-sarvam';
import { dispose, type AudioFrame } from '@livekit/rtc-node';
import path from 'node:path';
import {
  audioDurationMs,
  paceFrames,
  whiteNoiseFrames,
  writeWav,
} from './audio.js';
import { loadEnvironment, readBuddyVoiceConfig } from './config.js';
import { StreamingEmphasisAdapter } from './emphasis.js';
import { createSarvamLlm, createSarvamStt, createSarvamTts } from './providers.js';
import { StreamingTextChunker } from './text-chunker.js';
import { createVadGatedSttStream, type VadGateStats } from './vad-gated-stt.js';

interface ConversationSpec {
  id: string;
  concept: string;
  emphasisTerm: string;
  learnerTurns: string[];
}

interface LanguageMix {
  hindiWords: number;
  englishWords: number;
  englishRatio: number;
  description: string;
}

interface TurnResult {
  turn: number;
  scriptedLearnerText: string;
  transcript: string;
  buddyText: string;
  spokenBuddyText: string;
  measuredBuddyPauseMs: number;
  llmFirstTokenMs: number;
  buddyFirstAudioMs: number;
  gate: VadGateStats;
  languageMix: LanguageMix;
}

interface ConversationResult {
  id: string;
  concept: string;
  model: string;
  learnerSpeaker: string;
  buddySpeaker: string;
  outputFile: string;
  durationMs: number;
  overallLanguageMix: LanguageMix;
  turns: TurnResult[];
  passed: boolean;
}

const conversations: ConversationSpec[] = [
  {
    id: 'umbrella-and-rain',
    concept: 'Everyday weather vocabulary',
    emphasisTerm: 'umbrella',
    learnerTurns: [
      'Hey Buddy, umbrella का क्या meaning है?',
      'अच्छा, तो बारिश को English में क्या कहते हैं?',
      'Okay, thank you Buddy.',
    ],
  },
  {
    id: 'subject-verb-agreement',
    concept: 'Subject-verb agreement',
    emphasisTerm: 'walks',
    learnerTurns: [
      'Buddy, he walk बोलूँ या he walks?',
      'अगर subject they हो, then क्या बोलेंगे?',
      'Nice, अब मुझे rule समझ आ गया.',
    ],
  },
  {
    id: 'past-tense',
    concept: 'Past tense',
    emphasisTerm: 'went',
    learnerTurns: [
      'Buddy, yesterday के लिए go का कौन सा form होगा?',
      'तो I went to school बोलना सही है?',
      'Great, अब past tense clear है, thank you.',
    ],
  },
  {
    id: 'spatial-prepositions',
    concept: 'Under and above',
    emphasisTerm: 'under',
    learnerTurns: [
      'Buddy, cat table के नीचे है, इसे English में कैसे कहूँ?',
      'और अगर book table के ऊपर हो, then what should I say?',
      'Okay, under और above दोनों clear हैं.',
    ],
  },
];

loadEnvironment();
initializeLogger({ pretty: false, level: 'warn' });
const config = readBuddyVoiceConfig();
const outputDirectory = path.resolve(process.cwd(), 'test-output', 'conversations');
const learnerSpeaker = process.env.SARVAM_TEST_LEARNER_SPEAKER?.trim() || 'ritu';

const localVads: VAD[] = [];
let results: ConversationResult[] = [];
try {
  for (const _conversation of conversations) localVads.push(await loadLocalVad());
  results = await Promise.all(conversations.map((conversation, index) => (
    runConversation(conversation, localVads[index]!)
  )));
} finally {
  await Promise.all(localVads.map((vad) => vad.close()));
  await dispose();
}

console.log(JSON.stringify({
  passed: results.every((result) => result.passed),
  simultaneousConversations: conversations.length,
  model: config.llmModel,
  pauseDefinition: 'endpoint minimum delay + measured LLM-to-first-TTS-audio latency',
  results,
}, null, 2));

if (results.some((result) => !result.passed)) {
  throw new Error('One or more stitched conversation pipelines failed.');
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

async function runConversation(spec: ConversationSpec, vad: VAD): Promise<ConversationResult> {
  const model = createSarvamLlm(config);
  const speechRecognition = createSarvamStt(config);
  const learnerTts = createSarvamTts(config, 'hi-IN', learnerSpeaker);
  const buddyTts = createSarvamTts(config, 'hi-IN', config.ttsSpeaker);
  const chatContext = createConversationContext(spec);
  const stitched: AudioFrame[] = whiteNoiseFrames(420, 24000, 1, 115, seedFor(`${spec.id}:opening`));
  const turns: TurnResult[] = [];

  try {
    for (let index = 0; index < spec.learnerTurns.length; index++) {
      const scriptedLearnerText = spec.learnerTurns[index]!;
      const learnerAudio = await synthesizeInChunks(learnerTts, scriptedLearnerText);
      if (learnerAudio.length === 0) throw new Error(`No learner audio for ${spec.id} turn ${index + 1}.`);
      const first = learnerAudio[0]!;
      const sttFixture = [
        ...whiteNoiseFrames(260, first.sampleRate, first.channels, 90, seedFor(`${spec.id}:${index}:before`)),
        ...learnerAudio,
        ...whiteNoiseFrames(620, first.sampleRate, first.channels, 90, seedFor(`${spec.id}:${index}:after`)),
      ];
      const recognized = await transcribeTurn(sttFixture, speechRecognition, vad);
      if (!recognized.transcript) throw new Error(`No transcript for ${spec.id} turn ${index + 1}.`);

      chatContext.addMessage({ role: 'user', content: recognized.transcript });
      const buddy = await generateBuddyTurn(
        model,
        buddyTts,
        chatContext,
        index === 0 ? spec.emphasisTerm : undefined,
      );
      chatContext.addMessage({ role: 'assistant', content: buddy.text });

      // This is the pause a learner would perceive after finishing the turn:
      // endpointing plus the measured 105B -> first streamed audio latency.
      const measuredBuddyPauseMs = Math.round(config.endpointing.minimumDelay + buddy.firstAudioMs);
      const learnerReplyPauseMs = index === spec.learnerTurns.length - 1 ? 480 : 760;
      stitched.push(
        ...learnerAudio,
        ...whiteNoiseFrames(
          measuredBuddyPauseMs,
          first.sampleRate,
          first.channels,
          115,
          seedFor(`${spec.id}:${index}:buddy-pause`),
        ),
        ...buddy.frames,
        ...whiteNoiseFrames(
          learnerReplyPauseMs,
          first.sampleRate,
          first.channels,
          115,
          seedFor(`${spec.id}:${index}:learner-pause`),
        ),
      );

      turns.push({
        turn: index + 1,
        scriptedLearnerText,
        transcript: recognized.transcript,
        buddyText: buddy.text,
        spokenBuddyText: buddy.spokenText,
        measuredBuddyPauseMs,
        llmFirstTokenMs: Math.round(buddy.firstTokenMs),
        buddyFirstAudioMs: Math.round(buddy.firstAudioMs),
        gate: recognized.gate,
        languageMix: measureLanguageMix(buddy.spokenText),
      });
    }
  } finally {
    await Promise.allSettled([model.aclose(), learnerTts.close(), buddyTts.close()]);
  }

  const outputFile = path.join(outputDirectory, `${spec.id}-conversation.wav`);
  await writeWav(outputFile, stitched);
  const combinedBuddyText = turns.map((turn) => turn.spokenBuddyText).join(' ');
  return {
    id: spec.id,
    concept: spec.concept,
    model: config.llmModel,
    learnerSpeaker,
    buddySpeaker: config.ttsSpeaker,
    outputFile,
    durationMs: Math.round(audioDurationMs(stitched)),
    overallLanguageMix: measureLanguageMix(combinedBuddyText),
    turns,
    passed: turns.length === spec.learnerTurns.length
      && turns.every((turn) => Boolean(turn.transcript) && Boolean(turn.buddyText)),
  };
}

function createConversationContext(spec: ConversationSpec): llm.ChatContext {
  const context = new llm.ChatContext();
  context.addMessage({
    role: 'system',
    content: [
      'You are Buddy, a warm voice-first grammar and vocabulary tutor for a primary-school learner in India.',
      `The current concept is ${spec.concept}.`,
      'Reply with one short, conversational sentence and continue the existing conversation naturally.',
      'Use everyday Hinglish: Hindi in Devanagari for friendly explanation, with English words for grammar, vocabulary, and examples.',
      'Do not calculate a language quota and do not force a translation; most replies should naturally contain both languages.',
      'Never prepend labels, identifiers, names, markdown, emoji, or stage directions.',
      'If the learner says thanks or shows understanding, respond briefly and warmly instead of restarting the lesson.',
    ].join(' '),
  });
  context.addMessage({ role: 'user', content: 'Hey Buddy, umbrella का meaning क्या है?' });
  context.addMessage({
    role: 'assistant',
    content: 'Umbrella मतलब छाता होता है, और rain में यह आपको dry रखता है।',
  });
  return context;
}

async function transcribeTurn(
  fixture: AudioFrame[],
  speechRecognition: ReturnType<typeof createSarvamStt>,
  vad: VAD,
): Promise<{ transcript: string; gate: VadGateStats }> {
  let gate: VadGateStats = {
    receivedFrames: 0,
    forwardedFrames: 0,
    droppedFrames: 0,
    flushes: 0,
    speechStarts: 0,
    speechEnds: 0,
  };
  const transcripts: string[] = [];
  const stream = createVadGatedSttStream(paceFrames(fixture), speechRecognition, vad, {
    prefixPaddingMs: config.vad.prefixPaddingDuration,
    endOfInputDrainMs: 1200,
    onStats: (value) => { gate = value; },
  });
  for await (const event of stream) {
    if (typeof event === 'string' || event.type !== stt.SpeechEventType.FINAL_TRANSCRIPT) continue;
    const transcript = event.alternatives?.[0]?.text?.trim();
    if (transcript) transcripts.push(transcript);
  }
  return { transcript: transcripts.join(' ').trim(), gate };
}

async function generateBuddyTurn(
  model: ReturnType<typeof createSarvamLlm>,
  tts: TTS,
  chatContext: llm.ChatContext,
  emphasisTerm?: string,
): Promise<{
  text: string;
  spokenText: string;
  frames: AudioFrame[];
  firstTokenMs: number;
  firstAudioMs: number;
}> {
  const startedAt = performance.now();
  const chunker = new StreamingTextChunker(config.chunking);
  const emphasis = new StreamingEmphasisAdapter(emphasisTerm);
  const ttsStream = tts.stream();
  const frames: AudioFrame[] = [];
  let text = '';
  let spokenText = '';
  let firstTokenMs = -1;
  let firstAudioMs = -1;
  let inputEnded = false;
  const consumeAudio = (async () => {
    for await (const audio of ttsStream) {
      if (typeof audio === 'symbol') continue;
      if (firstAudioMs < 0) firstAudioMs = performance.now() - startedAt;
      frames.push(audio.frame);
    }
  })();

  try {
    for await (const chunk of model.chat({ chatCtx: chatContext })) {
      const content = chunk.delta?.content;
      if (!content) continue;
      if (firstTokenMs < 0) firstTokenMs = performance.now() - startedAt;
      text += content;
      for (const fragment of emphasis.push(content)) {
        spokenText += fragment;
        for (const speakableChunk of chunker.push(fragment)) ttsStream.pushText(speakableChunk);
      }
    }
    for (const fragment of emphasis.end()) {
      spokenText += fragment;
      for (const speakableChunk of chunker.push(fragment)) ttsStream.pushText(speakableChunk);
    }
    for (const speakableChunk of chunker.end()) ttsStream.pushText(speakableChunk);
    ttsStream.endInput();
    inputEnded = true;
    await consumeAudio;
  } finally {
    if (!inputEnded) ttsStream.endInput();
    await Promise.allSettled([consumeAudio]);
  }

  const reply = text.trim();
  if (!reply || frames.length === 0) throw new Error('Buddy returned an empty text or audio response.');
  return { text: reply, spokenText: spokenText.trim(), frames, firstTokenMs, firstAudioMs };
}

async function synthesizeInChunks(tts: TTS, text: string): Promise<AudioFrame[]> {
  const stream = tts.stream();
  const chunker = new StreamingTextChunker(config.chunking);
  const frames: AudioFrame[] = [];
  for (const chunk of [...chunker.push(text), ...chunker.end()]) stream.pushText(chunk);
  stream.endInput();
  for await (const audio of stream) {
    if (typeof audio !== 'symbol') frames.push(audio.frame);
  }
  return frames;
}

function measureLanguageMix(value: string): LanguageMix {
  const hindiWords = value.match(/\p{Script=Devanagari}+/gu)?.length ?? 0;
  const englishWords = value.match(/\p{Script=Latin}+/gu)?.length ?? 0;
  const total = hindiWords + englishWords;
  const englishRatio = total ? englishWords / total : 0;
  let description = 'monolingual or undetermined';
  if (hindiWords && englishWords) {
    if (englishRatio < 0.3) description = 'Hindi-led natural Hinglish';
    else if (englishRatio <= 0.7) description = 'balanced natural Hinglish';
    else description = 'English-led natural Hinglish';
  } else if (hindiWords) description = 'mostly Hindi';
  else if (englishWords) description = 'mostly English';
  return {
    hindiWords,
    englishWords,
    englishRatio: Math.round(englishRatio * 1000) / 1000,
    description,
  };
}

function seedFor(value: string): number {
  let seed = 2166136261;
  for (const character of value) {
    seed ^= character.codePointAt(0) ?? 0;
    seed = Math.imul(seed, 16777619);
  }
  return seed >>> 0;
}
