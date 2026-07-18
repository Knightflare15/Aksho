using System;
using NUnit.Framework;
using UnityEngine;

public class EnemyCreatureFamilyCatalogTests
{
    [Test]
    public void RuntimeDefaults_KeepAuthoredEnemyFamiliesSeparateFromPronunciationPool()
    {
        CreatureCombatCatalog catalog = CreatureCombatCatalog.CreateRuntimeDefault();
        try
        {
            CollectionAssert.AreEquivalent(
                new[] { "RABBIT", "CRAB" },
                CreatureCombatCatalog.AuthoredEnemyCreatureFamilyNouns);

            foreach (string family in CreatureCombatCatalog.AuthoredEnemyCreatureFamilyNouns)
            {
                Assert.IsFalse(
                    Array.Exists(
                        CreatureCombatCatalog.PronunciationBackedConcreteNouns,
                        noun => string.Equals(noun, family, StringComparison.OrdinalIgnoreCase)),
                    $"{family} must not be added to the pronunciation-backed summon pool without pronunciation content.");
            }

            NounDefinition rabbit = FindExactNoun(catalog, "RABBIT");
            NounDefinition crab = FindExactNoun(catalog, "CRAB");
            NounDefinition rat = FindExactNoun(catalog, "RAT");
            NounDefinition ant = FindExactNoun(catalog, "ANT");

            Assert.NotNull(rabbit);
            Assert.NotNull(crab);
            Assert.IsTrue(rabbit.Matches("BUNNY"));
            Assert.IsTrue(rabbit.HasSemanticTag("ANIMAL"));
            Assert.NotNull(rabbit.ResolveMoveSlot("HOP"));
            Assert.IsTrue(crab.HasSemanticTag("ANIMAL"));
            Assert.IsTrue(crab.HasSemanticTag("AQUATIC"));
            Assert.NotNull(crab.ResolveMoveSlot("CRAWL"));

            Assert.IsFalse(rat.Matches("RABBIT"), "RABBIT is a distinct family, not a RAT alias.");
            Assert.IsFalse(ant.Matches("CRAB"), "CRAB is a distinct family, not an ANT alias.");
            AssertRequiredCombatCategories(catalog, rabbit);
            AssertRequiredCombatCategories(catalog, crab);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(catalog);
        }
    }

    [Test]
    public void AuthoredCatalog_ResolvesHedgeHopperAndCragCrabToDedicatedFamilies()
    {
        CreatureCombatCatalog creatureCatalog = Resources.Load<CreatureCombatCatalog>("CreatureCombatCatalog_Main");
        EnemyCatalog enemyCatalog = Resources.Load<EnemyCatalog>("EnemyCatalog_Main");

        Assert.NotNull(creatureCatalog, "Authored creature catalog is missing.");
        Assert.NotNull(enemyCatalog, "Authored enemy catalog is missing.");

        EnemyDefinition hedgeHopper = FindEnemy(enemyCatalog, "hedge_hopper");
        EnemyDefinition cragCrab = FindEnemy(enemyCatalog, "crag_crab");
        Assert.NotNull(hedgeHopper);
        Assert.NotNull(cragCrab);

        var registryObject = new GameObject("EnemyCreatureFamilyCatalogTestRegistry");
        try
        {
            CreatureCombatRegistry registry = registryObject.AddComponent<CreatureCombatRegistry>();
            registry.catalog = creatureCatalog;

            Assert.IsTrue(registry.TryGetNoun(hedgeHopper.EffectiveCreatureFamilyNoun, out NounDefinition rabbit));
            Assert.AreEqual("RABBIT", CreaturePhraseUtility.NormalizeToken(rabbit.canonicalNoun));
            Assert.IsTrue(registry.TryGetNoun(cragCrab.EffectiveCreatureFamilyNoun, out NounDefinition crab));
            Assert.AreEqual("CRAB", CreaturePhraseUtility.NormalizeToken(crab.canonicalNoun));

            Assert.NotNull(rabbit.prefabOverride);
            Assert.NotNull(crab.prefabOverride);
            AssertRequiredCombatCategories(creatureCatalog, rabbit);
            AssertRequiredCombatCategories(creatureCatalog, crab);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(registryObject);
        }
    }

    static NounDefinition FindExactNoun(CreatureCombatCatalog catalog, string canonicalNoun)
    {
        return catalog?.nouns?.Find(noun =>
            noun != null &&
            string.Equals(
                CreaturePhraseUtility.NormalizeToken(noun.canonicalNoun),
                CreaturePhraseUtility.NormalizeToken(canonicalNoun),
                StringComparison.OrdinalIgnoreCase));
    }

    static EnemyDefinition FindEnemy(EnemyCatalog catalog, string enemyId)
    {
        return catalog?.enemyDefinitions?.Find(enemy =>
            enemy != null &&
            string.Equals(enemy.enemyId, enemyId, StringComparison.OrdinalIgnoreCase));
    }

    static void AssertRequiredCombatCategories(CreatureCombatCatalog catalog, NounDefinition noun)
    {
        Assert.IsTrue(noun.HasRequiredVerbCategory(CreatureVerbCategory.Attack, catalog.verbs), $"{noun.canonicalNoun} lacks an attack verb.");
        Assert.IsTrue(noun.HasRequiredVerbCategory(CreatureVerbCategory.Movement, catalog.verbs), $"{noun.canonicalNoun} lacks a movement verb.");
        Assert.IsTrue(noun.HasRequiredVerbCategory(CreatureVerbCategory.Defense, catalog.verbs), $"{noun.canonicalNoun} lacks a defense verb.");
    }
}
