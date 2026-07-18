using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

using DiagnosticsProcess = System.Diagnostics.Process;
using DiagnosticsProcessStartInfo = System.Diagnostics.ProcessStartInfo;

public sealed class PronunciationSpeaker : MonoBehaviour
{
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    static DiagnosticsProcess windowsTtsProcess;
    static string windowsTtsProcessDiagnostic;
    static readonly HashSet<string> warnedMissingWindowsLanguages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
    static AndroidJavaObject androidTts;
    static bool androidTtsReady;
    static bool androidTtsInitializing;
    static int androidCommandVersion;
    static int androidPendingCommandVersion = -1;
    static string queuedAndroidText;
    static string queuedAndroidLanguage;
    static int queuedAndroidCommandVersion = -1;
    static int androidUtteranceSequence;
#endif

    AudioSource audioSource;
    Coroutine delayedSpeech;
    Coroutine speechSequence;
    int speechSequenceVersion;

    public static PronunciationSpeaker EnsureExists()
    {
        PronunciationSpeaker speaker = FindAnyObjectByType<PronunciationSpeaker>();
        if (speaker != null)
            return speaker;

        var go = new GameObject("PronunciationSpeaker");
        // EditMode tests and editor tooling can legitimately request a speaker,
        // but Unity only permits DontDestroyOnLoad while the player is running.
        if (Application.isPlaying)
            DontDestroyOnLoad(go);
        speaker = go.AddComponent<PronunciationSpeaker>();
        return speaker;
    }

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        Debug.Log($"[PronunciationSpeaker] Ready. audioSource={audioSource != null} output='{(audioSource != null && audioSource.outputAudioMixerGroup != null ? audioSource.outputAudioMixerGroup.name : "default")}'");
    }

    public void Speak(string text, AudioClip clip = null, string languageCode = "")
    {
        StopSpeaking();
        SpeakImmediate(text, clip, languageCode);
    }

    void SpeakImmediate(string text, AudioClip clip, string languageCode)
    {
        string spokenText = string.IsNullOrWhiteSpace(text) ? "<empty>" : text.Trim();
        if (clip != null)
        {
            Debug.Log($"[PronunciationSpeaker] Playing clip for '{spokenText}': {DescribeClip(clip)}");
            audioSource.PlayOneShot(clip);
            return;
        }

        Debug.Log($"[PronunciationSpeaker] No AudioClip for '{spokenText}'. Trying system voice fallback.");
        if (TrySpeakWithSystemVoice(text, languageCode))
        {
            Debug.Log($"[PronunciationSpeaker] System voice started for '{spokenText}'.");
            return;
        }

        Debug.LogWarning($"[PronunciationSpeaker] No playable pronunciation source for '{spokenText}'. Assign an AudioClip or enable the device system voice.");
    }

    public void SpeakAfterClip(string text, AudioClip cueClip, string languageCode = "")
    {
        StopSpeaking();
        if (cueClip == null)
        {
            SpeakImmediate(text, null, languageCode);
            return;
        }

        SpeakImmediate("phonics cue", cueClip, "");
        delayedSpeech = StartCoroutine(SpeakAfterDelay(text, languageCode, cueClip.length + 0.2f));
    }

    IEnumerator SpeakAfterDelay(string text, string languageCode, float delaySeconds)
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, delaySeconds));
        delayedSpeech = null;
        SpeakImmediate(text, null, languageCode);
    }

    public float SpeakSequence(
        IReadOnlyList<BuddySpeechSegment> segments,
        AudioClip cueClip = null,
        Action completed = null)
    {
        StopSpeaking();
        var playable = new List<BuddySpeechSegment>();
        float estimatedSeconds = cueClip != null ? cueClip.length + 0.2f : 0f;
        if (segments != null)
        {
            foreach (BuddySpeechSegment segment in segments)
            {
                if (segment == null || string.IsNullOrWhiteSpace(segment.text))
                    continue;
                playable.Add(new BuddySpeechSegment
                {
                    language = BuddySpeechSequence.NormalizeLanguage(segment.language, "en"),
                    text = segment.text.Trim(),
                });
                estimatedSeconds += EstimateSpeechSeconds(segment.text);
            }
        }

        if (playable.Count == 0)
        {
            completed?.Invoke();
            return 0f;
        }

        int version = speechSequenceVersion;
        if (!Application.isPlaying)
        {
            SpeakImmediate(playable[0].text, cueClip, playable[0].language);
            completed?.Invoke();
            return estimatedSeconds;
        }

        speechSequence = StartCoroutine(SpeakSequenceRoutine(playable, cueClip, version, completed));
        return estimatedSeconds;
    }

    IEnumerator SpeakSequenceRoutine(
        IReadOnlyList<BuddySpeechSegment> segments,
        AudioClip cueClip,
        int version,
        Action completed)
    {
        if (cueClip != null)
        {
            SpeakImmediate("phonics cue", cueClip, "");
            yield return new WaitForSecondsRealtime(cueClip.length + 0.2f);
        }

        foreach (BuddySpeechSegment segment in segments)
        {
            if (version != speechSequenceVersion)
                yield break;

            bool started = TrySpeakWithSystemVoice(segment.text, segment.language);
            if (!started)
            {
                Debug.LogWarning($"[BuddyTTS] No local system voice could speak language='{segment.language}'.");
                continue;
            }

            yield return WaitForSystemVoiceCompletion(segment.text, version);
        }

        if (version != speechSequenceVersion)
            yield break;
        speechSequence = null;
        completed?.Invoke();
    }

    IEnumerator WaitForSystemVoiceCompletion(string text, int version)
    {
        float startedAt = Time.realtimeSinceStartup;
        float deadline = startedAt + Mathf.Max(5f, EstimateSpeechSeconds(text) + 6f);
        bool observedBusy = false;

        while (version == speechSequenceVersion && Time.realtimeSinceStartup < deadline)
        {
            bool busy = IsSystemVoiceBusy();
            observedBusy |= busy;
            if (observedBusy && !busy)
                yield break;
            if (!observedBusy && Time.realtimeSinceStartup - startedAt > 1f)
                yield break;
            yield return null;
        }

        if (version == speechSequenceVersion && Time.realtimeSinceStartup >= deadline)
            Debug.LogWarning("[BuddyTTS] Local speech completion timed out; continuing with the next segment.");
    }

    public void StopSpeaking()
    {
        speechSequenceVersion++;
        if (speechSequence != null)
        {
            StopCoroutine(speechSequence);
            speechSequence = null;
        }
        if (delayedSpeech != null)
        {
            StopCoroutine(delayedSpeech);
            delayedSpeech = null;
        }
        audioSource?.Stop();

#if UNITY_ANDROID && !UNITY_EDITOR
        androidCommandVersion++;
        androidPendingCommandVersion = -1;
        queuedAndroidText = "";
        queuedAndroidLanguage = "";
        queuedAndroidCommandVersion = -1;
        try
        {
            androidTts?.Call<int>("stop");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PronunciationSpeaker] Android system voice could not be stopped: {ex.Message}");
        }
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        StopWindowsTtsProcess();
#endif
    }

    static float EstimateSpeechSeconds(string text)
    {
        string[] words = (text ?? "").Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        return Mathf.Clamp(0.42f * words.Length + 0.6f, 1.2f, 12f);
    }

    static string DescribeClip(AudioClip clip)
    {
        if (clip == null)
            return "clip=null";

        return $"name='{clip.name}' length={clip.length:0.000}s samples={clip.samples} frequency={clip.frequency} channels={clip.channels} loadState={clip.loadState}";
    }

    static bool TrySpeakWithSystemVoice(string text, string languageCode)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

