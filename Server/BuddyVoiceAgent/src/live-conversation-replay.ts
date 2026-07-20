import {
  AudioSource,
  LocalAudioTrack,
  Room,
  RoomEvent,
  TrackPublishOptions,
  TrackSource,
  dispose,
} from '@livekit/rtc-node';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { paceFrames, readPcm16Wav } from './audio.js';
import { loadEnvironment } from './config.js';

loadEnvironment();

const firebaseApiKey = requiredEnvironment('BUDDY_FIREBASE_API_KEY');
const functionsBaseUrl = 'https://asia-south1-the-script-dea4f.cloudfunctions.net';
const debugTopic = 'buddy.debug_event';
const here = path.dirname(fileURLToPath(import.meta.url));
const sourceRoot = path.resolve(here, '..');
const fixtureNames = ['hindi-articles-input.wav'];

type DebugEvent = {
  stage?: string;
  type?: string;
  text?: string;
  message?: string;
  details?: Record<string, unknown>;
};

type FirebaseAuth = { idToken: string; localId: string };
type Admission = { serverUrl: string; token: string; voiceSessionId: string };

async function main(): Promise<void> {
  const email = requiredEnvironment('BUDDY_E2E_EMAIL');
  const password = requiredEnvironment('BUDDY_E2E_PASSWORD');
  const auth = await signIn(email, password);
  const learner = await loadLearner(auth).catch(() => ({ schoolId: 'demo-school', studentId: 'demo-aarav' }));
  const admission = await callFunction<Admission>(auth.idToken, 'createBuddyVoiceSession', {
    schoolId: learner.schoolId,
    studentId: learner.studentId,
    dialogueTaskId: 'article-a',
    clientRequestId: `wav-replay-${Date.now().toString(36)}`,
    trigger: 'manual',
    learnerAttempt: '',
    safeRelationshipMemory: [],
  });

  const startedAt = performance.now();
  const events: Array<{ atMs: number; event: DebugEvent }> = [];
  const room = new Room();
  room.on(RoomEvent.DataReceived, (payload, _participant, _kind, topic) => {
    if (topic !== debugTopic) return;
    try {
      events.push({ atMs: Math.round(performance.now() - startedAt), event: JSON.parse(new TextDecoder().decode(payload)) as DebugEvent });
    } catch {
      // A malformed debug packet must not hide the call result.
    }
  });

  try {
    await room.connect(admission.serverUrl, admission.token, { autoSubscribe: true, dynacast: true });
    await waitForEvent(events, event => event.stage === 'agent' && event.type === 'agent_joined', 45_000, 'Buddy worker join');
    await waitForEvent(events, event => event.stage === 'llm' && event.type === 'text_final', 45_000, 'Buddy opening reply');
    await delay(4_000);

    const results: Array<Record<string, unknown>> = [];
    for (const fixtureName of fixtureNames) {
      const before = events.length;
      const turnStartedAt = performance.now();
      await publishFixture(room, path.join(sourceRoot, 'test-output', fixtureName));
      const stt = await waitForEvent(
        events,
        event => event.stage === 'stt' && event.type === 'final',
        35_000,
        `${fixtureName} STT final`,
        before,
      );
      const reply = await waitForEvent(
        events,
        event => event.stage === 'llm' && event.type === 'text_final',
        45_000,
        `${fixtureName} Buddy reply`,
        before,
      );
      const gate = events.slice(before).filter(item => item.event.stage === 'vad' && item.event.type === 'flush').at(-1);
      results.push({
        fixture: fixtureName,
        sttFinal: stt.event.text ?? '',
        buddyReply: reply.event.text ?? '',
        sttFinalMs: Math.round(stt.atMs - (turnStartedAt - startedAt)),
        buddyReplyFinalMs: Math.round(reply.atMs - (turnStartedAt - startedAt)),
        gate: gate?.event.details ?? null,
      });
    }
    console.log(JSON.stringify({ passed: true, results }, null, 2));
  } finally {
    await room.disconnect().catch(() => undefined);
    await callFunction(auth.idToken, 'closeBuddyVoiceSession', {
      schoolId: learner.schoolId,
      studentId: learner.studentId,
      voiceSessionId: admission.voiceSessionId,
    }).catch(() => undefined);
    await dispose();
  }
}

