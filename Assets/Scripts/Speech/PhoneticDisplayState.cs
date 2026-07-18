using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public enum PhoneticDisplaySource
{
    None,
    LetterRecognition,
    SpellCast
}

public sealed class PhoneticDisplayState : MonoBehaviour
{
    public const float FeedbackVisibleSeconds = 2f;

    IReadOnlyList<PhoneticSoundSegment> segments = Array.Empty<PhoneticSoundSegment>();
    IReadOnlyList<string> syllableBeats = Array.Empty<string>();

    public PhoneticDisplaySource Source { get; private set; } = PhoneticDisplaySource.None;
    public string TargetText { get; private set; } = "";
    public string DetailText { get; private set; } = "";
    public float LastUpdatedAt { get; private set; } = -1f;
    public PronunciationInsightResult LastPronunciationInsight { get; private set; }
    public bool HasPronunciationInsight { get; private set; }
    public IReadOnlyList<PhoneticSoundSegment> Segments => segments;
    public IReadOnlyList<string> SyllableBeats => syllableBeats;
    public bool HasEntry => Source != PhoneticDisplaySource.None && !string.IsNullOrWhiteSpace(TargetText);

    public event Action OnChanged;

    public static PhoneticDisplayState EnsureExists(GameObject preferredHost = null)
    {
        PhoneticDisplayState existing = preferredHost != null
            ? preferredHost.GetComponent<PhoneticDisplayState>()
            : null;
        if (existing != null)
            return existing;

        existing = FindAnyObjectByType<PhoneticDisplayState>();
        if (existing != null)
            return existing;

        GameObject host = preferredHost != null
            ? preferredHost
            : new GameObject("PhoneticDisplayState");
        return host.AddComponent<PhoneticDisplayState>();
    }

    public void RecordLetterRecognition(char letter, float score, int tries)
    {
        string text = char.ToUpperInvariant(letter).ToString();
        string detail = tries > 0
            ? $"Letter accepted  Score {score:0.0}  Try {tries}"
            : $"Letter accepted  Score {score:0.0}";
        Record(PhoneticDisplaySource.LetterRecognition, text, detail);
    }

    public void RecordSuccessfulCast(
        string spellWord,
        bool areaCast,
        PronunciationInsightResult? pronunciationInsight = null)
    {
        string text = SpellRegistry.NormalizeWord(spellWord);
        string detail = areaCast ? "Successful special cast" : "Successful cast";
        if (pronunciationInsight.HasValue)
        {
            PronunciationInsightResult insight = pronunciationInsight.Value;
            if (HasActionablePronunciationInsight(insight))
                detail += $"  Pronunciation {Mathf.RoundToInt(insight.Score * 100f)}%";
        }
        Record(PhoneticDisplaySource.SpellCast, text, detail, pronunciationInsight);
    }

    public string BuildHudText()
    {
        if (!HasEntry)
            return "PHONETICS\nNo successful cast or letter yet.";

        string source = Source == PhoneticDisplaySource.SpellCast ? "Last cast" : "Last letter";
        var builder = new StringBuilder();
        builder.Append("PHONETICS").AppendLine();
        builder.Append(source).Append(": ").Append(TargetText).AppendLine();
        if (!string.IsNullOrWhiteSpace(DetailText))
            builder.Append(DetailText).AppendLine();

        builder.Append("Expected: ").Append(BuildExpectedSoundsLine()).AppendLine();
        string heard = BuildHeardSoundsLine();
        if (!string.IsNullOrWhiteSpace(heard))
            builder.Append("Heard: ").Append(heard).AppendLine();
        string practice = BuildPracticeLine();
        if (!string.IsNullOrWhiteSpace(practice))
            builder.Append("Practice: ").Append(practice).AppendLine();
        string beats = BuildBeatsLine();
        if (!string.IsNullOrWhiteSpace(beats) && !string.Equals(beats, TargetText, StringComparison.OrdinalIgnoreCase))
            builder.Append("Beats: ").Append(beats);

        return builder.ToString().TrimEnd();
    }

    public bool IsFeedbackVisible(float currentUnscaledTime, float visibleSeconds = FeedbackVisibleSeconds)
    {
        if (!HasEntry || LastUpdatedAt < 0f || visibleSeconds <= 0f)
            return false;

        float elapsed = Mathf.Max(0f, currentUnscaledTime - LastUpdatedAt);
        return elapsed < visibleSeconds;
    }

