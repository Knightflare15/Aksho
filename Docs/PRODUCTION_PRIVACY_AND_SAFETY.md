# Production privacy and child-safety controls

This document describes the implemented engineering controls. It is not a substitute for jurisdiction-specific legal advice.

## Consent matrix

New students start with all optional permissions disabled. Only a linked parent or school administrator may grant or withdraw consent. A policy-version mismatch fails closed until consent is renewed.

| Permission | When off | When on |
| --- | --- | --- |
| Learning progress | Only service-essential account/game state should be used | Adaptive learning records may be stored |
| AI Buddy | Deterministic game clues remain available | Bounded, redacted task context may be sent to the configured model |
| Cloud pronunciation | No cloud analysis; uploaded evidence is deleted | Short WAV is assessed and immediately deleted |
| Handwriting evidence | Raw points are stripped; derived scores remain | Bounded points are retained until the configured expiry |
| Crash diagnostics | No student-linked report is accepted | Redacted, rate-limited diagnostic envelopes are accepted |

## Retention and deletion

- Pronunciation audio: deleted immediately on success, failure, disablement, consent rejection, or quota rejection. A scheduled orphan sweep deletes uploads whose callable never arrived (default maximum age 24 hours).
- Buddy conversations: `BUDDY_CONVERSATION_RETENTION_DAYS` (default 30).
- Raw handwriting points: `HANDWRITING_RAW_STROKE_RETENTION_DAYS` (default 180), then scrubbed while derived metrics remain.
- Usage aggregates: `BUDDY_USAGE_RETENTION_DAYS` (default 400); these contain no conversation text.
- Individual redacted client diagnostics: `CLIENT_DIAGNOSTIC_RETENTION_DAYS` (default 30).
- Account deletion: queued with `STUDENT_DELETION_GRACE_DAYS` (default 7), then recursively deletes the student document tree, authentication identity, class membership, and storage prefix.

## Required human work before public beta

1. Have counsel adapt the privacy notice and parental-consent language for every launch jurisdiction.
2. Name the legal operator, privacy/safety contacts, subprocessors, processing regions, and request-response timeframes.
3. Create a staffed safeguarding escalation runbook and test it with school personnel.
4. Run a threat model, penetration test, Firebase rules test suite, backup/restore drill, and deletion audit in the production project.
5. Confirm provider contracts and child-data settings; do not rely on consumer-product defaults.
