export interface AzurePronunciationRestOptions {
  subscriptionKey: string;
  region: string;
  language: string;
  referenceText: string;
  timeoutMs: number;
  maximumAttempts: number;
  fetchImpl?: typeof fetch;
  random?: () => number;
}

export interface PcmWavInfo {
  sampleRate: number;
  channels: number;
  bitsPerSample: number;
  audioFormat: number;
  dataBytes: number;
  durationSeconds: number;
}

export interface AzurePronunciationAssessmentScores {
  AccuracyScore?: number;
  FluencyScore?: number;
  CompletenessScore?: number;
  PronScore?: number;
  ProsodyScore?: number;
  ErrorType?: string;
  NBestPhonemes?: Array<{ Phoneme?: string; Score?: number }>;
}

export class AzurePronunciationRestError extends Error {
  constructor(
    message: string,
    readonly status: number,
    readonly retryable: boolean,
  ) {
    super(message);
  }
}

export function buildPronunciationAssessmentHeader(referenceText: string): string {
  const config = {
    ReferenceText: referenceText,
    GradingSystem: "HundredMark",
    Granularity: "Phoneme",
    Dimension: "Comprehensive",
    EnableMiscue: true,
    PhonemeAlphabet: "IPA",
    NBestPhonemeCount: 5,
  };
  return Buffer.from(JSON.stringify(config), "utf8").toString("base64");
}

/**
 * The short-audio REST response exposes scores directly on NBest/word/phoneme
 * objects, while some SDK-era payloads nest them under PronunciationAssessment.
 * Normalize both shapes so a transport change cannot silently zero scores.
 */
export function normalizeAzurePronunciationAssessment(value: unknown): AzurePronunciationAssessmentScores {
  const direct = record(value);
  const nested = record(direct.PronunciationAssessment);
  const number = (name: string) => finiteOptionalNumber(nested[name]) ?? finiteOptionalNumber(direct[name]);
  const errorType = typeof nested.ErrorType === "string"
    ? nested.ErrorType
    : typeof direct.ErrorType === "string" ? direct.ErrorType : undefined;
  const rawNBest = Array.isArray(nested.NBestPhonemes)
    ? nested.NBestPhonemes
    : Array.isArray(direct.NBestPhonemes) ? direct.NBestPhonemes : undefined;
  const nBestPhonemes = rawNBest?.map(item => {
    const phoneme = record(item);
    return {
      Phoneme: typeof phoneme.Phoneme === "string" ? phoneme.Phoneme : undefined,
      Score: finiteOptionalNumber(phoneme.Score),
    };
  });
  return {
    AccuracyScore: number("AccuracyScore"),
    FluencyScore: number("FluencyScore"),
    CompletenessScore: number("CompletenessScore"),
    PronScore: number("PronScore"),
    ProsodyScore: number("ProsodyScore"),
    ErrorType: errorType,
    NBestPhonemes: nBestPhonemes,
  };
}

export function inspectPcmWav(audio: Uint8Array): PcmWavInfo {
  const bytes = Buffer.from(audio.buffer, audio.byteOffset, audio.byteLength);
  if (bytes.length < 44 || bytes.toString("ascii", 0, 4) !== "RIFF" || bytes.toString("ascii", 8, 12) !== "WAVE") {
    throw new Error("Pronunciation audio must be a PCM WAV file.");
  }

  let offset = 12;
  let audioFormat = 0;
  let channels = 0;
  let sampleRate = 0;
  let bitsPerSample = 0;
  let dataBytes = 0;
  while (offset + 8 <= bytes.length) {
    const id = bytes.toString("ascii", offset, offset + 4);
    const size = bytes.readUInt32LE(offset + 4);
    const payload = offset + 8;
    if (payload + size > bytes.length) throw new Error("Pronunciation WAV contains an invalid chunk length.");
    if (id === "fmt " && size >= 16) {
      audioFormat = bytes.readUInt16LE(payload);
      channels = bytes.readUInt16LE(payload + 2);
      sampleRate = bytes.readUInt32LE(payload + 4);
      bitsPerSample = bytes.readUInt16LE(payload + 14);
    } else if (id === "data") {
      dataBytes += size;
    }
    offset = payload + size + (size % 2);
  }

  if (audioFormat !== 1 || channels !== 1 || sampleRate !== 16000 || bitsPerSample !== 16 || dataBytes <= 0) {
    throw new Error("Azure pronunciation REST requires 16 kHz, mono, 16-bit PCM WAV audio.");
  }
  const durationSeconds = dataBytes / (sampleRate * channels * (bitsPerSample / 8));
  if (durationSeconds <= 0 || durationSeconds > 30) {
    throw new Error("Azure pronunciation REST audio must be between 0 and 30 seconds.");
  }
  return { sampleRate, channels, bitsPerSample, audioFormat, dataBytes, durationSeconds };
}

