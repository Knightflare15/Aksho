using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

public sealed class MobileZipaOnnxPronunciationInsightProvider : IPronunciationInsightProvider
{
    const string AndroidRunnerClassName = "com.thescript.phonetics.ZipaOnnxRunner";

    readonly LightweightWav2Vec2PronunciationInsightProvider fallback = new LightweightWav2Vec2PronunciationInsightProvider();

    public bool IsAvailable => true;
    public string Name => IsNativeRunnerAvailable()
        ? "ZIPA CR-CTC mobile ONNX phoneme recognizer"
        : "ZIPA mobile ONNX unavailable; using light phonetic insight";

    public PronunciationInsightResult Analyze(PronunciationInsightRequest request)
    {
        if (TryAnalyzeWithNativeRunner(request, out PronunciationInsightResult nativeResult))
            return nativeResult;

        PronunciationInsightResult fallbackResult = fallback.Analyze(request);
        return new PronunciationInsightResult(
            Name,
            fallbackResult.TargetWord,
            fallbackResult.ConfirmedWord,
            fallbackResult.RawRecognizedText,
            fallbackResult.VoskConfirmedWord,
            fallbackResult.AttemptedTarget,
            fallbackResult.Score,
            fallbackResult.HintKey,
            fallbackResult.FocusSegment,
            fallbackResult.Segments,
            fallbackResult.SyllableBeats,
            "Mobile ZIPA runner/model was not available, so the lightweight pronunciation fallback produced this result.");
    }

    static bool IsNativeRunnerAvailable()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using var runner = new AndroidJavaClass(AndroidRunnerClassName);
            using var activity = GetUnityActivity();
            return runner.CallStatic<bool>("isAvailable", activity, GetStreamingZipaPath());
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.Log($"[Pronunciation] Mobile ZIPA runner unavailable: {ex.Message}");
            return false;
        }
#else
        return false;
#endif
    }

    static bool TryAnalyzeWithNativeRunner(PronunciationInsightRequest request, out PronunciationInsightResult result)
    {
        result = default;
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using var runner = new AndroidJavaClass(AndroidRunnerClassName);
            using var activity = GetUnityActivity();
            string json = runner.CallStatic<string>(
                "analyze",
                activity,
                request.TargetWord,
                request.ConfirmedWord,
                request.RawRecognizedText,
                request.VoskConfirmedWord,
                request.Pcm16Audio,
                request.SampleRate,
                GetStreamingZipaPath());

            if (string.IsNullOrWhiteSpace(json))
                return false;

            MobileZipaReport report = JsonUtility.FromJson<MobileZipaReport>(json);
            result = BuildResultFromReport(request, report);
            return !string.IsNullOrWhiteSpace(result.TargetWord);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[Pronunciation] Mobile ZIPA runner failed; falling back to lightweight insight. {ex.Message}");
            return false;
        }
#else
        return false;
