export interface TextChunkingOptions {
  minimumBufferCharacters: number;
  maximumChunkCharacters: number;
  minimumChunkWords: number;
}

const sentenceBoundary = /[.!?।॥](?:["'’”)}\]]+)?\s*$/u;
const clauseBoundary = /[,;:—](?:["'’”)}\]]+)?\s*$/u;

/**
 * Turns arbitrary LLM token fragments into small, speakable clauses. It waits
 * for a few words so prosody is stable, but never lets a long punctuation-free
 * answer hold the TTS stream hostage.
 */
export class StreamingTextChunker {
  private buffer = '';

  constructor(private readonly options: TextChunkingOptions) {
    if (options.minimumBufferCharacters <= 0 || options.maximumChunkCharacters <= 0) {
      throw new Error('Text chunk sizes must be positive.');
    }
    if (options.minimumBufferCharacters > options.maximumChunkCharacters) {
      throw new Error('minimumBufferCharacters cannot exceed maximumChunkCharacters.');
    }
  }

  push(fragment: string): string[] {
    if (!fragment) return [];
    this.buffer += fragment;
    return this.drain(false);
  }

  end(): string[] {
    const chunks = this.drain(true);
    const remainder = this.buffer.trim();
    this.buffer = '';
    if (hasSpeakableContent(remainder)) chunks.push(remainder);
    return chunks;
  }

  private drain(final: boolean): string[] {
    const chunks: string[] = [];
    while (this.buffer.trim()) {
      const boundary = this.findBoundary(final);
      if (boundary <= 0) break;

      const chunk = this.buffer.slice(0, boundary).trim();
      this.buffer = this.buffer.slice(boundary).trimStart();
      if (hasSpeakableContent(chunk)) chunks.push(chunk);
    }
    return chunks;
  }

  private findBoundary(final: boolean): number {
    const max = Math.min(this.options.maximumChunkCharacters, this.buffer.length);
    const candidate = this.buffer.slice(0, max);
    const enoughWords = wordCount(candidate) >= this.options.minimumChunkWords;
    const enoughCharacters = candidate.trim().length >= this.options.minimumBufferCharacters;

    if (enoughWords && enoughCharacters) {
      const sentenceIndex = lastBoundaryIndex(candidate, sentenceBoundary);
      if (sentenceIndex > 0) return sentenceIndex;

      const clauseIndex = lastBoundaryIndex(candidate, clauseBoundary);
      if (clauseIndex > 0) return clauseIndex;
    }

    if (this.buffer.length >= this.options.maximumChunkCharacters) {
      const whitespace = candidate.lastIndexOf(' ');
      return whitespace >= Math.floor(this.options.maximumChunkCharacters * 0.55)
        ? whitespace + 1
        : max;
    }

    return final ? this.buffer.length : 0;
  }
}

export function chunkTextStream(
  source: ReadableStream<string> | AsyncIterable<string>,
  options: TextChunkingOptions,
): ReadableStream<string> {
  const chunker = new StreamingTextChunker(options);

  return new ReadableStream<string>({
    start(controller) {
      void (async () => {
        try {
          for await (const fragment of streamValues(source)) {
            for (const chunk of chunker.push(fragment)) controller.enqueue(chunk);
          }
          for (const chunk of chunker.end()) controller.enqueue(chunk);
          controller.close();
        } catch (error) {
          controller.error(error);
        }
      })();
    },
    cancel(reason) {
      return source instanceof ReadableStream
        ? source.cancel(reason).catch(() => undefined)
        : undefined;
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

function wordCount(value: string): number {
  return value.trim().split(/\s+/u).filter(Boolean).length;
}

function hasSpeakableContent(value: string): boolean {
  return /[\p{L}\p{N}]/u.test(value);
}

function lastBoundaryIndex(value: string, pattern: RegExp): number {
  for (let index = value.length; index > 0; index--) {
    if (pattern.test(value.slice(0, index))) return index;
  }
  return -1;
}
