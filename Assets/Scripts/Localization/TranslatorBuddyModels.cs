using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public enum SemanticZoneKind
{
    Town,
    Route,
    Gym,
}

public enum TranslatorAssistMode
{
    Full,
    Partial,
    Off,
}

public enum TranslatorProviderMode
{
    TextFallback,
    RestEndpoints,
}

public enum TranslatorSpeechCostPolicy
{
    CachedAndDeviceOnly,
    RemoteTtsOptIn,
}

public static class BuddyLanguageCatalog
{
    public static readonly string[] Codes =
    {
        "hi", "bn", "ta", "te", "mr", "gu", "kn", "ml", "pa", "od", "en",
        "as", "ur", "ne", "kok", "ks", "sd", "sa", "sat", "mni", "brx", "mai", "doi",
    };

    public static readonly string[] Names =
    {
        "Hindi", "Bengali", "Tamil", "Telugu", "Marathi", "Gujarati", "Kannada", "Malayalam", "Punjabi", "Odia", "English",
        "Assamese", "Urdu", "Nepali", "Konkani", "Kashmiri", "Sindhi", "Sanskrit", "Santali", "Manipuri", "Bodo", "Maithili", "Dogri",
    };

    public static int IndexOf(string languageCode)
    {
        string code = (languageCode ?? "").Trim().ToLowerInvariant();
        int separator = code.IndexOfAny(new[] { '-', '_' });
        if (separator > 0) code = code.Substring(0, separator);
        if (code == "or") code = "od";
        for (int i = 0; i < Codes.Length; i++)
            if (string.Equals(Codes[i], code, StringComparison.OrdinalIgnoreCase))
                return i;
        return 0;
    }

    public static string DisplayName(string languageCode)
    {
        return Names[IndexOf(languageCode)];
    }
}

[Serializable]
public sealed class TranslatorBuddyHintRequest
{
    public string userId = "";
    public string sessionId = "";
    public string interactionType = "";
    public string sourceLanguage = "hi";
    public string targetLanguage = "en";
    public string npcLine = "";
    public string learnerAttempt = "";
    public string expectedEnglishResponse = "";
    public string deterministicScaffold = "";
    public string conceptId = "";
    public string conceptTitle = "";
    public string grimoireExcerpt = "";
    public string teacherFacingSignal = "";
    public string dialogueTaskId = "";
    public string contentId = "";
    public string inputSource = "buddy_dialogue_support";
    public string buddyContractId = "adaptive_hint";
    public string promptTemplateId = "translator_buddy_hint_v1";
    public string policyVersion = "buddy_policy_v1";
    public bool noAiDuringCombat = true;
}

[Serializable]
public sealed class BuddySpeechSegment
{
    public string language = "en";
    [TextArea] public string text = "";
}

[Serializable]
public sealed class TranslatorBuddyHintResponse
{
    [TextArea] public string buddyResponse = "";
    [TextArea] public string learnerText = "";
    [TextArea] public string speechText = "";
    public List<BuddySpeechSegment> speechSegments = new List<BuddySpeechSegment>();
    public string responseLanguage = "en";
    public string phonicsCueKey = "";
    public string phonicsAnchorWord = "";
    public string status = "";
    public string fallbackReason = "";
    public string hintLevel = "";
    public string provider = "";
    public string model = "";
    public int latencyMs;
    public string routerIntent = "";
    public string routerAction = "";
    public string routerReason = "";
    public string tier = "";
    public float englishRatio;
    public string conversationSkill = "";
    public string wordChoiceIssue = "";
    public string formationIssue = "";
    public string errorCategory = "";
    public string correctedResponse = "";
    [TextArea] public string teacherNote = "";
    public List<string> safeMemoryTags = new List<string>();
    public List<string> relationshipMemoryCandidates = new List<string>();
    public List<string> safetyFlags = new List<string>();
    public bool openGrimoire;
    public string grimoireConceptId = "";
    public string grimoireHighlightKey = "";
    public string callDisposition = "continue";
    public string callId = "";
    public int callTurnIndex;
    public bool reportable = true;
}

[Serializable]
public sealed class FirebaseBuddyHelpRequest
{
    public string schoolId = "";
    public string studentId = "";
    public string sessionId = "";
    public string dialogueTaskId = "";
    public string learnerAttempt = "";
    public string trigger = "ask";
    public string zoneKind = "";
    public string areaId = "";
    public string callId = "";
    public int callTurnIndex;
    public bool isCallTurn;
    public string conceptId = "";
    [TextArea] public string grimoireExcerpt = "";
    public List<string> safeRelationshipMemory = new List<string>();
}

