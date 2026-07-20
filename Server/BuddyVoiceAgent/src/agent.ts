import {
  AgentSessionEventTypes,
  WorkerPermissions,
  WorkerOptions,
  cli,
  defineAgent,
  type JobProcess,
  type VAD,
  voice,
} from '@livekit/agents';
import * as silero from '@livekit/agents-plugin-silero';
import { TrackSource } from '@livekit/protocol';
import { RoomEvent } from '@livekit/rtc-node';
import { fileURLToPath } from 'node:url';
import { loadEnvironment, readBuddyVoiceConfig } from './config.js';
import { createSarvamLlm, createSarvamStt, createSarvamTts } from './providers.js';
import { chunkTextStream } from './text-chunker.js';
import { createVadGatedSttStream } from './vad-gated-stt.js';
import {
  buddyWorkerLoad,
  parseBuddyGameEvent,
  parseBuddyJobMetadata,
  type BuddyJobMetadata,
} from './session-metadata.js';

interface BuddyProcessData extends Record<string, unknown> {
  vad?: VAD;
}

loadEnvironment();
const workerConfig = readBuddyVoiceConfig({ requireSarvamKey: false });
const BUDDY_DEBUG_TOPIC = 'buddy.debug_event';

export default defineAgent<BuddyProcessData>({
  prewarm: async (process: JobProcess<BuddyProcessData>) => {
    const config = readBuddyVoiceConfig({ requireSarvamKey: false });
    process.userData.vad = await silero.VAD.load({
      forceCPU: true,
      sampleRate: 16000,
      activationThreshold: config.vad.activationThreshold,
      minSpeechDuration: config.vad.minSpeechDuration,
      minSilenceDuration: config.vad.minSilenceDuration,
      prefixPaddingDuration: config.vad.prefixPaddingDuration,
    });
  },

  entry: async (ctx) => {
    const config = readBuddyVoiceConfig();
    const vad = ctx.proc.userData.vad;
    if (!vad) throw new Error('Local Silero VAD was not prewarmed.');

    const lesson = parseBuddyJobMetadata(ctx.job.metadata);
    const remainingSessionMs = lesson.expiresAtEpochMs - Date.now();
    if (remainingSessionMs <= 0) throw new Error('Buddy voice session expired before startup.');
    const sessionDeadline = setTimeout(
      () => ctx.shutdown('voice_session_expired'),
      remainingSessionMs,
    );
    sessionDeadline.unref();
    ctx.addShutdownCallback(async () => clearTimeout(sessionDeadline));
    const publishDebug = (event: BuddyDebugEvent) => {
      void publishBuddyDebugEvent(ctx.room, lesson, event);
    };
    // The custom STT node receives a LiveKit-managed audio stream. It does not
    // subscribe to room tracks itself, so it cannot race the session's audio
    // lifecycle. Reuse the session resources so the gate and turn handling
    // apply the same VAD thresholds and Sarvam configuration.
    const speechRecognition = createSarvamStt(config);
    const agent = voice.Agent.create({
      instructions: buddyInstructions(lesson),
      sttNode(_hook, audio) {
        // Keep only bounded pre-roll plus speech in Sarvam's stream. On local
        // VAD end-of-speech, flush Sarvam immediately rather than making it
        // infer an end from uploaded silence. AgentSession still owns room
        // subscription and its separate VAD stream for turn handling.
        return createVadGatedSttStream(audio, speechRecognition, vad, {
          prefixPaddingMs: config.vad.prefixPaddingDuration,
          // Let short within-sentence pauses resume the same Sarvam stream.
          // Total local silence before a final flush stays within the endpoint limit.
          endOfSpeechFlushDelayMs: Math.max(
            0,
            config.endpointing.maximumDelay - config.vad.minSilenceDuration,
          ),
          onGateEvent: event => {
            const stats = event.stats;
            const message = event.type === 'speech_start'
              ? 'Local VAD detected learner speech; forwarding bounded pre-roll to Sarvam.'
              : event.type === 'speech_end'
                ? 'Local VAD detected end of speech; requesting a Sarvam final transcript.'
                : event.type === 'flush'
                  ? 'Sarvam STT stream flushed at the local VAD speech boundary.'
                  : 'VAD-gated STT stream statistics updated.';
            publishDebug({
              stage: 'vad',
              type: event.type,
              message,
              details: {
                receivedFrames: stats.receivedFrames,
                forwardedFrames: stats.forwardedFrames,
                droppedFrames: stats.droppedFrames,
                flushes: stats.flushes,
                speechStarts: stats.speechStarts,
                speechEnds: stats.speechEnds,
              },
            });
          },
        });
      },
      ttsNode(hook, text, modelSettings) {
        const tappedText = tapLlmTextStream(text, publishDebug);
        return voice.Agent.default.ttsNode(
          hook.agent,
          chunkTextStream(tappedText, config.chunking),
          modelSettings,
        );
      },
    });

    const session = new voice.AgentSession({
      vad,
      stt: speechRecognition,
      llm: createSarvamLlm(config),
      tts: createSarvamTts(config),
      turnHandling: {
        turnDetection: 'vad',
        endpointing: {
          mode: 'fixed',
          minDelay: config.endpointing.minimumDelay,
          maxDelay: config.endpointing.maximumDelay,
        },
        interruption: {
          enabled: true,
          mode: 'vad',
          minDuration: 260,
          minWords: 0,
          resumeFalseInterruption: true,
        },
        preemptiveGeneration: {
          enabled: true,
          preemptiveTts: true,
          maxSpeechDuration: 10_000,
          maxRetries: 2,
        },
      },
      ttsReadIdleTimeout: 8_000,
      forwardAudioIdleTimeout: 8_000,
    });

    await session.start({ room: ctx.room, agent });
    const subscribeLearnerTrack = (
      publication: { setSubscribed?(subscribed: boolean): void; sid?: string },
      participant: { identity?: string } | undefined,
    ) => {
      if (participant?.identity !== lesson.participantIdentity) return;
      publication.setSubscribed?.(true);
      publishDebug({
        stage: 'agent',
        message: 'Buddy requested the learner audio-track subscription.',
        type: 'learner_track_subscription_requested',
        details: { trackSid: publication.sid },
      });
    };
    ctx.room.on(RoomEvent.TrackPublished, subscribeLearnerTrack);
    // A browser can publish its microphone in the small gap between room
    // connection and worker startup. Subscribe to that already-present track
    // as well as future TrackPublished events.
    for (const participant of ctx.room.remoteParticipants.values()) {
      if (participant.identity !== lesson.participantIdentity) continue;
      for (const publication of participant.trackPublications.values()) {
        subscribeLearnerTrack(publication, participant);
      }
    }
    const onUserTranscript = (event: { transcript?: string; isFinal?: boolean; language?: string | null }) => {
      const text = event.transcript?.trim();
      if (!text) return;
      publishDebug({
        stage: 'stt',
        message: event.isFinal ? `STT final: ${text}` : `STT interim: ${text}`,
        type: event.isFinal ? 'final' : 'interim',
        text,
        details: { language: event.language ?? undefined },
      });
    };
    session.on(AgentSessionEventTypes.UserInputTranscribed, onUserTranscript);
    publishDebug({
      stage: 'agent',
      message: 'Buddy worker joined the room.',
      type: 'agent_joined',
      details: {
        room: ctx.room.name,
        participantIdentity: lesson.participantIdentity,
      },
    });
    const onGameEvent = (payload: Uint8Array, participant: { identity?: string } | undefined, _kind: unknown, topic?: string) => {
      if (topic !== 'buddy.game_event' || participant?.identity !== lesson.participantIdentity) return;
      try {
        const event = parseBuddyGameEvent(payload);
        if (event.conceptId && lesson.conceptId && event.conceptId !== lesson.conceptId) return;
        publishDebug({
          stage: 'game_event',
          message: `Game event received: ${event.type}.`,
          type: event.type,
          text: event.learnerAttempt || undefined,
          details: {
            conceptId: event.conceptId,
            turnIndex: event.turnIndex,
          },
        });
        if (event.type === 'wrong_answer') {
          publishReplyRequested(publishDebug, 'wrong_answer');
          session.generateReply({
            userInput: `[Trusted game event] The learner tried: ${event.learnerAttempt || '(no speech captured)'}`,
            instructions: 'Acknowledge the effort, give exactly one short clue for the current task, and invite another try. Do not say the corrected phrase; if you need an example, use a different noun or sentence.',
            allowInterruptions: true,
          });
        } else if (event.type === 'word_meaning') {
          publishReplyRequested(publishDebug, 'word_meaning');
          session.generateReply({
            userInput: `[Trusted game event] The learner selected a word and asks: ${event.learnerAttempt}`,
            instructions: 'Explain that word in this sentence with one child-friendly meaning and one tiny spoken example.',
            allowInterruptions: true,
          });
        } else {
          publishReplyRequested(publishDebug, 'task_answered');
          session.generateReply({
            userInput: '[Trusted game event] The learner completed the current task.',
            instructions: 'Celebrate briefly and naturally. Do not start a new lesson or ask a quiz question.',
            allowInterruptions: true,
          });
        }
      } catch (error) {
        publishDebug({
          stage: 'error',
          message: 'Ignored an invalid game event.',
          type: 'game_event_invalid',
          details: { reason: error instanceof Error ? error.message : String(error) },
        });
        console.warn('[BuddyVoice] ignored invalid game event', {
          voiceSessionId: lesson.voiceSessionId,
          reason: error instanceof Error ? error.message : String(error),
        });
      }
    };
    ctx.room.on(RoomEvent.DataReceived, onGameEvent);
    ctx.addShutdownCallback(async () => {
      ctx.room.off(RoomEvent.TrackPublished, subscribeLearnerTrack);
      session.off(AgentSessionEventTypes.UserInputTranscribed, onUserTranscript);
      ctx.room.off(RoomEvent.DataReceived, onGameEvent);
    });

    publishReplyRequested(publishDebug, 'opening');
    session.generateReply(openingReply(lesson));
  },
});

