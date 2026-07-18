using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

public sealed class LightweightWav2Vec2PronunciationInsightProvider : IPronunciationInsightProvider
{
    const float ConfirmedScore = 0.96f;
    const float CloseAttemptScore = 0.62f;

    public bool IsAvailable => true;
    public string Name => "Wav2Vec2 light phonetic insight";

    public PronunciationInsightResult Analyze(PronunciationInsightRequest request)
    {
        string target = !string.IsNullOrEmpty(request.TargetWord)
            ? request.TargetWord
            : request.ConfirmedWord;

        IReadOnlyList<PhoneticSoundSegment> expected = PronunciationProfileBuilder.BuildSegments(target);
        IReadOnlyList<string> beats = PronunciationProfileBuilder.BuildSyllableBeats(target, expected);
        if (string.IsNullOrEmpty(target) || expected.Count == 0)
        {
            return new PronunciationInsightResult(
                Name,
                target,
                request.ConfirmedWord,
                request.RawRecognizedText,
                request.VoskConfirmedWord,
                false,
                0f,
                PronunciationHintKey.TryAgain,
                default,
                expected,
                beats,
                "No target word was available for pronunciation insight.");
        }

        string heard = NormalizeLetters(request.RawRecognizedText);
        bool exactWordMatch = string.Equals(heard, target, StringComparison.Ordinal);
        if (request.VoskConfirmedWord && exactWordMatch)
        {
            var confirmedSegments = new List<PhoneticSoundSegment>(expected.Count);
            foreach (PhoneticSoundSegment segment in expected)
                confirmedSegments.Add(segment.WithStatus(PhoneticSegmentStatus.Matched, ConfirmedScore));

            return new PronunciationInsightResult(
                Name,
                target,
                request.ConfirmedWord,
                request.RawRecognizedText,
                true,
                true,
                ConfirmedScore,
                PronunciationHintKey.GreatTry,
                confirmedSegments[0],
                confirmedSegments,
                beats,
                "Vosk confirmed the word; phonetic sounds are available for training detail.");
        }

        List<PhoneticSoundSegment> aligned = AlignExpectedSegments(expected, heard);
        float score = EstimateScore(aligned, request.Pcm16Audio, request.SampleRate);
        if (request.VoskConfirmedWord)
            score = Mathf.Max(score, CloseAttemptScore);
        PhoneticSoundSegment focus = PickFocusSegment(aligned);
        PronunciationHintKey hintKey = PickHint(aligned, beats, focus, heard, score);
        bool attemptedTarget = request.VoskConfirmedWord ||
                               score >= CloseAttemptScore ||
                               SharesFirstOrLastSound(aligned) ||
                               LevenshteinDistance(target, heard) <= Mathf.Max(1, target.Length / 2);

        return new PronunciationInsightResult(
            Name,
            target,
            request.ConfirmedWord,
            request.RawRecognizedText,
            request.VoskConfirmedWord,
            attemptedTarget,
            score,
            hintKey,
            focus,
            aligned,
            beats,
            request.VoskConfirmedWord
                ? "Vosk accepted the word; phonetic insight found a sound to practice."
                : "Vosk did not confirm the word; phonetic insight estimated the sound parts to practice.");
    }

    static List<PhoneticSoundSegment> AlignExpectedSegments(IReadOnlyList<PhoneticSoundSegment> expected, string heard)
    {
        var result = new List<PhoneticSoundSegment>(expected.Count);
        int searchIndex = 0;

        foreach (PhoneticSoundSegment segment in expected)
        {
            string spelling = NormalizeLetters(segment.Spelling);
            if (string.IsNullOrEmpty(spelling))
            {
                result.Add(segment.WithStatus(PhoneticSegmentStatus.Unknown, 0f));
                continue;
            }

            int found = heard.IndexOf(spelling, searchIndex, StringComparison.Ordinal);
            if (found >= 0)
            {
                searchIndex = found + spelling.Length;
                result.Add(segment.WithStatus(PhoneticSegmentStatus.Matched, 0.82f));
                continue;
            }

            string heardSound = GuessHeardSoundFor(segment, heard, searchIndex, result.Count == 0);
            bool nearMatch = HasAnySharedLetter(spelling, heard, searchIndex);
            result.Add(segment.WithHeardSound(
                heardSound,
                nearMatch ? PhoneticSegmentStatus.NeedsPractice : PhoneticSegmentStatus.Missing,
                nearMatch ? 0.42f : 0.12f));
        }

        return result;
    }

    static float EstimateScore(IReadOnlyList<PhoneticSoundSegment> aligned, byte[] audio, int sampleRate)
    {
        if (aligned == null || aligned.Count == 0)
            return 0f;

        float total = 0f;
        foreach (PhoneticSoundSegment segment in aligned)
        {
            total += segment.Status == PhoneticSegmentStatus.Matched
                ? 1f
                : segment.Status == PhoneticSegmentStatus.NeedsPractice
                    ? 0.45f
                    : 0f;
        }

        float score = total / aligned.Count;
        if (audio != null && audio.Length > 0 && sampleRate > 0)
        {
            float seconds = audio.Length / (sampleRate * 2f);
            if (seconds >= 0.25f)
                score = Mathf.Max(score, 0.18f);
        }

        return Mathf.Clamp01(score);
    }

