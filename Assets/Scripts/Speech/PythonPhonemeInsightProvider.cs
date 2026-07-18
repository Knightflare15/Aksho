using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

public partial class PythonPhonemeInsightProvider : IPronunciationInsightProvider
{
    const float MatchScoreFloor = 0.05f;
    const float NearMatchScoreFloor = 0.35f;

    readonly string providerName;
    readonly string reportLabel;
    readonly string failureLabel;
    readonly string scriptName;
    readonly string modelPath;
    readonly string extraArguments;
    readonly int processTimeoutMilliseconds;
    readonly string projectRoot;
    readonly string temporaryCachePath;

    protected PythonPhonemeInsightProvider(
        string providerName,
        string reportLabel,
        string failureLabel,
        string scriptName,
        string modelPath,
        string extraArguments,
        int processTimeoutMilliseconds)
    {
        this.providerName = providerName;
        this.reportLabel = reportLabel;
        this.failureLabel = failureLabel;
        this.scriptName = scriptName;
        this.modelPath = modelPath;
        this.extraArguments = extraArguments ?? "";
        this.processTimeoutMilliseconds = Mathf.Max(1000, processTimeoutMilliseconds);
        projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        temporaryCachePath = Application.temporaryCachePath;
    }

    public bool IsAvailable =>
        File.Exists(ResolveTesterScriptPath(scriptName)) &&
        (string.IsNullOrEmpty(modelPath) || Directory.Exists(modelPath));

    public string Name => providerName;

    public PronunciationInsightResult Analyze(PronunciationInsightRequest request)
    {
        string target = !string.IsNullOrEmpty(request.TargetWord)
            ? request.TargetWord
            : request.ConfirmedWord;
        IReadOnlyList<PhoneticSoundSegment> expected = PronunciationProfileBuilder.BuildSegments(target);
        IReadOnlyList<string> beats = PronunciationProfileBuilder.BuildSyllableBeats(target, expected);

        if (string.IsNullOrEmpty(target) || expected.Count == 0)
            return BuildUnavailableResult(request, target, expected, beats, "No target word was available for phoneme recognition.");

        if (request.Pcm16Audio == null || request.Pcm16Audio.Length < 2 || request.SampleRate <= 0)
            return BuildUnavailableResult(request, target, expected, beats, "No captured Vosk attempt audio was available for phoneme recognition.");

        string scriptPath = ResolveTesterScriptPath(scriptName);
        if (!File.Exists(scriptPath) || (!string.IsNullOrEmpty(modelPath) && !Directory.Exists(modelPath)))
            return BuildUnavailableResult(request, target, expected, beats, $"{reportLabel} model or tester script was not found.");

        string tempDirectory = Path.Combine(temporaryCachePath, "PhonemeInsight");
        Directory.CreateDirectory(tempDirectory);
        string stamp = DateTime.UtcNow.Ticks.ToString();
        string wavPath = Path.Combine(tempDirectory, $"attempt-{stamp}.wav");
        string jsonPath = Path.Combine(tempDirectory, $"attempt-{stamp}.json");
        bool preserveFailureArtifacts = false;

        try
        {
            WritePcm16Wav(wavPath, request.Pcm16Audio, request.SampleRate);
            string output;
            string error;
            int exitCode = RunTester(scriptPath, modelPath, wavPath, jsonPath, target, extraArguments, processTimeoutMilliseconds, out output, out error);
            if (exitCode != 0 || !File.Exists(jsonPath))
            {
                preserveFailureArtifacts = true;
                string detail = !string.IsNullOrWhiteSpace(error) ? error.Trim() : output.Trim();
                if (string.IsNullOrWhiteSpace(detail))
                    detail = $"phoneme process exited with code {exitCode}";
                UnityEngine.Debug.LogWarning($"[Pronunciation] {failureLabel}. Preserved WAV for debugging: '{wavPath}'. exitCode={exitCode} detail='{LastMeaningfulLine(detail)}'");
                return BuildUnavailableResult(request, target, expected, beats, $"{failureLabel}: {LastMeaningfulLine(detail)}");
            }

            string json = File.ReadAllText(jsonPath);
            PhonemeReport report = JsonUtility.FromJson<PhonemeReport>(json);
            return BuildResultFromReport(request, target, expected, beats, report);
        }
        catch (Exception ex)
        {
            preserveFailureArtifacts = true;
            UnityEngine.Debug.LogWarning($"[Pronunciation] {failureLabel}. Preserved WAV for debugging: '{wavPath}'. exception='{ex.Message}'");
            return BuildUnavailableResult(request, target, expected, beats, $"{failureLabel}: {ex.Message}");
        }
        finally
        {
            if (File.Exists(jsonPath))
                TryDelete(jsonPath);
            if (File.Exists(wavPath) && !preserveFailureArtifacts)
                TryDelete(wavPath);
        }
    }

