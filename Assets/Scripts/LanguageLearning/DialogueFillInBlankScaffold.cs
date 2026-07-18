using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Selects blanks that carry the current grammar lesson.</summary>
public static class DialogueFillInBlankScaffold
{
    static readonly HashSet<string> GenericWords = Set(
        "a", "an", "the", "is", "am", "are", "was", "were", "be", "been",
        "to", "of", "and", "or", "but", "for", "with", "this", "that");
    static readonly HashSet<string> Greetings = Set(
        "hello", "hi", "goodbye", "please", "thank", "thanks", "yes", "no", "welcome");
    static readonly HashSet<string> Articles = Set("a", "an", "the");
    static readonly HashSet<string> Pronouns = Set("i", "you", "he", "she", "it", "we", "they");
    static readonly HashSet<string> Prepositions = Set(
        "in", "on", "under", "over", "behind", "beside", "near", "between", "above", "below", "through", "across");
    static readonly HashSet<string> Verbs = Set(
        "bite", "bites", "run", "runs", "jump", "jumps", "scratch", "scratches",
        "fly", "flies", "peck", "pecks", "swim", "swims", "play", "plays",
        "went", "go", "goes", "walk", "walks", "wait", "waits", "move", "moves");
    static readonly HashSet<string> Adjectives = Set(
        "big", "small", "quick", "slow", "strong", "weak", "fast", "tiny", "tall", "short", "brave", "quiet", "loud");
    static readonly HashSet<string> NonPluralSEndings = Set(
        "is", "this", "his", "was", "yes", "besides", "across");

    public static string Build(
        string sentence,
        GrammarConceptId conceptId,
        GrammarPhrasePattern pattern,
        string salt = "",
        IEnumerable<string> authoredFocusWords = null,
        int maximumBlanks = 1)
    {
        if (string.IsNullOrWhiteSpace(sentence))
            return "";

        List<TokenSpan> tokens = Parse(sentence);
        if (tokens.Count == 0)
            return sentence;

        List<int> candidates = ResolveTargetIndices(tokens, conceptId, pattern, authoredFocusWords);
        if (candidates.Count == 0)
            candidates.Add(0);

        int blankCount = Mathf.Clamp(maximumBlanks, 1, candidates.Count);
        int start = StableIndex($"{sentence}|{salt}|{conceptId}|{pattern}", candidates.Count);
        var selected = new List<int>();
        for (int offset = 0; offset < candidates.Count && selected.Count < blankCount; offset++)
        {
            int candidate = candidates[(start + offset) % candidates.Count];
            if (!selected.Contains(candidate))
                selected.Add(candidate);
        }

        selected.Sort((left, right) => tokens[right].start.CompareTo(tokens[left].start));
        string result = sentence;
        foreach (int tokenIndex in selected)
        {
            TokenSpan token = tokens[tokenIndex];
            result = result.Remove(token.start, token.length).Insert(token.start, "____");
        }
        return result;
    }

    public static List<string> InferFocusWords(
        string sentence,
        GrammarConceptId conceptId,
        GrammarPhrasePattern pattern)
    {
        var result = new List<string>();
        List<TokenSpan> tokens = Parse(sentence);
        foreach (int index in ResolveTargetIndices(tokens, conceptId, pattern, null))
        {
            if (index < 0 || index >= tokens.Count)
                continue;
            string normalized = DialogueTranscriptTokenizer.NormalizeWord(tokens[index].text);
            if (!result.Exists(value => DialogueTranscriptTokenizer.NormalizeWord(value) == normalized))
                result.Add(tokens[index].text);
        }
        return result;
    }