async function publishFixture(room: Room, fixturePath: string): Promise<void> {
  const frames = await readPcm16Wav(fixturePath);
  const first = frames[0];
  if (!first || !room.localParticipant) throw new Error('Room is not ready to publish fixture audio.');
  const source = new AudioSource(first.sampleRate, first.channels);
  const track = LocalAudioTrack.createAudioTrack(`wav-${path.basename(fixturePath)}`, source);
  const options = new TrackPublishOptions();
  options.source = TrackSource.SOURCE_MICROPHONE;
  const publication = await room.localParticipant.publishTrack(track, options);
  try {
    for await (const frame of paceFrames(frames)) await source.captureFrame(frame);
    await source.waitForPlayout();
  } finally {
    await room.localParticipant.unpublishTrack(publication.sid ?? track.sid ?? '').catch(() => undefined);
    await track.close().catch(() => undefined);
    await source.close().catch(() => undefined);
  }
}

async function waitForEvent(
  events: Array<{ atMs: number; event: DebugEvent }>,
  predicate: (event: DebugEvent) => boolean,
  timeoutMs: number,
  description: string,
  startIndex = 0,
): Promise<{ atMs: number; event: DebugEvent }> {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const match = events.slice(startIndex).find(item => predicate(item.event));
    if (match) return match;
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  throw new Error(`Timed out waiting for ${description}.`);
}

async function signIn(email: string, password: string): Promise<FirebaseAuth> {
  const response = await fetch(`https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=${firebaseApiKey}`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email, password, returnSecureToken: true }),
  });
  const body = await response.json() as { idToken?: string; localId?: string; error?: { message?: string } };
  if (!response.ok || !body.idToken || !body.localId) throw new Error(`Firebase sign-in failed: ${body.error?.message ?? response.statusText}`);
  return { idToken: body.idToken, localId: body.localId };
}

async function loadLearner(auth: FirebaseAuth): Promise<{ schoolId: string; studentId: string }> {
  const url = `https://firestore.googleapis.com/v1/projects/the-script-dea4f/databases/(default)/documents/users/${encodeURIComponent(auth.localId)}`;
  const response = await fetch(url, { headers: { Authorization: `Bearer ${auth.idToken}` } });
  const body = await response.json() as { fields?: Record<string, { stringValue?: string; arrayValue?: { values?: Array<{ stringValue?: string }> } }> };
  const fields = body.fields ?? {};
  const schoolId = fields.schoolId?.stringValue;
  const studentId = fields.studentId?.stringValue ?? fields.studentIds?.arrayValue?.values?.[0]?.stringValue;
  if (!schoolId || !studentId) throw new Error('Signed-in profile has no usable learner identifiers.');
  return { schoolId, studentId };
}

async function callFunction<T>(idToken: string, name: string, data: Record<string, unknown>): Promise<T> {
  const response = await fetch(`${functionsBaseUrl}/${name}`, {
    method: 'POST', headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${idToken}` }, body: JSON.stringify({ data }),
  });
  const body = await response.json() as { result?: T; error?: { message?: string } };
  if (!response.ok || body.error || body.result === undefined) throw new Error(`${name} failed: ${body.error?.message ?? response.statusText}`);
  return body.result;
}

function delay(milliseconds: number): Promise<void> {
  return new Promise(resolve => setTimeout(resolve, milliseconds));
}

function requiredEnvironment(name: string): string {
  const value = process.env[name]?.trim();
  if (!value) throw new Error(`${name} is required.`);
  return value;
}

void main().catch(error => {
  console.error(error instanceof Error ? error.message : String(error));
  process.exitCode = 1;
});
