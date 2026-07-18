import assert from "node:assert/strict";
import test from "node:test";
import { TokenVerifier } from "livekit-server-sdk";
import {
  createBuddyVoiceAccessToken,
  pruneBuddyVoiceLeases,
  type BuddyVoiceDispatchMetadata,
} from "../buddyVoiceAccess.js";

test("voice tokens isolate one learner and one explicitly dispatched agent per room", async () => {
  const metadata: BuddyVoiceDispatchMetadata = {
    schemaVersion: 1,
    voiceSessionId: "0123456789abcdef01234567",
    participantIdentity: "learner-0123456789abcdef",
    expiresAtEpochMs: Date.now() + 600_000,
    maxSessionSeconds: 600,
    homeLanguage: "hi",
    conceptId: "PastTense",
    taskPrompt: "Use the past tense.",
    conceptTitle: "Past tense",
    grammarPattern: "subject + past verb",
    zone: "Route",
    allowAnswerModel: false,
    openingTrigger: "ask",
    openingLearnerAttempt: "",
    explanationStyle: "short_then_expand",
    allowTransliteration: false,
    recommendedEnglishRatio: 0.45,
    supportBand: "Guided",
    strengthConceptIds: ["Articles"],
    needConceptIds: ["PastTense"],
    recurringErrorTags: ["verb_form"],
    safeRelationshipMemory: ["style:short"],
    groundingContext: "Curriculum: Past tense; use past verb forms. Learner pattern: recurring errors: verb_form.",
    groundingSourceIds: ["gameContentGrimoirePages/PastTense"],
  };
  const token = await createBuddyVoiceAccessToken({
    apiKey: "test-key",
    apiSecret: "test-secret-that-is-long-enough",
    agentName: "buddy-voice",
    roomName: "buddy-room-0123456789abcdef",
    participantIdentity: metadata.participantIdentity,
    metadata,
    tokenTtlSeconds: 660,
    roomEmptyTimeoutSeconds: 45,
    roomDepartureTimeoutSeconds: 20,
  });
  const grants = await new TokenVerifier("test-key", "test-secret-that-is-long-enough").verify(token);

  assert.equal(grants.video?.room, "buddy-room-0123456789abcdef");
  assert.equal(grants.video?.roomJoin, true);
  assert.equal(grants.video?.canSubscribe, true);
  assert.equal(grants.video?.canPublishData, true);
  assert.deepEqual(grants.video?.canPublishSources, ["microphone"]);
  assert.equal(grants.roomConfig?.maxParticipants, 2);
  assert.equal(grants.roomConfig?.agents.length, 1);
  assert.equal(grants.roomConfig?.agents[0]?.agentName, "buddy-voice");
  assert.deepEqual(JSON.parse(grants.roomConfig?.agents[0]?.metadata ?? "{}"), metadata);
});

test("expired and malformed capacity leases are pruned", () => {
  const now = 1_000_000;
  assert.deepEqual(pruneBuddyVoiceLeases({
    "0123456789abcdef01234567": { expiresAtEpochMs: now + 1000 },
    "abcdefabcdefabcdefabcdef": { expiresAtEpochMs: now - 1 },
    malformed: { expiresAtEpochMs: now + 1000 },
  }, now), {
    "0123456789abcdef01234567": { expiresAtEpochMs: now + 1000 },
  });
});
