import { dispose } from '@livekit/rtc-node';
import { initializeLogger, stt, type VAD } from '@livekit/agents';
import * as silero from '@livekit/agents-plugin-silero';
import path from 'node:path';
import { audioDurationMs, paceFrames, readPcm16Wav } from './audio.js';
import { loadEnvironment, readBuddyVoiceConfig } from './config.js';
import { createSarvamStt } from './providers.js';
import { createVadGatedSttStream, type VadGateStats } from './vad-gated-stt.js';

loadEnvironment();
initializeLogger({ pretty: false, level: 'warn' });
const config = readBuddyVoiceConfig();
const requestedFiles = process.argv.slice(2);
const fixtureFiles = requestedFiles.length
  ? requestedFiles
  : [
    'test-output/hindi-articles-input.wav',
    'test-output/english-prepositions-input.wav',
  ];

const results: Array<Record<string, unknown>> = [];
try {
  for (const fileName of fixtureFiles) results.push(await replayFixture(fileName));
} finally {
  await dispose();
}

const passed = results.every(result => result.passed === true);
console.log(JSON.stringify({ passed, results }, null, 2));
if (!passed) throw new Error('One or more WAV fixtures did not produce a final transcript through the gated STT path.');

async function replayFixture(fileName: string): Promise<Record<string, unknown>> {
  const filePath = path.resolve(process.cwd(), fileName);
  const frames = await readPcm16Wav(filePath);
  const vad = await loadLocalVad();
  const speechRecognition = createSarvamStt(config);
  let gate: VadGateStats = {
    receivedFrames: 0,
    forwardedFrames: 0,
    droppedFrames: 0,
    flushes: 0,
    speechStarts: 0,
    speechEnds: 0,
  };
  const transcripts: string[] = [];
  const startedAt = performance.now();
  let firstFinalMs = -1;
  try {
    const stream = createVadGatedSttStream(paceFrames(frames), speechRecognition, vad, {
      prefixPaddingMs: config.vad.prefixPaddingDuration,
      endOfSpeechFlushDelayMs: Math.max(
        0,
        config.endpointing.maximumDelay - config.vad.minSilenceDuration,
      ),
      endOfInputDrainMs: 1500,
      onStats: value => { gate = value; },
    });
    for await (const event of stream) {
      if (typeof event === 'string' || event.type !== stt.SpeechEventType.FINAL_TRANSCRIPT) continue;
      const transcript = event.alternatives?.[0]?.text?.trim();
      if (!transcript) continue;
      if (firstFinalMs < 0) firstFinalMs = Math.round(performance.now() - startedAt);
      transcripts.push(transcript);
    }
  } finally {
    await vad.close();
  }

  const transcript = transcripts.join(' ').trim();
  return {
    file: fileName,
    audioMs: Math.round(audioDurationMs(frames)),
    firstFinalMs,
    transcript,
    gate,
    passed: Boolean(transcript) && gate.flushes > 0 && gate.droppedFrames > 0,
  };
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