interface BuddyDebugEvent {
  stage: 'agent' | 'vad' | 'stt' | 'llm' | 'game_event' | 'error';
  message: string;
  type?: string;
  text?: string;
  details?: Record<string, unknown>;
}

interface DebugPublisherRoom {
  name?: string;
  localParticipant?: {
    publishData(data: Uint8Array, options: { reliable: boolean; topic: string }): Promise<void>;
  };
}

function publishBuddyDebugEvent(
  room: DebugPublisherRoom,
  metadata: BuddyJobMetadata,
  event: BuddyDebugEvent,
): void {
  const participant = room.localParticipant;
  if (!participant) return;
  const body = {
    schemaVersion: 1,
    atUtc: new Date().toISOString(),
    voiceSessionId: metadata.voiceSessionId,
    ...event,
  };
  const data = new TextEncoder().encode(JSON.stringify(body));
  void participant.publishData(data, { reliable: true, topic: BUDDY_DEBUG_TOPIC }).catch(error => {
    console.warn('[BuddyVoice] debug event publish failed', {
      voiceSessionId: metadata.voiceSessionId,
      reason: error instanceof Error ? error.message : String(error),
    });
  });
}

function publishReplyRequested(
  publishDebug: (event: BuddyDebugEvent) => void,
  reason: string,
): void {
  publishDebug({
    stage: 'llm',
    message: `LLM reply requested: ${reason}.`,
    type: 'reply_requested',
    details: { reason },
  });
}

