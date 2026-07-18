using System;
using System.Collections.Generic;
using UnityEngine;

public enum DialogueJumbleWordState
{
    Unused,
    CorrectPosition,
    PresentElsewhere,
    Distractor,
}

[Serializable]
public sealed class DialogueJumbleWordFeedback
{
    public string word = "";
    public string normalizedWord = "";
    public int submittedIndex = -1;
    public DialogueJumbleWordState state = DialogueJumbleWordState.Unused;
}

[Serializable]
public sealed class DialogueJumbleEvaluation
{
    public bool isCorrect;
    public int expectedWordCount;
    public int submittedWordCount;
    public int correctPositionCount;
    public int presentElsewhereCount;
    public int distractorCount;
    public List<DialogueJumbleWordFeedback> words = new List<DialogueJumbleWordFeedback>();
}

/// <summary>Builds distractor-aware word banks and Wordle-like position feedback.</summary>
public static class DialogueSentenceJumble
{
    static readonly Dictionary<GrammarConceptId, string[]> DefaultDistractors =
        new Dictionary<GrammarConceptId, string[]>
        {
            { GrammarConceptId.Greetings, new[] { "when", "because" } },
            { GrammarConceptId.Alphabet, new[] { "Z", "and" } },
            { GrammarConceptId.VowelsConsonants, new[] { "Y", "and" } },
            { GrammarConceptId.SentenceStartEnd, new[] { "when", "because" } },
            { GrammarConceptId.BasicNouns, new[] { "quickly", "when" } },
            { GrammarConceptId.BasicVerbs, new[] { "when", "thing" } },
            { GrammarConceptId.Articles, new[] { "when", "very" } },
            { GrammarConceptId.Pronouns, new[] { "the", "when" } },
            { GrammarConceptId.Plurals, new[] { "one", "when" } },
            { GrammarConceptId.Adjectives, new[] { "when", "quickly" } },
            { GrammarConceptId.BasicPrepositions, new[] { "yesterday", "when" } },
        };

    public static List<string> BuildWordBank(
        string expectedSentence,
        GrammarConceptId conceptId,
        GrammarPhrasePattern pattern,
        string salt,
        IEnumerable<string> authoredDistractors = null,
        int maximumDistractors = 1)
    {
        List<string> expected = DialogueTranscriptTokenizer.Tokenize(expectedSentence);
        var bank = new List<string>(expected);
        int limit = Mathf.Max(0, maximumDistractors);
        int added = 0;

        if (authoredDistractors != null)
            foreach (string distractor in authoredDistractors)
            {
                if (added >= limit) break;
                if (TryAddDistractor(bank, expected, distractor)) added++;
            }

        if (added < limit && DefaultDistractors.TryGetValue(conceptId, out string[] defaults))
        {
            int start = StableIndex($"{expectedSentence}|{salt}|distractor", defaults.Length);
            for (int offset = 0; offset < defaults.Length && added < limit; offset++)
                if (TryAddDistractor(bank, expected, defaults[(start + offset) % defaults.Length])) added++;
        }

        if (added < limit)
            TryAddDistractor(bank, expected, pattern == GrammarPhrasePattern.LetterOnly ? "X" : "when");

        Shuffle(bank, $"{expectedSentence}|{salt}|{conceptId}|{pattern}");
        if (bank.Count > 1 && SameOrder(bank, expected))
        {
            string first = bank[0];
            bank.RemoveAt(0);
            bank.Add(first);
        }
        return bank;
    }

