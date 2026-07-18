using System;
using System.Collections.Generic;
using UnityEngine;

public enum SupportLevel
{
    HighSupport,
    MediumSupport,
    LowSupport,
    AudioOnly,
}

public enum TaskType
{
    FullSentenceListen,
    FillBlank,
    SentenceJumble,
    BlankReconstruction,
    SpokenAnswer,
    WrittenAnswer,
    SpokenAndWrittenAnswer,
}

public enum AnswerErrorType
{
    None,
    EmptyAnswer,
    IncorrectAnswer,
    WrongWordOrder,
    MissingWord,
    InputModeMismatch,
    UnsupportedTask,
}

[Serializable]
public class HintData
{
    [TextArea] public string text = "";
    public bool isLocalLanguageHint;
}

[Serializable]
public class GrammarConcept
{
    public string conceptId = "";
    public string displayName = "";
    [TextArea] public string explanation = "";
    [TextArea] public string localLanguageExplanation = "";
    [Range(1, 5)] public int difficulty = 1;
}

[Serializable]
public class DialogueLine
{
    public string dialogueId = Guid.NewGuid().ToString("N");
    public string npcId = "";
    public string npcName = "";
    public string regionId = "";
    public string grammarTopic = "";
    public SemanticZoneKind zoneKind = SemanticZoneKind.Town;
    public string contextCue = "";
    [TextArea] public string text = "";
    public string conceptId = "";
    public List<string> targetWords = new List<string>();
    public List<string> grammarFocusWords = new List<string>();
    public List<string> grammarTags = new List<string>();
    [Range(1, 5)] public int difficulty = 1;
    public List<TaskType> taskTypes = new List<TaskType>();
    public List<string> expectedAnswers = new List<string>();
    public List<string> allowedAnswers = new List<string>();
    public List<string> partialAcceptedAnswers = new List<string>();
    [TextArea] public string fillBlankText = "";
    public List<string> jumbledWords = new List<string>();
    public List<string> jumbleDistractorWords = new List<string>();
    public List<HintData> hints = new List<HintData>();
    [TextArea] public string localLanguageHint = "";
    public AudioClip audioClip;
    public string audioPathPlaceholder = "";
}

[Serializable]
public class AnswerCheckResult
{
    public bool isCorrect;
    [Range(0f, 1f)] public float score;
    public string feedbackMessage = "";
    public AnswerErrorType detectedErrorType = AnswerErrorType.None;
    public bool shouldShowHint;
    public string matchedAnswer = "";
    public DialogueJumbleEvaluation jumbleEvaluation;
}

[Serializable]
public class TaskPresentation
{
    public TaskType taskType = TaskType.FullSentenceListen;
    public SupportLevel supportLevel = SupportLevel.HighSupport;
    public string promptText = "";
    public string displayText = "";
    public bool audioOnly;
}

[Serializable]
public class ConceptProgress
{
    public string conceptId = "";
    public int attempts;
    public int correctAttempts;
    public int wrongAttempts;
    [Range(0f, 1f)] public float masteryScore;
    public int hintUsageCount;
    public List<string> recentMistakes = new List<string>();
    public SupportLevel lastSupportLevelUsed = SupportLevel.HighSupport;
    public int consecutiveWrongAnswers;
    public bool isWeak;
    public bool isStrong;
}

[Serializable]
public class WordProgressRecord
{
    public string word = "";
    public int attempts;
    public int correctAttempts;
    public int wrongAttempts;
}

[Serializable]
public class TaskTypeProgressRecord
{
    public TaskType taskType;
    public int attempts;
    public int correctAttempts;
    public int wrongAttempts;
}

[Serializable]
public class LearnerProgressSummary
{
    public List<ConceptProgress> concepts = new List<ConceptProgress>();
    public List<WordProgressRecord> words = new List<WordProgressRecord>();
    public List<TaskTypeProgressRecord> taskTypes = new List<TaskTypeProgressRecord>();
}

[Serializable]
public class TeacherFeedbackSummary
{
    public List<string> weakConcepts = new List<string>();
    public List<string> strongConcepts = new List<string>();
    public List<string> recentMistakes = new List<string>();
    [Range(0f, 1f)] public float accuracy;
    public int hintsUsed;
}

[Serializable]
public class DialogueTurnResult
{
    public AnswerCheckResult answerResult = new AnswerCheckResult();
    public HintData hint;
    public ConceptProgress updatedConceptProgress;
    public SupportLevel nextSupportLevel = SupportLevel.HighSupport;
    public bool interactionComplete;
}

