import { AudioFrame } from '@livekit/rtc-node';
import { VADEventType, stt, type VAD } from '@livekit/agents';
import assert from 'node:assert/strict';
import test from 'node:test';
import {
  AudioFrameRingBuffer,
  createVadGatedSttStream,
  type VadGateStats,
} from '../vad-gated-stt.js';

test('the VAD pre-roll ring stays bounded and drains in order', () => {
  const ring = new AudioFrameRingBuffer(60);
  const frames = [1, 2, 3, 4, 5].map((value) => {
    const frame = AudioFrame.create(16000, 1, 320);
    frame.data.fill(value);
    return frame;
  });

  let dropped = 0;
  for (const frame of frames) dropped += ring.push(frame);
  assert.equal(dropped, 2);
  assert.equal(ring.length, 3);
  assert.deepEqual(ring.drain().map((frame) => frame.data[0]), [3, 4, 5]);
  assert.equal(ring.length, 0);
});

test('local VAD forwards pre-roll and speech while dropping extended silence', async () => {
  const vadEvents = new AsyncQueue<Record<string, unknown>>();
  const speechEvents = new AsyncQueue<stt.SpeechEvent>();
  const forwarded: AudioFrame[] = [];
  let vadFrameCount = 0;
  let flushes = 0;

  const vadStream = {
    pushFrame() {
      vadFrameCount++;
      if (vadFrameCount === 2) vadEvents.push(vadEvent(VADEventType.START_OF_SPEECH));
      if (vadFrameCount === 6) vadEvents.push(vadEvent(VADEventType.END_OF_SPEECH));
    },
    endInput() { vadEvents.end(); },
    close() { vadEvents.end(); },
    [Symbol.asyncIterator]() { return vadEvents; },
  };
  const speechStream = {
    pushFrame(frame: AudioFrame) { forwarded.push(frame); },
    flush() { flushes++; },
    endInput() { speechEvents.end(); },
    close() { speechEvents.end(); },
    [Symbol.asyncIterator]() { return speechEvents; },
  };
  const fakeVad = { stream: () => vadStream } as unknown as VAD;
  const fakeStt = {
    capabilities: { streaming: true, interimResults: true },
    stream: () => speechStream,
  } as unknown as stt.STT;
  let stats: VadGateStats | undefined;

  const stream = createVadGatedSttStream(testFrames(10), fakeStt, fakeVad, {
    prefixPaddingMs: 40,
    onStats: (value) => { stats = value; },
  });
  for await (const _event of stream) {
    // The fake STT records audio only; provider transcript events are irrelevant here.
  }

  assert.equal(forwarded.length, 6);
  assert.equal(flushes, 1);
  assert.deepEqual(stats, {
    receivedFrames: 10,
    forwardedFrames: 6,
    droppedFrames: 4,
    flushes: 1,
    speechStarts: 1,
    speechEnds: 1,
  });
});

function vadEvent(type: VADEventType): Record<string, unknown> {
  return {
    type,
    samplesIndex: 0,
    timestamp: Date.now(),
    speechDuration: 0,
    silenceDuration: 0,
    frames: [],
    probability: 1,
    inferenceDuration: 0,
    speaking: type === VADEventType.START_OF_SPEECH,
    rawAccumulatedSilence: 0,
    rawAccumulatedSpeech: 0,
  };
}

async function* testFrames(count: number): AsyncGenerator<AudioFrame> {
  for (let index = 0; index < count; index++) {
    yield AudioFrame.create(16000, 1, 320);
    await new Promise<void>((resolve) => setImmediate(resolve));
  }
}

class AsyncQueue<T> implements AsyncIterableIterator<T> {
  private values: T[] = [];
  private waiters: Array<(result: IteratorResult<T>) => void> = [];
  private ended = false;

  push(value: T): void {
    const waiter = this.waiters.shift();
    if (waiter) waiter({ done: false, value });
    else this.values.push(value);
  }

  end(): void {
    if (this.ended) return;
    this.ended = true;
    for (const waiter of this.waiters.splice(0)) waiter({ done: true, value: undefined });
  }

  next(): Promise<IteratorResult<T>> {
    const value = this.values.shift();
    if (value !== undefined) return Promise.resolve({ done: false, value });
    if (this.ended) return Promise.resolve({ done: true, value: undefined });
    return new Promise((resolve) => this.waiters.push(resolve));
  }

  [Symbol.asyncIterator](): AsyncIterableIterator<T> {
    return this;
  }
}