[Serializable]
public sealed class FirebaseBuddyHelpEnvelope
{
    public TranslatorBuddyHintResponse result;
}

[Serializable]
public sealed class FirebaseBuddySpeechTranscriptionRequest
{
    public string schoolId = "";
    public string studentId = "";
    public string audioBase64 = "";
    public string mimeType = "audio/wav";
    public string fileName = "buddy-turn.wav";
    public string languageCode = "unknown";
}

[Serializable]
public sealed class FirebaseBuddySpeechTranscriptionResponse
{
    public string status = "";
    public string fallbackReason = "";
    public string transcript = "";
    public string provider = "";
    public string model = "";
    public string mode = "";
    public string languageCode = "";
    public float languageProbability;
    public float remainingAudioSeconds;
    public int latencyMs;
}

[Serializable]
public sealed class FirebaseBuddySpeechTranscriptionEnvelope
{
    public FirebaseBuddySpeechTranscriptionResponse result;
}

[Serializable]
public class LocalizedDialogueLine
{
    public string lineId = Guid.NewGuid().ToString("N");
    public string dialogueTaskId = "";
    public string regionId = "";
    public string grammarTopic = "";
    public SemanticZoneKind zoneKind = SemanticZoneKind.Town;
    [Tooltip("Short in-world cue that grounds this line in its town, route, or gym situation.")]
    public string contextCue = "";
    public GrammarConceptId conceptId = GrammarConceptId.None;
    public string subskillId = "";
    [TextArea] public string npcLine;
    [TextArea] public string sourceText;
    public string sourceLanguage = "en";
    public string targetLanguage = "hi";
    [TextArea] public string expectedEnglishResponse;
    public List<string> acceptedEnglishResponses = new List<string>();
    [Tooltip("Words that carry the grammar target and should be preferred by fill-in-the-blank scaffolds.")]
    public List<string> grammarFocusWords = new List<string>();
    [Tooltip("Plausible extra transcript words that should be left out of a sentence jumble.")]
    public List<string> jumbleDistractorWords = new List<string>();
    [Tooltip("Optional authored choices for spoken NPC answers. If empty, choices are drawn from other tasks in the same grammar concept.")]
    public List<string> spokenAnswerChoices = new List<string>();
    public bool useSpokenAnswerChoices = true;
    public bool overrideAssistMode;
    public TranslatorAssistMode assistMode = TranslatorAssistMode.Full;
    public GrammarDialogueInputMode inputMode = GrammarDialogueInputMode.SpeakOnly;
    public GrammarDialogueMalfunctionType malfunctionType = GrammarDialogueMalfunctionType.None;
    public GrammarPhrasePattern grammarPattern = GrammarPhrasePattern.LetterOnly;
    public GrammarPracticeScaffoldMode scaffoldMode = GrammarPracticeScaffoldMode.AuthoredSubtitle;
    public TranslatorBuddyUseCase buddyUseCase = TranslatorBuddyUseCase.AuthoredLocalExplanation;
    public bool allowAiHint = true;
    public bool openGrimoireOnWrongAnswer = true;
    [TextArea] public string teachingNote;
    [TextArea] public string localLanguageHint;
    [TextArea] public string cachedTranslation;
    public AudioClip cachedSpeech;
    public string providerName;
    public string ttsProviderName;
    public string lastError;
    public bool remoteTranslationRequested;
    public bool remoteSpeechRequested;
    public int remoteTranslationAttempts;
    public int remoteSpeechAttempts;
    public bool remoteTranslationSucceeded;
    public bool remoteSpeechSucceeded;
}

[Serializable]
public sealed class TranslatorRestRequest
{
    public string text;
    public string sourceLanguage;
    public string targetLanguage;
    public string ttsBackend;
    public string voice;
}

[Serializable]
public sealed class TranslatorRestResponse
{
    public string translation;
    public string translatedText;
    public string text;
    public string audioUrl;
    public string audioBase64;
    public string audioContentType;
    public string providerName;
    public string error;
    public bool fallback;
    public bool translationConfigured;
}

[Serializable]
public sealed class TtsRestRequest
{
    public string text;
    public string language;
    public string ttsBackend;
    public string voice;
}

