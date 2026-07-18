using System.Collections.Generic;
using UnityEngine;

public partial class CreatureCombatCatalog : ScriptableObject
{
    static void EnsureRequiredVerbCategoryCoverage(List<NounMoveSlot> moves, IReadOnlyList<string> tags, IReadOnlyList<VerbActionDefinition> verbs)
    {
        if (!HasRequiredCategory(moves, verbs, CreatureVerbCategory.Attack))
        {
            EnsureFirstDefinedMove(
                moves,
                verbs,
                GetOffenseFallbackCandidates(tags),
                CreatureVerbCategory.Attack,
                3,
                1,
                0.16f,
                0.26f,
                nounPowerOffset: 1,
                allowedAdverbs: TagBasedAdverbs("offense"));
        }

        if (!HasRequiredCategory(moves, verbs, CreatureVerbCategory.Movement))
        {
            EnsureFirstDefinedMove(
                moves,
                verbs,
                GetMobilityFallbackCandidates(tags),
                CreatureVerbCategory.Movement,
                3,
                1,
                0.12f,
                0.22f,
                allowedAdverbs: TagBasedAdverbs("movement"));
        }

        if (!HasRequiredCategory(moves, verbs, CreatureVerbCategory.Defense))
        {
            EnsureFirstDefinedMove(
                moves,
                verbs,
                GetDefenseFallbackCandidates(tags),
                CreatureVerbCategory.Defense,
                3,
                1,
                0.12f,
                0.22f,
                allowedAdverbs: TagBasedAdverbs("defense"));
        }
    }

    static bool HasRequiredCategory(IReadOnlyList<NounMoveSlot> moves, IReadOnlyList<VerbActionDefinition> verbs, CreatureVerbCategory category)
    {
        if (!CreatureVerbCategoryUtility.IsRequiredCategory(category))
            return false;

        foreach (NounMoveSlot move in moves)
        {
            VerbActionDefinition verb = FindVerbDefinition(verbs, move != null ? move.verbId : "");
            if (move != null && move.ResolveCategory(verb) == category)
                return true;
        }

        return false;
    }

    static void EnsureFirstDefinedMove(
        List<NounMoveSlot> moves,
        IReadOnlyList<VerbActionDefinition> verbs,
        IEnumerable<string> candidates,
        CreatureVerbCategory category,
        int baseMaxPp,
        int minMaxPp,
        float masteryBias,
        float mistakeBias,
        int nounPowerOffset = 0,
        float accuracyOffset = 0f,
        List<string> allowedAdverbs = null)
    {
        foreach (string candidate in candidates)
        {
            if (!ContainsVerb(verbs, candidate))
                continue;
            if (ContainsMoveWithDifferentCategory(moves, verbs, candidate, category))
                continue;

            EnsureMoveIfDefined(
                moves,
                verbs,
                candidate,
                category,
                baseMaxPp,
                minMaxPp,
                masteryBias,
                mistakeBias,
                nounPowerOffset,
                accuracyOffset,
                allowedAdverbs);

            if (ContainsMoveWithCategory(moves, verbs, candidate, category))
                return;
        }
    }

    static VerbActionDefinition FindVerbDefinition(IReadOnlyList<VerbActionDefinition> verbs, string verbId)
    {
        string normalized = CreaturePhraseUtility.NormalizeToken(verbId);
        foreach (VerbActionDefinition verb in verbs)
        {
            if (verb != null && CreaturePhraseUtility.NormalizeToken(verb.verb) == normalized)
                return verb;
        }

        return null;
    }

    static IEnumerable<string> GetOffenseFallbackCandidates(IReadOnlyList<string> tags)
    {
        if (ContainsTag(tags, "bird"))
            yield return "PECK";
        if (ContainsTag(tags, "aquatic"))
            yield return "SPLASH";
        if (ContainsTag(tags, "body-part"))
        {
            yield return "GLARE";
            yield return "BUMP";
            yield return "KICK";
            yield return "RING";
            yield return "PUSH";
            yield return "TAP";
        }
        if (ContainsTag(tags, "animal") || ContainsTag(tags, "pet") || ContainsTag(tags, "fantasy"))
        {
            yield return "CHARGE";
            yield return "BITE";
            yield return "SCRATCH";
        }

        if (ContainsTag(tags, "person"))
        {
            yield return "THROW";
            yield return "PUSH";
        }

        if (ContainsTag(tags, "vehicle"))
            yield return "HONK";
        if (ContainsTag(tags, "instrument"))
            yield return "RING";
        if (ContainsTag(tags, "container"))
        {
            yield return "POUR";
            yield return "EMPTY";
        }

        if (ContainsTag(tags, "food"))
        {
            yield return "DROP";
            yield return "CRACK";
        }

        if (ContainsTag(tags, "nature") || ContainsTag(tags, "plant") || ContainsTag(tags, "celestial"))
        {
            yield return "FALL";
            yield return "DRIP";
        }

        if (ContainsTag(tags, "object") || ContainsTag(tags, "tool") || ContainsTag(tags, "household") ||
            ContainsTag(tags, "clothing") || ContainsTag(tags, "book"))
        {
            yield return "BUMP";
            yield return "PUSH";
            yield return "TAP";
            yield return "DROP";
            yield return "CRACK";
        }

        yield return "BUMP";
        yield return "DROP";
        yield return "TAP";
        yield return "PUSH";
    }