    public static string ExtractAuthoredTemplate(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt) || !prompt.Contains("____"))
            return "";

        string template = prompt.Trim();
        int colon = template.LastIndexOf(':');
        if (colon >= 0 && template.IndexOf("____", colon, StringComparison.Ordinal) >= 0)
            template = template.Substring(colon + 1).Trim();
        return template;
    }

    static List<int> ResolveTargetIndices(
        List<TokenSpan> tokens,
        GrammarConceptId conceptId,
        GrammarPhrasePattern pattern,
        IEnumerable<string> authoredFocusWords)
    {
        var result = new List<int>();
        if (tokens == null || tokens.Count == 0)
            return result;

        var authored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (authoredFocusWords != null)
            foreach (string focus in authoredFocusWords)
                AddNormalized(authored, focus);
        AddMatching(result, tokens, authored);
        if (result.Count > 0)
            return result;

        switch (conceptId)
        {
            case GrammarConceptId.Greetings:
                AddMatching(result, tokens, Greetings);
                break;
            case GrammarConceptId.Alphabet:
            case GrammarConceptId.VowelsConsonants:
                for (int i = 0; i < tokens.Count; i++)
                    if (Normalize(tokens[i].text).Length == 1) result.Add(i);
                break;
            case GrammarConceptId.SentenceStartEnd:
                result.Add(0);
                break;
            case GrammarConceptId.BasicNouns:
                AddNounPosition(result, tokens, pattern);
                break;
            case GrammarConceptId.BasicVerbs:
                AddMatching(result, tokens, Verbs);
                if (result.Count == 0) AddVerbPosition(result, tokens, pattern);
                break;
            case GrammarConceptId.Articles:
                AddMatching(result, tokens, Articles);
                break;
            case GrammarConceptId.Pronouns:
                AddMatching(result, tokens, Pronouns);
                break;
            case GrammarConceptId.Plurals:
                for (int i = 0; i < tokens.Count; i++)
                {
                    string word = Normalize(tokens[i].text);
                    if (word.Length > 2 && word.EndsWith("s", StringComparison.Ordinal) && !NonPluralSEndings.Contains(word))
                        result.Add(i);
                }
                break;
            case GrammarConceptId.Adjectives:
                AddMatching(result, tokens, Adjectives);
                if (result.Count == 0) AddAdjectivePosition(result, tokens, pattern);
                break;
            case GrammarConceptId.BasicPrepositions:
                AddMatching(result, tokens, Prepositions);
                break;
        }

        AddPatternFallback(result, tokens, pattern);
        if (result.Count == 0)
            for (int i = 0; i < tokens.Count; i++)
                if (!GenericWords.Contains(Normalize(tokens[i].text))) result.Add(i);
        if (result.Count == 0)
            for (int i = 0; i < tokens.Count; i++) result.Add(i);
        return result;
    }

    static void AddPatternFallback(List<int> result, List<TokenSpan> tokens, GrammarPhrasePattern pattern)
    {
        if (result.Count > 0 || tokens.Count == 0) return;
        switch (pattern)
        {
            case GrammarPhrasePattern.LetterOnly:
            case GrammarPhrasePattern.NounOnly:
            case GrammarPhrasePattern.VerbOnly:
                result.Add(0);
                break;
            case GrammarPhrasePattern.DeterminerNoun:
                result.Add(Mathf.Max(0, tokens.Count - 2));
                break;
            case GrammarPhrasePattern.DeterminerAdjectiveNoun:
            case GrammarPhrasePattern.AdjectiveNoun:
                result.Add(Mathf.Max(0, tokens.Count - 2));
                break;
            case GrammarPhrasePattern.NounVerbPresent:
            case GrammarPhrasePattern.PronounVerbPresent:
            case GrammarPhrasePattern.VerbAdverb:
                result.Add(Mathf.Min(1, tokens.Count - 1));
                break;
        }
    }

    static void AddNounPosition(List<int> result, List<TokenSpan> tokens, GrammarPhrasePattern pattern)
    {
        if (pattern == GrammarPhrasePattern.NounVerbPresent)
            result.Add(0);
        else
            result.Add(tokens.Count - 1);
    }

    static void AddVerbPosition(List<int> result, List<TokenSpan> tokens, GrammarPhrasePattern pattern)
    {
        if (pattern == GrammarPhrasePattern.VerbOnly)
            result.Add(0);
        else if (pattern == GrammarPhrasePattern.NounVerbPresent ||
                 pattern == GrammarPhrasePattern.PronounVerbPresent ||
                 pattern == GrammarPhrasePattern.VerbAdverb)
            result.Add(Mathf.Min(1, tokens.Count - 1));
    }

    static void AddAdjectivePosition(List<int> result, List<TokenSpan> tokens, GrammarPhrasePattern pattern)
    {
        if (tokens.Count >= 2 &&
            (pattern == GrammarPhrasePattern.AdjectiveNoun || pattern == GrammarPhrasePattern.DeterminerAdjectiveNoun))
            result.Add(tokens.Count - 2);
    }

    static void AddMatching(List<int> result, List<TokenSpan> tokens, HashSet<string> values)
    {
        if (values == null || values.Count == 0) return;
        for (int i = 0; i < tokens.Count; i++)
            if (values.Contains(Normalize(tokens[i].text))) result.Add(i);
    }

    static List<TokenSpan> Parse(string text)
    {
        var result = new List<TokenSpan>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        int start = -1;
        for (int i = 0; i <= text.Length; i++)
        {
            bool isToken = i < text.Length &&
                           (char.IsLetterOrDigit(text[i]) || text[i] == '\'' || text[i] == '\u2019');
            if (isToken && start < 0) start = i;
            if (isToken || start < 0) continue;
            result.Add(new TokenSpan { start = start, length = i - start, text = text.Substring(start, i - start) });
            start = -1;
        }
        return result;
    }

    static string Normalize(string value) => DialogueTranscriptTokenizer.NormalizeWord(value);

    static void AddNormalized(HashSet<string> values, string value)
    {
        string normalized = Normalize(value);
        if (!string.IsNullOrWhiteSpace(normalized)) values.Add(normalized);
    }

    static int StableIndex(string value, int count)
    {
        if (count <= 0) return 0;
        unchecked
        {
            int hash = 17;
            foreach (char character in value ?? "") hash = hash * 31 + character;
            int safe = hash == int.MinValue ? 0 : Math.Abs(hash);
            return safe % count;
        }
    }

    static HashSet<string> Set(params string[] values) =>
        new HashSet<string>(values ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

    sealed class TokenSpan
    {
        public int start;
        public int length;
        public string text;
    }
}
