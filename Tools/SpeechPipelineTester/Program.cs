using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Vosk;
using VoskVoiceTester;

namespace SpeechPipelineTester;

internal static class Program
{
    private const int SampleRate = 16000;
    private const int BytesPerSample = 2;
    private const double DefaultMaxSeconds = 8.0;
    private const double MinimumRecognitionSeconds = 0.4;
    private const string DefaultPhonemeBackend = "zipa";

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        Options options;
        try
        {
            options = Options.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            PrintUsage();
            return 2;
        }

        if (options.ShowHelp)
        {
            PrintUsage();
            return 0;
        }

        string repoRoot = FindRepoRoot();
        string modelPath = options.ModelPath ?? Path.Combine(repoRoot, "Assets", "StreamingAssets", "VoskModel");
        string pythonPath = options.PythonPath ?? Path.Combine(repoRoot, "Tools", "Wav2Vec2PhoneticTester", ".venv", "Scripts", "python.exe");
        string allosaurusScript = options.AllosaurusScriptPath ?? Path.Combine(repoRoot, "Tools", "Wav2Vec2PhoneticTester", "allosaurus_tester.py");
        string hfCtcScript = options.HfCtcScriptPath ?? Path.Combine(repoRoot, "Tools", "Wav2Vec2PhoneticTester", "hf_ctc_tester.py");
        string zipaScript = options.ZipaScriptPath ?? Path.Combine(repoRoot, "Tools", "Wav2Vec2PhoneticTester", "zipa_onnx_tester.py");
        string attemptRoot = options.AttemptRoot ?? Path.Combine(repoRoot, "Tools", "SpeechPipelineTester", "attempts");

        if (!Directory.Exists(modelPath))
            return Fail($"Vosk model folder not found: {modelPath}");
        if (!File.Exists(pythonPath))
            return Fail($"Python executable not found: {pythonPath}");
        if (options.PhonemeBackend == "allosaurus" && !File.Exists(allosaurusScript))
            return Fail($"Allosaurus tester script not found: {allosaurusScript}");
        if (options.PhonemeBackend == "zipa" && !File.Exists(zipaScript))
            return Fail($"ZIPA tester script not found: {zipaScript}");
        if (options.PhonemeBackend != "allosaurus" && options.PhonemeBackend != "zipa" && !File.Exists(hfCtcScript))
            return Fail($"HF CTC tester script not found: {hfCtcScript}");

        Vosk.Vosk.SetLogLevel(-1);
        using var model = new Model(modelPath);
        var entries = GameVoiceVocabulary.Build();
        var spokenLookup = BuildSpokenLookup(entries);

        Console.WriteLine("Speech pipeline tester");
        Console.WriteLine("----------------------");
        Console.WriteLine($"Vosk model: {modelPath}");
        Console.WriteLine($"Pronunciation backend: {options.PhonemeBackend}");
        if (options.PhonemeBackend == "allosaurus")
            Console.WriteLine($"Allosaurus emit: {options.Emit:0.###}");
        Console.WriteLine(options.TrimGameStyle
            ? "Pronunciation audio: game-style trimmed WAV"
            : "Pronunciation audio: full captured WAV (no trim)");
        Console.WriteLine();

        if (!string.IsNullOrWhiteSpace(options.WavPath))
        {
            string word = RequireWord(options.Word);
            return await RunFileAttemptAsync(word, options.WavPath!, options, model, entries, spokenLookup, pythonPath, allosaurusScript, hfCtcScript, zipaScript, attemptRoot).ConfigureAwait(false);
        }

