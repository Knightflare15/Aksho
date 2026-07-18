import assert from 'node:assert/strict';
import test from 'node:test';
import { StreamingEmphasisAdapter } from '../emphasis.js';

test('converts a fragmented emphasis directive without leaking markup', () => {
  const adapter = new StreamingEmphasisAdapter();
  const fragments = [
    ...adapter.push('Say [[em'),
    ...adapter.push('phasis:walks'),
    ...adapter.push(']] because the subject is he.'),
    ...adapter.end(),
  ];

  assert.equal(fragments.join(''), 'Say walks! because the subject is he.');
  assert.deepEqual(adapter.emphasizedTerms, ['walks']);
});

test('passes ordinary streamed text through unchanged', () => {
  const adapter = new StreamingEmphasisAdapter();
  assert.equal([...adapter.push('A normal '), ...adapter.end()].join(''), 'A normal ');
});

test('removes an unfinished directive at end of stream', () => {
  const adapter = new StreamingEmphasisAdapter();
  assert.equal([...adapter.push('Use [[emphasis:walked'), ...adapter.end()].join(''), 'Use walked');
});

test('emphasizes the configured concept term when the LLM emits no directive', () => {
  const adapter = new StreamingEmphasisAdapter('walked');
  const fragments = [
    ...adapter.push('The past form is wal'),
    ...adapter.push('ked, not walk.'),
    ...adapter.end(),
  ];

  assert.equal(fragments.join(''), 'The past form is walked!, not walk.');
  assert.deepEqual(adapter.emphasizedTerms, ['walked']);
});

test('does not emphasize a concept term inside another word', () => {
  const adapter = new StreamingEmphasisAdapter('under');
  assert.equal([...adapter.push('Understand this.'), ...adapter.end()].join(''), 'Understand this.');
  assert.deepEqual(adapter.emphasizedTerms, []);
});

test('replaces sentence punctuation after an emphasized term instead of doubling it', () => {
  const adapter = new StreamingEmphasisAdapter('walks');
  assert.equal([...adapter.push('He walks.'), ...adapter.end()].join(''), 'He walks!');
});
