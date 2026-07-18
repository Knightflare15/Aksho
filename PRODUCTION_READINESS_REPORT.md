# The Script — game, learning, architecture, AI, and production outlook

## Implementation update — 13 July 2026

The following previously identified engineering gaps are now implemented:

- fail-closed, policy-versioned parent/admin consent for learning analytics, Buddy, cloud audio, handwriting evidence, and diagnostics;
- cancellable account deletion with a scheduled recursive Firestore/Storage/Auth/class-membership cleanup;
- immediate pronunciation-audio deletion plus a scheduled orphan-upload sweep;
- local Buddy input moderation, provider safety settings, output moderation, PII redaction, strict memory allow-lists, and aggregate-only safety counters;
- managed Unity exception and unclean-exit reporting with redaction, deduplication, consent checks, per-session/per-day limits, aggregate crash counts, and retention purge;
- tiered per-user AI quotas, a transactional school-wide daily AI cost ceiling, estimated micro-USD accounting, latency bands, operations summary, and scheduled health alerts;
- replayable first-run onboarding for summoning, camera-relative movement, attacks/adverbs, curses, Buddy boundaries, and hidden-hex combat;
- automated curriculum drift QA covering the canonical Grimoire, Unity pages/progression, portal area IDs, authored/generated dialogue tasks, backend RAG pages, lexicon references, and Town/Route/Gym assistance rules;
- backend area/concept seeding now derives from the canonical generated Grimoire and an 11-tier progression map, eliminating the legacy tier gaps and missing area chains.

Still required before a public beta: jurisdiction-specific legal review and final operator/vendor details; a staffed safeguarding escalation process; native crash/ANR symbolication; Rules-emulator and abuse tests; a signed-device build/test matrix; human educator QA and child usability sessions; and production alert recipients. See `Docs/PRODUCTION_PRIVACY_AND_SAFETY.md` and `Docs/PRODUCTION_OPERATIONS.md`.

Updated 13 July 2026. Cost figures are planning estimates in USD, before tax, support, refunds, abuse, and committed-use discounts. Replace them with measured p50/p95 telemetry after a real pilot.

## Executive judgment

The project now has a coherent game loop and a credible educational product shape, but it is not yet ready for an unrestricted commercial launch. It is suitable for a controlled alpha or school pilot after the remaining P0 release gates are closed.

Better UI, 3D models, animation, sound, and effects will improve first impressions and retention, but they are not sufficient on their own. A sellable learning game also needs:

- a fun first ten minutes and an early payoff;
- trustworthy assessment rather than completion masquerading as mastery;
- child-device speech and handwriting calibration;
- privacy, consent, deletion, tenant isolation, and abuse controls;
- crash-free performance on low/mid-range target phones;
- measured evidence that learners return and improve;
- dependable school onboarding, support, and teacher reporting.

The highest-value next investment is a polished 20–30 minute vertical slice tested with real learners, not eleven regions of final art produced before the loop is validated.

## How the game works now

There are eleven grammar regions, in this order: Greetings, Alphabet, Vowels and Consonants, Sentence Start and Full Stop, Nouns, Verbs, Articles, Pronouns, Plurals, Adjectives, and Basic Prepositions.

Each region follows the same scaffold:

1. **Town — learn:** four authored tasks, full Buddy support, local explanations, examples, speech and/or writing.
2. **Route — practise:** four tasks, partial Buddy support, correction activities, exploration, and a recognition or tactical encounter where the concept supports it.
3. **Gym — prove:** three checks with Buddy disabled, followed by the required encounter. Clearing unlocks the next region and its grammar/vocabulary rewards.
4. **Review — master:** a Gym clear is progression, not automatically mastery. The mastery record now requires at least eight concept attempts, four independent correct attempts, and a learner-model estimate of at least 0.75. A learner can revisit a cleared Gym to earn mastery.

The game remains authoritative for answers, progression, battles, rewards, and Gym checks. Gemini Buddy may coach, but cannot decide correctness or unlock anything. Azure pronunciation assessment is delayed enrichment for analytics and remediation; it does not block moment-to-moment play.

