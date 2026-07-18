using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

public partial class PythonPhonemeInsightProvider
{
    static string PickHeardPhone(PhonemeTargetSegment targetSegment, PhonemeSpan[] spans)
    {
        if (targetSegment == null)
            return "";

        if (!string.IsNullOrWhiteSpace(targetSegment.heard))
            return targetSegment.heard;

        if (targetSegment.candidates == null || spans == null)
            return "";

        float bestConfidence = -1f;
        string bestPhone = "";
        foreach (PhonemeSpan span in spans)
        {
            if (span == null || string.IsNullOrEmpty(span.phone))
                continue;

            for (int i = 0; i < targetSegment.candidates.Length; i++)
            {
                if (!string.Equals(span.phone, targetSegment.candidates[i], StringComparison.OrdinalIgnoreCase))
                    continue;

                if (span.confidence > bestConfidence)
                {
                    bestConfidence = span.confidence;
                    bestPhone = span.phone;
                }
            }
        }

        return bestPhone;
    }

    static int FindHeardSpanIndex(string heardSound, PhonemeTargetSegment targetSegment, PhonemeSpan[] spans, int startIndex)
    {
        if (spans == null || spans.Length == 0)
            return -1;

        string normalizedHeard = NormalizePhone(heardSound);
        string[] candidates = targetSegment != null ? targetSegment.candidates : null;
        for (int i = Mathf.Clamp(startIndex, 0, spans.Length); i < spans.Length; i++)
        {
            string phone = spans[i] != null ? spans[i].DisplayPhone : "";
            string normalizedPhone = NormalizePhone(phone);
            if (!string.IsNullOrEmpty(normalizedHeard) && normalizedPhone == normalizedHeard)
                return i;

            if (candidates == null)
                continue;

            for (int c = 0; c < candidates.Length; c++)
                if (normalizedPhone == NormalizePhone(candidates[c]))
                    return i;
        }

        return -1;
    }

    static ApproximatePhoneMatch FindClosestHeardPhone(
        PhoneticSoundSegment expectedSegment,
        PhonemeTargetSegment targetSegment,
        PhonemeSpan[] spans,
        int startIndex)
    {
        if (spans == null || spans.Length == 0)
            return default;

        string expectedPhone = targetSegment != null && !string.IsNullOrWhiteSpace(targetSegment.DisplayPhone)
            ? targetSegment.DisplayPhone
            : expectedSegment.Spelling;
        string[] candidates = targetSegment != null ? targetSegment.candidates : null;
        ApproximatePhoneMatch best = default;
        int start = Mathf.Clamp(startIndex, 0, spans.Length);
        int end = Mathf.Min(spans.Length, start + 4);
        for (int i = start; i < end; i++)
        {
            PhonemeSpan span = spans[i];
            if (span == null || string.IsNullOrWhiteSpace(span.DisplayPhone))
                continue;

            string phone = span.DisplayPhone;
            float similarity = PhoneSimilarity(expectedPhone, candidates, phone);
            float confidence = Mathf.Clamp01(span.confidence);
            float score = similarity * Mathf.Lerp(0.75f, 1f, confidence);
            if (score <= best.Score)
                continue;

            best = new ApproximatePhoneMatch(phone, score, i);
        }

        return best;
    }

    static float PhoneSimilarity(string expectedPhone, string[] candidates, string heardPhone)
    {
        string expected = NormalizePhone(expectedPhone);
        string heard = NormalizePhone(heardPhone);
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(heard))
            return 0f;

        if (expected == heard)
            return 1f;

        if (candidates != null)
        {
            for (int i = 0; i < candidates.Length; i++)
                if (NormalizePhone(candidates[i]) == heard)
                    return 1f;
        }

        expected = CanonicalPhone(expected);
        heard = CanonicalPhone(heard);
        if (expected == heard)
            return 1f;

