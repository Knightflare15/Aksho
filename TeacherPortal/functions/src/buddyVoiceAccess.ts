import {
  AccessToken,
  RoomAgentDispatch,
  TrackSource,
} from "livekit-server-sdk";
import { RoomConfiguration } from "@livekit/protocol";

export const buddyVoiceMetadataSchemaVersion = 1;

export interface BuddyVoiceDispatchMetadata {
  schemaVersion: 1;
  voiceSessionId: string;
  participantIdentity: string;
  expiresAtEpochMs: number;
  maxSessionSeconds: number;
  homeLanguage: string;
  conceptId: string;
  taskPrompt: string;
  conceptTitle: string;
  grammarPattern: string;
  zone: "Town" | "Route";
  allowAnswerModel: boolean;
  openingTrigger: "ask" | "wrong_answer" | "word_meaning";
  openingLearnerAttempt: string;
  explanationStyle: string;
  allowTransliteration: boolean;
  recommendedEnglishRatio: number;
  supportBand: string;
  strengthConceptIds: string[];
  needConceptIds: string[];
  recurringErrorTags: string[];
  safeRelationshipMemory: string[];
  groundingContext: string;
  groundingSourceIds: string[];
}

export interface BuddyVoiceTokenRequest {
  apiKey: string;
  apiSecret: string;
  agentName: string;
  roomName: string;
  participantIdentity: string;
  metadata: BuddyVoiceDispatchMetadata;
  tokenTtlSeconds: number;
  roomEmptyTimeoutSeconds: number;
  roomDepartureTimeoutSeconds: number;
}

export interface BuddyVoiceLease {
  expiresAtEpochMs: number;
}

/**
 * Creates a room-scoped learner token. The signed room configuration performs
 * explicit dispatch, so clients cannot change agent metadata or route a job to
 * another learner's room.
 */
export async function createBuddyVoiceAccessToken(input: BuddyVoiceTokenRequest): Promise<string> {
  const token = new AccessToken(input.apiKey, input.apiSecret, {
    identity: input.participantIdentity,
    ttl: input.tokenTtlSeconds,
    attributes: {
      buddy_voice_session: input.metadata.voiceSessionId,
      buddy_client_kind: "learner",
    },
  });
  token.addGrant({
    roomJoin: true,
    room: input.roomName,
    canPublish: true,
    canPublishSources: [TrackSource.MICROPHONE],
    canSubscribe: true,
    // Authenticated game-state events stay inside the same realtime call. The
    // worker accepts only a tiny allow-listed schema from this participant.
    canPublishData: true,
    canUpdateOwnMetadata: false,
  });
  token.roomConfig = new RoomConfiguration({
    name: input.roomName,
    emptyTimeout: input.roomEmptyTimeoutSeconds,
    departureTimeout: input.roomDepartureTimeoutSeconds,
    maxParticipants: 2,
    agents: [
      new RoomAgentDispatch({
        agentName: input.agentName,
        metadata: JSON.stringify(input.metadata),
      }),
    ],
  });
  return token.toJwt();
}

export function pruneBuddyVoiceLeases(
  value: unknown,
  nowEpochMs: number,
): Record<string, BuddyVoiceLease> {
  if (typeof value !== "object" || value === null || Array.isArray(value)) return {};
  const result: Record<string, BuddyVoiceLease> = {};
  for (const [sessionId, raw] of Object.entries(value as Record<string, unknown>)) {
    if (!/^[a-f0-9]{24}$/i.test(sessionId) || typeof raw !== "object" || raw === null) continue;
    const expiresAtEpochMs = Number((raw as Record<string, unknown>).expiresAtEpochMs);
    if (!Number.isFinite(expiresAtEpochMs) || expiresAtEpochMs <= nowEpochMs) continue;
    result[sessionId] = { expiresAtEpochMs: Math.round(expiresAtEpochMs) };
  }
  return result;
}
