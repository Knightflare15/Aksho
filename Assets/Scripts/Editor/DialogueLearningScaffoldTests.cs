using System;
using System.Collections.Generic;
using NUnit.Framework;

public sealed class DialogueLearningScaffoldTests
{
    [TestCase("An owl waits", GrammarConceptId.Articles, GrammarPhrasePattern.DeterminerNoun, "____ owl waits")]
    [TestCase("A big rat", GrammarConceptId.Adjectives, GrammarPhrasePattern.DeterminerAdjectiveNoun, "A ____ rat")]
    [TestCase("Bird flies", GrammarConceptId.BasicVerbs, GrammarPhrasePattern.NounVerbPresent, "Bird ____")]
    [TestCase("The rat runs behind the rock", GrammarConceptId.BasicPrepositions, GrammarPhrasePattern.FullSentence, "The rat runs ____ the rock")]
    public void FillInBlank_BlanksTheGrammarTarget(
        string sentence,
        GrammarConceptId conceptId,
        GrammarPhrasePattern pattern,
        string expected)
    {
        string scaffold = DialogueFillInBlankScaffold.Build(sentence, conceptId, pattern, "test");

        Assert.AreEqual(expected, scaffold);
    }

    [Test]
    public void FillInBlank_AuthoredFocusOverridesGenericFallback()
    {
        string scaffold = DialogueFillInBlankScaffold.Build(
            "This is my town",
            GrammarConceptId.None,
            GrammarPhrasePattern.FullSentence,
            "possessive",
            new[] { "my" });

        Assert.AreEqual("This is ____ town", scaffold);
        Assert.That(scaffold, Does.Contain(" is "), "The generic be-verb must stay visible.");
    }

    [Test]
    public void JumbleWordBank_AddsAuthoredFakeWordAndKeepsAllExpectedWords()
    {
        const string sentence = "I went to the mall yesterday";
        List<string> bank = DialogueSentenceJumble.BuildWordBank(
            sentence,
            GrammarConceptId.SentenceStartEnd,
            GrammarPhrasePattern.FullSentence,
            "mall-route",
            new[] { "when" },
            maximumDistractors: 1);

        CollectionAssert.Contains(bank, "when");
        Assert.AreEqual(7, bank.Count);
        foreach (string expectedWord in DialogueTranscriptTokenizer.Tokenize(sentence))
            Assert.That(bank.Exists(word => SameWord(word, expectedWord)), Is.True, $"Missing '{expectedWord}' from bank.");
    }

    [Test]
    public void JumbleEvaluation_UsesGreenForRightPlaceNeutralForMovedAndGreyForFakeWord()
    {
        const string sentence = "I went to the mall yesterday";
        DialogueJumbleEvaluation moved = DialogueSentenceJumble.Evaluate(
            new[] { "mall", "went", "to", "the", "I", "yesterday" },
            sentence);
        DialogueJumbleEvaluation fake = DialogueSentenceJumble.Evaluate(
            new[] { "I", "went", "when", "the", "mall", "yesterday" },
            sentence);

        Assert.AreEqual(4, moved.correctPositionCount);
        Assert.AreEqual(2, moved.presentElsewhereCount);
        Assert.AreEqual(DialogueJumbleWordState.PresentElsewhere, moved.words[0].state);
        Assert.AreEqual(DialogueJumbleWordState.CorrectPosition, moved.words[1].state);

        Assert.AreEqual(5, fake.correctPositionCount);
        Assert.AreEqual(1, fake.distractorCount);
        Assert.AreEqual("when", fake.words[2].normalizedWord);
        Assert.AreEqual(DialogueJumbleWordState.Distractor, fake.words[2].state);
        Assert.That(DialogueSentenceJumble.BuildFeedback(fake), Does.Contain("grey"));
    }

    [Test]
    public void JumbleEvaluation_RepeatedWordsAreConsumedOnlyOnce()
    {
        const string sentence = "the cat and the dog";
        DialogueJumbleEvaluation validReorder = DialogueSentenceJumble.Evaluate(
            new[] { "the", "the", "and", "cat", "dog" },
            sentence);
        DialogueJumbleEvaluation extraDuplicate = DialogueSentenceJumble.Evaluate(
            new[] { "the", "the", "cat", "and", "the", "dog" },
            sentence);

        Assert.AreEqual(3, validReorder.correctPositionCount);
        Assert.AreEqual(2, validReorder.presentElsewhereCount);
        Assert.AreEqual(0, validReorder.distractorCount);
        Assert.AreEqual(1, extraDuplicate.distractorCount, "A third 'the' cannot consume either expected 'the' twice.");
    }