        if (SamePair(expected, heard, "T", "D") ||
            SamePair(expected, heard, "K", "G") ||
            SamePair(expected, heard, "P", "B") ||
            SamePair(expected, heard, "F", "V") ||
            SamePair(expected, heard, "S", "Z") ||
            SamePair(expected, heard, "SH", "ZH") ||
            SamePair(expected, heard, "CH", "JH"))
            return 0.72f;

        if (SamePair(expected, heard, "TH", "T") ||
            SamePair(expected, heard, "TH", "F") ||
            SamePair(expected, heard, "DH", "D") ||
            SamePair(expected, heard, "DH", "Z") ||
            SamePair(expected, heard, "NG", "N"))
            return 0.62f;

        if (IsVowelPhone(expected) && IsVowelPhone(heard))
            return 0.58f;

        if (SamePair(expected, heard, "R", "L") ||
            SamePair(expected, heard, "W", "Y"))
            return 0.45f;

        return 0f;
    }

    static string NormalizePhone(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return "";

        var builder = new StringBuilder(phone.Length);
        foreach (char c in phone.Trim())
        {
            if (char.IsWhiteSpace(c) || c == ':' || c == 'ː' || c == 'ˈ' || c == 'ˌ')
                continue;
            builder.Append(char.ToUpperInvariant(c));
        }

        return builder.ToString();
    }

    static string CanonicalPhone(string phone)
    {
        switch (phone)
        {
            case "Æ": return "AE";
            case "Ɑ":
            case "ɑ":
            case "Ɒ":
            case "ɒ": return "AA";
            case "ʌ":
            case "Ə":
            case "ɚ":
            case "ɝ": return "AH";
            case "Ɔ": return "AO";
            case "Ɛ": return "EH";
            case "Ɪ":
            case "ɪ": return "IH";
            case "I": return "IY";
            case "Ʊ":
            case "ʊ": return "UH";
            case "U": return "UW";
            case "O":
            case "OW": return "OW";
            case "AƱ":
            case "AW": return "AW";
            case "AꞮ":
            case "Aɪ":
            case "AJ":
            case "AY": return "AY";
            case "TƩ":
            case "Tʃ": return "CH";
            case "DƷ":
            case "Dʒ": return "JH";
            case "Ʃ":
            case "ʃ": return "SH";
            case "Ʒ":
            case "ʒ": return "ZH";
            case "Θ":
            case "θ": return "TH";
            case "Ð":
            case "ð": return "DH";
            case "Ŋ":
            case "ŋ": return "NG";
            case "ɹ":
            case "ɾ": return "R";
            default: return phone;
        }
    }

    static bool SamePair(string a, string b, string left, string right)
    {
        return (a == left && b == right) || (a == right && b == left);
    }

    static bool IsVowelPhone(string phone)
    {
        switch (phone)
        {
            case "AA":
            case "AE":
            case "AH":
            case "AO":
            case "AW":
            case "AY":
            case "EH":
            case "ER":
            case "EY":
            case "IH":
            case "IY":
            case "OW":
            case "OY":
            case "UH":
            case "UW":
                return true;
            default:
                return false;
        }
    }

    static string BuildObservedPhones(PhonemeSpan[] spans)
    {
        if (spans == null || spans.Length == 0)
            return "";

        var phones = new List<string>();
        string last = "";
        for (int i = 0; i < spans.Length; i++)
        {
            string phone = spans[i] != null ? spans[i].phone : "";
            if (string.IsNullOrWhiteSpace(phone))
                continue;

            string normalized = phone.ToUpperInvariant();
            if (normalized == "SIL" || normalized == "SPN" || normalized == last)
                continue;

            phones.Add(normalized);
            last = normalized;
            if (phones.Count >= 16)
                break;
        }

        return string.Join(" / ", phones);
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
        float score)
    {
        if (score >= 0.84f && CountMissingOrNeedsPractice(aligned) == 0)
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

    static string Quote(string value)
    {
        return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
    }

    static void TryDelete(string path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}
