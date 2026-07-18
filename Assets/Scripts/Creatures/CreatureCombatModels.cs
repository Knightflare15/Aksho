using System;
using System.Collections.Generic;
using UnityEngine;

public enum CreaturePhraseKind
{
    None,
    NounSummon,
    VerbCommand,
}

public enum ModifierGrammarRole
{
    Adjective,
    Adverb,
}

public enum CreatureCommandTense
{
    None,
    Bare,
    Present,
    Past,
    Progressive,
}

public enum CreatureVerbCategory
{
    Unspecified,
    Attack,
    Movement,
    Mobility = Movement,
    Defense,
    Utility,
}

[Serializable]
public struct CreatureStatBlock
{
    [Min(1)] public int maxHp;
    [Min(1)] public int attack;
    [Min(1)] public int defense;
    [Min(1)] public int speed;
    [Min(1)] public int maxPp;

    public static CreatureStatBlock Default => new CreatureStatBlock
    {
        maxHp = 5,
        attack = 2,
        defense = 1,
        speed = 5,
        maxPp = 12,
    };

    public CreatureStatBlock Clamp()
    {
        return new CreatureStatBlock
        {
            maxHp = Mathf.Max(1, maxHp),
            attack = Mathf.Max(1, attack),
            defense = Mathf.Max(1, defense),
            speed = Mathf.Max(1, speed),
            maxPp = Mathf.Max(1, maxPp),
        };
    }
}

[Serializable]
public sealed class VerbConjugationRecord
{
    public string bare = "";
    public List<string> pluralPresentForms = new List<string>();
    public List<string> thirdPersonSingularForms = new List<string>();
    public List<string> pastTenseForms = new List<string>();
    public List<string> progressiveForms = new List<string>();

    public IEnumerable<string> EnumerateAllForms()
    {
        if (!string.IsNullOrWhiteSpace(bare))
            yield return bare;

        foreach (string form in pluralPresentForms)
            if (!string.IsNullOrWhiteSpace(form))
                yield return form;

        foreach (string form in thirdPersonSingularForms)
            if (!string.IsNullOrWhiteSpace(form))
                yield return form;

        foreach (string form in pastTenseForms)
            if (!string.IsNullOrWhiteSpace(form))
                yield return form;

        foreach (string form in progressiveForms)
            if (!string.IsNullOrWhiteSpace(form))
                yield return form;
    }
}

[Serializable]
public sealed class NounMoveSlot
{
    public string verbId = "RUN";
    public CreatureVerbCategory category = CreatureVerbCategory.Unspecified;
    [Min(1)] public int baseMaxPp = 3;
    [Min(1)] public int minMaxPp = 1;
    [Min(1)] public int unlockTier = 1;
    [Range(0f, 1.5f)] public float masteryBias = 0.2f;
    [Range(0f, 1.5f)] public float mistakeBias = 0.35f;
    public int nounPowerOffset;
    [Range(-0.5f, 0.5f)] public float accuracyOffset;
    public List<string> allowedAdverbs = new List<string>();
    public List<string> bannedAdverbs = new List<string>();

    public string NormalizedVerbId => CreaturePhraseUtility.NormalizeToken(verbId);

    public CreatureVerbCategory ResolveCategory(VerbActionDefinition verbDefinition)
    {
        return category != CreatureVerbCategory.Unspecified
            ? category
            : CreatureVerbCategoryUtility.InferCategory(verbDefinition, verbId);
    }

    public bool MatchesVerb(string verb)
    {
        return NormalizedVerbId == CreaturePhraseUtility.NormalizeToken(verb);
    }

    public bool AllowsAdverb(string adverb)
    {
        string normalized = CreaturePhraseUtility.NormalizeToken(adverb);
        if (string.IsNullOrEmpty(normalized))
            return true;

        if (bannedAdverbs != null)
        {
            foreach (string banned in bannedAdverbs)
                if (CreaturePhraseUtility.NormalizeToken(banned) == normalized)
                    return false;
        }

        if (allowedAdverbs == null || allowedAdverbs.Count == 0)
            return true;

        foreach (string allowed in allowedAdverbs)
            if (CreaturePhraseUtility.NormalizeToken(allowed) == normalized)
                return true;

        return false;
    }
}

