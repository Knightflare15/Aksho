# Learner scheduler

The learner scheduler is a deterministic curriculum layer. It decides **what the learner should practise next and when**. Existing combat and activity systems still decide the moment-to-moment difficulty of their own content.

## Inputs

- canonical ordered regions from `NaturalGrammarProgression`
- unlocked/current world progress
- concept mastery, independent success, hint dependence and modality history
- recent error tags and attempts
- spaced-review stage, lapse count and next-review timestamp

## Output contract

`LearnerScheduleRecommendation` returns:

- target concept and region
- activity: introduction, guided practice, micro-lesson, retrieval review or challenge
- weakest/most useful modality
- difficulty from 1 to 5
- Buddy support level and recommended English ratio
- up to two secondary review concepts
- repeated error tags, reason code and learner-readable reason
- review-due and mastery-unlock flags

## Scheduling rules

- A new learner receives the earliest unlocked prerequisite.
- Three recent failures on one concept trigger a short micro-lesson and retry.
- Due spaced reviews receive a strong priority bonus.
- Weak mastery, lapses and hint dependence increase practice priority.
- Independent success advances review intervals through 1, 3, 7, 14 and 30 days.
- Failure reduces the review stage and schedules a short retry after 10 minutes.
- Assisted success does not advance independent retrieval spacing.
- A concept is unlock-ready only after at least eight attempts, four independent correct attempts and 0.75 mastery.

The scheduler runs locally after every captured learning attempt and is included in Buddy context snapshots. Review state is also computed in the Firebase aggregate so the server has a cross-device source of truth. No model API call is required.
