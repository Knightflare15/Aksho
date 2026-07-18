using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public enum BuddyCallState
{
    Idle,
    Connecting,
    BuddySpeaking,
    ReadyToTalk,
    Listening,
    Thinking,
    TaskAnswering,
    Ended,
}

[Serializable]
public sealed class BuddyCallRequestContext
{
    public string callId = "";
    public int turnIndex;
    public bool isCallTurn;
    public List<string> safeRelationshipMemory = new List<string>();
}

public sealed class BuddyCallSession
{
    public string CallId { get; private set; } = "";
    public string PlaySessionId { get; private set; } = "";
    public string DialogueTaskId { get; private set; } = "";
    public GrammarConceptId ConceptId { get; private set; } = GrammarConceptId.None;
    public int TurnIndex { get; private set; }
    public BuddyCallState State { get; private set; } = BuddyCallState.Idle;
    public bool IsActive => State != BuddyCallState.Idle && State != BuddyCallState.Ended;

    public void Begin(string playSessionId, string dialogueTaskId, GrammarConceptId conceptId)
    {
        CallId = Guid.NewGuid().ToString("N");
        PlaySessionId = playSessionId ?? "";
        DialogueTaskId = dialogueTaskId ?? "";
        ConceptId = conceptId;
        TurnIndex = 0;
        State = BuddyCallState.Connecting;
    }

    public int NextTurn()
    {
        TurnIndex++;
        return TurnIndex;
    }

    public void SetState(BuddyCallState state)
    {
        if (State == BuddyCallState.Ended && state != BuddyCallState.Idle)
            return;
        State = state;
    }

    public void End()
    {
        State = BuddyCallState.Ended;
    }

    public void Reset()
    {
        CallId = "";
        PlaySessionId = "";
        DialogueTaskId = "";
        ConceptId = GrammarConceptId.None;
        TurnIndex = 0;
        State = BuddyCallState.Idle;
    }

    public BuddyCallRequestContext BuildRequestContext(IEnumerable<string> safeRelationshipMemory)
    {
        return new BuddyCallRequestContext
        {
            callId = CallId,
            turnIndex = TurnIndex,
            isCallTurn = IsActive,
            safeRelationshipMemory = safeRelationshipMemory != null
                ? new List<string>(safeRelationshipMemory)
                : new List<string>(),
        };
    }
}

/// <summary>
/// Platform-local speech boundary. Windows development and Android production
/// can use different native engines without changing the Buddy call state machine.
/// </summary>
public interface IBuddySpeechOutput
{
    string ProviderName { get; }
    float Speak(string text, string languageCode = "", string phonicsCueKey = "", string phonicsAnchorWord = "");
    float SpeakSegments(
        IReadOnlyList<BuddySpeechSegment> segments,
        string phonicsCueKey = "",
        string phonicsAnchorWord = "",
        Action completed = null);
    void Stop();
}

public static class BuddySpeechOutputFactory
{
    public static Func<IBuddySpeechOutput> OverrideFactory { get; set; }

    public static IBuddySpeechOutput Create()
    {
        return OverrideFactory != null
            ? OverrideFactory.Invoke()
            : new DeviceBuddySpeechOutput();
    }
}

sealed class DeviceBuddySpeechOutput : IBuddySpeechOutput
{
    public string ProviderName => "Local system TTS";

    public float Speak(string text, string languageCode = "", string phonicsCueKey = "", string phonicsAnchorWord = "")
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0f;

