import { VADEventType, stt, type VAD } from '@livekit/agents';
import {
  AudioFrame,
  AudioResampler,
  AudioResamplerQuality,
} from '@livekit/rtc-node';

const SARVAM_STT_SAMPLE_RATE = 16000;

export interface VadGateStats {
  receivedFrames: number;
  forwardedFrames: number;
  droppedFrames: number;
  flushes: number;
  speechStarts: number;
  speechEnds: number;
}

export interface VadGatedSttOptions {
  prefixPaddingMs: number;
  /** Test/file-input helper; live room streams should leave this unset. */
  endOfInputDrainMs?: number;
  onStats?: (stats: VadGateStats) => void;
  onGateEvent?: (event: { type: 'speech_start' | 'speech_end' | 'flush' | 'stats'; stats: VadGateStats }) => void;
  onSpeechEvent?: (event: stt.SpeechEvent) => void;
}

/** Keeps only the newest bounded pre-roll while local VAD says the user is silent. */
export class AudioFrameRingBuffer {
  private frames: AudioFrame[] = [];
  private durationMs = 0;

  constructor(private readonly maximumDurationMs: number) {}

  push(frame: AudioFrame): number {
    this.frames.push(frame);
    this.durationMs += frameDurationMs(frame);
    let dropped = 0;
    while (this.frames.length > 1 && this.durationMs > this.maximumDurationMs) {
      const removed = this.frames.shift();
      if (!removed) break;
      this.durationMs -= frameDurationMs(removed);
      dropped++;
    }
    return dropped;
  }

  drain(): AudioFrame[] {
    const frames = this.frames;
    this.frames = [];
    this.durationMs = 0;
    return frames;
  }

  clear(): number {
    const count = this.frames.length;
    this.frames = [];
    this.durationMs = 0;
    return count;
  }

  get length(): number {
    return this.frames.length;
  }
}

/**
 * Uses a local VAD stream as a gate in front of a native streaming STT stream.
 * Speech and a bounded pre-roll are forwarded immediately; extended silence is
 * dropped. END_OF_SPEECH explicitly flushes Sarvam's WebSocket so it can emit a
 * final transcript without waiting for a provider-side silence timeout.
 */