[Serializable]
public class DialogueSessionState
{
    public string learnerId = "default";
    public string npcId = "";
    public string npcName = "";
    public DialogueLine line;
    public TaskPresentation presentation = new TaskPresentation();
    public int hintsShown;
    public bool usedHintThisTurn;
    public string sourceDialogueId => line != null ? line.dialogueId : "";
}

public interface ISpeechRecognitionService
{
    bool IsAvailable { get; }
    string ProviderName { get; }
    string CaptureTranscript(string prompt);
}

public interface IAudioPlaybackService
{
    void PlayDialogue(DialogueLine line);
}

public interface ILearnerProgressPersistence
{
    void Save(string learnerId, LearnerProgressSummary summary);
    LearnerProgressSummary Load(string learnerId);
}

[CreateAssetMenu(fileName = "LanguageLearningContentDatabase", menuName = "The Script/Language Learning/Content Database")]
public class ContentDatabase : ScriptableObject
{
    const string DefaultResourcePath = "LanguageContent/LanguageLearningContentDatabase";

    public List<GrammarConcept> grammarConcepts = new List<GrammarConcept>();
    public List<DialogueLine> dialogueLines = new List<DialogueLine>();

    public static ContentDatabase LoadOrCreateDefault()
    {
        ContentDatabase asset = Resources.Load<ContentDatabase>(DefaultResourcePath);
        return asset != null ? asset : CreateRuntimeDefault();
    }

    public static ContentDatabase CreateRuntimeDefault()
    {
        ContentDatabase database = CreateInstance<ContentDatabase>();
        database.name = "Runtime Language Learning Content Database";
        database.grammarConcepts = BuildDefaultConcepts();
        database.dialogueLines = BuildDefaultDialogues();
        return database;
    }

    public bool TryGetDialogue(string dialogueId, out DialogueLine line)
    {
        foreach (DialogueLine candidate in dialogueLines)
        {
            if (candidate != null && string.Equals(candidate.dialogueId, dialogueId, StringComparison.OrdinalIgnoreCase))
            {
                line = candidate;
                return true;
            }
        }

        line = null;
        return false;
    }

    public List<DialogueLine> GetDialoguesForNpc(string npcId)
    {
        var result = new List<DialogueLine>();
        foreach (DialogueLine line in dialogueLines)
        {
            if (line != null && string.Equals(line.npcId, npcId, StringComparison.OrdinalIgnoreCase))
                result.Add(line);
        }
        return result;
    }

    public bool TryGetConcept(string conceptId, out GrammarConcept concept)
    {
        foreach (GrammarConcept candidate in grammarConcepts)
        {
            if (candidate != null && string.Equals(candidate.conceptId, conceptId, StringComparison.OrdinalIgnoreCase))
            {
                concept = candidate;
                return true;
            }
        }

        concept = null;
        return false;
    }