        return SpeakSegments(
            new[] { new BuddySpeechSegment { language = languageCode, text = text.Trim() } },
            phonicsCueKey,
            phonicsAnchorWord);
    }

    public float SpeakSegments(
        IReadOnlyList<BuddySpeechSegment> segments,
        string phonicsCueKey = "",
        string phonicsAnchorWord = "",
        Action completed = null)
    {
        if (segments == null || segments.Count == 0)
        {
            completed?.Invoke();
            return 0f;
        }

        AudioClip cueClip = BuddyPhonicsCueCatalog.LoadClip(phonicsCueKey, phonicsAnchorWord);
        float estimate = cueClip != null ? cueClip.length + 0.2f : 0f;
        foreach (BuddySpeechSegment segment in segments)
            if (segment != null && !string.IsNullOrWhiteSpace(segment.text))
                estimate += EstimateSpeechSeconds(segment.text);

        PronunciationSpeaker.EnsureExists().SpeakSequence(segments, cueClip, completed);
        return estimate;
    }

    public void Stop()
    {
        PronunciationSpeaker.EnsureExists().StopSpeaking();
    }

    static float EstimateSpeechSeconds(string text)
    {
        string[] words = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        return Mathf.Clamp(0.42f * words.Length + 0.6f, 1.2f, 12f);
    }
}

/// <summary>
/// Treats the server segment array as untrusted input. A malformed array falls
/// back to the legacy speechText field as one local-TTS utterance.
/// </summary>
public static class BuddySpeechSequence
{
    public const int MaximumSegments = 8;
    public const int MaximumCharacters = 520;
    public const int MaximumSegmentCharacters = 240;

    static readonly HashSet<string> AllowedLanguages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "as", "bn", "brx", "doi", "en", "gu", "hi", "kn", "kok", "ks", "mai", "ml",
        "mni", "mr", "ne", "od", "pa", "sa", "sat", "sd", "ta", "te", "ur",
    };

    static readonly Regex MarkupPattern = new Regex(
        @"<[^>]+>|```|\*\*|__|~~|\[[^\]]+\]\([^)]+\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    static readonly Regex SlashNotationPattern = new Regex(
        @"/[a-z\u0250-\u02af\u1d00-\u1d7f\u02c8\u02cc\u02d0.\s]{1,18}/",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static List<BuddySpeechSegment> Resolve(
        TranslatorBuddyHintResponse response,
        string defaultLanguage = "en")
    {
        if (TryNormalize(response?.speechSegments, out List<BuddySpeechSegment> normalized))
            return normalized;

        string fallbackText = FirstNonEmpty(response?.speechText, response?.learnerText, response?.buddyResponse);
        if (string.IsNullOrWhiteSpace(fallbackText))
            return new List<BuddySpeechSegment>();

        fallbackText = fallbackText.Trim();
        if (fallbackText.Length > MaximumCharacters)
            fallbackText = fallbackText.Substring(0, MaximumCharacters).Trim();
        return new List<BuddySpeechSegment>
        {
            new BuddySpeechSegment
            {
                language = NormalizeLanguage(response?.responseLanguage, defaultLanguage),
                text = fallbackText,
            }
        };
    }

    public static bool TryNormalize(
        IReadOnlyList<BuddySpeechSegment> source,
        out List<BuddySpeechSegment> normalized)
    {
        normalized = new List<BuddySpeechSegment>();
        if (source == null || source.Count == 0 || source.Count > MaximumSegments)
            return false;

        int characterCount = 0;
        foreach (BuddySpeechSegment segment in source)
        {
            string language = NormalizeLanguage(segment?.language, "");
            string text = segment?.text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(language) || string.IsNullOrWhiteSpace(text) ||
                text.Length > MaximumSegmentCharacters || MarkupPattern.IsMatch(text) ||
                SlashNotationPattern.IsMatch(text))
            {
                normalized.Clear();
                return false;
            }

            characterCount += text.Length + (normalized.Count > 0 ? 1 : 0);
            if (characterCount > MaximumCharacters)
            {
                normalized.Clear();
                return false;
            }

            normalized.Add(new BuddySpeechSegment { language = language, text = text });
        }
        return normalized.Count > 0;
    }

    public static string NormalizeLanguage(string value, string fallback = "en")
    {
        string normalized = (value ?? "").Trim().ToLowerInvariant().Replace('_', '-');
        int separator = normalized.IndexOf('-');
        if (separator > 0)
            normalized = normalized.Substring(0, separator);
        if (normalized == "or")
            normalized = "od";
        if (AllowedLanguages.Contains(normalized))
            return normalized;

        string normalizedFallback = (fallback ?? "en").Trim().ToLowerInvariant().Replace('_', '-');
        separator = normalizedFallback.IndexOf('-');
        if (separator > 0)
            normalizedFallback = normalizedFallback.Substring(0, separator);
        if (normalizedFallback == "or")
            normalizedFallback = "od";
        return AllowedLanguages.Contains(normalizedFallback) ? normalizedFallback : "";
    }

    static string FirstNonEmpty(params string[] values)
    {
        foreach (string value in values)
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        return "";
    }
}

