using System;
using System.Collections.Generic;
using UnityEngine;


public static partial class NaturalGrammarProgression
{
    static Dictionary<string, GrammarDialogueTaskDefinition> BuildFirstSliceDialogueTasks()
    {
        var tasks = new Dictionary<string, GrammarDialogueTaskDefinition>();

        Add(tasks, Task("welcome-greet", GrammarConceptId.Greetings, "Hello. Welcome to the village. Answer with hello.", "Hello", GrammarPhrasePattern.LetterOnly, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None, "Hindi help: hello means namaste or namaskar.", teachingNote: "Greetings are whole phrases. Say them clearly so people understand you right away."));
        Add(tasks, Task("welcome-thank", GrammarConceptId.Greetings, "When someone helps you, say thank you. Try it with me.", "Thank you", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None, "Hindi help: thank you means dhanyavaad."));
        Add(tasks, Task("welcome-name", GrammarConceptId.Greetings, "Tell people your name with: my name is Aryan.", "My name is Aryan", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None, "Buddy can model the full sentence first."));
        Add(tasks, Task("welcome-goodbye", GrammarConceptId.Greetings, "When you leave kindly, say goodbye.", "Goodbye", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None, "Hindi help: goodbye can mean alvida."));
        Add(tasks, Task("welcome-listen", GrammarConceptId.Greetings, "The road sign crackles: hello traveler. Fill what you heard.", "Hello traveler", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Partial, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.MissingWord));
        Add(tasks, Task("welcome-answer", GrammarConceptId.Greetings, "A traveler asks: are you ready? Answer politely.", "Yes please", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Partial, GrammarDialogueInputMode.SpeakOrWrite, GrammarDialogueMalfunctionType.PartialTranscript, alternatives: new[] { "Yes, please", "Yes please." }));
        Add(tasks, Task("welcome-sign", GrammarConceptId.Greetings, "The route sign fades after good____. Complete the greeting.", "Goodbye", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Partial, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.PartialTranscript));
        Add(tasks, Task("welcome-heard-greeting", GrammarConceptId.Greetings, "The route speaker heard good night, but the guide said goodbye. Correct it.", "Goodbye", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Partial, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.HeardWrong));
        Add(tasks, Task("welcome-introduce", GrammarConceptId.Greetings, "No Buddy now. Greet me and introduce yourself.", "Hello my name is Aryan", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Off, GrammarDialogueInputMode.SpeakOrWrite, GrammarDialogueMalfunctionType.None, alternatives: new[] { "Hello, my name is Aryan" }));
        Add(tasks, Task("welcome-polite-reply", GrammarConceptId.Greetings, "Gym check: a villager offers help and you want it. Accept politely in English.", "Yes please", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Off, GrammarDialogueInputMode.SpeakOrWrite, GrammarDialogueMalfunctionType.None, alternatives: new[] { "Yes, please" }));
        Add(tasks, Task("welcome-goodbye-check", GrammarConceptId.Greetings, "Gym check: a villager is leaving. Say the appropriate English farewell.", "Goodbye", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Off, GrammarDialogueInputMode.SpeakOrWrite, GrammarDialogueMalfunctionType.None));

        Add(tasks, Task("alphabet-capitals", GrammarConceptId.Alphabet, "Capital letters are the tall forms. Say the capital letter A.", "A", GrammarPhrasePattern.LetterOnly, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("alphabet-smalls", GrammarConceptId.Alphabet, "Small letters are the everyday forms. Say the small letter b.", "b", GrammarPhrasePattern.LetterOnly, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("alphabet-order", GrammarConceptId.Alphabet, "Letters follow an order. After A comes B.", "B", GrammarPhrasePattern.LetterOnly, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("alphabet-letter-pair", GrammarConceptId.Alphabet, "Capital D and small d name the same letter. Say D.", "D", GrammarPhrasePattern.LetterOnly, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("alphabet-road-order", GrammarConceptId.Alphabet, "The sign says A ____ C. Fill the missing letter.", "B", GrammarPhrasePattern.LetterOnly, TranslatorAssistMode.Partial, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.MissingWord));
        Add(tasks, Task("alphabet-case-fix", GrammarConceptId.Alphabet, "The road board heard the wrong case. Correct it to capital A.", "A", GrammarPhrasePattern.LetterOnly, TranslatorAssistMode.Partial, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.HeardWrong));
        Add(tasks, Task("alphabet-heard-letter", GrammarConceptId.Alphabet, "A wild RAT appears. Say and write the first letter: R.", "R", GrammarPhrasePattern.LetterOnly, TranslatorAssistMode.Partial, GrammarDialogueInputMode.SpeakAndWrite, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("alphabet-route-chain", GrammarConceptId.Alphabet, "The route radio fades after C. Write the next letter you heard.", "D", GrammarPhrasePattern.LetterOnly, TranslatorAssistMode.Partial, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.PartialTranscript));
        Add(tasks, Task("alphabet-gym-capital", GrammarConceptId.Alphabet, "Gym check: a wild CAT appears. Say and write its first letter: C.", "C", GrammarPhrasePattern.LetterOnly, TranslatorAssistMode.Off, GrammarDialogueInputMode.SpeakAndWrite, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("alphabet-gym-next-letter", GrammarConceptId.Alphabet, "Gym check: what comes after D?", "E", GrammarPhrasePattern.LetterOnly, TranslatorAssistMode.Off, GrammarDialogueInputMode.SpeakOrWrite, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("alphabet-gym-small", GrammarConceptId.Alphabet, "Gym check: show the small letter f.", "f", GrammarPhrasePattern.LetterOnly, TranslatorAssistMode.Off, GrammarDialogueInputMode.SpeakOrWrite, GrammarDialogueMalfunctionType.None));

        Add(tasks, Task("vowel-five-vowels", GrammarConceptId.VowelsConsonants, "English has five vowels. Say A.", "A", GrammarPhrasePattern.LetterOnly, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("vowel-consonants", GrammarConceptId.VowelsConsonants, "B is not a vowel. It is a consonant. Say B.", "B", GrammarPhrasePattern.LetterOnly, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("vowel-letter-check", GrammarConceptId.VowelsConsonants, "O is one of the five vowels. Say O.", "O", GrammarPhrasePattern.LetterOnly, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("vowel-sound-sort", GrammarConceptId.VowelsConsonants, "I is a vowel too. Say I.", "I", GrammarPhrasePattern.LetterOnly, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("vowel-road-missing", GrammarConceptId.VowelsConsonants, "The route board lost one vowel: ____ is a vowel.", "A", GrammarPhrasePattern.LetterOnly, TranslatorAssistMode.Partial, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.MissingWord));
        Add(tasks, Task("vowel-road-heardwrong", GrammarConceptId.VowelsConsonants, "The route transcript said E, but the vowel you need is O.", "O", GrammarPhrasePattern.LetterOnly, TranslatorAssistMode.Partial, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.HeardWrong));
        Add(tasks, Task("vowel-road-choice", GrammarConceptId.VowelsConsonants, "A wild RAT appears. Say and write the vowel in RAT.", "A", GrammarPhrasePattern.LetterOnly, TranslatorAssistMode.Partial, GrammarDialogueInputMode.SpeakAndWrite, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("vowel-road-vowel-heard", GrammarConceptId.VowelsConsonants, "The route singer held a vowel. Write the vowel you heard.", "E", GrammarPhrasePattern.LetterOnly, TranslatorAssistMode.Partial, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.PartialTranscript));
        Add(tasks, Task("vowel-gym-vowel", GrammarConceptId.VowelsConsonants, "Gym check: a wild RAT appears. Say and write one vowel from RAT.", "A", GrammarPhrasePattern.LetterOnly, TranslatorAssistMode.Off, GrammarDialogueInputMode.SpeakAndWrite, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("vowel-gym-consonant", GrammarConceptId.VowelsConsonants, "Gym check: give me one consonant.", "R", GrammarPhrasePattern.LetterOnly, TranslatorAssistMode.Off, GrammarDialogueInputMode.SpeakOrWrite, GrammarDialogueMalfunctionType.None, alternatives: new[] { "B", "C", "D", "M" }));
        Add(tasks, Task("vowel-gym-pick-e", GrammarConceptId.VowelsConsonants, "Gym check: say the vowel E.", "E", GrammarPhrasePattern.LetterOnly, TranslatorAssistMode.Off, GrammarDialogueInputMode.SpeakOrWrite, GrammarDialogueMalfunctionType.None));

        Add(tasks, Task("sentence-start-capital", GrammarConceptId.SentenceStartEnd, "A sentence starts with a capital letter. Say: I am ready.", "I am ready.", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("sentence-end-stop", GrammarConceptId.SentenceStartEnd, "A sentence ends with a full stop. Say the whole clean sentence.", "We play.", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("sentence-clean-model", GrammarConceptId.SentenceStartEnd, "Clean sentence shape means start big and finish with a stop. Say: I play.", "I play.", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("sentence-ready-model", GrammarConceptId.SentenceStartEnd, "Try another clean sentence. Say: We are here.", "We are here.", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("sentence-road-capital", GrammarConceptId.SentenceStartEnd, "Fix the start: ____ am ready.", "I am ready.", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Partial, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.MissingWord));
        Add(tasks, Task("sentence-road-stop", GrammarConceptId.SentenceStartEnd, "The route transcript needs the clean sentence back.", "We play.", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Partial, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.PartialTranscript));
        Add(tasks, Task("sentence-road-fix", GrammarConceptId.SentenceStartEnd, "Correct what the signal got wrong: i play", "I play.", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Partial, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.HeardWrong));
        Add(tasks, Task("sentence-road-unscramble", GrammarConceptId.SentenceStartEnd, "Unscramble the clean sentence you heard.", "We are here.", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Partial, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.ScrambledSentence));
        Add(tasks, Task("sentence-gym-write", GrammarConceptId.SentenceStartEnd, "Gym check: put these words in natural sentence order: READY / AM / I.", "I am ready.", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Off, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("sentence-gym-say", GrammarConceptId.SentenceStartEnd, "Gym check: put these words in natural sentence order: PLAY / WE.", "We play.", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Off, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("sentence-gym-clean-copy", GrammarConceptId.SentenceStartEnd, "Gym check: put these words in natural sentence order: HERE / ARE / WE.", "We are here.", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Off, GrammarDialogueInputMode.SpeakOrWrite, GrammarDialogueMalfunctionType.None));

        Add(tasks, Task("noun-person-animal-place-thing", GrammarConceptId.BasicNouns, "A noun names a person, animal, place, or thing. Cat is a noun.", "Cat", GrammarPhrasePattern.NounOnly, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("noun-place-thing", GrammarConceptId.BasicNouns, "Shop is a noun because it names a place. Box is a noun because it names a thing.", "Shop", GrammarPhrasePattern.NounOnly, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None, alternatives: new[] { "Box" }));
        Add(tasks, Task("noun-creature-summon", GrammarConceptId.BasicNouns, "In battle, nouns become creatures. Summon a rat.", "Rat", GrammarPhrasePattern.NounOnly, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("noun-thing-check", GrammarConceptId.BasicNouns, "Box is a thing noun. Say box.", "Box", GrammarPhrasePattern.NounOnly, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("noun-family-sort", GrammarConceptId.BasicNouns, "The route transcript broke: ____ is an animal noun.", "Rat", GrammarPhrasePattern.NounOnly, TranslatorAssistMode.Partial, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.MissingWord, alternatives: new[] { "Cat", "Dog", "Duck", "Owl", "Bird", "Fish" }));
        Add(tasks, Task("noun-wild-summon", GrammarConceptId.BasicNouns, "A wild RAT appears. Say and write the noun: rat.", "Rat", GrammarPhrasePattern.NounOnly, TranslatorAssistMode.Partial, GrammarDialogueInputMode.SpeakAndWrite, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("noun-article-summon", GrammarConceptId.BasicNouns, "The route summon is missing its article: ____ rat.", "A rat", GrammarPhrasePattern.DeterminerNoun, TranslatorAssistMode.Partial, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.MissingWord, alternatives: new[] { "The rat" }, teachingNote: "Use a or the before the noun to make the summon phrase complete."));
        Add(tasks, Task("noun-heard-wrong", GrammarConceptId.BasicNouns, "The route transcript said run. Correct it with the noun you heard.", "Rat", GrammarPhrasePattern.NounOnly, TranslatorAssistMode.Partial, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.HeardWrong));
        Add(tasks, Task("noun-route-place-word", GrammarConceptId.BasicNouns, "The route speaker names a place noun: shop. Write the noun you heard.", "Shop", GrammarPhrasePattern.NounOnly, TranslatorAssistMode.Partial, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.PartialTranscript));
        Add(tasks, Task("noun-mixed-family-battle", GrammarConceptId.BasicNouns, "Gym check: a wild RAT appears. Say and write the full noun.", "Rat", GrammarPhrasePattern.NounOnly, TranslatorAssistMode.Off, GrammarDialogueInputMode.SpeakAndWrite, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("noun-gym-choice", GrammarConceptId.BasicNouns, "Gym check: choose one naming word for battle.", "Dog", GrammarPhrasePattern.NounOnly, TranslatorAssistMode.Off, GrammarDialogueInputMode.SpeakOrWrite, GrammarDialogueMalfunctionType.None, alternatives: new[] { "Rat", "Cat", "Bird" }));
        Add(tasks, Task("noun-gym-place", GrammarConceptId.BasicNouns, "Gym check: give me one place or thing noun.", "Shop", GrammarPhrasePattern.NounOnly, TranslatorAssistMode.Off, GrammarDialogueInputMode.SpeakOrWrite, GrammarDialogueMalfunctionType.None, alternatives: new[] { "Box" }));

        Add(tasks, Task("verb-action", GrammarConceptId.BasicVerbs, "A verb is an action. Bite, run, jump, and scratch are verbs.", "Bite", GrammarPhrasePattern.VerbOnly, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("verb-after-noun", GrammarConceptId.BasicVerbs, "Summon a noun first. Then use a full creature command. Say: rat bites.", "Rat bites", GrammarPhrasePattern.NounVerbPresent, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None, teachingNote: "In battle, the noun can lead the command. Rat bites joins the creature and the action together."));
        Add(tasks, Task("verb-context", GrammarConceptId.BasicVerbs, "Not every noun uses every verb. Birds fly. Say: bird flies.", "Bird flies", GrammarPhrasePattern.NounVerbPresent, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("verb-fish-swims", GrammarConceptId.BasicVerbs, "Fish use swim. Say: fish swims.", "Fish swims", GrammarPhrasePattern.NounVerbPresent, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("verb-animation-match", GrammarConceptId.BasicVerbs, "The trainer moved fast. Which verb did you hear: ____?", "Run", GrammarPhrasePattern.VerbOnly, TranslatorAssistMode.Partial, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.MissingWord));
        Add(tasks, Task("verb-wild-action", GrammarConceptId.BasicVerbs, "A wild creature waits. Use the creature and the action together.", "Rat bites", GrammarPhrasePattern.NounVerbPresent, TranslatorAssistMode.Partial, GrammarDialogueInputMode.SpeakOrWrite, GrammarDialogueMalfunctionType.PartialTranscript, alternatives: new[] { "Dog runs", "Bird flies", "Fish swims" }));
        Add(tasks, Task("verb-trainer-action", GrammarConceptId.BasicVerbs, "Unscramble the command you heard.", "Dog runs", GrammarPhrasePattern.NounVerbPresent, TranslatorAssistMode.Partial, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.ScrambledSentence));
        Add(tasks, Task("verb-road-correct-action", GrammarConceptId.BasicVerbs, "The route transcript said bird runs. Correct it with the action you heard.", "Bird flies", GrammarPhrasePattern.NounVerbPresent, TranslatorAssistMode.Partial, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.HeardWrong));
        Add(tasks, Task("verb-action-battle", GrammarConceptId.BasicVerbs, "Gym check: use a noun and a verb together without help.", "Rat bites", GrammarPhrasePattern.NounVerbPresent, TranslatorAssistMode.Off, GrammarDialogueInputMode.SpeakOrWrite, GrammarDialogueMalfunctionType.None, alternatives: new[] { "Dog runs", "Bird flies", "Fish swims" }));
        Add(tasks, Task("verb-gym-context", GrammarConceptId.BasicVerbs, "Gym check: use a bird action.", "Fly", GrammarPhrasePattern.VerbOnly, TranslatorAssistMode.Off, GrammarDialogueInputMode.SpeakOrWrite, GrammarDialogueMalfunctionType.None, alternatives: new[] { "Peck" }));
        Add(tasks, Task("verb-gym-runner", GrammarConceptId.BasicVerbs, "Gym check: use a runner command.", "Dog runs", GrammarPhrasePattern.NounVerbPresent, TranslatorAssistMode.Off, GrammarDialogueInputMode.SpeakOrWrite, GrammarDialogueMalfunctionType.None, alternatives: new[] { "Pup runs" }));

        Add(tasks, Task("article-a", GrammarConceptId.Articles, "Use a before a consonant sound. Say: a rat.", "A rat", GrammarPhrasePattern.DeterminerNoun, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None, teachingNote: "Use a before a consonant sound, an before a vowel sound, and the when the noun is specific."));
        Add(tasks, Task("article-an", GrammarConceptId.Articles, "Use an before a vowel sound. Say: an owl.", "An owl", GrammarPhrasePattern.DeterminerNoun, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("article-the", GrammarConceptId.Articles, "Use the when we mean a specific noun. Say: the cat.", "The cat", GrammarPhrasePattern.DeterminerNoun, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("article-bird", GrammarConceptId.Articles, "Bird starts with a consonant sound. Say: a bird.", "A bird", GrammarPhrasePattern.DeterminerNoun, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("article-road-missing", GrammarConceptId.Articles, "The route transcript lost its article: ____ rat.", "A rat", GrammarPhrasePattern.DeterminerNoun, TranslatorAssistMode.Partial, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.MissingWord, alternatives: new[] { "The rat" }));
        Add(tasks, Task("article-road-heardwrong", GrammarConceptId.Articles, "The route transcript heard a owl. Correct it.", "An owl", GrammarPhrasePattern.DeterminerNoun, TranslatorAssistMode.Partial, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.HeardWrong));
        Add(tasks, Task("article-road-answer", GrammarConceptId.Articles, "A trainer points to one known cat. Answer with the correct noun phrase.", "The cat", GrammarPhrasePattern.DeterminerNoun, TranslatorAssistMode.Partial, GrammarDialogueInputMode.SpeakOrWrite, GrammarDialogueMalfunctionType.PartialTranscript));
        Add(tasks, Task("article-road-unscramble", GrammarConceptId.Articles, "Unscramble the noun phrase you heard on the route.", "A bird", GrammarPhrasePattern.DeterminerNoun, TranslatorAssistMode.Partial, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.ScrambledSentence));
        Add(tasks, Task("article-gym-correct", GrammarConceptId.Articles, "Gym check: summon a noun with the correct article.", "An owl", GrammarPhrasePattern.DeterminerNoun, TranslatorAssistMode.Off, GrammarDialogueInputMode.SpeakOrWrite, GrammarDialogueMalfunctionType.None, alternatives: new[] { "A rat", "The cat" }));
        Add(tasks, Task("article-gym-specific", GrammarConceptId.Articles, "Gym check: use the for the specific dog.", "The dog", GrammarPhrasePattern.DeterminerNoun, TranslatorAssistMode.Off, GrammarDialogueInputMode.SpeakOrWrite, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("article-gym-bird", GrammarConceptId.Articles, "Gym check: use a before bird.", "A bird", GrammarPhrasePattern.DeterminerNoun, TranslatorAssistMode.Off, GrammarDialogueInputMode.SpeakOrWrite, GrammarDialogueMalfunctionType.None));

        Add(tasks, Task("pronoun-replace-nouns", GrammarConceptId.Pronouns, "Pronouns replace nouns. I, you, he, she, it, we, and they can stand in battle.", "I bite", GrammarPhrasePattern.PronounVerbPresent, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None, teachingNote: "Pronouns replace nouns so you do not repeat the full name every time."));
        Add(tasks, Task("pronoun-personal", GrammarConceptId.Pronouns, "When the curse says I, start your command with I.", "I bite", GrammarPhrasePattern.PronounVerbPresent, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("pronoun-curse-preview", GrammarConceptId.Pronouns, "He, she, and it change the verb: he bites.", "He bites", GrammarPhrasePattern.PronounVerbPresent, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("pronoun-it-runs", GrammarConceptId.Pronouns, "When the creature is already known, you can say it. Try: it runs.", "It runs", GrammarPhrasePattern.PronounVerbPresent, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("pronoun-ticket-replace", GrammarConceptId.Pronouns, "The ticket lost its subject: ____ bite.", "I bite", GrammarPhrasePattern.PronounVerbPresent, TranslatorAssistMode.Partial, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.MissingWord, alternatives: new[] { "You bite", "They bite" }));
        Add(tasks, Task("pronoun-curse-battle", GrammarConceptId.Pronouns, "The route curse says he. Use the correct command.", "He bites", GrammarPhrasePattern.PronounVerbPresent, TranslatorAssistMode.Partial, GrammarDialogueInputMode.SpeakOrWrite, GrammarDialogueMalfunctionType.PartialTranscript));
        Add(tasks, Task("pronoun-trainer-cycle", GrammarConceptId.Pronouns, "Trainer question: they or he? Answer with they bite.", "They bite", GrammarPhrasePattern.PronounVerbPresent, TranslatorAssistMode.Partial, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.ScrambledSentence));
        Add(tasks, Task("pronoun-road-correct", GrammarConceptId.Pronouns, "The route curse wanted they bite, not he bites. Correct it.", "They bite", GrammarPhrasePattern.PronounVerbPresent, TranslatorAssistMode.Partial, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.HeardWrong));
        Add(tasks, Task("pronoun-boss-cycle", GrammarConceptId.Pronouns, "Gym check: answer the curse with he.", "He bites", GrammarPhrasePattern.PronounVerbPresent, TranslatorAssistMode.Off, GrammarDialogueInputMode.SpeakOrWrite, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("pronoun-gym-they", GrammarConceptId.Pronouns, "Gym check: several creatures perform BITE. Use the correct pronoun and verb form.", "They bite", GrammarPhrasePattern.PronounVerbPresent, TranslatorAssistMode.Off, GrammarDialogueInputMode.SpeakOrWrite, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("pronoun-gym-i", GrammarConceptId.Pronouns, "Gym check: speak about yourself performing BITE. Use the correct pronoun and verb form.", "I bite", GrammarPhrasePattern.PronounVerbPresent, TranslatorAssistMode.Off, GrammarDialogueInputMode.SpeakOrWrite, GrammarDialogueMalfunctionType.None));

        Add(tasks, Task("plural-one-many", GrammarConceptId.Plurals, "One rat is singular. Many rats are plural.", "Rats", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None, teachingNote: "Plural endings change the noun to show more than one."));
        Add(tasks, Task("plural-es", GrammarConceptId.Plurals, "Box becomes boxes when there is more than one.", "Boxes", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("plural-ies", GrammarConceptId.Plurals, "Puppy becomes puppies when there is more than one.", "Puppies", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("plural-cats", GrammarConceptId.Plurals, "One cat, many cats. Say cats.", "Cats", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("plural-road-missing", GrammarConceptId.Plurals, "The route board says one cat, many ____. Fill the plural.", "Cats", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Partial, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.MissingWord));
        Add(tasks, Task("plural-road-heardwrong", GrammarConceptId.Plurals, "The route transcript heard puppyes. Correct the plural.", "Puppies", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Partial, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.HeardWrong));
        Add(tasks, Task("plural-road-unscramble", GrammarConceptId.Plurals, "Unscramble the plural phrase you heard.", "Many boxes", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Partial, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.ScrambledSentence));
        Add(tasks, Task("plural-road-many-dogs", GrammarConceptId.Plurals, "The route speaker says one dog, many dogs. Write the plural you heard.", "Dogs", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Partial, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.PartialTranscript));
        Add(tasks, Task("plural-gym-many", GrammarConceptId.Plurals, "Gym check: tell me the plural of rat.", "Rats", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Off, GrammarDialogueInputMode.SpeakOrWrite, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("plural-gym-ending", GrammarConceptId.Plurals, "Gym check: tell me the plural of puppy.", "Puppies", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Off, GrammarDialogueInputMode.SpeakOrWrite, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("plural-gym-boxes", GrammarConceptId.Plurals, "Gym check: tell me the plural of box.", "Boxes", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Off, GrammarDialogueInputMode.SpeakOrWrite, GrammarDialogueMalfunctionType.None));

        Add(tasks, Task("adjective-describe-nouns", GrammarConceptId.Adjectives, "An adjective describes a noun. Big rat means a rat with more strength.", "Big rat", GrammarPhrasePattern.AdjectiveNoun, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None, teachingNote: "Adjectives describe nouns and usually come before the noun here."));
        Add(tasks, Task("adjective-summon-tradeoff", GrammarConceptId.Adjectives, "Small cat is quicker but weaker. Say the summon.", "Small cat", GrammarPhrasePattern.AdjectiveNoun, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("adjective-article-summon", GrammarConceptId.Adjectives, "Articles can combine with adjectives. Say: a big rat.", "A big rat", GrammarPhrasePattern.DeterminerAdjectiveNoun, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None, alternatives: new[] { "The small cat", "A small bird" }));
        Add(tasks, Task("adjective-one-at-summon", GrammarConceptId.Adjectives, "Use one adjective when you summon. After that, verbs do the battling.", "Big dog", GrammarPhrasePattern.AdjectiveNoun, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("adjective-missing-word", GrammarConceptId.Adjectives, "The route transcript dropped the describing word: ____ rat.", "Big rat", GrammarPhrasePattern.AdjectiveNoun, TranslatorAssistMode.Partial, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.MissingWord));
        Add(tasks, Task("adjective-wild-summon", GrammarConceptId.Adjectives, "A wild trainer asks for a boosted summon.", "Small cat", GrammarPhrasePattern.AdjectiveNoun, TranslatorAssistMode.Partial, GrammarDialogueInputMode.SpeakOrWrite, GrammarDialogueMalfunctionType.PartialTranscript, alternatives: new[] { "Big rat", "Big dog", "Small bird" }));
        Add(tasks, Task("adjective-article-route", GrammarConceptId.Adjectives, "The route transcript lost two pieces: ____ big rat.", "A big rat", GrammarPhrasePattern.DeterminerAdjectiveNoun, TranslatorAssistMode.Partial, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.MissingWord, alternatives: new[] { "The big rat", "A small cat" }));
        Add(tasks, Task("adjective-trainer-choice", GrammarConceptId.Adjectives, "Unscramble the summon you heard.", "Big dog", GrammarPhrasePattern.AdjectiveNoun, TranslatorAssistMode.Partial, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.ScrambledSentence));
        Add(tasks, Task("adjective-boss-summon", GrammarConceptId.Adjectives, "Gym check: summon a noun with exactly one adjective.", "Big rat", GrammarPhrasePattern.AdjectiveNoun, TranslatorAssistMode.Off, GrammarDialogueInputMode.SpeakOrWrite, GrammarDialogueMalfunctionType.None, alternatives: new[] { "Small cat", "Big dog", "Small bird" }));
        Add(tasks, Task("adjective-article-boss", GrammarConceptId.Adjectives, "Gym check: use an article, one adjective, and one noun.", "A big rat", GrammarPhrasePattern.DeterminerAdjectiveNoun, TranslatorAssistMode.Off, GrammarDialogueInputMode.SpeakOrWrite, GrammarDialogueMalfunctionType.None, alternatives: new[] { "The small cat", "A small bird" }));
        Add(tasks, Task("adjective-gym-specific-summon", GrammarConceptId.Adjectives, "Gym check: use the, one adjective, and one noun.", "The small cat", GrammarPhrasePattern.DeterminerAdjectiveNoun, TranslatorAssistMode.Off, GrammarDialogueInputMode.SpeakOrWrite, GrammarDialogueMalfunctionType.None, alternatives: new[] { "The big dog" }));

        Add(tasks, Task("preposition-in-on", GrammarConceptId.BasicPrepositions, "Prepositions choose real grid positions. Say: the rat runs beside the rock.", "The rat runs beside the rock", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None, teachingNote: "Prepositions like beside, behind, over, under, and near only work when the grid has a possible place for them."));
        Add(tasks, Task("preposition-under-behind", GrammarConceptId.BasicPrepositions, "Behind means the back-side cell from the target. Say: the dog runs behind the wall.", "The dog runs behind the wall", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("preposition-meaning", GrammarConceptId.BasicPrepositions, "Over crosses a blocking object only when traversal is allowed. Say: the bird flies over the wall.", "The bird flies over the wall", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("preposition-on-roof", GrammarConceptId.BasicPrepositions, "Near means a cell one or two steps away. Say: the cat runs near the rock.", "The cat runs near the rock", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Full, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("preposition-road-missing", GrammarConceptId.BasicPrepositions, "The route transcript lost the place word: the rat runs ____ the rock.", "The rat runs beside the rock", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Partial, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.MissingWord, alternatives: new[] { "The rat runs near the rock", "The rat runs behind the rock" }));
        Add(tasks, Task("preposition-road-unscramble", GrammarConceptId.BasicPrepositions, "Unscramble the whole movement command you heard.", "The bird flies over the wall", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Partial, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.ScrambledSentence));
        Add(tasks, Task("preposition-road-correct", GrammarConceptId.BasicPrepositions, "The transcript said the rat runs beside the rock, but the target was behind it. Correct the command.", "The rat runs behind the rock", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Partial, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.HeardWrong));
        Add(tasks, Task("preposition-road-roof", GrammarConceptId.BasicPrepositions, "The route speaker almost said it: the cat runs near the ____. Write the full command.", "The cat runs near the rock", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Partial, GrammarDialogueInputMode.WriteOnly, GrammarDialogueMalfunctionType.PartialTranscript));
        Add(tasks, Task("preposition-gym-under", GrammarConceptId.BasicPrepositions, "Gym check: use under with authored cover.", "The rat runs under the roof", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Off, GrammarDialogueInputMode.SpeakOrWrite, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("preposition-gym-behind", GrammarConceptId.BasicPrepositions, "Gym check: move behind the rock.", "The dog runs behind the rock", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Off, GrammarDialogueInputMode.SpeakOrWrite, GrammarDialogueMalfunctionType.None));
        Add(tasks, Task("preposition-gym-in", GrammarConceptId.BasicPrepositions, "Gym check: move near the rock.", "The cat runs near the rock", GrammarPhrasePattern.FullSentence, TranslatorAssistMode.Off, GrammarDialogueInputMode.SpeakOrWrite, GrammarDialogueMalfunctionType.None));

        GeneratedGrammarExerciseBank.AddTo(tasks);

        return tasks;
    }

    static GrammarDialogueTaskDefinition Task(
        string id,
        GrammarConceptId conceptId,
        string npcLine,
        string expectedResponse,
        GrammarPhrasePattern pattern,
        TranslatorAssistMode assistMode,
        GrammarDialogueInputMode inputMode,
        GrammarDialogueMalfunctionType malfunctionType,
        string localLanguageHint = "",
        IEnumerable<string> alternatives = null,
        string subskillId = "",
        string teachingNote = "",
        IEnumerable<string> grammarFocusWords = null,
        IEnumerable<string> jumbleDistractors = null,
        string contextCue = "")
    {
        var accepted = new List<string>();
        AddAccepted(accepted, expectedResponse);
        if (alternatives != null)
        {
            foreach (string alternative in alternatives)
                AddAccepted(accepted, alternative);
        }

        GrammarDialogueInputMode resolvedInputMode = malfunctionType == GrammarDialogueMalfunctionType.HeardWrong
            ? GrammarDialogueInputMode.SpeakOnly
            : inputMode;

        var focusWords = grammarFocusWords != null
            ? new List<string>(grammarFocusWords)
            : DialogueFillInBlankScaffold.InferFocusWords(expectedResponse, conceptId, pattern);
        var distractors = jumbleDistractors != null
            ? new List<string>(jumbleDistractors)
            : new List<string>();

        return new GrammarDialogueTaskDefinition
        {
            taskId = id,
            conceptId = conceptId,
            subskillId = string.IsNullOrWhiteSpace(subskillId) ? id : subskillId,
            contextCue = contextCue,
            npcLine = npcLine,
            expectedResponse = expectedResponse,
            acceptedResponses = accepted,
            grammarFocusWords = focusWords,
            jumbleDistractorWords = distractors,
            grammarPattern = pattern,
            assistMode = assistMode,
            inputMode = resolvedInputMode,
            malfunctionType = malfunctionType,
            scaffoldMode = ResolveScaffoldMode(assistMode, malfunctionType),
            buddyUseCase = ResolveBuddyUseCase(assistMode),
            allowAiHint = assistMode != TranslatorAssistMode.Off,
            openGrimoireOnWrongAnswer = true,
            teachingNote = teachingNote,
            localLanguageHint = localLanguageHint,
        };
    }

    static GrammarPracticeScaffoldMode ResolveScaffoldMode(
        TranslatorAssistMode assistMode,
        GrammarDialogueMalfunctionType malfunctionType)
    {
        if (assistMode == TranslatorAssistMode.Off)
            return GrammarPracticeScaffoldMode.NoSubtitleGym;

        return malfunctionType switch
        {
            GrammarDialogueMalfunctionType.MissingWord => GrammarPracticeScaffoldMode.FillInBlank,
            GrammarDialogueMalfunctionType.ScrambledSentence => GrammarPracticeScaffoldMode.JumbledWords,
            GrammarDialogueMalfunctionType.HeardWrong => GrammarPracticeScaffoldMode.CorrectTranscript,
            GrammarDialogueMalfunctionType.PartialTranscript => GrammarPracticeScaffoldMode.PartialTranscript,
            _ => GrammarPracticeScaffoldMode.AuthoredSubtitle,
        };
    }

    static TranslatorBuddyUseCase ResolveBuddyUseCase(TranslatorAssistMode assistMode)
    {
        return assistMode switch
        {
            TranslatorAssistMode.Off => TranslatorBuddyUseCase.TeacherReportOnly,
            TranslatorAssistMode.Partial => TranslatorBuddyUseCase.AdaptiveHint,
            _ => TranslatorBuddyUseCase.ResponseCoach,
        };
    }

    static void Add(Dictionary<string, GrammarDialogueTaskDefinition> tasks, GrammarDialogueTaskDefinition task)
    {
        if (tasks == null || task == null || string.IsNullOrWhiteSpace(task.taskId))
            return;
        tasks[task.taskId] = task;
    }
}
