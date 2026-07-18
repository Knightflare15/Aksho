using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

public static class PronunciationProfileBuilder
{
    static readonly string[] Digraphs =
    {
        "SH", "TH", "CH", "WH", "PH", "CK", "NG", "QU", "EE", "EA", "AI", "AY",
        "OA", "OO", "OW", "OU", "AR", "OR", "ER", "IR", "UR"
    };

    public static IReadOnlyList<PhoneticSoundSegment> BuildSegments(string word)
    {
        word = NormalizeLetters(word);
        var segments = new List<PhoneticSoundSegment>();
        if (string.IsNullOrEmpty(word))
            return segments;

        int beatIndex = 0;
        for (int i = 0; i < word.Length;)
        {
            if (i == word.Length - 1 && word[i] == 'E' && HasEarlierVowel(word))
                break;

            string chunk = MatchDigraph(word, i);
            if (string.IsNullOrEmpty(chunk))
                chunk = word[i].ToString();

            segments.Add(new PhoneticSoundSegment(
                chunk,
                FriendlySound(chunk),
                beatIndex,
                PhoneticSegmentStatus.Unknown,
                0f));

            if (ContainsVowel(chunk) && i + chunk.Length < word.Length)
                beatIndex++;

            i += chunk.Length;
        }

        return segments;
    }

    public static IReadOnlyList<string> BuildSyllableBeats(string word, IReadOnlyList<PhoneticSoundSegment> segments)
    {
        var beats = new List<string>();
        if (segments == null || segments.Count == 0)
            return beats;

        var builder = new StringBuilder();
        int activeBeat = segments[0].BeatIndex;
        foreach (PhoneticSoundSegment segment in segments)
        {
            if (segment.BeatIndex != activeBeat)
            {
                if (builder.Length > 0)
                    beats.Add(builder.ToString());
                builder.Length = 0;
                activeBeat = segment.BeatIndex;
            }

            builder.Append(segment.Spelling);
        }

        if (builder.Length > 0)
            beats.Add(builder.ToString());

        return beats;
    }

    static string MatchDigraph(string word, int index)
    {
        foreach (string digraph in Digraphs)
            if (index + digraph.Length <= word.Length &&
                string.Compare(word, index, digraph, 0, digraph.Length, StringComparison.Ordinal) == 0)
                return digraph;
        return "";
    }

    static bool HasEarlierVowel(string word)
    {
        for (int i = 0; i < word.Length - 1; i++)
            if (IsVowel(word[i]))
                return true;
        return false;
    }

    static bool ContainsVowel(string value)
    {
        foreach (char c in value)
            if (IsVowel(c))
                return true;
        return false;
    }

    static bool IsVowel(char c)
    {
        c = char.ToUpperInvariant(c);
        return c == 'A' || c == 'E' || c == 'I' || c == 'O' || c == 'U';
    }

    static string FriendlySound(string chunk)
    {
        switch (chunk)
        {
            case "SH": return "sh";
            case "TH": return "th";
            case "CH": return "ch";
            case "WH": return "wh";
            case "PH": return "f";
            case "CK": return "k";
            case "NG": return "ng";
            case "QU": return "kw";
            case "EE":
            case "EA": return "ee";
            case "AI":
            case "AY": return "ay";
            case "OA": return "oh";
            case "OO": return "oo";
            case "OW": return "ow";
            case "OU": return "ow";
            case "AR": return "ar";
            case "OR": return "or";
            case "ER":
            case "IR":
            case "UR": return "er";
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
}
