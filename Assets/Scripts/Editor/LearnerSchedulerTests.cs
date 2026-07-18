#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using NUnit.Framework;

public sealed class LearnerSchedulerTests
{
    static readonly DateTime Now = new DateTime(2026, 7, 13, 10, 0, 0, DateTimeKind.Utc);

    [Test]
    public void FreshLearnerStartsAtFirstAvailableFoundation()
    {
        LearnerScheduleRecommendation result = LearnerScheduler.Recommend(
            new BuddyLearnerStateRecord(),
            NaturalGrammarProgression.Regions,
            new GrammarWorldProgressData(),
            Now);

        Assert.AreEqual(GrammarConceptId.Greetings.ToString(), result.conceptId);
        Assert.AreEqual("introduction", result.activityType);
        Assert.AreEqual(1, result.difficulty);
    }

    [Test]
    public void ThreeRecentFailuresTriggerMicroLesson()
    {
        var concept = new BuddyConceptLearningStateRecord
        {
            conceptId = GrammarConceptId.Greetings.ToString(),
            attempts = 3,
            masteryEstimate = 0.2f,
            recommendedEnglishRatio = 0.3f,
            nextReviewAtUtc = Now.AddHours(1).ToString("o"),
        };
        var learner = new BuddyLearnerStateRecord
        {
            concepts = new List<BuddyConceptLearningStateRecord> { concept },
            recentAttempts = new List<BuddyLearningAttemptRecord>(),
        };
        for (int i = 0; i < 3; i++)
        {
            learner.recentAttempts.Add(new BuddyLearningAttemptRecord
            {
                conceptId = concept.conceptId,
                countsTowardMastery = true,
                correct = false,
                errorTags = new List<string> { "word_order" },
            });
        }

        LearnerScheduleRecommendation result = LearnerScheduler.Recommend(
            learner, NaturalGrammarProgression.Regions, new GrammarWorldProgressData(), Now);

        Assert.AreEqual("micro_lesson", result.activityType);
        Assert.AreEqual("direct", result.buddySupport);
        CollectionAssert.Contains(result.focusErrorTags, "word_order");
    }

    [Test]
    public void IndependentSuccessAdvancesReviewWhileFailureCreatesShortRetry()
    {
        var concept = new BuddyConceptLearningStateRecord();
        LearnerScheduler.UpdateReviewState(concept, new BuddyLearningAttemptRecord
        {
            countsTowardMastery = true,
            correct = true,
            completedIndependently = true,
            createdAtUtc = Now.ToString("o"),
        });
        Assert.AreEqual(1, concept.reviewStage);
        Assert.AreEqual(Now.AddDays(1), DateTime.Parse(concept.nextReviewAtUtc).ToUniversalTime());

        LearnerScheduler.UpdateReviewState(concept, new BuddyLearningAttemptRecord
        {
            countsTowardMastery = true,
            correct = false,
            createdAtUtc = Now.AddDays(1).ToString("o"),
        });
        Assert.AreEqual(0, concept.reviewStage);
        Assert.AreEqual(1, concept.lapseCount);
        Assert.AreEqual(Now.AddDays(1).AddMinutes(10), DateTime.Parse(concept.nextReviewAtUtc).ToUniversalTime());
    }

    [Test]
    public void DueReviewBeatsCurrentAreaConvenienceBonus()
    {
        NaturalGrammarRegion first = NaturalGrammarProgression.Regions[0];
        NaturalGrammarRegion second = NaturalGrammarProgression.Regions[1];
        var learner = new BuddyLearnerStateRecord
        {
            concepts = new List<BuddyConceptLearningStateRecord>
            {
                new BuddyConceptLearningStateRecord
                {
                    conceptId = first.conceptId.ToString(), attempts = 10, masteryEstimate = 0.9f,
                    independentCorrectAttempts = 7, nextReviewAtUtc = Now.AddDays(5).ToString("o"),
                },
                new BuddyConceptLearningStateRecord
                {
                    conceptId = second.conceptId.ToString(), attempts = 8, masteryEstimate = 0.8f,
                    independentCorrectAttempts = 5, nextReviewAtUtc = Now.AddDays(-2).ToString("o"),
                },
            },
        };
        var progress = new GrammarWorldProgressData
        {
            currentAreaId = "current",
            unlockedConceptIds = new List<string> { first.conceptId.ToString(), second.conceptId.ToString() },
            areas = new List<GrammarMapAreaState>
            {
                new GrammarMapAreaState { areaId = "current", conceptId = first.conceptId, grammarTopicTier = first.tier },
            },
        };

        LearnerScheduleRecommendation result = LearnerScheduler.Recommend(
            learner, NaturalGrammarProgression.Regions, progress, Now);

        Assert.AreEqual(second.conceptId.ToString(), result.conceptId);
        Assert.AreEqual("retrieval_review", result.activityType);
        Assert.IsTrue(result.isReviewDue);
    }
}
#endif
