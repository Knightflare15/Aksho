using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

public enum PronunciationHintKey
{
    GreatTry,
    TryFirstSound,
    TryLastSound,
    TryAllBeats,
    TrySlower,
    TryAgain
}

public enum PhoneticSegmentStatus
{
    Unknown,
    Matched,
    NeedsPractice,
    Missing
}

public readonly struct PhoneticSoundSegment
{
    public readonly string Spelling;
    public readonly string FriendlySound;
    public readonly string HeardSound;
    public readonly int BeatIndex;
    public readonly PhoneticSegmentStatus Status;
    public readonly float Confidence;

    public PhoneticSoundSegment(
        string spelling,
        string friendlySound,
        int beatIndex,
        PhoneticSegmentStatus status,
        float confidence,
        string heardSound = null)
    {
        Spelling = spelling ?? "";
        FriendlySound = friendlySound ?? "";
        HeardSound = heardSound ?? "";
        BeatIndex = Mathf.Max(0, beatIndex);
        Status = status;
        Confidence = Mathf.Clamp01(confidence);
    }

    public PhoneticSoundSegment WithStatus(PhoneticSegmentStatus status, float confidence) =>
        new PhoneticSoundSegment(Spelling, FriendlySound, BeatIndex, status, confidence, HeardSound);

    public PhoneticSoundSegment WithHeardSound(string heardSound, PhoneticSegmentStatus status, float confidence) =>
        new PhoneticSoundSegment(Spelling, FriendlySound, BeatIndex, status, confidence, heardSound);
}

public readonly struct PronunciationInsightRequest
{
    public readonly string TargetWord;
    public readonly string ConfirmedWord;
    public readonly string RawRecognizedText;
    public readonly bool VoskConfirmedWord;
    public readonly byte[] Pcm16Audio;
    public readonly int SampleRate;

    public PronunciationInsightRequest(
        string targetWord,
        string confirmedWord,
        string rawRecognizedText,
        bool voskConfirmedWord,
        byte[] pcm16Audio,
        int sampleRate)
    {
        TargetWord = NormalizeWord(targetWord);
        ConfirmedWord = NormalizeWord(confirmedWord);
        RawRecognizedText = rawRecognizedText ?? "";
        VoskConfirmedWord = voskConfirmedWord;
        Pcm16Audio = pcm16Audio ?? Array.Empty<byte>();
        SampleRate = Mathf.Max(0, sampleRate);
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
}

public readonly struct PronunciationInsightResult
{
    public readonly string ProviderName;
    public readonly string TargetWord;
    public readonly string ConfirmedWord;
    public readonly string RawRecognizedText;
    public readonly bool VoskConfirmedWord;
    public readonly bool AttemptedTarget;
    public readonly float Score;
    public readonly PronunciationHintKey HintKey;
    public readonly PhoneticSoundSegment FocusSegment;
    public readonly IReadOnlyList<PhoneticSoundSegment> Segments;
    public readonly IReadOnlyList<string> SyllableBeats;
    public readonly string Message;

    public PronunciationInsightResult(
        string providerName,
        string targetWord,
        string confirmedWord,
        string rawRecognizedText,
        bool voskConfirmedWord,
        bool attemptedTarget,
        float score,
        PronunciationHintKey hintKey,
        PhoneticSoundSegment focusSegment,
        IReadOnlyList<PhoneticSoundSegment> segments,
        IReadOnlyList<string> syllableBeats,
        string message = null)
    {
        ProviderName = providerName ?? "";
        TargetWord = targetWord ?? "";
        ConfirmedWord = confirmedWord ?? "";
        RawRecognizedText = rawRecognizedText ?? "";
        VoskConfirmedWord = voskConfirmedWord;
        AttemptedTarget = attemptedTarget;
        Score = Mathf.Clamp01(score);
        HintKey = hintKey;
        FocusSegment = focusSegment;
        Segments = segments ?? Array.Empty<PhoneticSoundSegment>();
        SyllableBeats = syllableBeats ?? Array.Empty<string>();
        Message = message ?? "";
    }
}

public interface IPronunciationInsightProvider
{
    bool IsAvailable { get; }
    string Name { get; }
    PronunciationInsightResult Analyze(PronunciationInsightRequest request);
}

public static class PronunciationInsightProviderFactory
{
    public static IPronunciationInsightProvider Create()
    {
        // Pronunciation insight is authoritative on the Azure server. Vosk/STT
        // only gates the response locally; the trimmed WAV is uploaded by the
        // Firebase spoken-phrase pipeline for Azure assessment.
        return new VoskOnlyPronunciationInsightProvider();
    }
}

public sealed class VoskOnlyPronunciationInsightProvider : IPronunciationInsightProvider
{
    public bool IsAvailable => false;
    public string Name => "Vosk-only pronunciation gate";

    public PronunciationInsightResult Analyze(PronunciationInsightRequest request)
    {
        string target = !string.IsNullOrEmpty(request.TargetWord)
            ? request.TargetWord
            : request.ConfirmedWord;
        IReadOnlyList<PhoneticSoundSegment> segments = PronunciationProfileBuilder.BuildSegments(target);
        IReadOnlyList<string> beats = PronunciationProfileBuilder.BuildSyllableBeats(target, segments);
        return new PronunciationInsightResult(
            Name,
            target,
            request.ConfirmedWord,
            request.RawRecognizedText,
            request.VoskConfirmedWord,
            request.VoskConfirmedWord,
            request.VoskConfirmedWord ? 0.96f : 0f,
            request.VoskConfirmedWord ? PronunciationHintKey.GreatTry : PronunciationHintKey.TryAgain,
            segments.Count > 0 ? segments[0] : default,
            segments,
            beats,
            request.VoskConfirmedWord
                ? "Vosk confirmed the word. Detailed pronunciation review is handled by the server."
                : "Vosk did not confirm the word. Detailed pronunciation review is handled by the server.");
    }
}

public sealed class ZipaCrctcPronunciationInsightProvider : PythonPhonemeInsightProvider
{
    public ZipaCrctcPronunciationInsightProvider()
        : base(
            "ZIPA CR-CTC phoneme recognizer",
            "ZIPA CR-CTC",
            "ZIPA CR-CTC phoneme pass failed",
            "zipa_onnx_tester.py",
            null,
            "--precision int8",
            90000)
    {
    }
}

public sealed class CharsiuWav2Vec2PhonemeInsightProvider : PythonPhonemeInsightProvider
{
    public CharsiuWav2Vec2PhonemeInsightProvider()
        : base(
            "Charsiu wav2vec2 phoneme recognizer",
            "Charsiu wav2vec2",
            "Charsiu wav2vec2 phoneme pass failed",
            "phonetic_tester.py",
            ResolveModelPath(),
            "--top-k 120",
            60000)
    {
    }

    static string ResolveModelPath()
    {
        return new ConfigurableLocalSpeechModelPathResolver()
            .ResolvePath(LocalSpeechModelKind.CharsiuPronunciation);
    }
}
