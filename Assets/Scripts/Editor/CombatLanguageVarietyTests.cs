#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class CombatLanguageVarietyTests
{
    [Test]
    public void CombatWordVarietyTracker_DiminishesRecentVerbAndAdverbReuse()
    {
        var tracker = new CombatWordVarietyTracker();

        CombatWordVarietyEvaluation first = tracker.EvaluateAction("BITE", "FAST");
        tracker.RecordAction("BITE", "FAST");
        CombatWordVarietyEvaluation second = tracker.EvaluateAction("BITE", "FAST");
        tracker.RecordAction("BITE", "FAST");
        CombatWordVarietyEvaluation third = tracker.EvaluateAction("BITE", "FAST");

        Assert.AreEqual(1f, first.effectiveness, 0.001f);
        Assert.Less(second.effectiveness, first.effectiveness);
        Assert.Less(third.effectiveness, second.effectiveness);
        Assert.Greater(second.ppCostMultiplier, 1f);
        Assert.Greater(second.cooldownMultiplier, 1f);
        Assert.That(second.BuildFeedback(), Does.Contain("Try a different verb or adverb"));

        for (int i = 0; i < 12; i++)
            tracker.Record(CombatWordRole.Verb, "BITE");
        Assert.LessOrEqual(tracker.Evaluate(CombatWordRole.Verb, "BITE").repeatLevel, CombatWordVarietyTracker.DefaultHistorySize);
        tracker.Clear();
        Assert.AreEqual(1f, tracker.EvaluateAction("BITE", "FAST").effectiveness, 0.001f);
    }

    [Test]
    public void CreatureCombatRegistry_FamilyAliasesShareNounRulesAndAdjectives()
    {
        GameObject go = new GameObject("CombatNounFamilyTest");
        CreatureCombatCatalog catalog = null;
        try
        {
            CreatureCombatRegistry registry = go.AddComponent<CreatureCombatRegistry>();
            catalog = CreateFocusedCatalog();
            registry.catalog = catalog;

            Assert.IsTrue(registry.AreInSameNounFamily("RAT", "MOUSE"));
            Assert.IsTrue(registry.TryParsePhrase("BIG RAT", out CreaturePhraseParseResult rat));
            Assert.IsTrue(registry.TryParsePhrase("BIG MOUSE", out CreaturePhraseParseResult mouse));
            Assert.AreSame(rat.noun, mouse.noun);
            Assert.AreSame(rat.modifier, mouse.modifier);
            CollectionAssert.AreEquivalent(
                new[] { "RAT", "MOUSE" },
                registry.GetNounFamilyForms("mouse"));
        }
        finally
        {
            if (catalog != null)
                Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void CombatLanguageGuide_ListsAllUseCaseCategoriesAndSummonAdjectives()
    {
        GameObject go = new GameObject("CombatLanguageGuideTest");
        CreatureCombatCatalog catalog = null;
        try
        {
            CreatureCombatRegistry registry = go.AddComponent<CreatureCombatRegistry>();
            catalog = CreateFocusedCatalog();
            registry.catalog = catalog;

            string commandGuide = CombatLanguageGuide.BuildHudText(registry, "MOUSE");
            string summonGuide = CombatLanguageGuide.BuildHudText(registry, "", "MOUSE");

            Assert.That(commandGuide, Does.Contain("ATTACK (damage): BITE"));
            Assert.That(commandGuide, Does.Contain("DEFENSE (block): BLOCK"));
            Assert.That(commandGuide, Does.Contain("MOBILITY (move/dodge): RUN"));
            Assert.That(commandGuide, Does.Contain("UTILITY (boost/help): LOOK"));
            Assert.That(commandGuide, Does.Contain("ADVERBS: FAST"));
            Assert.That(summonGuide, Does.Contain("RAT (also MOUSE)"));
            Assert.That(summonGuide, Does.Contain("BIG (attack)"));
        }
        finally
        {
            if (catalog != null)
                Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void TacticalGrammarBattler_RepeatedWordsLoseDamageAndAdjectiveEffect()
    {
        GameObject go = new GameObject("TacticalVarietyTest");
        CreatureCombatCatalog catalog = null;
        try
        {
            CreatureCombatRegistry registry = go.AddComponent<CreatureCombatRegistry>();
            catalog = CreateFocusedCatalog();
            registry.catalog = catalog;
            var battler = new TacticalGrammarBattler(registry);
            battler.SetEnemyUnit("RAT", new TacticalBattlePosition(0, 1));

            TacticalBattleCommandResult firstSummon = battler.SummonPlayer("BIG RAT", new TacticalBattlePosition(0, 0));
            int firstAttackStat = battler.State.playerUnit.stats.attack;
            TacticalBattleCommandResult repeatedFamilySummon = battler.SummonPlayer("BIG MOUSE", new TacticalBattlePosition(0, 0));
            int repeatedAttackStat = battler.State.playerUnit.stats.attack;

            Assert.IsTrue(firstSummon.success);
            Assert.IsTrue(repeatedFamilySummon.success);
            Assert.Less(repeatedAttackStat, firstAttackStat);
            Assert.Less(battler.State.playerUnit.adjectiveEffectiveness, 1f);
            Assert.That(repeatedFamilySummon.message, Does.Contain("Repeated adjective BIG"));

            TacticalBattleCommandResult firstBite = battler.ExecutePlayerCommand("mouse bites");
            int firstDamage = battler.ResolvePlayerAttackDamage(firstBite.actionProfile);
            TacticalBattleCommandResult repeatedBite = battler.ExecutePlayerCommand("rat bites");
            int repeatedDamage = battler.ResolvePlayerAttackDamage(repeatedBite.actionProfile);

            Assert.IsTrue(firstBite.success, firstBite.message);
            Assert.IsTrue(repeatedBite.success, repeatedBite.message);
            Assert.Less(repeatedDamage, firstDamage);
            Assert.Greater(repeatedBite.actionProfile.ppCost, firstBite.actionProfile.ppCost);
            Assert.That(repeatedBite.message, Does.Contain("Repeated verb BITE"));
        }
        finally
        {
            if (catalog != null)
                Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void SummonedCreatureActor_RepeatedVerbReportsAndAppliesVarietyPenalty()
    {
        GameObject go = new GameObject("RealtimeCombatVarietyTest");
        CreatureCombatCatalog catalog = null;
        try
        {
            SummonedCreatureActor actor = go.AddComponent<SummonedCreatureActor>();
            catalog = CreateFocusedCatalog();
            NounDefinition noun = catalog.nouns[0];
            noun.moveSet = new List<NounMoveSlot>();
            noun.baseStats = new CreatureStatBlock
            {
                maxHp = 20,
                attack = 10,
                defense = 5,
                speed = 5,
                maxPp = 100,
            };
            VerbActionDefinition bite = CreateVerb("BITE", "BITES", BattleActionRole.Attack, false, 10);
            bite.cooldownSeconds = 0f;
            actor.Configure(noun, null);

            Assert.IsTrue(actor.TryUseVerb(bite, null, null, out _));
            int firstCost = actor.LastPpSpent;
            Assert.AreEqual(1f, actor.LastVarietyEffectiveness, 0.001f);
            Assert.IsTrue(actor.TryUseVerb(bite, null, null, out string repeatedMessage));

            Assert.Less(actor.LastVarietyEffectiveness, 1f);
            Assert.Greater(actor.LastPpSpent, firstCost);
            Assert.That(repeatedMessage, Does.Contain("Repeated verb BITE"));
        }
        finally
        {
            if (catalog != null)
                Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(go);
        }
    }

    static CreatureCombatCatalog CreateFocusedCatalog()
    {
        CreatureCombatCatalog catalog = ScriptableObject.CreateInstance<CreatureCombatCatalog>();
        var noun = new NounDefinition
        {
            canonicalNoun = "RAT",
            synonyms = new List<string> { "MOUSE" },
            nounRole = GrammarNounRole.Creature,
            semanticTags = new List<string> { "ANIMAL" },
            baseStats = new CreatureStatBlock
            {
                maxHp = 30,
                attack = 10,
                defense = 8,
                speed = 8,
                maxPp = 100,
            },
            allowedAdjectives = new List<string> { "BIG" },
            allowedVerbs = new List<string> { "BITE", "BLOCK", "RUN", "LOOK" },
            moveSet = new List<NounMoveSlot>
            {
                CreateMove("BITE", CreatureVerbCategory.Attack),
                CreateMove("BLOCK", CreatureVerbCategory.Defense),
                CreateMove("RUN", CreatureVerbCategory.Movement),
                CreateMove("LOOK", CreatureVerbCategory.Utility),
            },
        };

        catalog.nouns = new List<NounDefinition> { noun };
        catalog.verbs = new List<VerbActionDefinition>
        {
            CreateVerb("BITE", "BITES", BattleActionRole.Attack, false, 10),
            CreateVerb("BLOCK", "BLOCKS", BattleActionRole.Defense, false, 0),
            CreateVerb("RUN", "RUNS", BattleActionRole.Mobility, true, 0),
            CreateVerb("LOOK", "LOOKS", BattleActionRole.Utility, false, 0),
        };
        catalog.modifiers = new List<ModifierDefinition>
        {
            new ModifierDefinition
            {
                modifier = "BIG",
                role = ModifierGrammarRole.Adjective,
                attackMultiplier = 2f,
                maxHpMultiplier = 1.5f,
            },
            new ModifierDefinition
            {
                modifier = "FAST",
                role = ModifierGrammarRole.Adverb,
                speedMultiplier = 1.5f,
            },
        };
        return catalog;
    }

    static NounMoveSlot CreateMove(string verb, CreatureVerbCategory category)
    {
        return new NounMoveSlot
        {
            verbId = verb,
            category = category,
            baseMaxPp = 100,
            minMaxPp = 1,
            allowedAdverbs = new List<string> { "FAST" },
        };
    }

    static VerbActionDefinition CreateVerb(
        string verb,
        string thirdPerson,
        BattleActionRole role,
        bool movement,
        int power,
        params string[] tags)
    {
        return new VerbActionDefinition
        {
            verb = verb,
            thirdPersonSingularForms = new List<string> { thirdPerson },
            role = role,
            movementVerb = movement,
            tacticalMovementCells = movement ? 2 : 0,
            tacticalRangeCells = 1,
            ppCost = 2,
            power = power,
            cooldownSeconds = 0f,
            verbTags = tags != null ? new List<string>(tags) : new List<string>(),
            allowedAdverbs = new List<string> { "FAST" },
        };
    }
}
#endif