function tapLlmTextStream(
  text: ReadableStream<string> | AsyncIterable<string>,
  publishDebug: (event: BuddyDebugEvent) => void,
): ReadableStream<string> | AsyncIterable<string> {
  if (text instanceof ReadableStream) {
    const [debugStream, ttsStream] = text.tee();
    void collectLlmText(debugStream, publishDebug);
    return ttsStream;
  }

  return (async function* tappedText() {
    let fullText = '';
    publishDebug({ stage: 'llm', message: 'LLM text stream started.', type: 'text_started' });
    try {
      for await (const chunk of text) {
        if (chunk) {
          fullText += chunk;
          publishDebug({
            stage: 'llm',
            message: 'LLM text chunk streamed.',
            type: 'text_delta',
            text: chunk,
          });
        }
        yield chunk;
      }
      publishDebug({
        stage: 'llm',
        message: 'LLM text stream finished.',
        type: 'text_final',
        text: fullText.trim() || undefined,
      });
    } catch (error) {
      publishDebug({
        stage: 'error',
        message: 'LLM text stream failed.',
        type: 'llm_text_error',
        details: { reason: error instanceof Error ? error.message : String(error) },
      });
      throw error;
    }
  })();
}

async function collectLlmText(
  text: ReadableStream<string>,
  publishDebug: (event: BuddyDebugEvent) => void,
): Promise<void> {
  let fullText = '';
  publishDebug({ stage: 'llm', message: 'LLM text stream started.', type: 'text_started' });
  const reader = text.getReader();
  try {
    while (true) {
      const result = await reader.read();
      if (result.done) break;
      if (!result.value) continue;
      fullText += result.value;
      publishDebug({
        stage: 'llm',
        message: 'LLM text chunk streamed.',
        type: 'text_delta',
        text: result.value,
      });
    }
    publishDebug({
      stage: 'llm',
      message: 'LLM text stream finished.',
      type: 'text_final',
      text: fullText.trim() || undefined,
    });
  } catch (error) {
    publishDebug({
      stage: 'error',
      message: 'LLM text stream failed.',
      type: 'llm_text_error',
      details: { reason: error instanceof Error ? error.message : String(error) },
    });
  } finally {
    reader.releaseLock();
  }
}

