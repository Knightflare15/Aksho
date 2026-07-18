using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEngine;

#if UNITY_STANDALONE || UNITY_EDITOR
public sealed class OfflineVoskSpeechRecognitionProvider : ISpeechRecognitionProvider, ISpeechRecognitionProviderUpdate, IHoldToTalkSpeechRecognitionProvider
{
    const int SampleRate = 16000;
    const int UnityMicrophoneSampleRate = 44100;
    const int MicrophoneBufferSeconds = 3;
    const float MinimumRecognitionSeconds = 0.25f;
    const float MaxCapturedAttemptSeconds = 8f;

    sealed class VoskRuntime
    {
        public readonly Type ModelType;
        public readonly Type RecognizerType;
        public readonly MethodInfo AcceptWaveformMethod;
        public readonly MethodInfo ResultMethod;
        public readonly MethodInfo PartialResultMethod;
        public readonly MethodInfo FinalResultMethod;
        public readonly MethodInfo DisposeRecognizerMethod;
        public readonly MethodInfo SetWordsMethod;
        public readonly MethodInfo SetLogLevelMethod;
        public readonly ConstructorInfo GrammarRecognizerConstructor;
        public readonly ConstructorInfo BasicRecognizerConstructor;

        public VoskRuntime(
            Type modelType,
            Type recognizerType,
            MethodInfo acceptWaveformMethod,
            MethodInfo resultMethod,
            MethodInfo partialResultMethod,
            MethodInfo finalResultMethod,
            MethodInfo disposeRecognizerMethod,
            MethodInfo setWordsMethod,
            MethodInfo setLogLevelMethod,
            ConstructorInfo grammarRecognizerConstructor,
            ConstructorInfo basicRecognizerConstructor)
        {
            ModelType = modelType;
            RecognizerType = recognizerType;
            AcceptWaveformMethod = acceptWaveformMethod;
            ResultMethod = resultMethod;
            PartialResultMethod = partialResultMethod;
            FinalResultMethod = finalResultMethod;
            DisposeRecognizerMethod = disposeRecognizerMethod;
            SetWordsMethod = setWordsMethod;
            SetLogLevelMethod = setLogLevelMethod;
            GrammarRecognizerConstructor = grammarRecognizerConstructor;
            BasicRecognizerConstructor = basicRecognizerConstructor;
        }
    }

    sealed class RecognitionSession
    {
        readonly VoskRuntime runtime;
        readonly object recognizer;
        readonly HashSet<string> acceptedKeywords;
        readonly Queue<byte[]> audioQueue = new Queue<byte[]>();
        readonly List<byte> capturedAudio = new List<byte>();
        readonly object audioLock = new object();
        readonly Queue<PendingProviderResult> results;
        readonly object resultLock;
        readonly Thread worker;
        readonly int generation;
        int processedAudioBytes;
        bool running = true;
        bool resultSubmitted;
        string latestCompletedText = "";