[Serializable]
public class NounDefinition
{
    public string canonicalNoun = "RAT";
    public List<string> synonyms = new List<string>();
    public GrammarNounRole nounRole = GrammarNounRole.Creature;
    [Min(1)] public int unlockLevel = 1;
    public List<string> semanticTags = new List<string>();
    public CreatureStatBlock baseStats = CreatureStatBlock.Default;
    public List<string> allowedAdjectives = new List<string>();
    public List<NounMoveSlot> moveSet = new List<NounMoveSlot>();
    public List<string> allowedVerbs = new List<string> { "SCRATCH", "RUN", "JUMP" };
    [Tooltip("Optional creature prefab for this noun family. Leave empty to spawn the default placeholder cube.")]
    public SummonedCreatureActor prefabOverride;
    public Sprite journalPhoto;
    public AudioClip pronunciationClip;

    public bool Matches(string value)
    {
        string normalized = CreaturePhraseUtility.NormalizeToken(value);
        if (string.IsNullOrEmpty(normalized))
            return false;
        if (normalized == CreaturePhraseUtility.NormalizeToken(canonicalNoun))
            return true;
        if (synonyms == null)
            return false;
        foreach (string synonym in synonyms)
            if (normalized == CreaturePhraseUtility.NormalizeToken(synonym))
                return true;
        return false;
    }

    public IEnumerable<string> AcceptedForms()
    {
        if (!string.IsNullOrWhiteSpace(canonicalNoun))
            yield return canonicalNoun;
        if (synonyms == null)
            yield break;
        foreach (string synonym in synonyms)
            if (!string.IsNullOrWhiteSpace(synonym))
                yield return synonym;
    }

    public IEnumerable<string> EnumerateVerbIds()
    {
        if (moveSet != null)
        {
            foreach (NounMoveSlot slot in moveSet)
                if (slot != null && !string.IsNullOrWhiteSpace(slot.verbId))
                    yield return slot.verbId;
        }

        if (allowedVerbs == null)
            yield break;

        foreach (string verb in allowedVerbs)
            if (!string.IsNullOrWhiteSpace(verb))
                yield return verb;
    }

    public bool HasSemanticTag(string tag)
    {
        string normalized = CreaturePhraseUtility.NormalizeToken(tag);
        if (string.IsNullOrEmpty(normalized) || semanticTags == null)
            return false;

        foreach (string entry in semanticTags)
            if (CreaturePhraseUtility.NormalizeToken(entry) == normalized)
                return true;

        return false;
    }

    public bool IsCreatureNoun => nounRole == GrammarNounRole.Creature || HasSemanticTag("animal") || HasSemanticTag("pet") || HasSemanticTag("fantasy");

    public bool AllowsAdjective(string adjective)
    {
        string normalized = CreaturePhraseUtility.NormalizeToken(adjective);
        if (string.IsNullOrEmpty(normalized))
            return true;
        if (allowedAdjectives == null || allowedAdjectives.Count == 0)
            return true;

        foreach (string allowed in allowedAdjectives)
            if (CreaturePhraseUtility.NormalizeToken(allowed) == normalized)
                return true;

        return false;
    }

    public NounMoveSlot ResolveMoveSlot(string verb)
    {
        string normalized = CreaturePhraseUtility.NormalizeToken(verb);
        if (string.IsNullOrEmpty(normalized) || moveSet == null)
            return null;

        foreach (NounMoveSlot slot in moveSet)
            if (slot != null && slot.MatchesVerb(normalized))
                return slot;

        return null;
    }

    public bool AllowsVerb(string verb)
    {
        string normalized = CreaturePhraseUtility.NormalizeToken(verb);
        if (string.IsNullOrEmpty(normalized))
            return false;

        if (ResolveMoveSlot(normalized) != null)
            return true;

        if (allowedVerbs == null || allowedVerbs.Count == 0)
            return moveSet == null || moveSet.Count == 0;

        foreach (string allowed in allowedVerbs)
            if (CreaturePhraseUtility.NormalizeToken(allowed) == normalized)
                return true;
        return false;
    }

    public bool AllowsAdverb(string verb, string adverb)
    {
        string normalizedAdverb = CreaturePhraseUtility.NormalizeToken(adverb);
        if (string.IsNullOrEmpty(normalizedAdverb))
            return true;

        NounMoveSlot slot = ResolveMoveSlot(verb);
        if (slot == null)
            return true;

        return slot.AllowsAdverb(normalizedAdverb);
    }

