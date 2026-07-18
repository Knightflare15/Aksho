using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class VoiceInputTestUI : MonoBehaviour
{
    GameObject panel;
    TextMeshProUGUI providerLabel;
    TextMeshProUGUI stateLabel;
    TextMeshProUGUI resultLabel;
    VoiceUnlockRecognizer recognizer;
    FakeSpeechRecognitionProvider fakeProvider;
    readonly List<string> testWords = new List<string> { "CAT", "HEN", "PIG", "HOP", "BUG", "SHIP", "THIN", "CRAB", "BARK", "STONE" };

    public bool IsOpen => panel != null && panel.activeSelf;

    public void Build(Transform parent, System.Action onBack)
    {
        if (panel != null || parent == null)
            return;

        panel = new GameObject("VoiceInputTestPanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        panel.transform.SetParent(parent, false);
        var rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(560f, 620f);
        GameUiTheme.StylePanel(panel);

        var layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(28, 28, 24, 24);
        layout.spacing = 12f;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;

        MakeLabel("Voice Input Test", 30f, GameUiTheme.Text);
        providerLabel = MakeLabel("", 16f, GameUiTheme.TextMuted);
        stateLabel = MakeLabel("", 22f, GameUiTheme.Accent);
        resultLabel = MakeLabel("", 17f, GameUiTheme.Text);
        resultLabel.textWrappingMode = TextWrappingModes.Normal;

        MakeButton("Use Live Microphone", UseLiveProvider, GameUiTheme.ButtonRole.Primary);
        MakeButton("Tap To Listen", StartListening, GameUiTheme.ButtonRole.Secondary);
        MakeButton("Simulate Accepted: CAT", () => Simulate("cat"), GameUiTheme.ButtonRole.Secondary);
        MakeButton("Simulate Alias: SEE A TEA", () => Simulate("see a tea"), GameUiTheme.ButtonRole.Secondary);
        MakeButton("Simulate Unknown: CAT", () => Simulate("cat"), GameUiTheme.ButtonRole.Secondary);
        MakeButton("Simulate Timeout", SimulateTimeout, GameUiTheme.ButtonRole.Secondary);
        MakeButton("Back", () =>
        {
            Close();
            onBack?.Invoke();
        }, GameUiTheme.ButtonRole.Quiet);

        recognizer = gameObject.GetComponent<VoiceUnlockRecognizer>() ?? gameObject.AddComponent<VoiceUnlockRecognizer>();
        recognizer.OnDisplayStateChanged += HandleStateChanged;
        recognizer.OnKeywordRecognized += HandleRecognized;
        recognizer.OnPronunciationInsightReady += HandlePronunciationInsightReady;
        UseLiveProvider();
        panel.SetActive(false);
    }

    public void Open()
    {
        if (panel == null) return;
        panel.SetActive(true);
        RefreshStatus();
    }

    public void Close()
    {
        recognizer?.StopListening();
        if (panel != null) panel.SetActive(false);
    }

    void UseLiveProvider()
    {
        fakeProvider = null;
        recognizer.SetProvider(SpeechRecognitionProviderFactory.Create());
        ConfigureRecognizer();
        resultLabel.text = recognizer.IsAvailable
            ? "Live provider ready. Press Tap To Listen, pause briefly, then say one spell clearly. Windows recognition uses your installed Windows speech language."
            : "Live voice input is unavailable on this platform. Simulated tests still work.";
        RefreshStatus();
    }

    void UseFakeProvider()
    {
        if (fakeProvider != null) return;
        fakeProvider = new FakeSpeechRecognitionProvider();
        recognizer.SetProvider(fakeProvider);
        ConfigureRecognizer();
    }

    void ConfigureRecognizer()
    {
        var aliases = new Dictionary<string, string>
        {
            { "SEE A TEA", "CAT" }
        };
        recognizer.ConfigureKeywords(testWords, aliases, "CAT", autoStart: false);
    }

    void StartListening()
    {
        recognizer.StartListening();
        RefreshStatus();
    }

    void Simulate(string result)
    {
        UseFakeProvider();
        recognizer.StartListening();
        fakeProvider.Submit(result);
    }

    void SimulateTimeout()
    {
        UseFakeProvider();
        recognizer.StartListening();
        fakeProvider.Fail(SpeechRecognitionStatus.TimedOut, "Simulated timeout: no spell was heard.");
    }

    void HandleStateChanged(VoiceUnlockRecognizer.VoiceDisplayState state)
    {
        RefreshStatus();
        if (state == VoiceUnlockRecognizer.VoiceDisplayState.Error ||
            state == VoiceUnlockRecognizer.VoiceDisplayState.PermissionDenied ||
            state == VoiceUnlockRecognizer.VoiceDisplayState.Unavailable)
            resultLabel.text = recognizer.StatusMessage;
    }

    void HandleRecognized(string word)
    {
        resultLabel.text = $"Accepted spell: {word}\nRaw result: {recognizer.LastRecognizedText}";
        RefreshStatus();
    }

    void HandlePronunciationInsightReady(PronunciationInsightResult insight)
    {
        var sounds = new List<string>();
        if (insight.Segments != null)
        {
            foreach (PhoneticSoundSegment segment in insight.Segments)
            {
                string sound = string.IsNullOrWhiteSpace(segment.FriendlySound)
                    ? segment.Spelling
                    : segment.FriendlySound;
                string heard = string.IsNullOrWhiteSpace(segment.HeardSound) ? "" : $"<-{segment.HeardSound}";
                sounds.Add($"{sound}{heard}:{segment.Status}");
            }
        }

        string beats = insight.SyllableBeats != null && insight.SyllableBeats.Count > 0
            ? string.Join("-", insight.SyllableBeats)
            : insight.TargetWord;
        resultLabel.text =
            $"Accepted spell: {(insight.VoskConfirmedWord ? insight.ConfirmedWord : "no")}\n" +
            $"Raw result: {insight.RawRecognizedText}\n" +
            $"Sounds: {string.Join("  ", sounds)}\n" +
            $"Beats: {beats}    Hint: {insight.HintKey}";
        RefreshStatus();
    }

    void RefreshStatus()
    {
        if (recognizer == null || providerLabel == null) return;
        providerLabel.text = $"Provider: {recognizer.ProviderName}\nPhonetics: {recognizer.PronunciationInsightProviderName}\nAvailable: {recognizer.IsAvailable}    Requested language: {AccessibilitySettings.SpeechLanguage}";
        stateLabel.text = $"State: {recognizer.CurrentDisplayState}";
    }

    Button MakeButton(string label, UnityEngine.Events.UnityAction action, GameUiTheme.ButtonRole role)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(panel.transform, false);
        go.GetComponent<LayoutElement>().preferredHeight = 46f;
        var button = go.GetComponent<Button>();
        button.onClick.AddListener(action);
        GameUiTheme.StyleButton(button, role);
        var text = MakeLabel(label, 16f, GameUiTheme.Text, go.transform);
        text.fontStyle = FontStyles.Bold;
        return button;
    }

    TextMeshProUGUI MakeLabel(string text, float size, Color colour, Transform parent = null)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(parent != null ? parent : panel.transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var label = go.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.color = colour;
        label.alignment = TextAlignmentOptions.Center;
        label.verticalAlignment = VerticalAlignmentOptions.Middle;
        GameUiTheme.StyleText(label, size, parent != null);
        go.GetComponent<LayoutElement>().preferredHeight = size + 20f;
        return label;
    }

    void OnDestroy()
    {
        if (recognizer == null) return;
        recognizer.OnDisplayStateChanged -= HandleStateChanged;
        recognizer.OnKeywordRecognized -= HandleRecognized;
        recognizer.OnPronunciationInsightReady -= HandlePronunciationInsightReady;
    }
}