        public RecognitionSession(
            VoskRuntime runtime,
            object recognizer,
            IReadOnlyList<string> acceptedKeywords,
            Queue<PendingProviderResult> results,
            object resultLock,
            int generation)
        {
            this.runtime = runtime;
            this.recognizer = recognizer;
            this.acceptedKeywords = new HashSet<string>(acceptedKeywords ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            this.results = results;
            this.resultLock = resultLock;
            this.generation = generation;
            worker = new Thread(Run) { IsBackground = true, Name = "Vosk speech recognition" };
            worker.Start();
        }

        public void Enqueue(byte[] pcmBytes)
        {
            if (pcmBytes == null || pcmBytes.Length == 0)
                return;

            lock (audioLock)
            {
                if (!running)
                    return;

                audioQueue.Enqueue(pcmBytes);
                Monitor.Pulse(audioLock);
            }
        }

        public void RequestStop()
        {
            lock (audioLock)
            {
                running = false;
                Monitor.Pulse(audioLock);
            }
        }

        void Run()
        {
            try
            {
                while (true)
                {
                    byte[] chunk;
                    lock (audioLock)
                    {
                        while (running && audioQueue.Count == 0)
                            Monitor.Wait(audioLock, 100);

                        if (!running && audioQueue.Count == 0)
                            break;

                        chunk = audioQueue.Dequeue();
                    }

                    processedAudioBytes += chunk.Length;
                    AppendCapturedAudio(chunk);
                    bool utteranceCompleted = (bool)runtime.AcceptWaveformMethod.Invoke(
                        recognizer,
                        new object[] { chunk, chunk.Length });

                    if (!utteranceCompleted)
                    {
                        if (SubmitPartialResult(runtime.PartialResultMethod?.Invoke(recognizer, null) as string))
                            break;
                        continue;
                    }

                    // Vosk can endpoint before the player releases the held
                    // control. Preserve that text, but wait to emit the final
                    // result (and trim the full clip) until release.
                    string candidate = SanitizeRecognizedText(
                        ExtractJsonString(runtime.ResultMethod.Invoke(recognizer, null) as string, "text"));
                    if (!string.IsNullOrWhiteSpace(candidate))
                        latestCompletedText = candidate;
                }
            }
            catch (Exception ex)
            {
                SubmitResult(new SpeechRecognitionResult(SpeechRecognitionStatus.Error, message: RootMessage(ex)));
            }
            finally
            {
                try
                {
                    // Releasing hold-to-talk requests a stop, but the queued
                    // PCM still needs one final Vosk result. Do not suppress
                    // the final flush merely because stop was requested.
                    if (!resultSubmitted)
                        SubmitCompletedResult(runtime.FinalResultMethod.Invoke(recognizer, null) as string, latestCompletedText);
                }
                catch
                {
                    // The final result is best-effort during cancellation.
                }

                try
                {
                    runtime.DisposeRecognizerMethod?.Invoke(recognizer, null);
                }
                catch
                {
                    // Native recognizer cleanup should never bubble back into gameplay.
                }
            }
        }

        bool SubmitCompletedResult(string json, string fallbackText = "")
        {
            if (resultSubmitted)
                return true;

            string text = ExtractJsonString(json, "text");
            text = SanitizeRecognizedText(text);
            if (string.IsNullOrWhiteSpace(text))
                text = SanitizeRecognizedText(fallbackText);
            if (string.IsNullOrWhiteSpace(text))
                return false;

            if (processedAudioBytes < SampleRate * 2 * MinimumRecognitionSeconds)
                return false;

            resultSubmitted = true;
            TrimmedPronunciationAudio trimmed = PronunciationAudioTrimmer.Trim(capturedAudio.ToArray(), SampleRate);
            Debug.Log($"[Pronunciation] Vosk completed audio trimmed source={trimmed.SourceSeconds:0.00}s voiced={trimmed.TrimmedSeconds:0.00}s hasSpeech={trimmed.HasSpeech} bytes={trimmed.Pcm16Audio.Length}");
            SubmitResult(new SpeechRecognitionResult(
                SpeechRecognitionStatus.Completed,
                new[] { text },
                pcm16Audio: trimmed.Pcm16Audio,
                sampleRate: SampleRate));
            return true;
        }

        bool SubmitPartialResult(string json)
        {
            if (resultSubmitted)
                return true;

            string text = SanitizeRecognizedText(ExtractJsonString(json, "partial"));
            if (string.IsNullOrWhiteSpace(text))
                return false;

            SubmitResult(new SpeechRecognitionResult(
                SpeechRecognitionStatus.Partial,
                new[] { text },
                // Partials are transcript updates only. Do not trim or attach
                // audio until Vosk emits the final completed result after the
                // user releases hold-to-talk.
                pcm16Audio: Array.Empty<byte>(),
                sampleRate: SampleRate));
            return false;
        }

        void SubmitResult(SpeechRecognitionResult result)
        {
            lock (resultLock)
                results.Enqueue(new PendingProviderResult(result, generation));
        }

        void AppendCapturedAudio(byte[] chunk)
        {
            if (chunk == null || chunk.Length == 0)
                return;

            int maxBytes = Mathf.RoundToInt(SampleRate * 2f * MaxCapturedAttemptSeconds);
            if (capturedAudio.Count + chunk.Length > maxBytes)
            {
                int overflow = capturedAudio.Count + chunk.Length - maxBytes;
                capturedAudio.RemoveRange(0, Mathf.Min(overflow, capturedAudio.Count));
            }

            capturedAudio.AddRange(chunk);
        }

    }

