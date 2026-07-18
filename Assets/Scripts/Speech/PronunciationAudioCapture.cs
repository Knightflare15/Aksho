using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct TrimmedPronunciationAudio
{
    public readonly byte[] Pcm16Audio;
    public readonly int SampleRate;
    public readonly float SourceSeconds;
    public readonly float TrimmedSeconds;
    public readonly bool HasSpeech;

    public TrimmedPronunciationAudio(byte[] pcm16Audio, int sampleRate, float sourceSeconds, float trimmedSeconds, bool hasSpeech)
    {
        Pcm16Audio = pcm16Audio ?? Array.Empty<byte>();
        SampleRate = sampleRate;
        SourceSeconds = sourceSeconds;
        TrimmedSeconds = trimmedSeconds;
        HasSpeech = hasSpeech;
    }
}

// Keeps sentence pauses while removing only the lead-in and tail silence around an attempt.
public static class PronunciationAudioTrimmer
{
    const float FrameSeconds = 0.02f;
    const float NoiseFloorPercentile = 0.20f;
    const float MinimumThresholdDb = -42f;
    // Microphone input often carries a steady room/electrical floor. A larger
    // margin prevents that floor from being treated as speech across the whole
    // Vosk session, so Azure receives the actual utterance plus small padding.
    const float NoiseFloorMarginDb = 18f;
    const float StartPaddingSeconds = 0.25f;
    const float EndPaddingSeconds = 0.40f;
    const float MinimumVoicedSeconds = 0.08f;

    public static TrimmedPronunciationAudio Trim(byte[] pcm16Audio, int sampleRate)
    {
        if (pcm16Audio == null || pcm16Audio.Length < 2 || sampleRate <= 0)
            return new TrimmedPronunciationAudio(Array.Empty<byte>(), sampleRate, 0f, 0f, false);

        int sampleCount = pcm16Audio.Length / 2;
        float sourceSeconds = sampleCount / (float)sampleRate;
        int frameSamples = Mathf.Max(1, Mathf.RoundToInt(sampleRate * FrameSeconds));
        int frameCount = Mathf.CeilToInt(sampleCount / (float)frameSamples);
        var frameDb = new float[frameCount];
        for (int frame = 0; frame < frameCount; frame++)
        {
            int start = frame * frameSamples;
            int end = Mathf.Min(sampleCount, start + frameSamples);
            double sumSquares = 0d;
            for (int sample = start; sample < end; sample++)
            {
                int byteIndex = sample * 2;
                short value = (short)(pcm16Audio[byteIndex] | (pcm16Audio[byteIndex + 1] << 8));
                float normalized = value / 32768f;
                sumSquares += normalized * normalized;
            }

            double rms = Math.Sqrt(sumSquares / Mathf.Max(1, end - start));
            frameDb[frame] = 20f * Mathf.Log10(Mathf.Max((float)rms, 0.000001f));
        }

        float[] sorted = (float[])frameDb.Clone();
        Array.Sort(sorted);
        int noiseIndex = Mathf.Clamp(Mathf.FloorToInt((sorted.Length - 1) * NoiseFloorPercentile), 0, sorted.Length - 1);
        float thresholdDb = Mathf.Max(MinimumThresholdDb, sorted[noiseIndex] + NoiseFloorMarginDb);

        int firstVoiced = -1;
        int lastVoiced = -1;
        FindVoicedRange(frameDb, thresholdDb, ref firstVoiced, ref lastVoiced);

        // Some microphones report a high, steady floor. If the first pass
        // rejects everything, retry with a conservative relative threshold
        // before giving up; an accepted STT result must never lose its audio.
        if (firstVoiced < 0 || lastVoiced < firstVoiced)
        {
            firstVoiced = -1;
            lastVoiced = -1;
            float retryThresholdDb = Mathf.Max(-48f, sorted[noiseIndex] + 6f);
            FindVoicedRange(frameDb, retryThresholdDb, ref firstVoiced, ref lastVoiced);
        }

        if (firstVoiced < 0 || lastVoiced < firstVoiced ||
            (lastVoiced - firstVoiced + 1) * FrameSeconds < MinimumVoicedSeconds)
        {
            // STT has already accepted this attempt, so dropping the PCM here
            // would make the server pronunciation job impossible. Keep the
            // captured clip as a last-resort trim when the device's RMS data
            // cannot produce a reliable voiced range (common with WebGL,
            // Bluetooth and some Android microphone drivers).
            Debug.LogWarning($"[Pronunciation] Trimmer found no reliable voiced range; retaining captured audio source={sourceSeconds:0.00}s bytes={pcm16Audio.Length}.");
            return new TrimmedPronunciationAudio(pcm16Audio, sampleRate, sourceSeconds, sourceSeconds, true);
        }

        int startSample = Mathf.Max(0, firstVoiced * frameSamples - Mathf.RoundToInt(sampleRate * StartPaddingSeconds));
        int endSample = Mathf.Min(sampleCount, (lastVoiced + 1) * frameSamples + Mathf.RoundToInt(sampleRate * EndPaddingSeconds));
        int byteCount = Mathf.Max(0, endSample - startSample) * 2;
        if (byteCount <= 0)
            return new TrimmedPronunciationAudio(Array.Empty<byte>(), sampleRate, sourceSeconds, 0f, false);

        var trimmed = new byte[byteCount];
        Buffer.BlockCopy(pcm16Audio, startSample * 2, trimmed, 0, byteCount);
        return new TrimmedPronunciationAudio(trimmed, sampleRate, sourceSeconds, trimmed.Length / (sampleRate * 2f), true);
    }