#if UNITY_ANDROID && !UNITY_EDITOR
        return TrySpeakWithAndroidSystemVoice(text, languageCode);
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        return TrySpeakWithWindowsSystemVoice(text, languageCode);
#else
        return false;
#endif
    }

    static bool IsSystemVoiceBusy()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (androidTtsInitializing || androidPendingCommandVersion == androidCommandVersion)
            return true;
        if (!androidTtsReady || androidTts == null)
            return false;
        try
        {
            return androidTts.Call<bool>("isSpeaking");
        }
        catch
        {
            return false;
        }
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (windowsTtsProcess == null)
            return false;
        try
        {
            if (!windowsTtsProcess.HasExited)
                return true;
            CompleteWindowsTtsProcess();
            return false;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PronunciationSpeaker] Windows system voice status failed: {ex.Message}");
            StopWindowsTtsProcess();
            return false;
        }
#else
        return false;
#endif
    }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    static bool TrySpeakWithWindowsSystemVoice(string text, string languageCode)
    {
        try
        {
            StopWindowsTtsProcess();
            string powerShellPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");
            if (!File.Exists(powerShellPath))
            {
                Debug.LogWarning($"[PronunciationSpeaker] Windows PowerShell was not found at '{powerShellPath}'.");
                return false;
            }

            string requested = ResolveSpeechLanguage(languageCode, text);
            if (TrySpeakWithWindowsEspeakVoice(text, requested))
                return true;

            string script = BuildWindowsTtsScript(text, requested);
            string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var startInfo = new DiagnosticsProcessStartInfo
            {
                FileName = powerShellPath,
                Arguments = $"-NoLogo -NoProfile -NonInteractive -Sta -WindowStyle Hidden -EncodedCommand {encodedCommand}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            windowsTtsProcess = DiagnosticsProcess.Start(startInfo);
            windowsTtsProcessDiagnostic = "";
            return windowsTtsProcess != null;
        }
        catch (Exception ex)
        {
            StopWindowsTtsProcess();
            Debug.LogWarning($"[PronunciationSpeaker] Windows system voice unavailable: {ex.Message}");
            return false;
        }
    }

    static bool TrySpeakWithWindowsEspeakVoice(string text, string requestedLanguage)
    {
        if (!TryResolveEspeakVoice(requestedLanguage, out string espeakVoice))
            return false;

        string espeakPath = FindWindowsExecutable("espeak-ng.exe");
        if (string.IsNullOrWhiteSpace(espeakPath))
            return false;

        try
        {
            StopWindowsTtsProcess();
            var startInfo = new DiagnosticsProcessStartInfo
            {
                FileName = espeakPath,
                Arguments = $"-v {QuoteWindowsArgument(espeakVoice)} {QuoteWindowsArgument(text ?? "")}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            windowsTtsProcess = DiagnosticsProcess.Start(startInfo);
            windowsTtsProcessDiagnostic = $"[BuddyTTS] Windows dev selected eSpeak NG voice '{espeakVoice}' for language='{requestedLanguage}'.";
            return windowsTtsProcess != null;
        }
        catch (Exception ex)
        {
            StopWindowsTtsProcess();
            Debug.LogWarning($"[PronunciationSpeaker] eSpeak NG voice unavailable: {ex.Message}");
            return false;
        }
    }

    static bool TryResolveEspeakVoice(string requestedLanguage, out string espeakVoice)
    {
        string requested = (requestedLanguage ?? "").Trim().ToLowerInvariant();
        if (requested == "hi" || requested == "hin" || requested == "hi-in")
        {
            espeakVoice = "hi";
            return true;
        }

        espeakVoice = "";
        return false;
    }

    static string FindWindowsExecutable(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "";

        var candidates = new List<string>();
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
            candidates.Add(Path.Combine(userProfile, "scoop", "shims", fileName));

        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
            candidates.Add(Path.Combine(programFiles, "eSpeak NG", fileName));

        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
            candidates.Add(Path.Combine(programFilesX86, "eSpeak NG", fileName));

        string path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string directory in path.Split(Path.PathSeparator))
        {
            if (!string.IsNullOrWhiteSpace(directory))
                candidates.Add(Path.Combine(directory.Trim(), fileName));
        }

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return "";
    }

    static string QuoteWindowsArgument(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";

        var builder = new StringBuilder("\"");
        int backslashes = 0;
        foreach (char c in value)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                builder.Append('\\', backslashes * 2 + 1);
                builder.Append('"');
                backslashes = 0;
                continue;
            }

            if (backslashes > 0)
            {
                builder.Append('\\', backslashes);
                backslashes = 0;
            }
            builder.Append(c);
        }

        if (backslashes > 0)
            builder.Append('\\', backslashes * 2);
        builder.Append('"');
        return builder.ToString();
    }

    static string BuildWindowsTtsScript(string text, string requestedLanguage)
    {
        string encodedText = Convert.ToBase64String(Encoding.UTF8.GetBytes(text ?? ""));
        string requested = requestedLanguage == "od" ? "or" : requestedLanguage;
        requested = (requested ?? "en").Replace("'", "''");
        return string.Join(Environment.NewLine, new[]
        {
            "$ErrorActionPreference = 'Stop'",
            "Add-Type -AssemblyName System.Speech",
            "$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer",
            "try {",
            $"  $requested = '{requested}'",
            $"  $encodedText = '{encodedText}'",
            "  $voices = @($synth.GetInstalledVoices() | Where-Object { $_.Enabled })",
            "  $selected = $voices | Where-Object { $_.VoiceInfo.Culture.TwoLetterISOLanguageName.ToLowerInvariant() -eq $requested } | Select-Object -First 1",
            "  $fallback = $false",
            "  if ($null -eq $selected) {",
            "    $selected = $voices | Where-Object { $_.VoiceInfo.Culture.TwoLetterISOLanguageName.ToLowerInvariant() -eq 'en' } | Select-Object -First 1",
            "    $fallback = $true",
            "  }",
            "  if ($null -eq $selected -and $voices.Count -gt 0) { $selected = $voices[0]; $fallback = $true }",
            "  if ($null -eq $selected) { throw 'Windows has no enabled local TTS voices.' }",
            "  $synth.SelectVoice($selected.VoiceInfo.Name)",
            "  if ($fallback) { [Console]::Out.WriteLine('__BUDDY_TTS_FALLBACK__|' + $requested + '|' + $selected.VoiceInfo.Name) }",
            "  else { [Console]::Out.WriteLine('__BUDDY_TTS_SELECTED__|' + $requested + '|' + $selected.VoiceInfo.Name) }",
            "  $spokenText = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($encodedText))",
            "  $synth.Speak($spokenText)",
            "} finally {",
            "  $synth.Dispose()",
            "}",
        });
    }

    static void CompleteWindowsTtsProcess()
    {
        DiagnosticsProcess process = windowsTtsProcess;
        windowsTtsProcess = null;
        if (process == null)
            return;

        try
        {
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            int exitCode = process.ExitCode;
            if (!string.IsNullOrWhiteSpace(windowsTtsProcessDiagnostic))
                Debug.Log(windowsTtsProcessDiagnostic);
            windowsTtsProcessDiagnostic = "";
            foreach (string rawLine in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = rawLine.Trim();
                if (line.StartsWith("__BUDDY_TTS_FALLBACK__|", StringComparison.Ordinal))
                {
                    string[] fields = line.Split('|');
                    string requested = fields.Length > 1 ? fields[1] : "unknown";
                    string voice = fields.Length > 2 ? fields[2] : "English";
                    if (warnedMissingWindowsLanguages.Add(requested))
                        Debug.LogWarning($"[BuddyTTS] Windows has no installed '{requested}' voice. Falling back to local voice '{voice}'.");
                }
                else if (line.StartsWith("__BUDDY_TTS_SELECTED__|", StringComparison.Ordinal))
                {
                    string[] fields = line.Split('|');
                    Debug.Log($"[BuddyTTS] Windows selected '{(fields.Length > 2 ? fields[2] : "local voice")}' for language='{(fields.Length > 1 ? fields[1] : "unknown")}'.");
                }
            }

            if (exitCode != 0)
                Debug.LogWarning($"[PronunciationSpeaker] Windows TTS worker exited with code {exitCode}: {error.Trim()}");
        }
        finally
        {
            process.Dispose();
        }
    }

    static void StopWindowsTtsProcess()
    {
        DiagnosticsProcess process = windowsTtsProcess;
        windowsTtsProcess = null;
        windowsTtsProcessDiagnostic = "";
        if (process == null)
            return;

        try
        {
            if (!process.HasExited)
            {
                process.Kill();
                process.WaitForExit(1000);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PronunciationSpeaker] Windows system voice could not be stopped: {ex.Message}");
        }
        finally
        {
            process.Dispose();
        }
    }
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
    sealed class AndroidTtsInitListener : AndroidJavaProxy
    {
        public AndroidTtsInitListener() : base("android.speech.tts.TextToSpeech$OnInitListener") { }

        void onInit(int status)
        {
            androidTtsInitializing = false;
            androidTtsReady = status == 0;
            if (!androidTtsReady)
            {
                Debug.LogWarning($"[PronunciationSpeaker] Android system voice failed to initialize (status={status}).");
                return;
            }

            string queued = queuedAndroidText;
            string queuedLanguage = queuedAndroidLanguage;
            int queuedVersion = queuedAndroidCommandVersion;
            queuedAndroidText = "";
            queuedAndroidLanguage = "";
            queuedAndroidCommandVersion = -1;
            if (queuedVersion == androidCommandVersion && !string.IsNullOrWhiteSpace(queued))
                SpeakWithAndroidTts(queued, queuedLanguage);
        }
    }

    static bool TrySpeakWithAndroidSystemVoice(string text, string languageCode)
    {
        int commandVersion = androidCommandVersion;
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                if (activity == null)
                    return false;

                androidPendingCommandVersion = commandVersion;
                activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    try
                    {
                        if (commandVersion != androidCommandVersion)
                            return;

                        using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                        using AndroidJavaObject currentActivity = player.GetStatic<AndroidJavaObject>("currentActivity");
                        if (currentActivity == null)
                            return;

                        if (androidTts == null && !androidTtsInitializing)
                        {
                            androidTtsInitializing = true;
                            queuedAndroidText = text;
                            queuedAndroidLanguage = languageCode;
                            queuedAndroidCommandVersion = commandVersion;
                            androidTts = new AndroidJavaObject("android.speech.tts.TextToSpeech", currentActivity, new AndroidTtsInitListener());
                            return;
                        }

                        if (!androidTtsReady)
                        {
                            queuedAndroidText = text;
                            queuedAndroidLanguage = languageCode;
                            queuedAndroidCommandVersion = commandVersion;
                            return;
                        }

                        SpeakWithAndroidTts(text, languageCode);
                    }
                    finally
                    {
                        if (androidPendingCommandVersion == commandVersion)
                            androidPendingCommandVersion = -1;
                    }
                }));
                return true;
            }
        }
        catch (Exception ex)
        {
            if (androidPendingCommandVersion == commandVersion)
                androidPendingCommandVersion = -1;
            Debug.LogWarning($"[PronunciationSpeaker] Android system voice unavailable: {ex.Message}");
            return false;
        }
    }

    static void SpeakWithAndroidTts(string text, string languageCode)
    {
        if (androidTts == null || string.IsNullOrWhiteSpace(text))
            return;

        string language = ResolveSpeechLanguage(languageCode, text);
        string androidLanguage = language == "od" ? "or" : language;
        if (!TrySetAndroidLanguage(androidLanguage, "IN"))
        {
            Debug.LogWarning($"[BuddyTTS] Android has no installed '{language}' voice. Falling back to English.");
            if (!TrySetAndroidLanguage("en", "IN"))
                TrySetAndroidLanguage("en", "US");
        }

        // Every segment begins only after isSpeaking becomes false, so changing
        // the global TTS locale cannot affect an utterance that is still queued.
        // QUEUE_FLUSH also makes cancellation/recovery deterministic.
        string utteranceId = $"the_script_buddy_{++androidUtteranceSequence}";
        androidTts.Call<int>("speak", text, 0, null, utteranceId);
    }

    static bool TrySetAndroidLanguage(string language, string country)
    {
        try
        {
            using var locale = new AndroidJavaObject("java.util.Locale", language, country);
            int status = androidTts.Call<int>("setLanguage", locale);
            return status >= 0;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PronunciationSpeaker] Could not select Android voice language: {ex.Message}");
            return false;
        }
    }
