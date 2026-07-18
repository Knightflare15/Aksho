using System.Text.Json;
using Vosk;

const int TargetSampleRate = 16000;
const int BytesPerSample = 2;

string repoRoot = FindRepoRoot();
string modelPath = Path.Combine(repoRoot, "Assets", "StreamingAssets", "VoskModel");
string wavRoot = Path.Combine(repoRoot, "Assets", "Audio", "Pronunciations", "Spells");
string? writeTrimmedDir = ReadOption("--write-trimmed=");
string[] requestedWords = args.Any(arg => string.Equals(arg, "--all", StringComparison.OrdinalIgnoreCase))
    ? Directory.GetFiles(wavRoot, "*.wav")
        .Select(Path.GetFileNameWithoutExtension)
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Select(name => name!)
        .Order()
        .ToArray()
    : args.Where(arg => !arg.StartsWith("--", StringComparison.Ordinal)).ToArray() is { Length: > 0 } positionalArgs
    ? positionalArgs.SelectMany(arg => arg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToArray()
    : args.Length == 0
    ? new[] { "CAT", "BAT", "BAD", "CAN", "DOT", "FIN", "PUP", "SIT", "TAP", "WET" }
    : Array.Empty<string>();

Vosk.Vosk.SetLogLevel(-1);
using var model = new Model(modelPath);

foreach (string rawWord in requestedWords)
{
    string word = Path.GetFileNameWithoutExtension(rawWord).ToUpperInvariant();
    string wavPath = Path.IsPathRooted(rawWord)
        ? rawWord
        : Path.Combine(wavRoot, $"{word}.wav");
    if (!File.Exists(wavPath))
    {
        Console.WriteLine($"{word}: missing WAV");
        continue;
    }

    byte[] pcm = ReadWavAsPcm16Mono16k(wavPath);
    double duration = pcm.Length / (double)(TargetSampleRate * BytesPerSample);
    using var recognizer = new VoskRecognizer(model, TargetSampleRate, $"[\"{word.ToLowerInvariant()}\", \"[unk]\"]");
    recognizer.SetWords(true);
    recognizer.AcceptWaveform(pcm, pcm.Length);
    string json = recognizer.FinalResult();

    VoskWord[] voskWords = ExtractVoskWords(json);
    TrimWindow energy = FindEnergyWindow(pcm, TargetSampleRate);
    TrimWindow adaptive = FindAdaptiveRmsWindow(pcm, TargetSampleRate);
    TrimWindow upload = SelectUploadWindow(voskWords, adaptive, duration);
    Console.WriteLine($"{word}: full={duration:0.000}s energy={energy.Start:0.000}-{energy.End:0.000}s ({energy.Duration:0.000}s) adaptive={adaptive.Start:0.000}-{adaptive.End:0.000}s ({adaptive.Duration:0.000}s) voskText=\"{ExtractText(json)}\"");
    Console.WriteLine($"  upload window: {upload.Start:0.000}-{upload.End:0.000}s ({upload.Duration:0.000}s)");
    if (!string.IsNullOrWhiteSpace(writeTrimmedDir))
    {
        Directory.CreateDirectory(writeTrimmedDir);
        string trimmedPath = Path.Combine(writeTrimmedDir, $"{word}.wav");
        WritePcm16MonoWav(trimmedPath, CopyWindow(pcm, TargetSampleRate, upload), TargetSampleRate);
        Console.WriteLine($"  wrote: {trimmedPath}");
    }

    if (voskWords.Length == 0)
    {
        Console.WriteLine("  vosk word window: none");
        continue;
    }

    foreach (VoskWord voskWord in voskWords)
        Console.WriteLine($"  vosk word: {voskWord.Word} {voskWord.Start:0.000}-{voskWord.End:0.000}s conf={voskWord.Confidence:0.000}");
}

string? ReadOption(string prefix)
{
    string? value = args.FirstOrDefault(arg => arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    return value == null ? null : value[prefix.Length..].Trim('"');
}

static TrimWindow SelectUploadWindow(IReadOnlyList<VoskWord> voskWords, TrimWindow fallback, double fullDuration)
{
    VoskWord? recognizedWord = voskWords.FirstOrDefault(word => !string.Equals(word.Word, "[unk]", StringComparison.OrdinalIgnoreCase));
    if (recognizedWord.HasValue && recognizedWord.Value.End > recognizedWord.Value.Start)
    {
        return new TrimWindow(
            Math.Max(0, recognizedWord.Value.Start - 0.15),
            Math.Min(fullDuration, recognizedWord.Value.End + 0.30));
    }

    if (fallback.Duration > 0.1 && fallback.Duration < fullDuration)
        return fallback;

    return new TrimWindow(0, fullDuration);
}

static byte[] CopyWindow(byte[] pcm16, int sampleRate, TrimWindow window)
{
    int sampleCount = pcm16.Length / 2;
    int startSample = Math.Clamp((int)Math.Floor(window.Start * sampleRate), 0, Math.Max(0, sampleCount - 1));
    int endSample = Math.Clamp((int)Math.Ceiling(window.End * sampleRate), startSample + 1, sampleCount);
    var output = new byte[(endSample - startSample) * 2];
    Buffer.BlockCopy(pcm16, startSample * 2, output, 0, output.Length);
    return output;
}

static VoskWord[] ExtractVoskWords(string json)
{
    try
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("result", out JsonElement result) ||
            result.ValueKind != JsonValueKind.Array)
            return Array.Empty<VoskWord>();

        var words = new List<VoskWord>();
        foreach (JsonElement item in result.EnumerateArray())
        {
            words.Add(new VoskWord(
                item.TryGetProperty("word", out JsonElement word) ? word.GetString() ?? "" : "",
                item.TryGetProperty("start", out JsonElement start) ? start.GetDouble() : 0,
                item.TryGetProperty("end", out JsonElement end) ? end.GetDouble() : 0,
                item.TryGetProperty("conf", out JsonElement conf) ? conf.GetDouble() : 0));
        }

        return words.ToArray();
    }
    catch
    {
        return Array.Empty<VoskWord>();
    }
}