    public void ValidateReadiness(List<ContentValidationIssue> issues, bool productionStrict = false)
    {
        if (issues == null)
            return;

        if (grammarConcepts == null || grammarConcepts.Count == 0)
            issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, "Language learning content has no grammar concepts.", this));
        if (dialogueLines == null || dialogueLines.Count == 0)
            issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, "Language learning content has no dialogue lines.", this));
        else if (productionStrict && dialogueLines.Count < 12)
            issues.Add(new ContentValidationIssue(ContentValidationSeverity.Warning, "Language learning content has fewer than 12 dialogue lines; this is still demo-scale.", this));

        var dialogueIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var conceptIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (grammarConcepts != null)
        {
            for (int i = 0; i < grammarConcepts.Count; i++)
            {
                GrammarConcept concept = grammarConcepts[i];
                if (concept == null)
                {
                    issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Grammar concept entry {i} is null.", this));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(concept.conceptId))
                    issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Grammar concept entry {i} has no id.", this));
                else if (!conceptIds.Add(concept.conceptId.Trim()))
                    issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Grammar concept '{concept.conceptId}' is duplicated.", this));
            }
        }

        if (dialogueLines == null)
            return;

        for (int i = 0; i < dialogueLines.Count; i++)
        {
            DialogueLine line = dialogueLines[i];
            if (line == null)
            {
                issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Dialogue line entry {i} is null.", this));
                continue;
            }

            string id = string.IsNullOrWhiteSpace(line.dialogueId) ? $"entry {i}" : line.dialogueId.Trim();
            if (string.IsNullOrWhiteSpace(line.dialogueId))
                issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Dialogue line entry {i} has no stable id.", this));
            else if (!dialogueIds.Add(line.dialogueId.Trim()))
                issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Dialogue line '{line.dialogueId}' is duplicated.", this));
            if (string.IsNullOrWhiteSpace(line.npcId))
                issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Dialogue line '{id}' has no npcId.", this));
            if (string.IsNullOrWhiteSpace(line.text))
                issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Dialogue line '{id}' has no NPC text.", this));
            if (line.taskTypes == null || line.taskTypes.Count == 0)
                issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Dialogue line '{id}' has no task types.", this));
            if ((line.expectedAnswers == null || line.expectedAnswers.Count == 0) &&
                (line.allowedAnswers == null || line.allowedAnswers.Count == 0))
                issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Dialogue line '{id}' has no accepted answer.", this));
            if (!string.IsNullOrWhiteSpace(line.conceptId) && conceptIds.Count > 0 && !conceptIds.Contains(line.conceptId))
                issues.Add(new ContentValidationIssue(ContentValidationSeverity.Warning, $"Dialogue line '{id}' references unknown concept '{line.conceptId}'.", this));
            if (productionStrict && (line.hints == null || line.hints.Count == 0) && string.IsNullOrWhiteSpace(line.localLanguageHint))
                issues.Add(new ContentValidationIssue(ContentValidationSeverity.Warning, $"Dialogue line '{id}' has no hint support.", this));
        }
    }

    public DialogueLine CreateDialogueLineFromLocalized(LocalizedDialogueLine source, string npcId, string npcName)
    {
        if (source == null)
            return null;

        var mapped = new DialogueLine
        {
            dialogueId = string.IsNullOrWhiteSpace(source.dialogueTaskId) ? source.lineId : source.dialogueTaskId,
            npcId = npcId ?? "",
            npcName = npcName ?? "",
            regionId = source.regionId ?? "",
            grammarTopic = source.grammarTopic ?? "",
            zoneKind = source.zoneKind,
            contextCue = source.contextCue ?? "",
            text = string.IsNullOrWhiteSpace(source.npcLine) ? source.sourceText ?? "" : source.npcLine,
            conceptId = source.conceptId != GrammarConceptId.None ? source.conceptId.ToString() : "",
            difficulty = 1,
            fillBlankText = source.malfunctionType == GrammarDialogueMalfunctionType.MissingWord
                ? ResolveGrammarFillBlank(source)
                : "",
            localLanguageHint = source.localLanguageHint ?? "",
            audioClip = source.cachedSpeech,
            audioPathPlaceholder = string.IsNullOrWhiteSpace(source.providerName) ? "" : source.providerName,
        };
        if (source.grammarFocusWords != null)
            mapped.grammarFocusWords.AddRange(source.grammarFocusWords);
        if (source.jumbleDistractorWords != null)
            mapped.jumbleDistractorWords.AddRange(source.jumbleDistractorWords);

        GrammarDialogueInputMode resolvedInputMode = source.malfunctionType == GrammarDialogueMalfunctionType.HeardWrong
            ? GrammarDialogueInputMode.SpeakOnly
            : source.inputMode;
        mapped.taskTypes.Add(MapTaskType(resolvedInputMode, source.malfunctionType));
        AddUnique(mapped.expectedAnswers, source.expectedEnglishResponse);
        if (source.acceptedEnglishResponses != null)
        {
            foreach (string response in source.acceptedEnglishResponses)
                AddUnique(mapped.allowedAnswers, response);
        }

        if (source.malfunctionType == GrammarDialogueMalfunctionType.MissingWord)
        {
            List<string> blankAnswers = ExtractMissingWordAnswers(source.expectedEnglishResponse, mapped.fillBlankText);
            foreach (string answer in blankAnswers)
                AddUnique(mapped.partialAcceptedAnswers, answer);
        }

        if (source.malfunctionType == GrammarDialogueMalfunctionType.ScrambledSentence)
        {
            foreach (string token in DialogueSentenceJumble.BuildWordBank(
                         source.expectedEnglishResponse,
                         source.conceptId,
                         source.grammarPattern,
                         source.lineId,
                         source.jumbleDistractorWords,
                         maximumDistractors: 1))
                mapped.jumbledWords.Add(token);
        }

        if (!string.IsNullOrWhiteSpace(source.teachingNote))
            mapped.hints.Add(new HintData { text = source.teachingNote });
        if (!string.IsNullOrWhiteSpace(source.localLanguageHint))
            mapped.hints.Add(new HintData { text = source.localLanguageHint, isLocalLanguageHint = true });
        if (mapped.expectedAnswers.Count == 0 && mapped.allowedAnswers.Count == 0)
            AddUnique(mapped.expectedAnswers, "I understand");
        return mapped;
    }

    static string ResolveGrammarFillBlank(LocalizedDialogueLine source)
    {
        if (source == null)
            return "";

        string authored = DialogueFillInBlankScaffold.ExtractAuthoredTemplate(
            string.IsNullOrWhiteSpace(source.npcLine) ? source.sourceText : source.npcLine);
        if (!string.IsNullOrWhiteSpace(authored))
            return authored;

        return DialogueFillInBlankScaffold.Build(
            source.expectedEnglishResponse ?? "",
            source.conceptId,
            source.grammarPattern,
            source.lineId,
            source.grammarFocusWords);
    }

    static TaskType MapTaskType(GrammarDialogueInputMode inputMode, GrammarDialogueMalfunctionType malfunctionType)
    {
        switch (malfunctionType)
        {
            case GrammarDialogueMalfunctionType.MissingWord:
                return TaskType.FillBlank;
            case GrammarDialogueMalfunctionType.ScrambledSentence:
                return TaskType.SentenceJumble;
            case GrammarDialogueMalfunctionType.PartialTranscript:
                return TaskType.BlankReconstruction;
        }

        switch (inputMode)
        {
            case GrammarDialogueInputMode.SpeakOnly:
                return TaskType.SpokenAnswer;
            case GrammarDialogueInputMode.SpeakAndWrite:
                return TaskType.SpokenAndWrittenAnswer;
            default:
                return TaskType.WrittenAnswer;
        }
    }

    static List<string> ExtractMissingWordAnswers(string expectedEnglishResponse, string fillBlankText)
    {
        var result = new List<string>();
        List<string> expectedTokens = AnswerChecker.Tokenize(expectedEnglishResponse);
        List<string> promptTokens = AnswerChecker.Tokenize(fillBlankText.Replace("____", "BLANK"));
        if (expectedTokens.Count == 0 || promptTokens.Count == 0 || expectedTokens.Count != promptTokens.Count)
            return result;

        for (int i = 0; i < expectedTokens.Count; i++)
        {
            if (promptTokens[i] == "blank")
                AddUnique(result, expectedTokens[i]);
        }

        return result;
    }

    static List<GrammarConcept> BuildDefaultConcepts()
    {
        return new List<GrammarConcept>
        {
            new GrammarConcept
            {
                conceptId = "possessive_my",
                displayName = "Possessive My",
                explanation = "Use my when something belongs to you.",
                localLanguageExplanation = "Use 'my' to show ownership.",
                difficulty = 1,
            },
            new GrammarConcept
            {
                conceptId = "color_question",
                displayName = "Color Questions",
                explanation = "Answer a color question with a short or full English response.",
                localLanguageExplanation = "Answer the question by naming the color.",
                difficulty = 1,
            },
            new GrammarConcept
            {
                conceptId = "preposition_under",
                displayName = "Preposition Under",
                explanation = "Use under to show that something is below something else.",
                localLanguageExplanation = "Under shows a lower position.",
                difficulty = 2,
            },
            new GrammarConcept
            {
                conceptId = "sentence_ordering",
                displayName = "Sentence Ordering",
                explanation = "Put words back into the correct order.",
                localLanguageExplanation = "Arrange the sentence in the right order.",
                difficulty = 2,
            },
            new GrammarConcept
            {
                conceptId = "basic_noun",
                displayName = "Basic Noun",
                explanation = "A noun names a person, place, animal, or thing.",
                localLanguageExplanation = "A noun is a naming word.",
                difficulty = 1,
            },
        };
    }

    static List<DialogueLine> BuildDefaultDialogues()
    {
        return new List<DialogueLine>
        {
            new DialogueLine
            {
                dialogueId = "town-guard-my-town",
                npcId = "town_guard",
                npcName = "Town Guard",
                text = "This is my town.",
                conceptId = "possessive_my",
                targetWords = new List<string> { "this", "is", "my", "town" },
                grammarTags = new List<string> { "demonstrative", "be_verb", "possessive", "noun" },
                difficulty = 1,
                taskTypes = new List<TaskType>
                {
                    TaskType.FullSentenceListen,
                    TaskType.FillBlank,
                    TaskType.SentenceJumble,
                    TaskType.BlankReconstruction,
                },
                expectedAnswers = new List<string> { "This is my town" },
                allowedAnswers = new List<string> { "this is my town" },
                partialAcceptedAnswers = new List<string> { "my" },
                fillBlankText = "This is __ town.",
                jumbledWords = new List<string> { "town", "your", "is", "my", "This" },
                jumbleDistractorWords = new List<string> { "your" },
                hints = new List<HintData>
                {
                    new HintData { text = "Listen to the word before town." },
                    new HintData { text = "The missing word shows ownership." },
                    new HintData { text = "Use 'my' when something belongs to you." },
                },
                localLanguageHint = "'my' means mera/meri.",
                audioPathPlaceholder = "npc/town_guard/this_is_my_town",
            },
            new DialogueLine
            {
                dialogueId = "tailor-color-question",
                npcId = "tailor",
                npcName = "Tailor",
                text = "What color is your dress?",
                conceptId = "color_question",
                targetWords = new List<string> { "what", "color", "dress", "red" },
                grammarTags = new List<string> { "question", "color", "possessive" },
                difficulty = 1,
                taskTypes = new List<TaskType> { TaskType.SpokenAnswer, TaskType.WrittenAnswer },
                expectedAnswers = new List<string> { "My dress is red" },
                allowedAnswers = new List<string> { "It is red", "red" },
                partialAcceptedAnswers = new List<string> { "red" },
                hints = new List<HintData>
                {
                    new HintData { text = "Answer with the color you hear in the scene." },
                    new HintData { text = "You can start with 'It is...' or name the dress." },
                },
                localLanguageHint = "Name the color in English.",
                audioPathPlaceholder = "npc/tailor/what_color_is_your_dress",
            },
            new DialogueLine
            {
                dialogueId = "guide-under-table",
                npcId = "guide",
                npcName = "Guide",
                text = "The cat is under the table.",
                conceptId = "preposition_under",
                targetWords = new List<string> { "cat", "under", "table" },
                grammarTags = new List<string> { "noun", "preposition", "location" },
                difficulty = 2,
                taskTypes = new List<TaskType> { TaskType.FillBlank, TaskType.SentenceJumble },
                expectedAnswers = new List<string> { "The cat is under the table" },
                allowedAnswers = new List<string> { "the cat is under the table" },
                partialAcceptedAnswers = new List<string> { "under" },
                fillBlankText = "The cat is __ the table.",
                jumbledWords = new List<string> { "table", "above", "the", "under", "is", "cat", "The" },
                jumbleDistractorWords = new List<string> { "above" },
                hints = new List<HintData>
                {
                    new HintData { text = "Think about the location word between 'is' and 'the table'." },
                    new HintData { text = "The cat is below the table, not on it." },
                },
                localLanguageHint = "Use the English word for 'below'.",
                audioPathPlaceholder = "npc/guide/the_cat_is_under_the_table",
            },
        };
    }

    static void AddUnique(List<string> values, string value)
    {
        if (values == null)
            return;

        string normalized = AnswerChecker.Normalize(value);
        if (!string.IsNullOrWhiteSpace(normalized) && !values.Exists(existing => AnswerChecker.Normalize(existing) == normalized))
            values.Add(value.Trim());
    }
}

