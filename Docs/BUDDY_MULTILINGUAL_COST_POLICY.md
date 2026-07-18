# Buddy multilingual and cost policy

## Runtime path

- Buddy speech input uses Sarvam Saaras v3 in `codemix` mode with per-turn language auto-detection.
- Buddy reasoning uses Sarvam 105B first and Gemini Flash as a quality/provider fallback.
- Buddy speech output is an ordered `speechSegments` array. Unity speaks each run locally with Windows System.Speech during development and Android `TextToSpeech` in production.
- The Buddy runtime never calls a Sarvam TTS endpoint. `speechText` remains only as a rollout fallback for older clients or provider responses.
- Phonics cues never send IPA to TTS. The model returns an allow-listed cue key, and Unity plays a recorded anchor word before the localized explanation.

The learner can select any of the 23 configured home languages in Settings. The response prompt uses that language's name and native-script requirement rather than assuming Hindi.

## Default daily caps

| Tier | LLM calls | LLM token budget | Sarvam STT audio |
| --- | ---: | ---: | ---: |
| Free | 8 | 6,000 | 90 seconds |
| Standard | 40 | 24,000 | 360 seconds |
| Premium | 120 | 80,000 | 900 seconds |

Every STT turn is capped at 20 seconds. STT and LLM reservations also obey the school-wide daily AI cost ceiling.

## Deployment configuration

The defaults can be changed without a client release:

- `BUDDY_STT_FREE_DAILY_SECONDS`
- `BUDDY_STT_STANDARD_DAILY_SECONDS`
- `BUDDY_STT_PREMIUM_DAILY_SECONDS`
- `BUDDY_STT_MAX_TURN_SECONDS`
- `SARVAM_STT_USD_PER_AUDIO_HOUR`
- Existing `BUDDY_*_DAILY_MODEL_CALLS` and `BUDDY_*_DAILY_TOKEN_LIMIT` variables

The operations dashboard reports daily Sarvam STT minutes and estimated spend. Provider prices remain estimates for guardrails, not billing truth.

## Phonics contract

Buddy responses include `responseLanguage`, `phonicsCueKey`, and `phonicsAnchorWord`. The client ignores arbitrary audio names and resolves the cue key through `BuddyPhonicsCueCatalog`. For example, `short_a` always plays the recorded `APPLE` pronunciation before speaking the explanation. If a cue asset is missing, the explanation still uses an anchor phrase such as "the short A sound, like apple" and never asks TTS to read `/ae/`.

## Device speech contract

`speechSegments` is authoritative and preserves every language switch in playback order. Each item contains one lowercase allow-listed base language code and one short plain-text run. The server and Unity both reject malformed, empty, overlong, unsupported, markup, SSML, and IPA-like slash-notation segments. Android changes locale only after the previous utterance has completed; Windows starts one hidden local speech worker per segment. Missing regional voices fall back to an installed English voice and emit a `[BuddyTTS]` diagnostic. Ending a Buddy call cancels the active worker/utterance and invalidates pending segments.