[Serializable]
public sealed class TtsRestResponse
{
    public string audioUrl;
    public string audioBase64;
    public string audioContentType;
    public string providerName;
    public string error;
    public bool fallback;
    public bool ttsConfigured;
}

[Serializable]
public sealed class TranslatorBuddyReadinessReport
{
    public bool isReady;
    public string providerName = "";
    public bool translationEndpointConfigured;
    public bool ttsEndpointConfigured;
    public bool speechRequested;
    public bool remoteTextFallbackAllowed;
    public List<string> errors = new List<string>();
    public List<string> warnings = new List<string>();
}

public interface ITranslatorBuddyProvider
{
    string ProviderName { get; }
    bool TryTranslate(string sourceText, string sourceLanguage, string targetLanguage, out string translatedText);
    bool TryGetSpeech(string text, string language, out AudioClip speech);
}

public sealed class TextFallbackTranslatorBuddyProvider : ITranslatorBuddyProvider
{
    public string ProviderName => "Text fallback";

    public bool TryTranslate(string sourceText, string sourceLanguage, string targetLanguage, out string translatedText)
    {
        translatedText = sourceText ?? "";
        return !string.IsNullOrWhiteSpace(translatedText);
    }

    public bool TryGetSpeech(string text, string language, out AudioClip speech)
    {
        speech = null;
        return false;
    }
}

public partial class TranslatorBuddyService : MonoBehaviour
{
    public TranslatorAssistMode currentAssistMode = TranslatorAssistMode.Full;
    public TranslatorProviderMode providerMode = TranslatorProviderMode.TextFallback;
    public string preferredLanguage = "hi";
    [Tooltip("Let Saaras detect the spoken language on every Buddy turn. Recommended for code-mixed and multilingual homes.")]
    public bool autoDetectSpokenLanguage = true;
    public string ttsBackendHint = "SPRINGLab/Indic-Mio";
    public string voice = "";
    public TranslatorSpeechCostPolicy speechCostPolicy = TranslatorSpeechCostPolicy.CachedAndDeviceOnly;

    [Header("REST Provider")]
    [Tooltip("POST endpoint accepting TranslatorRestRequest and returning TranslatorRestResponse.")]
    public string translationEndpointUrl = "";
    [Tooltip("POST endpoint accepting TtsRestRequest and returning TtsRestResponse or raw audio/wav.")]
    public string ttsEndpointUrl = "";
    [Tooltip("Legacy remote TTS switch. It is honored only when speechCostPolicy is RemoteTtsOptIn.")]
    public bool requestSpeechAudio;
    public bool allowRemoteTextFallback;
    public bool requireProductionReadyEndpoints = true;
    public bool requireProductionTts;
    [Min(1)] public int requestTimeoutSeconds = 20;
    [Min(1)] public int maxRemoteAttempts = 3;
    [Min(0.25f)] public float remoteRetryBackoffSeconds = 2f;

    [Header("Realtime Buddy Call")]
    public bool enableAiTutor = true;

    readonly Dictionary<string, LocalizedDialogueLine> cache = new Dictionary<string, LocalizedDialogueLine>();
    readonly HashSet<string> pendingRemoteLines = new HashSet<string>();
    readonly Dictionary<string, float> nextRemoteRetryAt = new Dictionary<string, float>();
    readonly Dictionary<string, int> conceptMissCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, int> subskillMissCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    ITranslatorBuddyProvider provider;

    public TranslatorAssistMode CurrentAssistMode => currentAssistMode;
    public bool TranslationEndpointConfigured => !string.IsNullOrWhiteSpace(translationEndpointUrl);
    public bool TtsEndpointConfigured => !string.IsNullOrWhiteSpace(ttsEndpointUrl);
    public bool RemoteTtsAllowed => speechCostPolicy == TranslatorSpeechCostPolicy.RemoteTtsOptIn && requestSpeechAudio;
    public bool UsesRemoteEndpoints => providerMode == TranslatorProviderMode.RestEndpoints &&
                                       TranslationEndpointConfigured;
    public string ProviderName => providerMode == TranslatorProviderMode.RestEndpoints ? "REST endpoints" :
        provider != null ? provider.ProviderName : "none";
    public event Action<LocalizedDialogueLine> OnLineResolved;