[DisallowMultipleComponent]
public class DialogueManager : MonoBehaviour
{
    public ContentDatabase contentDatabase;
    public MonoBehaviour speechRecognitionComponent;
    public MonoBehaviour audioPlaybackComponent;
    public MonoBehaviour persistenceComponent;

    readonly SupportLevelManager supportLevelManager = new SupportLevelManager();
    readonly HintManager hintManager = new HintManager();
    readonly AnswerChecker answerChecker = new AnswerChecker();

    GrammarConceptManager conceptManager;
    ProgressTracker progressTracker;
    EnergyManager energyManager;
    TeacherFeedbackManager teacherFeedbackManager;
    ISpeechRecognitionService speechRecognitionService;
    IAudioPlaybackService audioPlaybackService;
    ILearnerProgressPersistence persistenceService;

    DialogueSessionState currentSession;

    public ProgressTracker ProgressTracker => progressTracker;
    public DialogueSessionState CurrentSession => currentSession;
    public EnergyManager EnergyManager => energyManager;
    public TeacherFeedbackManager TeacherFeedbackManager => teacherFeedbackManager;
    public ContentDatabase ActiveContentDatabase
    {
        get
        {
            EnsureInitialized();
            return contentDatabase;
        }
    }

    void Awake()
    {
        EnsureInitialized();
    }

