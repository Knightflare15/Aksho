import { AudioFrame } from '@livekit/rtc-node';
import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';

export function silenceFrames(
  durationMs: number,
  sampleRate: number,
  channels = 1,
  frameDurationMs = 20,
): AudioFrame[] {
  const frames: AudioFrame[] = [];
  let remainingSamples = Math.round((durationMs * sampleRate) / 1000);
  const nominalSamples = Math.max(1, Math.round((frameDurationMs * sampleRate) / 1000));
  while (remainingSamples > 0) {
    const samples = Math.min(nominalSamples, remainingSamples);
    frames.push(AudioFrame.create(sampleRate, channels, samples));
    remainingSamples -= samples;
  }
  return frames;
}

export function whiteNoiseFrames(
  durationMs: number,
  sampleRate: number,
  channels = 1,
  amplitude = 120,
  seed = 0x5eed1234,
  frameDurationMs = 20,
): AudioFrame[] {
  const frames = silenceFrames(durationMs, sampleRate, channels, frameDurationMs);
  let state = seed >>> 0;
  for (const frame of frames) {
    const samples = new Int16Array(frame.data.buffer, frame.data.byteOffset, frame.data.byteLength / 2);
    for (let index = 0; index < samples.length; index++) {
      state ^= state << 13;
      state ^= state >>> 17;
      state ^= state << 5;
      samples[index] = Math.round((((state >>> 0) / 0xffffffff) * 2 - 1) * amplitude);
    }
  }
  return frames;
}

export async function* paceFrames(frames: readonly AudioFrame[]): AsyncGenerator<AudioFrame> {
  const startedAt = performance.now();
  let scheduledMs = 0;
  for (const frame of frames) {
    const waitMs = scheduledMs - (performance.now() - startedAt);
    if (waitMs > 1) await delay(waitMs);
    yield frame;
    scheduledMs += (frame.samplesPerChannel / frame.sampleRate) * 1000;
  }
}

export function audioDurationMs(frames: readonly AudioFrame[]): number {
  return frames.reduce(
    (total, frame) => total + (frame.samplesPerChannel / frame.sampleRate) * 1000,
    0,
  );
}

export async function writeWav(filePath: string, frames: readonly AudioFrame[]): Promise<void> {
  if (frames.length === 0) throw new Error('Cannot write an empty WAV file.');
  const sampleRate = frames[0]!.sampleRate;
  const channels = frames[0]!.channels;
  for (const frame of frames) {
    if (frame.sampleRate !== sampleRate || frame.channels !== channels) {
      throw new Error('All WAV frames must use the same sample rate and channel count.');
    }
  }

  const pcmBytes = frames.reduce((total, frame) => total + frame.data.byteLength, 0);
  const wav = Buffer.allocUnsafe(44 + pcmBytes);
  wav.write('RIFF', 0, 4, 'ascii');
  wav.writeUInt32LE(36 + pcmBytes, 4);
  wav.write('WAVE', 8, 4, 'ascii');
  wav.write('fmt ', 12, 4, 'ascii');
  wav.writeUInt32LE(16, 16);
  wav.writeUInt16LE(1, 20);
  wav.writeUInt16LE(channels, 22);
  wav.writeUInt32LE(sampleRate, 24);
  wav.writeUInt32LE(sampleRate * channels * 2, 28);
  wav.writeUInt16LE(channels * 2, 32);
  wav.writeUInt16LE(16, 34);
  wav.write('data', 36, 4, 'ascii');
  wav.writeUInt32LE(pcmBytes, 40);

  let offset = 44;
  for (const frame of frames) {
    const bytes = Buffer.from(frame.data.buffer, frame.data.byteOffset, frame.data.byteLength);
    bytes.copy(wav, offset);
    offset += bytes.length;
  }

  await mkdir(path.dirname(filePath), { recursive: true });
  await writeFile(filePath, wav);
}

function delay(milliseconds: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}