    public static TranslatorBuddyService EnsureExists()
    {
        TranslatorBuddyService existing = FindAnyObjectByType<TranslatorBuddyService>();
        if (existing != null)
            return existing;

        GameObject go = new GameObject("TranslatorBuddyService");
        TranslatorBuddyService service = go.AddComponent<TranslatorBuddyService>();
        DontDestroyOnLoad(go);
        return service;
    }

    void Awake()
    {
        provider ??= new TextFallbackTranslatorBuddyProvider();
        CurriculumSessionManager curriculum = CurriculumSessionManager.EnsureExists();
        if (!string.IsNullOrWhiteSpace(curriculum.buddyHomeLanguage))
            preferredLanguage = curriculum.buddyHomeLanguage;
        if (enableAiTutor && curriculum.HasStudentSession &&
            (string.IsNullOrWhiteSpace(curriculum.firebaseFunctionsBaseUrl) || string.IsNullOrWhiteSpace(curriculum.studentIdToken)))
            Debug.LogWarning("[TranslatorBuddy] Buddy cloud unavailable. Check login and the Firebase Functions URL.");
    }

    public void SetAssistMode(TranslatorAssistMode mode)
    {
        currentAssistMode = mode;
    }

    public TranslatorBuddyReadinessReport BuildReadinessReport(bool productionStrict = false)
    {
        bool strict = productionStrict || requireProductionReadyEndpoints;
        var report = new TranslatorBuddyReadinessReport
        {
            providerName = ProviderName,
            translationEndpointConfigured = TranslationEndpointConfigured,
            ttsEndpointConfigured = TtsEndpointConfigured,
            speechRequested = RemoteTtsAllowed,
            remoteTextFallbackAllowed = allowRemoteTextFallback,
        };

        if (providerMode != TranslatorProviderMode.RestEndpoints)
        {
            string message = "Translator Buddy is using local text fallback instead of REST endpoints.";
            if (strict)
                report.errors.Add(message);
            else
                report.warnings.Add(message);
        }

        if (providerMode == TranslatorProviderMode.RestEndpoints && !TranslationEndpointConfigured)
            report.errors.Add("Translator Buddy REST mode requires translationEndpointUrl.");

        if (RemoteTtsAllowed && requireProductionTts && !TtsEndpointConfigured)
            report.errors.Add("Translator Buddy speech is requested, but ttsEndpointUrl is empty.");

        if (allowRemoteTextFallback)
            report.warnings.Add("Remote text fallback is allowed; production translation quality is not guaranteed.");

        report.isReady = report.errors.Count == 0;
        return report;
    }

    public LocalizedDialogueLine ResolveLine(string lineId, string sourceText, string sourceLanguage = "en")
    {
        string cacheKey = BuildCacheKey(lineId, sourceText, preferredLanguage);
        if (cache.TryGetValue(cacheKey, out LocalizedDialogueLine cached))
        {
            BeginRemoteRefreshIfNeeded(cacheKey, cached);
            return cached;
        }

        var line = new LocalizedDialogueLine
        {
            lineId = string.IsNullOrWhiteSpace(lineId) ? sourceText ?? "" : lineId,
            sourceText = sourceText ?? "",
            sourceLanguage = string.IsNullOrWhiteSpace(sourceLanguage) ? "en" : sourceLanguage,
            targetLanguage = preferredLanguage,
            providerName = ProviderName,
        };

        if (provider != null &&
            provider.TryTranslate(line.sourceText, line.sourceLanguage, preferredLanguage, out string translated))
            line.cachedTranslation = translated;
        else
            line.cachedTranslation = line.sourceText;

        if (provider != null &&
            provider.TryGetSpeech(line.cachedTranslation, preferredLanguage, out AudioClip speech))
            line.cachedSpeech = speech;

        cache[cacheKey] = line;
        BeginRemoteRefreshIfNeeded(cacheKey, line);
        return line;
    }

    public string BuildAssistText(string lineId, string sourceText, string sourceLanguage = "en")
    {
        if (currentAssistMode == TranslatorAssistMode.Off)
            return "";

        LocalizedDialogueLine line = ResolveLine(lineId, sourceText, sourceLanguage);
        if (currentAssistMode == TranslatorAssistMode.Partial)
            return string.IsNullOrWhiteSpace(line.cachedTranslation)
                ? ""
                : $"Signal weak: {FumbleResponse(line.cachedTranslation, line.lineId)}";

        return line.cachedTranslation;
    }