    readonly struct PendingProviderResult
    {
        public readonly SpeechRecognitionResult Result;
        public readonly int Generation;

        public PendingProviderResult(SpeechRecognitionResult result, int generation)
        {
            Result = result;
            Generation = generation;
        }
    }

    readonly VoskRuntime runtime;
    readonly string modelPath;
    readonly Queue<PendingProviderResult> pendingResults = new Queue<PendingProviderResult>();
    readonly object pendingResultsLock = new object();
    readonly object modelLock = new object();
    object model;
    Thread modelLoadThread;
    bool modelLoadStarted;
    bool modelReady;
    string modelLoadError = "";

    RecognitionSession activeSession;
    SpeechRecognitionRequest pendingRequest;
    AudioClip microphoneClip;
    string microphoneDevice;
    int lastSamplePosition;
    int microphoneSampleRate = UnityMicrophoneSampleRate;
    int generation;
    bool listening;
    bool waitingForModel;

    OfflineVoskSpeechRecognitionProvider(VoskRuntime runtime, string modelPath)
    {
        this.runtime = runtime;
        this.modelPath = modelPath;
        BeginModelLoad();
    }

    public bool IsAvailable => runtime != null && IsUsableModelDirectory(modelPath) && string.IsNullOrEmpty(modelLoadError);
    public bool IsListening => listening || waitingForModel;
    public string Name => "Offline Vosk";
    public event Action<SpeechRecognitionResult> ResultReceived;

    public static ISpeechRecognitionProvider CreateIfAvailable(ILocalSpeechModelPathResolver modelPathResolver = null)
    {
        if (!TryCreateRuntime(out VoskRuntime runtime))
            return null;

        ILocalSpeechModelPathResolver paths = modelPathResolver ?? new ConfigurableLocalSpeechModelPathResolver();
        string modelPath = paths.ResolvePath(LocalSpeechModelKind.VoskRecognition);
        if (!IsUsableModelDirectory(modelPath))
            return null;

        return new OfflineVoskSpeechRecognitionProvider(runtime, modelPath);
    }

    public void Start(SpeechRecognitionRequest request)
    {
        if (request.Keywords == null || request.Keywords.Count == 0)
        {
            ResultReceived?.Invoke(new SpeechRecognitionResult(SpeechRecognitionStatus.Error, message: "No spell words configured."));
            return;
        }

        Stop();
        generation++;
        pendingRequest = request;

        if (!IsAvailable)
        {
            ResultReceived?.Invoke(new SpeechRecognitionResult(
                SpeechRecognitionStatus.Unavailable,
                message: string.IsNullOrEmpty(modelLoadError) ? "Offline speech model is unavailable." : modelLoadError));
            return;
        }

        if (!modelReady)
        {
            waitingForModel = true;
            BeginModelLoad();
            ResultReceived?.Invoke(new SpeechRecognitionResult(
                SpeechRecognitionStatus.Loading,
                message: "Loading offline speech model..."));
            return;
        }

        BeginMicrophoneSession();
    }

    public void Tick()
    {
        if (waitingForModel)
        {
            if (!string.IsNullOrEmpty(modelLoadError))
            {
                waitingForModel = false;
                ResultReceived?.Invoke(new SpeechRecognitionResult(SpeechRecognitionStatus.Error, message: modelLoadError));
            }
            else if (modelReady)
            {
                waitingForModel = false;
                BeginMicrophoneSession();
            }
        }

        PumpMicrophoneAudio();
        DrainResults();
    }

    public void Stop()
    {
        generation++;
        StopInternal();
    }

    public void FinishAttempt()
    {
        if (!listening && activeSession == null)
            return;

        // Capture the final microphone frames first, then let the worker drain
        // them and invoke Vosk.FinalResult(). Do not advance generation here:
        // this is completion, not cancellation.
        PumpMicrophoneAudio();
        listening = false;
        StopMicrophone();
        StopSession();
    }

    void StopInternal()
    {
        waitingForModel = false;
        listening = false;
        StopMicrophone();
        StopSession();
    }

