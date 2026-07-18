using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEngine;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using UnityEngine.Windows.Speech;
#endif

public enum SpeechRecognitionStatus
{
    Unavailable,
    Idle,
    RequestingPermission,
    Listening,
    Loading,
    Partial,
    Completed,
    Denied,
    TimedOut,
    Error
}

public readonly struct SpeechRecognitionRequest
{
    public readonly IReadOnlyList<string> Keywords;
    public readonly string LanguageCode;

    public SpeechRecognitionRequest(IReadOnlyList<string> keywords, string languageCode)
    {
        Keywords = keywords;
        LanguageCode = string.IsNullOrWhiteSpace(languageCode) ? "en-US" : languageCode;
    }
}

public readonly struct SpeechRecognitionResult
{
    public readonly IReadOnlyList<string> Alternatives;
    public readonly SpeechRecognitionStatus Status;
    public readonly string Message;
    public readonly byte[] Pcm16Audio;
    public readonly int SampleRate;

    public SpeechRecognitionResult(
        SpeechRecognitionStatus status,
        IReadOnlyList<string> alternatives = null,
        string message = null,
        byte[] pcm16Audio = null,
        int sampleRate = 0)
    {
        Status = status;
        Alternatives = alternatives ?? Array.Empty<string>();
        Message = message ?? "";
        Pcm16Audio = pcm16Audio ?? Array.Empty<byte>();
        SampleRate = sampleRate;
    }
}

public interface ISpeechRecognitionProvider : IDisposable
{
    bool IsAvailable { get; }
    bool IsListening { get; }
    string Name { get; }
    event Action<SpeechRecognitionResult> ResultReceived;
    void Start(SpeechRecognitionRequest request);
    void Stop();
}

public interface ISpeechRecognitionProviderUpdate
{
    void Tick();
}

// Stop cancels an attempt. Releasing a hold-to-talk control must instead flush
// the final STT result and keep its PCM available for pronunciation analysis.
public interface IHoldToTalkSpeechRecognitionProvider
{
    void FinishAttempt();
}

public static class SpeechRecognitionProviderFactory
{
    public static ISpeechRecognitionProvider Create()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return new AndroidSpeechRecognitionProvider();
#elif UNITY_IOS && !UNITY_EDITOR
        return new IosSpeechRecognitionProvider();
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        ISpeechRecognitionProvider offlineProvider = OfflineVoskSpeechRecognitionProvider.CreateIfAvailable();
        if (offlineProvider != null)
            return offlineProvider;
        return new WindowsKeywordSpeechRecognitionProvider();
#else
        return new UnavailableSpeechRecognitionProvider();
#endif
    }
}

public sealed class FakeSpeechRecognitionProvider : ISpeechRecognitionProvider
{
    public bool IsAvailable => true;
    public bool IsListening { get; private set; }
    public string Name => "Fake speech";
    public event Action<SpeechRecognitionResult> ResultReceived;
    public void Start(SpeechRecognitionRequest request) => IsListening = true;
    public void Stop() => IsListening = false;
    public void Dispose() => Stop();
    public void Submit(params string[] alternatives)
    {
        if (!IsListening) return;
        IsListening = false;
        ResultReceived?.Invoke(new SpeechRecognitionResult(SpeechRecognitionStatus.Completed, alternatives));
    }
    public void Fail(SpeechRecognitionStatus status, string message)
    {
        if (!IsListening) return;
        IsListening = false;
        ResultReceived?.Invoke(new SpeechRecognitionResult(status, message: message));
    }
}

public sealed class UnavailableSpeechRecognitionProvider : ISpeechRecognitionProvider
{
    public bool IsAvailable => false;
    public bool IsListening => false;
    public string Name => "Unavailable";
    public event Action<SpeechRecognitionResult> ResultReceived;
    public void Start(SpeechRecognitionRequest request) =>
        ResultReceived?.Invoke(new SpeechRecognitionResult(SpeechRecognitionStatus.Unavailable, message: "Voice input is unavailable."));
    public void Stop() { }
    public void Dispose() { }
}