    public bool HasRequiredVerbCategory(CreatureVerbCategory category, IReadOnlyList<VerbActionDefinition> verbs)
    {
        if (!CreatureVerbCategoryUtility.IsRequiredCategory(category) || moveSet == null)
            return false;

        foreach (NounMoveSlot slot in moveSet)
        {
            if (slot == null)
                continue;

            VerbActionDefinition verb = FindVerbDefinition(verbs, slot.verbId);
            if (slot.ResolveCategory(verb) == category)
                return true;
        }

        return false;
    }

    public bool HasMinimumRequiredVerbCategories(IReadOnlyList<VerbActionDefinition> verbs)
    {
        return HasRequiredVerbCategory(CreatureVerbCategory.Attack, verbs) &&
               HasRequiredVerbCategory(CreatureVerbCategory.Movement, verbs) &&
               HasRequiredVerbCategory(CreatureVerbCategory.Defense, verbs);
    }

    static VerbActionDefinition FindVerbDefinition(IReadOnlyList<VerbActionDefinition> verbs, string verbId)
    {
        string normalized = CreaturePhraseUtility.NormalizeToken(verbId);
        if (string.IsNullOrEmpty(normalized) || verbs == null)
            return null;

        foreach (VerbActionDefinition verb in verbs)
            if (verb != null && CreaturePhraseUtility.NormalizeToken(verb.verb) == normalized)
                return verb;

        return null;
    }
}

[Serializable]
public class VerbActionDefinition
{
    public string verb = "SCRATCH";
    public List<string> aliases = new List<string>();
    public List<string> verbTags = new List<string>();
    public List<string> allowedAdverbs = new List<string>();
    public VerbConjugationRecord conjugation = new VerbConjugationRecord();
    public List<string> thirdPersonSingularForms = new List<string>();
    public List<string> pastTenseForms = new List<string>();
    public List<string> progressiveForms = new List<string>();
    public BattleActionRole role = BattleActionRole.Attack;
    [Min(1)] public int ppCost = 2;
    [Min(0)] public int power = 2;
    [Range(0.05f, 1f)] public float accuracy = 0.9f;
    [Min(0.1f)] public float range = 3.5f;
    [Header("Tactical Grid")]
    [Min(1)] public int tacticalRangeCells = 1;
    [Min(0)] public int tacticalMovementCells;
    [Range(0.1f, 1f)] public float tacticalDamageMultiplier = 1f;
    [Min(0f)] public float cooldownSeconds = 0.5f;
    public bool movementVerb;
    public string movementProfile = "advance";
    public string animationTrigger = "Attack";
    public GameObject effectPrefab;

    public bool Matches(string value)
    {
        string normalized = CreaturePhraseUtility.NormalizeToken(value);
        if (string.IsNullOrEmpty(normalized))
            return false;
        if (normalized == CreaturePhraseUtility.NormalizeToken(verb))
            return true;
        if (aliases == null)
            return false;
        foreach (string alias in aliases)
            if (normalized == CreaturePhraseUtility.NormalizeToken(alias))
                return true;
        return false;
    }

    public bool HasTag(string tag)
    {
        string normalized = CreaturePhraseUtility.NormalizeToken(tag);
        if (string.IsNullOrEmpty(normalized) || verbTags == null)
            return false;

        foreach (string entry in verbTags)
            if (CreaturePhraseUtility.NormalizeToken(entry) == normalized)
                return true;

        return false;
    }

    public bool AllowsAdverb(string adverb)
    {
        string normalized = CreaturePhraseUtility.NormalizeToken(adverb);
        if (string.IsNullOrEmpty(normalized))
            return true;
        if (allowedAdverbs == null || allowedAdverbs.Count == 0)
            return true;

        foreach (string allowed in allowedAdverbs)
            if (CreaturePhraseUtility.NormalizeToken(allowed) == normalized)
                return true;

        return false;
    }

    public IEnumerable<string> GetThirdPersonSingularForms()
    {
        if (conjugation != null && conjugation.thirdPersonSingularForms != null)
        {
            foreach (string form in conjugation.thirdPersonSingularForms)
                if (!string.IsNullOrWhiteSpace(form))
                    yield return form;
        }

        if (thirdPersonSingularForms == null)
            yield break;

        foreach (string form in thirdPersonSingularForms)
            if (!string.IsNullOrWhiteSpace(form))
                yield return form;
    }