export function createVadGatedSttStream(
  audio: ReadableStream<AudioFrame> | AsyncIterable<AudioFrame>,
  sttProvider: stt.STT,
  vadProvider: VAD,
  options: VadGatedSttOptions,
): ReadableStream<stt.SpeechEvent | string> {
  if (!sttProvider.capabilities.streaming) {
    throw new Error('Vad-gated STT requires a native streaming STT provider.');
  }

  const speechStream = sttProvider.stream();
  const vadStream = vadProvider.stream();
  const preRoll = new AudioFrameRingBuffer(Math.max(0, options.prefixPaddingMs));
  const stats: VadGateStats = {
    receivedFrames: 0,
    forwardedFrames: 0,
    droppedFrames: 0,
    flushes: 0,
    speechStarts: 0,
    speechEnds: 0,
  };
  let speaking = false;
  let closed = false;
  let vadClosed = false;
  let vadInputClosed = false;
  let speechClosed = false;
  let forwardTask: Promise<void> | undefined;
  let sttResampler: AudioResampler | undefined;
  let sttInputSampleRate = 0;

  const snapshotStats = () => ({ ...stats });
  const emitGateEvent = (type: 'speech_start' | 'speech_end' | 'flush' | 'stats') => {
    options.onGateEvent?.({ type, stats: snapshotStats() });
  };
  const emitStats = () => {
    const snapshot = snapshotStats();
    options.onStats?.(snapshot);
    options.onGateEvent?.({ type: 'stats', stats: snapshot });
  };
  const closeVad = () => {
    if (vadClosed) return;
    vadClosed = true;
    vadStream.close();
  };
  const closeVadInput = async () => {
    if (vadInputClosed) return;
    vadInputClosed = true;
    const writer = (vadStream as unknown as { inputWriter?: { close(): Promise<void> } })
      .inputWriter;
    try {
      // Agents JS 1.5.2 exposes a locked writer internally but endInput()
      // attempts to close the WritableStream itself. Closing that same writer
      // is the valid Web Streams operation and also lets Silero's task exit.
      if (writer) await writer.close();
      else vadStream.endInput();
    } catch {
      // A simultaneous room/session cancellation may already have closed it.
    }
  };
  const closeSpeech = () => {
    if (speechClosed) return;
    speechClosed = true;
    speechStream.close();
  };
  const safeFlush = () => {
    if (closed) return;
    speechStream.flush();
    stats.flushes++;
    emitGateEvent('flush');
  };
  const pushSttFrame = (frame: AudioFrame) => {
    const mono = downmixToMono(frame);
    if (mono.sampleRate === SARVAM_STT_SAMPLE_RATE) {
      speechStream.pushFrame(mono);
      return;
    }

    if (!sttResampler || sttInputSampleRate !== mono.sampleRate) {
      sttResampler?.close();
      sttInputSampleRate = mono.sampleRate;
      sttResampler = new AudioResampler(
        mono.sampleRate,
        SARVAM_STT_SAMPLE_RATE,
        1,
        AudioResamplerQuality.MEDIUM,
      );
    }
    for (const resampled of sttResampler.push(mono)) speechStream.pushFrame(resampled);
  };

  const consumeVad = async () => {
    for await (const event of vadStream) {
      if (event.type === VADEventType.START_OF_SPEECH) {
        if (speaking) continue;
        speaking = true;
        stats.speechStarts++;
        emitGateEvent('speech_start');
        for (const frame of preRoll.drain()) {
          pushSttFrame(frame);
          stats.forwardedFrames++;
        }
      } else if (event.type === VADEventType.END_OF_SPEECH) {
        if (!speaking) continue;
        speaking = false;
        stats.speechEnds++;
        safeFlush();
        stats.droppedFrames += preRoll.clear();
        emitGateEvent('speech_end');
      }
    }
  };

  const forwardAudio = async () => {
    const vadTask = consumeVad();
    try {
      for await (const frame of streamValues(audio)) {
        if (closed) break;
        stats.receivedFrames++;
        vadStream.pushFrame(frame);
        if (speaking) {
          pushSttFrame(frame);
          stats.forwardedFrames++;
        } else {
          stats.droppedFrames += preRoll.push(frame);
        }
      }

      if (closed) return;

      await closeVadInput();
      closeVad();
      await vadTask;
      if (speaking) safeFlush();
      stats.droppedFrames += preRoll.clear();
      if (sttResampler) {
        for (const residual of sttResampler.flush()) speechStream.pushFrame(residual);
      }
      speechStream.endInput();
      if (options.endOfInputDrainMs !== undefined) {
        await delay(Math.max(0, options.endOfInputDrainMs));
        closeSpeech();
      }
    } catch (error) {
      await closeVadInput();
      closeVad();
      closeSpeech();
      throw error;
    } finally {
      sttResampler?.close();
      emitStats();
    }
  };

  return new ReadableStream<stt.SpeechEvent | string>({
    start(controller) {
      void (async () => {
        forwardTask = forwardAudio();
        try {
          for await (const event of speechStream) {
            options.onSpeechEvent?.(event);
            controller.enqueue(event);
          }
          await forwardTask;
          if (!closed) controller.close();
        } catch (error) {
          await forwardTask.catch(() => undefined);
          if (!closed) controller.error(error);
        } finally {
          closed = true;
          closeVad();
          closeSpeech();
        }
      })();
    },
    async cancel(reason) {
      closed = true;
      await closeVadInput();
      closeVad();
      closeSpeech();
      if (audio instanceof ReadableStream) await audio.cancel(reason).catch(() => undefined);
      await forwardTask?.catch(() => undefined);
    },
  });
}

async function* streamValues<T>(
  source: ReadableStream<T> | AsyncIterable<T>,
): AsyncGenerator<T> {
  if (!(source instanceof ReadableStream)) {
    yield* source;
    return;
  }

  const reader = source.getReader();
  try {
    while (true) {
      const result = await reader.read();
      if (result.done) break;
      yield result.value;
    }
  } finally {
    reader.releaseLock();
  }
}

function frameDurationMs(frame: AudioFrame): number {
  return frame.sampleRate > 0 ? (frame.samplesPerChannel / frame.sampleRate) * 1000 : 0;
}

function delay(milliseconds: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

function downmixToMono(frame: AudioFrame): AudioFrame {
  if (frame.channels === 1) return frame;
  const mono = new Int16Array(frame.samplesPerChannel);
  for (let sample = 0; sample < frame.samplesPerChannel; sample++) {
    let total = 0;
    for (let channel = 0; channel < frame.channels; channel++) {
      total += frame.data[sample * frame.channels + channel] ?? 0;
    }
    mono[sample] = Math.max(-32768, Math.min(32767, Math.round(total / frame.channels)));
  }
  return new AudioFrame(mono, frame.sampleRate, 1, frame.samplesPerChannel, frame.userdata);
}
