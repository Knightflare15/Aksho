#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class GrammarRpgFlowTests
{
    [Test]
    public void CanonicalCurriculumOrderAndTiersMatchTeacherContract()
    {
        string[] expectedTopics =
        {
            "Greetings and Survival English", "Alphabet", "Vowels and Consonants",
            "Sentence Start and Full Stop", "Nouns", "Verbs", "Articles", "Pronouns",
            "Plurals", "Adjectives", "Basic Prepositions",
        };
        Assert.AreEqual(expectedTopics.Length, NaturalGrammarProgression.Regions.Count);
        for (int i = 0; i < expectedTopics.Length; i++)
        {
            Assert.AreEqual(expectedTopics[i], NaturalGrammarProgression.Regions[i].grammarTopic);
            Assert.AreEqual(i + 1, NaturalGrammarProgression.Regions[i].tier);
        }
    }

    [TestCase("TOWN:ARTICLES:8", "TOWN:ARTICLES:7")]
    [TestCase("ROUTE:PRONOUNS:10", "ROUTE:PRONOUNS:8")]
    [TestCase("GYM:PLURALS:11", "GYM:PLURALS:9")]
    [TestCase("TOWN:ADJECTIVES:9", "TOWN:ADJECTIVES:10")]
    [TestCase("GYM:BASICPREPOSITIONS:7", "GYM:BASICPREPOSITIONS:11")]
    public void LegacyAreaIdsMigrateByTopic(string oldId, string expected)
    {
        Assert.AreEqual(expected, GrammarWorldProgressService.CanonicalizeAreaId(oldId));
    }

    [Test]
    public void AreaIdMigrationPreservesTownRouteAndGymProgress()
    {
        GameObject go = new GameObject("AreaMigrationTest");
        try
        {
            GrammarWorldProgressService service = go.AddComponent<GrammarWorldProgressService>();
            var saved = new GrammarWorldProgressData
            {
                areas = new List<GrammarMapAreaState>
                {
                    new GrammarMapAreaState { areaId = "TOWN:ARTICLES:8", sceneKind = SemanticZoneKind.Town, grammarTopic = "Articles", grammarTopicTier = 8 },
                    new GrammarMapAreaState { areaId = "ROUTE:ARTICLES:8", sceneKind = SemanticZoneKind.Route, grammarTopic = "Articles", grammarTopicTier = 8 },
                    new GrammarMapAreaState { areaId = "GYM:ARTICLES:8", sceneKind = SemanticZoneKind.Gym, grammarTopic = "Articles", grammarTopicTier = 8 },
                }
            };
            typeof(GrammarWorldProgressService)
                .GetField("data", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(service, saved);
            typeof(GrammarWorldProgressService)
                .GetMethod("MigrateAreaIdsToCanonicalForm", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(service, null);

            Assert.AreEqual(3, saved.areas.Count);
            CollectionAssert.AreEquivalent(
                new[] { SemanticZoneKind.Town, SemanticZoneKind.Route, SemanticZoneKind.Gym },
                saved.areas.Select(area => area.sceneKind));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void EveryRegionHasTownRouteAndGymBlueprints()
    {
        foreach (NaturalGrammarRegion region in NaturalGrammarProgression.Regions)
        {
            foreach (SemanticZoneKind kind in new[] { SemanticZoneKind.Town, SemanticZoneKind.Route, SemanticZoneKind.Gym })
            {
                string id = GrammarWorldProgressService.BuildAreaId(kind, region.grammarTopic, region.tier);
                Assert.IsTrue(GrammarWorldRuntimeBootstrap.TryResolveAreaBlueprint(
                    id, out _, out SemanticZoneKind resolvedKind, out string topic, out int tier, out _, out _), id);
                Assert.AreEqual(kind, resolvedKind);
                Assert.AreEqual(region.grammarTopic, topic);
                Assert.AreEqual(region.tier, tier);
            }
        }
    }

    [Test]
    public void WorldGoalDefaultsToConfiguredWeeklyReward()
    {
        var goal = new WorldGoalAssignment();
        Assert.AreEqual(25, goal.rewardCoins);
        Assert.AreEqual("Asia/Kolkata", goal.schoolTimeZone);
    }

    [Test]
    public void SentenceAssessmentPreservesCapitalizationAndTerminalPunctuation()
    {
        MethodInfo normalize = typeof(GrammarSceneController).GetMethod(
            "NormalizeTypedSentence",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(normalize);
        Assert.AreEqual("I am ready.", normalize.Invoke(null, new object[] { "  I  am ready . " }));
        Assert.AreNotEqual(
            normalize.Invoke(null, new object[] { "I am ready." }),
            normalize.Invoke(null, new object[] { "i am ready" }));
    }

    [Test]
    public void ProductionBuildContainsOnlyWorldScenes()
    {
        string[] enabled = UnityEditor.EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => System.IO.Path.GetFileNameWithoutExtension(scene.path))
            .ToArray();
        CollectionAssert.AreEqual(new[] { "MainMenu", "Town", "Route", "Gym", "Battle", "Shop" }, enabled);
    }
}
#endif
