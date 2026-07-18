using System.Collections.Generic;
using UnityEngine;

public partial class CreatureCombatCatalog : ScriptableObject
{
    static List<NounDefinition> BuildDefaultNouns(IReadOnlyList<VerbActionDefinition> verbs, IReadOnlyList<ModifierDefinition> modifiers)
    {
        var nouns = new List<NounDefinition>(
            PronunciationBackedConcreteNouns.Length +
            AuthoredEnemyCreatureFamilyNouns.Length +
            ContextualTerrainNouns.Length);
        for (int i = 0; i < PronunciationBackedConcreteNouns.Length; i++)
            nouns.Add(BuildDefaultNoun(PronunciationBackedConcreteNouns[i], i, BuildTrueAliases(PronunciationBackedConcreteNouns[i]), verbs, modifiers));
        foreach (string enemyFamilyNoun in AuthoredEnemyCreatureFamilyNouns)
            nouns.Add(BuildDefaultNoun(enemyFamilyNoun, nouns.Count, BuildTrueAliases(enemyFamilyNoun), verbs, modifiers));
        foreach (string terrainNoun in ContextualTerrainNouns)
            nouns.Add(BuildContextualTerrainNoun(terrainNoun, modifiers));
        return nouns;
    }

    static NounDefinition BuildContextualTerrainNoun(
        string canonicalNoun,
        IReadOnlyList<ModifierDefinition> modifiers)
    {
        var tags = new List<string> { "object", "terrain", "structure" };
        return new NounDefinition
        {
            canonicalNoun = canonicalNoun,
            nounRole = GrammarNounRole.Object,
            unlockLevel = 1,
            semanticTags = tags,
            baseStats = CreatureStatBlock.Default,
            allowedAdjectives = BuildDefaultAllowedAdjectives(tags, modifiers),
            moveSet = new List<NounMoveSlot>(),
            allowedVerbs = new List<string>(),
        };
    }

    static NounDefinition BuildDefaultNoun(
        string canonicalNoun,
        int index,
        List<string> synonyms,
        IReadOnlyList<VerbActionDefinition> verbs,
        IReadOnlyList<ModifierDefinition> modifiers)
    {
        List<string> tags = BuildSemanticTags(canonicalNoun);
        List<string> adjectivePool = BuildDefaultAllowedAdjectives(tags, modifiers);
        List<NounMoveSlot> moveSlots = BuildDefaultMoveSet(canonicalNoun, tags, verbs);

        CreatureStatBlock stats = new CreatureStatBlock
        {
            maxHp = 5 + index % 5,
            attack = 2 + index % 2,
            defense = 1 + index % 3,
            speed = 3 + index % 6,
            maxPp = Mathf.Max(6, SumMovePp(moveSlots)),
        };

        return new NounDefinition
        {
            canonicalNoun = canonicalNoun,
            synonyms = synonyms ?? new List<string>(),
            nounRole = ResolveNounRole(canonicalNoun, tags),
            unlockLevel = 1,
            semanticTags = tags,
            baseStats = stats,
            allowedAdjectives = adjectivePool,
            moveSet = moveSlots,
            allowedVerbs = BuildLegacyAllowedVerbs(moveSlots),
        };
    }

    static GrammarNounRole ResolveNounRole(string canonicalNoun, IReadOnlyList<string> tags)
    {
        string noun = CreaturePhraseUtility.NormalizeToken(canonicalNoun);
        if (noun == "SHOP" || noun == "SCHOOL" || noun == "PARK")
            return GrammarNounRole.Place;
        if (ContainsTag(tags, "animal") || ContainsTag(tags, "pet") || ContainsTag(tags, "fantasy"))
            return GrammarNounRole.Creature;
        return GrammarNounRole.Object;
    }

