using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

[Serializable]
public sealed class CombatVerbHint
{
    public string word = "";
    public CreatureVerbCategory category = CreatureVerbCategory.Unspecified;
    public string useCase = "";
}

[Serializable]
public sealed class CombatModifierHint
{
    public string word = "";
    public string effect = "";
}

/// <summary>
/// Read-only vocabulary queries shared by the combat HUD and tutorial surfaces.
/// The guide derives its suggestions from the same noun/move/modifier data used
/// to validate commands, so displayed words cannot drift from gameplay rules.
/// </summary>
public static class CombatLanguageGuide
{
    public static List<CombatVerbHint> GetVerbHints(
        CreatureCombatRegistry registry,
        string noun,
        int maxPerCategory = 3)
    {
        var result = new List<CombatVerbHint>();
        if (registry == null || !registry.TryGetNoun(noun, out NounDefinition definition) || definition == null)
            return result;

        int categoryLimit = Mathf.Max(1, maxPerCategory);
        var categoryCounts = new Dictionary<CreatureVerbCategory, int>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string verbId in definition.EnumerateVerbIds())
        {
            string normalized = CreaturePhraseUtility.NormalizeToken(verbId);
            if (string.IsNullOrEmpty(normalized) || !seen.Add(normalized))
                continue;

            registry.TryGetVerb(normalized, out VerbActionDefinition verb);
            NounMoveSlot slot = definition.ResolveMoveSlot(normalized);
            CreatureVerbCategory category = slot != null
                ? slot.ResolveCategory(verb)
                : CreatureVerbCategoryUtility.InferCategory(verb, normalized);
            category = NormalizeDisplayCategory(category);
            categoryCounts.TryGetValue(category, out int count);
            if (count >= categoryLimit)
                continue;

            categoryCounts[category] = count + 1;
            result.Add(new CombatVerbHint
            {
                word = verb != null
                    ? CreaturePhraseUtility.NormalizeToken(verb.verb)
                    : normalized,
                category = category,
                useCase = GetUseCase(category),
            });
        }

