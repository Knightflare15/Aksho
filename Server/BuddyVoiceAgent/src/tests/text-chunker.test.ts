import assert from 'node:assert/strict';
import test from 'node:test';
import { StreamingTextChunker } from '../text-chunker.js';

const options = {
  minimumBufferCharacters: 12,
  maximumChunkCharacters: 48,
  minimumChunkWords: 3,
};

test('streams a complete Hindi-English sentence as soon as punctuation arrives', () => {
  const chunker = new StreamingTextChunker(options);
  assert.deepEqual(chunker.push('यह एक small grammar '), []);
  assert.deepEqual(chunker.push('hint है। Next'), ['यह एक small grammar hint है।']);
  assert.deepEqual(chunker.end(), ['Next']);
});

test('bounds punctuation-free output at a word boundary', () => {
  const chunker = new StreamingTextChunker(options);
  const chunks = chunker.push('one two three four five six seven eight nine ten eleven twelve');
  assert.equal(chunks.length, 1);
  assert.ok(chunks[0]!.length <= options.maximumChunkCharacters);
  assert.equal(chunks[0]!.endsWith(' '), false);
  assert.ok(chunker.end().length > 0);
});

test('rejects an invalid buffer configuration', () => {
  assert.throws(
    () => new StreamingTextChunker({ ...options, minimumBufferCharacters: 60 }),
    /cannot exceed/u,
  );
});

test('does not emit a punctuation-only trailing chunk', () => {
  const chunker = new StreamingTextChunker(options);
  assert.deepEqual(chunker.push('The teaching word is walks!'), ['The teaching word is walks!']);
  assert.deepEqual(chunker.push('.'), []);
  assert.deepEqual(chunker.end(), []);
});