    public void Dispose()
    {
        Stop();
        lock (modelLock)
        {
            if (model is IDisposable disposable)
                disposable.Dispose();
            model = null;
            modelReady = false;
        }
    }

    void BeginModelLoad()
    {
        if (modelLoadStarted || modelReady)
            return;

        modelLoadStarted = true;
        modelLoadThread = new Thread(() =>
        {
            try
            {
                runtime.SetLogLevelMethod?.Invoke(null, new object[] { -1 });
                object loadedModel = Activator.CreateInstance(runtime.ModelType, modelPath);
                lock (modelLock)
                {
                    model = loadedModel;
                    modelReady = true;
                }
            }
            catch (Exception ex)
            {
                modelLoadError = $"Offline speech model failed to load: {RootMessage(ex)}";
            }
        })
        {
            IsBackground = true,
            Name = "Vosk model loading"
        };
        modelLoadThread.Start();
    }

    void BeginMicrophoneSession()
    {
        StopMicrophone();
        StopSession();

        string[] devices = Microphone.devices;
        if (devices == null || devices.Length == 0)
        {
            ResultReceived?.Invoke(new SpeechRecognitionResult(SpeechRecognitionStatus.Unavailable, message: "No microphone was detected."));
            return;
        }

        microphoneDevice = ResolveMicrophoneDevice(devices);
        int requestedFrequency = ResolveMicrophoneFrequency(microphoneDevice);
        microphoneClip = Microphone.Start(microphoneDevice, true, MicrophoneBufferSeconds, requestedFrequency);
        if (microphoneClip == null)
        {
            ResultReceived?.Invoke(new SpeechRecognitionResult(SpeechRecognitionStatus.Error, message: "Could not start the microphone."));
            return;
        }

        microphoneSampleRate = Mathf.Max(1, microphoneClip.frequency);
        lastSamplePosition = 0;
        object recognizer = CreateRecognizer(pendingRequest);
        activeSession = new RecognitionSession(runtime, recognizer, pendingRequest.Keywords, pendingResults, pendingResultsLock, generation);
        listening = true;
        ResultReceived?.Invoke(new SpeechRecognitionResult(
            SpeechRecognitionStatus.Listening,
            message: $"Unity mic '{(string.IsNullOrEmpty(microphoneDevice) ? "Default" : microphoneDevice)}' at {microphoneSampleRate} Hz."));
    }

        object CreateRecognizer(SpeechRecognitionRequest request)
        {
            object loadedModel;
            lock (modelLock)
                loadedModel = model;

            bool unrestrictedDictation = request.Keywords == null || request.Keywords.Count == 0;
            string grammar = unrestrictedDictation ? "" : BuildGrammar(request.Keywords);
            object recognizer = unrestrictedDictation && runtime.BasicRecognizerConstructor != null
                ? runtime.BasicRecognizerConstructor.Invoke(new object[] { loadedModel, (float)SampleRate })
                : runtime.GrammarRecognizerConstructor != null
                    ? runtime.GrammarRecognizerConstructor.Invoke(new object[] { loadedModel, (float)SampleRate, grammar })
                    : runtime.BasicRecognizerConstructor.Invoke(new object[] { loadedModel, (float)SampleRate });
            runtime.SetWordsMethod?.Invoke(recognizer, new object[] { true });
            return recognizer;
        }

    void PumpMicrophoneAudio()
    {
        if (!listening || activeSession == null || microphoneClip == null || !Microphone.IsRecording(microphoneDevice))
            return;

        int position = Microphone.GetPosition(microphoneDevice);
        if (position < 0 || position == lastSamplePosition)
            return;

        int available = position > lastSamplePosition
            ? position - lastSamplePosition
            : microphoneClip.samples - lastSamplePosition + position;

        if (available < Mathf.Max(1, microphoneSampleRate / 20))
            return;

        if (position > lastSamplePosition)
        {
            EnqueueSamples(lastSamplePosition, available);
        }
        else
        {
            int tailCount = microphoneClip.samples - lastSamplePosition;
            EnqueueSamples(lastSamplePosition, tailCount);
            if (position > 0)
                EnqueueSamples(0, position);
        }

        lastSamplePosition = position;
    }