    public static DialogueJumbleEvaluation Evaluate(
        IEnumerable<string> submittedWords,
        string expectedSentence)
    {
        List<string> submitted = submittedWords != null
            ? new List<string>(submittedWords)
            : new List<string>();
        List<string> expected = DialogueTranscriptTokenizer.Tokenize(expectedSentence);
        var evaluation = new DialogueJumbleEvaluation
        {
            expectedWordCount = expected.Count,
            submittedWordCount = submitted.Count,
        };

        // First consume exact-position words, then use the remaining token bag.
        // This mirrors Wordle and handles repeated words without over-crediting.
        var remaining = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var exact = new bool[submitted.Count];
        for (int index = 0; index < expected.Count; index++)
        {
            string expectedWord = Normalize(expected[index]);
            bool exactAtIndex = index < submitted.Count && Normalize(submitted[index]) == expectedWord;
            if (exactAtIndex)
            {
                exact[index] = true;
                evaluation.correctPositionCount++;
            }
            else
            {
                remaining.TryGetValue(expectedWord, out int count);
                remaining[expectedWord] = count + 1;
            }
        }

        for (int index = 0; index < submitted.Count; index++)
        {
            string normalized = Normalize(submitted[index]);
            DialogueJumbleWordState state;
            if (exact[index])
            {
                state = DialogueJumbleWordState.CorrectPosition;
            }
            else if (!string.IsNullOrWhiteSpace(normalized) &&
                     remaining.TryGetValue(normalized, out int available) && available > 0)
            {
                state = DialogueJumbleWordState.PresentElsewhere;
                remaining[normalized] = available - 1;
                evaluation.presentElsewhereCount++;
            }
            else
            {
                state = DialogueJumbleWordState.Distractor;
                evaluation.distractorCount++;
            }

            evaluation.words.Add(new DialogueJumbleWordFeedback
            {
                word = submitted[index] ?? "",
                normalizedWord = normalized,
                submittedIndex = index,
                state = state,
            });
        }

        evaluation.isCorrect = submitted.Count == expected.Count &&
                               evaluation.correctPositionCount == expected.Count &&
                               evaluation.distractorCount == 0;
        return evaluation;
    }

    public static string BuildFeedback(DialogueJumbleEvaluation evaluation)
    {
        if (evaluation == null) return "Build the sentence and try again.";
        if (evaluation.isCorrect) return "Every word is in the right place.";

        var parts = new List<string>();
        if (evaluation.correctPositionCount > 0)
            parts.Add($"{evaluation.correctPositionCount} green: right place");
        if (evaluation.presentElsewhereCount > 0)
            parts.Add($"{evaluation.presentElsewhereCount} blue: belongs somewhere else");
        if (evaluation.distractorCount > 0)
            parts.Add($"{evaluation.distractorCount} grey: does not belong");
        if (evaluation.submittedWordCount < evaluation.expectedWordCount)
            parts.Add("the sentence still needs another word");
        return parts.Count == 0
            ? "Move a word into the sentence to begin."
            : string.Join(". ", parts) + ".";
    }

    static bool TryAddDistractor(List<string> bank, List<string> expected, string distractor)
    {
        string normalized = Normalize(distractor);
        if (string.IsNullOrWhiteSpace(normalized)) return false;
        foreach (string word in expected)
            if (Normalize(word) == normalized) return false;
        foreach (string word in bank)
            if (Normalize(word) == normalized) return false;
        bank.Add(distractor.Trim());
        return true;
    }

    static bool SameOrder(List<string> left, List<string> right)
    {
        if (left == null || right == null || left.Count != right.Count) return false;
        for (int i = 0; i < left.Count; i++)
            if (Normalize(left[i]) != Normalize(right[i])) return false;
        return true;
    }

    static void Shuffle(List<string> values, string seedText)
    {
        if (values == null || values.Count < 2) return;
        var random = new System.Random(StableHash(seedText));
        for (int i = values.Count - 1; i > 0; i--)
        {
            int swap = random.Next(i + 1);
            (values[i], values[swap]) = (values[swap], values[i]);
        }
    }

    static string Normalize(string value) => DialogueTranscriptTokenizer.NormalizeWord(value);
    static int StableIndex(string value, int count) => count <= 0 ? 0 : StableHash(value) % count;

    static int StableHash(string value)
    {
        unchecked
        {
            int hash = 17;
            foreach (char character in value ?? "") hash = hash * 31 + character;
            return hash == int.MinValue ? 0 : Math.Abs(hash);
        }
    }
}