    public string BuildAssistText(LocalizedDialogueLine dialogueLine)
    {
        if (dialogueLine == null)
            return "";

        TranslatorAssistMode previous = currentAssistMode;
        if (dialogueLine.overrideAssistMode)
            currentAssistMode = dialogueLine.assistMode;

        string assist = BuildAssistText(
            dialogueLine.lineId,
            ResolveNpcLine(dialogueLine),
            dialogueLine.sourceLanguage);
        currentAssistMode = previous;

        return assist;
    }

    public string BuildResponsePrompt(LocalizedDialogueLine dialogueLine)
    {
        if (dialogueLine == null || string.IsNullOrWhiteSpace(dialogueLine.expectedEnglishResponse))
            return "";

        string response = dialogueLine.expectedEnglishResponse.Trim();
        TranslatorAssistMode effectiveMode = dialogueLine.overrideAssistMode ? dialogueLine.assistMode : currentAssistMode;
        return effectiveMode switch
        {
            TranslatorAssistMode.Full => BuildFullResponsePrompt(dialogueLine, response),
            TranslatorAssistMode.Partial => BuildPartialResponsePrompt(
                BuildPartialPromptText(dialogueLine, response),
                dialogueLine.malfunctionType),
            _ => "",
        };
    }

    /// <summary>Returns the optional teaching explanation for the dialogue hint UI.</summary>
    public string BuildTeachingHint(LocalizedDialogueLine dialogueLine)
    {
        if (dialogueLine == null)
            return "";

        string teaching = BuildTeachingLead(dialogueLine);
        if (!string.IsNullOrWhiteSpace(dialogueLine.localLanguageHint))
            teaching = string.IsNullOrWhiteSpace(teaching)
                ? dialogueLine.localLanguageHint.Trim()
                : $"{dialogueLine.localLanguageHint.Trim()}\n{teaching}";
        return teaching;
    }

    public void RegisterSuccessfulResponse(LocalizedDialogueLine dialogueLine)
    {
        if (dialogueLine == null)
            return;

        string conceptKey = BuildConceptCounterKey(dialogueLine.conceptId);
        string subskillKey = BuildSubskillCounterKey(dialogueLine);
        if (!string.IsNullOrWhiteSpace(conceptKey))
            conceptMissCounts.Remove(conceptKey);
        if (!string.IsNullOrWhiteSpace(subskillKey))
            subskillMissCounts.Remove(subskillKey);
    }

    public TranslatorBuddyHintRequest BuildHintRequest(
        LocalizedDialogueLine dialogueLine,
        string learnerAttempt = "",
        TutorFeedbackPlan feedback = null,
        TranslatorBuddyUseCase requestedUseCase = TranslatorBuddyUseCase.AdaptiveHint)
    {
        CurriculumSessionManager curriculum = CurriculumSessionManager.Instance;
        var request = new TranslatorBuddyHintRequest
        {
            userId = curriculum != null ? curriculum.activeStudentId : "",
            sessionId = curriculum != null ? curriculum.BuddyLearningSessionId : "",
            interactionType = requestedUseCase.ToString(),
            sourceLanguage = preferredLanguage,
            targetLanguage = "en",
            npcLine = ResolveNpcLine(dialogueLine),
            learnerAttempt = learnerAttempt ?? "",
            // Route requests must never carry an answer key. The Firebase callable
            // also resolves the task independently and enforces this boundary.
            expectedEnglishResponse = dialogueLine != null &&
                                      (dialogueLine.overrideAssistMode ? dialogueLine.assistMode : currentAssistMode) == TranslatorAssistMode.Full
                ? dialogueLine.expectedEnglishResponse ?? ""
                : "",
            deterministicScaffold = dialogueLine != null ? dialogueLine.scaffoldMode.ToString() : "",
            conceptId = dialogueLine != null ? dialogueLine.conceptId.ToString() : GrammarConceptId.None.ToString(),
            teacherFacingSignal = feedback != null ? feedback.errorCategory ?? "" : "",
            dialogueTaskId = dialogueLine != null
                ? string.IsNullOrWhiteSpace(dialogueLine.dialogueTaskId) ? dialogueLine.lineId : dialogueLine.dialogueTaskId
                : "",
            contentId = dialogueLine != null
                ? string.IsNullOrWhiteSpace(dialogueLine.dialogueTaskId) ? dialogueLine.lineId : dialogueLine.dialogueTaskId
                : "",
            inputSource = "buddy_dialogue_support",
            buddyContractId = requestedUseCase == TranslatorBuddyUseCase.AuthoredLocalExplanation ? "npc_explanation" : "adaptive_hint",
            promptTemplateId = "translator_buddy_hint_v1",
            policyVersion = "buddy_policy_v1",
            noAiDuringCombat = true,
        };

        if (dialogueLine != null && GrammarGrimoireCatalog.TryGetPage(dialogueLine.conceptId, out GrammarGrimoirePage page))
        {
            request.conceptTitle = page.title;
            request.grimoireExcerpt = GrammarGrimoireCatalog.BuildBuddyExcerpt(page);
        }

        return request;
    }

