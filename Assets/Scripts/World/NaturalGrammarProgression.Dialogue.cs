using System;
using System.Collections.Generic;
using UnityEngine;


public static partial class NaturalGrammarProgression
{
    public static LocalizedDialogueLine BuildGeneratedDialogue(
        SemanticZoneKind zoneKind,
        string grammarTopic,
        int tier,
        int npcIndex,
        bool trainerBattle)
    {
        NaturalGrammarRegion region = Resolve(grammarTopic, tier);
        string taskId = ResolveDialogueTaskId(region, zoneKind, npcIndex);
        if (!string.IsNullOrWhiteSpace(taskId) && FirstSliceDialogueTasks.TryGetValue(taskId, out GrammarDialogueTaskDefinition task))
            return BuildTaskLine(region, zoneKind, task, npcIndex);

        string lineId = $"{region.id}-{zoneKind.ToString().ToLowerInvariant()}-{npcIndex}";
        string sourceText;
        string expectedResponse;

        if (zoneKind == SemanticZoneKind.Route)
        {
            if (region.encounterMode == GrammarEncounterMode.TacticalCommand && trainerBattle)
            {
                sourceText = region.tier switch
                {
                    5 => "Creatures listen for nouns. Say rat, cat, dog, duck, or owl to send one out.",
                    6 => "A verb is an action. Summon a creature, then say bite, run, jump, or scratch.",
                    7 => "Articles matter in battle now. Try a rat or an owl.",
                    8 => "Pronouns can stand in for nouns. A curse might make you command with I, he, or they.",
                    10 => "Adjectives change a summon. Try big rat or small cat, then notice the tradeoff.",
                    _ => $"Practice battle. Use {region.focus}.",
                };
                expectedResponse = "I am ready";
            }
            else
            {
                sourceText = region.tier switch
                {
                    1 => "Walk the road and greet people. No battles yet. Listen, answer, and get comfortable.",
                    2 => "A wild noun may appear, but you only need its first letter. Say or write the target letter.",
                    3 => "A wild noun may appear, but the check is still letters. Listen for vowels and consonants.",
                    4 => "Clean up sentence starts and endings on the road.",
                    5 => "A wild noun may appear. Say or write the noun itself to answer the encounter.",
                    _ => $"This route reviews {region.focus}.",
                };
                expectedResponse = "I understand";
            }
        }
        else if (zoneKind == SemanticZoneKind.Gym)
        {
            sourceText = region.encounterMode == GrammarEncounterMode.TacticalCommand
                ? $"Show mastery of {region.displayName}. Use {region.focus} without full Buddy help."
                : $"Show what you learned in {region.displayName}. Speak clearly and answer the guide.";
            expectedResponse = "I am ready";
        }
        else
        {
            sourceText = region.tier switch
            {
                1 => npcIndex % 3 == 0
                    ? "Hello. Welcome to the village. You can answer: Hello."
                    : npcIndex % 3 == 1
                        ? "When someone helps you, say thank you. Try it with me."
                        : "Tell people your name with: My name is Aryan.",
                2 => npcIndex % 3 == 0
                    ? "Capital letters and small letters are two shapes of the same letter."
                    : npcIndex % 3 == 1
                        ? "The alphabet stays in order. After A comes B."
                        : "Watch the letter form carefully when you answer.",
                3 => npcIndex % 3 == 0
                    ? "English has five vowels: a, e, i, o, and u."
                    : npcIndex % 3 == 1
                        ? "The other letters here are consonants."
                        : "Listen to the letter and decide what kind it is.",
                4 => npcIndex % 3 == 0
                    ? "A sentence starts with a capital letter."
                    : npcIndex % 3 == 1
                        ? "A sentence ends with a full stop."
                        : "The start and end help the reader see one complete thought.",
                5 => npcIndex % 3 == 0
                    ? "A noun names a person, animal, place, or thing. Cat is a noun. Shop is a noun."
                    : npcIndex % 3 == 1
                        ? "In battle, nouns become creatures. Say rat, cat, dog, duck, or owl."
                        : "A naming word tells us who or what we mean.",
                6 => npcIndex % 3 == 0
                    ? "A verb is an action. Bite, run, jump, and scratch are verbs."
                    : npcIndex % 3 == 1
                        ? "Summon a noun first. Then use a verb to act."
                        : "Some nouns fit some verbs better than others.",
                7 => npcIndex % 3 == 0
                    ? "Articles come before nouns. Use a before a consonant sound."
                    : npcIndex % 3 == 1
                        ? "Use an before a vowel sound, like an owl."
                        : "Use the when you mean a specific noun.",
                8 => npcIndex % 3 == 0
                    ? "Pronouns can replace nouns. I, you, he, she, it, we, and they are personal pronouns."
                    : npcIndex % 3 == 1
                        ? "A curse can force your battle voice. If it says I, answer with I bite."
                        : "He, she, and it need the action to change: he bites, she runs, it jumps.",
                9 => npcIndex % 3 == 0
                    ? "One rat is singular. Many rats are plural."
                    : npcIndex % 3 == 1
                        ? "Some plurals add es, like boxes."
                        : "Some plurals change y to ies, like puppies.",
                10 => npcIndex % 3 == 0
                    ? "An adjective describes a noun. Big rat and small cat summon different builds."
                    : npcIndex % 3 == 1
                        ? "Adjectives trade stats. Big hits harder but moves slower."
                        : "Use one adjective at summon time. Then battle with verbs.",
                11 => npcIndex % 3 == 0
                    ? "Prepositions show place. In, on, under, and behind change the meaning."
                    : npcIndex % 3 == 1
                        ? "The rat is in the box is different from the rat is on the box."
                        : "Choose the location word that matches what you mean.",
                _ => $"This town studies {region.focus}. Buddy can explain it in your language.",
            };
            expectedResponse = region.tier switch
            {
                1 when npcIndex % 3 == 0 => "Hello",
                1 when npcIndex % 3 == 1 => "Thank you",
                1 => "My name is Aryan",
                2 when npcIndex % 3 == 0 => "A",
                2 when npcIndex % 3 == 1 => "B",
                2 => "C",
                3 when npcIndex % 3 == 0 => "A",
                3 when npcIndex % 3 == 1 => "B",
                3 => "O",
                4 when npcIndex % 3 == 0 => "I am ready.",
                4 when npcIndex % 3 == 1 => "We play.",
                4 => "I play.",
                5 => "Rat",
                6 => "Bite",
                7 => "A rat",
                8 => "I bite",
                9 => "Rats",
                10 => "Big rat",
                11 => "Rat is in the box.",
                _ => "I understand",
            };
        }

        return new LocalizedDialogueLine
        {
            lineId = lineId,
            regionId = region.id,
            grammarTopic = region.grammarTopic,
            zoneKind = zoneKind,
            contextCue = BuildDialogueContextCue(region, zoneKind, npcIndex),
            conceptId = region.conceptId,
            subskillId = $"{region.id}_{zoneKind.ToString().ToLowerInvariant()}",
            sourceText = sourceText,
            sourceLanguage = "en",
            expectedEnglishResponse = expectedResponse,
            acceptedEnglishResponses = new List<string> { expectedResponse },
            grammarFocusWords = DialogueFillInBlankScaffold.InferFocusWords(
                expectedResponse,
                region.conceptId,
                ResolvePrimaryPattern(region)),
            overrideAssistMode = true,
            assistMode = zoneKind == SemanticZoneKind.Gym
                ? TranslatorAssistMode.Off
                : zoneKind == SemanticZoneKind.Route
                    ? TranslatorAssistMode.Partial
                    : TranslatorAssistMode.Full,
            inputMode = zoneKind == SemanticZoneKind.Town
                ? GrammarDialogueInputMode.SpeakOnly
                : GrammarDialogueInputMode.SpeakOrWrite,
            malfunctionType = zoneKind == SemanticZoneKind.Route
                ? GrammarDialogueMalfunctionType.PartialTranscript
                : GrammarDialogueMalfunctionType.None,
            grammarPattern = ResolvePrimaryPattern(region),
            scaffoldMode = ResolveScaffoldMode(
                zoneKind == SemanticZoneKind.Gym
                    ? TranslatorAssistMode.Off
                    : zoneKind == SemanticZoneKind.Route
                        ? TranslatorAssistMode.Partial
                        : TranslatorAssistMode.Full,
                zoneKind == SemanticZoneKind.Route
                    ? GrammarDialogueMalfunctionType.PartialTranscript
                    : GrammarDialogueMalfunctionType.None),
            buddyUseCase = ResolveBuddyUseCase(
                zoneKind == SemanticZoneKind.Gym
                    ? TranslatorAssistMode.Off
                    : zoneKind == SemanticZoneKind.Route
                        ? TranslatorAssistMode.Partial
                        : TranslatorAssistMode.Full),
            allowAiHint = zoneKind != SemanticZoneKind.Gym,
            openGrimoireOnWrongAnswer = true,
            teachingNote = BuildTeachingNote(region),
            localLanguageHint = zoneKind == SemanticZoneKind.Town ? "Buddy can translate and model the answer." : "",
        };
    }