#endif

    static string ResolveSpeechLanguage(string requestedLanguage, string text)
    {
        string normalized = BuddySpeechSequence.NormalizeLanguage(requestedLanguage, "");
        if (!string.IsNullOrWhiteSpace(normalized))
            return normalized;

        if (ContainsScript(text, '\u0980', '\u09FF')) return normalized == "as" ? "as" : "bn";
        if (ContainsScript(text, '\u0A80', '\u0AFF')) return "gu";
        if (ContainsScript(text, '\u0B80', '\u0BFF')) return "ta";
        if (ContainsScript(text, '\u0C00', '\u0C7F')) return "te";
        if (ContainsScript(text, '\u0C80', '\u0CFF')) return "kn";
        if (ContainsScript(text, '\u0D00', '\u0D7F')) return "ml";
        if (ContainsScript(text, '\u0A00', '\u0A7F')) return "pa";
        if (ContainsScript(text, '\u0B00', '\u0B7F')) return "od";
        if (ContainsScript(text, '\u0900', '\u097F'))
            return normalized == "mr" || normalized == "ne" ? normalized : "hi";
        if (ContainsScript(text, '\u0600', '\u06FF'))
            return normalized == "ks" || normalized == "sd" ? normalized : "ur";
        if (ContainsScript(text, '\u1C50', '\u1C7F')) return "sat";

        // Legacy calls without an explicit segment language default to English.
        return "en";
    }

    static bool ContainsScript(string value, char first, char last)
    {
        if (string.IsNullOrEmpty(value)) return false;
        foreach (char character in value)
            if (character >= first && character <= last)
                return true;
        return false;
    }
}