    public IEnumerable<string> GetPluralPresentForms()
    {
        if (conjugation != null && conjugation.pluralPresentForms != null)
        {
            foreach (string form in conjugation.pluralPresentForms)
                if (!string.IsNullOrWhiteSpace(form))
                    yield return form;
        }

        if (!string.IsNullOrWhiteSpace(verb))
            yield return verb;

        if (aliases == null)
            yield break;

        foreach (string alias in aliases)
            if (!string.IsNullOrWhiteSpace(alias))
                yield return alias;
    }

    public IEnumerable<string> GetPastTenseForms()
    {
        if (conjugation != null && conjugation.pastTenseForms != null)
        {
            foreach (string form in conjugation.pastTenseForms)
                if (!string.IsNullOrWhiteSpace(form))
                    yield return form;
        }

        if (pastTenseForms == null)
            yield break;

        foreach (string form in pastTenseForms)
            if (!string.IsNullOrWhiteSpace(form))
                yield return form;
    }

    public IEnumerable<string> GetProgressiveForms()
    {
        if (conjugation != null && conjugation.progressiveForms != null)
        {
            foreach (string form in conjugation.progressiveForms)
                if (!string.IsNullOrWhiteSpace(form))
                    yield return form;
        }

        if (progressiveForms == null)
            yield break;

        foreach (string form in progressiveForms)
            if (!string.IsNullOrWhiteSpace(form))
                yield return form;
    }

    public IEnumerable<string> EnumerateAllCommandForms()
    {
        foreach (string form in GetPluralPresentForms())
            yield return form;
        foreach (string form in GetThirdPersonSingularForms())
            yield return form;
        foreach (string form in GetPastTenseForms())
            yield return form;
        foreach (string form in GetProgressiveForms())
            yield return form;
    }
}

[Serializable]
public class ModifierDefinition
{
    public string modifier = "BIG";
    public ModifierGrammarRole role = ModifierGrammarRole.Adjective;
    public List<string> aliases = new List<string>();
    public List<string> semanticTags = new List<string>();
    public List<string> allowedNounTags = new List<string>();
    public List<string> allowedVerbTags = new List<string>();
    [Min(0.05f)] public float maxHpMultiplier = 1f;
    [Min(0.05f)] public float attackMultiplier = 1f;
    [Min(0.05f)] public float defenseMultiplier = 1f;
    [Min(0.05f)] public float speedMultiplier = 1f;
    [Min(0.05f)] public float powerMultiplier = 1f;
    [Min(0.05f)] public float accuracyMultiplier = 1f;
    [Min(0.05f)] public float evasionMultiplier = 1f;
    [Min(0.05f)] public float ppCostMultiplier = 1f;

    public bool Matches(string value)
    {
        string normalized = CreaturePhraseUtility.NormalizeToken(value);
        if (string.IsNullOrEmpty(normalized))
            return false;
        if (normalized == CreaturePhraseUtility.NormalizeToken(modifier))
            return true;
        if (aliases == null)
            return false;
        foreach (string alias in aliases)
            if (normalized == CreaturePhraseUtility.NormalizeToken(alias))
                return true;
        return false;
    }

    public bool AllowsForNoun(NounDefinition noun)
    {
        if (noun == null || allowedNounTags == null || allowedNounTags.Count == 0)
            return true;

        foreach (string tag in allowedNounTags)
            if (noun.HasSemanticTag(tag))
                return true;

        return false;
    }

    public bool AllowsForVerb(VerbActionDefinition verbDefinition)
    {
        if (verbDefinition == null || allowedVerbTags == null || allowedVerbTags.Count == 0)
            return true;

        foreach (string tag in allowedVerbTags)
            if (verbDefinition.HasTag(tag))
                return true;

        return false;
    }
}

public struct CreaturePhraseParseResult
{
    public CreaturePhraseKind kind;
    public GrammarPhrasePattern pattern;
    public NounDefinition noun;
    public VerbActionDefinition verb;
    public ModifierDefinition modifier;
    public string subject;
    public string matchedVerbForm;
    public string canonicalText;
    public string originalText;
    public CreatureGrammarCommand command;
}