School mode overlays a teacher mission and weekly world goal on the same game. Evidence is submitted through validated callables, a matching server-recorded Gym attempt can claim a goal reward, and the shop now spends from the server-owned wallet. This raises the cheating cost but does not make a client-played game cryptographically authoritative; reports should remain formative unless signed/attested session evidence is added.

The final Basic Prepositions Gym now records campaign completion and shows a completion celebration instead of ending silently.

## Is the flow good for learners?

### What is strong

- The Town → Route → Gym reduction in help is a sound “model, scaffold, independent retrieval” structure.
- Speech, handwriting, typed sentence mechanics, battles, and mini-games provide useful modality variety.
- Correctness is deterministic and curriculum-owned; generative AI is not the assessor.
- Wrong answers can lead to a rule, a clue, a micro-lesson, or the relevant Grimoire anchor.
- Gym spoken answer choices are now disabled, so an assessment does not show the learner its answer.
- Sentence mechanics now preserve capitalization and terminal punctuation for typed writing; speech remains tolerant because STT capitalization/punctuation is unreliable.
- Generated Gym prompts are checked for exact-answer leakage, and the bank was regenerated with 3,305 validated task instances.

### What still needs learner testing

- Tactical combat does not begin until the Verbs region. A learner may complete roughly 55 responses across the first five regions before reaching the strongest game payoff. Add a five-minute onboarding set-piece or non-verbal/recognition encounter in the first session, then test it; do not guess from adult intuition.
- Practice is still mainly blocked by region. Add interleaved retrieval after the vertical slice: approximately 70% current concept, 20% current weak skill, and 10% an older concept, adjusted by evidence rather than fixed forever.
- Unlimited retries are appropriate for access, but reports must distinguish first-attempt, independent, assisted, and eventual success. The learner model already captures these dimensions; teacher UI should avoid one undifferentiated “accuracy” number.
- Pronunciation scores must be calibrated on the target population, accents, microphones, noise, and age group. Do not punish gameplay based on an uncalibrated cloud score.
- Every voice-dependent activity needs a quiet alternative and an accessible input path.
- The opening needs an explicit fantasy goal, a visible first reward, a short tutorial, and a reason to return tomorrow. Art amplifies these; it does not replace them.

## Architecture outlook

### Current production shape

```text
Unity client
  ├─ deterministic curriculum, answer checks, progression, battles, local learner state
  ├─ Android/iOS platform STT and device TTS
  ├─ Firebase Auth / token refresh
  ├─ Firestore reads for missions and goals
  ├─ callable Functions for validated writes, Buddy, wallet, codes, and rewards
  └─ Storage upload for sampled pronunciation WAV evidence

Firebase / Google Cloud
  ├─ Identity Platform / Firebase Auth
  ├─ Firestore and Storage
  ├─ 2nd-generation callable/event/scheduled functions
  └─ Gemini 3.1 Flash-Lite for bounded Buddy responses

Azure Central India
  └─ scripted pronunciation assessment for selected accepted utterances

Optional authenticated ML worker
  └─ evaluation/future self-hosted models; not authoritative gameplay
```

This separation is directionally correct: the client owns responsive deterministic play, the backend owns money/identity/tenant trust, and AI is advisory.

### Architectural debt to address after the pilot

- `CurriculumSessionManager`, `GrammarSceneController`, the Functions `index.ts`, and portal `App.tsx` are large coordination units. Split them by bounded responsibility only after behavior is protected by tests: identity/session, curriculum sync, evidence queue, pronunciation, Buddy, progression, wallet, and presentation.
- Replace hundreds of per-event callables with a validated `submitSessionBatch` endpoint (for example 20–50 events), idempotency keys, and server BulkWriter. Keep immediate calls only for money, access codes, Buddy, and goal claims.
- Add a local durable outbox with exponential backoff and a visible sync state. The current provider is now non-blocking, but evidence delivery should be observable and testable.
- Move Functions from `us-central1` to `asia-south1` in a staged dual-deploy, because Firestore is in Mumbai and Azure is in Central India. Do not switch the hard-coded client URL until both regions are live and tested.
- Add App Check/custom attestation for Unity, emulator-based Rules tests, callable authorization tests, CI, Crashlytics, performance traces, budgets, and alert delivery.
- Store refresh credentials in Android Keystore/iOS Keychain rather than release PlayerPrefs. Short-lived ID tokens should not be persisted.

