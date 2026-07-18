using System;
using System.Collections.Generic;

/// <summary>
/// Produces a small, deterministic answer bank for NPC speech tasks. The
/// learner selects and previews one known phrase before hold-to-talk begins.
/// </summary>
public static class NpcSpokenAnswerChoiceBuilder
{
    static readonly Dictionary<GrammarConceptId, List<string>> conceptDistractors =
        new Dictionary<GrammarConceptId, List<string>>();
    static readonly Dictionary<string, List<string>> patternDistractors =
        new Dictionary<string, List<string>>(StringComparer.Ordinal);
    static bool distractorIndexBuilt;

    public static List<string> Build(LocalizedDialogueLine line, int maximum = 3)
    {
        var choices = new List<string>();
        if (line == null || !line.useSpokenAnswerChoices || string.IsNullOrWhiteSpace(line.expectedEnglishResponse))
            return choices;

        AddUnique(choices, line.expectedEnglishResponse);
        if (line.spokenAnswerChoices != null)
            foreach (string authored in line.spokenAnswerChoices)
                AddUnique(choices, authored);

        EnsureDistractorIndex();
        var distractors = new List<string>();
        if (patternDistractors.TryGetValue(PatternKey(line.conceptId, line.grammarPattern), out List<string> exactPattern))
        {
            foreach (string candidate in exactPattern)
                AddUnique(distractors, candidate);
        }
        if (conceptDistractors.TryGetValue(line.conceptId, out List<string> sameConcept))
        {
            foreach (string candidate in sameConcept)
                AddUnique(distractors, candidate);
        }

        int seed = StableSeed(string.IsNullOrWhiteSpace(line.dialogueTaskId) ? line.lineId : line.dialogueTaskId);
        for (int offset = 0; distractors.Count > 0 && choices.Count < Math.Max(2, maximum); offset++)
        {
            string candidate = distractors[(seed + offset) % distractors.Count];
            AddUnique(choices, candidate);
            if (offset > distractors.Count * 2) break;
        }

        if (choices.Count <= 1)
            return choices;

        while (choices.Count > maximum)
            choices.RemoveAt(choices.Count - 1);

        // Keep option positions from revealing the answer while remaining
        // deterministic for screenshots, tests, and repeated attempts.
        int correctIndex = seed % choices.Count;
        string correct = choices[0];
        choices.RemoveAt(0);
        choices.Insert(correctIndex, correct);
        return choices;
    }

    static void EnsureDistractorIndex()
    {
        if (distractorIndexBuilt)
            return;

        distractorIndexBuilt = true;
        foreach (GrammarDialogueTaskDefinition task in NaturalGrammarProgression.DialogueTasks.Values)
        {
            if (task == null || string.IsNullOrWhiteSpace(task.expectedResponse))
                continue;

            if (!conceptDistractors.TryGetValue(task.conceptId, out List<string> conceptValues))
            {
                conceptValues = new List<string>();
                conceptDistractors[task.conceptId] = conceptValues;
            }
            AddUnique(conceptValues, task.expectedResponse);

            string patternKey = PatternKey(task.conceptId, task.grammarPattern);
            if (!patternDistractors.TryGetValue(patternKey, out List<string> patternValues))
            {
                patternValues = new List<string>();
                patternDistractors[patternKey] = patternValues;
            }
            AddUnique(patternValues, task.expectedResponse);
        }
    }

    static string PatternKey(GrammarConceptId conceptId, GrammarPhrasePattern pattern) =>
        $"{(int)conceptId}:{(int)pattern}";

    static void AddUnique(List<string> values, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        string candidate = value.Trim();
        string normalized = VoiceUnlockRecognizer.NormalizeKeyword(candidate);
        foreach (string existing in values)
            if (VoiceUnlockRecognizer.NormalizeKeyword(existing) == normalized)
                return;
        values.Add(candidate);
    }

    static int StableSeed(string value)
    {
        unchecked
        {
            int hash = 17;
            foreach (char c in value ?? "") hash = hash * 31 + c;
            return hash == int.MinValue ? 0 : Math.Abs(hash);
        }
    }
}
