# Hindi-English Buddy learning-data contract

This document is the hand-off between the Unity game and the separately deployed AI Buddy.
The game remains authoritative for questions, accepted answers, grammar validation, hints,
rewards, combat, and progression. The Buddy consumes the records described here and must not
change game state.

## Firebase collections

All paths are below `schools/{schoolId}/students/{studentId}`.

| Collection/document | Ownership | Purpose |
| --- | --- | --- |
| `buddyLearningAttempts/{eventId}` | Game, append-only | Canonical normalized event for every learning attempt or support action. |
| `buddyLearnerState/current` | Cloud Function only | Server-derived strengths, needs, recurring errors, per-modality performance, and recommended English ratio. |
| `buddyLearningSessions/{sessionId}` | Game checkpoint | Session counts and the language-support recommendation at the end/checkpoint of a play session. |
| `buddyLearnerProfiles/current` | Game/profile settings | Hindi-English preference, transliteration option, memory opt-in, and explanation style. |
| `spokenPhraseEvents`, `writtenPhraseEvents`, `wordCastEvents`, `grammarBattleEvents`, `letterAttempts` | Existing systems | Detailed source evidence. The normalized Buddy event points back using `sourceRecordType`/`sourceRecordId` when available. |

`aggregateBuddyLearningAttempt` is triggered whenever an append-only Buddy attempt is created.
It updates `buddyLearnerState/current` transactionally and idempotently at the event-document level.

## Attempt coverage

The Unity `CurriculumSessionManager` normalizes these interactions:

- Every accepted or rejected handwriting confirmation, including diagnostic tags.
- Every ordinary spoken word cast and its on-device pronunciation result.
- Delayed server pronunciation reviews as diagnostic updates, without double-counting mastery attempts.
- Every accepted or rejected NPC spoken response.
- Every accepted or rejected NPC written response.
- Fill-in-the-blank, unjumbling, transcript completion, and transcript correction as distinct activity types.
- Every grammar-combat command, including grammar pattern, curse, action verb/role, outcome, and pronunciation diagnostics.
- Counting and colour spoken-answer attempts.
- Gym completion summaries (recorded but excluded from mastery to avoid double-counting the underlying questions).
- Grimoire opens and future hint-button events (recorded but excluded from mastery).
- Future Buddy conversation turns that carry grammar diagnoses.

The normalized event deliberately does not duplicate raw audio or handwriting strokes. Those remain
in the existing evidence collections and follow their existing retention policies.

## Important attempt fields

Each `BuddyLearningAttemptRecord` includes:

- Identity: `studentId`, `schoolId`, `classId`, `sessionId`, `missionId`, `goalId`.
- World context: `areaId`, `zoneKind`, `activityType`, `modality`, `inputSource`.
- Content linkage: `contentId`, `dialogueTaskId`, `attemptGroupId`, `attemptNumber`.
- Diagnosis inputs: `questionPrompt`, `expectedResponse`, `submittedResponse`, `normalizedResponse`, `correctedResponse`.
- Curriculum linkage: `grammarPattern`, `conceptId`, `grimoireReference`, `masteryTags`, `vocabularyTokens`.
- Learning outcome: `correct`, `completedIndependently`, `hintCount`, `highestHintLevel`, `remediationStep`, `errorTags`.
- Modality quality: response time, confidence, recognition confidence, pronunciation score, handwriting diagnostics, and pronunciation diagnostics.
- Combat context when relevant: `activeCurse`, `actionVerb`, `actionRole`, and `outcome`.

`countsTowardMastery` distinguishes genuine attempts from reference/support/diagnostic events.
Never treat a Grimoire open or a delayed pronunciation review as a second answer attempt.

## Learner-state semantics

`buddyLearnerState/current` contains per-concept aggregates:

- Attempts and correct attempts.
- Independent and assisted correct attempts.
- First-attempt success.
- Hint dependency.
- Response time.
- Error counts.
- Separate speaking, writing, arrangement, combat, and other modality rates.
- A smoothed mastery estimate with an evidence-confidence ramp over the first eight attempts.
- A deterministic Hindi-English support band.

Language-support bands are:

| Band | Recommended English ratio |
| --- | ---: |
| Foundation | 0.30 |
| Guided | 0.45 |
| Growing | 0.65 |
| Independent | 0.85 |

The Buddy may temporarily use more Hindi after repeated failures, but it should use this ratio as
the stable baseline. It must not independently overwrite mastery or the language band.

## Retrieving context

The callable Cloud Function `getBuddyLearnerContext` accepts:

```json
{
  "schoolId": "school-id",
  "studentId": "student-id",
  "conceptId": "BasicVerbs",
  "recentAttemptLimit": 8
}
```

It returns the current profile, server learner state, and recent relevant attempts. A separately
hosted Buddy service using the Firebase Admin SDK can read the same documents directly after it
verifies the student's Firebase ID token.

Inside Unity, the equivalent local/offline bundle is available from:

```csharp
BuddyContextSnapshotRecord context =
    CurriculumSessionManager.EnsureExists().BuildBuddyContextSnapshot("BasicVerbs", 8);
```

Language preferences are configured with:

```csharp
CurriculumSessionManager.EnsureExists().ConfigureBuddyLearningPreferences(
    homeLanguage: "hi",
    targetLanguage: "en",
    allowTransliteration: false,
    learningMemoryEnabled: true);
```

## Local/offline persistence

Unity writes a per-student local mirror under:

```text
Application.persistentDataPath/BuddyLearningData/{studentId}/
  attempts.jsonl
  learner_state.json
  sessions/{sessionId}.json
```

The JSONL file is an audit/history mirror. `learner_state.json` is the compact prompt-time state.
Firebase remains the cross-device source once the student is authenticated.

## Buddy prompt boundary

For a Town explanation, the Buddy may receive the expected response. For Route hints, the Buddy
should preferably receive expected grammar features and deterministic error tags rather than the
full answer. For a Gym, do not call the Buddy and do not retrieve the active answer.

The useful prompt-time slice is:

1. Current question and deterministic diagnosis.
2. Relevant Grimoire card.
3. Current concept aggregate and modality weaknesses.
4. Up to eight relevant recent attempts.
5. Learner profile and safe relationship memories maintained by the future Buddy service.

Do not send the complete raw attempt history to the language model.

## Buddy response speech contract

Learner-facing display text and spoken text are separate. The callable Buddy response returns an
ordered device-speech payload:

```json
{
  "learnerText": "Short display-safe coaching text.",
  "speechText": "Legacy rollout fallback only.",
  "speechSegments": [
    { "language": "hi", "text": "बहुत अच्छा!" },
    { "language": "en", "text": "Now try the sentence again." }
  ],
  "responseLanguage": "hi"
}
```

`speechSegments` is authoritative. Every language switch is a new continuous text run, and Unity
plays the array in order with device-local TTS. Codes are lowercase allow-listed base languages;
text must be short and plain, without SSML, markup, IPA, or slash notation. The server and Unity
both validate the array. `speechText` is retained temporarily so an older response can still be
spoken as one segment. Buddy speech output does not call Sarvam TTS; Sarvam STT remains a separate
metered input path when voice input is enabled.

## Deployment requirement

The new cloud collections work locally immediately. Cross-device aggregation and the context
callable require deploying the updated Firebase Functions and Firestore rules from `TeacherPortal`.