function openingReply(metadata: BuddyJobMetadata) {
  if (metadata.openingTrigger === 'word_meaning') {
    return {
      userInput: `[Trusted game event] The learner selected a word and asks: ${metadata.openingLearnerAttempt}`,
      instructions: 'Answer as if you just picked up the call: greet very briefly, then explain that word with one child-friendly meaning and one tiny spoken example.',
      allowInterruptions: true,
    };
  }
  if (metadata.openingTrigger === 'wrong_answer') {
    return {
      userInput: `[Trusted game event] The learner tried: ${metadata.openingLearnerAttempt || '(no speech captured)'}`,
      instructions: 'Answer as if you just picked up the call: greet very briefly, acknowledge the effort, give one clue, and invite another try. Do not say the corrected phrase; if you need an example, use a different noun or sentence.',
      allowInterruptions: true,
    };
  }
  return {
    instructions: 'Answer as if you just picked up the call: greet the learner in one very short sentence, then ask what part of this task feels confusing.',
    allowInterruptions: true,
  };
}

export function buddyInstructions(metadata: BuddyJobMetadata): string {
  const zone = metadata.zone;
  const answerPolicy = metadata.allowAnswerModel && zone === 'Town'
    ? 'You may give the answer only after first giving a clue.'
    : 'Never reveal, spell, complete, reorder, or quote the exact task answer. Give only a clue or micro-example.';
  const routeCluePolicy = zone === 'Route'
    ? 'For Route corrections, do not say the corrected phrase after "try saying", "say", or similar wording. Do not directly validate the attempt; point to the rule and use a different-example micro-example when helpful.'
    : '';
  const context = [
    metadata.conceptTitle ? `Concept: ${metadata.conceptTitle}.` : '',
    metadata.grammarPattern ? `Grammar pattern: ${metadata.grammarPattern}.` : '',
    metadata.taskPrompt ? `Current task prompt: ${metadata.taskPrompt}.` : '',
    `Learner support band: ${metadata.supportBand}; target English ratio: ${metadata.recommendedEnglishRatio}.`,
    metadata.strengthConceptIds.length ? `Recent strengths: ${metadata.strengthConceptIds.join(', ')}.` : '',
    metadata.needConceptIds.length ? `Needs support with: ${metadata.needConceptIds.join(', ')}.` : '',
    metadata.recurringErrorTags.length ? `Recurring error patterns: ${metadata.recurringErrorTags.join(', ')}.` : '',
    metadata.safeRelationshipMemory.length ? `Safe relationship preferences: ${metadata.safeRelationshipMemory.join(', ')}.` : '',
    metadata.groundingContext ? `Grounding context: ${metadata.groundingContext}` : '',
    metadata.groundingSourceIds.length ? `Grounding source ids: ${metadata.groundingSourceIds.join(', ')}.` : '',
  ].filter(Boolean).join(' ');
  const languageStyle = /^(?:hi|en)(?:-|$)/iu.test(metadata.homeLanguage)
    ? [
      'Use natural Hinglish with a roughly even Hindi-English balance unless the learner explicitly asks for one language.',
      'Write Hindi in Devanagari and English in Latin script. Use English for grammar terms and examples, and Hindi for explanation and warmth.',
      'Do not drift into a fully Hindi or fully English reply.',
      'Imitate this language-mixing pattern, not its lesson content: “इस sentence में verb बदलता है, because subject singular है।”',
    ].join(' ')
    : 'Start in the learner home language and mix simple English learning words only when helpful. Never replace their home language with Hindi.';

  return [
    'You are Buddy, a warm voice-first grammar tutor for primary-school learners in India.',
    'Sound like a familiar, attentive friend who also teaches well: remember harmless preferences, notice effort, and avoid repetitive greetings or scripted praise.',
    `Reply in one or two short, natural sentences. ${languageStyle}`,
    `Preferred explanation style: ${metadata.explanationStyle}. ${metadata.allowTransliteration ? 'Transliteration is allowed when it helps.' : 'Use native scripts rather than transliteration.'}`,
    'Do not use markdown, lists, emoji, SSML, IPA, or stage directions because every token is spoken immediately.',
    'Do not ask for or repeat names, ages, school, address, phone, email, photos, secrets, off-platform contact, or meetings.',
    'Refuse unsafe topics briefly and tell the learner to speak to a trusted adult when safety is involved.',
    `Zone: ${zone}. ${answerPolicy}`,
    routeCluePolicy,
    context,
  ].filter(Boolean).join(' ');
}