    static List<string> BuildSemanticTags(string canonicalNoun)
    {
        string noun = CreaturePhraseUtility.NormalizeToken(canonicalNoun);
        var tags = new List<string>();

        if (noun == "BIRD" || noun == "DUCK" || noun == "OWL" || noun == "QUAIL" || noun == "HEN" || noun == "BAT")
        {
            AddTag(tags, "animal");
            AddTag(tags, "bird");
        }

        if (noun == "CAT" || noun == "DOG" || noun == "PUP" || noun == "PET")
        {
            AddTag(tags, "animal");
            AddTag(tags, "pet");
        }

        if (noun == "FISH" || noun == "DUCK" || noun == "BOAT" || noun == "CRAB")
            AddTag(tags, "aquatic");

        if (noun == "FISH" || noun == "CRAB")
            AddTag(tags, "animal");

        if (noun == "DAD" || noun == "KID" || noun == "KING" || noun == "MAN" || noun == "MEN")
            AddTag(tags, "person");

        if (noun == "APPLE" || noun == "BUN" || noun == "EGG" || noun == "GRAPES" || noun == "JAM" || noun == "GUM" || noun == "NUT")
            AddTag(tags, "food");

        if (noun == "BROOM" || noun == "MOP" || noun == "PEN" || noun == "KEY" || noun == "WATCH" || noun == "XRAY" || noun == "XYLOPHONE")
            AddTag(tags, "tool");

        if (noun == "BOAT" || noun == "BUS" || noun == "CAR" || noun == "VAN")
            AddTag(tags, "vehicle");

        if (noun == "ARM" || noun == "EAR" || noun == "EYE" || noun == "FIN" || noun == "LEG" || noun == "NOSE")
            AddTag(tags, "body-part");

        if (noun == "BAG" || noun == "BIN" || noun == "BOX" || noun == "CAN" || noun == "CUP" || noun == "JAR" ||
            noun == "JUG" || noun == "MUG" || noun == "PAN" || noun == "POT")
            AddTag(tags, "container");

        if (noun == "BELL" || noun == "DRUM" || noun == "XYLOPHONE")
            AddTag(tags, "instrument");

        if (noun == "BOOK")
            AddTag(tags, "book");

        if (noun == "CAP" || noun == "HAT" || noun == "SOCK" || noun == "WIG")
            AddTag(tags, "clothing");

        if (noun == "MOON" || noun == "STAR" || noun == "SUN")
            AddTag(tags, "celestial");

        if (noun == "TREE" || noun == "GRAPES")
            AddTag(tags, "plant");

        if (noun == "MOON" || noun == "RAIN" || noun == "STAR" || noun == "SUN" || noun == "TREE" || noun == "LOG" || noun == "NEST")
            AddTag(tags, "nature");

        if (noun == "UNICORN")
        {
            AddTag(tags, "animal");
            AddTag(tags, "fantasy");
        }

        if (noun == "RAT" || noun == "RABBIT" || noun == "FOX" || noun == "GOAT" || noun == "LION" || noun == "OWL" || noun == "OX" ||
            noun == "PIG" || noun == "TIGER" || noun == "YAK" || noun == "ZEBRA" || noun == "COW" || noun == "ANT")
            AddTag(tags, "animal");

        if (noun == "BALL" || noun == "BOOK" || noun == "BOX" || noun == "DOOR" || noun == "DRUM" || noun == "KITE" ||
            noun == "RING" || noun == "SPOON" || noun == "TOY" || noun == "YOYO")
            AddTag(tags, "object");

        if (noun == "BED" || noun == "BROOM" || noun == "DOOR" || noun == "KEY" || noun == "MAT" || noun == "MOP" ||
            noun == "RUG" || noun == "SPOON" || noun == "WALL")
            AddTag(tags, "household");

        if (tags.Count == 0)
            AddTag(tags, "object");

        return tags;
    }

