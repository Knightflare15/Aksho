# Buddy realtime voice worker

Buddy conversations use this LiveKit worker as their single production path. Unity and the browser harness obtain an authenticated room token while the call UI rings, then keep microphone audio, model turns, and Buddy speech inside that room until hang-up.

## Realtime path

```text
LiveKit room audio
  -> local Silero VAD
  -> bounded 240 ms pre-roll + extended-silence removal
  -> Sarvam Saaras v3 streaming STT (WebSocket)
  -> Sarvam 105B streaming chat completion (SSE)
  -> sentence/clause chunk buffer
  -> Sarvam Bulbul v3 streaming TTS (WebSocket)
  -> LiveKit room audio
```

The local VAD is used for both turn handling and the STT gate. On speech start, the gate forwards its bounded pre-roll so initial consonants are not clipped. On speech end, it immediately flushes Sarvam STT; subsequent silence is not uploaded. Agent replies use preemptive generation/TTS, allow interruption, and start feeding TTS at a sentence or clause boundary with a hard maximum buffer.

## Configure and run

Use Node.js 20 or newer.

```powershell
Copy-Item .env.example .env
npm install
npm run download-files
npm run dev
```

Set `LIVEKIT_URL`, `LIVEKIT_API_KEY`, `LIVEKIT_API_SECRET`, and `SARVAM_API_KEY` in `.env` or the deployment secret store. Do not place any of these secrets in Unity. `SARVAM_VOICE_MODEL` defaults to `sarvam-105b`, matching asynchronous Buddy; latency is tracked by the live conversation test.

## Verify

```powershell
npm test
npm run test:loop
```

`npm test` is offline and verifies the text chunk buffer plus the VAD gate's pre-roll, speech forwarding, provider flush, and silence dropping.

`npm run test:loop` requires `SARVAM_API_KEY`. It runs two isolated learner pipelines concurrently, one Hindi/Hinglish and one English/Hinglish. Each pipeline:

1. streams the text through Sarvam TTS;
2. adds leading and trailing silence and writes a WAV fixture under `test-output/`;
3. paces the fixture as microphone input through local Silero VAD and Sarvam streaming STT;
4. checks that a plausible transcript returned and prints first-audio, first-transcript, total-time, and gate statistics as JSON.

Each simulated learner owns separate VAD, STT, and TTS stream objects. The generated WAVs are deliberately ignored by Git and can be expanded into a larger regression corpus later.

## Production handoff

Firebase now exposes `createBuddyVoiceSession` and `closeBuddyVoiceSession`. Admission creates an opaque room per conversation, mints a short-lived JWT scoped to that exact room and microphone source, signs an explicit `buddy-voice` dispatch, and admits only trusted lesson metadata read by the server. App Check is enforced by default. A verified `receiveBuddyVoiceWebhook` endpoint releases abandoned capacity when LiveKit emits `room_finished`; configure that HTTPS URL in the LiveKit project's webhook settings.

Every accepted room runs in its own LiveKit job child process, including its own Sarvam STT/LLM/TTS clients. Invalid or expired dispatch metadata is rejected before a child process is assigned, and every child enforces the signed session deadline even if a learner token remains connected. The worker advertises `active jobs / BUDDY_MAX_ACTIVE_SESSIONS` as load. Run multiple replicas under the same `LIVEKIT_AGENT_NAME`; LiveKit assigns a room to exactly one available replica, so an HTTP sticky-session load balancer is neither needed nor desirable for media.

`deploy/kubernetes.yaml` is a production starting point with two replicas, health probes on port 8081, zero-unavailable rolling deploys, a 15-minute termination grace period, a disruption budget, and an HPA that scales early and scales down slowly. Replace the image and secret name, then load-test your actual provider quotas before increasing the default eight sessions per replica. CPU HPA is the portable baseline; an active-job custom metric is preferable once the cluster metrics adapter is available.

The Unity client exchanges its existing Firebase identity plus `dialogueTaskId` for the callable token, connects with the pinned LiveKit Unity SDK, publishes one microphone track, plays the agent audio track, and calls `closeBuddyVoiceSession` on hang-up or learner/session changes. Trusted wrong-answer and word-meaning events use the room data channel; they do not open a second LLM request. LiveKit secrets and Sarvam keys never belong in Unity.

The older `requestBuddyHelp` callable is no longer exported. Buddy LLM generation happens only inside the dispatched realtime room.

The most useful latency knobs are in `.env.example`. Tune them from measured loop and real-room metrics instead of removing the pre-roll or pushing silence to the provider.