        return result;
    }

    public static List<CombatModifierHint> GetAdjectiveHints(
        CreatureCombatRegistry registry,
        string noun,
        int max = 5)
    {
        var result = new List<CombatModifierHint>();
        if (registry == null || !registry.TryGetNoun(noun, out NounDefinition definition) || definition == null)
            return result;

        foreach (ModifierDefinition modifier in registry.Modifiers)
        {
            if (modifier == null ||
                modifier.role != ModifierGrammarRole.Adjective ||
                !definition.AllowsAdjective(modifier.modifier) ||
                !modifier.AllowsForNoun(definition))
            {
                continue;
            }

            result.Add(new CombatModifierHint
            {
                word = CreaturePhraseUtility.NormalizeToken(modifier.modifier),
                effect = DescribeAdjectiveEffect(modifier),
            });
            if (result.Count >= Mathf.Max(1, max))
                break;
        }

        return result;
    }

    public static List<CombatModifierHint> GetAdverbHints(
        CreatureCombatRegistry registry,
        string noun,
        int max = 6)
    {
        var result = new List<CombatModifierHint>();
        if (registry == null || !registry.TryGetNoun(noun, out NounDefinition definition) || definition == null)
            return result;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ModifierDefinition modifier in registry.Modifiers)
        {
            if (modifier == null || modifier.role != ModifierGrammarRole.Adverb)
                continue;

            bool fitsMove = false;
            foreach (string verbId in definition.EnumerateVerbIds())
            {
                if (!registry.TryGetVerb(verbId, out VerbActionDefinition verb) || verb == null)
                    continue;
                if (verb.AllowsAdverb(modifier.modifier) &&
                    modifier.AllowsForVerb(verb) &&
                    definition.AllowsAdverb(verb.verb, modifier.modifier))
                {
                    fitsMove = true;
                    break;
                }
            }

            string word = CreaturePhraseUtility.NormalizeToken(modifier.modifier);
            if (!fitsMove || !seen.Add(word))
                continue;

            result.Add(new CombatModifierHint
            {
                word = word,
                effect = DescribeAdverbEffect(modifier),
            });
            if (result.Count >= Mathf.Max(1, max))
                break;
        }

        return result;
    }

    public static string BuildHudText(
        CreatureCombatRegistry registry,
        string activeNoun,
        string desiredNoun = "")
    {
        if (registry == null)
            return "";

        string normalizedActive = CreaturePhraseUtility.NormalizeToken(activeNoun);
        if (string.IsNullOrEmpty(normalizedActive))
            return BuildSummonHudText(registry, desiredNoun);

        if (!registry.TryGetNoun(normalizedActive, out NounDefinition noun) || noun == null)
            return "";

        string canonical = CreaturePhraseUtility.NormalizeToken(noun.canonicalNoun);
        List<CombatVerbHint> verbs = GetVerbHints(registry, canonical);
        var text = new StringBuilder();
        text.Append("COMBAT WORDS - ").Append(BuildFamilyLabel(noun)).AppendLine();
        AppendCategory(text, verbs, CreatureVerbCategory.Attack, "ATTACK", "damage");
        AppendCategory(text, verbs, CreatureVerbCategory.Defense, "DEFENSE", "block");
        AppendCategory(text, verbs, CreatureVerbCategory.Movement, "MOBILITY", "move/dodge");
        AppendCategory(text, verbs, CreatureVerbCategory.Utility, "UTILITY", "boost/help");

        List<CombatModifierHint> adjectives = GetAdjectiveHints(registry, canonical, 4);
        if (adjectives.Count > 0)
        {
            text.Append("NEXT SUMMON: ");
            AppendModifierWords(text, adjectives);
            text.AppendLine();
        }

        List<CombatModifierHint> adverbs = GetAdverbHints(registry, canonical, 5);
        if (adverbs.Count > 0)
        {
            text.Append("ADVERBS: ");
            AppendModifierWords(text, adverbs);
            text.AppendLine();
        }

        text.Append("Mix up verbs and modifiers; repeated words lose strength.");
        return text.ToString();
    }

    public static string BuildSummonHudText(CreatureCombatRegistry registry, string desiredNoun = "")
    {
        if (registry == null)
            return "";

        NounDefinition noun = ResolveSuggestedNoun(registry, desiredNoun);
        if (noun == null)
            return "COMBAT WORDS\nSay a creature noun to summon it.";

        string canonical = CreaturePhraseUtility.NormalizeToken(noun.canonicalNoun);
        List<CombatModifierHint> adjectives = GetAdjectiveHints(registry, canonical, 5);
        var text = new StringBuilder();
        text.Append("SUMMON - ").Append(BuildFamilyLabel(noun)).AppendLine();
        text.Append("Say: ");
        if (adjectives.Count > 0)
            text.Append(adjectives[0].word).Append(' ');
        text.Append(canonical).AppendLine();
        text.Append("ADJECTIVES: ");
        if (adjectives.Count == 0)
        {
            text.Append("none needed");
        }
        else
        {
            for (int i = 0; i < adjectives.Count; i++)
            {
                if (i > 0)
                    text.Append(" | ");
                text.Append(adjectives[i].word).Append(" (").Append(adjectives[i].effect).Append(')');
            }
        }
        text.AppendLine();
        text.Append("Use a different adjective next summon to keep its full effect.");
        return text.ToString();
    }

    public static string GetUseCase(CreatureVerbCategory category)
    {
        switch (NormalizeDisplayCategory(category))
        {
            case CreatureVerbCategory.Attack:
                return "damage an enemy";
            case CreatureVerbCategory.Defense:
                return "block or reduce damage";
            case CreatureVerbCategory.Movement:
                return "move, dodge, or reposition";
            case CreatureVerbCategory.Utility:
                return "boost stats or help tactically";
            default:
                return "take a combat action";
        }
    }

    public static string DescribeAdjectiveEffect(ModifierDefinition modifier)
    {
        if (modifier == null)
            return "balanced";

        float best = 1f;
        string label = "balanced";
        ConsiderBenefit(modifier.maxHpMultiplier, "health", ref best, ref label);
        ConsiderBenefit(modifier.attackMultiplier, "attack", ref best, ref label);
        ConsiderBenefit(modifier.defenseMultiplier, "defense", ref best, ref label);
        ConsiderBenefit(modifier.speedMultiplier, "speed", ref best, ref label);
        ConsiderBenefit(modifier.accuracyMultiplier, "accuracy", ref best, ref label);
        ConsiderBenefit(modifier.evasionMultiplier, "evasion", ref best, ref label);
        return label;
    }

    static string DescribeAdverbEffect(ModifierDefinition modifier)
    {
        if (modifier == null)
            return "balanced";

        float best = 1f;
        string label = "control";
        ConsiderBenefit(modifier.powerMultiplier, "power", ref best, ref label);
        ConsiderBenefit(modifier.defenseMultiplier, "defense", ref best, ref label);
        ConsiderBenefit(modifier.speedMultiplier, "speed", ref best, ref label);
        ConsiderBenefit(modifier.accuracyMultiplier, "accuracy", ref best, ref label);
        ConsiderBenefit(modifier.evasionMultiplier, "evasion", ref best, ref label);
        if (modifier.ppCostMultiplier < 0.95f && best <= 1.05f)
            label = "lower PP";
        return label;
    }

    static void AppendCategory(
        StringBuilder text,
        List<CombatVerbHint> verbs,
        CreatureVerbCategory category,
        string label,
        string useCase)
    {
        text.Append(label).Append(" (").Append(useCase).Append("): ");
        bool appended = false;
        foreach (CombatVerbHint hint in verbs)
        {
            if (hint == null || NormalizeDisplayCategory(hint.category) != NormalizeDisplayCategory(category))
                continue;
            if (appended)
                text.Append(", ");
            text.Append(hint.word);
            appended = true;
        }
        if (!appended)
            text.Append("-");
        text.AppendLine();
    }

    static void AppendModifierWords(StringBuilder text, List<CombatModifierHint> modifiers)
    {
        for (int i = 0; i < modifiers.Count; i++)
        {
            if (i > 0)
                text.Append(", ");
            text.Append(modifiers[i].word);
        }
    }

    static NounDefinition ResolveSuggestedNoun(CreatureCombatRegistry registry, string desiredNoun)
    {
        if (!string.IsNullOrWhiteSpace(desiredNoun) &&
            registry.TryGetNoun(desiredNoun, out NounDefinition requested) &&
            requested != null && requested.IsCreatureNoun)
        {
            return requested;
        }

        foreach (NounDefinition noun in registry.Nouns)
            if (noun != null && noun.IsCreatureNoun)
                return noun;
        return null;
    }

    static string BuildFamilyLabel(NounDefinition noun)
    {
        if (noun == null)
            return "CREATURE";

        string canonical = CreaturePhraseUtility.NormalizeToken(noun.canonicalNoun);
        if (noun.synonyms == null)
            return canonical;
        foreach (string synonym in noun.synonyms)
        {
            string normalized = CreaturePhraseUtility.NormalizeToken(synonym);
            if (!string.IsNullOrEmpty(normalized) && normalized != canonical)
                return $"{canonical} (also {normalized})";
        }
        return canonical;
    }

    static CreatureVerbCategory NormalizeDisplayCategory(CreatureVerbCategory category)
    {
        return category == CreatureVerbCategory.Mobility
            ? CreatureVerbCategory.Movement
            : category;
    }

    static void ConsiderBenefit(float multiplier, string candidate, ref float best, ref string label)
    {
        if (multiplier <= best + 0.001f)
            return;
        best = multiplier;
        label = candidate;
    }
}