    static GrammarPhrasePattern ResolvePrimaryPattern(NaturalGrammarRegion region)
    {
        if (region == null || region.unlockedPhrasePatterns == null || region.unlockedPhrasePatterns.Length == 0)
            return GrammarPhrasePattern.LetterOnly;
        return region.unlockedPhrasePatterns[region.unlockedPhrasePatterns.Length - 1];
    }

    static string BuildTeachingNote(NaturalGrammarRegion region)
    {
        if (region == null)
            return "";

        return region.conceptId switch
        {
            GrammarConceptId.Greetings => "Use the whole greeting phrase clearly.",
            GrammarConceptId.Alphabet => "Watch the letter form and order.",
            GrammarConceptId.VowelsConsonants => "Check whether the letter is a vowel or a consonant.",
            GrammarConceptId.SentenceStartEnd => "Start the sentence with a capital and finish it cleanly.",
            GrammarConceptId.BasicNouns => "Nouns are naming words.",
            GrammarConceptId.BasicVerbs => "Verbs are action words.",
            GrammarConceptId.Articles => "Use the article that matches the noun sound and meaning.",
            GrammarConceptId.Pronouns => "Use the pronoun that replaces the noun correctly.",
            GrammarConceptId.Plurals => "Show when there is more than one.",
            GrammarConceptId.Adjectives => "Put the describing word with the noun.",
            GrammarConceptId.BasicPrepositions => "Choose the location word that matches the picture.",
            _ => "",
        };
    }