cli.runApp(
  new WorkerOptions({
    agent: fileURLToPath(import.meta.url),
    agentName: workerConfig.agentName,
    requestFunc: async request => {
      try {
        const metadata = parseBuddyJobMetadata(request.job.metadata);
        if (!/^buddy-[a-f0-9]{24}$/i.test(request.room?.name ?? '')) {
          throw new Error('Buddy job room name is invalid.');
        }
        await request.accept(
          'Buddy',
          `buddy-agent-${metadata.voiceSessionId}`,
          '',
          { buddy_voice_session: metadata.voiceSessionId },
        );
      } catch (error) {
        console.warn('[BuddyVoice] rejected invalid or expired dispatch', {
          jobId: request.id,
          reason: error instanceof Error ? error.message : String(error),
        });
        await request.reject();
      }
    },
    loadFunc: async worker => buddyWorkerLoad(
      worker.activeJobs.length,
      workerConfig.capacity.maximumActiveSessions,
    ),
    loadThreshold: 1,
    numIdleProcesses: workerConfig.capacity.idleProcesses,
    drainTimeout: workerConfig.capacity.drainTimeoutMs,
    shutdownProcessTimeout: 60000,
    jobMemoryWarnMB: workerConfig.capacity.jobMemoryWarnMb,
    jobMemoryLimitMB: workerConfig.capacity.jobMemoryLimitMb,
    permissions: new WorkerPermissions(
      true,
      true,
      true,
      false,
      [TrackSource.MICROPHONE],
      false,
    ),
    port: 8081,
  }),
);