## Complete API and cloud usage inventory

| Service | Caller and purpose | Cost/traffic behavior | Decision |
|---|---|---|---|
| Firebase Identity Toolkit | Unity sign-in with custom token and portal email/password | A few calls at login; Tier 1 auth is free through 50,000 MAU | Keep |
| Secure Token API | Unity refreshes the student ID token about every 50 minutes | Tiny; not a material cost | Keep; move refresh token to secure storage |
| Firestore REST/SDK | Unity missions/goals/status polling; portal live class and learner dashboards; Functions evidence and summaries | Reads/writes are cheap individually but can dominate through fan-out and polling | Keep; batch writes, paginate, aggregate, and measure index reads |
| Cloud Functions / Cloud Run functions | 30 callable/event/scheduled functions for tenant-safe creation, evidence, Buddy, wallet, goal claim, access codes, aggregation, and Azure dispatch | Invocation and CPU grow with per-event submission and long Buddy/Azure calls | Keep; batch ordinary evidence and colocate in India |
| Firebase Storage | Short sampled WAV upload, Azure dispatch download, then deletion | Storage is small; upload/download operations and abandoned objects matter | Keep only for selected reviews; enforce ownership/size/retention |
| Gemini Developer API | `requestBuddyHelp` uses stable `gemini-3.1-flash-lite` with JSON schema, no thinking budget, and a 512-token ceiling | Token-priced; modelled at 2,500 input + 150 output tokens/call | Keep for open-ended Buddy; use authored/local responses first |
| Azure Speech | Scripted phoneme-level pronunciation assessment | Audio-hour priced and the largest variable API at high review volume | Keep for Gym, weak/uncertain, and sampled calibration evidence; do not call on every success |
| Android/iOS STT | Immediate platform recognition/gating | No direct app API fee; behavior/device availability varies | Keep with non-voice fallback |
| Device TTS | Buddy and dialogue fallback | No app API fee; voice quality varies by installed engine | Keep; authored core voice first, downloadable polished packs later |
| Optional WavLM/phonetic worker | Future self-hosted pronunciation evaluation and experimental TTS | Compute cost only when deployed; now authenticated and bounded | Keep as evaluation path, not production authority yet |
| Remote translation/TTS endpoints | Fields exist but production endpoints are empty | No current live spend | Leave disabled; authored bilingual content is safer and cheaper |
| OpenAI API | Not used by the runtime | $0 | Nothing to remove |

The portal initializes reCAPTCHA App Check, but callable enforcement and Unity attestation are not complete. Treat this as a release gate, not as current protection.

## Cost model

### Official unit prices used