    public void ConfigureForTests(ContentDatabase database)
    {
        contentDatabase = database;
        EnsureInitialized(forceRefresh: true);
    }

    public DialogueSessionState StartInteraction(string npcId, string learnerId = "default")
    {
        EnsureInitialized();
        List<DialogueLine> dialogues = contentDatabase.GetDialoguesForNpc(npcId);
        if (dialogues.Count == 0)
            return null;

        return StartInteractionWithLine(dialogues[0], learnerId);
    }

    public DialogueSessionState StartInteractionWithLine(DialogueLine line, string learnerId = "default")
    {
        EnsureInitialized();
        if (line == null)
            return null;
        if ((line.expectedAnswers == null || line.expectedAnswers.Count == 0) &&
            (line.allowedAnswers == null || line.allowedAnswers.Count == 0))
            AddDefaultAcceptedAnswer(line);
        if (line.taskTypes == null || line.taskTypes.Count == 0)
            line.taskTypes = new List<TaskType> { TaskType.WrittenAnswer };
        if (!energyManager.TryConsumeTaskEnergy())
            return null;

        ConceptProgress progress = progressTracker.GetConceptProgress(line.conceptId);
        SupportLevel supportLevel = supportLevelManager.DetermineSupportLevel(progress);
        TaskPresentation presentation = BuildPresentation(line, supportLevel);
        currentSession = new DialogueSessionState
        {
            learnerId = learnerId,
            npcId = line.npcId,
            npcName = line.npcName,
            line = line,
            presentation = presentation,
        };
        audioPlaybackService?.PlayDialogue(line);

        return currentSession;
    }