    void EnqueueSamples(int offset, int count)
    {
        if (count <= 0)
            return;

        var samples = new float[count];
        microphoneClip.GetData(samples, offset);
        activeSession?.Enqueue(ConvertToPcm16(samples, microphoneSampleRate, SampleRate));
    }

    void DrainResults()
    {
        while (true)
        {
            SpeechRecognitionResult result;
            lock (pendingResultsLock)
            {
                if (pendingResults.Count == 0)
                    break;

                PendingProviderResult pending = pendingResults.Dequeue();
                if (pending.Generation != generation)
                    continue;

                result = pending.Result;
            }

            if (IsTerminalResult(result.Status))
                StopInternal();
            ResultReceived?.Invoke(result);
        }
    }

    static bool IsTerminalResult(SpeechRecognitionStatus status)
    {
        return status == SpeechRecognitionStatus.Completed ||
               status == SpeechRecognitionStatus.Denied ||
               status == SpeechRecognitionStatus.TimedOut ||
               status == SpeechRecognitionStatus.Unavailable ||
               status == SpeechRecognitionStatus.Error;
    }

    void StopMicrophone()
    {
        if (microphoneClip != null && Microphone.IsRecording(microphoneDevice))
            Microphone.End(microphoneDevice);

        microphoneClip = null;
        microphoneDevice = null;
        lastSamplePosition = 0;
        microphoneSampleRate = UnityMicrophoneSampleRate;
    }

    void StopSession()
    {
        activeSession?.RequestStop();
        activeSession = null;
    }

    static bool TryCreateRuntime(out VoskRuntime runtime)
    {
        runtime = null;
        Type modelType = FindType("Vosk.Model");
        Type recognizerType = FindType("Vosk.VoskRecognizer");
        if (modelType == null || recognizerType == null)
            return false;

        MethodInfo acceptWaveform = recognizerType.GetMethod("AcceptWaveform", new[] { typeof(byte[]), typeof(int) });
        MethodInfo result = recognizerType.GetMethod("Result", Type.EmptyTypes);
        MethodInfo partialResult = recognizerType.GetMethod("PartialResult", Type.EmptyTypes);
        MethodInfo finalResult = recognizerType.GetMethod("FinalResult", Type.EmptyTypes);
        ConstructorInfo grammarConstructor = recognizerType.GetConstructor(new[] { modelType, typeof(float), typeof(string) });
        ConstructorInfo basicConstructor = recognizerType.GetConstructor(new[] { modelType, typeof(float) });
        if (acceptWaveform == null || result == null || finalResult == null ||
            (grammarConstructor == null && basicConstructor == null))
            return false;

        Type voskType = FindType("Vosk.Vosk");
        runtime = new VoskRuntime(
            modelType,
            recognizerType,
            acceptWaveform,
            result,
            partialResult,
            finalResult,
            recognizerType.GetMethod("Dispose", Type.EmptyTypes),
            recognizerType.GetMethod("SetWords", new[] { typeof(bool) }),
            voskType?.GetMethod("SetLogLevel", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(int) }, null),
            grammarConstructor,
            basicConstructor);
        return true;
    }

    static Type FindType(string fullName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type = assembly.GetType(fullName);
            if (type != null)
                return type;
        }

