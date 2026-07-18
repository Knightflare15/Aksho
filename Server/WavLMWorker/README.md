# WavLM Cloud Worker

FastAPI service intended for Cloud Run. Firebase remains the main backend; this worker processes pronunciation `analysisJobs` and exposes lightweight translator-buddy endpoints for Unity.

Runtime responsibilities are intentionally split:

- `app/main.py` composes HTTP routes and analysis behavior.
- `app/auth.py` owns Firebase verification, tenant scope, and the loopback-only local bypass.
- `app/config.py` owns environment-backed model/provider selection and allow-lists.
- `app/direct_endpoint_policy.py` owns opt-in access and per-process rate limits for direct media routes.
- `app/model_loading.py` serializes heavyweight model initialization within each worker process.
- `app/schemas.py` owns bounded public request contracts.
- `app/storage_policy.py` pins pronunciation jobs to the configured Firebase Storage bucket and student path.

This keeps model and service changes out of the route module. Provider/model IDs remain deployment-owned and callers cannot choose an arbitrary checkpoint.

## Local smoke test

```powershell
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
$env:ALLOW_UNAUTHENTICATED_LOCAL_DEV="1"
$env:ENABLE_LOCAL_PATH_ANALYSIS="1"
$env:LOCAL_AUDIO_ROOT="C:\path\to\reviewed-wavs"
.\.venv\Scripts\python.exe -m uvicorn app.main:app --reload --port 8080
```

Then test a local WAV:

```powershell
Invoke-RestMethod -Method Post `
  -Uri "http://127.0.0.1:8080/analyze/path" `
  -ContentType "application/json" `
  -Body '{"wavPath":"C:\\path\\to\\clip.wav","targetText":"CAT"}'
