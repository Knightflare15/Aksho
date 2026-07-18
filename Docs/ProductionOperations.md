# Production operations

This document describes the live service controls around the deterministic game. Sarvam is the default managed provider for Buddy LLM calls and short Buddy STT turns; TTS and the future handwriting model remain replaceable. Azure Pronunciation Assessment remains the managed pronunciation provider.

## Authoritative service tiers

`schools/{schoolId}/students/{studentId}.subscriptionTier` is the only tier source used for paid services. Learner-submitted Buddy profile data is sanitized and cannot grant a tier. New students start on `free`; an administrator changes the tier through the Teacher Portal, which calls the audited `setStudentServiceTier` function.

| Tier | Buddy model calls/day | Reserved Buddy tokens/day | Azure reviews/day | Azure audio/day |
|---|---:|---:|---:|---:|
| Free | 8 | 6,000 | 4 | 20 seconds |
| Standard | 40 | 24,000 | 30 | 120 seconds |
| Premium | 120 | 80,000 | 80 | 360 seconds |

All limits are transactional per learner and UTC day. Local/authored Buddy routes do not consume a model call. Azure audio rejected by the budget is deleted. Environment variables may override every value; see `TeacherPortal/functions/.env.production.example`.

## Emergency controls

- `BUDDY_MODEL_ENABLED=false` routes all Buddy requests to reviewed deterministic help without reserving provider budget.
- `AZURE_PRONUNCIATION_ENABLED=false` prevents new jobs, marks any newly observed pending job disabled, and deletes its uploaded WAV.
- Removing a provider key also causes a learner-safe fallback, but the explicit switches are preferred because the operational reason is recorded.

Deploy a switch change through the normal Functions configuration pipeline. Do not edit learner documents or ship a client update during an incident.

## Learner-state synchronization

Unity calls `getBuddyLearnerContext` after login, restored login, and token refresh. The backend returns both the canonical concept map and an additive array-shaped client view. Unity merges per concept:

1. More accumulated attempts win.
2. If attempt counts tie, the newer practiced timestamp wins.
3. Recent attempts are deduplicated by immutable event ID.
4. A response for another learner is rejected.

This preserves newer offline evidence without summing aggregates that may contain the same uploaded events twice. The scheduler refreshes immediately after hydration.

## Offline evidence delivery

Callable writes retry three times immediately. Failed payloads enter a durable local outbox, automatically retry with exponential backoff from 15 seconds to 5 minutes, deduplicate identical entries, and survive restarts. The outbox is capped at 500 submissions or 2 MiB; reaching the cap logs an error and drops the oldest evidence. Treat that log as a connectivity/operations incident.

## Minimum alerts

Create alerts for:

- Buddy fallback rate above 10% for 15 minutes;
- Buddy p95 latency above 4 seconds;
- Azure failed/disabled jobs above 5% or unexpected audio-second growth;
- budget-rejection rate by tier;
- Functions 5xx rate and Firestore permission failures;
- evidence-outbox-cap errors from clients;
- handwriting retry/reject rates, material-assist rate, low-separation calibration letters, per-letter confusion, and diagnostic-tag drift by released model/version;
- daily spend and projected monthly spend at 50%, 80%, and 100% of budget.

## Dependency policy

Production `npm audit --omit=dev` currently reports zero vulnerabilities for both the portal and Functions. Functions overrides transitive `uuid` to `^11.1.1` because current Firebase Admin/Google Cloud/Azure Speech packages still request v9 while the published buffer-bound advisory is fixed in 11.1.1. Runtime imports and TypeScript builds pass. Keep this override in CI, smoke-test it after every provider upgrade, and remove it once all upstream packages declare a patched compatible range.

## Release gates requiring external setup

- Install and configure Firebase App Check/custom attestation for Unity, then enable callable and Storage enforcement. The project currently uses REST without the Firebase Unity SDK, so enforcement cannot be safely switched on until the client can obtain and refresh App Check tokens.
- Store refresh credentials in Android Keystore and iOS Keychain. PlayerPrefs is still used and is not acceptable for a paid child-facing release.
- Run Firestore/Storage emulator rules tests and callable abuse tests in CI against a dedicated Firebase test project.
- Close the open Unity editor, run the complete EditMode/PlayMode suite and strict production validator, then build and device-smoke-test a signed AAB.
- Configure Sarvam/Azure secrets, billing exports, budgets, alert recipients, retention/deletion workflows, and child-data consent text.
- Run learner/teacher pilots and calibrate Azure pronunciation thresholds by age, Indian accent, device, and room noise. Cloud scores remain advisory until that calibration is complete.

## Deferred model interfaces

Hinglish TTS and learned handwriting assessment remain replaceable providers. Sarvam short-turn STT is available for Buddy conversation capture, but speech recognition must not become authoritative for progression until datasets, commercial licenses, child privacy basis, held-out evaluation, confidence calibration, latency, offline fallback, and rollback switch are documented.

The current handwriting recognizer is a production fallback/gameplay system, not a validated child-assessment model. Configure `HANDWRITING_RAW_STROKE_RETENTION_DAYS`, leave rejected-sample collection disabled until consent exists, and use `Docs/HandwritingSystem.md` as the operational contract.