    static PhoneticSoundSegment PickFocusSegment(IReadOnlyList<PhoneticSoundSegment> aligned)
    {
        if (aligned == null || aligned.Count == 0)
            return default;

        foreach (PhoneticSoundSegment segment in aligned)
            if (segment.Status == PhoneticSegmentStatus.Missing)
                return segment;

        foreach (PhoneticSoundSegment segment in aligned)
            if (segment.Status == PhoneticSegmentStatus.NeedsPractice)
                return segment;

        return aligned[0];
    }

    static PronunciationHintKey PickHint(
        IReadOnlyList<PhoneticSoundSegment> aligned,
        IReadOnlyList<string> beats,
        PhoneticSoundSegment focus,
        string heard,
        float score)
    {
        if (string.IsNullOrEmpty(heard))
            return PronunciationHintKey.TryAgain;

        if (score >= 0.84f)
            return PronunciationHintKey.GreatTry;

        if (beats != null && beats.Count > 1 && CountMissingOrNeedsPractice(aligned) > 1)
            return PronunciationHintKey.TryAllBeats;

        if (aligned != null && aligned.Count > 0)
        {
            if (string.Equals(focus.Spelling, aligned[0].Spelling, StringComparison.OrdinalIgnoreCase))
                return PronunciationHintKey.TryFirstSound;
            if (string.Equals(focus.Spelling, aligned[aligned.Count - 1].Spelling, StringComparison.OrdinalIgnoreCase))
                return PronunciationHintKey.TryLastSound;
        }

        return score < 0.35f ? PronunciationHintKey.TrySlower : PronunciationHintKey.TryAgain;
    }

    static int CountMissingOrNeedsPractice(IReadOnlyList<PhoneticSoundSegment> aligned)
    {
        int count = 0;
        if (aligned == null)
            return count;

        foreach (PhoneticSoundSegment segment in aligned)
            if (segment.Status == PhoneticSegmentStatus.Missing ||
                segment.Status == PhoneticSegmentStatus.NeedsPractice)
                count++;
        return count;
    }

    static bool SharesFirstOrLastSound(IReadOnlyList<PhoneticSoundSegment> aligned)
    {
        if (aligned == null || aligned.Count == 0)
            return false;

        return aligned[0].Status == PhoneticSegmentStatus.Matched ||
               aligned[aligned.Count - 1].Status == PhoneticSegmentStatus.Matched;
    }

    static bool HasAnySharedLetter(string spelling, string heard, int searchIndex)
    {
        if (string.IsNullOrEmpty(spelling) || string.IsNullOrEmpty(heard))
            return false;

        int start = Mathf.Clamp(searchIndex - 1, 0, heard.Length);
        for (int i = start; i < heard.Length; i++)
            if (spelling.IndexOf(heard[i]) >= 0)
                return true;
        return false;
    }

    static string GuessHeardSoundFor(PhoneticSoundSegment expected, string heard, int searchIndex, bool firstSegment)
    {
        if (string.IsNullOrEmpty(heard))
            return "";

        int start = Mathf.Clamp(searchIndex, 0, heard.Length - 1);
        if (firstSegment)
            start = 0;

        string consonant = heard[start].ToString();
        if (start + 1 < heard.Length && IsVowel(heard[start + 1]) && !IsVowel(heard[start]))
            return (consonant + heard[start + 1]).ToLowerInvariant();

        return FriendlySound(consonant);
    }

    static string FriendlySound(string chunk)
    {
        switch (chunk)
        {
            case "C":
            case "K":
            case "Q": return "k";
            case "G": return "g";
            case "J": return "j";
            case "X": return "ks";
            default: return chunk.ToLowerInvariant();
        }
    }

    static string NormalizeLetters(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var builder = new StringBuilder(value.Length);
        foreach (char c in value)
            if (char.IsLetter(c))
                builder.Append(char.ToUpperInvariant(c));
        return builder.ToString();
    }

    static bool IsVowel(char c)
    {
        c = char.ToUpperInvariant(c);
        return c == 'A' || c == 'E' || c == 'I' || c == 'O' || c == 'U';
    }

    static int LevenshteinDistance(string a, string b)
    {
        a = a ?? "";
        b = b ?? "";
        var costs = new int[b.Length + 1];
        for (int j = 0; j < costs.Length; j++)
            costs[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            int previous = costs[0];
            costs[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int temp = costs[j];
                int substitution = previous + (a[i - 1] == b[j - 1] ? 0 : 1);
                costs[j] = Mathf.Min(Mathf.Min(costs[j] + 1, costs[j - 1] + 1), substitution);
                previous = temp;
            }
        }

        return costs[b.Length];
    }
}
