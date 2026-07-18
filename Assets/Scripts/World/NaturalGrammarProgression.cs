using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class NaturalGrammarRegion : GrammarRegionDefinition
{
}

public static partial class NaturalGrammarProgression
{
    static readonly string[] EarlyReviewNouns =
    {
        "ANT", "BIRD", "CAT", "DOG", "DUCK", "FISH", "HEN", "OWL", "PIG", "PUP", "QUAIL", "RAT"
    };

    static readonly NaturalGrammarRegion[] FirstSlice =
    {
        new NaturalGrammarRegion
        {
            id = "welcome_village",
            displayName = "Welcome Village",
            conceptId = GrammarConceptId.Greetings,
            grammarTopic = "Greetings and Survival English",
            grammarFocus = "hello, goodbye, yes, no, please, thank you, my name is",
            focus = "hello, goodbye, yes, no, please, thank you, my name is",
            tier = 1,
            encounterMode = GrammarEncounterMode.None,
            combatUnlocked = false,
            assistMode = TranslatorAssistMode.Full,
            unlockedPhrasePatterns = new[] { GrammarPhrasePattern.LetterOnly, GrammarPhrasePattern.FullSentence },
            vocabularyPool = new[] { "HELLO", "GOODBYE", "YES", "NO", "PLEASE", "THANK", "YOU", "MY", "NAME" },
            npcLessonIds = new[] { "welcome-greet", "welcome-thank", "welcome-name", "welcome-goodbye" },
            routePracticeIds = new[] { "welcome-listen", "welcome-answer", "welcome-sign", "welcome-heard-greeting" },
            gymCheckIds = new[] { "welcome-introduce", "welcome-polite-reply", "welcome-goodbye-check" },
            townNpcNames = new[] { "Mayor Mira", "Helper Hari", "Guide Gita", "Host Leela" },
            routeNpcNames = new[] { "Traveler Tara", "Courier Kabir", "Scout Noor", "Camper Vihaan" },
            gymLeaderName = "Head Guide Saanvi",
            groundTint = new Color(0.24f, 0.42f, 0.30f, 1f),
            roadTint = new Color(0.78f, 0.68f, 0.48f, 1f),
            buildingTint = new Color(0.88f, 0.76f, 0.58f, 1f),
            accentTint = new Color(0.99f, 0.47f, 0.33f, 1f),
            masteryTags = new[] { "greetings", "survival-english", "buddy-support" },
        },
        new NaturalGrammarRegion
        {
            id = "alphabet_acres",
            displayName = "Alphabet Acres",
            conceptId = GrammarConceptId.Alphabet,
            grammarTopic = "Alphabet",
            grammarFocus = "capital letters, small letters, and alphabetical order",
            focus = "capital letters, small letters, and alphabetical order",
            tier = 2,
            encounterMode = GrammarEncounterMode.LetterRecognition,
            combatUnlocked = false,
            assistMode = TranslatorAssistMode.Full,
            unlockedPhrasePatterns = new[] { GrammarPhrasePattern.LetterOnly },
            vocabularyPool = new[] { "A", "B", "C", "D", "E", "F", "R" },
            npcLessonIds = new[] { "alphabet-capitals", "alphabet-smalls", "alphabet-order", "alphabet-letter-pair" },
            routePracticeIds = new[] { "alphabet-road-order", "alphabet-case-fix", "alphabet-heard-letter", "alphabet-route-chain" },
            gymCheckIds = new[] { "alphabet-gym-capital", "alphabet-gym-next-letter", "alphabet-gym-small" },
            townNpcNames = new[] { "Caretaker Asha", "Farmer Ben", "Librarian Dev", "Tutor Faye" },
            routeNpcNames = new[] { "Signkeeper Isha", "Messenger Lee", "Ranger Om", "Walker Ritu" },
            gymLeaderName = "Alphabet Warden Cora",
            groundTint = new Color(0.48f, 0.66f, 0.34f, 1f),
            roadTint = new Color(0.32f, 0.29f, 0.25f, 1f),
            buildingTint = new Color(0.79f, 0.84f, 0.49f, 1f),
            accentTint = new Color(0.95f, 0.25f, 0.22f, 1f),
            masteryTags = new[] { "alphabet", "capital-letters", "small-letters" },
        },
        new NaturalGrammarRegion
        {
            id = "vowel_valley",
            displayName = "Vowel Valley",
            conceptId = GrammarConceptId.VowelsConsonants,
            grammarTopic = "Vowels and Consonants",
            grammarFocus = "the five vowels and the consonants around them",
            focus = "the five vowels and the consonants around them",
            tier = 3,
            encounterMode = GrammarEncounterMode.LetterRecognition,
            combatUnlocked = false,
            assistMode = TranslatorAssistMode.Full,
            unlockedPhrasePatterns = new[] { GrammarPhrasePattern.LetterOnly },
            vocabularyPool = new[] { "A", "E", "I", "O", "U", "B", "C", "D", "M", "R" },
            npcLessonIds = new[] { "vowel-five-vowels", "vowel-consonants", "vowel-letter-check", "vowel-sound-sort" },
            routePracticeIds = new[] { "vowel-road-missing", "vowel-road-heardwrong", "vowel-road-choice", "vowel-road-vowel-heard" },
            gymCheckIds = new[] { "vowel-gym-vowel", "vowel-gym-consonant", "vowel-gym-pick-e" },
            townNpcNames = new[] { "Singer Esha", "Teacher Manoj", "Guide Rumi", "Chanter Emon" },
            routeNpcNames = new[] { "Echo Ira", "Scout Niam", "Bellkeeper Omi", "Guide Rhea" },
            gymLeaderName = "Valley Voice Uma",
            groundTint = new Color(0.26f, 0.48f, 0.56f, 1f),
            roadTint = new Color(0.22f, 0.26f, 0.32f, 1f),
            buildingTint = new Color(0.58f, 0.78f, 0.88f, 1f),
            accentTint = new Color(0.98f, 0.89f, 0.40f, 1f),
            masteryTags = new[] { "vowels", "consonants", "letter-classification" },
        },
        new NaturalGrammarRegion
        {
            id = "sentence_square",
            displayName = "Sentence Square",
            conceptId = GrammarConceptId.SentenceStartEnd,
            grammarTopic = "Sentence Start and Full Stop",
            grammarFocus = "a sentence starts with a capital letter and ends with a full stop",
            focus = "a sentence starts with a capital letter and ends with a full stop",
            tier = 4,
            encounterMode = GrammarEncounterMode.None,
            combatUnlocked = false,
            assistMode = TranslatorAssistMode.Full,
            unlockedPhrasePatterns = new[] { GrammarPhrasePattern.FullSentence },
            vocabularyPool = new[] { "I", "AM", "READY", "WE", "PLAY" },
            npcLessonIds = new[] { "sentence-start-capital", "sentence-end-stop", "sentence-clean-model", "sentence-ready-model" },
            routePracticeIds = new[] { "sentence-road-capital", "sentence-road-stop", "sentence-road-fix", "sentence-road-unscramble" },
            gymCheckIds = new[] { "sentence-gym-write", "sentence-gym-say", "sentence-gym-clean-copy" },
            townNpcNames = new[] { "Clerk Piya", "Coach Rehan", "Tutor Sana", "Editor Zain" },
            routeNpcNames = new[] { "Proofreader Kian", "Messenger Jia", "Watcher Neel", "Runner Tara" },
            gymLeaderName = "Square Keeper Imaan",
            groundTint = new Color(0.49f, 0.37f, 0.53f, 1f),
            roadTint = new Color(0.18f, 0.20f, 0.24f, 1f),
            buildingTint = new Color(0.74f, 0.67f, 0.83f, 1f),
            accentTint = new Color(0.97f, 0.70f, 0.30f, 1f),
            masteryTags = new[] { "sentence-start", "full-stop", "sentence-mechanics" },
        },
        new NaturalGrammarRegion
        {
            id = "nounfield_town",
            displayName = "Nounfield Town",
            conceptId = GrammarConceptId.BasicNouns,
            grammarTopic = "Nouns",
            grammarFocus = "people, animals, places, and things as naming words",
            focus = "people, animals, places, and things as naming words",
            tier = 5,
            encounterMode = GrammarEncounterMode.NounRecognition,
            combatUnlocked = false,
            assistMode = TranslatorAssistMode.Full,
            unlockedPhrasePatterns = new[] { GrammarPhrasePattern.DeterminerNoun, GrammarPhrasePattern.NounOnly },
            vocabularyPool = new[] { "RAT", "CAT", "DOG", "DUCK", "OWL", "PUP", "BIRD", "FISH", "SHOP", "BOX" },
            currentNounFamilies = CreatureCombatCatalog.PronunciationBackedConcreteNouns,
            reviewNounFamilies = EarlyReviewNouns,
            npcLessonIds = new[] { "noun-person-animal-place-thing", "noun-place-thing", "noun-creature-summon", "noun-thing-check" },
            routePracticeIds = new[] { "noun-family-sort", "noun-wild-summon", "noun-article-summon", "noun-heard-wrong", "noun-route-place-word" },
            gymCheckIds = new[] { "noun-mixed-family-battle", "noun-gym-choice", "noun-gym-place" },
            townNpcNames = new[] { "Ranger Nila", "Shopkeeper Bharat", "Breeder Toma", "Guide Kavi" },
            routeNpcNames = new[] { "Trainer Rafi", "Trainer Mina", "Trainer Oren", "Trainer Elin", "Scout Asha" },
            gymLeaderName = "Noun Master Rohan",
            groundTint = new Color(0.47f, 0.58f, 0.26f, 1f),
            roadTint = new Color(0.35f, 0.25f, 0.18f, 1f),
            buildingTint = new Color(0.79f, 0.63f, 0.42f, 1f),
            accentTint = new Color(0.85f, 0.32f, 0.27f, 1f),
            wildEncounterPools = new[]
            {
                Pool("nounfield-wild-small-animals", "Small animal nouns", SemanticZoneKind.Route, 1, Patterns(GrammarPhrasePattern.NounOnly), Tags("noun", "summon"), "RAT", "CAT", "DOG", "PUP"),
                Pool("nounfield-wild-wing-water", "Wing and water nouns", SemanticZoneKind.Route, 2, Patterns(GrammarPhrasePattern.NounOnly), Tags("noun", "family"), "BIRD", "DUCK", "FISH", "OWL"),
            },
            trainerBattlePools = new[]
            {
                Pool("nounfield-trainer-starter", "Starter noun trainer", SemanticZoneKind.Route, 2, Patterns(GrammarPhrasePattern.NounOnly), Tags("noun", "trainer"), "RAT", "CAT", "DOG"),
                Pool("nounfield-trainer-mixed", "Mixed noun trainer", SemanticZoneKind.Route, 3, Patterns(GrammarPhrasePattern.NounOnly), Tags("noun", "mixed-family"), "BIRD", "FISH", "DUCK", "OWL"),
            },
            masteryTags = new[] { "noun", "noun-summon", "noun-family" },
        },
        new NaturalGrammarRegion
        {
            id = "verb_village",
            displayName = "Verb Village",
            conceptId = GrammarConceptId.BasicVerbs,
            grammarTopic = "Verbs",
            grammarFocus = "bite, run, jump, scratch, fly, peck, and swim as action words",
            focus = "bite, run, jump, scratch, fly, peck, and swim as action words",
            tier = 6,
            encounterMode = GrammarEncounterMode.TacticalCommand,
            combatUnlocked = true,
            assistMode = TranslatorAssistMode.Full,
            unlockedPhrasePatterns = new[] { GrammarPhrasePattern.NounOnly, GrammarPhrasePattern.NounVerbPresent, GrammarPhrasePattern.VerbOnly },
            vocabularyPool = new[] { "BITE", "RUN", "JUMP", "SCRATCH", "FLY", "PECK", "SWIM" },
            currentNounFamilies = CreatureCombatCatalog.PronunciationBackedConcreteNouns,
            reviewNounFamilies = EarlyReviewNouns,
            npcLessonIds = new[] { "verb-action", "verb-after-noun", "verb-context", "verb-fish-swims" },
            routePracticeIds = new[] { "verb-animation-match", "verb-wild-action", "verb-trainer-action", "verb-road-correct-action" },
            gymCheckIds = new[] { "verb-action-battle", "verb-gym-context", "verb-gym-runner" },
            townNpcNames = new[] { "Coach Veer", "Fisher Lata", "Falconer Zoya", "Swimmer Rupa" },
            routeNpcNames = new[] { "Runner Iqra", "Trainer Kunal", "Trainer Sefi", "Trainer Dev" },
            gymLeaderName = "Verb Chief Arjun",
            groundTint = new Color(0.27f, 0.44f, 0.29f, 1f),
            roadTint = new Color(0.22f, 0.24f, 0.27f, 1f),
            buildingTint = new Color(0.57f, 0.70f, 0.58f, 1f),
            accentTint = new Color(0.99f, 0.54f, 0.22f, 1f),
            wildEncounterPools = new[]
            {
                Pool("verb-wild-bite-run", "Rat and dog action practice", SemanticZoneKind.Route, 2, Patterns(GrammarPhrasePattern.VerbOnly, GrammarPhrasePattern.NounVerbPresent), Tags("verb", "starter-action"), "RAT", "DOG", "PUP"),
                Pool("verb-wild-context", "Context verb practice", SemanticZoneKind.Route, 2, Patterns(GrammarPhrasePattern.VerbOnly, GrammarPhrasePattern.NounVerbPresent), Tags("verb", "noun-context"), "BIRD", "DUCK", "FISH"),
            },
            trainerBattlePools = new[]
            {
                Pool("verb-trainer-rat", "Rat Trainer", SemanticZoneKind.Route, 3, Patterns(GrammarPhrasePattern.VerbOnly, GrammarPhrasePattern.NounVerbPresent), Tags("verb", "starter-action"), "RAT", "CAT", "DOG"),
                Pool("verb-trainer-river", "River Trainer", SemanticZoneKind.Route, 3, Patterns(GrammarPhrasePattern.VerbOnly, GrammarPhrasePattern.NounVerbPresent), Tags("verb", "noun-context"), "BIRD", "DUCK", "FISH"),
            },
            masteryTags = new[] { "verb", "verb-role", "noun-verb-context" },
        },
        new NaturalGrammarRegion
        {
            id = "article_arcade",
            displayName = "Article Arcade",
            conceptId = GrammarConceptId.Articles,
            grammarTopic = "Articles",
            grammarFocus = "a, an, and the before nouns",
            focus = "a, an, and the before nouns",
            tier = 7,
            encounterMode = GrammarEncounterMode.TacticalCommand,
            combatUnlocked = true,
            assistMode = TranslatorAssistMode.Full,
            unlockedPhrasePatterns = new[] { GrammarPhrasePattern.NounOnly, GrammarPhrasePattern.VerbOnly, GrammarPhrasePattern.NounVerbPresent, GrammarPhrasePattern.DeterminerNoun },
            vocabularyPool = new[] { "A", "AN", "THE", "RAT", "CAT", "DOG", "OWL", "BIRD", "BOX" },
            currentNounFamilies = CreatureCombatCatalog.PronunciationBackedConcreteNouns,
            reviewNounFamilies = EarlyReviewNouns,
            npcLessonIds = new[] { "article-a", "article-an", "article-the", "article-bird" },
            routePracticeIds = new[] { "article-road-missing", "article-road-heardwrong", "article-road-answer", "article-road-unscramble" },
            gymCheckIds = new[] { "article-gym-correct", "article-gym-specific", "article-gym-bird" },
            townNpcNames = new[] { "Vendor Alia", "Curator Theo", "Guide Naren", "Merchant Isha" },
            routeNpcNames = new[] { "Trainer Elio", "Trainer Kavya", "Trainer Miro", "Trainer Veda" },
            gymLeaderName = "Article Host Elena",
            groundTint = new Color(0.55f, 0.34f, 0.24f, 1f),
            roadTint = new Color(0.25f, 0.21f, 0.18f, 1f),
            buildingTint = new Color(0.92f, 0.78f, 0.52f, 1f),
            accentTint = new Color(0.96f, 0.24f, 0.22f, 1f),
            wildEncounterPools = new[]
            {
                Pool("article-wild-a-an", "A and an summons", SemanticZoneKind.Route, 2, Patterns(GrammarPhrasePattern.DeterminerNoun), Tags("articles", "a-an"), "RAT", "OWL", "CAT"),
                Pool("article-wild-the", "The summons", SemanticZoneKind.Route, 2, Patterns(GrammarPhrasePattern.DeterminerNoun), Tags("articles", "specific"), "DOG", "BIRD", "DUCK"),
            },
            trainerBattlePools = new[]
            {
                Pool("article-trainer-starter", "Starter article trainer", SemanticZoneKind.Route, 3, Patterns(GrammarPhrasePattern.DeterminerNoun), Tags("articles", "trainer"), "RAT", "OWL", "CAT"),
                Pool("article-trainer-specific", "Specific article trainer", SemanticZoneKind.Route, 3, Patterns(GrammarPhrasePattern.DeterminerNoun), Tags("articles", "specific"), "DOG", "BIRD", "OWL"),
            },
            masteryTags = new[] { "articles", "a-an-the", "noun-phrases" },
        },
        new NaturalGrammarRegion
        {
            id = "pronoun_port",
            displayName = "Pronoun Port",
            conceptId = GrammarConceptId.Pronouns,
            grammarTopic = "Pronouns",
            grammarFocus = "I, you, he, she, it, we, and they replace nouns in commands",
            focus = "I, you, he, she, it, we, and they replace nouns in commands",
            tier = 8,
            encounterMode = GrammarEncounterMode.TacticalCommand,
            combatUnlocked = true,
            assistMode = TranslatorAssistMode.Full,
            unlockedPhrasePatterns = new[]
            {
                GrammarPhrasePattern.NounOnly,
                GrammarPhrasePattern.VerbOnly,
                GrammarPhrasePattern.NounVerbPresent,
                GrammarPhrasePattern.DeterminerNoun,
                GrammarPhrasePattern.PronounVerbPresent,
            },
            vocabularyPool = new[] { "I", "YOU", "HE", "SHE", "IT", "WE", "THEY", "BITE", "RUN", "JUMP" },
            currentNounFamilies = CreatureCombatCatalog.PronunciationBackedConcreteNouns,
            reviewNounFamilies = EarlyReviewNouns,
            npcLessonIds = new[] { "pronoun-replace-nouns", "pronoun-personal", "pronoun-curse-preview", "pronoun-it-runs" },
            routePracticeIds = new[] { "pronoun-ticket-replace", "pronoun-curse-battle", "pronoun-trainer-cycle", "pronoun-road-correct" },
            gymCheckIds = new[] { "pronoun-boss-cycle", "pronoun-gym-they", "pronoun-gym-i" },
            townNpcNames = new[] { "Captain Ria", "Scribe Heman", "Guide Yara", "Navigator Pia" },
            routeNpcNames = new[] { "Trainer Ivo", "Trainer Tashi", "Trainer Noor", "Trainer Hadi" },
            gymLeaderName = "Port Captain Heena",
            groundTint = new Color(0.20f, 0.42f, 0.53f, 1f),
            roadTint = new Color(0.17f, 0.22f, 0.28f, 1f),
            buildingTint = new Color(0.45f, 0.74f, 0.86f, 1f),
            accentTint = new Color(0.97f, 0.66f, 0.27f, 1f),
            newCurses = new[] { GrammarBattleCurse.I, GrammarBattleCurse.You, GrammarBattleCurse.HeSheIt, GrammarBattleCurse.They },
            wildEncounterPools = new[]
            {
                Pool("pronoun-wild-i-you", "I and you curse practice", SemanticZoneKind.Route, 2, Patterns(GrammarPhrasePattern.PronounVerbPresent), Tags("pronoun", "curse"), "RAT", "CAT", "DOG"),
                Pool("pronoun-wild-they", "They command practice", SemanticZoneKind.Route, 2, Patterns(GrammarPhrasePattern.PronounVerbPresent), Tags("pronoun", "agreement"), "BIRD", "DUCK", "FISH"),
            },
            trainerBattlePools = new[]
            {
                Pool("pronoun-trainer-cycle-a", "Pronoun cycle trainer A", SemanticZoneKind.Route, 3, Patterns(GrammarPhrasePattern.PronounVerbPresent), Tags("pronoun", "trainer"), "RAT", "DOG", "PUP"),
                Pool("pronoun-trainer-cycle-b", "Pronoun cycle trainer B", SemanticZoneKind.Route, 3, Patterns(GrammarPhrasePattern.PronounVerbPresent), Tags("pronoun", "curse"), "CAT", "BIRD", "OWL"),
            },
            masteryTags = new[] { "pronoun", "pronoun-verb", "curse-subject" },
        },
        new NaturalGrammarRegion
        {
            id = "plural_plains",
            displayName = "Plural Plains",
            conceptId = GrammarConceptId.Plurals,
            grammarTopic = "Plurals",
            grammarFocus = "one noun and more-than-one noun with s, es, or ies",
            focus = "one noun and more-than-one noun with s, es, or ies",
            tier = 9,
            encounterMode = GrammarEncounterMode.None,
            combatUnlocked = false,
            assistMode = TranslatorAssistMode.Full,
            unlockedPhrasePatterns = new[] { GrammarPhrasePattern.NounOnly, GrammarPhrasePattern.FullSentence },
            vocabularyPool = new[] { "RAT", "RATS", "BOX", "BOXES", "PUPPY", "PUPPIES" },
            npcLessonIds = new[] { "plural-one-many", "plural-es", "plural-ies", "plural-cats" },
            routePracticeIds = new[] { "plural-road-missing", "plural-road-heardwrong", "plural-road-unscramble", "plural-road-many-dogs" },
            gymCheckIds = new[] { "plural-gym-many", "plural-gym-ending", "plural-gym-boxes" },
            townNpcNames = new[] { "Farmer Pema", "Collector Yusuf", "Guide Bela", "Guide Omna" },
            routeNpcNames = new[] { "Counter Nivi", "Scout Daman", "Watcher Elan", "Counter Ravi" },
            gymLeaderName = "Plains Keeper Rosie",
            groundTint = new Color(0.64f, 0.72f, 0.31f, 1f),
            roadTint = new Color(0.40f, 0.31f, 0.19f, 1f),
            buildingTint = new Color(0.90f, 0.84f, 0.57f, 1f),
            accentTint = new Color(0.86f, 0.36f, 0.24f, 1f),
            masteryTags = new[] { "plurals", "s-es-ies", "noun-number" },
        },
        new NaturalGrammarRegion
        {
            id = "adjective_grove",
            displayName = "Adjective Grove",
            conceptId = GrammarConceptId.Adjectives,
            grammarTopic = "Adjectives",
            grammarFocus = "big rat, small cat, and describing nouns before battle",
            focus = "big rat, small cat, and describing nouns before battle",
            tier = 10,
            encounterMode = GrammarEncounterMode.TacticalCommand,
            combatUnlocked = true,
            assistMode = TranslatorAssistMode.Full,
            unlockedPhrasePatterns = new[]
            {
                GrammarPhrasePattern.NounOnly,
                GrammarPhrasePattern.NounVerbPresent,
                GrammarPhrasePattern.DeterminerNoun,
                GrammarPhrasePattern.VerbOnly,
                GrammarPhrasePattern.AdjectiveNoun,
                GrammarPhrasePattern.DeterminerAdjectiveNoun,
            },
            vocabularyPool = new[] { "BIG", "SMALL", "RAT", "CAT", "DOG", "BIRD", "DUCK", "FISH", "A", "THE" },
            currentNounFamilies = CreatureCombatCatalog.PronunciationBackedConcreteNouns,
            reviewNounFamilies = EarlyReviewNouns,
            npcLessonIds = new[] { "adjective-describe-nouns", "adjective-summon-tradeoff", "adjective-article-summon", "adjective-one-at-summon" },
            routePracticeIds = new[] { "adjective-missing-word", "adjective-wild-summon", "adjective-article-route", "adjective-trainer-choice" },
            gymCheckIds = new[] { "adjective-boss-summon", "adjective-article-boss", "adjective-gym-specific-summon" },
            townNpcNames = new[] { "Smith Tara", "Guide Milan", "Breeder Jovi", "Cook Reva" },
            routeNpcNames = new[] { "Trainer Kira", "Trainer Arlo", "Trainer Nima", "Trainer Sora" },
            gymLeaderName = "Grove Stylist Leena",
            groundTint = new Color(0.30f, 0.50f, 0.23f, 1f),
            roadTint = new Color(0.23f, 0.18f, 0.16f, 1f),
            buildingTint = new Color(0.70f, 0.58f, 0.44f, 1f),
            accentTint = new Color(0.98f, 0.44f, 0.63f, 1f),
            wildEncounterPools = new[]
            {
                Pool("adjective-wild-big-small", "Big and small summons", SemanticZoneKind.Route, 2, Patterns(GrammarPhrasePattern.AdjectiveNoun), Tags("adjective", "summon-tradeoff"), "RAT", "CAT", "DOG"),
                Pool("adjective-wild-speed-trade", "Stat tradeoff summons", SemanticZoneKind.Route, 2, Patterns(GrammarPhrasePattern.AdjectiveNoun), Tags("adjective", "stat-balance"), "BIRD", "DUCK", "PUP"),
            },
            trainerBattlePools = new[]
            {
                Pool("adjective-trainer-power", "Power adjective trainer", SemanticZoneKind.Route, 3, Patterns(GrammarPhrasePattern.AdjectiveNoun), Tags("adjective", "power-tradeoff"), "RAT", "DOG", "BIRD"),
                Pool("adjective-trainer-balance", "Balanced adjective trainer", SemanticZoneKind.Route, 3, Patterns(GrammarPhrasePattern.AdjectiveNoun), Tags("adjective", "anti-spam"), "CAT", "DUCK", "FISH"),
            },
            masteryTags = new[] { "adjective", "summon-tradeoff", "anti-spam" },
        },
        new NaturalGrammarRegion
        {
            id = "preposition_park",
            displayName = "Preposition Park",
            conceptId = GrammarConceptId.BasicPrepositions,
            grammarTopic = "Basic Prepositions",
            grammarFocus = "beside, behind, over, under, and near for tactical location",
            focus = "beside, behind, over, under, and near for tactical location",
            tier = 11,
            encounterMode = GrammarEncounterMode.TacticalCommand,
            combatUnlocked = true,
            assistMode = TranslatorAssistMode.Full,
            unlockedPhrasePatterns = new[]
            {
                GrammarPhrasePattern.NounOnly,
                GrammarPhrasePattern.VerbOnly,
                GrammarPhrasePattern.NounVerbPresent,
                GrammarPhrasePattern.FullSentence,
            },
            vocabularyPool = new[] { "BESIDE", "BEHIND", "OVER", "UNDER", "NEAR", "THROUGH", "RAT", "DOG", "BIRD", "BOX", "ROCK", "WALL", "ROOF", "BRIDGE" },
            currentNounFamilies = CreatureCombatCatalog.PronunciationBackedConcreteNouns,
            reviewNounFamilies = EarlyReviewNouns,
            npcLessonIds = new[] { "preposition-in-on", "preposition-under-behind", "preposition-meaning", "preposition-on-roof" },
            routePracticeIds = new[] { "preposition-road-missing", "preposition-road-unscramble", "preposition-road-correct", "preposition-road-roof" },
            gymCheckIds = new[] { "preposition-gym-under", "preposition-gym-behind", "preposition-gym-in" },
            townNpcNames = new[] { "Guard Ina", "Builder Rook", "Guide Sumi", "Guide Kuhu" },
            routeNpcNames = new[] { "Scout Pari", "Marker Tenzin", "Watcher Hugh", "Watcher Rian" },
            gymLeaderName = "Park Ranger Bhavya",
            groundTint = new Color(0.22f, 0.45f, 0.36f, 1f),
            roadTint = new Color(0.18f, 0.25f, 0.22f, 1f),
            buildingTint = new Color(0.54f, 0.78f, 0.72f, 1f),
            accentTint = new Color(0.99f, 0.76f, 0.31f, 1f),
            wildEncounterPools = new[]
            {
                Pool("preposition-wild-beside-near", "Beside and near practice", SemanticZoneKind.Route, 2, Patterns(GrammarPhrasePattern.NounVerbPresent, GrammarPhrasePattern.FullSentence), Tags("prepositions", "grid-position"), "RAT", "CAT", "DOG"),
                Pool("preposition-wild-over-behind", "Over and behind practice", SemanticZoneKind.Route, 2, Patterns(GrammarPhrasePattern.NounVerbPresent, GrammarPhrasePattern.FullSentence), Tags("prepositions", "obstacle-path"), "BIRD", "DUCK", "PUP"),
            },
            trainerBattlePools = new[]
            {
                Pool("preposition-trainer-rock", "Rock Trainer", SemanticZoneKind.Route, 3, Patterns(GrammarPhrasePattern.NounVerbPresent, GrammarPhrasePattern.FullSentence), Tags("prepositions", "grid-position"), "RAT", "DOG", "CAT"),
                Pool("preposition-trainer-wall", "Wall Trainer", SemanticZoneKind.Route, 3, Patterns(GrammarPhrasePattern.NounVerbPresent, GrammarPhrasePattern.FullSentence), Tags("prepositions", "obstacle-path"), "BIRD", "DUCK", "FISH"),
            },
            masteryTags = new[] { "prepositions", "location-words", "sentence-meaning" },
        },
    };

    static readonly NaturalGrammarRegion[] ProgressionOrder = BuildProgressionOrder();

    public static IReadOnlyList<NaturalGrammarRegion> Regions => ProgressionOrder;
    public static IReadOnlyList<GrammarRegionDefinition> RegionDefinitions => ProgressionOrder;
    public static IReadOnlyDictionary<string, GrammarDialogueTaskDefinition> DialogueTasks => FirstSliceDialogueTasks;

    static readonly Dictionary<string, GrammarDialogueTaskDefinition> FirstSliceDialogueTasks = BuildFirstSliceDialogueTasks();





}