public static class BuddyPhonicsCueCatalog
{
    static readonly Dictionary<string, string> AnchorWords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "short_a", "APPLE" }, { "short_e", "EGG" }, { "short_i", "INK" },
        { "short_o", "OX" }, { "short_u", "CUP" }, { "long_a", "RAIN" },
        { "long_e", "TREE" }, { "long_i", "ICE" }, { "long_o", "BOAT" },
        { "long_u", "UNICORN" }, { "sound_b", "BAT" }, { "sound_d", "DOG" },
        { "sound_f", "FISH" }, { "sound_g", "GOAT" }, { "sound_h", "HAT" },
        { "sound_j", "JAM" }, { "sound_k", "CAT" }, { "sound_l", "LION" },
        { "sound_m", "MAN" }, { "sound_n", "NEST" }, { "sound_p", "PIG" },
        { "sound_r", "RAT" }, { "sound_s", "SUN" }, { "sound_t", "TAP" },
        { "sound_v", "VAN" }, { "sound_w", "WIG" }, { "sound_y", "YAK" },
        { "sound_z", "ZEBRA" }, { "sound_sh", "FISH" },
    };

    public static AudioClip LoadClip(string cueKey, string requestedAnchorWord = "")
    {
        if (string.IsNullOrWhiteSpace(cueKey) || !AnchorWords.TryGetValue(cueKey.Trim(), out string anchorWord))
            return null;

        // The server's anchor is display metadata only. Audio always comes from
        // this allow-listed catalog so generated text cannot select arbitrary media.
        SpellRegistry registry = UnityEngine.Object.FindAnyObjectByType<SpellRegistry>();
        if (registry != null && registry.TryGetSpell(anchorWord, out SpellDefinition definition) && definition?.pronunciationClip != null)
            return definition.pronunciationClip;

#if UNITY_EDITOR
        return UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/Audio/Pronunciations/Spells/{anchorWord}.wav");
#else
        return Resources.Load<AudioClip>($"Audio/Pronunciations/Spells/{anchorWord}");
#endif
    }

    public static string AnchorWordFor(string cueKey)
    {
        return !string.IsNullOrWhiteSpace(cueKey) && AnchorWords.TryGetValue(cueKey.Trim(), out string word)
            ? word.ToLowerInvariant()
            : "";
    }
}

[Serializable]
sealed class BuddyRelationshipMemoryData
{
    public List<string> safeTags = new List<string>();
}

