import dotenv from 'dotenv';

export interface BuddyVoiceConfig {
  sarvamApiKey: string;
  agentName: string;
  llmModel: string;
  sttModel: 'saaras:v3';
  sttLanguage: string;
  sttMode: string;
  ttsModel: 'bulbul:v3';
  ttsSpeaker: string;
  ttsLanguage: string;
  vad: {
    activationThreshold: number;
    minSpeechDuration: number;
    minSilenceDuration: number;
    prefixPaddingDuration: number;
  };
  chunking: {
    minimumBufferCharacters: number;
    maximumChunkCharacters: number;
    minimumChunkWords: number;
  };
  endpointing: {
    minimumDelay: number;
    maximumDelay: number;
  };
  capacity: {
    maximumActiveSessions: number;
    idleProcesses: number;
    drainTimeoutMs: number;
    jobMemoryWarnMb: number;
    jobMemoryLimitMb: number;
  };
}

export function loadEnvironment(): void {
  dotenv.config({ path: process.env.BUDDY_ENV_FILE?.trim() || '.env' });
}

export function readBuddyVoiceConfig(options: { requireSarvamKey?: boolean } = {}): BuddyVoiceConfig {
  const requireSarvamKey = options.requireSarvamKey ?? true;
  const sarvamApiKey = process.env.SARVAM_API_KEY?.trim() ?? '';
  if (requireSarvamKey && !sarvamApiKey) {
    throw new Error('SARVAM_API_KEY is required for the Buddy voice worker and integration loop.');
  }

  return {
    sarvamApiKey,
    agentName: process.env.LIVEKIT_AGENT_NAME?.trim() || 'buddy-voice',
    // Keep realtime Buddy on the same higher-quality model as asynchronous
    // Buddy; provider latency is measured continuously by the voice tests.
    llmModel: process.env.SARVAM_VOICE_MODEL?.trim() || 'sarvam-105b',
    sttModel: 'saaras:v3',
    sttLanguage: process.env.SARVAM_STT_LANGUAGE?.trim() || 'unknown',
    sttMode: process.env.SARVAM_STT_MODE?.trim() || 'codemix',
    ttsModel: 'bulbul:v3',
    ttsSpeaker: process.env.SARVAM_TTS_SPEAKER?.trim() || 'shubh',
    ttsLanguage: process.env.SARVAM_TTS_LANGUAGE?.trim() || 'hi-IN',
    vad: {
      activationThreshold: environmentNumber('BUDDY_VAD_ACTIVATION_THRESHOLD', 0.5, 0.1, 0.95),
      minSpeechDuration: environmentNumber('BUDDY_VAD_MIN_SPEECH_MS', 120, 32, 2000),
      minSilenceDuration: environmentNumber('BUDDY_VAD_MIN_SILENCE_MS', 360, 100, 3000),
      prefixPaddingDuration: environmentNumber('BUDDY_VAD_PREFIX_PADDING_MS', 240, 0, 1500),
    },
    chunking: {
      minimumBufferCharacters: environmentInteger('BUDDY_TTS_MIN_BUFFER_CHARS', 24, 8, 200),
      maximumChunkCharacters: environmentInteger('BUDDY_TTS_MAX_CHUNK_CHARS', 140, 40, 500),
      minimumChunkWords: environmentInteger('BUDDY_TTS_MIN_CHUNK_WORDS', 3, 1, 20),
    },
    endpointing: {
      minimumDelay: environmentInteger('BUDDY_ENDPOINT_MIN_DELAY_MS', 360, 100, 3000),
      maximumDelay: environmentInteger('BUDDY_ENDPOINT_MAX_DELAY_MS', 1100, 300, 5000),
    },
    capacity: {
      maximumActiveSessions: environmentInteger('BUDDY_MAX_ACTIVE_SESSIONS', 8, 1, 100),
      idleProcesses: environmentInteger('BUDDY_IDLE_PROCESSES', 4, 1, 32),
      drainTimeoutMs: environmentInteger('BUDDY_DRAIN_TIMEOUT_MS', 840000, 60000, 1800000),
      jobMemoryWarnMb: environmentInteger('BUDDY_JOB_MEMORY_WARN_MB', 768, 128, 4096),
      jobMemoryLimitMb: environmentInteger('BUDDY_JOB_MEMORY_LIMIT_MB', 1024, 256, 8192),
    },
  };
}

function environmentInteger(name: string, fallback: number, minimum: number, maximum: number): number {
  return Math.round(environmentNumber(name, fallback, minimum, maximum));
}

function environmentNumber(name: string, fallback: number, minimum: number, maximum: number): number {
  const parsed = Number(process.env[name]);
  const value = Number.isFinite(parsed) ? parsed : fallback;
  return Math.max(minimum, Math.min(maximum, value));
}