        return Type.GetType(fullName);
    }

    static bool IsUsableModelDirectory(string path)
    {
        return !string.IsNullOrEmpty(path) &&
               Directory.Exists(path) &&
               Directory.Exists(Path.Combine(path, "am")) &&
               Directory.Exists(Path.Combine(path, "conf"));
    }

    static string ResolveMicrophoneDevice(IReadOnlyList<string> devices)
    {
        string selected = GameSettings.SelectedMicrophone;
        if (!string.IsNullOrWhiteSpace(selected))
        {
            for (int i = 0; i < devices.Count; i++)
                if (string.Equals(devices[i], selected, StringComparison.OrdinalIgnoreCase))
                    return devices[i];
        }

        return null;
    }

    static int ResolveMicrophoneFrequency(string device)
    {
        int min;
        int max;
        Microphone.GetDeviceCaps(device, out min, out max);
        if (min == 0 && max == 0)
            return UnityMicrophoneSampleRate;

        if (max > 0 && UnityMicrophoneSampleRate > max)
            return max;

        if (min > 0 && UnityMicrophoneSampleRate < min)
            return min;

        return UnityMicrophoneSampleRate;
    }

    static string BuildGrammar(IReadOnlyList<string> keywords)
    {
        var builder = new StringBuilder("[");
        bool wroteAny = false;
        if (keywords != null)
        {
            foreach (string keyword in keywords)
            {
                if (string.IsNullOrWhiteSpace(keyword))
                    continue;

                if (wroteAny)
                    builder.Append(',');

                AppendJsonString(builder, keyword.ToLowerInvariant());
                wroteAny = true;
            }
        }

        if (wroteAny)
            builder.Append(',');

        AppendJsonString(builder, "[unk]");
        builder.Append(']');
        return builder.ToString();
    }

    static void AppendJsonString(StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }
        builder.Append('"');
    }

    static byte[] ConvertToPcm16(IReadOnlyList<float> samples, int sourceRate, int targetRate)
    {
        sourceRate = Mathf.Max(1, sourceRate);
        targetRate = Mathf.Max(1, targetRate);

        if (sourceRate == targetRate)
        {
            var directBytes = new byte[samples.Count * 2];
            for (int i = 0; i < samples.Count; i++)
                WritePcm16(directBytes, i, samples[i]);
            return directBytes;
        }

        int targetCount = Mathf.Max(1, Mathf.RoundToInt(samples.Count * (targetRate / (float)sourceRate)));
        var bytes = new byte[targetCount * 2];
        float ratio = sourceRate / (float)targetRate;
        for (int i = 0; i < targetCount; i++)
        {
            float sourcePosition = i * ratio;
            int index = Mathf.Clamp(Mathf.FloorToInt(sourcePosition), 0, samples.Count - 1);
            int nextIndex = Mathf.Min(index + 1, samples.Count - 1);
            float t = Mathf.Clamp01(sourcePosition - index);
            float value = Mathf.Lerp(samples[index], samples[nextIndex], t);
            WritePcm16(bytes, i, value);
        }

        return bytes;
    }

    static void WritePcm16(byte[] bytes, int sampleIndex, float sample)
    {
        float clamped = Mathf.Clamp(sample, -1f, 1f);
        short value = (short)Mathf.RoundToInt(clamped * short.MaxValue);
        int byteIndex = sampleIndex * 2;
        bytes[byteIndex] = (byte)(value & 0xff);
        bytes[byteIndex + 1] = (byte)((value >> 8) & 0xff);
    }

    static string ExtractJsonString(string json, string key)
    {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
            return "";

        string marker = $"\"{key}\"";
        int keyIndex = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (keyIndex < 0)
            return "";

        int colonIndex = json.IndexOf(':', keyIndex + marker.Length);
        if (colonIndex < 0)
            return "";

        int quoteIndex = json.IndexOf('"', colonIndex + 1);
        if (quoteIndex < 0)
            return "";

        var value = new StringBuilder();
        bool escaped = false;
        for (int i = quoteIndex + 1; i < json.Length; i++)
        {
            char c = json[i];
            if (escaped)
            {
                value.Append(c);
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                continue;
            }

            if (c == '"')
                break;

            value.Append(c);
        }

        return value.ToString();
    }

    static string SanitizeRecognizedText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        string trimmed = text.Trim();
        if (string.Equals(trimmed, "[unk]", StringComparison.OrdinalIgnoreCase))
            return "";

        return trimmed.Replace("[unk]", "", StringComparison.OrdinalIgnoreCase).Trim();
    }

    static string NormalizeSpeechText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var builder = new StringBuilder(text.Length);
        bool pendingSpace = false;
        foreach (char c in text.Trim())
        {
            if (char.IsLetterOrDigit(c))
            {
                if (pendingSpace && builder.Length > 0)
                    builder.Append(' ');
                builder.Append(char.ToUpperInvariant(c));
                pendingSpace = false;
            }
            else if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c))
            {
                pendingSpace = builder.Length > 0;
            }
        }

        return builder.ToString();
    }

    static string RootMessage(Exception ex)
    {
        while (ex is TargetInvocationException && ex.InnerException != null)
            ex = ex.InnerException;

        return ex.Message;
    }
}
#endif
