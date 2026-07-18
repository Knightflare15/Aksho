# Production operations guide

## What is measured

`getOperationsSummary` returns aggregate-only daily arrays for Buddy, pronunciation, managed client diagnostics, safety interceptions, and the shared AI cost budget. Costs are integer `micro_USD_estimate` values and must not be treated as invoices.

Buddy records request outcomes, input/output/total tokens, four latency bands, total latency, and estimated cost. Pronunciation records completed/failed/disabled counts, assessed audio seconds, three latency bands, total latency, and estimated cost. Diagnostics record only counts by severity in the school aggregate; individual reports are redacted, consent-gated, deduplicated in Unity, and limited per learner/day.

`monitorOperationsHealth` runs every 30 minutes and emits a de-identified `[OpsAlert]` Cloud Logging entry when average latency is slow, daily crashes spike, or reserved AI spend crosses the warning percentage. Each school/category is cooled down for one hour. Configure Cloud Monitoring log-based alerts for `[OpsAlert]` before beta; code logging alone does not page anyone.

## Hard limits

- Buddy: per-learner daily model-call and token limits by Free/Standard/Premium tier, plus cooldown, in-flight lease, absolute daily cap, output-token cap, and model kill switch.
- Pronunciation: per-learner daily review and audio-second limits by tier, provider kill switch, 30-second recording cap, and immediate raw-audio deletion.
- All paid AI: a transactional school-wide daily reservation ceiling (`SCHOOL_DAILY_AI_COST_LIMIT_USD`). A request is rejected before the provider call when the ceiling would be crossed.
- Diagnostics: `CLIENT_DIAGNOSTIC_DAILY_LIMIT` per learner and eight deduplicated reports per Unity session.

## Realtime Buddy multi-user lane

`createBuddyVoiceSession` is the only admission path. It requires Firebase Auth, current Buddy/audio consent, an active learner account, and App Check by default. The backend reads the dialogue task itself, reserves one active session for that learner, applies tiered per-learner daily admission plus a per-school concurrent-session ceiling, and returns a short-lived LiveKit JWT that can join only one opaque room and publish only microphone audio. Idempotent retries with the same client request ID do not consume a second daily admission. The signed room configuration dispatches exactly one `buddy-voice` agent with server-authored lesson metadata; Unity cannot choose the answer policy or another learner's room.

All worker replicas register the same agent name. LiveKit performs job assignment across available replicas, while each room executes in its own child process with separate VAD and Sarvam STT/LLM/TTS stream objects. The worker stops advertising capacity at `BUDDY_MAX_ACTIVE_SESSIONS`, rejects malformed or expired jobs, and enforces the signed deadline inside the child process. No sticky HTTP load balancer belongs in the media path.

The custom active-job load function applies to self-hosted workers. LiveKit Cloud agent deployments manage worker load/scaling themselves and ignore custom load functions; use the platform controls there instead of the Kubernetes HPA below.

The learner should call `closeBuddyVoiceSession` whenever identity/session changes. The verified `receiveBuddyVoiceWebhook` endpoint is the disconnect safety net: configure its public HTTPS URL in the LiveKit project so `room_finished` releases the Firestore lease. Consent withdrawal and deletion requests also delete the active room immediately. Expired leases remain a final crash/retry safety net.

Deploy `Server/BuddyVoiceAgent/deploy/kubernetes.yaml` as a starting shape: at least two replicas, four CPU/eight GiB limits, health checks on `GET /:8081`, zero-unavailable rolling updates, 900-second pod termination grace, and slow HPA scale-down. The included eight jobs per replica is intentionally conservative. Increase it only after a staged 1/2/10/25/50/100-room test shows acceptable CPU, memory, provider throttling, and latency.

Latency experience targets for the production-like staging test are p95 room join below 750 ms, p95 speech-end to committed transcript below 700 ms, p95 speech-end to first reply audio below 1.2 seconds, and barge-in audio cutoff below 300 ms. Record the actual device/network percentiles; these are launch gates, not guarantees from configuration alone.

The offline suite asserts signed room isolation and 64 simultaneous metadata contexts. `npm run test:loop` in `Server/BuddyVoiceAgent` adds a live two-user Hindi/Hinglish plus English/Hinglish Sarvam TTS -> silence padding -> local VAD -> streaming STT loop. Keep its generated WAVs as the seed for a larger, accent/noise/device regression corpus.

## Delayed Azure pronunciation lane

Pronunciation remains asynchronous and does not delay gameplay. `dispatchAnalysisJob` transactionally claims each Firestore job with an expiring owner lease, re-checks audio consent and deletion state, downloads the owned WAV into memory, and calls Azure's short-audio REST endpoint. The lane accepts only 16-kHz mono 16-bit PCM WAV clips up to 30 seconds, omits the paid prosody add-on, retries 429/5xx responses with jitter, and has explicit Functions concurrency/max-instance ceilings. Only the derived learner insight is retained; raw provider JSON and uploaded audio are deleted.

REST is operationally lighter than loading the Azure SDK in every cold instance, but Microsoft does not price baseline pronunciation assessment more cheaply merely because REST is used. Savings here come from lower runtime/cold-start overhead and leaving prosody disabled. Monitor 429s and adjust `PRONUNCIATION_FUNCTION_CONCURRENCY` before raising `PRONUNCIATION_FUNCTION_MAX_INSTANCES`.

## Deployment checklist

1. Copy `TeacherPortal/functions/.env.example` values into the managed Functions environment. Set `AZURE_SPEECH_KEY`, `SARVAM_API_KEY`, `GEMINI_API_KEY`, `ACCESS_CODE_PEPPER`, `LIVEKIT_API_KEY`, and `LIVEKIT_API_SECRET` in Firebase Secret Manager; never place them in Unity or Vite variables.
2. Replace the cost estimates with the contracted provider prices. Keep the shared daily ceiling intentionally conservative for the first beta.
3. Run `npm run content:validate` and both frontend/functions builds in CI.
4. Deploy Firestore indexes/rules, Storage rules, Functions, and Hosting; do not deploy Functions alone on a fresh project.
5. Create Cloud Monitoring alerts for Function errors, `[OpsAlert]`, p95 callable latency, scheduled-job failure, Firestore denial spikes, and budget notifications from the cloud billing account.
6. Test consent grant/withdrawal, every tier limit, safety interception, audio cleanup, deletion/cancellation, and a deliberately thrown Unity exception in the production-like staging project.
7. Run an account-deletion audit: verify Firestore descendants, Storage prefix, class membership, `users/{uid}`, and Firebase Auth identity are gone after the grace period.
8. Configure the `receiveBuddyVoiceWebhook` URL and verify a signed `room_finished` event removes `buddyVoiceRoomBindings/{room}` plus the learner and school leases.
9. Validate App Check on production Android/iOS builds. `BUDDY_VOICE_ENFORCE_APP_CHECK=false` is emulator-only and must not ship.
10. Run the concurrent generated-audio loop, then the staged multi-room ramp. Block rollout on cross-room participants/audio, token replay into another room, unbounded provider 429s, or a missed consent-revocation disconnect.

## Known boundary

`ProductionDiagnostics` captures managed Unity errors/exceptions and detects an unclean prior exit. Native crashes, ANRs, GPU-driver failures, symbolication, and device-level crash-free-user metrics still require a platform crash SDK such as the chosen production crash provider. Add that provider only after its child-data/privacy terms are approved, and keep the in-house redacted report as a fallback.