export async function assessPronunciationWithAzureRest(
  audio: Uint8Array,
  options: AzurePronunciationRestOptions,
): Promise<Record<string, unknown>> {
  inspectPcmWav(audio);
  const region = options.region.trim().toLowerCase();
  if (!/^[a-z0-9-]{2,40}$/.test(region)) throw new Error("AZURE_SPEECH_REGION is invalid.");
  const language = options.language.trim();
  if (!/^[a-z]{2,3}-[A-Z]{2}$/.test(language)) throw new Error("AZURE_SPEECH_LANGUAGE is invalid.");
  const fetchImpl = options.fetchImpl ?? fetch;
  const maximumAttempts = Math.max(1, Math.min(5, Math.round(options.maximumAttempts)));
  const random = options.random ?? Math.random;
  const endpoint = new URL(
    `https://${region}.stt.speech.microsoft.com/speech/recognition/conversation/cognitiveservices/v1`,
  );
  endpoint.searchParams.set("language", language);
  endpoint.searchParams.set("format", "detailed");
  endpoint.searchParams.set("profanity", "masked");

  let lastError: unknown;
  for (let attempt = 1; attempt <= maximumAttempts; attempt++) {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), Math.max(1000, options.timeoutMs));
    try {
      const response = await fetchImpl(endpoint, {
        method: "POST",
        headers: {
          "Ocp-Apim-Subscription-Key": options.subscriptionKey,
          "Pronunciation-Assessment": buildPronunciationAssessmentHeader(options.referenceText),
          "Content-Type": "audio/wav; codecs=audio/pcm; samplerate=16000",
          Accept: "application/json",
        },
        body: new Uint8Array(audio),
        signal: controller.signal,
      });
      if (response.ok) return await response.json() as Record<string, unknown>;

      const retryable = response.status === 429 || response.status >= 500;
      const error = new AzurePronunciationRestError(
        `Azure pronunciation REST returned HTTP ${response.status}.`,
        response.status,
        retryable,
      );
      if (!retryable || attempt === maximumAttempts) throw error;
      lastError = error;
      const retryAfterSeconds = Number(response.headers.get("retry-after"));
      const baseMs = Number.isFinite(retryAfterSeconds) && retryAfterSeconds > 0
        ? Math.min(5000, retryAfterSeconds * 1000)
        : Math.min(4000, 250 * 2 ** (attempt - 1));
      await delay(baseMs + Math.round(random() * 200));
    } catch (error) {
      const retryable = error instanceof AzurePronunciationRestError
        ? error.retryable
        : error instanceof Error && (error.name === "AbortError" || error instanceof TypeError);
      if (!retryable || attempt === maximumAttempts) throw error;
      lastError = error;
      await delay(Math.min(4000, 250 * 2 ** (attempt - 1)) + Math.round(random() * 200));
    } finally {
      clearTimeout(timeout);
    }
  }
  throw lastError instanceof Error ? lastError : new Error("Azure pronunciation REST failed.");
}

function delay(milliseconds: number): Promise<void> {
  return new Promise(resolve => setTimeout(resolve, milliseconds));
}

function record(value: unknown): Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value)
    ? value as Record<string, unknown>
    : {};
}

function finiteOptionalNumber(value: unknown): number | undefined {
  if (typeof value !== "number" && (typeof value !== "string" || !value.trim())) return undefined;
  const number = typeof value === "number" ? value : Number(value);
  return Number.isFinite(number) ? number : undefined;
}