    public void SpeakAssistLine(string lineId, string sourceText, string sourceLanguage = "en")
    {
        if (currentAssistMode == TranslatorAssistMode.Off)
            return;

        LocalizedDialogueLine line = ResolveLine(lineId, sourceText, sourceLanguage);
        string text = currentAssistMode == TranslatorAssistMode.Partial
            ? BuildAssistText(lineId, sourceText, sourceLanguage)
            : line.cachedTranslation;
        PronunciationSpeaker.EnsureExists().Speak(text, line.cachedSpeech);
    }

}

public static class TranslatorAudioClipDecoder
{
    public static bool TryDecodeWav(byte[] wavBytes, string clipName, out AudioClip clip)
    {
        clip = null;
        if (wavBytes == null || wavBytes.Length < 44)
            return false;
        if (wavBytes[0] != 'R' || wavBytes[1] != 'I' || wavBytes[2] != 'F' || wavBytes[3] != 'F' ||
            wavBytes[8] != 'W' || wavBytes[9] != 'A' || wavBytes[10] != 'V' || wavBytes[11] != 'E')
            return false;

        int channels = 1;
        int sampleRate = 44100;
        int bitsPerSample = 16;
        int dataOffset = -1;
        int dataSize = 0;
        int offset = 12;
        while (offset + 8 <= wavBytes.Length)
        {
            string chunkId = Encoding.ASCII.GetString(wavBytes, offset, 4);
            int chunkSize = BitConverter.ToInt32(wavBytes, offset + 4);
            int payloadOffset = offset + 8;
            if (chunkSize < 0 || payloadOffset + chunkSize > wavBytes.Length)
                return false;

            if (chunkId == "fmt " && chunkSize >= 16)
            {
                short audioFormat = BitConverter.ToInt16(wavBytes, payloadOffset);
                channels = Mathf.Max(1, BitConverter.ToInt16(wavBytes, payloadOffset + 2));
                sampleRate = Mathf.Max(1, BitConverter.ToInt32(wavBytes, payloadOffset + 4));
                bitsPerSample = Mathf.Max(1, BitConverter.ToInt16(wavBytes, payloadOffset + 14));
                if (audioFormat != 1 || bitsPerSample != 16)
                    return false;
            }
            else if (chunkId == "data")
            {
                dataOffset = payloadOffset;
                dataSize = chunkSize;
                break;
            }

            offset = payloadOffset + chunkSize + (chunkSize % 2);
        }

        if (dataOffset < 0 || dataSize <= 0)
            return false;

        int sampleCount = dataSize / 2;
        int frames = sampleCount / Mathf.Max(1, channels);
        if (frames <= 0)
            return false;

        float[] samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            short sample = BitConverter.ToInt16(wavBytes, dataOffset + i * 2);
            samples[i] = Mathf.Clamp(sample / 32768f, -1f, 1f);
        }

        clip = AudioClip.Create(
            string.IsNullOrWhiteSpace(clipName) ? "Translator TTS" : clipName,
            frames,
            Mathf.Max(1, channels),
            sampleRate,
            false);
        clip.SetData(samples, 0);
        return true;
    }
}

[DisallowMultipleComponent]
public class GrammarRegion : MonoBehaviour
{
    public SemanticZoneKind zoneKind = SemanticZoneKind.Route;
    public string grammarTopic = "Nouns and verbs";
    public TranslatorAssistMode translatorAssist = TranslatorAssistMode.Partial;
    public List<string> encounterNounFamilies = new List<string>();

    void OnTriggerEnter(Collider other)
    {
        if (other.GetComponentInParent<PlayerController>() == null)
            return;

        TranslatorBuddyService buddy = FindAnyObjectByType<TranslatorBuddyService>();
        if (buddy != null)
            buddy.SetAssistMode(translatorAssist);
    }
}
