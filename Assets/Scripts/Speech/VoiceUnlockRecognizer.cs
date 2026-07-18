using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public class VoiceUnlockRecognizer : MonoBehaviour
{
    public enum VoiceInputMode
    {
        None,
        Manual,
        BuddyConversation,
        WritingListenOnce,
        CombatAutoListen,
    }

    public enum VoiceDisplayState
    {
        Idle,
        Listening,
        Heard,
        Fallback,
        Unavailable,
        PermissionDenied,
        Error
    }

    const float RecentActivityHoldSeconds = 1.1f;
    const float ListeningTimeoutSeconds = 9f;

    readonly List<string> registeredKeywords = new List<string>();
    readonly Dictionary<string, string> acceptedToCanonical =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    readonly Queue<PendingSpeechResult> pendingResults = new Queue<PendingSpeechResult>();
    readonly Queue<PendingVoiceCallback> pendingCallbacks = new Queue<PendingVoiceCallback>();
    readonly Queue<PendingPronunciationInsightRequest> pendingPronunciationInsightRequests =
        new Queue<PendingPronunciationInsightRequest>();
    readonly object pendingResultsLock = new object();
    ISpeechRecognitionProvider provider;
    IPronunciationInsightProvider pronunciationInsightProvider;
    Task<PronunciationInsightResult> pendingPronunciationInsightTask;
    int pendingPronunciationInsightId;
    int pronunciationInsightGeneration;
    byte[] lastCapturedPcm16Audio = Array.Empty<byte>();
    int lastCapturedSampleRate;
    string currentKeyword = "";
    float listeningStartedAt = -1f;
    int recognitionGeneration;
    bool acceptingProviderResults;
    VoiceInputMode activeMode = VoiceInputMode.None;

    public bool IsListening => provider != null && provider.IsListening;
    public bool IsAvailable => provider != null && provider.IsAvailable;
    public string ProviderName => provider != null ? provider.Name : "Unavailable";
    public string CurrentKeyword => currentKeyword;
    public IReadOnlyList<string> RegisteredKeywords => registeredKeywords;
    public string LastRecognizedText { get; private set; } = "";
    public string StatusMessage { get; private set; } = "Tap to speak.";
    public string ActivityDetail { get; private set; } = "";
    public PronunciationInsightResult LastPronunciationInsight { get; private set; }
    public string PronunciationInsightProviderName => pronunciationInsightProvider != null ? pronunciationInsightProvider.Name : "Unavailable";
    public bool CombatPronunciationInsightEnabled { get; set; } = true;
    public VoiceDisplayState CurrentDisplayState { get; private set; } = VoiceDisplayState.Idle;
    public float LastRecognizedAt { get; private set; } = -1f;
    public float LastStateChangedAt { get; private set; } = -1f;
    public float LastActivityAt { get; private set; } = -1f;
    public int ConsecutiveFailures { get; private set; }
    public VoiceInputMode ActiveMode => activeMode;
    public string LanguageOverride { get; set; } = "";

    public readonly struct RecognitionEvent
    {
        public readonly VoiceInputMode Mode;
        public readonly bool Recognized;
        public readonly string Text;
        public readonly string RawText;
        public readonly byte[] Pcm16Audio;
        public readonly int SampleRate;

        public RecognitionEvent(
            VoiceInputMode mode,
            bool recognized,
            string text,
            string rawText,
            byte[] pcm16Audio = null,
            int sampleRate = 0)
        {
            Mode = mode;
            Recognized = recognized;
            Text = text ?? "";
            RawText = rawText ?? "";
            Pcm16Audio = pcm16Audio ?? Array.Empty<byte>();
            SampleRate = sampleRate;
        }
    }

    public event Action<RecognitionEvent> OnRecognitionResolved;
    public event Action<PronunciationInsightResult> OnPronunciationInsightReady;
    public event Action<string> OnKeywordRecognized;
    public event Action<string> OnKeywordRejected;
    public event Action<VoiceDisplayState> OnDisplayStateChanged;

    readonly struct PendingSpeechResult
    {
        public readonly SpeechRecognitionResult Result;
        public readonly int Generation;

        public PendingSpeechResult(SpeechRecognitionResult result, int generation)
        {
            Result = result;
            Generation = generation;
        }
    }

    readonly struct PendingVoiceCallback
    {
        public readonly bool Recognized;
        public readonly string Text;
        public readonly string RawText;
        public readonly byte[] Pcm16Audio;
        public readonly int SampleRate;
        public readonly int Generation;
        public readonly int ReadyFrame;
        public readonly VoiceInputMode Mode;

        public PendingVoiceCallback(
            bool recognized,
            string text,
            string rawText,
            byte[] pcm16Audio,
            int sampleRate,
            int generation,
            int readyFrame,
            VoiceInputMode mode)
        {
            Recognized = recognized;
            Text = text;
            RawText = rawText;
            Pcm16Audio = pcm16Audio ?? Array.Empty<byte>();
            SampleRate = sampleRate;
            Generation = generation;
            ReadyFrame = readyFrame;
            Mode = mode;
        }
    }

    readonly struct PendingPronunciationInsightRequest
    {
        public readonly int Id;
        public readonly PronunciationInsightRequest Request;
        public readonly IPronunciationInsightProvider Provider;

        public PendingPronunciationInsightRequest(
            int id,
            PronunciationInsightRequest request,
            IPronunciationInsightProvider provider)
        {
            Id = id;
            Request = request;
            Provider = provider;
        }
    }

    void Awake()
    {
        SetPronunciationInsightProvider(PronunciationInsightProviderFactory.Create());
        SetProvider(SpeechRecognitionProviderFactory.Create());
    }

    public void SetProvider(ISpeechRecognitionProvider recognitionProvider)
    {
        recognitionGeneration++;
        acceptingProviderResults = false;
        activeMode = VoiceInputMode.None;
        ClearPendingResults();
        pendingCallbacks.Clear();

        if (provider != null)
        {
            provider.ResultReceived -= HandleProviderResult;
            provider.Dispose();
        }

        provider = recognitionProvider ?? new UnavailableSpeechRecognitionProvider();
        provider.ResultReceived += HandleProviderResult;
        SetDisplayState(IsAvailable ? VoiceDisplayState.Idle : VoiceDisplayState.Unavailable,
            IsAvailable ? "Tap to speak." : "Voice input is unavailable.");
    }

    public void SetPronunciationInsightProvider(IPronunciationInsightProvider insightProvider)
    {
        pronunciationInsightProvider = insightProvider ?? PronunciationInsightProviderFactory.Create();
    }

    public void ConfigureKeyword(string keyword, bool autoStart = true) =>
        ConfigureKeywords(new[] { keyword }, keyword, autoStart);

    public void ConfigureKeywords(IEnumerable<string> keywords, string activeKeyword = null, bool autoStart = true)
    {
        ConfigureKeywords(keywords, null, activeKeyword, autoStart);
    }

    public void ConfigureKeywords(
        IEnumerable<string> keywords,
        IDictionary<string, string> aliases,
        string activeKeyword = null,
        bool autoStart = false)
    {
        StopListening();
        recognitionGeneration++;
        acceptingProviderResults = false;
        activeMode = VoiceInputMode.None;
        ClearPendingResults();
        pendingCallbacks.Clear();
        registeredKeywords.Clear();
        acceptedToCanonical.Clear();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (keywords != null)
        {
            foreach (string keyword in keywords)
            {
                string normalized = NormalizeKeyword(keyword);
                if (string.IsNullOrEmpty(normalized) || !seen.Add(normalized)) continue;
                registeredKeywords.Add(normalized);
                acceptedToCanonical[normalized] = normalized;
            }
        }

        if (aliases != null)
        {
            foreach (var pair in aliases)
            {
                string alias = NormalizeKeyword(pair.Key);
                string canonical = NormalizeKeyword(pair.Value);
                if (!string.IsNullOrEmpty(alias) && acceptedToCanonical.ContainsKey(canonical))
                    acceptedToCanonical[alias] = canonical;
            }
        }

        currentKeyword = NormalizeKeyword(activeKeyword);
        if (string.IsNullOrEmpty(currentKeyword) && registeredKeywords.Count > 0)
            currentKeyword = registeredKeywords[0];

        StatusMessage = IsAvailable ? "Tap to speak." : "Voice input is unavailable.";
        SetDisplayState(IsAvailable ? VoiceDisplayState.Idle : VoiceDisplayState.Unavailable, StatusMessage);
        if (autoStart) StartListening();
    }

    public void StartListening()
    {
        StartListening(VoiceInputMode.Manual);
    }

    public void StartListening(VoiceInputMode mode)
    {
        if (PauseMenuController.IsPaused)
        {
            StopListeningIfActive();
            return;
        }

        if (!IsAvailable)
        {
            SetDisplayState(VoiceDisplayState.Unavailable, "Voice input is unavailable.");
            return;
        }
        if (registeredKeywords.Count == 0 && mode != VoiceInputMode.BuddyConversation)
        {
            SetDisplayState(VoiceDisplayState.Error, "No spell words configured.");
            return;
        }

        provider.Stop();
        recognitionGeneration++;
        ClearPendingResults();
        pendingCallbacks.Clear();
        acceptingProviderResults = true;
        activeMode = mode;
        listeningStartedAt = Time.unscaledTime;
        LastRecognizedText = "";
        LastRecognizedAt = -1f;
        LastActivityAt = -1f;
        LastPronunciationInsight = default;
        lastCapturedPcm16Audio = Array.Empty<byte>();
        lastCapturedSampleRate = 0;
        ActivityDetail = "";
        SetDisplayState(VoiceDisplayState.Listening, $"Listening through {provider.Name}.");
        string requestedLanguage = string.IsNullOrWhiteSpace(LanguageOverride)
            ? AccessibilitySettings.SpeechLanguage
            : LanguageOverride.Trim();
        provider.Start(new SpeechRecognitionRequest(new List<string>(acceptedToCanonical.Keys), requestedLanguage));
    }

    public void StopListening(bool preserveRecentActivity = false)
    {
        recognitionGeneration++;
        acceptingProviderResults = false;
        activeMode = VoiceInputMode.None;
        ClearPendingResults();
        pendingCallbacks.Clear();
        provider?.Stop();
        listeningStartedAt = -1f;
        if (!preserveRecentActivity || CurrentDisplayState == VoiceDisplayState.Listening)
            SetDisplayState(IsAvailable ? VoiceDisplayState.Idle : VoiceDisplayState.Unavailable,
                IsAvailable ? "Tap to speak." : "Voice input is unavailable.");
    }

    // Called by a hold-to-talk release. Unlike StopListening this deliberately
    // keeps the current generation and callbacks alive until the provider has
    // returned its one final transcript plus captured PCM audio.
    public void FinishListeningAttempt()
    {
        if (!acceptingProviderResults || activeMode == VoiceInputMode.None)
            return;

        listeningStartedAt = -1f;
        StatusMessage = "Processing your response...";
        ActivityDetail = StatusMessage;
        SetDisplayState(VoiceDisplayState.Listening, StatusMessage);

        if (provider is IHoldToTalkSpeechRecognitionProvider holdToTalkProvider)
            holdToTalkProvider.FinishAttempt();
        else
            provider?.Stop();
    }

    public void SubmitRecognizedText(string recognizedText)
    {
        if (!IsListening) return;
        HandleAlternatives(new[] { recognizedText });
    }

    public void NotifyFallbackTriggered(string detail = null)
    {
        StopListening(true);
        string message = string.IsNullOrWhiteSpace(detail) ? "Fallback selected." : detail.Trim();
        ActivityDetail = message;
        StatusMessage = message;
        LastActivityAt = Time.unscaledTime;
        SetDisplayState(VoiceDisplayState.Fallback, message);
    }

    public PronunciationInsightResult AnalyzePronunciationGuess(
        string targetWord,
        string rawRecognizedText,
        bool recognized)
    {
        if (!CombatPronunciationInsightEnabled)
        {
            Debug.Log($"[Pronunciation] Combat pronunciation review skipped from live guess: no enemy target. target='{targetWord}' raw='{rawRecognizedText}'");
            return default;
        }

        if (pronunciationInsightProvider == null || !pronunciationInsightProvider.IsAvailable)
        {
            Debug.Log($"[Pronunciation] Pronunciation review skipped from live guess: provider unavailable. provider='{PronunciationInsightProviderName}' target='{targetWord}' raw='{rawRecognizedText}'");
            return default;
        }

        string target = NormalizeKeyword(targetWord);
        string rawText = rawRecognizedText ?? "";
        string confirmedWord = recognized ? target : "";
        var request = new PronunciationInsightRequest(
            target,
            confirmedWord,
            rawText,
            recognized,
            lastCapturedPcm16Audio,
            lastCapturedSampleRate);
        BeginPronunciationInsightAnalysis(request);
        return LastPronunciationInsight;
    }

    public byte[] GetLastCapturedPronunciationWav()
    {
        if (lastCapturedPcm16Audio == null || lastCapturedPcm16Audio.Length < 2 || lastCapturedSampleRate <= 0)
            return Array.Empty<byte>();

        return EncodePcm16Wav(lastCapturedPcm16Audio, lastCapturedSampleRate);
    }

    void HandleProviderResult(SpeechRecognitionResult result)
    {
        if (!acceptingProviderResults || PauseMenuController.IsPaused)
            return;

        lock (pendingResultsLock)
            pendingResults.Enqueue(new PendingSpeechResult(result, recognitionGeneration));
    }

    void ProcessProviderResult(SpeechRecognitionResult result)
    {
        switch (result.Status)
        {
            case SpeechRecognitionStatus.Completed:
                VoiceInputMode completedMode = activeMode;
                // Buddy calls are free conversation. Their PCM must never
                // overwrite the expected-phrase evidence used by Azure
                // pronunciation assessment.
                if (completedMode != VoiceInputMode.BuddyConversation)
                    CapturePronunciationAudio(result);
                bool recognized = HandleAlternatives(result.Alternatives, result.Pcm16Audio, result.SampleRate);
                AnalyzePronunciationAttempt(result, recognized, completedMode);
                break;
            case SpeechRecognitionStatus.RequestingPermission:
                SetDisplayState(VoiceDisplayState.Idle, result.Message);
                break;
            case SpeechRecognitionStatus.Loading:
                SetDisplayState(VoiceDisplayState.Listening, result.Message);
                break;
            case SpeechRecognitionStatus.Listening:
                SetDisplayState(VoiceDisplayState.Listening, result.Message);
                break;
            case SpeechRecognitionStatus.Partial:
                if (result.Alternatives != null && result.Alternatives.Count > 0)
                {
                    LastRecognizedText = result.Alternatives[0] ?? "";
                    if (activeMode != VoiceInputMode.BuddyConversation)
                        CapturePronunciationAudio(result);
                    ActivityDetail = string.IsNullOrWhiteSpace(LastRecognizedText)
                        ? ""
                        : $"Guessing \"{LastRecognizedText}\".";
                    LastActivityAt = Time.unscaledTime;

                    string normalized = NormalizeKeyword(LastRecognizedText);
                    if (!string.IsNullOrEmpty(normalized) && acceptedToCanonical.ContainsKey(normalized))
                    {
                        // Partials are display-only. Wait for the Completed result
                        // so Vosk can provide the trimmed PCM attempt audio.
                        ActivityDetail = $"Heard a possible match for \"{LastRecognizedText}\". Keep speaking briefly...";
                    }
                }
                break;
            case SpeechRecognitionStatus.Denied:
                acceptingProviderResults = false;
                activeMode = VoiceInputMode.None;
                ConsecutiveFailures++;
                SetDisplayState(VoiceDisplayState.PermissionDenied, result.Message);
                break;
            case SpeechRecognitionStatus.TimedOut:
                acceptingProviderResults = false;
                activeMode = VoiceInputMode.None;
                ConsecutiveFailures++;
                SetDisplayState(VoiceDisplayState.Error, string.IsNullOrEmpty(result.Message) ? "I did not hear a spell. Tap to retry." : result.Message);
                break;
            case SpeechRecognitionStatus.Unavailable:
                acceptingProviderResults = false;
                activeMode = VoiceInputMode.None;
                ConsecutiveFailures++;
                SetDisplayState(VoiceDisplayState.Unavailable, result.Message);
                break;
            case SpeechRecognitionStatus.Error:
                acceptingProviderResults = false;
                activeMode = VoiceInputMode.None;
                ConsecutiveFailures++;
                SetDisplayState(VoiceDisplayState.Error, result.Message);
                break;
        }
    }

    bool HandleAlternatives(IReadOnlyList<string> alternatives, byte[] pcm16Audio = null, int sampleRate = 0)
    {
        VoiceInputMode completedMode = activeMode;
        acceptingProviderResults = false;
        activeMode = VoiceInputMode.None;
        CompleteProviderResult();
        listeningStartedAt = -1f;
        LastRecognizedAt = Time.unscaledTime;

        if (completedMode == VoiceInputMode.BuddyConversation)
        {
            string conversationText = "";
            if (alternatives != null)
            {
                foreach (string alternative in alternatives)
                {
                    if (string.IsNullOrWhiteSpace(alternative))
                        continue;
                    conversationText = alternative.Trim();
                    break;
                }
            }

            bool heardConversation = !string.IsNullOrWhiteSpace(conversationText);
            LastRecognizedText = conversationText;
            if (heardConversation)
            {
                ConsecutiveFailures = 0;
                StatusMessage = "Heard your Buddy question.";
                ActivityDetail = StatusMessage;
                LastActivityAt = LastRecognizedAt;
                SetDisplayState(VoiceDisplayState.Heard, StatusMessage);
            }
            else
            {
                ConsecutiveFailures++;
                SetDisplayState(VoiceDisplayState.Error, "I did not hear a Buddy question.");
            }

                pendingCallbacks.Enqueue(new PendingVoiceCallback(
                heardConversation,
                conversationText,
                conversationText,
                pcm16Audio,
                sampleRate,
                recognitionGeneration,
                Time.frameCount + 1,
                completedMode));
            return heardConversation;
        }

        if (alternatives != null)
        {
            foreach (string alternative in alternatives)
            {
                string normalized = NormalizeKeyword(alternative);
                if (!acceptedToCanonical.TryGetValue(normalized, out string canonical)) continue;
                LastRecognizedText = alternative ?? canonical;
                ConsecutiveFailures = 0;
                StatusMessage = $"Heard \"{LastRecognizedText}\".";
                ActivityDetail = StatusMessage;
                LastActivityAt = LastRecognizedAt;
                SetDisplayState(VoiceDisplayState.Heard, StatusMessage);
                pendingCallbacks.Enqueue(new PendingVoiceCallback(
                    true,
                    canonical,
                    LastRecognizedText,
                    pcm16Audio,
                    sampleRate,
                    recognitionGeneration,
                    Time.frameCount + 1,
                    completedMode));
                return true;
            }
        }

        LastRecognizedText = alternatives != null && alternatives.Count > 0 ? alternatives[0] : "";
        ConsecutiveFailures++;
        pendingCallbacks.Enqueue(new PendingVoiceCallback(
            false,
            LastRecognizedText,
            LastRecognizedText,
            pcm16Audio,
            sampleRate,
            recognitionGeneration,
            Time.frameCount + 1,
            completedMode));
        SetDisplayState(VoiceDisplayState.Error,
            string.IsNullOrWhiteSpace(LastRecognizedText)
                ? "I did not hear a spell. Tap to retry."
                : $"Heard \"{LastRecognizedText}\", but it was not an unlocked spell.");
        return false;
    }

    void AnalyzePronunciationAttempt(SpeechRecognitionResult speechResult, bool recognized, VoiceInputMode mode)
    {
        if (mode == VoiceInputMode.BuddyConversation)
            return;

        if (mode == VoiceInputMode.CombatAutoListen && !CombatPronunciationInsightEnabled)
        {
            Debug.Log($"[Pronunciation] Combat pronunciation review skipped from speech input {speechResult.Status}: no enemy target. raw='{FormatAlternatives(speechResult.Alternatives)}' {DescribeAudio(speechResult.Pcm16Audio, speechResult.SampleRate)}");
            return;
        }

        if (!recognized)
        {
            Debug.Log($"[Pronunciation] Azure analysis not queued: STT response was not accepted. raw='{FormatAlternatives(speechResult.Alternatives)}'");
            return;
        }

        if (pronunciationInsightProvider == null || !pronunciationInsightProvider.IsAvailable)
        {
            // Pronunciation is intentionally authoritative on the Azure
            // server. The local Vosk gate only decides whether the response
            // is accepted; it must not be mistaken for the pronunciation
            // analysis path. The accepted event uploads the captured WAV and
            // the server result is shown in AzurePronunciationInsightWindow.
            Debug.Log($"[Pronunciation] Local insight disabled; Azure analysis will be queued from the completed response. raw='{FormatAlternatives(speechResult.Alternatives)}' {DescribeAudio(speechResult.Pcm16Audio, speechResult.SampleRate)}");
            return;
        }

        string rawText = speechResult.Alternatives != null && speechResult.Alternatives.Count > 0
            ? speechResult.Alternatives[0]
            : "";
        string confirmedWord = recognized ? ResolveCanonicalWord(rawText) : "";
        string targetWord = !string.IsNullOrWhiteSpace(currentKeyword)
            ? currentKeyword
            : confirmedWord;

        var request = new PronunciationInsightRequest(
            targetWord,
            confirmedWord,
            rawText,
            recognized,
            speechResult.Pcm16Audio,
            speechResult.SampleRate);
        BeginPronunciationInsightAnalysis(request);
    }

    void CapturePronunciationAudio(SpeechRecognitionResult result)
    {
        if (result.Pcm16Audio == null || result.Pcm16Audio.Length < 2 || result.SampleRate <= 0)
        {
            if (result.Status == SpeechRecognitionStatus.Completed)
                Debug.LogWarning($"[Pronunciation] Completed speech had no PCM audio. alternatives='{FormatAlternatives(result.Alternatives)}' {DescribeAudio(result.Pcm16Audio, result.SampleRate)}");
            return;
        }

        lastCapturedPcm16Audio = result.Pcm16Audio;
        lastCapturedSampleRate = result.SampleRate;
        if (result.Status == SpeechRecognitionStatus.Completed)
            Debug.Log($"[Pronunciation] Captured completed speech audio alternatives='{FormatAlternatives(result.Alternatives)}' {DescribeAudio(lastCapturedPcm16Audio, lastCapturedSampleRate)}");
    }

    void BeginPronunciationInsightAnalysis(PronunciationInsightRequest request)
    {
        if (pronunciationInsightProvider == null || !pronunciationInsightProvider.IsAvailable)
        {
            Debug.Log($"[Pronunciation] Pronunciation review skipped: provider unavailable. provider='{PronunciationInsightProviderName}' target='{request.TargetWord}' raw='{request.RawRecognizedText}' {DescribeAudio(request.Pcm16Audio, request.SampleRate)}");
            return;
        }

        IPronunciationInsightProvider providerSnapshot = pronunciationInsightProvider;
        int requestId = ++pronunciationInsightGeneration;
        Debug.Log($"[Pronunciation] Pronunciation review queued id={requestId} provider='{providerSnapshot.Name}' target='{request.TargetWord}' confirmed='{request.ConfirmedWord}' raw='{request.RawRecognizedText}' voskConfirmed={request.VoskConfirmedWord} {DescribeAudio(request.Pcm16Audio, request.SampleRate)}");
        pendingPronunciationInsightRequests.Enqueue(new PendingPronunciationInsightRequest(requestId, request, providerSnapshot));
        StartNextPronunciationInsightIfIdle();
    }

    void StartNextPronunciationInsightIfIdle()
    {
        if (pendingPronunciationInsightTask != null || pendingPronunciationInsightRequests.Count == 0)
            return;

        PendingPronunciationInsightRequest pending = pendingPronunciationInsightRequests.Dequeue();
        pendingPronunciationInsightId = pending.Id;
        Debug.Log($"[Pronunciation] Pronunciation review started id={pending.Id} provider='{pending.Provider.Name}' target='{pending.Request.TargetWord}' raw='{pending.Request.RawRecognizedText}'");
        pendingPronunciationInsightTask = Task.Run(() => pending.Provider.Analyze(pending.Request));
    }

    void PollPronunciationInsight()
    {
        StartNextPronunciationInsightIfIdle();
        Task<PronunciationInsightResult> task = pendingPronunciationInsightTask;
        if (task == null || !task.IsCompleted)
            return;

        int requestId = pendingPronunciationInsightId;
        pendingPronunciationInsightTask = null;

        if (task.IsFaulted)
        {
            Debug.LogWarning($"[Pronunciation] Pronunciation review failed id={requestId}: {task.Exception?.GetBaseException().Message}");
            StartNextPronunciationInsightIfIdle();
            return;
        }

        LastPronunciationInsight = task.Result;
        Debug.Log($"[Pronunciation] Pronunciation review complete id={requestId} provider='{LastPronunciationInsight.ProviderName}' target='{LastPronunciationInsight.TargetWord}' raw='{LastPronunciationInsight.RawRecognizedText}' score={LastPronunciationInsight.Score:0.00} attempted={LastPronunciationInsight.AttemptedTarget} voskConfirmed={LastPronunciationInsight.VoskConfirmedWord} hint={LastPronunciationInsight.HintKey} segments='{DescribeSegments(LastPronunciationInsight.Segments)}' message='{LastPronunciationInsight.Message}'");
        OnPronunciationInsightReady?.Invoke(LastPronunciationInsight);
        StartNextPronunciationInsightIfIdle();
    }

    static string FormatAlternatives(IReadOnlyList<string> alternatives)
    {
        return alternatives == null || alternatives.Count == 0
            ? ""
            : string.Join("|", alternatives);
    }

    static string DescribeAudio(byte[] pcm16Audio, int sampleRate)
    {
        int bytes = pcm16Audio?.Length ?? 0;
        float duration = sampleRate > 0 ? bytes / (sampleRate * 2f) : 0f;
        int peak = 0;
        double sumSquares = 0d;
        int sampleCount = bytes / 2;
        if (pcm16Audio != null)
        {
            for (int i = 0; i + 1 < bytes; i += 2)
            {
                short sample = (short)(pcm16Audio[i] | (pcm16Audio[i + 1] << 8));
                int abs = Mathf.Abs(sample);
                if (abs > peak)
                    peak = abs;
                sumSquares += sample * (double)sample;
            }
        }

        float rms = sampleCount > 0
            ? Mathf.Sqrt((float)(sumSquares / sampleCount))
            : 0f;
        return $"audioBytes={bytes} sampleRate={sampleRate} duration={duration:0.00}s peak={peak} rms={rms:0}";
    }

    static byte[] EncodePcm16Wav(byte[] pcm16Audio, int sampleRate)
    {
        if (pcm16Audio == null || pcm16Audio.Length < 2 || sampleRate <= 0)
            return Array.Empty<byte>();

        using var stream = new MemoryStream(44 + pcm16Audio.Length);
        using var writer = new BinaryWriter(stream);
        int byteRate = sampleRate * 2;
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + pcm16Audio.Length);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(pcm16Audio.Length);
        writer.Write(pcm16Audio);
        writer.Flush();
        return stream.ToArray();
    }

    static string DescribeSegments(IReadOnlyList<PhoneticSoundSegment> segments)
    {
        if (segments == null || segments.Count == 0)
            return "";

        var parts = new List<string>(segments.Count);
        foreach (PhoneticSoundSegment segment in segments)
        {
            string expected = string.IsNullOrWhiteSpace(segment.FriendlySound)
                ? segment.Spelling
                : segment.FriendlySound;
            string heard = string.IsNullOrWhiteSpace(segment.HeardSound)
                ? "-"
                : segment.HeardSound;
            parts.Add($"{expected}:{segment.Status}:{heard}:{segment.Confidence:0.00}");
        }

        return string.Join(", ", parts);
    }

    string ResolveCanonicalWord(string rawText)
    {
        string normalized = NormalizeKeyword(rawText);
        return acceptedToCanonical.TryGetValue(normalized, out string canonical) ? canonical : "";
    }

    void CompleteProviderResult()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (provider is WindowsKeywordSpeechRecognitionProvider windowsProvider)
        {
            windowsProvider.AcknowledgeResult();
            return;
        }
