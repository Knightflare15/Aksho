using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Deterministic curriculum scheduler. It chooses what to practise next while
/// existing encounter/combat systems remain responsible for moment-to-moment
/// adaptive difficulty. It is intentionally local, explainable and cheap.
/// </summary>
public static class LearnerScheduler
{
    const float MasteryThreshold = 0.75f;
    static readonly TimeSpan[] ReviewIntervals =
    {
        TimeSpan.FromMinutes(10),
        TimeSpan.FromDays(1),
        TimeSpan.FromDays(3),
        TimeSpan.FromDays(7),
        TimeSpan.FromDays(14),
        TimeSpan.FromDays(30),
    };

    public static void UpdateReviewState(BuddyConceptLearningStateRecord concept, BuddyLearningAttemptRecord attempt)
    {
        if (concept == null || attempt == null || !attempt.countsTowardMastery)
            return;

        DateTime occurredAt = ParseUtc(attempt.createdAtUtc, DateTime.UtcNow);
        if (attempt.completedIndependently)
        {
            concept.consecutiveIndependentCorrect++;
            concept.reviewStage = Mathf.Clamp(concept.reviewStage + 1, 1, ReviewIntervals.Length);
            concept.lastIndependentCorrectAtUtc = occurredAt.ToString("o");
        }
        else if (!attempt.correct)
        {
            concept.lapseCount++;
            concept.consecutiveIndependentCorrect = 0;
            concept.reviewStage = Mathf.Max(0, concept.reviewStage - 2);
        }
        else
        {
            // Assisted success is useful evidence, but it must not advance a
            // spaced-repetition stage as if retrieval was independent.
            concept.consecutiveIndependentCorrect = 0;
        }

        int intervalIndex = Mathf.Clamp(concept.reviewStage, 0, ReviewIntervals.Length - 1);
        TimeSpan interval = !attempt.correct ? ReviewIntervals[0] : ReviewIntervals[intervalIndex];
        concept.nextReviewAtUtc = occurredAt.Add(interval).ToString("o");
    }

    public static LearnerScheduleRecommendation Recommend(
        BuddyLearnerStateRecord learner,
        IReadOnlyList<NaturalGrammarRegion> regions,
        GrammarWorldProgressData progress,
        DateTime utcNow)
    {
        learner ??= new BuddyLearnerStateRecord();
        regions ??= Array.Empty<NaturalGrammarRegion>();
        utcNow = utcNow.Kind == DateTimeKind.Utc ? utcNow : utcNow.ToUniversalTime();

        int availableTier = ResolveAvailableTier(regions, progress);
        NaturalGrammarRegion selectedRegion = null;
        BuddyConceptLearningStateRecord selectedState = null;
        float selectedScore = float.MinValue;

        for (int i = 0; i < regions.Count; i++)
        {
            NaturalGrammarRegion region = regions[i];
            if (region == null || region.conceptId == GrammarConceptId.None || region.tier > availableTier)
                continue;

            BuddyConceptLearningStateRecord concept = FindConcept(learner, region.conceptId.ToString());
            float score = Score(region, concept, progress, utcNow);
            if (score > selectedScore)
            {
                selectedScore = score;
                selectedRegion = region;
                selectedState = concept;
            }
        }

        // A fresh profile always starts at the first curriculum region.
        if (selectedRegion == null && regions.Count > 0)
        {
            selectedRegion = regions[0];
            selectedState = FindConcept(learner, selectedRegion.conceptId.ToString());
        }

        return BuildRecommendation(learner, regions, selectedRegion, selectedState, utcNow);
    }