static string ExtractText(string json)
{
    try
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("text", out JsonElement text) ? text.GetString() ?? "" : "";
    }
    catch
    {
        return "";
    }
}

static TrimWindow FindEnergyWindow(byte[] pcm16, int sampleRate)
{
    int sampleCount = pcm16.Length / 2;
    int windowSamples = Math.Max(1, sampleRate / 100);
    const int amplitudeThreshold = 450;
    int startSample = 0;
    int endSample = sampleCount - 1;

    for (int i = 0; i < sampleCount; i += windowSamples)
    {
        if (WindowPeak(pcm16, i, Math.Min(sampleCount, i + windowSamples)) >= amplitudeThreshold)
        {
            startSample = i;
            break;
        }
    }

    for (int i = sampleCount - windowSamples; i >= 0; i -= windowSamples)
    {
        int start = Math.Max(0, i);
        int end = Math.Min(sampleCount, i + windowSamples);
        if (WindowPeak(pcm16, start, end) >= amplitudeThreshold)
        {
            endSample = end - 1;
            break;
        }
    }

    int prePad = (int)Math.Round(sampleRate * 0.15);
    int postPad = (int)Math.Round(sampleRate * 0.30);
    startSample = Math.Max(0, startSample - prePad);
    endSample = Math.Min(sampleCount - 1, endSample + postPad);
    return new TrimWindow(
        startSample / (double)sampleRate,
        (endSample + 1) / (double)sampleRate);
}

static TrimWindow FindAdaptiveRmsWindow(byte[] pcm16, int sampleRate)
{
    int sampleCount = pcm16.Length / 2;
    int windowSamples = Math.Max(1, sampleRate / 50);
    int windowCount = Math.Max(1, (int)Math.Ceiling(sampleCount / (double)windowSamples));
    var rmsDb = new double[windowCount];

    for (int window = 0; window < windowCount; window++)
    {
        int start = window * windowSamples;
        int end = Math.Min(sampleCount, start + windowSamples);
        double sumSquares = 0;
        int count = Math.Max(1, end - start);
        for (int sampleIndex = start; sampleIndex < end; sampleIndex++)
        {
            double sample = ReadSample(pcm16, sampleIndex) / 32768.0;
            sumSquares += sample * sample;
        }

        double rms = Math.Sqrt(sumSquares / count);
        rmsDb[window] = 20.0 * Math.Log10(Math.Max(rms, 1e-6));
    }

    double[] sorted = rmsDb.Order().ToArray();
    double floor = sorted[Math.Min(sorted.Length - 1, Math.Max(0, sorted.Length / 5))];
    double threshold = Math.Max(floor + 10.0, -42.0);
    int startWindow = -1;
    int endWindow = -1;

    for (int i = 0; i < rmsDb.Length; i++)
    {
        if (rmsDb[i] >= threshold)
        {
            startWindow = i;
            break;
        }
    }

    for (int i = rmsDb.Length - 1; i >= 0; i--)
    {
        if (rmsDb[i] >= threshold)
        {
            endWindow = i;
            break;
        }
    }

    if (startWindow < 0 || endWindow < startWindow)
        return new TrimWindow(0, sampleCount / (double)sampleRate);

    int startSample = startWindow * windowSamples;
    int endSample = Math.Min(sampleCount - 1, ((endWindow + 1) * windowSamples) - 1);
    int prePad = (int)Math.Round(sampleRate * 0.15);
    int postPad = (int)Math.Round(sampleRate * 0.30);
    startSample = Math.Max(0, startSample - prePad);
    endSample = Math.Min(sampleCount - 1, endSample + postPad);
    return new TrimWindow(startSample / (double)sampleRate, (endSample + 1) / (double)sampleRate);
}

static int WindowPeak(byte[] pcm16, int startSample, int endSample)
{
    int peak = 0;
    for (int i = startSample; i < endSample; i++)
    {
        int byteIndex = i * 2;
        if (byteIndex + 1 >= pcm16.Length)
            break;
        short sample = (short)(pcm16[byteIndex] | (pcm16[byteIndex + 1] << 8));
        peak = Math.Max(peak, Math.Abs(sample));
    }

    return peak;
}

static byte[] ReadWavAsPcm16Mono16k(string path)
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
    return sampleRate == TargetSampleRate ? mono : ResampleLinear(mono, sampleRate, TargetSampleRate);
}

static byte[] DownmixToMono(byte[] interleavedPcm16, int channels)
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

static byte[] ResampleLinear(byte[] pcm16, int sourceRate, int targetRate)
{
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

static void WritePcm16MonoWav(string path, byte[] pcm16, int sampleRate)
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

static short ReadSample(byte[] pcm16, int sampleIndex)
{
    int offset = sampleIndex * 2;
    return (short)(pcm16[offset] | (pcm16[offset + 1] << 8));
}

static string FindRepoRoot()
{
    string? dir = AppContext.BaseDirectory;
    while (!string.IsNullOrEmpty(dir))
    {
        if (Directory.Exists(Path.Combine(dir, "Assets")) && Directory.Exists(Path.Combine(dir, ".git")))
            return dir;
        dir = Directory.GetParent(dir)?.FullName;
    }

    return Directory.GetCurrentDirectory();
}

readonly record struct VoskWord(string Word, double Start, double End, double Confidence);
readonly record struct TrimWindow(double Start, double End)
{
    public double Duration => Math.Max(0, End - Start);
}