#endif
        provider?.Stop();
    }

    void ClearPendingResults()
    {
        lock (pendingResultsLock)
            pendingResults.Clear();
    }

    void Update()
    {
        if (PauseMenuController.IsPaused)
        {
            StopListeningIfActive();
            return;
        }

        if (provider is ISpeechRecognitionProviderUpdate tickProvider)
            tickProvider.Tick();

        while (true)
        {
            PendingSpeechResult pending;
            lock (pendingResultsLock)
            {
                if (pendingResults.Count == 0) break;
                pending = pendingResults.Dequeue();
            }
            if (pending.Generation == recognitionGeneration)
                ProcessProviderResult(pending.Result);
        }

        while (pendingCallbacks.Count > 0)
        {
            PendingVoiceCallback callback = pendingCallbacks.Peek();
            if (callback.ReadyFrame > Time.frameCount)
                break;

            pendingCallbacks.Dequeue();
            if (callback.Generation != recognitionGeneration)
                continue;

            var resolved = new RecognitionEvent(
                callback.Mode,
                callback.Recognized,
                callback.Text,
                callback.RawText,
                callback.Pcm16Audio,
                callback.SampleRate);
            OnRecognitionResolved?.Invoke(resolved);

            if (callback.Mode == VoiceInputMode.Manual)
            {
                if (callback.Recognized)
                    OnKeywordRecognized?.Invoke(callback.Text);
                else
                    OnKeywordRejected?.Invoke(callback.Text);
            }
        }

        PollPronunciationInsight();

        if (CurrentDisplayState == VoiceDisplayState.Listening && listeningStartedAt >= 0f &&
            Time.unscaledTime - listeningStartedAt >= ListeningTimeoutSeconds)
        {
            provider?.Stop();
            ProcessProviderResult(new SpeechRecognitionResult(SpeechRecognitionStatus.TimedOut));
        }

        if ((CurrentDisplayState == VoiceDisplayState.Heard || CurrentDisplayState == VoiceDisplayState.Fallback) &&
            LastStateChangedAt >= 0f && Time.unscaledTime - LastStateChangedAt >= RecentActivityHoldSeconds)
            SetDisplayState(IsAvailable ? VoiceDisplayState.Idle : VoiceDisplayState.Unavailable,
                IsAvailable ? "Tap to speak." : "Voice input is unavailable.");
    }

    void OnApplicationPause(bool paused)
    {
        if (paused) StopListening();
    }

    void OnDisable() => StopListening();

    void StopListeningIfActive()
    {
        bool hasPendingResults;
        lock (pendingResultsLock)
            hasPendingResults = pendingResults.Count > 0;

        if (acceptingProviderResults ||
            activeMode != VoiceInputMode.None ||
            IsListening ||
            hasPendingResults ||
            pendingCallbacks.Count > 0)
            StopListening();
    }

    void OnDestroy()
    {
        if (provider == null) return;
        provider.ResultReceived -= HandleProviderResult;
        provider.Dispose();
        provider = null;
    }

    void SetDisplayState(VoiceDisplayState state, string message)
    {
        if (!string.IsNullOrWhiteSpace(message)) StatusMessage = message;
        if (state != VoiceDisplayState.Heard && state != VoiceDisplayState.Fallback) ActivityDetail = "";
        if (CurrentDisplayState == state && Mathf.Approximately(LastStateChangedAt, Time.unscaledTime)) return;
        CurrentDisplayState = state;
        LastStateChangedAt = Time.unscaledTime;
        OnDisplayStateChanged?.Invoke(state);
    }

    public static string NormalizeKeyword(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return "";
        var normalized = new StringBuilder(keyword.Length);
        bool pendingSpace = false;
        foreach (char value in keyword.Trim())
        {
            if (char.IsLetterOrDigit(value))
            {
                if (pendingSpace && normalized.Length > 0) normalized.Append(' ');
                normalized.Append(char.ToUpperInvariant(value));
                pendingSpace = false;
            }
            else if (char.IsWhiteSpace(value) || char.IsPunctuation(value) || char.IsSymbol(value))
                pendingSpace = normalized.Length > 0;
        }
        return normalized.ToString();
    }
}
