import * as openaiPlugin from '@livekit/agents-plugin-openai';
import * as sarvam from '@livekit/agents-plugin-sarvam';
import type { BuddyVoiceConfig } from './config.js';

class SarvamStreamingLlm extends openaiPlugin.LLM {
  constructor(
    options: ConstructorParameters<typeof openaiPlugin.LLM>[0],
    private readonly maximumTokens: number,
  ) {
    super(options);
  }

  override chat(args: Parameters<openaiPlugin.LLM['chat']>[0]) {
    return super.chat({
      ...args,
      extraKwargs: {
        ...args.extraKwargs,
        // Voice replies are short and time-sensitive. Sarvam reasoning is on
        // by default and can consume the whole latency/token budget first.
        reasoning_effort: null,
        max_tokens: this.maximumTokens,
      },
    });
  }
}

export function createSarvamLlm(config: BuddyVoiceConfig): openaiPlugin.LLM {
  return new SarvamStreamingLlm(
    {
      apiKey: config.sarvamApiKey,
      baseURL: 'https://api.sarvam.ai/v1',
      model: config.llmModel,
      temperature: 0.2,
      parallelToolCalls: false,
    },
    180,
  );
}

export function createSarvamStt(config: BuddyVoiceConfig): sarvam.STT {
  return new sarvam.STT({
    apiKey: config.sarvamApiKey,
    model: config.sttModel,
    languageCode: config.sttLanguage,
    mode: config.sttMode,
    streaming: true,
    highVadSensitivity: true,
    flushSignal: true,
    preSpeechPadFrames: 4,
    interruptMinSpeechFrames: 3,
  });
}

export function createSarvamTts(
  config: BuddyVoiceConfig,
  targetLanguageCode = config.ttsLanguage,
  speaker = config.ttsSpeaker,
): sarvam.TTS {
  return new sarvam.TTS({
    apiKey: config.sarvamApiKey,
    model: config.ttsModel,
    targetLanguageCode,
    speaker,
    pace: 1.05,
    temperature: 0.55,
    sampleRate: 24000,
    outputAudioCodec: 'linear16',
    streaming: true,
  });
}