    static IEnumerable<string> GetMobilityFallbackCandidates(IReadOnlyList<string> tags)
    {
        if (ContainsTag(tags, "bird"))
        {
            yield return "FLY";
            yield return "GLIDE";
            yield return "HOVER";
        }

        if (ContainsTag(tags, "aquatic"))
        {
            yield return "SWIM";
            yield return "FLOAT";
            yield return "DRIFT";
        }

        if (ContainsTag(tags, "vehicle"))
        {
            yield return "DRIVE";
            yield return "TURN";
            yield return "PARK";
        }

        if (ContainsTag(tags, "animal") || ContainsTag(tags, "pet") || ContainsTag(tags, "person") || ContainsTag(tags, "fantasy"))
        {
            yield return "DODGE";
            yield return "RUN";
            yield return "JUMP";
            yield return "DANCE";
        }

        if (ContainsTag(tags, "body-part"))
        {
            yield return "SHIFT";
            yield return "BLINK";
            yield return "HOP";
        }

        if (ContainsTag(tags, "nature") || ContainsTag(tags, "plant") || ContainsTag(tags, "celestial"))
        {
            yield return "SWAY";
            yield return "FLOAT";
            yield return "SHAKE";
        }

        if (ContainsTag(tags, "object") || ContainsTag(tags, "tool") || ContainsTag(tags, "container") ||
            ContainsTag(tags, "book") || ContainsTag(tags, "clothing") || ContainsTag(tags, "household") ||
            ContainsTag(tags, "food") || ContainsTag(tags, "instrument"))
        {
            yield return "SHIFT";
            yield return "SLIDE";
            yield return "ROLL";
            yield return "SPIN";
            yield return "HOP";
        }

        yield return "SHIFT";
        yield return "SLIDE";
        yield return "ROLL";
    }

    static IEnumerable<string> GetDefenseFallbackCandidates(IReadOnlyList<string> tags)
    {
        if (ContainsTag(tags, "animal") || ContainsTag(tags, "pet") || ContainsTag(tags, "person") || ContainsTag(tags, "fantasy"))
        {
            yield return "HIDE";
            yield return "LOOK";
            yield return "SNIFF";
            yield return "REST";
        }

        if (ContainsTag(tags, "bird"))
        {
            yield return "SING";
            yield return "LOOK";
            yield return "LAND";
        }

        if (ContainsTag(tags, "aquatic"))
        {
            yield return "SPLASH";
            yield return "FLOAT";
            yield return "DRIFT";
        }

        if (ContainsTag(tags, "vehicle"))
        {
            yield return "ROCK";
            yield return "TURN";
            yield return "PARK";
        }

        if (ContainsTag(tags, "body-part"))
        {
            yield return "BLOCK";
            yield return "LISTEN";
            yield return "SNIFF";
            yield return "BLINK";
        }

        if (ContainsTag(tags, "container") || ContainsTag(tags, "book"))
        {
            yield return "CLOSE";
            yield return "FILL";
            yield return "OPEN";
        }

        if (ContainsTag(tags, "nature") || ContainsTag(tags, "plant") || ContainsTag(tags, "celestial"))
        {
            yield return "GLOW";
            yield return "SHINE";
            yield return "GROW";
        }

        if (ContainsTag(tags, "object") || ContainsTag(tags, "tool") || ContainsTag(tags, "household") ||
            ContainsTag(tags, "clothing") || ContainsTag(tags, "instrument") ||
            ContainsTag(tags, "food"))
        {
            yield return "BLOCK";
            yield return "STICK";
            yield return "PULL";
            yield return "CLOSE";
        }

        yield return "BLOCK";
        yield return "HIDE";
        yield return "STICK";
        yield return "GLOW";
    }

