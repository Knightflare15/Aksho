using System;
using System.Collections.Generic;
using UnityEngine;


public static partial class NaturalGrammarProgression
{
    static NaturalGrammarRegion[] BuildProgressionOrder()
    {
        var ordered = new List<NaturalGrammarRegion>(FirstSlice);
        ordered.Sort((left, right) => left.tier.CompareTo(right.tier));
        return ordered.ToArray();
    }

    static GrammarEncounterPoolDefinition Pool(
        string poolId,
        string displayName,
        SemanticZoneKind zoneKind,
        int enemyCount,
        GrammarPhrasePattern[] practicePatterns,
        string[] masteryTags,
        params string[] nounFamilies)
    {
        return new GrammarEncounterPoolDefinition
        {
            poolId = poolId,
            displayName = displayName,
            zoneKind = zoneKind,
            enemyCount = Mathf.Max(1, enemyCount),
            nounFamilies = new List<string>(nounFamilies ?? Array.Empty<string>()),
            practicePatterns = new List<GrammarPhrasePattern>(practicePatterns ?? Array.Empty<GrammarPhrasePattern>()),
            masteryTags = new List<string>(masteryTags ?? Array.Empty<string>()),
        };
    }

    static GrammarPhrasePattern[] Patterns(params GrammarPhrasePattern[] patterns)
    {
        return patterns ?? Array.Empty<GrammarPhrasePattern>();
    }

    static string[] Tags(params string[] tags)
    {
        return tags ?? Array.Empty<string>();
    }

    public static NaturalGrammarRegion Resolve(string grammarTopic, int tier)
    {
        string normalizedTopic = SpellRegistry.NormalizeWord(grammarTopic);
        if (!string.IsNullOrEmpty(normalizedTopic))
        {
            foreach (NaturalGrammarRegion region in ProgressionOrder)
            {
                if (SpellRegistry.NormalizeWord(region.displayName) == normalizedTopic ||
                    SpellRegistry.NormalizeWord(region.grammarTopic) == normalizedTopic)
                    return region;
            }
        }

        int clampedTier = Mathf.Max(1, tier);
        foreach (NaturalGrammarRegion region in ProgressionOrder)
        {
            if (region.tier == clampedTier)
                return region;
        }

        return ProgressionOrder[0];
    }

    public static NaturalGrammarRegion ResolveByTopicOrTier(string grammarTopic, int tier)
    {
        string normalizedTopic = SpellRegistry.NormalizeWord(grammarTopic);
        if (!string.IsNullOrEmpty(normalizedTopic))
        {
            foreach (NaturalGrammarRegion region in ProgressionOrder)
            {
                if (SpellRegistry.NormalizeWord(region.displayName) == normalizedTopic ||
                    SpellRegistry.NormalizeWord(region.grammarTopic) == normalizedTopic)
                    return region;
            }
        }

        return Resolve(grammarTopic, tier);
    }

    public static bool IsCombatUnlocked(string grammarTopic, int tier)
    {
        return Resolve(grammarTopic, tier).combatUnlocked;
    }

    public static bool IsTacticalCombatUnlocked(string grammarTopic, int tier)
    {
        NaturalGrammarRegion region = Resolve(grammarTopic, tier);
        return region.combatUnlocked && region.encounterMode == GrammarEncounterMode.TacticalCommand;
    }

    public static GrammarEncounterMode ResolveEncounterMode(string grammarTopic, int tier)
    {
        return Resolve(grammarTopic, tier).encounterMode;
    }

    public static List<string> BuildCurrentNounFamilies(string grammarTopic, int tier)
    {
        return new List<string>(Resolve(grammarTopic, tier).currentNounFamilies ?? Array.Empty<string>());
    }

    public static List<string> BuildReviewNounFamilies(string grammarTopic, int tier)
    {
        return new List<string>(Resolve(grammarTopic, tier).reviewNounFamilies ?? Array.Empty<string>());
    }

    public static GrammarEncounterPoolDefinition ResolveWildEncounterPool(string grammarTopic, int tier, int poolIndex)
    {
        NaturalGrammarRegion region = Resolve(grammarTopic, tier);
        return PickEncounterPool(region, region.wildEncounterPools, poolIndex, "wild");
    }

    public static GrammarEncounterPoolDefinition ResolveTrainerBattlePool(string grammarTopic, int tier, int poolIndex)
    {
        NaturalGrammarRegion region = Resolve(grammarTopic, tier);
        return PickEncounterPool(region, region.trainerBattlePools, poolIndex, "trainer");
    }

    public static List<string> BuildWildEncounterNounFamilies(string grammarTopic, int tier, int poolIndex)
    {
        return BuildEncounterPoolNouns(ResolveWildEncounterPool(grammarTopic, tier, poolIndex));
    }