    PronunciationInsightResult BuildResultFromReport(
        PronunciationInsightRequest request,
        string target,
        IReadOnlyList<PhoneticSoundSegment> expected,
        IReadOnlyList<string> beats,
        PhonemeReport report)
    {
        PhonemeTargetSegment[] targets = report != null && report.target != null
            ? report.target
            : Array.Empty<PhonemeTargetSegment>();
        PhonemeSpan[] spans = report != null && report.spans != null
            ? report.spans
            : report != null && report.phones != null
                ? report.phones
            : Array.Empty<PhonemeSpan>();

        int segmentCount = targets.Length > 0 ? targets.Length : expected.Count;
        var aligned = new List<PhoneticSoundSegment>(segmentCount);
        int matched = 0;
        float scoreTotal = 0f;
        int nextSpanSearchIndex = 0;
        for (int i = 0; i < segmentCount; i++)
        {
            PhoneticSoundSegment expectedSegment = i < expected.Count ? expected[i] : default;
            PhonemeTargetSegment targetSegment = i < targets.Length ? targets[i] : null;
            string reportedPhone = targetSegment != null ? targetSegment.DisplayPhone : "";
            if (!string.IsNullOrWhiteSpace(reportedPhone))
            {
                string phone = reportedPhone.Trim().ToUpperInvariant();
                expectedSegment = new PhoneticSoundSegment(
                    phone,
                    phone.ToLowerInvariant(),
                    i,
                    PhoneticSegmentStatus.Unknown,
                    0f);
            }

            bool isMatched = targetSegment != null && targetSegment.matched;
            float confidence = targetSegment != null ? Mathf.Clamp01(targetSegment.DisplayConfidence) : 0f;
            string heardSound = PickHeardPhone(targetSegment, spans);
            PhoneticSegmentStatus status;
            if (isMatched)
            {
                status = PhoneticSegmentStatus.Matched;
                matched++;
                int matchedSpanIndex = FindHeardSpanIndex(heardSound, targetSegment, spans, nextSpanSearchIndex);
                if (matchedSpanIndex >= 0)
                    nextSpanSearchIndex = matchedSpanIndex + 1;
            }
            else
            {
                ApproximatePhoneMatch approximate = FindClosestHeardPhone(expectedSegment, targetSegment, spans, nextSpanSearchIndex);
                if (approximate.Score > confidence)
                {
                    confidence = approximate.Score;
                    heardSound = approximate.Phone;
                    if (approximate.Index >= 0)
                        nextSpanSearchIndex = approximate.Index + 1;
                }

                status = confidence >= NearMatchScoreFloor
                    ? PhoneticSegmentStatus.NeedsPractice
                    : PhoneticSegmentStatus.Missing;
            }

            scoreTotal += status == PhoneticSegmentStatus.Matched
                ? 1f
                : status == PhoneticSegmentStatus.NeedsPractice
                    ? confidence
                    : 0f;
            aligned.Add(expectedSegment.WithHeardSound(heardSound, status, confidence));
        }

        float score = aligned.Count > 0 ? Mathf.Clamp01(scoreTotal / aligned.Count) : 0f;
        PhoneticSoundSegment focus = PickFocusSegment(aligned);
        PronunciationHintKey hint = PickHint(aligned, beats, focus, score);
        bool attemptedTarget = request.VoskConfirmedWord || matched > 0 || score > 0f;
        string observedPhones = BuildObservedPhones(spans);

        return new PronunciationInsightResult(
            providerName,
            target,
            request.ConfirmedWord,
            request.RawRecognizedText,
            request.VoskConfirmedWord,
            attemptedTarget,
            score,
            hint,
            focus,
            aligned,
            beats,
            string.IsNullOrEmpty(observedPhones)
                ? $"{reportLabel} ran on the Vosk attempt audio, but no stable phones were reported."
                : $"{reportLabel} phones: {observedPhones}");
    }