    void Record(
        PhoneticDisplaySource source,
        string targetText,
        string detail,
        PronunciationInsightResult? pronunciationInsight = null)
    {
        targetText = SpellRegistry.NormalizeWord(targetText);
        Source = string.IsNullOrEmpty(targetText) ? PhoneticDisplaySource.None : source;
        TargetText = targetText;
        DetailText = detail ?? "";
        HasPronunciationInsight = pronunciationInsight.HasValue;
        LastPronunciationInsight = pronunciationInsight.GetValueOrDefault();
        segments = HasPronunciationInsight && LastPronunciationInsight.Segments != null && LastPronunciationInsight.Segments.Count > 0
            ? LastPronunciationInsight.Segments
            : PronunciationProfileBuilder.BuildSegments(targetText);
        syllableBeats = PronunciationProfileBuilder.BuildSyllableBeats(targetText, segments);
        LastUpdatedAt = Time.unscaledTime;
        Debug.Log($"[Pronunciation] Phonetics state recorded source={Source} target='{TargetText}' detail='{DetailText}' hasInsight={HasPronunciationInsight}");
        OnChanged?.Invoke();
    }

    string BuildExpectedSoundsLine()
    {
        if (segments == null || segments.Count == 0)
            return TargetText;

        var sounds = new List<string>(segments.Count);
        foreach (PhoneticSoundSegment segment in segments)
        {
            string sound = string.IsNullOrWhiteSpace(segment.FriendlySound)
                ? segment.Spelling.ToLowerInvariant()
                : segment.FriendlySound;
            if (!string.IsNullOrWhiteSpace(sound))
                sounds.Add(sound);
        }

        return sounds.Count > 0 ? string.Join(" / ", sounds) : TargetText;
    }

    string BuildHeardSoundsLine()
    {
        if (!HasPronunciationInsight || segments == null || segments.Count == 0)
            return "";

        var heard = new List<string>();
        foreach (PhoneticSoundSegment segment in segments)
        {
            if (segment.Status == PhoneticSegmentStatus.Matched)
                heard.Add(string.IsNullOrWhiteSpace(segment.FriendlySound) ? segment.Spelling.ToLowerInvariant() : segment.FriendlySound);
            else if (!string.IsNullOrWhiteSpace(segment.HeardSound))
                heard.Add(segment.HeardSound);
            else if (segment.Status == PhoneticSegmentStatus.Missing)
                heard.Add("-");
        }

        return heard.Count > 0 ? string.Join(" / ", heard) : LastPronunciationInsight.RawRecognizedText;
    }

    string BuildPracticeLine()
    {
        if (!HasActionablePronunciationInsight(LastPronunciationInsight) ||
            LastPronunciationInsight.HintKey == PronunciationHintKey.GreatTry)
            return "";

        PhoneticSoundSegment focus = LastPronunciationInsight.FocusSegment;
        string expected = string.IsNullOrWhiteSpace(focus.FriendlySound)
            ? focus.Spelling.ToLowerInvariant()
            : focus.FriendlySound;
        if (string.IsNullOrWhiteSpace(expected))
            return "";

        return string.IsNullOrWhiteSpace(focus.HeardSound)
            ? expected
            : $"{expected} instead of {focus.HeardSound}";
    }

    public static bool HasActionablePronunciationInsight(PronunciationInsightResult insight)
    {
        if (string.IsNullOrWhiteSpace(insight.TargetWord))
            return false;

        if (insight.Score > 0f || insight.HintKey == PronunciationHintKey.GreatTry)
            return true;

        if (insight.Segments == null)
            return false;

        foreach (PhoneticSoundSegment segment in insight.Segments)
        {
            if (segment.Status == PhoneticSegmentStatus.Matched ||
                segment.Status == PhoneticSegmentStatus.NeedsPractice)
                return true;

            if (segment.Status == PhoneticSegmentStatus.Missing && !insight.VoskConfirmedWord)
                return true;
        }

        return false;
    }

    string BuildBeatsLine()
    {
        if (syllableBeats == null || syllableBeats.Count == 0)
            return "";

        return string.Join("-", syllableBeats);
    }
}
