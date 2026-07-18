using System;
using System.Collections.Generic;
using UnityEngine;


public static partial class NaturalGrammarProgression
{
    public static List<LocalizedDialogueLine> BuildGeneratedDialogueSet(
        SemanticZoneKind zoneKind,
        string grammarTopic,
        int tier,
        bool trainerBattle)
    {
        string[] ids = GetDialogueTaskIds(zoneKind, grammarTopic, tier);
        if (ids == null || ids.Length == 0)
        {
            return new List<LocalizedDialogueLine>
            {
                BuildGeneratedDialogue(zoneKind, grammarTopic, tier, 0, trainerBattle),
            };
        }

        var lines = new List<LocalizedDialogueLine>();
        for (int i = 0; i < ids.Length; i++)
            lines.Add(BuildGeneratedDialogue(zoneKind, grammarTopic, tier, i, trainerBattle));
        return lines;
    }

    public static List<EnemyAttackDefinition> BuildEnemyAttackSet(
        SemanticZoneKind zoneKind,
        string grammarTopic,
        int tier,
        string enemyNounFamily = "",
        IEnumerable<GrammarPhrasePattern> practicePatterns = null)
    {
        NaturalGrammarRegion region = ResolveByTopicOrTier(grammarTopic, tier);
        string nounFamily = ResolveEnemyNounFamily(region, enemyNounFamily);
        VerbActionDefinition offenseVerb = ResolveEnemyVerb(nounFamily, BattleActionRole.Offense);
        VerbActionDefinition mobilityVerb = ResolveEnemyVerb(nounFamily, BattleActionRole.Dodge) ??
            ResolveEnemyVerb(nounFamily, BattleActionRole.Defense);
        var attacks = new List<EnemyAttackDefinition>
        {
            new EnemyAttackDefinition
            {
                attackId = BuildEnemyAttackId(nounFamily, offenseVerb, "DirectAttack"),
                animationTrigger = "Attack",
                hitboxId = "Melee",
                range = 1.85f,
                cooldown = zoneKind == SemanticZoneKind.Gym ? 1.1f : 1.35f,
                windupSeconds = 0.28f,
                activeSeconds = 0.22f,
                recoverySeconds = 0.42f,
                weight = 3f,
                damage = zoneKind == SemanticZoneKind.Gym ? 2 : 1,
                battleRole = BattleActionRole.Offense,
                grammarNounFamily = nounFamily,
                grammarVerb = ResolveVerbToken(offenseVerb, "ATTACK"),
                grammarCommand = BuildEnemyCommand(nounFamily, offenseVerb, "attacks"),
                grammarPattern = GrammarPhrasePattern.NounVerbPresent,
            },
            new EnemyAttackDefinition
            {
                attackId = BuildEnemyAttackId(nounFamily, mobilityVerb, "Dodge"),
                animationTrigger = "Move",
                hitboxId = "Melee",
                range = 4.5f,
                cooldown = 2.4f,
                windupSeconds = 0.12f,
                activeSeconds = 0.05f,
                recoverySeconds = 0.25f,
                weight = 1.25f,
                damage = 0,
                battleRole = BattleActionRole.Dodge,
                dodgeSeconds = 0.45f,
                grammarNounFamily = nounFamily,
                grammarVerb = ResolveVerbToken(mobilityVerb, "DODGE"),
                grammarCommand = BuildEnemyCommand(nounFamily, mobilityVerb, "dodges"),
                grammarPattern = GrammarPhrasePattern.NounVerbPresent,
            },
        };

        GrammarBattleCurse curse = SelectEncounterCurse(region, zoneKind, practicePatterns);
        if (curse != GrammarBattleCurse.None)
        {
            attacks.Add(new EnemyAttackDefinition
            {
                attackId = $"{curse}Curse",
                animationTrigger = "Attack",
                hitboxId = "Melee",
                range = zoneKind == SemanticZoneKind.Gym ? 7f : 5.5f,
                cooldown = zoneKind == SemanticZoneKind.Gym ? 2.6f : 3.4f,
                windupSeconds = 0.18f,
                activeSeconds = 0.08f,
                recoverySeconds = 0.35f,
                weight = zoneKind == SemanticZoneKind.Gym ? 2.2f : 1.4f,
                damage = 0,
                battleRole = BattleActionRole.Curse,
                grammarNounFamily = nounFamily,
                grammarVerb = "CURSE",
                grammarCommand = $"{FormatCommandNoun(nounFamily)} curses",
                grammarPattern = GrammarPhrasePattern.NounVerbPresent,
                inflictedGrammarCurse = curse,
            });
        }

        return attacks;
    }

    static GrammarBattleCurse SelectEncounterCurse(
        NaturalGrammarRegion region,
        SemanticZoneKind zoneKind,
        IEnumerable<GrammarPhrasePattern> practicePatterns)
    {
        GrammarBattleCurse patternCurse = SelectPatternCurse(region, zoneKind, practicePatterns);
        return patternCurse != GrammarBattleCurse.None
            ? patternCurse
            : SelectRegionCurse(region, zoneKind);
    }

    static GrammarBattleCurse SelectPatternCurse(
        NaturalGrammarRegion region,
        SemanticZoneKind zoneKind,
        IEnumerable<GrammarPhrasePattern> practicePatterns)
    {
        if (practicePatterns == null)
            return GrammarBattleCurse.None;

        foreach (GrammarPhrasePattern pattern in practicePatterns)
        {
            switch (pattern)
            {
                case GrammarPhrasePattern.PastTense:
                    return RegionHasCurse(region, GrammarBattleCurse.PastFog)
                        ? GrammarBattleCurse.PastFog
                        : GrammarBattleCurse.None;
                case GrammarPhrasePattern.ProgressiveTense:
                    return RegionHasCurse(region, GrammarBattleCurse.NowMist)
                        ? GrammarBattleCurse.NowMist
                        : GrammarBattleCurse.None;
                case GrammarPhrasePattern.PronounVerbPresent:
                    return SelectPronounCurse(region, zoneKind);
            }
        }

        return GrammarBattleCurse.None;
    }

    static GrammarBattleCurse SelectPronounCurse(NaturalGrammarRegion region, SemanticZoneKind zoneKind)
    {
        if (region == null || region.newCurses == null || region.newCurses.Length == 0)
            return GrammarBattleCurse.None;

        GrammarBattleCurse[] preferred = zoneKind == SemanticZoneKind.Gym
            ? new[] { GrammarBattleCurse.HeSheIt, GrammarBattleCurse.They, GrammarBattleCurse.I, GrammarBattleCurse.You }
            : new[] { GrammarBattleCurse.I, GrammarBattleCurse.You, GrammarBattleCurse.HeSheIt, GrammarBattleCurse.They };
        foreach (GrammarBattleCurse curse in preferred)
        {
            if (RegionHasCurse(region, curse))
                return curse;
        }

        return GrammarBattleCurse.None;
    }

    static bool RegionHasCurse(NaturalGrammarRegion region, GrammarBattleCurse curse)
    {
        if (region == null || region.newCurses == null || curse == GrammarBattleCurse.None)
            return false;

        foreach (GrammarBattleCurse candidate in region.newCurses)
            if (candidate == curse)
                return true;
        return false;
    }
}