    public DialogueTurnResult SubmitCapturedSpeechAnswer()
    {
        EnsureInitialized();
        if (currentSession == null)
        {
            return new DialogueTurnResult
            {
                answerResult = new AnswerCheckResult
                {
                    feedbackMessage = "No interaction is active.",
                    detectedErrorType = AnswerErrorType.UnsupportedTask,
                },
            };
        }

        string transcript = speechRecognitionService != null && speechRecognitionService.IsAvailable
            ? speechRecognitionService.CaptureTranscript(currentSession.presentation.promptText)
            : "";
        return SubmitAnswer(transcript);
    }

    public DialogueTurnResult SubmitAnswer(string learnerAnswer)
    {
        EnsureInitialized();
        if (currentSession == null || currentSession.line == null)
        {
            return new DialogueTurnResult
            {
                answerResult = new AnswerCheckResult
                {
                    feedbackMessage = "No interaction is active.",
                    detectedErrorType = AnswerErrorType.UnsupportedTask,
                },
            };
        }

        var request = new AnswerCheckRequest
        {
            line = currentSession.line,
            learnerAnswer = learnerAnswer,
            supportLevel = currentSession.presentation.supportLevel,
            taskType = currentSession.presentation.taskType,
        };
        AnswerCheckResult result = answerChecker.CheckAnswer(request);
        ConceptProgress updated = progressTracker.RecordAttempt(
            currentSession.line,
            currentSession.presentation.taskType,
            currentSession.presentation.supportLevel,
            result,
            currentSession.usedHintThisTurn);

        HintData hint = null;
        if (!result.isCorrect && result.shouldShowHint)
        {
            hint = hintManager.GetNextHint(currentSession.line, currentSession.hintsShown);
            if (hint != null)
            {
                currentSession.hintsShown++;
                currentSession.usedHintThisTurn = true;
                progressTracker.RecordHintUsed(currentSession.line.conceptId);
            }
        }

        SupportLevel nextSupport = supportLevelManager.DetermineSupportLevel(updated);
        if (result.isCorrect)
            currentSession = null;

        return new DialogueTurnResult
        {
            answerResult = result,
            hint = hint,
            updatedConceptProgress = updated,
            nextSupportLevel = nextSupport,
            interactionComplete = result.isCorrect,
        };
    }

    public HintData RequestNextHint()
    {
        if (currentSession == null || currentSession.line == null)
            return null;

        HintData hint = hintManager.GetNextHint(currentSession.line, currentSession.hintsShown);
        if (hint == null)
            return null;

        currentSession.hintsShown++;
        currentSession.usedHintThisTurn = true;
        progressTracker.RecordHintUsed(currentSession.line.conceptId);
        return hint;
    }