        while (true)
        {
            string word = options.Word ?? Prompt("Target word (blank to quit): ");
            if (string.IsNullOrWhiteSpace(word))
                break;

            word = GameVoiceVocabulary.NormalizeRecognized(word);
            if (word.Length == 0)
                continue;

            await RunMicrophoneAttemptAsync(word, options, model, entries, spokenLookup, pythonPath, allosaurusScript, hfCtcScript, zipaScript, attemptRoot).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(options.Word))
                break;
        }

        return 0;
    }

    private static async Task<int> RunFileAttemptAsync(
        string word,
        string wavPath,
        Options options,
        Model model,
        IReadOnlyList<VoiceEntry> entries,
        IReadOnlyDictionary<string, VoiceEntry> spokenLookup,
        string pythonPath,
        string allosaurusScript,
        string hfCtcScript,
        string zipaScript,
        string attemptRoot)
    {
        if (!File.Exists(wavPath))
            return Fail($"WAV not found: {wavPath}");

        byte[] pcm = ReadWavAsPcm16Mono16k(wavPath);
        string sessionDir = CreateSessionDir(attemptRoot);
        string rawPath = Path.Combine(sessionDir, $"{Timestamp()}_{word}_full.wav");
        WritePcm16MonoWav(rawPath, pcm, SampleRate);
        await RunPipelineAsync(word, rawPath, pcm, options, model, entries, spokenLookup, pythonPath, allosaurusScript, hfCtcScript, zipaScript, sessionDir).ConfigureAwait(false);
        return 0;
    }

    private static async Task RunMicrophoneAttemptAsync(
        string word,
        Options options,
        Model model,
        IReadOnlyList<VoiceEntry> entries,
        IReadOnlyDictionary<string, VoiceEntry> spokenLookup,
        string pythonPath,
        string allosaurusScript,
        string hfCtcScript,
        string zipaScript,
        string attemptRoot)
    {
        string sessionDir = CreateSessionDir(attemptRoot);
        Console.WriteLine($"Target: {word}");
        Console.WriteLine("Listening through Vosk. Speak once, then pause.");

        var captured = new List<byte>(SampleRate * BytesPerSample * 4);
        var queue = new ConcurrentQueue<byte[]>();
        using var recognizer = CreateRecognizer(model, entries);
        using var recorder = new WaveInRecorder();
        recorder.DataAvailable += bytes => queue.Enqueue(bytes);
        recorder.Start();

        var stopwatch = Stopwatch.StartNew();
        string finalText = "";
        string lastPartial = "";
        int processedBytes = 0;

        try
        {
            while (stopwatch.Elapsed.TotalSeconds < options.MaxSeconds)
            {
                if (!queue.TryDequeue(out byte[]? chunk))
                {
                    await Task.Delay(10).ConfigureAwait(false);
                    continue;
                }

                captured.AddRange(chunk);
                processedBytes += chunk.Length;

                bool completed = recognizer.AcceptWaveform(chunk, chunk.Length);
                string json = completed ? recognizer.Result() : recognizer.PartialResult();
                string text = ExtractText(json, completed ? "text" : "partial");

                if (!completed)
                {
                    if (!string.IsNullOrWhiteSpace(text) && !string.Equals(text, lastPartial, StringComparison.OrdinalIgnoreCase))
                    {
                        lastPartial = text;
                        Console.WriteLine($"Partial: {text}");
                    }

                    continue;
                }

                text = CleanRecognizerText(text);
                if (text.Length == 0 || processedBytes < SampleRate * BytesPerSample * MinimumRecognitionSeconds)
                    continue;

                finalText = text;
                break;
            }
        }
        finally
        {
            recorder.Dispose();
        }

        if (finalText.Length == 0)
            finalText = CleanRecognizerText(ExtractText(recognizer.FinalResult(), "text"));

        string rawPath = Path.Combine(sessionDir, $"{Timestamp()}_{word}_full.wav");
        WritePcm16MonoWav(rawPath, captured.ToArray(), SampleRate);
        await RunPipelineAsync(word, rawPath, captured.ToArray(), options, model, entries, spokenLookup, pythonPath, allosaurusScript, hfCtcScript, zipaScript, sessionDir, finalText).ConfigureAwait(false);
    }

    private static async Task RunPipelineAsync(
        string word,
        string rawPath,
        byte[] pcm,
        Options options,
        Model model,
        IReadOnlyList<VoiceEntry> entries,
        IReadOnlyDictionary<string, VoiceEntry> spokenLookup,
        string pythonPath,
        string allosaurusScript,
        string hfCtcScript,
        string zipaScript,
        string sessionDir,
        string? recognizedText = null)
    {
        if (recognizedText == null)
            recognizedText = RecognizeWholeFile(pcm, model, entries);

        VoiceEntry? matchedEntry = null;
        string normalizedText = GameVoiceVocabulary.NormalizeRecognized(recognizedText);
        if (normalizedText.Length > 0)
            spokenLookup.TryGetValue(normalizedText, out matchedEntry);

        bool acceptedTarget = matchedEntry != null &&
                              string.Equals(matchedEntry.Canonical, word, StringComparison.OrdinalIgnoreCase);

        string pronunciationPath = rawPath;
        if (options.TrimGameStyle)
        {
            byte[] trimmed = TrimGameStyle(pcm, SampleRate);
            pronunciationPath = Path.Combine(sessionDir, $"{Timestamp()}_{word}_game_trimmed.wav");
            WritePcm16MonoWav(pronunciationPath, trimmed, SampleRate);
        }

        Console.WriteLine();
        Console.WriteLine("Vosk result");
        Console.WriteLine("-----------");
        Console.WriteLine(string.IsNullOrWhiteSpace(recognizedText) ? "heard: --" : $"heard: {recognizedText.ToUpperInvariant()}");
        Console.WriteLine(matchedEntry == null
            ? "canonical: no game vocabulary match"
            : $"canonical: {matchedEntry.Canonical} [{matchedEntry.Kind}]");
        Console.WriteLine($"target accepted: {(acceptedTarget ? "yes" : "no")}");
        Console.WriteLine($"captured WAV: {rawPath}");
        Console.WriteLine($"pronunciation WAV: {pronunciationPath}");
        Console.WriteLine();
        Console.WriteLine($"{options.PhonemeBackend.ToUpperInvariant()} pronunciation result");
        Console.WriteLine("-------------------------------");

        int exitCode = await RunPronunciationAsync(pythonPath, allosaurusScript, hfCtcScript, zipaScript, word, pronunciationPath, options).ConfigureAwait(false);
        if (exitCode != 0)
            Console.WriteLine($"Pronunciation backend exited with code {exitCode}.");

        Console.WriteLine();
    }

    private static VoskRecognizer CreateRecognizer(Model model, IEnumerable<VoiceEntry> entries)
    {
        IEnumerable<VoiceEntry> spellEntries = entries.Where(entry => entry.Kind == "Spell word");
        string grammar = GameVoiceVocabulary.BuildGrammarJson(spellEntries);
        return new VoskRecognizer(model, SampleRate, grammar);
    }

    private static string RecognizeWholeFile(byte[] pcm, Model model, IEnumerable<VoiceEntry> entries)
    {
        using var recognizer = CreateRecognizer(model, entries);
        recognizer.AcceptWaveform(pcm, pcm.Length);
        return CleanRecognizerText(ExtractText(recognizer.FinalResult(), "text"));
    }

    private static async Task<int> RunPronunciationAsync(string pythonPath, string allosaurusScript, string hfCtcScript, string zipaScript, string word, string wavPath, Options options)
    {
        string scriptPath = options.PhonemeBackend switch
        {
            "allosaurus" => allosaurusScript,
            "zipa" => zipaScript,
            _ => hfCtcScript,
        };
        var startInfo = new ProcessStartInfo
        {
            FileName = pythonPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? Environment.CurrentDirectory,
        };
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.ArgumentList.Add(scriptPath);
        if (options.PhonemeBackend == "allosaurus")
        {
            startInfo.ArgumentList.Add("--word");
            startInfo.ArgumentList.Add(word);
            startInfo.ArgumentList.Add("--wav");
            startInfo.ArgumentList.Add(wavPath);
            startInfo.ArgumentList.Add("--emit");
            startInfo.ArgumentList.Add(options.Emit.ToString(System.Globalization.CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("--top-k");
            startInfo.ArgumentList.Add("20");
        }
        else if (options.PhonemeBackend == "zipa")
        {
            startInfo.ArgumentList.Add("--word");
            startInfo.ArgumentList.Add(word);
            startInfo.ArgumentList.Add("--wav");
            startInfo.ArgumentList.Add(wavPath);
            if (!string.IsNullOrWhiteSpace(options.HfModel))
            {
                startInfo.ArgumentList.Add("--repo");
                startInfo.ArgumentList.Add(options.HfModel);
            }
        }
        else
        {
            startInfo.ArgumentList.Add("--backend");
            startInfo.ArgumentList.Add(options.PhonemeBackend);
            startInfo.ArgumentList.Add("--word");
            startInfo.ArgumentList.Add(word);
            startInfo.ArgumentList.Add("--wav");
            startInfo.ArgumentList.Add(wavPath);
            if (!string.IsNullOrWhiteSpace(options.HfModel))
            {
                startInfo.ArgumentList.Add("--model");
                startInfo.ArgumentList.Add(options.HfModel);
            }
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start Allosaurus process.");
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        string stdout = await stdoutTask.ConfigureAwait(false);
        string stderr = await stderrTask.ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(stdout))
            Console.Write(stdout);
        string filteredStderr = FilterPronunciationStderr(stderr, options.PhonemeBackend);
        if (!string.IsNullOrWhiteSpace(filteredStderr))
            Console.Error.Write(filteredStderr);

        return process.ExitCode;
    }

    private static string FilterPronunciationStderr(string stderr, string backend)
    {
        if (string.IsNullOrWhiteSpace(stderr) || backend == "allosaurus")
            return stderr;

        var kept = new List<string>();
        foreach (string rawLine in stderr.Replace("\r", "\n").Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
                continue;
            if (line.Contains("Warning: You are sending unauthenticated requests to the HF Hub", StringComparison.OrdinalIgnoreCase))
                continue;
            if (line.Contains("Loading weights:", StringComparison.OrdinalIgnoreCase))
                continue;
            if (line.Contains("it/s]", StringComparison.OrdinalIgnoreCase))
                continue;
            kept.Add(rawLine);
        }

        return kept.Count == 0 ? "" : string.Join(Environment.NewLine, kept) + Environment.NewLine;
    }

    private static IReadOnlyDictionary<string, VoiceEntry> BuildSpokenLookup(IEnumerable<VoiceEntry> entries)
    {
        return entries
            .GroupBy(entry => GameVoiceVocabulary.NormalizeRecognized(entry.Spoken), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    private static byte[] TrimGameStyle(byte[] pcm16Audio, int sampleRate)
    {
        if (pcm16Audio.Length < 2 || sampleRate <= 0)
            return pcm16Audio;

        int sampleCount = pcm16Audio.Length / 2;
        const int amplitudeThreshold = 450;
        int windowSamples = Math.Max(1, sampleRate / 50);
        int startSample = 0;
        int endSample = sampleCount - 1;

        for (int i = 0; i < sampleCount; i += windowSamples)
        {
            if (WindowPeak(pcm16Audio, i, Math.Min(sampleCount, i + windowSamples)) >= amplitudeThreshold)
            {
                startSample = i;
                break;
            }
        }

        for (int i = sampleCount - windowSamples; i >= 0; i -= windowSamples)
        {
            int start = Math.Max(0, i);
            int end = Math.Min(sampleCount, i + windowSamples);
            if (WindowPeak(pcm16Audio, start, end) >= amplitudeThreshold)
            {
                endSample = end - 1;
                break;
            }
        }

        int paddingSamples = (int)MathF.Round(sampleRate * 0.15f);
        startSample = Math.Max(0, startSample - paddingSamples);
        endSample = Math.Min(sampleCount - 1, endSample + paddingSamples);
        if (endSample <= startSample)
            return pcm16Audio;

        int trimmedBytes = (endSample - startSample + 1) * 2;
        var trimmed = new byte[trimmedBytes];
        Buffer.BlockCopy(pcm16Audio, startSample * 2, trimmed, 0, trimmedBytes);
        return trimmed;
    }

    private static int WindowPeak(byte[] pcm16Audio, int startSample, int endSample)
    {
        int peak = 0;
        for (int i = startSample; i < endSample; i++)
        {
            int byteIndex = i * 2;
            if (byteIndex + 1 >= pcm16Audio.Length)
                break;

            short sample = (short)(pcm16Audio[byteIndex] | (pcm16Audio[byteIndex + 1] << 8));
            peak = Math.Max(peak, Math.Abs(sample));
        }

        return peak;
    }

    private static byte[] ReadWavAsPcm16Mono16k(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (new string(reader.ReadChars(4)) != "RIFF")
            throw new InvalidDataException("Expected RIFF WAV.");
        reader.ReadInt32();
        if (new string(reader.ReadChars(4)) != "WAVE")
            throw new InvalidDataException("Expected WAVE WAV.");

        short audioFormat = 0;
        short channels = 0;
        int sampleRate = 0;
        short bitsPerSample = 0;
        byte[]? data = null;

        while (stream.Position + 8 <= stream.Length)
        {
            string chunkId = new(reader.ReadChars(4));
            int chunkSize = reader.ReadInt32();
            long nextChunk = stream.Position + chunkSize + (chunkSize % 2);

            if (chunkId == "fmt ")
            {
                audioFormat = reader.ReadInt16();
                channels = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt16();
                bitsPerSample = reader.ReadInt16();
            }
            else if (chunkId == "data")
            {
                data = reader.ReadBytes(chunkSize);
            }

            stream.Position = Math.Min(nextChunk, stream.Length);
        }

        if (data == null)
            throw new InvalidDataException("WAV has no data chunk.");
        if (audioFormat != 1 || bitsPerSample != 16 || channels <= 0)
            throw new InvalidDataException("Only PCM 16-bit WAV input is supported.");

        byte[] mono = channels == 1 ? data : DownmixToMono(data, channels);
        return sampleRate == SampleRate ? mono : ResampleLinear(mono, sampleRate, SampleRate);
    }

    private static byte[] DownmixToMono(byte[] interleavedPcm16, int channels)
    {
        int frameCount = interleavedPcm16.Length / (channels * BytesPerSample);
        var mono = new byte[frameCount * BytesPerSample];
        for (int frame = 0; frame < frameCount; frame++)
        {
            int sum = 0;
            for (int channel = 0; channel < channels; channel++)
            {
                int offset = (frame * channels + channel) * BytesPerSample;
                sum += (short)(interleavedPcm16[offset] | (interleavedPcm16[offset + 1] << 8));
            }

            short sample = (short)Math.Clamp(sum / channels, short.MinValue, short.MaxValue);
            mono[frame * 2] = (byte)(sample & 0xff);
            mono[frame * 2 + 1] = (byte)((sample >> 8) & 0xff);
        }

        return mono;
    }

    private static byte[] ResampleLinear(byte[] pcm16, int sourceRate, int targetRate)
    {
        if (sourceRate <= 0)
            throw new InvalidDataException("Invalid WAV sample rate.");

        int sourceSamples = pcm16.Length / 2;
        int targetSamples = Math.Max(1, (int)Math.Round(sourceSamples * (double)targetRate / sourceRate));
        var output = new byte[targetSamples * 2];

        for (int i = 0; i < targetSamples; i++)
        {
            double sourcePosition = i * (double)sourceRate / targetRate;
            int left = Math.Min(sourceSamples - 1, (int)Math.Floor(sourcePosition));
            int right = Math.Min(sourceSamples - 1, left + 1);
            double fraction = sourcePosition - left;
            short leftSample = ReadSample(pcm16, left);
            short rightSample = ReadSample(pcm16, right);
            short sample = (short)Math.Round(leftSample + (rightSample - leftSample) * fraction);
            output[i * 2] = (byte)(sample & 0xff);
            output[i * 2 + 1] = (byte)((sample >> 8) & 0xff);
        }

        return output;
    }

    private static short ReadSample(byte[] pcm16, int sampleIndex)
    {
        int offset = sampleIndex * 2;
        return (short)(pcm16[offset] | (pcm16[offset + 1] << 8));
    }

    private static void WritePcm16MonoWav(string path, byte[] pcm16, int sampleRate)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        int dataLength = pcm16.Length;
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataLength);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * BytesPerSample);
        writer.Write((short)BytesPerSample);
        writer.Write((short)16);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(dataLength);
        writer.Write(pcm16);
    }

    private static string ExtractText(string json, string key)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(key, out JsonElement element)
                ? element.GetString() ?? ""
                : "";
        }
        catch
        {
            return "";
        }
    }

    private static string CleanRecognizerText(string text)
    {
        text = text.Trim();
        return string.Equals(text, "[unk]", StringComparison.OrdinalIgnoreCase) ? "" : text;
    }

    private static string RequireWord(string? word)
    {
        word = GameVoiceVocabulary.NormalizeRecognized(word ?? "");
        if (word.Length == 0)
            throw new ArgumentException("--word is required when using --wav.");
        return word;
    }

    private static string Prompt(string label)
    {
        Console.Write(label);
        return Console.ReadLine() ?? "";
    }

    private static string CreateSessionDir(string attemptRoot)
    {
        string path = Path.Combine(attemptRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Timestamp() => DateTime.Now.ToString("yyyyMMdd-HHmmss-ffffff");

    private static string FindRepoRoot()
    {
        string current = AppContext.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            if (Directory.Exists(Path.Combine(current, "Assets")) && Directory.Exists(Path.Combine(current, "Tools")))
                return current;
            current = Path.GetFullPath(Path.Combine(current, ".."));
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
        SpeechPipelineTester

        Interactive mic mode:
          SpeechPipelineTester.exe
          SpeechPipelineTester.exe --word CAT

        Existing WAV mode:
          SpeechPipelineTester.exe --word CAT --wav Assets\Audio\Pronunciations\Spells\CAT.wav

        Options:
          --emit VALUE             Allosaurus emit value. Default: 0.75
          --phoneme-backend VALUE  zipa, allosaurus, wavlm, wavlm-base-plus-fr-it, wav2vec2, or hubert. Default: zipa
          --hf-model VALUE         Override Hugging Face model id/repo for wavlm/hubert/zipa.
          --max-seconds VALUE      Mic listening timeout. Default: 8
          --trim-game-style        Send game-style trimmed audio to pronunciation pass.
          --model PATH             Vosk model folder.
          --python PATH            Python executable for Allosaurus.
          --allosaurus-script PATH allosaurus_tester.py path.
          --hf-ctc-script PATH     hf_ctc_tester.py path.
          --zipa-script PATH       zipa_onnx_tester.py path.
          --attempt-root PATH      Where captured attempt WAVs are saved.
          --help                   Show this help.
        """);
    }

    private sealed class Options
    {
        public string? Word { get; init; }
        public string? WavPath { get; init; }
        public string? ModelPath { get; init; }
        public string? PythonPath { get; init; }
        public string? AllosaurusScriptPath { get; init; }
        public string? HfCtcScriptPath { get; init; }
        public string? ZipaScriptPath { get; init; }
        public string? AttemptRoot { get; init; }
        public string PhonemeBackend { get; init; } = DefaultPhonemeBackend;
        public string? HfModel { get; init; }
        public double Emit { get; init; } = 0.75;
        public double MaxSeconds { get; init; } = DefaultMaxSeconds;
        public bool TrimGameStyle { get; init; }
        public bool ShowHelp { get; init; }

        public static Options Parse(string[] args)
        {
            var options = new MutableOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                switch (arg)
                {
                    case "--word":
                        options.Word = NeedValue(args, ref i, arg);
                        break;
                    case "--wav":
                        options.WavPath = NeedValue(args, ref i, arg);
                        break;
                    case "--model":
                        options.ModelPath = NeedValue(args, ref i, arg);
                        break;
                    case "--python":
                        options.PythonPath = NeedValue(args, ref i, arg);
                        break;
                    case "--allosaurus-script":
                        options.AllosaurusScriptPath = NeedValue(args, ref i, arg);
                        break;
                    case "--hf-ctc-script":
                        options.HfCtcScriptPath = NeedValue(args, ref i, arg);
                        break;
                    case "--zipa-script":
                        options.ZipaScriptPath = NeedValue(args, ref i, arg);
                        break;
                    case "--attempt-root":
                        options.AttemptRoot = NeedValue(args, ref i, arg);
                        break;
                    case "--phoneme-backend":
                    case "--backend":
                        options.PhonemeBackend = NeedValue(args, ref i, arg).Trim().ToLowerInvariant();
                        if (options.PhonemeBackend is not ("allosaurus" or "wavlm" or "wavlm-base-plus-fr-it" or "wav2vec2" or "hubert" or "zipa"))
                            throw new ArgumentException($"{arg} must be allosaurus, wavlm, wavlm-base-plus-fr-it, wav2vec2, hubert, or zipa.");
                        break;
                    case "--hf-model":
                        options.HfModel = NeedValue(args, ref i, arg);
                        break;
                    case "--emit":
                        options.Emit = ParseDouble(NeedValue(args, ref i, arg), arg);
                        break;
                    case "--max-seconds":
                        options.MaxSeconds = ParseDouble(NeedValue(args, ref i, arg), arg);
                        break;
                    case "--trim-game-style":
                        options.TrimGameStyle = true;
                        break;
                    case "--help":
                    case "-h":
                    case "/?":
                        options.ShowHelp = true;
                        break;
                    default:
                        throw new ArgumentException($"Unknown option: {arg}");
                }
            }

            return new Options
            {
                Word = options.Word,
                WavPath = options.WavPath,
                ModelPath = options.ModelPath,
                PythonPath = options.PythonPath,
                AllosaurusScriptPath = options.AllosaurusScriptPath,
                HfCtcScriptPath = options.HfCtcScriptPath,
                ZipaScriptPath = options.ZipaScriptPath,
                AttemptRoot = options.AttemptRoot,
                PhonemeBackend = options.PhonemeBackend,
                HfModel = options.HfModel,
                Emit = options.Emit,
                MaxSeconds = options.MaxSeconds,
                TrimGameStyle = options.TrimGameStyle,
                ShowHelp = options.ShowHelp,
            };
        }

        private static string NeedValue(string[] args, ref int index, string option)
        {
            if (index + 1 >= args.Length)
                throw new ArgumentException($"{option} needs a value.");
            index++;
            return args[index];
        }

        private static double ParseDouble(string value, string option)
        {
            if (!double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed))
                throw new ArgumentException($"{option} needs a number.");
            return parsed;
        }

        private sealed class MutableOptions
        {
            public string? Word;
            public string? WavPath;
            public string? ModelPath;
            public string? PythonPath;
            public string? AllosaurusScriptPath;
            public string? HfCtcScriptPath;
            public string? ZipaScriptPath;
            public string? AttemptRoot;
            public string PhonemeBackend = DefaultPhonemeBackend;
            public string? HfModel;
            public double Emit = 0.75;
            public double MaxSeconds = DefaultMaxSeconds;
            public bool TrimGameStyle;
            public bool ShowHelp;
        }
    }
}