- Gemini 3.1 Flash-Lite: **$0.25 per million text input tokens and $1.50 per million output tokens**. See the [official Gemini 3 guide](https://ai.google.dev/gemini-api/docs/gemini-3) and [pricing page](https://ai.google.dev/gemini-api/docs/pricing).
- Azure pronunciation baseline is billed like Standard speech-to-text. The Central India Retail Prices API returned **$1.00 per audio hour** for S1 Speech to Text on 13 July 2026. See [Microsoft pronunciation pricing guidance](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/how-to-pronunciation-assessment) and [Azure Speech pricing](https://azure.microsoft.com/en-us/pricing/details/speech/).
- Mumbai Firestore Standard beyond the free quota: **$0.03/100,000 reads and $0.09/100,000 writes**, with 50,000 reads/day and 20,000 writes/day free. See [Firestore pricing](https://cloud.google.com/firestore/pricing) and [Firebase quotas](https://firebase.google.com/docs/firestore/pricing).
- Identity Platform Tier 1 is free for the first **50,000 MAU**, then starts at $0.0055/MAU for the 50,000–100,000 tier. See [Identity Platform pricing](https://cloud.google.com/identity-platform/pricing).
- Functions use [Cloud Run pricing](https://cloud.google.com/run/pricing), including a monthly compute free tier; requests, Eventarc, logs, build artifacts, secrets, storage, and egress can add small charges.

### Usage assumptions per monthly active learner

- 20 sessions/month;
- 400 scored/evidence interactions;
- average selected WAV duration 2.5 seconds;
- controlled case: 40 Azure reviews and 20 Buddy calls/month;
- full-cloud comparison: 100 Azure reviews and 30 Buddy calls/month;
- Buddy call: 2,500 input and 150 output tokens;
- 20 full teacher/parent portal loads/month;
- ordinary authentication under 50,000 MAU, no SMS auth;
- Firestore/Functions estimates include the current evidence-heavy architecture and free quotas, but are not a substitute for billing export telemetry.

### Monthly estimate

| MAU | Controlled policy | Per learner | Review-everything comparison | Per learner |
|---:|---:|---:|---:|---:|
| 100 | **$4.53** | $0.045 | **$9.54** | $0.095 |
| 1,000 | **$55.08** | $0.055 | **$109.04** | $0.109 |
| 10,000 | **$605.78** | $0.061 | **$1,156.34** | $0.116 |

Use a **20–30% operating contingency** until production telemetry exists. A sensible initial budget is therefore about $6/month at 100 MAU, $70/month at 1,000 MAU, and $760/month at 10,000 MAU under controlled behavior. Taxes, support tooling, CDN voice packs, backups, abuse, and human support are outside this estimate.

The formulas for the dominant APIs are:

```text
Azure = learners × reviews × average_audio_seconds ÷ 3600 × $1.00

Gemini = learners × calls ×
         ((input_tokens × $0.25) + (output_tokens × $1.50)) ÷ 1,000,000
```

The controlled 1,000-MAU estimate is approximately Azure $27.78, Gemini $17.00, Functions $6.00, Firestore $4.00, and storage/egress $0.30. The real bill should be monitored by school, endpoint, model, status, latency, token count, and audio seconds.

### Cost controls implemented

- Buddy is automatic only after a second wrong answer and at most once per task; manual help remains available.
- Buddy has a 3.5-second cooldown, request lease, trusted server-owned Free/Standard/Premium limits (8/40/120 model calls and 6k/24k/80k reserved tokens per day), bounded output, retries, and deterministic fallback.
- Azure uses the same trusted tier source with 4/30/80 reviews and 20/120/360 audio seconds per day; rejected or disabled-provider audio is deleted.
- Gemini and Azure have environment kill switches that fail into deterministic/local learning paths without a client update.
- Unity hydrates the authoritative learner aggregate after login/token refresh and conservatively merges it with deduplicated offline evidence before refreshing the scheduler.
- The local evidence outbox now retries automatically with exponential backoff, deduplicates identical payloads, survives restarts, and has explicit safety caps.
- Buddy context queries are concept/session scoped and limited instead of scanning/filtering broad histories.
- Routine Azure reviews are sampled; Gym and locally weak pronunciation are prioritized, with a maximum of 12 selected reviews per run.
- The server independently caps a learner at 30 pronunciation jobs and 120 audio seconds/day, marks budget skips, and deletes refused uploads.
- Rejected speech never enters Azure; accepted evidence without usable WAV never creates a job.
- Poll loops are capped; there is no infinite WordCast poll.
- The non-existent handwriting server analyzer no longer creates permanent pending jobs.
- Handwriting now separates raw input from magnetically assisted display ink, calibrates P$ thresholds per letter, fuses unconstrained P$/expected P$/CNN evidence into Accept-Retry-Reject, and treats uncertainty as a neutral retry rather than a definite error.
- Release handwriting feedback hides technical scores/tags; first-attempt formation coaching allows valid alternative stroke orders.
- Accepted and rejected raw handwriting collection are independently consent-gated and disabled by default; evidence is point-capped, server-expiring, and scrubbed after retention while aggregate diagnostics remain.
- Per-letter calibration now considers both same-letter variation and other-letter impostors. Weakly separated authored templates can request a retry but cannot cause a score-only hard rejection.
- Diagnostic findings carry source, confidence, and actionable/advisory status; canonical stroke-order guesses are advisory rather than authoritative.
- Safe-zone support records affected-point fraction and displacement. Letter input is capped at 2,048 points/32 pen lifts, while retained evidence uses stroke-preserving sampling and normalized training coordinates.
- 3,377 generated dialogue WAVs use free device TTS fallback and are no longer installed in every build.
- Inactive 296 MB ZIPA and 67 MB Vosk model data no longer ships in mobile builds.

### Next cost reductions

1. Add event batching and server BulkWriter; this reduces calls, radio use, retries, and Functions CPU.
2. Precompute portal class summaries and paginate detail instead of attaching broad real-time listeners.
3. Record actual provider token/audio usage in billing dashboards and alert on cost per active learner.
4. Use Gemini only for open-ended/repeated confusion. A local intent/error router plus authored templates should answer common cases.
5. Dual-deploy Functions in Mumbai, then remove cross-region traffic after clients migrate.

## Models worth building yourself

Do not train foundation models from scratch. Build narrow models around the product's proprietary learning data, evaluate them against a managed baseline, and distill/quantize only after they win.

### 1. Handwriting quality model — build first

Use the existing time-ordered stroke points, pen direction, start point, speed, spacing, retries, and template scores. Train a small temporal CNN/Transformer or stroke-graph model to predict letter, formation error tags, and calibrated confidence. Export an int8 ONNX/TFLite model for on-device inference; keep the cloud for training and opt-in diagnostics.

Why first: the project already captures structured stroke data, inference is cheap, feedback can be immediate/offline, and there is no paid handwriting API to replace. Collect consented, de-identified samples and keep a child-independent holdout by learner/device.

### 2. Indian-child pronunciation error detector — build after data collection

Fine-tune/distill a commercially usable speech encoder into a small phoneme-CTC model plus substitution/omission/error and confidence heads. Train for target words/sentences, Indian English accents, child voices, room noise, and phone microphones. Use an on-device model to gate obvious cases and a small server model for calibration/teacher evidence.

Do not use the current ZIPA archive in a paid build until its model license is explicit. WavLM and every pretraining/fine-tuning dataset also need a written license/data review. The objective is not “native accent”; it is intelligibility and useful, non-punitive feedback.

Economically, a continuously warm ~$68/month CPU service breaks even with Azure at roughly 980 learners if every learner sends 250 audio seconds/month, or about 2,450 learners under the new 100-second controlled policy. A scale-to-zero service changes the exact point. Accuracy and privacy, not just compute price, should decide the migration.

### 3. Learner knowledge/scheduling model — build now, no GPU required

Start with Bayesian Knowledge Tracing or IRT, then compare a small gradient-boosted/sequence model. Inputs: concept, modality, first-attempt correctness, hint depth, independence, response time band, recency, and error tags. Output: next-review probability and support band. This is cheaper, more interpretable, and more educationally valuable than a custom LLM.

### 4. Buddy router — build, but not a language model

Train a small classifier for intent, error category, hint level, Hindi/English ratio, safety escalation, and “authored template vs Gemini.” Run it on-device or on CPU Cloud Run. It should route the majority of common errors to reviewed templates and reserve Gemini for genuinely open-ended help.

### Do not self-host yet

- **General Buddy LLM:** Gemini at 10,000 MAU is modelled at about $170–255/month. A reliable always-on GPU, scaling, moderation, monitoring, and upgrades usually cost more; reconsider only when measured API spend/latency/privacy justifies it.
- **TTS:** keep authored core lines plus device TTS, then add downloadable studio voice packs. Most Silero TTS models are CC-NC-BY and `facebook/mms-tts-hin` is CC-BY-NC-4.0, so neither is a safe default for a paid product without separate permission. Silero VAD is separately MIT-licensed, but that does not make Silero TTS commercial.
- **Translation:** curriculum text should stay authored and reviewed. Translation APIs/models add inconsistency where exact pedagogy matters.

## Production fixes completed in this pass

- Route/Gym encounters self-activate, fixing the first tactical battle hard-lock.
- Delayed arena setup can no longer overwrite an already-started combat state.
- Save migration preserves Town, Route, and Gym progress.
- Gym answer choices and exact-answer fallback were removed.
- Sentence mechanics now use typed assessment that preserves case and punctuation.
- Progression and evidence-based mastery are separated; final campaign completion is persisted and celebrated.
- Wrong answers no longer trigger immediate paid AI; status/success feedback and haptics were polished.
- Generated exercise prompts, word order, preposition contexts, verb agreement, and answer-leak guards were rebuilt; 3,305 tasks pass generation.
- NPC choice building avoids repeated full-bank scans.
- Mission/world-goal loading is asynchronous; the main thread no longer busy-waits on REST calls.
- Goal document ID mismatch is backward-compatible through dual write/read.
- Goal reward claims require timely, matching Gym evidence recorded through the server callable.
- Shop prices, ownership, and wallet spending are server transactional.
- Client Firestore writes to analytics, summaries, analysis jobs, and audit logs are denied; release telemetry must pass through Functions, and immutable evidence IDs cannot be rewritten on retry.
- Buddy aggregation uses idempotency receipts, and parent weekly summaries transactionally aggregate unique sessions instead of replacing the week with the latest run.
- School/role/class tenant checks, immutable class bindings, access-code entropy/rate limits/transactions, payload/path limits, and pronunciation Storage ownership were tightened.
- Gemini keys are sent in headers, Buddy budgets are enforced, and context reads/output are bounded.
- Optional ML worker endpoints require Firebase bearer auth, student/path ownership, bounded payloads, non-root execution, local-only model loading, and disabled arbitrary path/model access.
- Generated voices and inactive models were moved outside packaged Unity assets: always-packaged raw audio/model content fell from about 1.03 GB to 26 MB.
- The portal production bundle was split from one 741 KB chunk into bounded app/React/Firebase chunks.
- Hosting now sends CSP, clickjacking, MIME-sniffing, referrer, permissions, and immutable-asset cache headers.
- Signed AAB creation now requires strict zero-warning content validation and all six intended build scenes.

## Remaining release gates

### P0 — before taking payments or broadly distributing

- Close the currently open Unity editor, run the complete EditMode/PlayMode suite and strict production validator, then create and smoke-test a fresh signed AAB. C# runtime/editor, Functions, portal, and Python worker compile successfully with zero warnings, but the locked editor prevented a fresh Unity Test Runner/AAB run in this pass.
- Test the first-login → first reward → first encounter → Gym → shop → relaunch journey on low/mid/high Android devices, offline/poor network, denied microphone, no TTS voice, and expired token.
- Move credentials to Android Keystore/iOS Keychain and enforce App Check/custom attestation on Unity callables and Storage.
- Add Firestore/Storage Rules emulator tests, callable authorization/abuse tests, CI dependency/license scanning, native crash/ANR symbolication, device performance traces, and configured alert recipients. Managed Unity exception reporting and aggregate operational alerts now exist, but they do not replace platform crash tooling or an on-call route.
- Treat teacher evidence as client-attested until App Check and signed session manifests/anomaly checks exist; do not market it as exam-grade proctoring.
- Perform a formal privacy/consent/data-retention/deletion review for child voice, stroke, teacher, and parent data. Parent consent/withdrawal and cancellable learner deletion now exist; add verified data export and whole-school offboarding after counsel defines the required identity checks and retention exceptions.
- Run target-learner usability and learning pilots. Calibrate pronunciation/handwriting by age, accent, noise, and device before using scores in consequential reports.
- Audit every third-party 3D/audio/code/model license. Do not ship ZIPA, Silero TTS, MMS Hindi, or an unreviewed WavLM derivative merely because the files run.

### P1 — before calling it a polished commercial product

- Put a game payoff in the first session and measure time-to-first-fun, tutorial completion, D1/D7 retention, first-attempt learning gains, and rage quits.
- Build downloadable versioned voice/region packs if studio-quality generated dialogue is required; keep the base install small.
- Add interleaved review and a learner-visible “cleared vs mastered” map treatment.
- Break the large coordinator files into tested domain modules and introduce session batching.
- Add a richer epilogue/replay loop, achievements tied to independent learning, and teacher-configurable accessibility—not grind-based rewards.

## Bottom line

The game is no longer just a content/graphics shell: the critical loop, assessment integrity, cost controls, tenant/wallet trust boundaries, build size, and release gate are materially stronger. It can become sellable, but final art alone would still produce an attractive risky alpha. Validate one delightful, measurable learning slice with real children and teachers, close the P0 operational/security gates, and then scale the art pipeline with confidence.