    static string ResolveDialogueTaskId(NaturalGrammarRegion region, SemanticZoneKind zoneKind, int npcIndex)
    {
        if (region == null)
            return "";

        string[] ids = GetDialogueTaskIds(region, zoneKind);
        if (ids == null || ids.Length == 0)
            return "";
        return ids[Mathf.Abs(npcIndex) % ids.Length];
    }

    static string PickNpcName(string[] names, string fallback, int npcIndex)
    {
        if (names != null && names.Length > 0)
        {
            string value = names[Mathf.Abs(npcIndex) % names.Length];
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return fallback;
    }

    static string[] GetDialogueTaskIds(NaturalGrammarRegion region, SemanticZoneKind zoneKind)
    {
        if (region == null)
            return Array.Empty<string>();

        return zoneKind switch
        {
            SemanticZoneKind.Town => region.npcLessonIds ?? Array.Empty<string>(),
            SemanticZoneKind.Route => region.routePracticeIds ?? Array.Empty<string>(),
            SemanticZoneKind.Gym => region.gymCheckIds ?? Array.Empty<string>(),
            _ => Array.Empty<string>(),
        };
    }

    static GrammarBattleCurse SelectRegionCurse(NaturalGrammarRegion region, SemanticZoneKind zoneKind)
    {
        if (region == null || region.newCurses == null || region.newCurses.Length == 0)
            return GrammarBattleCurse.None;

        int offset = zoneKind == SemanticZoneKind.Gym ? region.newCurses.Length - 1 : 0;
        for (int i = 0; i < region.newCurses.Length; i++)
        {
            GrammarBattleCurse curse = region.newCurses[(offset + i) % region.newCurses.Length];
            if (curse != GrammarBattleCurse.None)
                return curse;
        }

        return GrammarBattleCurse.None;
    }

    static string ResolveEnemyNounFamily(NaturalGrammarRegion region, string enemyNounFamily)
    {
        string noun = CreaturePhraseUtility.NormalizeToken(enemyNounFamily);
        if (!string.IsNullOrEmpty(noun))
            return noun;

        if (region != null && region.currentNounFamilies != null && region.currentNounFamilies.Length > 0)
            return CreaturePhraseUtility.NormalizeToken(region.currentNounFamilies[0]);

        return "RAT";
    }

    static VerbActionDefinition ResolveEnemyVerb(string nounFamily, BattleActionRole role)
    {
        CreatureCombatCatalog catalog = CreatureCombatCatalog.CreateRuntimeDefault();
        NounDefinition noun = null;
        foreach (NounDefinition candidate in catalog.nouns)
        {
            if (candidate != null && candidate.Matches(nounFamily))
            {
                noun = candidate;
                break;
            }
        }

        if (noun == null || noun.allowedVerbs == null)
            return null;

        string preferredVerb = role == BattleActionRole.Dodge && ContainsSemanticTag(noun, "bird")
            ? "FLY"
            : role == BattleActionRole.Defense && ContainsSemanticTag(noun, "aquatic")
                ? "SWIM"
                : "";
        if (!string.IsNullOrEmpty(preferredVerb))
        {
            foreach (VerbActionDefinition verb in catalog.verbs)
            {
                if (verb != null && verb.role == role &&
                    CreaturePhraseUtility.NormalizeToken(verb.verb) == preferredVerb)
                    return verb;
            }
        }

        foreach (string allowed in noun.allowedVerbs)
        {
            string normalizedAllowed = CreaturePhraseUtility.NormalizeToken(allowed);
            foreach (VerbActionDefinition verb in catalog.verbs)
            {
                if (verb == null || verb.role != role)
                    continue;
                if (CreaturePhraseUtility.NormalizeToken(verb.verb) == normalizedAllowed)
                    return verb;
            }
        }

        return null;
    }

    static bool ContainsSemanticTag(NounDefinition noun, string tag)
    {
        if (noun?.semanticTags == null || string.IsNullOrWhiteSpace(tag))
            return false;

        foreach (string candidate in noun.semanticTags)
        {
            if (string.Equals(candidate, tag, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    static string BuildEnemyAttackId(string nounFamily, VerbActionDefinition verb, string fallback)
    {
        string noun = CreaturePhraseUtility.NormalizeToken(nounFamily);
        string action = ResolveVerbToken(verb, fallback);
        return string.IsNullOrEmpty(noun) ? action : $"{noun}_{action}";
    }

    static string ResolveVerbToken(VerbActionDefinition verb, string fallback)
    {
        string token = CreaturePhraseUtility.NormalizeToken(verb != null ? verb.verb : fallback);
        return string.IsNullOrEmpty(token) ? CreaturePhraseUtility.NormalizeToken(fallback) : token;
    }

    static string BuildEnemyCommand(string nounFamily, VerbActionDefinition verb, string fallbackVerb)
    {
        string noun = FormatCommandNoun(nounFamily);
        if (verb == null)
            return $"{noun} {fallbackVerb}";

        string present = ResolveThirdPersonVerb(verb);
        return $"{noun} {present.ToLowerInvariant()}";
    }

    static string ResolveThirdPersonVerb(VerbActionDefinition verb)
    {
        if (verb == null)
            return "acts";
        if (verb.thirdPersonSingularForms != null && verb.thirdPersonSingularForms.Count > 0)
            return CreaturePhraseUtility.NormalizeToken(verb.thirdPersonSingularForms[0]);

        string baseVerb = CreaturePhraseUtility.NormalizeToken(verb.verb);
        if (string.IsNullOrEmpty(baseVerb))
            return "ACTS";
        if (baseVerb.EndsWith("Y") && baseVerb.Length > 1)
            return baseVerb.Substring(0, baseVerb.Length - 1) + "IES";
        if (baseVerb.EndsWith("S") || baseVerb.EndsWith("SH") || baseVerb.EndsWith("CH") || baseVerb.EndsWith("X") || baseVerb.EndsWith("Z"))
            return baseVerb + "ES";
        return baseVerb + "S";
    }

    static string FormatCommandNoun(string nounFamily)
    {
        string noun = CreaturePhraseUtility.NormalizeToken(nounFamily);
        if (string.IsNullOrEmpty(noun))
            return "Enemy";
        return char.ToUpperInvariant(noun[0]) + noun.Substring(1).ToLowerInvariant();
    }

    static LocalizedDialogueLine BuildTaskLine(
        NaturalGrammarRegion region,
        SemanticZoneKind zoneKind,
        GrammarDialogueTaskDefinition task,
        int npcIndex)
    {
        string expected = task.expectedResponse ?? "";
        var accepted = new List<string>();
        AddAccepted(accepted, expected);
        if (task.acceptedResponses != null)
        {
            foreach (string response in task.acceptedResponses)
                AddAccepted(accepted, response);
        }

        string lineId = $"{region.id}-{zoneKind.ToString().ToLowerInvariant()}-{npcIndex}-{task.taskId}";
        // A heard-wrong transcript is a listening/speaking correction exercise.
        // Keep this invariant at the line-building boundary so authored and
        // generated content cannot accidentally expose a handwriting response.
        GrammarDialogueInputMode resolvedInputMode = task.malfunctionType == GrammarDialogueMalfunctionType.HeardWrong
            ? GrammarDialogueInputMode.SpeakOnly
            : task.inputMode;
        return new LocalizedDialogueLine
        {
            lineId = lineId,
            dialogueTaskId = task.taskId,
            regionId = region.id,
            grammarTopic = region.grammarTopic,
            zoneKind = zoneKind,
            contextCue = string.IsNullOrWhiteSpace(task.contextCue)
                ? BuildDialogueContextCue(region, zoneKind, npcIndex)
                : task.contextCue,
            conceptId = task.conceptId != GrammarConceptId.None ? task.conceptId : region.conceptId,
            subskillId = string.IsNullOrWhiteSpace(task.subskillId) ? task.taskId : task.subskillId,
            npcLine = task.npcLine,
            sourceText = task.npcLine,
            sourceLanguage = "en",
            expectedEnglishResponse = expected,
            acceptedEnglishResponses = accepted,
            grammarFocusWords = ResolveGrammarFocusWords(task, expected),
            jumbleDistractorWords = ResolveJumbleDistractors(task),
            overrideAssistMode = true,
            assistMode = task.assistMode,
            inputMode = resolvedInputMode,
            malfunctionType = task.malfunctionType,
            grammarPattern = task.grammarPattern,
            scaffoldMode = task.scaffoldMode,
            buddyUseCase = task.buddyUseCase,
            allowAiHint = task.allowAiHint,
            openGrimoireOnWrongAnswer = task.openGrimoireOnWrongAnswer,
            teachingNote = task.teachingNote,
            localLanguageHint = task.localLanguageHint,
            cachedSpeech = NpcDialogueAudioCatalog.Load(task.taskId) ?? NpcDialogueAudioCatalog.Load(lineId),
        };
    }

    static List<string> ResolveGrammarFocusWords(GrammarDialogueTaskDefinition task, string expected)
    {
        if (task?.grammarFocusWords != null && task.grammarFocusWords.Count > 0)
            return new List<string>(task.grammarFocusWords);

        return DialogueFillInBlankScaffold.InferFocusWords(
            expected,
            task != null ? task.conceptId : GrammarConceptId.None,
            task != null ? task.grammarPattern : GrammarPhrasePattern.FullSentence);
    }

    static List<string> ResolveJumbleDistractors(GrammarDialogueTaskDefinition task)
    {
        if (task?.jumbleDistractorWords != null && task.jumbleDistractorWords.Count > 0)
            return new List<string>(task.jumbleDistractorWords);
        if (task == null || task.malfunctionType != GrammarDialogueMalfunctionType.ScrambledSentence)
            return new List<string>();

        List<string> bank = DialogueSentenceJumble.BuildWordBank(
            task.expectedResponse,
            task.conceptId,
            task.grammarPattern,
            task.taskId,
            maximumDistractors: 1);
        var expected = new HashSet<string>(
            DialogueTranscriptTokenizer.Tokenize(task.expectedResponse).ConvertAll(DialogueTranscriptTokenizer.NormalizeWord),
            StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (string word in bank)
            if (!expected.Contains(DialogueTranscriptTokenizer.NormalizeWord(word)))
                result.Add(word);
        return result;
    }

    static string BuildDialogueContextCue(NaturalGrammarRegion region, SemanticZoneKind zoneKind, int npcIndex)
    {
        if (region == null)
            return "";

        string place = zoneKind switch
        {
            SemanticZoneKind.Route => "on the route",
            SemanticZoneKind.Gym => "in the mastery gym",
            _ => "in town",
        };
        string role = zoneKind switch
        {
            SemanticZoneKind.Route => "a traveler or route sign",
            SemanticZoneKind.Gym => "the gym guide",
            _ => "a local resident",
        };
        return $"{role} {place} practices {region.grammarTopic}";
    }

    static void AddAccepted(List<string> accepted, string response)
    {
        if (accepted == null || string.IsNullOrWhiteSpace(response))
            return;
        string normalized = response.Trim();
        if (!accepted.Contains(normalized))
            accepted.Add(normalized);
    }
}