    public static List<string> BuildTrainerBattleNounFamilies(string grammarTopic, int tier, int poolIndex)
    {
        return BuildEncounterPoolNouns(ResolveTrainerBattlePool(grammarTopic, tier, poolIndex));
    }

    public static List<GrammarPhrasePattern> BuildWildEncounterPracticePatterns(string grammarTopic, int tier, int poolIndex)
    {
        return BuildEncounterPoolPracticePatterns(ResolveWildEncounterPool(grammarTopic, tier, poolIndex));
    }

    public static List<GrammarPhrasePattern> BuildTrainerBattlePracticePatterns(string grammarTopic, int tier, int poolIndex)
    {
        return BuildEncounterPoolPracticePatterns(ResolveTrainerBattlePool(grammarTopic, tier, poolIndex));
    }

    public static List<string> BuildWildEncounterMasteryTags(string grammarTopic, int tier, int poolIndex)
    {
        return BuildEncounterPoolMasteryTags(ResolveWildEncounterPool(grammarTopic, tier, poolIndex));
    }

    public static List<string> BuildTrainerBattleMasteryTags(string grammarTopic, int tier, int poolIndex)
    {
        return BuildEncounterPoolMasteryTags(ResolveTrainerBattlePool(grammarTopic, tier, poolIndex));
    }

    public static int ResolveWildEncounterEnemyCount(string grammarTopic, int tier, int poolIndex)
    {
        return Mathf.Max(1, ResolveWildEncounterPool(grammarTopic, tier, poolIndex).enemyCount);
    }

    public static int ResolveTrainerBattleEnemyCount(string grammarTopic, int tier, int poolIndex)
    {
        return Mathf.Max(1, ResolveTrainerBattlePool(grammarTopic, tier, poolIndex).enemyCount);
    }

    static GrammarEncounterPoolDefinition PickEncounterPool(
        NaturalGrammarRegion region,
        GrammarEncounterPoolDefinition[] pools,
        int poolIndex,
        string fallbackId)
    {
        if (pools != null && pools.Length > 0)
        {
            int index = Mathf.Abs(poolIndex) % pools.Length;
            if (pools[index] != null)
                return pools[index];
        }

        return Pool(
            $"{region.id}-{fallbackId}-fallback",
            $"{region.displayName} {fallbackId}",
            SemanticZoneKind.Route,
            1,
            region.unlockedPhrasePatterns ?? Array.Empty<GrammarPhrasePattern>(),
            region.masteryTags ?? Array.Empty<string>(),
            region.currentNounFamilies ?? Array.Empty<string>());
    }

    static List<string> BuildEncounterPoolNouns(GrammarEncounterPoolDefinition pool)
    {
        return pool != null && pool.nounFamilies != null
            ? new List<string>(pool.nounFamilies)
            : new List<string>();
    }

    static List<GrammarPhrasePattern> BuildEncounterPoolPracticePatterns(GrammarEncounterPoolDefinition pool)
    {
        return pool != null && pool.practicePatterns != null
            ? new List<GrammarPhrasePattern>(pool.practicePatterns)
            : new List<GrammarPhrasePattern>();
    }

    static List<string> BuildEncounterPoolMasteryTags(GrammarEncounterPoolDefinition pool)
    {
        return pool != null && pool.masteryTags != null
            ? new List<string>(pool.masteryTags)
            : new List<string>();
    }

    public static string[] GetDialogueTaskIds(SemanticZoneKind zoneKind, string grammarTopic, int tier)
    {
        NaturalGrammarRegion region = Resolve(grammarTopic, tier);
        return GetDialogueTaskIds(region, zoneKind);
    }

    public static string ResolveTownNpcName(string grammarTopic, int tier, int npcIndex)
    {
        NaturalGrammarRegion region = Resolve(grammarTopic, tier);
        return PickNpcName(region?.townNpcNames, $"Guide {Mathf.Abs(npcIndex) + 1}", npcIndex);
    }

    public static string ResolveRouteNpcName(string grammarTopic, int tier, int npcIndex)
    {
        NaturalGrammarRegion region = Resolve(grammarTopic, tier);
        return PickNpcName(region?.routeNpcNames, $"Route Guide {Mathf.Abs(npcIndex) + 1}", npcIndex);
    }

    public static string ResolveGymLeaderName(string grammarTopic, int tier)
    {
        NaturalGrammarRegion region = Resolve(grammarTopic, tier);
        if (region == null)
            return "Gym Leader";
        return string.IsNullOrWhiteSpace(region.gymLeaderName)
            ? $"{region.displayName} Gym Leader"
            : region.gymLeaderName.Trim();
    }
}