```

The v1 worker expects 16 kHz WAV audio. Unity/Firebase jobs must attach `audioStoragePath` before Firestore jobs can complete.

All endpoints except `/health` require a valid Firebase ID token by default. The two local-development flags above must never be enabled on a public deployment. `/analyze/path` is disabled unless explicitly enabled and can read only `.wav` files whose resolved path stays under `LOCAL_AUDIO_ROOT`.

The direct media routes `POST /tts` and `POST /analyze/upload` are also disabled by default, including on authenticated deployments. Enable only the route you intend to expose with `ENABLE_DIRECT_TTS_ENDPOINT=1` or `ENABLE_DIRECT_AUDIO_UPLOAD_ENDPOINT=1`. Enabled routes still enforce authorization plus a per-process request limit. Use an API gateway or Cloud Armor for a shared, deployment-wide quota when the service can scale beyond one worker process.

The response `score` is a target-vs-heard phoneme alignment score. Raw model certainty is returned separately as `modelConfidence`.

Run the fast request-contract suite without loading a speech model:

```powershell
.\.venv\Scripts\python.exe -m unittest discover -s tests -v
```

## Translator buddy endpoints

Unity can point `TranslatorBuddyService.translationEndpointUrl` and `ttsEndpointUrl` at this worker:

- `POST /translate`
- `POST /tts`

`POST /tts` returns `404` until the operator explicitly sets `ENABLE_DIRECT_TTS_ENDPOINT=1`. Its default per-process limit is controlled by `DIRECT_TTS_REQUESTS_PER_MINUTE`.

`/translate` accepts:

```json
{
  "text": "Where is the market?",
  "sourceLanguage": "en",
  "targetLanguage": "hi",
  "ttsBackend": "local",
  "voice": ""
}
```

It returns:

```json
{
  "translation": "Where is the market?",
  "providerName": "Text fallback",
  "fallback": true,
  "translationConfigured": false
}
```

By default translation echoes the source text for local development so gameplay never blocks. Set
`TRANSLATION_EXTERNAL_URL` to proxy a real translation service. For production checks, set
`TRANSLATION_FALLBACK_ENABLED=0` so `/translate` returns `503` instead of silently echoing source text
when no translation provider is configured.

`/tts` accepts:

```json
{
  "text": "Namaste",
  "language": "hi",
  "ttsBackend": "local",
  "voice": ""
}
```

Local TTS is disabled by default. This is deliberate: the previously referenced Silero and MMS checkpoints are non-commercially licensed and cannot be shipped in a paid product without separate rights. Prefer device TTS or fixed, pre-generated audio until you have an approved model and voice-data license.

For a loopback-only local smoke test, `run-local-tts-worker.ps1` opts into both the direct route and unauthenticated local-development identity. The script binds to `127.0.0.1`; do not copy those opt-ins into a public deployment.

If you explicitly enable reviewed Silero weights, bake the reviewed repository into the image and set `SILERO_REPO_DIR`; production never downloads or executes a Torch Hub repository at request time. You can request a configured speaker with `ttsBackend`, for example `silero:telugu_female`.

Hugging Face model overrides must be listed in `LOCAL_TTS_ALLOWED_MODEL_IDS`; arbitrary caller-selected model downloads are rejected.

The built-in local Silero speaker map currently includes:

- `hi`: `hindi_female`
- `bn`: `bengali_female`
- `gu`: `gujarati_female`
- `kn`: `kannada_female`
- `ml`: `malayalam_female`
- `ta`: `tamil_female`
- `te`: `telugu_female`

Set `TTS_EXTERNAL_URL` to an Indic-Mio, svara-TTS, or compatible wrapper when you want to use a separate service instead. The external service may return either raw `audio/wav` or JSON:

```json
{
  "audioBase64": "<pcm16 wav bytes>",
  "audioContentType": "audio/wav",
  "providerName": "SPRINGLab/Indic-Mio"
}
```

Unity decodes PCM 16-bit WAV responses and falls back to text/system speech if audio is unavailable.

## Cloud Run shape

Recommended v1 region: `asia-south1` or `asia-south2` for CPU.

GPU later: use a Cloud Run GPU region such as `asia-southeast1` if WavLM latency requires it.

Required runtime env vars:

- `GOOGLE_CLOUD_PROJECT`
- `FIREBASE_STORAGE_BUCKET`, required for pronunciation jobs; relative paths resolve inside this bucket and full `gs://...` paths must name this exact bucket
- `WAVLM_MODEL_ID`, defaults to `speech31/wavlm-large-english-phoneme`
- `WAVLM_DEVICE`, defaults to `cuda` when available, otherwise `cpu`
- `TRANSLATION_EXTERNAL_URL`, optional proxy target for translation
- `TTS_EXTERNAL_URL`, optional proxy target for localized TTS
- `TRANSLATION_PROVIDER_NAME`, defaults to `Text fallback`
- `TTS_PROVIDER_NAME`, defaults to `External TTS`
- `LOCAL_TTS_ENABLED`, defaults to `0`; enable only with commercially approved, image-baked weights
- `LOCAL_TTS_ENGINE`, defaults to `silero`; set `hf` to prefer Hugging Face VITS/MMS
- `LOCAL_TTS_MODEL_ID`, optional forced local Hugging Face VITS/MMS model id
- `LOCAL_TTS_ALLOWED_MODEL_IDS`, comma-separated operator allow-list for any `hf:` override
- `SILERO_REPO_DIR`, reviewed local repository path baked into the image; runtime Torch Hub downloads are disabled
- `LOCAL_TTS_DEFAULT_LANGUAGE`, defaults to `hi`
- `ENABLE_DIRECT_TTS_ENDPOINT`, defaults to `0`; explicitly enables `POST /tts`
- `DIRECT_TTS_REQUESTS_PER_MINUTE`, defaults to `12`; per-process limit for the enabled direct TTS route
- `ENABLE_DIRECT_AUDIO_UPLOAD_ENDPOINT`, defaults to `0`; explicitly enables `POST /analyze/upload`
- `DIRECT_AUDIO_UPLOADS_PER_MINUTE`, defaults to `6`; per-process limit for the enabled direct upload route
- `ALLOW_UNAUTHENTICATED_LOCAL_DEV`, keep `0` in every deployed environment
- `ENABLE_LOCAL_PATH_ANALYSIS`, keep `0` in every deployed environment
- `LOCAL_AUDIO_ROOT`, required when local path analysis is enabled; requests cannot escape this directory
- `MAX_AUDIO_UPLOAD_BYTES`, defaults to 5 MiB and is capped at 10 MiB

The Cloud Run service account needs Firestore read/write access and Storage object read/delete access for the configured `FIREBASE_STORAGE_BUCKET`. Jobs that reference another bucket are rejected rather than following caller-supplied storage locations. Add a short Storage lifecycle rule as a backstop for failed cleanup.