    static void AddMove(
        List<NounMoveSlot> moves,
        string verbId,
        int baseMaxPp,
        int minMaxPp,
        float masteryBias,
        float mistakeBias,
        int nounPowerOffset = 0,
        float accuracyOffset = 0f,
        List<string> allowedAdverbs = null)
    {
        AddMove(
            moves,
            verbId,
            CreatureVerbCategory.Unspecified,
            baseMaxPp,
            minMaxPp,
            masteryBias,
            mistakeBias,
            nounPowerOffset,
            accuracyOffset,
            allowedAdverbs);
    }

    static void AddMove(
        List<NounMoveSlot> moves,
        string verbId,
        CreatureVerbCategory category,
        int baseMaxPp,
        int minMaxPp,
        float masteryBias,
        float mistakeBias,
        int nounPowerOffset = 0,
        float accuracyOffset = 0f,
        List<string> allowedAdverbs = null)
    {
        moves.Add(new NounMoveSlot
        {
            verbId = verbId,
            category = category != CreatureVerbCategory.Unspecified
                ? category
                : CreatureVerbCategoryUtility.InferCategory(null, verbId),
            baseMaxPp = baseMaxPp,
            minMaxPp = minMaxPp,
            masteryBias = masteryBias,
            mistakeBias = mistakeBias,
            nounPowerOffset = nounPowerOffset,
            accuracyOffset = accuracyOffset,
            allowedAdverbs = allowedAdverbs ?? new List<string>(),
        });
    }

    static void EnsureMoveIfDefined(
        List<NounMoveSlot> moves,
        IReadOnlyList<VerbActionDefinition> verbs,
        string verbId,
        int baseMaxPp,
        int minMaxPp,
        float masteryBias,
        float mistakeBias,
        int nounPowerOffset = 0,
        float accuracyOffset = 0f,
        List<string> allowedAdverbs = null)
    {
        EnsureMoveIfDefined(
            moves,
            verbs,
            verbId,
            CreatureVerbCategory.Unspecified,
            baseMaxPp,
            minMaxPp,
            masteryBias,
            mistakeBias,
            nounPowerOffset,
            accuracyOffset,
            allowedAdverbs);
    }

    static void EnsureMoveIfDefined(
        List<NounMoveSlot> moves,
        IReadOnlyList<VerbActionDefinition> verbs,
        string verbId,
        CreatureVerbCategory category,
        int baseMaxPp,
        int minMaxPp,
        float masteryBias,
        float mistakeBias,
        int nounPowerOffset = 0,
        float accuracyOffset = 0f,
        List<string> allowedAdverbs = null)
    {
        if (!ContainsVerb(verbs, verbId))
            return;
        if (ContainsMove(moves, verbId))
            return;

        AddMove(moves, verbId, category, baseMaxPp, minMaxPp, masteryBias, mistakeBias, nounPowerOffset, accuracyOffset, allowedAdverbs);
    }

    static bool ContainsVerb(IReadOnlyList<VerbActionDefinition> verbs, string verbId)
    {
        string normalized = CreaturePhraseUtility.NormalizeToken(verbId);
        foreach (VerbActionDefinition verb in verbs)
            if (verb != null && CreaturePhraseUtility.NormalizeToken(verb.verb) == normalized)
                return true;
        return false;
    }

    static bool ContainsMove(List<NounMoveSlot> moves, string verbId)
    {
        string normalized = CreaturePhraseUtility.NormalizeToken(verbId);
        foreach (NounMoveSlot move in moves)
            if (move != null && move.NormalizedVerbId == normalized)
                return true;
        return false;
    }

    static bool ContainsMoveWithCategory(
        List<NounMoveSlot> moves,
        IReadOnlyList<VerbActionDefinition> verbs,
        string verbId,
        CreatureVerbCategory category)
    {
        string normalized = CreaturePhraseUtility.NormalizeToken(verbId);
        foreach (NounMoveSlot move in moves)
        {
            if (move == null || move.NormalizedVerbId != normalized)
                continue;

            VerbActionDefinition verb = FindVerbDefinition(verbs, move.verbId);
            if (move.ResolveCategory(verb) == category)
                return true;
        }

        return false;
    }

    static bool ContainsMoveWithDifferentCategory(
        List<NounMoveSlot> moves,
        IReadOnlyList<VerbActionDefinition> verbs,
        string verbId,
        CreatureVerbCategory category)
    {
        string normalized = CreaturePhraseUtility.NormalizeToken(verbId);
        foreach (NounMoveSlot move in moves)
        {
            if (move == null || move.NormalizedVerbId != normalized)
                continue;

            VerbActionDefinition verb = FindVerbDefinition(verbs, move.verbId);
            return move.ResolveCategory(verb) != category;
        }

        return false;
    }

