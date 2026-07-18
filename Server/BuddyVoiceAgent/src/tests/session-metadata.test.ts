import assert from 'node:assert/strict';
import test from 'node:test';
import { buddyWorkerLoad, parseBuddyGameEvent, parseBuddyJobMetadata } from '../session-metadata.js';

function metadata(index: number, now: number) {
  return JSON.stringify({
    schemaVersion: 1,
    voiceSessionId: index.toString(16).padStart(24, '0'),
    participantIdentity: `learner-${index.toString(16).padStart(20, '0')}`,
    expiresAtEpochMs: now + 600_000,
    maxSessionSeconds: 600,
    homeLanguage: index % 2 === 0 ? 'hi' : 'en-IN',
    taskPrompt: `private-watermark-${index}`,
    conceptTitle: `concept-${index}`,
    grammarPattern: index % 2 === 0 ? 'is/are' : 'has/have',
    zone: index % 3 === 0 ? 'Town' : 'Route',
    allowAnswerModel: index % 3 === 0,
    groundingContext: `grounding-watermark-${index}: Grimoire rule and learner pattern stay scoped to this room.`,
    groundingSourceIds: [`gameContentDialogueTasks/task-${index}`],
  });
}

test('strict signed metadata rejects expired, unknown, and unsafe route jobs', () => {
  const now = 1_800_000_000_000;
  const expired = JSON.parse(metadata(1, now));
  expired.expiresAtEpochMs = now - 1;
  assert.throws(() => parseBuddyJobMetadata(JSON.stringify(expired), now), /expired/i);

  const unknown = JSON.parse(metadata(2, now));
  unknown.schoolId = 'must-not-be-client-authored';
  assert.throws(() => parseBuddyJobMetadata(JSON.stringify(unknown), now));

  const route = JSON.parse(metadata(4, now));
  route.allowAnswerModel = true;
  assert.throws(() => parseBuddyJobMetadata(JSON.stringify(route), now), /Route sessions/i);
});

test('64 simultaneous learner contexts retain their own prompt watermark', async () => {
  const now = 1_800_000_000_000;
  const sessions = await Promise.all(Array.from({ length: 64 }, async (_, index) => {
    await Promise.resolve();
    return parseBuddyJobMetadata(metadata(index + 1, now), now);
  }));

  assert.equal(new Set(sessions.map(session => session.voiceSessionId)).size, 64);
  for (const [index, session] of sessions.entries()) {
    assert.equal(session.taskPrompt, `private-watermark-${index + 1}`);
    assert.equal(session.groundingContext.includes(`grounding-watermark-${index + 1}`), true);
    assert.equal(session.participantIdentity, `learner-${(index + 1).toString(16).padStart(20, '0')}`);
  }
});

test('grounding context is bounded and source ids are schema checked', () => {
  const now = 1_800_000_000_000;
  const valid = parseBuddyJobMetadata(metadata(7, now), now);
  assert.deepEqual(valid.groundingSourceIds, ['gameContentDialogueTasks/task-7']);

  const oversized = JSON.parse(metadata(8, now));
  oversized.groundingContext = 'x'.repeat(901);
  assert.throws(() => parseBuddyJobMetadata(JSON.stringify(oversized), now));

  const tooManySources = JSON.parse(metadata(9, now));
  tooManySources.groundingSourceIds = Array.from({ length: 9 }, (_, index) => `source/${index}`);
  assert.throws(() => parseBuddyJobMetadata(JSON.stringify(tooManySources), now));
});

test('active-job load reaches the dispatch cutoff at configured capacity', () => {
  assert.equal(buddyWorkerLoad(0, 8), 0);
  assert.equal(buddyWorkerLoad(4, 8), 0.5);
  assert.equal(buddyWorkerLoad(8, 8), 1);
  assert.equal(buddyWorkerLoad(12, 8), 1);
  assert.equal(buddyWorkerLoad(1, 0), 1);
});

test('in-call game events accept only the bounded trusted schema', () => {
  const event = parseBuddyGameEvent(Buffer.from(JSON.stringify({
    schemaVersion: 1,
    type: 'word_meaning',
    learnerAttempt: 'What does enchanted mean here?',
    conceptId: 'Adjectives',
    turnIndex: 2,
  })));
  assert.equal(event.type, 'word_meaning');
  assert.equal(event.turnIndex, 2);

  assert.throws(() => parseBuddyGameEvent(Buffer.from(JSON.stringify({
    ...event,
    rawConversation: 'must not cross the data boundary',
  }))));
});