    static LearnerScheduleRecommendation BuildRecommendation(
        BuddyLearnerStateRecord learner,
        IReadOnlyList<NaturalGrammarRegion> regions,
        NaturalGrammarRegion region,
        BuddyConceptLearningStateRecord concept,
        DateTime utcNow)
    {
        string conceptId = region != null ? region.conceptId.ToString() : "Unscoped";
        int recentErrors = CountRecentErrors(learner, conceptId, out List<string> focusErrors);
        bool due = concept != null && concept.attempts > 0 && ParseUtc(concept.nextReviewAtUtc, DateTime.MaxValue) <= utcNow;
        LearnerScheduleActivity activity;
        string reasonCode;

        if (concept == null || concept.attempts == 0)
        {
            activity = LearnerScheduleActivity.Introduction;
            reasonCode = "next_unpractised_prerequisite";
        }
        else if (recentErrors >= 3)
        {
            activity = LearnerScheduleActivity.MicroLesson;
            reasonCode = "repeated_error_micro_lesson";
        }
        else if (due)
        {
            activity = LearnerScheduleActivity.RetrievalReview;
            reasonCode = "spaced_review_due";
        }
        else if (concept.masteryEstimate >= MasteryThreshold && concept.independentCorrectAttempts >= 4)
        {
            activity = LearnerScheduleActivity.Challenge;
            reasonCode = "mastery_challenge";
        }
        else
        {
            activity = LearnerScheduleActivity.GuidedPractice;
            reasonCode = "mastery_building";
        }

        float mastery = concept != null ? concept.masteryEstimate : 0.2f;
        float englishRatio = concept != null && concept.recommendedEnglishRatio > 0f
            ? concept.recommendedEnglishRatio
            : learner.recommendedEnglishRatio > 0f ? learner.recommendedEnglishRatio : 0.3f;
        int difficulty = DifficultyFor(activity, concept);
        string buddySupport = activity == LearnerScheduleActivity.MicroLesson || mastery < 0.45f
            ? "direct"
            : mastery < MasteryThreshold ? "guided" : "minimal";

        var result = new LearnerScheduleRecommendation
        {
            recommendationId = $"{conceptId}:{utcNow:yyyyMMddHHmm}",
            generatedAtUtc = utcNow.ToString("o"),
            conceptId = conceptId,
            regionId = region?.id ?? "",
            regionDisplayName = region?.displayName ?? "Current lesson",
            activityType = ToSnakeCase(activity.ToString()),
            modality = SelectModality(region, concept),
            difficulty = difficulty,
            buddySupport = buddySupport,
            recommendedEnglishRatio = Mathf.Clamp(englishRatio, 0.2f, 0.95f),
            focusErrorTags = focusErrors,
            reasonCode = reasonCode,
            reason = Explain(activity, region, focusErrors),
            isReviewDue = due,
            unlockReady = concept != null && concept.attempts >= 8 &&
                concept.independentCorrectAttempts >= 4 && concept.masteryEstimate >= MasteryThreshold,
            nextReviewAtUtc = concept?.nextReviewAtUtc ?? "",
        };
        result.reviewConceptIds = FindDueReviews(learner, regions, conceptId, utcNow, 2);
        return result;
    }

    static float Score(NaturalGrammarRegion region, BuddyConceptLearningStateRecord concept, GrammarWorldProgressData progress, DateTime now)
    {
        if (concept == null || concept.attempts == 0)
        {
            // Prefer the earliest available gap instead of skipping foundations.
            return 48f - region.tier * 0.25f;
        }

        float score = (1f - Mathf.Clamp01(concept.masteryEstimate)) * 55f;
        DateTime dueAt = ParseUtc(concept.nextReviewAtUtc, DateTime.MaxValue);
        if (dueAt <= now)
            score += 35f + Mathf.Min(20f, (float)(now - dueAt).TotalDays * 2f);
        score += Mathf.Min(15f, concept.lapseCount * 2.5f);
        score += Mathf.Clamp01(concept.hintDependency) * 10f;

        GrammarMapAreaState current = FindCurrentArea(progress);
        if (current != null && current.conceptId == region.conceptId)
            score += 5f;
        return score;
    }

    static int ResolveAvailableTier(IReadOnlyList<NaturalGrammarRegion> regions, GrammarWorldProgressData progress)
    {
        int tier = regions.Count > 0 ? Mathf.Max(1, regions[0].tier) : 1;
        if (progress == null)
            return tier;

        GrammarMapAreaState current = FindCurrentArea(progress);
        if (current != null)
            tier = Mathf.Max(tier, current.grammarTopicTier);
        foreach (NaturalGrammarRegion region in regions)
        {
            if (region != null && progress.unlockedConceptIds != null &&
                progress.unlockedConceptIds.Exists(id => string.Equals(id, region.conceptId.ToString(), StringComparison.OrdinalIgnoreCase)))
                tier = Mathf.Max(tier, region.tier);
        }
        return Mathf.Clamp(tier, 1, regions.Count > 0 ? regions[regions.Count - 1].tier : tier);
    }

    static int DifficultyFor(LearnerScheduleActivity activity, BuddyConceptLearningStateRecord concept)
    {
        if (activity == LearnerScheduleActivity.Introduction || activity == LearnerScheduleActivity.MicroLesson)
            return 1;
        float mastery = concept?.masteryEstimate ?? 0f;
        int difficulty = mastery < 0.45f ? 1 : mastery < 0.60f ? 2 : mastery < 0.75f ? 3 : mastery < 0.88f ? 4 : 5;
        if (concept != null && concept.hintDependency > 0.5f)
            difficulty--;
        return Mathf.Clamp(difficulty, 1, 5);
    }