    static List<string> BuildDefaultAllowedAdjectives(IReadOnlyList<string> tags, IReadOnlyList<ModifierDefinition> modifiers)
    {
        var result = new List<string>();
        foreach (ModifierDefinition modifier in modifiers)
        {
            if (modifier == null || modifier.role != ModifierGrammarRole.Adjective)
                continue;

            if (modifier.allowedNounTags == null || modifier.allowedNounTags.Count == 0)
            {
                result.Add(modifier.modifier);
                continue;
            }

            foreach (string tag in modifier.allowedNounTags)
            {
                if (ContainsTag(tags, tag))
                {
                    result.Add(modifier.modifier);
                    break;
                }
            }
        }

        return result;
    }

    static List<NounMoveSlot> BuildDefaultMoveSet(string canonicalNoun, IReadOnlyList<string> tags, IReadOnlyList<VerbActionDefinition> verbs)
    {
        var result = new List<NounMoveSlot>();
        string noun = CreaturePhraseUtility.NormalizeToken(canonicalNoun);

        if (ContainsTag(tags, "animal") || ContainsTag(tags, "pet") || ContainsTag(tags, "person") || ContainsTag(tags, "fantasy"))
        {
            AddMove(result, "WALK", 4, 1, 0.18f, 0.3f, allowedAdverbs: TagBasedAdverbs("movement"));
            AddMove(result, "RUN", 3, 1, 0.2f, 0.35f, allowedAdverbs: TagBasedAdverbs("movement"));
            AddMove(result, "DODGE", 3, 1, 0.18f, 0.3f, allowedAdverbs: TagBasedAdverbs("movement"));
            AddMove(result, "JUMP", 3, 1, 0.18f, 0.25f, allowedAdverbs: TagBasedAdverbs("movement"));
            AddMove(result, "LOOK", 3, 1, 0.1f, 0.18f, allowedAdverbs: TagBasedAdverbs("utility"));
        }

        if (ContainsTag(tags, "bird"))
        {
            AddMove(result, "FLY", 4, 2, 0.16f, 0.28f, allowedAdverbs: TagBasedAdverbs("movement"));
            AddMove(result, "GLIDE", 3, 1, 0.14f, 0.18f, allowedAdverbs: TagBasedAdverbs("movement"));
            AddMove(result, "LAND", 3, 1, 0.12f, 0.18f, allowedAdverbs: TagBasedAdverbs("movement"));
            AddMove(result, "HOVER", 3, 1, 0.12f, 0.2f, allowedAdverbs: TagBasedAdverbs("movement"));
            AddMove(result, "PECK", 4, 2, 0.2f, 0.32f, nounPowerOffset: 1, allowedAdverbs: TagBasedAdverbs("offense"));
            AddMove(result, "SING", 3, 1, 0.1f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
        }

        if (ContainsTag(tags, "aquatic"))
        {
            AddMove(result, "SWIM", 4, 2, 0.18f, 0.28f, allowedAdverbs: TagBasedAdverbs("movement"));
            AddMove(result, "DRIFT", 3, 1, 0.1f, 0.24f, allowedAdverbs: TagBasedAdverbs("movement"));
            AddMove(result, "DIVE", 3, 1, 0.14f, 0.24f, allowedAdverbs: TagBasedAdverbs("movement"));
            AddMove(result, "SPLASH", 3, 1, 0.12f, 0.24f, allowedAdverbs: TagBasedAdverbs("offense"));
            AddMove(result, "FLOAT", 3, 1, 0.1f, 0.18f, allowedAdverbs: TagBasedAdverbs("movement"));
        }

        if (ContainsTag(tags, "animal") || ContainsTag(tags, "pet"))
        {
            AddMove(result, "ATTACK", 4, 2, 0.22f, 0.32f, nounPowerOffset: 1, allowedAdverbs: TagBasedAdverbs("offense"));
            AddMove(result, "BITE", 4, 2, 0.24f, 0.34f, nounPowerOffset: 1, allowedAdverbs: TagBasedAdverbs("offense"));
            AddMove(result, "SCRATCH", 4, 2, 0.18f, 0.3f, allowedAdverbs: TagBasedAdverbs("offense"));
            AddMove(result, "CHARGE", 3, 1, 0.12f, 0.28f, nounPowerOffset: 1, allowedAdverbs: TagBasedAdverbs("offense"));
        }

        if (noun == "ANT")
        {
            AddMove(result, "STING", 4, 2, 0.22f, 0.38f, allowedAdverbs: TagBasedAdverbs("offense"));
            AddMove(result, "CRAWL", 3, 1, 0.12f, 0.24f, allowedAdverbs: TagBasedAdverbs("movement"));
            AddMove(result, "CLIMB", 3, 1, 0.1f, 0.18f, allowedAdverbs: TagBasedAdverbs("movement"));
            AddMove(result, "DIG", 3, 1, 0.14f, 0.26f, allowedAdverbs: TagBasedAdverbs("utility"));
        }

        if (ContainsTag(tags, "person"))
        {
            AddMove(result, "THROW", 4, 2, 0.18f, 0.22f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "HIDE", 3, 1, 0.14f, 0.2f, allowedAdverbs: TagBasedAdverbs("defense"));
            AddMove(result, "CARRY", 3, 1, 0.1f, 0.2f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "WAVE", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "CALL", 3, 1, 0.08f, 0.18f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "SING", 3, 1, 0.1f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "DANCE", 3, 1, 0.12f, 0.18f, allowedAdverbs: TagBasedAdverbs("movement"));
            AddMove(result, "READ", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "WRITE", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "COUNT", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "POINT", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "SIT", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("defense"));
            AddMove(result, "REST", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("defense"));
            AddMove(result, "EAT", 3, 1, 0.1f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "DRINK", 3, 1, 0.1f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "ASK", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "ANSWER", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "EXPLAIN", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "DESCRIBE", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "LEARN", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("defense"));
            AddMove(result, "THINK", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("defense"));
            AddMove(result, "REMEMBER", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("defense"));
            AddMove(result, "SOLVE", 3, 1, 0.1f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "COMPARE", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "MEASURE", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "OBSERVE", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "NOTICE", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "FIND", 3, 1, 0.1f, 0.18f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "DISCOVER", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "CHOOSE", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("defense"));
            AddMove(result, "DECIDE", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("defense"));
            AddMove(result, "DRAW", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "COLOR", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "HELP", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("defense"));
            AddMove(result, "SHARE", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "TEACH", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
        }

        if (ContainsTag(tags, "tool") || ContainsTag(tags, "object"))
        {
            AddMove(result, "DROP", 3, 1, 0.15f, 0.2f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "SLIDE", 3, 1, 0.1f, 0.18f, allowedAdverbs: TagBasedAdverbs("movement"));
        }

        if (ContainsTag(tags, "tool") || ContainsTag(tags, "household"))
        {
            AddMove(result, "CLEAN", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("defense"));
            AddMove(result, "WIPE", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("defense"));
            AddMove(result, "WASH", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("defense"));
        }

        if (ContainsTag(tags, "container") || ContainsTag(tags, "book"))
        {
            AddMove(result, "STACK", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "SORT", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
        }

        if (ContainsTag(tags, "clothing"))
            AddMove(result, "FOLD", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));

        if (noun == "APPLE" || noun == "BALL" || noun == "BELL" || noun == "BUN" || noun == "DOT" || noun == "EGG" ||
            noun == "MOON" || noun == "NUT" || noun == "RING" || noun == "SUN" || noun == "YOYO")
        {
            AddMove(result, "ROLL", 4, 2, 0.14f, 0.25f, allowedAdverbs: TagBasedAdverbs("movement"));
            AddMove(result, "BOUNCE", 3, 1, 0.12f, 0.2f, allowedAdverbs: TagBasedAdverbs("movement"));
            AddMove(result, "SPIN", 3, 1, 0.1f, 0.22f, allowedAdverbs: TagBasedAdverbs("movement"));
        }

        if (ContainsTag(tags, "vehicle"))
        {
            AddMove(result, "DRIVE", 4, 2, 0.12f, 0.22f, allowedAdverbs: TagBasedAdverbs("movement"));
            AddMove(result, "TURN", 3, 1, 0.1f, 0.16f, allowedAdverbs: TagBasedAdverbs("movement"));
            AddMove(result, "DRIFT", 4, 2, 0.12f, 0.26f, allowedAdverbs: TagBasedAdverbs("movement"));
            AddMove(result, "ROCK", 3, 1, 0.1f, 0.18f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "HONK", 3, 1, 0.1f, 0.18f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "PARK", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("movement"));
        }

        if (ContainsTag(tags, "food"))
        {
            AddMove(result, "ROLL", 3, 1, 0.16f, 0.24f, allowedAdverbs: TagBasedAdverbs("movement"));
            AddMove(result, "DROP", 4, 2, 0.12f, 0.24f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "SLIDE", 3, 1, 0.1f, 0.18f, allowedAdverbs: TagBasedAdverbs("movement"));
        }

        if (ContainsTag(tags, "nature"))
        {
            AddMove(result, "GLOW", 3, 1, 0.12f, 0.26f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "SHAKE", 3, 1, 0.1f, 0.22f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "FALL", 3, 1, 0.14f, 0.22f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "SWAY", 3, 1, 0.1f, 0.18f, allowedAdverbs: TagBasedAdverbs("movement"));
        }

        if (ContainsTag(tags, "fantasy"))
        {
            AddMove(result, "HOVER", 3, 1, 0.12f, 0.2f, allowedAdverbs: TagBasedAdverbs("movement"));
            AddMove(result, "GLITTER", 3, 1, 0.1f, 0.22f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "SHINE", 3, 1, 0.1f, 0.2f, allowedAdverbs: TagBasedAdverbs("utility"));
        }

        if (ContainsTag(tags, "container"))
        {
            AddMove(result, "OPEN", 3, 1, 0.1f, 0.18f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "CLOSE", 3, 1, 0.1f, 0.18f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "FILL", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "EMPTY", 3, 1, 0.1f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "POUR", 3, 1, 0.1f, 0.18f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "SPILL", 3, 1, 0.12f, 0.18f, allowedAdverbs: TagBasedAdverbs("utility"));
        }

        if (ContainsTag(tags, "instrument"))
        {
            AddMove(result, "RING", 3, 1, 0.1f, 0.18f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "PLAY", 3, 1, 0.1f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
        }

        if (ContainsTag(tags, "book"))
        {
            AddMove(result, "OPEN", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "CLOSE", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "READ", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
        }

        if (ContainsTag(tags, "plant"))
        {
            AddMove(result, "GROW", 3, 1, 0.1f, 0.2f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "BLOOM", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
        }

        if (ContainsTag(tags, "celestial"))
        {
            AddMove(result, "SHINE", 3, 1, 0.08f, 0.18f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "GLOW", 3, 1, 0.08f, 0.18f, allowedAdverbs: TagBasedAdverbs("utility"));
        }

        switch (noun)
        {
            case "RABBIT":
                AddMove(result, "HOP", 3, 1, 0.12f, 0.18f, allowedAdverbs: TagBasedAdverbs("movement"));
                break;
            case "CRAB":
                AddMove(result, "CRAWL", 3, 1, 0.12f, 0.24f, allowedAdverbs: TagBasedAdverbs("movement"));
                break;
            case "ARM":
                AddMove(result, "WAVE", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
                AddMove(result, "PUSH", 3, 1, 0.12f, 0.18f, allowedAdverbs: TagBasedAdverbs("offense"));
                AddMove(result, "PULL", 3, 1, 0.1f, 0.18f, allowedAdverbs: TagBasedAdverbs("utility"));
                AddMove(result, "POINT", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
                AddMove(result, "BLOCK", 3, 1, 0.1f, 0.18f, allowedAdverbs: TagBasedAdverbs("defense"));
                break;
            case "EAR":
                AddMove(result, "LISTEN", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
                AddMove(result, "RING", 3, 1, 0.1f, 0.16f, allowedAdverbs: TagBasedAdverbs("offense"));
                break;
            case "EYE":
                AddMove(result, "LOOK", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
                AddMove(result, "BLINK", 3, 1, 0.1f, 0.16f, allowedAdverbs: TagBasedAdverbs("defense"));
                AddMove(result, "GLARE", 3, 1, 0.12f, 0.18f, allowedAdverbs: TagBasedAdverbs("offense"));
                break;
            case "FIN":
                AddMove(result, "SWIM", 3, 1, 0.12f, 0.2f, allowedAdverbs: TagBasedAdverbs("movement"));
                AddMove(result, "SPLASH", 3, 1, 0.12f, 0.2f, allowedAdverbs: TagBasedAdverbs("offense"));
                break;
            case "LEG":
                AddMove(result, "KICK", 3, 1, 0.16f, 0.22f, nounPowerOffset: 1, allowedAdverbs: TagBasedAdverbs("offense"));
                AddMove(result, "HOP", 3, 1, 0.12f, 0.18f, allowedAdverbs: TagBasedAdverbs("movement"));
                AddMove(result, "BLOCK", 3, 1, 0.1f, 0.18f, allowedAdverbs: TagBasedAdverbs("defense"));
                break;
            case "NOSE":
                AddMove(result, "SNIFF", 3, 1, 0.08f, 0.18f, allowedAdverbs: TagBasedAdverbs("utility"));
                AddMove(result, "POINT", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
                AddMove(result, "BUMP", 3, 1, 0.12f, 0.18f, allowedAdverbs: TagBasedAdverbs("offense"));
                break;
            case "KEY":
                AddMove(result, "OPEN", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
                AddMove(result, "TURN", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
                AddMove(result, "POINT", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
                break;
            case "DOOR":
                AddMove(result, "OPEN", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
                AddMove(result, "CLOSE", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
                break;
            case "WATCH":
                AddMove(result, "TICK", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
                break;
            case "KITE":
                AddMove(result, "FLY", 3, 1, 0.1f, 0.16f, allowedAdverbs: TagBasedAdverbs("movement"));
                AddMove(result, "HOVER", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("movement"));
                AddMove(result, "LAND", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("movement"));
                break;
            case "BED":
            case "MAT":
            case "RUG":
            case "QUILT":
                AddMove(result, "REST", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("defense"));
                AddMove(result, "BLOCK", 3, 1, 0.1f, 0.18f, allowedAdverbs: TagBasedAdverbs("defense"));
                break;
            case "BROOM":
            case "MOP":
                AddMove(result, "PUSH", 3, 1, 0.1f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
                AddMove(result, "PULL", 3, 1, 0.1f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
                break;
            case "PEN":
                AddMove(result, "WRITE", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
                AddMove(result, "POINT", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
                AddMove(result, "DRAW", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
                AddMove(result, "COLOR", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
                break;
            case "BELL":
            case "DRUM":
            case "SPOON":
            case "TAP":
            case "XYLOPHONE":
                AddMove(result, "TAP", 3, 1, 0.1f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
                break;
            case "JAM":
            case "GUM":
                AddMove(result, "STICK", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
                AddMove(result, "STIR", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
                break;
            case "ICE":
                AddMove(result, "MELT", 3, 1, 0.1f, 0.18f, allowedAdverbs: TagBasedAdverbs("utility"));
                AddMove(result, "DRIP", 3, 1, 0.1f, 0.18f, allowedAdverbs: TagBasedAdverbs("utility"));
                AddMove(result, "CRACK", 3, 1, 0.14f, 0.2f, allowedAdverbs: TagBasedAdverbs("offense"));
                break;
            case "RAIN":
                AddMove(result, "POUR", 3, 1, 0.12f, 0.2f, allowedAdverbs: TagBasedAdverbs("utility"));
                AddMove(result, "DRIP", 3, 1, 0.12f, 0.2f, allowedAdverbs: TagBasedAdverbs("utility"));
                break;
            case "EGG":
            case "NUT":
                AddMove(result, "CRACK", 3, 1, 0.14f, 0.2f, allowedAdverbs: TagBasedAdverbs("offense"));
                break;
            case "CUP":
            case "JAR":
            case "JUG":
            case "MUG":
            case "PAN":
            case "POT":
                AddMove(result, "POUR", 3, 1, 0.1f, 0.18f, allowedAdverbs: TagBasedAdverbs("utility"));
                AddMove(result, "STIR", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
                break;
            case "UMBRELLA":
            case "WALL":
                AddMove(result, "BLOCK", 3, 1, 0.12f, 0.18f, allowedAdverbs: TagBasedAdverbs("defense"));
                break;
        }

        if (noun == "BAT" || noun == "CAT" || noun == "DOG" || noun == "FOX" || noun == "LION" || noun == "OWL" || noun == "PUP" || noun == "RAT" || noun == "TIGER")
            AddMove(result, "CHASE", 3, 1, 0.18f, 0.26f, allowedAdverbs: TagBasedAdverbs("offense"));

        if (noun == "DOG" || noun == "FOX" || noun == "PIG" || noun == "PUP" || noun == "RAT")
            AddMove(result, "DIG", 3, 1, 0.16f, 0.3f, allowedAdverbs: TagBasedAdverbs("utility"));

        if (noun == "CAT" || noun == "DOG" || noun == "FOX" || noun == "GOAT" || noun == "PIG" || noun == "PUP" || noun == "RAT" || noun == "YAK")
            AddMove(result, "SNIFF", 3, 1, 0.1f, 0.24f, allowedAdverbs: TagBasedAdverbs("utility"));

        if (noun == "CAT" || noun == "DOG" || noun == "KID" || noun == "PET" || noun == "PUP")
        {
            AddMove(result, "NAP", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("defense"));
            AddMove(result, "SLEEP", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("defense"));
            AddMove(result, "SIT", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("defense"));
        }

        if (noun == "COW" || noun == "GOAT" || noun == "OX" || noun == "UNICORN" || noun == "YAK")
            AddMove(result, "GRAZE", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));

        if (noun == "CAT" || noun == "DOG" || noun == "PET" || noun == "PUP")
        {
            AddMove(result, "LICK", 3, 1, 0.08f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "CHEW", 3, 1, 0.1f, 0.18f, allowedAdverbs: TagBasedAdverbs("utility"));
        }

        if (noun == "DAD" || noun == "KID" || noun == "MAN" || noun == "MEN")
        {
            AddMove(result, "EAT", 3, 1, 0.1f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
            AddMove(result, "DRINK", 3, 1, 0.1f, 0.16f, allowedAdverbs: TagBasedAdverbs("utility"));
        }

        AddMove(result, "ATTACK", 3, 1, 0.12f, 0.22f, nounPowerOffset: 1, allowedAdverbs: TagBasedAdverbs("offense"));
        AddMove(result, "RUN", 3, 1, 0.1f, 0.2f, allowedAdverbs: TagBasedAdverbs("movement"));
        AddMove(result, "BLOCK", 3, 1, 0.1f, 0.2f, allowedAdverbs: TagBasedAdverbs("defense"));
        AddMove(result, "HIDE", 3, 1, 0.08f, 0.18f, allowedAdverbs: TagBasedAdverbs("defense"));

        EnsureRequiredVerbCategoryCoverage(result, tags, verbs);

        return DeduplicateMoves(result);
    }
}
