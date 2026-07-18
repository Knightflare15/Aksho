using System;
using System.Collections.Generic;
using UnityEngine;

public static class GeneratedGrammarExerciseBank
{
    const string ResourcePath = "Grammar/generated-dialogue-tasks";

    static List<GeneratedGrammarDialogueTask> cachedTasks;

    public static IReadOnlyList<GeneratedGrammarDialogueTask> Tasks
    {
        get
        {
            EnsureLoaded();
            return cachedTasks;
        }
    }

    public static void AddTo(Dictionary<string, GrammarDialogueTaskDefinition> tasks)
    {
        if (tasks == null)
            return;

        EnsureLoaded();
        foreach (GeneratedGrammarDialogueTask source in cachedTasks)
        {
            GrammarDialogueTaskDefinition task = source.ToTaskDefinition();
            if (task != null && !string.IsNullOrWhiteSpace(task.taskId))
                tasks[task.taskId] = task;
        }
    }

    public static string[] GetTaskIds(GrammarConceptId conceptId, SemanticZoneKind zoneKind)
    {
        EnsureLoaded();
        var ids = new List<string>();
        foreach (GeneratedGrammarDialogueTask task in cachedTasks)
        {
            if (task == null || string.IsNullOrWhiteSpace(task.id))
                continue;
            if (!TryParseEnum(task.conceptId, GrammarConceptId.None, out GrammarConceptId taskConcept) || taskConcept != conceptId)
                continue;
            if (!TryParseEnum(task.zoneKind, SemanticZoneKind.Town, out SemanticZoneKind taskZone) || taskZone != zoneKind)
                continue;
            ids.Add(task.id);
        }

        return ids.ToArray();
    }

    static void EnsureLoaded()
    {
        if (cachedTasks != null)
            return;

        cachedTasks = new List<GeneratedGrammarDialogueTask>();
        TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
        if (asset == null || string.IsNullOrWhiteSpace(asset.text))
            return;

        GeneratedGrammarDialogueTaskFile file = JsonUtility.FromJson<GeneratedGrammarDialogueTaskFile>(asset.text);
        if (file?.tasks == null)
            return;

        foreach (GeneratedGrammarDialogueTask task in file.tasks)
        {
            if (task != null && !string.IsNullOrWhiteSpace(task.id))
                cachedTasks.Add(task);
        }
    }

    static bool TryParseEnum<TEnum>(string value, TEnum fallback, out TEnum parsed)
        where TEnum : struct
    {
        if (!string.IsNullOrWhiteSpace(value) && Enum.TryParse(value.Trim(), true, out parsed))
            return true;

        parsed = fallback;
        return false;
    }

    [Serializable]
    sealed class GeneratedGrammarDialogueTaskFile
    {
        public List<GeneratedGrammarDialogueTask> tasks = new List<GeneratedGrammarDialogueTask>();
    }
}

[Serializable]
public sealed class GeneratedGrammarDialogueTask
{
    public string id = "";
    public string taskId = "";
    public string lineId = "";
    public string conceptId = "";
    public string zoneKind = "";
    public string contextCue = "";
    [TextArea] public string npcLine = "";
    [TextArea] public string englishText = "";
    [TextArea] public string expectedResponse = "";
    public List<string> acceptedResponses = new List<string>();
    public List<string> alternatives = new List<string>();
    public List<string> grammarFocusWords = new List<string>();
    public List<string> jumbleDistractorWords = new List<string>();
    public string grammarPattern = "";
    public string assistMode = "";
    public string inputMode = "";
    public string malfunctionType = "";
    [TextArea] public string teachingNote = "";
    [TextArea] public string localLanguageHint = "";

    public GrammarDialogueTaskDefinition ToTaskDefinition()
    {
        string resolvedId = string.IsNullOrWhiteSpace(taskId) ? id : taskId;
        if (string.IsNullOrWhiteSpace(resolvedId))
            return null;

        var accepted = new List<string>();
        AddAccepted(accepted, expectedResponse);
        if (acceptedResponses != null)
        {
            foreach (string response in acceptedResponses)
                AddAccepted(accepted, response);
        }

        if (alternatives != null)
        {
            foreach (string response in alternatives)
                AddAccepted(accepted, response);
        }

        GrammarConceptId resolvedConcept = ParseEnum(conceptId, GrammarConceptId.None);
        TranslatorAssistMode resolvedAssist = ParseEnum(assistMode, TranslatorAssistMode.Full);
        GrammarDialogueMalfunctionType resolvedMalfunction = ParseEnum(malfunctionType, GrammarDialogueMalfunctionType.None);
        GrammarDialogueInputMode resolvedInputMode = ParseEnum(inputMode, GrammarDialogueInputMode.SpeakOrWrite);
        if (resolvedMalfunction == GrammarDialogueMalfunctionType.HeardWrong)
            resolvedInputMode = GrammarDialogueInputMode.SpeakOnly;

        return new GrammarDialogueTaskDefinition
        {
            taskId = resolvedId,
            conceptId = resolvedConcept,
            subskillId = resolvedId,
            contextCue = contextCue,
            npcLine = string.IsNullOrWhiteSpace(npcLine) ? englishText : npcLine,
            expectedResponse = expectedResponse,
            acceptedResponses = accepted,
            grammarFocusWords = grammarFocusWords != null && grammarFocusWords.Count > 0
                ? new List<string>(grammarFocusWords)
                : DialogueFillInBlankScaffold.InferFocusWords(
                    expectedResponse,
                    resolvedConcept,
                    ParseEnum(grammarPattern, GrammarPhrasePattern.FullSentence)),
            jumbleDistractorWords = jumbleDistractorWords != null
                ? new List<string>(jumbleDistractorWords)
                : new List<string>(),
            grammarPattern = ParseEnum(grammarPattern, GrammarPhrasePattern.FullSentence),
            assistMode = resolvedAssist,
            inputMode = resolvedInputMode,
            malfunctionType = resolvedMalfunction,
            scaffoldMode = ResolveScaffoldMode(resolvedAssist, resolvedMalfunction),
            buddyUseCase = ResolveBuddyUseCase(resolvedAssist),
            allowAiHint = resolvedAssist != TranslatorAssistMode.Off,
            openGrimoireOnWrongAnswer = resolvedAssist != TranslatorAssistMode.Off,
            teachingNote = teachingNote,
            localLanguageHint = localLanguageHint,
        };
    }

    static TEnum ParseEnum<TEnum>(string value, TEnum fallback)
        where TEnum : struct
    {
        return !string.IsNullOrWhiteSpace(value) && Enum.TryParse(value.Trim(), true, out TEnum parsed)
            ? parsed
            : fallback;
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

    static void AddAccepted(List<string> accepted, string response)
    {
        if (accepted == null || string.IsNullOrWhiteSpace(response))
            return;

        string normalized = response.Trim();
        if (!accepted.Contains(normalized))
            accepted.Add(normalized);
    }
}