public sealed class CreatureGrammarCommand
{
    public string subject = "";
    public string determiner = "";
    public string pronoun = "";
    public string noun = "";
    public string verb = "";
    public string adverb = "";
    public string directObject = "";
    public string preposition = "";
    public string conjunction = "";
    public string tenseObject = "";
    public string canonicalText = "";
    public CreatureCommandTense tense = CreatureCommandTense.None;
    public string[] adjectives = Array.Empty<string>();
}

public static class CreaturePhraseUtility
{
    public static string NormalizeToken(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "" : value.Trim().ToUpperInvariant();
    }

    public static List<string> Tokenize(string phrase)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(phrase))
            return result;

        string[] rawTokens = phrase.Split(new[] { ' ', '\t', '\r', '\n', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string token in rawTokens)
        {
            string normalized = NormalizeToken(token.Trim(',', '.', '!', '?', ':', ';', '"', '\''));
            if (!string.IsNullOrEmpty(normalized))
                result.Add(normalized);
        }
        return result;
    }
}

public static class CreatureVerbCategoryUtility
{
    public static bool IsRequiredCategory(CreatureVerbCategory category)
    {
        return category == CreatureVerbCategory.Attack ||
               category == CreatureVerbCategory.Movement ||
               category == CreatureVerbCategory.Defense;
    }

    public static CreatureVerbCategory InferCategory(VerbActionDefinition verbDefinition, string fallbackVerbId = "")
    {
        if (verbDefinition != null)
        {
            if (verbDefinition.role == BattleActionRole.Offense)
                return CreatureVerbCategory.Attack;
            if (verbDefinition.role == BattleActionRole.Utility ||
                (verbDefinition.HasTag("UTILITY") && !verbDefinition.movementVerb && verbDefinition.power <= 0))
                return CreatureVerbCategory.Utility;
            if (verbDefinition.role == BattleActionRole.Defense)
                return CreatureVerbCategory.Defense;
            if (verbDefinition.movementVerb || verbDefinition.role == BattleActionRole.Dodge)
                return CreatureVerbCategory.Movement;
            return CreatureVerbCategory.Defense;
        }

        switch (CreaturePhraseUtility.NormalizeToken(fallbackVerbId))
        {
            case "ATTACK":
            case "BITE":
            case "SCRATCH":
            case "PECK":
            case "DROP":
            case "CHARGE":
            case "THROW":
            case "STING":
            case "DIG":
            case "CHASE":
            case "SPLASH":
            case "FALL":
            case "GLARE":
            case "KICK":
            case "POUR":
            case "EMPTY":
            case "SPILL":
            case "MELT":
            case "DRIP":
            case "CRACK":
            case "HONK":
            case "TAP":
            case "CHEW":
            case "BUMP":
            case "PUSH":
            case "RING":
                return CreatureVerbCategory.Attack;

            case "RUN":
            case "WALK":
            case "DODGE":
            case "JUMP":
            case "FLY":
            case "SWIM":
            case "ROLL":
            case "BOUNCE":
            case "DRIFT":
            case "DANCE":
            case "CLIMB":
            case "CRAWL":
            case "GLIDE":
            case "DIVE":
            case "FLOAT":
            case "DRIVE":
            case "TURN":
            case "SPIN":
            case "SLIDE":
            case "SWAY":
            case "BLINK":
            case "HOP":
            case "LAND":
            case "HOVER":
            case "PARK":
            case "SHIFT":
                return CreatureVerbCategory.Movement;

            case "ANSWER":
            case "ASK":
            case "BLOOM":
            case "CALL":
            case "CARRY":
            case "CLOSE":
            case "COLOR":
            case "COMPARE":
            case "COUNT":
            case "DESCRIBE":
            case "DISCOVER":
            case "DRAW":
            case "DRINK":
            case "EAT":
            case "EXPLAIN":
            case "FILL":
            case "FIND":
            case "GLITTER":
            case "GLOW":
            case "GROW":
            case "LEARN":
            case "LOOK":
            case "MEASURE":
            case "NOTICE":
            case "OBSERVE":
            case "OPEN":
            case "PLAY":
            case "POINT":
            case "READ":
            case "REMEMBER":
            case "SHARE":
            case "SHINE":
            case "SING":
            case "SOLVE":
            case "SORT":
            case "STACK":
            case "TEACH":
            case "THINK":
            case "WAVE":
            case "WRITE":
                return CreatureVerbCategory.Utility;

            default:
                return CreatureVerbCategory.Defense;
        }
    }
}
