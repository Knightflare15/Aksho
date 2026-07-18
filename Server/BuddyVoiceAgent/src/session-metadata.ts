import { z } from 'zod';

const buddyJobMetadataSchema = z.object({
  schemaVersion: z.literal(1),
  voiceSessionId: z.string().regex(/^[a-f0-9]{24}$/i),
  participantIdentity: z.string().regex(/^learner-[a-f0-9]{20}$/i),
  expiresAtEpochMs: z.number().int().positive(),
  maxSessionSeconds: z.number().int().min(60).max(1800),
  homeLanguage: z.string().regex(/^[a-z]{2,3}(?:-[A-Za-z]{2,8})?$/).max(16),
  conceptId: z.string().max(96).default(''),
  taskPrompt: z.string().max(400),
  conceptTitle: z.string().max(100),
  grammarPattern: z.string().max(64),
  zone: z.enum(['Town', 'Route']),
  allowAnswerModel: z.boolean(),
  openingTrigger: z.enum(['ask', 'wrong_answer', 'word_meaning']).default('ask'),
  openingLearnerAttempt: z.string().max(240).default(''),
  explanationStyle: z.string().max(48).default('short_then_expand'),
  allowTransliteration: z.boolean().default(false),
  recommendedEnglishRatio: z.number().min(0).max(1).default(0.3),
  supportBand: z.string().max(32).default('Foundation'),
  strengthConceptIds: z.array(z.string().max(96)).max(3).default([]),
  needConceptIds: z.array(z.string().max(96)).max(3).default([]),
  recurringErrorTags: z.array(z.string().max(64)).max(5).default([]),
  safeRelationshipMemory: z.array(z.string().max(48)).max(12).default([]),
  groundingContext: z.string().max(900).default(''),
  groundingSourceIds: z.array(z.string().max(200)).max(8).default([]),
}).strict();

const buddyGameEventSchema = z.object({
  schemaVersion: z.literal(1),
  type: z.enum(['wrong_answer', 'task_answered', 'word_meaning']),
  learnerAttempt: z.string().max(240).default(''),
  conceptId: z.string().max(96).default(''),
  turnIndex: z.number().int().min(0).max(1000),
}).strict();

export type BuddyJobMetadata = z.infer<typeof buddyJobMetadataSchema>;
export type BuddyGameEvent = z.infer<typeof buddyGameEventSchema>;

export function parseBuddyJobMetadata(raw: string, nowEpochMs = Date.now()): BuddyJobMetadata {
  if (!raw || Buffer.byteLength(raw, 'utf8') > 6144) {
    throw new Error('Buddy job metadata is missing or too large.');
  }
  let decoded: unknown;
  try {
    decoded = JSON.parse(raw);
  } catch {
    throw new Error('Buddy job metadata is not valid JSON.');
  }
  const metadata = buddyJobMetadataSchema.parse(decoded);
  if (metadata.expiresAtEpochMs <= nowEpochMs) {
    throw new Error('Buddy voice session has expired.');
  }
  const maximumFutureExpiry = nowEpochMs + (metadata.maxSessionSeconds + 120) * 1000;
  if (metadata.expiresAtEpochMs > maximumFutureExpiry) {
    throw new Error('Buddy voice session expiry exceeds its signed duration.');
  }
  if (metadata.zone === 'Route' && metadata.allowAnswerModel) {
    throw new Error('Route sessions cannot enable direct-answer behavior.');
  }
  return metadata;
}

export function parseBuddyGameEvent(raw: Uint8Array): BuddyGameEvent {
  if (raw.byteLength === 0 || raw.byteLength > 1024) {
    throw new Error('Buddy game event is missing or too large.');
  }
  let decoded: unknown;
  try {
    decoded = JSON.parse(Buffer.from(raw).toString('utf8'));
  } catch {
    throw new Error('Buddy game event is not valid JSON.');
  }
  return buddyGameEventSchema.parse(decoded);
}

export function buddyWorkerLoad(activeJobs: number, maximumActiveSessions: number): number {
  if (!Number.isFinite(activeJobs) || !Number.isFinite(maximumActiveSessions) || maximumActiveSessions <= 0) {
    return 1;
  }
  return Math.max(0, Math.min(1, Math.floor(activeJobs) / Math.floor(maximumActiveSessions)));
}