/// <summary>
/// Stores only a tiny, allow-listed relationship profile on the device. Raw
/// child conversations and personal details are deliberately never persisted.
/// </summary>
public sealed class BuddyRelationshipMemoryStore
{
    static readonly HashSet<string> AllowedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "interest:pirates",
        "interest:space",
        "interest:dinosaurs",
        "interest:animals",
        "interest:sports",
        "interest:music",
        "interest:drawing",
        "interest:magic",
        "interest:cars",
        "interest:stories",
        "style:playful",
        "style:short",
        "style:examples",
    };

    readonly string filePath;
    readonly List<string> safeTags = new List<string>();

    public IReadOnlyList<string> SafeTags => safeTags;

    public BuddyRelationshipMemoryStore(string studentId)
    {
        string safeStudentId = SanitizeFileName(string.IsNullOrWhiteSpace(studentId) ? "local" : studentId);
        string directory = Path.Combine(Application.persistentDataPath, "BuddyRelationshipMemory");
        filePath = Path.Combine(directory, $"{safeStudentId}.json");
        Load();
    }

    public void Merge(IEnumerable<string> candidates)
    {
        bool changed = false;
        if (candidates != null)
        {
            foreach (string candidate in candidates)
            {
                string normalized = (candidate ?? "").Trim().ToLowerInvariant();
                if (!AllowedTags.Contains(normalized) || safeTags.Contains(normalized))
                    continue;
                safeTags.Add(normalized);
                changed = true;
                if (safeTags.Count >= 12)
                    break;
            }
        }

        if (changed)
            Save();
    }

    void Load()
    {
        safeTags.Clear();
        try
        {
            if (!File.Exists(filePath))
                return;
            BuddyRelationshipMemoryData data = JsonUtility.FromJson<BuddyRelationshipMemoryData>(File.ReadAllText(filePath));
            if (data?.safeTags == null)
                return;
            foreach (string tag in data.safeTags)
            {
                string normalized = (tag ?? "").Trim().ToLowerInvariant();
                if (AllowedTags.Contains(normalized) && !safeTags.Contains(normalized))
                    safeTags.Add(normalized);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[BuddyMemory] Could not load safe local memory: {ex.Message}");
        }
    }

    void Save()
    {
        try
        {
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(filePath, JsonUtility.ToJson(new BuddyRelationshipMemoryData
            {
                safeTags = new List<string>(safeTags)
            }, true));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[BuddyMemory] Could not save safe local memory: {ex.Message}");
        }
    }

    static string SanitizeFileName(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');
        return value;
    }
}

/// <summary>A voice-call overlay: state only, never generated transcript text.</summary>
public sealed class BuddyCallOverlayRuntime : MonoBehaviour
{
    static BuddyCallOverlayRuntime instance;
    GameObject root;
    TextMeshProUGUI status;
    Button talkButton;
    TextMeshProUGUI talkLabel;
    Button grimoireAssistButton;
    TextMeshProUGUI grimoireAssistLabel;
    AudioSource ringSource;
    Action muteRequested;
    Action endRequested;
    Action grimoireAssistRequested;

    public static BuddyCallOverlayRuntime EnsureExists()
    {
        if (instance != null)
            return instance;
        GameObject go = new GameObject("BuddyCallOverlay");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<BuddyCallOverlayRuntime>();
        return instance;
    }

    void Awake()
    {
        Build();
        Hide();
    }

    public void Show(Action onMuteRequested, Action onEndRequested)
    {
        muteRequested = onMuteRequested;
        endRequested = onEndRequested;
        if (root != null)
            root.SetActive(true);
        SetState(BuddyCallState.Connecting);
        SetGrimoireAssist(null);
    }

    public void SetGrimoireAssist(Action onRequested, string label = "Grimoire Assist")
    {
        grimoireAssistRequested = onRequested;
        if (grimoireAssistButton != null)
            grimoireAssistButton.gameObject.SetActive(onRequested != null);
        if (grimoireAssistLabel != null)
            grimoireAssistLabel.text = string.IsNullOrWhiteSpace(label) ? "Grimoire Assist" : label;
    }

    public void Hide()
    {
        if (root != null)
            root.SetActive(false);
        if (ringSource != null)
            ringSource.Stop();
        muteRequested = null;
        endRequested = null;
        grimoireAssistRequested = null;
    }

    public void SetState(BuddyCallState state)
    {
        if (status == null || talkButton == null || talkLabel == null)
            return;

        status.text = state switch
        {
            BuddyCallState.Connecting => "Ringing Buddy...",
            BuddyCallState.BuddySpeaking => "Buddy is talking",
            BuddyCallState.ReadyToTalk => "Connected",
            BuddyCallState.Listening => "Listening...",
            BuddyCallState.Thinking => "Buddy is thinking...",
            BuddyCallState.TaskAnswering => "Answer the task",
            BuddyCallState.Ended => "Call ended",
            _ => "Buddy call",
        };
        talkLabel.text = "Mute";
        talkButton.interactable = state != BuddyCallState.Connecting && state != BuddyCallState.Ended;
        if (ringSource != null)
        {
            if (state == BuddyCallState.Connecting && !ringSource.isPlaying)
                ringSource.Play();
            else if (state != BuddyCallState.Connecting && ringSource.isPlaying)
                ringSource.Stop();
        }
    }