    static string SelectModality(NaturalGrammarRegion region, BuddyConceptLearningStateRecord concept)
    {
        if (concept?.modalityStates != null && concept.modalityStates.Count > 0)
        {
            BuddyModalityLearningStateRecord weakest = null;
            float weakestScore = float.MaxValue;
            foreach (BuddyModalityLearningStateRecord modality in concept.modalityStates)
            {
                if (modality == null || modality.attempts == 0 ||
                    string.Equals(modality.modality, BuddyLearningModality.Reference.ToString(), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(modality.modality, BuddyLearningModality.Unknown.ToString(), StringComparison.OrdinalIgnoreCase))
                    continue;
                float confidence = modality.independentSuccessRate + Mathf.Min(0.2f, modality.attempts * 0.02f);
                if (confidence < weakestScore)
                {
                    weakest = modality;
                    weakestScore = confidence;
                }
            }
            if (weakest != null)
                return weakest.modality.ToLowerInvariant();
        }

        return region?.conceptId switch
        {
            GrammarConceptId.Greetings => "speaking",
            GrammarConceptId.Alphabet => "writing",
            GrammarConceptId.VowelsConsonants => "listening",
            GrammarConceptId.SentenceStartEnd => "writing",
            GrammarConceptId.BasicNouns => "reading",
            GrammarConceptId.BasicVerbs => "combat",
            _ => region != null && region.combatUnlocked ? "combat" : "arrangement",
        };
    }

    static int CountRecentErrors(BuddyLearnerStateRecord learner, string conceptId, out List<string> tags)
    {
        tags = new List<string>();
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int failures = 0;
        List<BuddyLearningAttemptRecord> attempts = learner?.recentAttempts;
        if (attempts == null)
            return 0;
        for (int i = attempts.Count - 1; i >= 0 && i >= attempts.Count - 5; i--)
        {
            BuddyLearningAttemptRecord attempt = attempts[i];
            if (attempt == null || !attempt.countsTowardMastery ||
                !string.Equals(attempt.conceptId, conceptId, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!attempt.correct)
                failures++;
            foreach (string tag in attempt.errorTags ?? new List<string>())
                counts[tag] = counts.TryGetValue(tag, out int count) ? count + 1 : 1;
        }
        foreach (KeyValuePair<string, int> pair in counts)
            if (pair.Value >= 2 && tags.Count < 3)
                tags.Add(pair.Key);
        return failures;
    }

    static List<string> FindDueReviews(BuddyLearnerStateRecord learner, IReadOnlyList<NaturalGrammarRegion> regions, string selected, DateTime now, int limit)
    {
        var due = new List<Tuple<DateTime, string>>();
        foreach (NaturalGrammarRegion region in regions)
        {
            string id = region?.conceptId.ToString();
            if (string.IsNullOrWhiteSpace(id) || string.Equals(id, selected, StringComparison.OrdinalIgnoreCase))
                continue;
            BuddyConceptLearningStateRecord concept = FindConcept(learner, id);
            DateTime dueAt = ParseUtc(concept?.nextReviewAtUtc, DateTime.MaxValue);
            if (concept != null && concept.attempts > 0 && dueAt <= now)
                due.Add(Tuple.Create(dueAt, id));
        }
        due.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        var result = new List<string>();
        for (int i = 0; i < due.Count && result.Count < limit; i++)
            result.Add(due[i].Item2);
        return result;
    }

    static BuddyConceptLearningStateRecord FindConcept(BuddyLearnerStateRecord learner, string conceptId)
    {
        if (learner?.concepts == null)
            return null;
        return learner.concepts.Find(c => c != null && string.Equals(c.conceptId, conceptId, StringComparison.OrdinalIgnoreCase));
    }

    static GrammarMapAreaState FindCurrentArea(GrammarWorldProgressData progress)
    {
        if (progress?.areas == null || string.IsNullOrWhiteSpace(progress.currentAreaId))
            return null;
        return progress.areas.Find(a => a != null && string.Equals(a.areaId, progress.currentAreaId, StringComparison.OrdinalIgnoreCase));
    }

    static DateTime ParseUtc(string value, DateTime fallback)
    {
        return DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime parsed)
            ? parsed.ToUniversalTime()
            : fallback;
    }

    static string Explain(LearnerScheduleActivity activity, NaturalGrammarRegion region, List<string> errors)
    {
        string name = region?.displayName ?? "this lesson";
        return activity switch
        {
            LearnerScheduleActivity.Introduction => $"Start the next available foundation in {name}.",
            LearnerScheduleActivity.MicroLesson => errors.Count > 0
                ? $"Give a short worked example for {errors[0]}, then retry in {name}."
                : $"Give one short worked example, then retry in {name}.",
            LearnerScheduleActivity.RetrievalReview => $"This concept is due for a short memory-strengthening review in {name}.",
            LearnerScheduleActivity.Challenge => $"Independent performance is strong; use a harder transfer challenge in {name}.",
            _ => $"Continue building independent success in {name} with limited hints.",
        };
    }

    static string ToSnakeCase(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "guided_practice";
        var chars = new List<char>();
        for (int i = 0; i < value.Length; i++)
        {
            if (i > 0 && char.IsUpper(value[i]))
                chars.Add('_');
            chars.Add(char.ToLowerInvariant(value[i]));
        }
        return new string(chars.ToArray());
    }
}