    [Test]
    public void AnswerChecker_ExposesPositionFeedbackForJumbleAttempt()
    {
        var line = new DialogueLine
        {
            expectedAnswers = new List<string> { "I went to the mall yesterday" },
            jumbledWords = new List<string> { "mall", "I", "yesterday", "went", "when", "the", "to" },
            jumbleDistractorWords = new List<string> { "when" },
        };

        AnswerCheckResult result = new AnswerChecker().CheckAnswer(new AnswerCheckRequest
        {
            line = line,
            taskType = TaskType.SentenceJumble,
            supportLevel = SupportLevel.MediumSupport,
            learnerAnswer = "I went when the mall yesterday",
        });

        Assert.IsFalse(result.isCorrect);
        Assert.NotNull(result.jumbleEvaluation);
        Assert.AreEqual(5, result.jumbleEvaluation.correctPositionCount);
        Assert.AreEqual(1, result.jumbleEvaluation.distractorCount);
    }

    [Test]
    public void GeneratedTownAndRouteDialogue_CarryPlaceAndGrammarContext()
    {
        LocalizedDialogueLine town = NaturalGrammarProgression.BuildGeneratedDialogue(
            SemanticZoneKind.Town,
            "Articles",
            7,
            0,
            trainerBattle: false);
        LocalizedDialogueLine route = NaturalGrammarProgression.BuildGeneratedDialogue(
            SemanticZoneKind.Route,
            "Articles",
            7,
            0,
            trainerBattle: false);

        Assert.AreEqual(SemanticZoneKind.Town, town.zoneKind);
        Assert.AreEqual(SemanticZoneKind.Route, route.zoneKind);
        Assert.That(town.grammarTopic, Does.Contain("Article"));
        Assert.That(route.grammarTopic, Does.Contain("Article"));
        Assert.That(town.contextCue, Does.Contain("in town"));
        Assert.That(route.contextCue, Does.Contain("on the route"));
        Assert.AreNotEqual(town.npcLine, route.npcLine);
        Assert.That(route.grammarFocusWords, Is.Not.Empty);
    }

    [Test]
    public void LocalizedMissingWordMapping_PreservesAuthoredGrammarBlank()
    {
        var source = new LocalizedDialogueLine
        {
            lineId = "article-route",
            conceptId = GrammarConceptId.Articles,
            grammarPattern = GrammarPhrasePattern.DeterminerNoun,
            malfunctionType = GrammarDialogueMalfunctionType.MissingWord,
            npcLine = "The route transcript lost its article: ____ owl.",
            expectedEnglishResponse = "An owl",
            grammarFocusWords = new List<string> { "An" },
        };

        DialogueLine mapped = ContentDatabase.CreateRuntimeDefault()
            .CreateDialogueLineFromLocalized(source, "route-guide", "Route Guide");

        Assert.AreEqual("____ owl.", mapped.fillBlankText);
        CollectionAssert.Contains(mapped.partialAcceptedAnswers, "an");
    }

    [Test]
    public void TranscriptTokenizerAndMeaningCatalog_SupportSpecificWordActions()
    {
        List<string> words = DialogueTranscriptTokenizer.Tokenize("I went to the mall yesterday.");

        CollectionAssert.AreEqual(new[] { "I", "went", "to", "the", "mall", "yesterday" }, words);
        Assert.That(DialogueWordMeaningCatalog.GetMeaning("mall", GrammarConceptId.BasicNouns), Does.Contain("shops"));
        Assert.That(DialogueWordMeaningCatalog.GetMeaning("behind", GrammarConceptId.BasicPrepositions), Does.Contain("back"));
        Assert.That(DialogueWordMeaningCatalog.GetMeaning("roof", GrammarConceptId.BasicPrepositions), Does.Contain("building"));
        Assert.That(DialogueWordMeaningCatalog.GetMeaning("bridge", GrammarConceptId.BasicPrepositions), Does.Contain("over"));
    }

    [Test]
    public void WordInteractionFactory_CanSwapProviders()
    {
        var fake = new FakeWordInteractionService();
        DialogueWordInteractionServiceFactory.OverrideFactory = () => fake;
        try
        {
            IDialogueWordInteractionService service = DialogueWordInteractionServiceFactory.Create();
            service.Speak("mall");

            Assert.AreSame(fake, service);
            Assert.AreEqual("mall", fake.lastSpoken);
            Assert.AreEqual("fake meaning", service.GetMeaning("mall", GrammarConceptId.BasicNouns));
        }
        finally
        {
            DialogueWordInteractionServiceFactory.OverrideFactory = null;
        }
    }

    static bool SameWord(string left, string right) =>
        string.Equals(
            DialogueTranscriptTokenizer.NormalizeWord(left),
            DialogueTranscriptTokenizer.NormalizeWord(right),
            StringComparison.Ordinal);

    sealed class FakeWordInteractionService : IDialogueWordInteractionService
    {
        public string lastSpoken = "";
        public void Speak(string text) => lastSpoken = text ?? "";
        public string GetMeaning(string text, GrammarConceptId conceptId) => "fake meaning";
    }
}