    public SupportLevel PredictNextSupportLevel(string conceptId)
    {
        EnsureInitialized();
        return supportLevelManager.DetermineSupportLevel(progressTracker.GetConceptProgress(conceptId));
    }

    public TeacherFeedbackSummary BuildTeacherFeedbackSummary()
    {
        EnsureInitialized();
        return teacherFeedbackManager.BuildSummary(progressTracker);
    }

    public void SaveProgress(string learnerId = "default")
    {
        EnsureInitialized();
        progressTracker.Save(learnerId);
    }

    TaskPresentation BuildPresentation(DialogueLine line, SupportLevel supportLevel)
    {
        TaskType taskType = ResolveTaskType(line, supportLevel);
        return new TaskPresentation
        {
            taskType = taskType,
            supportLevel = supportLevel,
            promptText = line != null ? line.text : "",
            displayText = BuildDisplayText(line, taskType, supportLevel),
            audioOnly = supportLevel == SupportLevel.AudioOnly,
        };
    }

    static TaskType ResolveTaskType(DialogueLine line, SupportLevel supportLevel)
    {
        if (line == null || line.taskTypes == null || line.taskTypes.Count == 0)
            return TaskType.FullSentenceListen;

        switch (supportLevel)
        {
            case SupportLevel.HighSupport:
                if (line.taskTypes.Contains(TaskType.FullSentenceListen))
                    return TaskType.FullSentenceListen;
                break;
            case SupportLevel.MediumSupport:
                if (line.taskTypes.Contains(TaskType.FillBlank))
                    return TaskType.FillBlank;
                if (line.taskTypes.Contains(TaskType.SentenceJumble))
                    return TaskType.SentenceJumble;
                break;
            case SupportLevel.LowSupport:
                if (line.taskTypes.Contains(TaskType.BlankReconstruction))
                    return TaskType.BlankReconstruction;
                if (line.taskTypes.Contains(TaskType.SpokenAndWrittenAnswer))
                    return TaskType.SpokenAndWrittenAnswer;
                if (line.taskTypes.Contains(TaskType.WrittenAnswer))
                    return TaskType.WrittenAnswer;
                break;
            case SupportLevel.AudioOnly:
                if (line.taskTypes.Contains(TaskType.SpokenAndWrittenAnswer))
                    return TaskType.SpokenAndWrittenAnswer;
                if (line.taskTypes.Contains(TaskType.SpokenAnswer))
                    return TaskType.SpokenAnswer;
                break;
        }

        return line.taskTypes[0];
    }

    static string BuildDisplayText(DialogueLine line, TaskType taskType, SupportLevel supportLevel)
    {
        if (line == null)
            return "";

        switch (taskType)
        {
            case TaskType.FillBlank:
                return string.IsNullOrWhiteSpace(line.fillBlankText) ? line.text : line.fillBlankText;
            case TaskType.SentenceJumble:
                return line.jumbledWords != null && line.jumbledWords.Count > 0
                    ? string.Join(" ", line.jumbledWords)
                    : ScrambleWords(line.text);
            case TaskType.BlankReconstruction:
                return BuildBlankReconstructionText(line.text);
            case TaskType.SpokenAnswer:
            case TaskType.WrittenAnswer:
            case TaskType.SpokenAndWrittenAnswer:
                return supportLevel == SupportLevel.AudioOnly ? "[Audio Only]" : line.text;
            case TaskType.FullSentenceListen:
            default:
                return line.text;
        }
    }