    static List<NounMoveSlot> DeduplicateMoves(List<NounMoveSlot> moves)
    {
        var result = new List<NounMoveSlot>();
        var seen = new HashSet<string>();
        foreach (NounMoveSlot move in moves)
        {
            if (move == null || string.IsNullOrWhiteSpace(move.verbId))
                continue;

            string normalized = move.NormalizedVerbId;
            if (!seen.Add(normalized))
                continue;

            result.Add(move);
        }

        return result;
    }

    static int SumMovePp(List<NounMoveSlot> moves)
    {
        int total = 0;
        foreach (NounMoveSlot move in moves)
            total += move != null ? Mathf.Max(1, move.baseMaxPp) : 0;
        return Mathf.Max(1, total);
    }

    static List<string> BuildLegacyAllowedVerbs(List<NounMoveSlot> moveSet)
    {
        var verbs = new List<string>();
        foreach (NounMoveSlot move in moveSet)
            if (move != null && !string.IsNullOrWhiteSpace(move.verbId))
                verbs.Add(move.verbId);
        return verbs;
    }

    static bool ContainsTag(IReadOnlyList<string> tags, string tag)
    {
        string normalized = CreaturePhraseUtility.NormalizeToken(tag);
        if (tags == null)
            return false;

        foreach (string entry in tags)
            if (CreaturePhraseUtility.NormalizeToken(entry) == normalized)
                return true;

        return false;
    }

    static List<string> BuildTrueAliases(string canonicalNoun)
    {
        return canonicalNoun switch
        {
            "ANT" => new List<string> { "INSECT" },
            "APPLE" => new List<string> { "FRUIT" },
            "BAG" => new List<string> { "SACK", "POUCH" },
            "BIN" => new List<string> { "DUSTBIN", "TRASHCAN" },
            "BIRD" => new List<string> { "AVIAN" },
            "BOAT" => new List<string> { "SHIP" },
            "BOOK" => new List<string> { "VOLUME" },
            "BOX" => new List<string> { "CRATE" },
            "BROOM" => new List<string> { "BRUSH" },
            "BUN" => new List<string> { "ROLL" },
            "BUS" => new List<string> { "COACH" },
            "CAN" => new List<string> { "TIN" },
            "CAR" => new List<string> { "AUTO", "AUTOMOBILE" },
            "CAT" => new List<string> { "KITTY", "FELINE", "KITTEN" },
            "COW" => new List<string> { "CATTLE" },
            "CUP" => new List<string> { "GOBLET" },
            "DAD" => new List<string> { "FATHER", "PAPA" },
            "DOG" => new List<string> { "MUTT", "POOCH", "CANINE", "HOUND" },
            "DOOR" => new List<string> { "GATE" },
            "DRUM" => new List<string> { "TOM" },
            "FOX" => new List<string> { "VIXEN" },
            "ICE" => new List<string> { "FROST" },
            "JUG" => new List<string> { "PITCHER" },
            "KID" => new List<string> { "CHILD" },
            "LEG" => new List<string> { "LIMB" },
            "MAN" => new List<string> { "PERSON" },
            "MEN" => new List<string> { "PEOPLE" },
            "MOON" => new List<string> { "LUNA" },
            "MOP" => new List<string> { "SWAB" },
            "MUG" => new List<string> { "CUPFUL" },
            "PAN" => new List<string> { "SKILLET" },
            "PIG" => new List<string> { "HOG", "SWINE" },
            "PUP" => new List<string> { "PUPPY" },
            "QUILT" => new List<string> { "BLANKET" },
            "RAIN" => new List<string> { "SHOWER", "DRIZZLE" },
            // "Rodent" is a biological category, not an unambiguous classroom
            // noun. Keeping only the true lexical alias avoids collapsing the
            // distinct creature families used by battle commands.
            "RAT" => new List<string> { "MOUSE" },
            "RABBIT" => new List<string> { "BUNNY" },
            "RING" => new List<string> { "BAND" },
            "SOCK" => new List<string> { "STOCKING" },
            "SPOON" => new List<string> { "LADLE" },
            "STAR" => new List<string> { "ASTRA" },
            "SUN" => new List<string> { "SUNLIGHT" },
            "TAP" => new List<string> { "FAUCET" },
            "TOY" => new List<string> { "PLAYTHING" },
            "TREE" => new List<string> { "PLANT" },
            "UMBRELLA" => new List<string> { "BROLLY" },
            "VAN" => new List<string> { "MINIVAN" },
            "VASE" => new List<string> { "URN" },
            "WATCH" => new List<string> { "WRISTWATCH", "TIMEPIECE" },
            "WIG" => new List<string> { "HAIRPIECE" },
            "XRAY" => new List<string> { "X-RAY" },
            _ => new List<string>(),
        };
    }
}