    public void SetMicrophoneMuted(bool muted)
    {
        if (talkLabel != null)
            talkLabel.text = muted ? "Unmute" : "Mute";
    }

    void Build()
    {
        if (root != null)
            return;

        root = new GameObject("BuddyCallCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 720;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        ringSource = root.AddComponent<AudioSource>();
        ringSource.playOnAwake = false;
        ringSource.loop = true;
        ringSource.volume = 0.16f;
        ringSource.clip = BuildRingClip();

        GameObject panel = new GameObject("CallPanel", typeof(RectTransform), typeof(Image), typeof(Outline));
        panel.transform.SetParent(root.transform, false);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(1f, 1f);
        panelRect.anchorMax = new Vector2(1f, 1f);
        panelRect.pivot = new Vector2(1f, 1f);
        panelRect.anchoredPosition = new Vector2(-34f, -34f);
        panelRect.sizeDelta = new Vector2(410f, 260f);
        panel.GetComponent<Image>().color = new Color(0.035f, 0.09f, 0.12f, 0.97f);
        Outline outline = panel.GetComponent<Outline>();
        outline.effectColor = new Color(0.18f, 0.85f, 0.74f, 0.9f);
        outline.effectDistance = new Vector2(2f, -2f);

        status = MakeText(panel.transform, "CallStatus", new Vector2(20f, -18f), new Vector2(-20f, -70f), 25f);
        status.alignment = TextAlignmentOptions.Center;

        grimoireAssistButton = MakeButton(panel.transform, "GrimoireAssist", "Grimoire Assist", new Vector2(22f, 104f), new Vector2(366f, 52f), out grimoireAssistLabel);
        grimoireAssistButton.onClick.AddListener(() => grimoireAssistRequested?.Invoke());
        grimoireAssistButton.gameObject.SetActive(false);

        talkButton = MakeButton(panel.transform, "Mute", "Mute", new Vector2(22f, 22f), new Vector2(238f, 70f), out talkLabel);
        talkButton.onClick.AddListener(() => muteRequested?.Invoke());

        Button end = MakeButton(panel.transform, "EndCall", "End Call", new Vector2(254f, 22f), new Vector2(134f, 70f), out _);
        end.onClick.AddListener(() => endRequested?.Invoke());
    }

    static AudioClip BuildRingClip()
    {
        const int sampleRate = 22050;
        const float durationSeconds = 2.4f;
        int sampleCount = Mathf.RoundToInt(sampleRate * durationSeconds);
        var samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            float time = i / (float)sampleRate;
            bool audible = (time >= 0.08f && time <= 0.42f) || (time >= 0.58f && time <= 0.92f);
            if (!audible)
                continue;
            float local = time < 0.5f ? time - 0.08f : time - 0.58f;
            float envelope = Mathf.Clamp01(local / 0.04f) * Mathf.Clamp01((0.34f - local) / 0.06f);
            samples[i] = envelope * (Mathf.Sin(2f * Mathf.PI * 440f * time) + Mathf.Sin(2f * Mathf.PI * 480f * time)) * 0.22f;
        }
        AudioClip clip = AudioClip.Create("BuddyPhoneRing", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    static TextMeshProUGUI MakeText(Transform parent, string name, Vector2 offsetMin, Vector2 offsetMax, float size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = offsetMin;
        text.rectTransform.offsetMax = offsetMax;
        text.fontSize = size;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        return text;
    }

    static Button MakeButton(Transform parent, string name, string text, Vector2 position, Vector2 size, out TextMeshProUGUI label)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        go.GetComponent<Image>().color = new Color(0.08f, 0.24f, 0.28f, 1f);
        label = MakeText(go.transform, "Label", Vector2.zero, Vector2.zero, 21f);
        label.text = text;
        return go.GetComponent<Button>();
    }
}