    static string BuildBlankReconstructionText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        string[] words = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            string cleaned = words[i].Trim(',', '.', '!', '?');
            words[i] = new string('_', Mathf.Max(3, cleaned.Length));
        }
        return string.Join(" ", words);
    }

    static string ScrambleWords(string value)
    {
        List<string> tokens = AnswerChecker.Tokenize(value);
        if (tokens.Count <= 1)
            return value ?? "";

        string first = tokens[0];
        tokens.RemoveAt(0);
        tokens.Add(first);
        return string.Join(" ", tokens);
    }

    void EnsureInitialized(bool forceRefresh = false)
    {
        if (contentDatabase == null || forceRefresh)
            contentDatabase = contentDatabase != null && !forceRefresh ? contentDatabase : ContentDatabase.LoadOrCreateDefault();
        if (forceRefresh || speechRecognitionService == null)
            speechRecognitionService = speechRecognitionComponent as ISpeechRecognitionService;
        if (forceRefresh || audioPlaybackService == null)
            audioPlaybackService = audioPlaybackComponent as IAudioPlaybackService;
        if (forceRefresh || persistenceService == null)
            persistenceService = persistenceComponent as ILearnerProgressPersistence;
        if (progressTracker == null || forceRefresh)
            progressTracker = new ProgressTracker(persistenceService);
        if (energyManager == null || forceRefresh)
            energyManager = new EnergyManager();
        if (teacherFeedbackManager == null || forceRefresh)
            teacherFeedbackManager = new TeacherFeedbackManager();
        if (conceptManager == null || forceRefresh)
            conceptManager = new GrammarConceptManager(contentDatabase);
    }

    static void AddDefaultAcceptedAnswer(DialogueLine line)
    {
        if (line == null)
            return;
        line.expectedAnswers ??= new List<string>();
        line.expectedAnswers.Add("I understand");
    }
}

[DisallowMultipleComponent]
public class NPCInteractionManager : MonoBehaviour
{
    public DialogueManager dialogueManager;

    void Awake()
    {
        dialogueManager ??= GetComponent<DialogueManager>() ?? FindAnyObjectByType<DialogueManager>();
        if (dialogueManager == null)
            dialogueManager = gameObject.AddComponent<DialogueManager>();
    }

    public DialogueSessionState StartDialogue(string npcId, string learnerId = "default")
    {
        return dialogueManager != null ? dialogueManager.StartInteraction(npcId, learnerId) : null;
    }

    public DialogueSessionState StartLocalizedDialogue(string npcId, string npcName, LocalizedDialogueLine line, string learnerId = "default")
    {
        dialogueManager ??= GetComponent<DialogueManager>() ?? FindAnyObjectByType<DialogueManager>();
        if (dialogueManager == null)
            dialogueManager = gameObject.AddComponent<DialogueManager>();
        if (line == null)
            return null;

        ContentDatabase database = dialogueManager.ActiveContentDatabase;
        DialogueLine mapped = database.CreateDialogueLineFromLocalized(line, npcId, npcName);
        return dialogueManager.StartInteractionWithLine(mapped, learnerId);
    }
}

[DisallowMultipleComponent]
public class LanguageLearningDemoRunner : MonoBehaviour
{
    public bool runOnStart = true;
    public DialogueManager dialogueManager;

    void Start()
    {
        if (runOnStart)
            RunDemo();
    }

    [ContextMenu("Run Language Learning Demo")]
    public void RunDemo()
    {
        dialogueManager ??= GetComponent<DialogueManager>();
        if (dialogueManager == null)
            dialogueManager = gameObject.AddComponent<DialogueManager>();

        DialogueSessionState session = dialogueManager.StartInteraction("town_guard");
        if (session == null)
        {
            Debug.LogWarning("[LanguageLearningDemo] Failed to start demo session.");
            return;
        }

        Debug.Log($"[LanguageLearningDemo] NPC: {session.npcName}");
        Debug.Log($"[LanguageLearningDemo] Line: {session.line.text}");
        Debug.Log($"[LanguageLearningDemo] Support level: {session.presentation.supportLevel}");
        Debug.Log($"[LanguageLearningDemo] Task type: {session.presentation.taskType}");
        Debug.Log($"[LanguageLearningDemo] Display text: {session.presentation.displayText}");

        DialogueTurnResult wrongTurn = dialogueManager.SubmitAnswer("This is your town");
        Debug.Log($"[LanguageLearningDemo] Wrong answer correct? {wrongTurn.answerResult.isCorrect}");
        Debug.Log($"[LanguageLearningDemo] Feedback: {wrongTurn.answerResult.feedbackMessage}");
        if (wrongTurn.hint != null)
            Debug.Log($"[LanguageLearningDemo] Hint 1: {wrongTurn.hint.text}");

        session = dialogueManager.StartInteraction("town_guard");
        DialogueTurnResult correctTurn = dialogueManager.SubmitAnswer("This is my town");
        Debug.Log($"[LanguageLearningDemo] Correct answer correct? {correctTurn.answerResult.isCorrect}");
        Debug.Log($"[LanguageLearningDemo] Mastery: {correctTurn.updatedConceptProgress.masteryScore:0.00}");
        Debug.Log($"[LanguageLearningDemo] Next support: {correctTurn.nextSupportLevel}");
    }
}