    PronunciationInsightResult BuildUnavailableResult(
        PronunciationInsightRequest request,
        string target,
        IReadOnlyList<PhoneticSoundSegment> expected,
        IReadOnlyList<string> beats,
        string message)
    {
        PhoneticSoundSegment focus = expected != null && expected.Count > 0 ? expected[0] : default;
        return new PronunciationInsightResult(
            providerName,
            target,
            request.ConfirmedWord,
            request.RawRecognizedText,
            request.VoskConfirmedWord,
            false,
            0f,
            PronunciationHintKey.TryAgain,
            focus,
            expected,
            beats,
            message);
    }

    static int RunTester(
        string scriptPath,
        string modelPath,
        string wavPath,
        string jsonPath,
        string target,
        string extraArguments,
        int timeoutMilliseconds,
        out string output,
        out string error)
    {
        string python = ResolvePythonExecutable();
        string modelArgument = string.IsNullOrEmpty(modelPath) ? "" : $" --model {Quote(modelPath)}";
        string trailingArguments = string.IsNullOrWhiteSpace(extraArguments) ? "" : " " + extraArguments.Trim();
        string arguments = $"{Quote(scriptPath)} --word {Quote(target)} --wav {Quote(wavPath)}{modelArgument} --json {Quote(jsonPath)}{trailingArguments}";
        var startInfo = new ProcessStartInfo
        {
            FileName = python,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
        UnityEngine.Debug.Log($"[Pronunciation] Running phoneme tester python='{python}' args='{arguments}'");

        using (Process process = Process.Start(startInfo))
        {
            if (process == null)
            {
                output = "";
                error = "Could not start Python phoneme process.";
                return -1;
            }

            output = process.StandardOutput.ReadToEnd();
            error = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(timeoutMilliseconds))
            {
                try { process.Kill(); } catch { }
                error = "Timed out waiting for Python phoneme process.";
                return -1;
            }

            return process.ExitCode;
        }
    }

    static void WritePcm16Wav(string path, byte[] pcm16Audio, int sampleRate)
    {
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var writer = new BinaryWriter(stream))
        {
            int byteRate = sampleRate * 2;
            int dataLength = pcm16Audio.Length;
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataLength);
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
            writer.Write(dataLength);
            writer.Write(pcm16Audio);
        }
    }

    string ResolveTesterScriptPath(string scriptName)
    {
        return Path.Combine(projectRoot, "Tools", "Wav2Vec2PhoneticTester", scriptName);
    }

    static string ResolvePythonExecutable()
    {
        string configured = Environment.GetEnvironmentVariable("PYTHON");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string projectVenv = Path.Combine(projectRoot, "Tools", "Wav2Vec2PhoneticTester", ".venv", "Scripts", "python.exe");
        if (File.Exists(projectVenv))
            return projectVenv;

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string bundled = Path.Combine(home, ".cache", "codex-runtimes", "codex-primary-runtime", "dependencies", "python", "python.exe");
        return File.Exists(bundled) ? bundled : "python";
    }

    static string LastMeaningfulLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            string line = lines[i].Trim();
            if (!string.IsNullOrWhiteSpace(line))
                return line;
        }

        return text.Trim();
    }

#pragma warning disable 0649
    [Serializable]
    sealed class PhonemeReport
    {
        public string word;
        public float duration_seconds;
        public PhonemeSpan[] spans;
        public PhonemeSpan[] phones;
        public PhonemeTargetSegment[] target;
    }

    [Serializable]
    sealed class PhonemeSpan
    {
        public string phone;
        public string normalized;
        public float start;
        public float end;
        public float duration;
        public float confidence;

        public string DisplayPhone => !string.IsNullOrWhiteSpace(phone) ? phone : normalized;
    }

    [Serializable]
    sealed class PhonemeTargetSegment
    {
        public string spelling;
        public string phone;
        public string[] candidates;
        public bool matched;
        public float first_seen;
        public float best_confidence;
        public float confidence;
        public string heard;

        public string DisplayPhone => !string.IsNullOrWhiteSpace(spelling) ? spelling : phone;
        public float DisplayConfidence => best_confidence > 0f ? best_confidence : confidence;
    }

    readonly struct ApproximatePhoneMatch
    {
        public readonly string Phone;
        public readonly float Score;
        public readonly int Index;

        public ApproximatePhoneMatch(string phone, float score, int index)
        {
            Phone = phone ?? "";
            Score = Mathf.Clamp01(score);
            Index = index;
        }
    }
#pragma warning restore 0649
}