    static void FindVoicedRange(float[] frameDb, float thresholdDb, ref int firstVoiced, ref int lastVoiced)
    {
        for (int frame = 0; frame < frameDb.Length; frame++)
        {
            if (frameDb[frame] < thresholdDb)
                continue;
            if (firstVoiced < 0)
                firstVoiced = frame;
            lastVoiced = frame;
        }
    }
}

// SpeechRecognizer owns recognition on Android; this recorder independently preserves the same attempt for Azure.
public sealed class SpeechAttemptAudioCapture
{
    const int TargetSampleRate = 16000;
    const int MaximumSeconds = 15;

    readonly List<float> capturedSamples = new List<float>();
    AudioClip microphoneClip;
    string microphoneDevice;
    int sampleRate;
    int channels;
    int lastFramePosition;
    bool recording;

    public bool IsRecording => recording;

    public bool Start()
    {
        StopDiscarding();
        string[] devices = Microphone.devices;
        if (devices == null || devices.Length == 0)
            return false;

        microphoneDevice = ResolveMicrophoneDevice(devices);
        int requestedRate = ResolveMicrophoneFrequency(microphoneDevice);
        microphoneClip = Microphone.Start(microphoneDevice, true, MaximumSeconds + 1, requestedRate);
        if (microphoneClip == null)
            return false;

        sampleRate = Mathf.Max(1, microphoneClip.frequency);
        channels = Mathf.Max(1, microphoneClip.channels);
        lastFramePosition = 0;
        recording = true;
        return true;
    }

    public void Tick()
    {
        if (!recording || microphoneClip == null || !Microphone.IsRecording(microphoneDevice))
            return;

        int position = Microphone.GetPosition(microphoneDevice);
        if (position < 0 || position == lastFramePosition)
            return;

        if (position > lastFramePosition)
        {
            AppendFrames(lastFramePosition, position - lastFramePosition);
        }
        else
        {
            AppendFrames(lastFramePosition, microphoneClip.samples - lastFramePosition);
            if (position > 0)
                AppendFrames(0, position);
        }

        lastFramePosition = position;
    }

    public TrimmedPronunciationAudio StopAndTrim()
    {
        Tick();
        StopMicrophone();
        if (capturedSamples.Count < channels)
            return new TrimmedPronunciationAudio(Array.Empty<byte>(), TargetSampleRate, 0f, 0f, false);

        byte[] pcm16 = ConvertToMonoPcm16(capturedSamples, channels, sampleRate, TargetSampleRate);
        capturedSamples.Clear();
        return PronunciationAudioTrimmer.Trim(pcm16, TargetSampleRate);
    }

    public void StopDiscarding()
    {
        StopMicrophone();
        capturedSamples.Clear();
    }

    void AppendFrames(int offsetFrames, int frameCount)
    {
        if (frameCount <= 0 || microphoneClip == null)
            return;

        var samples = new float[frameCount * channels];
        microphoneClip.GetData(samples, offsetFrames);
        int maxSamples = MaximumSeconds * sampleRate * channels;
        int overflow = capturedSamples.Count + samples.Length - maxSamples;
        if (overflow > 0)
            capturedSamples.RemoveRange(0, Mathf.Min(overflow, capturedSamples.Count));
        capturedSamples.AddRange(samples);
    }

    void StopMicrophone()
    {
        if (microphoneClip != null && Microphone.IsRecording(microphoneDevice))
            Microphone.End(microphoneDevice);

        microphoneClip = null;
        microphoneDevice = null;
        sampleRate = 0;
        channels = 0;
        lastFramePosition = 0;
        recording = false;
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
        Microphone.GetDeviceCaps(device, out int min, out int max);
        if (min == 0 && max == 0)
            return 44100;
        if (max > 0 && 44100 > max)
            return max;
        return min > 0 && 44100 < min ? min : 44100;
    }

    static byte[] ConvertToMonoPcm16(IReadOnlyList<float> input, int sourceChannels, int sourceRate, int targetRate)
    {
        int sourceFrames = input.Count / Mathf.Max(1, sourceChannels);
        if (sourceFrames == 0 || sourceRate <= 0 || targetRate <= 0)
            return Array.Empty<byte>();

        int targetFrames = Mathf.Max(1, Mathf.RoundToInt(sourceFrames * (targetRate / (float)sourceRate)));
        var output = new byte[targetFrames * 2];
        float ratio = sourceRate / (float)targetRate;
        for (int frame = 0; frame < targetFrames; frame++)
        {
            float sourcePosition = frame * ratio;
            int left = Mathf.Clamp(Mathf.FloorToInt(sourcePosition), 0, sourceFrames - 1);
            int right = Mathf.Min(left + 1, sourceFrames - 1);
            float t = Mathf.Clamp01(sourcePosition - left);
            float value = Mathf.Lerp(Downmix(input, left, sourceChannels), Downmix(input, right, sourceChannels), t);
            short sample = (short)Mathf.RoundToInt(Mathf.Clamp(value, -1f, 1f) * short.MaxValue);
            output[frame * 2] = (byte)(sample & 0xff);
            output[frame * 2 + 1] = (byte)((sample >> 8) & 0xff);
        }

        return output;
    }

    static float Downmix(IReadOnlyList<float> samples, int frame, int channelCount)
    {
        float sum = 0f;
        int offset = frame * channelCount;
        for (int channel = 0; channel < channelCount; channel++)
            sum += samples[offset + channel];
        return sum / channelCount;
    }
}