#endif
    }

    static string GetStreamingZipaPath()
    {
        return new ConfigurableLocalSpeechModelPathResolver()
            .ResolvePath(LocalSpeechModelKind.ZipaPronunciation, false);
    }

    static AndroidJavaObject GetUnityActivity()
    {
        using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        return unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
    }

    static PronunciationInsightResult BuildResultFromReport(PronunciationInsightRequest request, MobileZipaReport report)
    {
        string target = !string.IsNullOrWhiteSpace(report?.word) ? NormalizeWord(report.word) :
            !string.IsNullOrWhiteSpace(request.TargetWord) ? request.TargetWord : request.ConfirmedWord;
        IReadOnlyList<PhoneticSoundSegment> expected = PronunciationProfileBuilder.BuildSegments(target);
        IReadOnlyList<string> beats = PronunciationProfileBuilder.BuildSyllableBeats(target, expected);
        PhoneticSoundSegment[] segments = BuildSegments(expected, report);
        float score = Mathf.Clamp01(report != null ? report.score : 0f);
        bool attempted = request.VoskConfirmedWord || score > 0f;
        PhoneticSoundSegment focus = PickFocus(segments);
        PronunciationHintKey hint = PickHint(segments, beats, score);

        return new PronunciationInsightResult(
            "ZIPA CR-CTC mobile ONNX phoneme recognizer",
            target,
            request.ConfirmedWord,
            request.RawRecognizedText,
            request.VoskConfirmedWord,
            attempted,
            score,
            hint,
            focus,
            segments,
            beats,
            string.IsNullOrWhiteSpace(report?.message)
                ? $"Mobile ZIPA phones: {report?.decoded ?? ""}".Trim()
                : report.message);
    }

    static PhoneticSoundSegment[] BuildSegments(IReadOnlyList<PhoneticSoundSegment> expected, MobileZipaReport report)
    {
        if (expected == null || expected.Count == 0)
            return Array.Empty<PhoneticSoundSegment>();

        var segments = new PhoneticSoundSegment[expected.Count];
        for (int i = 0; i < expected.Count; i++)
        {
            PhoneticSoundSegment expectedSegment = expected[i];
            MobileZipaTarget target = report != null && report.target != null && i < report.target.Length
                ? report.target[i]
                : null;
            if (target == null)
            {
                segments[i] = expectedSegment.WithStatus(PhoneticSegmentStatus.Unknown, 0f);
                continue;
            }

            PhoneticSegmentStatus status = target.matched
                ? PhoneticSegmentStatus.Matched
                : target.confidence >= 0.35f
                    ? PhoneticSegmentStatus.NeedsPractice
                    : PhoneticSegmentStatus.Missing;
            segments[i] = expectedSegment.WithHeardSound(target.heard, status, target.confidence);
        }

        return segments;
    }

    static PhoneticSoundSegment PickFocus(IReadOnlyList<PhoneticSoundSegment> segments)
    {
        if (segments == null || segments.Count == 0)
            return default;

        foreach (PhoneticSoundSegment segment in segments)
            if (segment.Status == PhoneticSegmentStatus.Missing)
                return segment;

        foreach (PhoneticSoundSegment segment in segments)
            if (segment.Status == PhoneticSegmentStatus.NeedsPractice)
                return segment;

        return segments[0];
    }

    static PronunciationHintKey PickHint(
        IReadOnlyList<PhoneticSoundSegment> segments,
        IReadOnlyList<string> beats,
        float score)
    {
        if (segments == null || segments.Count == 0)
            return PronunciationHintKey.TryAgain;

        if (score >= 0.84f)
            return PronunciationHintKey.GreatTry;

        if (beats != null && beats.Count > 1 && CountNeedsPractice(segments) > 1)
            return PronunciationHintKey.TryAllBeats;

        int focusIndex = 0;
        for (int i = 0; i < segments.Count; i++)
        {
            if (segments[i].Status == PhoneticSegmentStatus.Missing ||
                segments[i].Status == PhoneticSegmentStatus.NeedsPractice)
            {
                focusIndex = i;
                break;
            }
        }

        if (focusIndex == 0)
            return PronunciationHintKey.TryFirstSound;

        if (focusIndex == segments.Count - 1)
            return PronunciationHintKey.TryLastSound;

        return PronunciationHintKey.TryAgain;
    }

    static int CountNeedsPractice(IReadOnlyList<PhoneticSoundSegment> segments)
    {
        int count = 0;
        foreach (PhoneticSoundSegment segment in segments)
            if (segment.Status == PhoneticSegmentStatus.Missing ||
                segment.Status == PhoneticSegmentStatus.NeedsPractice)
                count++;
        return count;
    }

    static string NormalizeWord(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var builder = new StringBuilder(value.Length);
        foreach (char c in value)
            if (char.IsLetter(c))
                builder.Append(char.ToUpperInvariant(c));
        return builder.ToString();
    }

#pragma warning disable 0649
    [Serializable]
    sealed class MobileZipaReport
    {
        public string word;
        public string decoded;
        public float score;
        public string message;
        public MobileZipaTarget[] target;
    }

    [Serializable]
    sealed class MobileZipaTarget
    {
        public string phone;
        public bool matched;
        public float confidence;
        public string heard;
    }
#pragma warning restore 0649
}
