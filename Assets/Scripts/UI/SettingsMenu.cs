using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class SettingsMenu : MonoBehaviour
{
    enum SettingsPage
    {
        General,
        Controls,
        Dev,
    }

    GameObject root;
    GameObject generalPage;
    GameObject controlsPage;
    GameObject devPage;
    TextMeshProUGUI resolutionLabel;
    TextMeshProUGUI microphoneLabel;
    TextMeshProUGUI micStatusLabel;
    TextMeshProUGUI volumeLabel;
    TextMeshProUGUI buddyLanguageLabel;
    TextMeshProUGUI devTravelRegionLabel;
    Toggle fullscreenToggle;
    Toggle touchControlsToggle;
    Toggle vibrationToggle;
    Toggle handwritingDevDiagnosticsToggle;
    Toggle buddyTransliterationToggle;
    Slider volumeSlider;
    Slider shakeSlider;
    AudioSource microphonePlayback;
    PlayerControls controls;
    InputActionRebindingExtensions.RebindingOperation rebindOperation;
    readonly List<Resolution> resolutions = new List<Resolution>();
    readonly List<Button> bindingButtons = new List<Button>();
    readonly List<InputAction> bindingActions = new List<InputAction>();
    readonly List<int> bindingIndices = new List<int>();
    int resolutionIndex;
    int microphoneIndex;
    int buddyLanguageIndex;
    int devTravelRegionIndex;
    bool recording;

    public bool IsOpen => root != null && root.activeSelf;

    public void Build(Transform parent, System.Action onClose)
    {
        if (root != null || parent == null)
            return;

        controls = new PlayerControls();
        GameSettings.ApplyBindingOverrides(controls.asset);
        microphonePlayback = gameObject.AddComponent<AudioSource>();
        microphonePlayback.playOnAwake = false;
        microphonePlayback.loop = false;
        BuildResolutionList();

        root = MakePanel("SettingsPanel", parent, new Vector2(720f, 700f));
        var rootLayout = root.GetComponent<VerticalLayoutGroup>();
        rootLayout.padding = new RectOffset(28, 28, 20, 20);
        rootLayout.spacing = 8f;

        MakeLabel(root.transform, "Settings", 25f, 36f);
        var tabs = MakeHorizontal(root.transform, 42f);
        MakeButton(tabs.transform, "General", () => ShowPage(SettingsPage.General), GameUiTheme.ButtonRole.Primary);
        MakeButton(tabs.transform, "Controls", () => ShowPage(SettingsPage.Controls), GameUiTheme.ButtonRole.Secondary);
        MakeButton(tabs.transform, "Dev", () => ShowPage(SettingsPage.Dev), GameUiTheme.ButtonRole.Secondary);

        generalPage = MakePage(root.transform);
        controlsPage = MakePage(root.transform);
        devPage = MakePage(root.transform);
        BuildGeneralPage();
        BuildControlsPage();
        BuildDevPage();

        MakeButton(root.transform, "Close", () =>
        {
            Close();
            onClose?.Invoke();
        }, GameUiTheme.ButtonRole.Quiet);

        ShowPage(SettingsPage.General);
        root.SetActive(false);
    }

    public void Open()
    {
        if (root == null)
            return;
        RefreshGeneralPage();
        RefreshBindings();
        RefreshDevPage();
        root.SetActive(true);
    }

    public void Close()
    {
        CancelRebind();
        StopMicrophoneRecording();
        microphonePlayback?.Stop();
        if (root != null)
            root.SetActive(false);
    }

    void BuildGeneralPage()
    {
        MakeLabel(generalPage.transform, "Display", 18f, 26f);
        resolutionLabel = MakeCycleRow(generalPage.transform, "Resolution", CycleResolution);
        fullscreenToggle = MakeToggle(generalPage.transform, "Fullscreen", Screen.fullScreen);
        fullscreenToggle.onValueChanged.AddListener(ApplyCurrentResolution);

        MakeLabel(generalPage.transform, "Audio", 18f, 26f);
        volumeLabel = MakeLabel(generalPage.transform, "", 14f, 24f);
        volumeSlider = MakeSlider(generalPage.transform, GameSettings.MasterVolume);
        volumeSlider.onValueChanged.AddListener(value =>
        {
            GameSettings.MasterVolume = value;
            volumeLabel.text = $"Master volume: {Mathf.RoundToInt(value * 100f)}%";
        });
        MakeLabel(generalPage.transform, "Output device: System default (change it in your operating system)", 12f, 28f);
        microphoneLabel = MakeCycleRow(generalPage.transform, "Microphone", CycleMicrophone);
        MakeLabel(generalPage.transform, "Selected microphone is used for this test. Speech recognition uses the operating system default.", 11f, 28f);
        MakeButton(generalPage.transform, "Record 3 Seconds & Listen Back", StartMicrophoneTest, GameUiTheme.ButtonRole.Secondary);
        micStatusLabel = MakeLabel(generalPage.transform, "Ready", 12f, 24f);

        MakeLabel(generalPage.transform, "Learning support", 18f, 26f);
        buddyLanguageLabel = MakeCycleRow(generalPage.transform, "Buddy language", CycleBuddyLanguage);
        CurriculumSessionManager curriculum = CurriculumSessionManager.EnsureExists();
        buddyTransliterationToggle = MakeToggle(generalPage.transform, "Use Roman-script support", curriculum.buddyAllowTransliteration);
        buddyTransliterationToggle.onValueChanged.AddListener(value => ApplyBuddyLanguage(value));

        MakeLabel(generalPage.transform, "Accessibility", 18f, 26f);
        vibrationToggle = MakeToggle(generalPage.transform, "Vibration", AccessibilitySettings.VibrationEnabled);
        vibrationToggle.onValueChanged.AddListener(value => AccessibilitySettings.VibrationEnabled = value);
        MakeLabel(generalPage.transform, "Screen shake intensity", 13f, 24f);
        shakeSlider = MakeSlider(generalPage.transform, AccessibilitySettings.ShakeIntensity);
        shakeSlider.onValueChanged.AddListener(value => AccessibilitySettings.ShakeIntensity = value);

    }

    void BuildControlsPage()
    {
        touchControlsToggle = MakeToggle(controlsPage.transform, "Touch controls", GameSettings.TouchControlsEnabled);
        touchControlsToggle.onValueChanged.AddListener(value => GameSettings.TouchControlsEnabled = value);
        MakeLabel(controlsPage.transform, "Select a control, then press a new key.", 14f, 30f);
        InputAction move = controls.Movement.Newaction;
        AddBindingButton("Move Forward", move, 1);
        AddBindingButton("Move Backward", move, 2);
        AddBindingButton("Move Left", move, 3);
        AddBindingButton("Move Right", move, 4);
        AddBindingButton("Jump", controls.Movement.Jump, 0);
        AddBindingButton("Forge / Draw", controls.Movement.ToggleDraw, 0);
        AddBindingButton("Attack / Speak", controls.Movement.Attack, 0);
        MakeButton(controlsPage.transform, "Reset Controls", ResetControls, GameUiTheme.ButtonRole.Danger);
    }

    void BuildDevPage()
    {
        MakeLabel(devPage.transform, "Developer", 18f, 26f);
        handwritingDevDiagnosticsToggle = MakeToggle(devPage.transform, "Handwriting diagnostics", GameSettings.HandwritingDevDiagnosticsVisible);
        handwritingDevDiagnosticsToggle.onValueChanged.AddListener(value => GameSettings.HandwritingDevDiagnosticsVisible = value);
        MakeLabel(devPage.transform, "Shows hidden handwriting guides, green zones, markers, and ruled-line diagnostics.", 11f, 32f);

        MakeLabel(devPage.transform, "World travel", 18f, 30f);
        devTravelRegionIndex = ResolveCurrentRegionIndex();
        devTravelRegionLabel = MakeCycleRow(devPage.transform, "Region", CycleDevTravelRegion);

        var row = MakeHorizontal(devPage.transform, 42f);
        MakeButton(row.transform, "Town", () => TravelToSelectedRegion(SemanticZoneKind.Town), GameUiTheme.ButtonRole.Primary);
        MakeButton(row.transform, "Route", () => TravelToSelectedRegion(SemanticZoneKind.Route), GameUiTheme.ButtonRole.Secondary);
        MakeButton(row.transform, "Gym", () => TravelToSelectedRegion(SemanticZoneKind.Gym), GameUiTheme.ButtonRole.Secondary);
        MakeLabel(devPage.transform, "Loads the selected runtime area in the current save slot without marking it complete.", 11f, 34f);
    }

    void AddBindingButton(string label, InputAction action, int bindingIndex)
    {
        Button button = MakeButton(controlsPage.transform, "", () => BeginRebind(action, bindingIndex), GameUiTheme.ButtonRole.Secondary);
        button.name = label;
        bindingButtons.Add(button);
        bindingActions.Add(action);
        bindingIndices.Add(bindingIndex);
    }

    void BeginRebind(InputAction action, int bindingIndex)
    {
        CancelRebind();
        controls.Disable();
        Button button = FindBindingButton(action, bindingIndex);
        SetButtonText(button, "Press a key... (Esc cancels)");
        rebindOperation = action.PerformInteractiveRebinding(bindingIndex)
            .WithControlsExcluding("<Mouse>/position")
            .WithControlsExcluding("<Mouse>/delta")
            .WithCancelingThrough("<Keyboard>/escape")
            .OnCancel(_ => FinishRebind(false))
            .OnComplete(_ => FinishRebind(true))
            .Start();
    }

    void FinishRebind(bool save)
    {
        if (save)
            GameSettings.SaveBindingOverrides(controls.asset);
        rebindOperation?.Dispose();
        rebindOperation = null;
        controls.Enable();
        RefreshBindings();
    }

    void CancelRebind()
    {
        if (rebindOperation == null)
            return;
        var operation = rebindOperation;
        rebindOperation = null;
        operation.Cancel();
        operation.Dispose();
    }

    void ResetControls()
    {
        GameSettings.ResetBindingOverrides(controls.asset);
        RefreshBindings();
    }

    void RefreshBindings()
    {
        if (touchControlsToggle != null)
            touchControlsToggle.SetIsOnWithoutNotify(GameSettings.TouchControlsEnabled);

        for (int i = 0; i < bindingButtons.Count; i++)
        {
            Button button = bindingButtons[i];
            InputAction action = bindingActions[i];
            int index = bindingIndices[i];
            SetButtonText(button, $"{button.name}: {action.GetBindingDisplayString(index)}");
        }
    }

    Button FindBindingButton(InputAction action, int bindingIndex)
    {
        foreach (Button button in bindingButtons)
        {
            int index = bindingButtons.IndexOf(button);
            bool matchesAction = bindingActions[index] == action;
            if (matchesAction && bindingIndices[index] == bindingIndex)
                return button;
        }
        return null;
    }

    void BuildResolutionList()
    {
        var seen = new HashSet<string>();
        foreach (Resolution resolution in Screen.resolutions)
        {
            string key = $"{resolution.width}x{resolution.height}";
            if (seen.Add(key))
                resolutions.Add(resolution);
        }
        if (resolutions.Count == 0)
            resolutions.Add(new Resolution { width = Screen.width, height = Screen.height });

        resolutionIndex = resolutions.FindIndex(r => r.width == Screen.width && r.height == Screen.height);
        if (resolutionIndex < 0)
            resolutionIndex = resolutions.Count - 1;
    }

    void CycleResolution()
    {
        resolutionIndex = (resolutionIndex + 1) % resolutions.Count;
        ApplyCurrentResolution(fullscreenToggle != null && fullscreenToggle.isOn);
    }

    void ApplyCurrentResolution(bool fullscreen)
    {
        GameSettings.SetResolution(resolutions[resolutionIndex], fullscreen);
        RefreshGeneralPage();
    }

    void CycleMicrophone()
    {
        string[] devices = Microphone.devices;
        if (devices.Length == 0)
        {
            microphoneIndex = -1;
            GameSettings.SelectedMicrophone = "";
        }
        else
        {
            microphoneIndex = (microphoneIndex + 1) % devices.Length;
            GameSettings.SelectedMicrophone = devices[microphoneIndex];
        }
        RefreshGeneralPage();
    }

    void CycleBuddyLanguage()
    {
        buddyLanguageIndex = (buddyLanguageIndex + 1) % BuddyLanguageCatalog.Codes.Length;
        ApplyBuddyLanguage(buddyTransliterationToggle != null && buddyTransliterationToggle.isOn);
    }

    void ApplyBuddyLanguage(bool allowTransliteration)
    {
        CurriculumSessionManager curriculum = CurriculumSessionManager.EnsureExists();
        string code = BuddyLanguageCatalog.Codes[Mathf.Clamp(buddyLanguageIndex, 0, BuddyLanguageCatalog.Codes.Length - 1)];
        curriculum.ConfigureBuddyLearningPreferences(
            code,
            curriculum.buddyTargetLanguage,
            allowTransliteration,
            curriculum.buddyLearningMemoryEnabled,
            curriculum.buddyExplanationStyle);
        RefreshGeneralPage();
    }

    void StartMicrophoneTest()
    {
        if (recording)
            return;
        string device = CurrentMicrophone();
        if (string.IsNullOrEmpty(device))
        {
            micStatusLabel.text = "No microphone detected.";
            return;
        }
        StartCoroutine(RecordAndPlayBack(device));
    }

    IEnumerator RecordAndPlayBack(string device)
    {
        recording = true;
        microphonePlayback.Stop();
        micStatusLabel.text = "Recording... speak now.";
        AudioClip clip = Microphone.Start(device, false, 3, 44100);
        if (clip == null)
        {
            micStatusLabel.text = "Could not start the microphone. Check permission.";
            recording = false;
            yield break;
        }
        yield return new WaitForSecondsRealtime(3.1f);
        Microphone.End(device);
        microphonePlayback.clip = clip;
        microphonePlayback.Play();
        micStatusLabel.text = "Playing your recording through the system default output.";
        recording = false;
    }

    void StopMicrophoneRecording()
    {
        if (!recording)
            return;
        string device = CurrentMicrophone();
        if (!string.IsNullOrEmpty(device) && Microphone.IsRecording(device))
            Microphone.End(device);
        StopAllCoroutines();
        recording = false;
    }

    string CurrentMicrophone()
    {
        string[] devices = Microphone.devices;
        if (devices.Length == 0)
            return "";
        if (microphoneIndex < 0 || microphoneIndex >= devices.Length)
            microphoneIndex = 0;
        return devices[microphoneIndex];
    }

    void RefreshGeneralPage()
    {
        Resolution resolution = resolutions[Mathf.Clamp(resolutionIndex, 0, resolutions.Count - 1)];
        if (resolutionLabel != null)
            resolutionLabel.text = $"Resolution: {resolution.width} x {resolution.height}";
        if (fullscreenToggle != null)
            fullscreenToggle.SetIsOnWithoutNotify(Screen.fullScreen);
        if (volumeSlider != null)
            volumeSlider.SetValueWithoutNotify(GameSettings.MasterVolume);
        if (volumeLabel != null)
            volumeLabel.text = $"Master volume: {Mathf.RoundToInt(GameSettings.MasterVolume * 100f)}%";

        string[] devices = Microphone.devices;
        string saved = GameSettings.SelectedMicrophone;
        microphoneIndex = System.Array.IndexOf(devices, saved);
        if (microphoneIndex < 0 && devices.Length > 0)
            microphoneIndex = 0;
        if (microphoneLabel != null)
            microphoneLabel.text = devices.Length == 0 ? "Microphone: None detected" : $"Microphone: {devices[microphoneIndex]}";
        CurriculumSessionManager curriculum = CurriculumSessionManager.EnsureExists();
        buddyLanguageIndex = BuddyLanguageCatalog.IndexOf(curriculum.buddyHomeLanguage);
        if (buddyLanguageLabel != null)
            buddyLanguageLabel.text = $"Buddy language: {BuddyLanguageCatalog.Names[buddyLanguageIndex]}";
        if (buddyTransliterationToggle != null)
            buddyTransliterationToggle.SetIsOnWithoutNotify(curriculum.buddyAllowTransliteration);
    }

    void RefreshDevPage()
    {
        if (handwritingDevDiagnosticsToggle != null)
            handwritingDevDiagnosticsToggle.SetIsOnWithoutNotify(GameSettings.HandwritingDevDiagnosticsVisible);

        IReadOnlyList<NaturalGrammarRegion> regions = NaturalGrammarProgression.Regions;
        if (regions.Count == 0)
        {
            if (devTravelRegionLabel != null)
                devTravelRegionLabel.text = "Region: None";
            return;
        }

        devTravelRegionIndex = Mathf.Clamp(devTravelRegionIndex, 0, regions.Count - 1);
        NaturalGrammarRegion region = regions[devTravelRegionIndex];
        if (devTravelRegionLabel != null)
            devTravelRegionLabel.text = $"Region: {devTravelRegionIndex + 1}. {region.displayName}";
    }

    void CycleDevTravelRegion()
    {
        IReadOnlyList<NaturalGrammarRegion> regions = NaturalGrammarProgression.Regions;
        if (regions.Count == 0)
            return;

        devTravelRegionIndex = (devTravelRegionIndex + 1) % regions.Count;
        RefreshDevPage();
    }

    void TravelToSelectedRegion(SemanticZoneKind kind)
    {
        IReadOnlyList<NaturalGrammarRegion> regions = NaturalGrammarProgression.Regions;
        if (regions.Count == 0)
            return;

        devTravelRegionIndex = Mathf.Clamp(devTravelRegionIndex, 0, regions.Count - 1);
        NaturalGrammarRegion region = regions[devTravelRegionIndex];
        if (region == null)
            return;

        Close();
        WorldSessionManager.EnsureExists().TravelToAreaForDevelopment(kind, region);
    }

    int ResolveCurrentRegionIndex()
    {
        GrammarWorldProgressService progress = GrammarWorldProgressService.Instance;
        GrammarMapAreaState current = progress != null && progress.Data != null
            ? progress.Data.areas.Find(area =>
                area != null &&
                string.Equals(area.areaId, progress.Data.currentAreaId, System.StringComparison.OrdinalIgnoreCase))
            : null;
        if (current == null)
            return 0;

        IReadOnlyList<NaturalGrammarRegion> regions = NaturalGrammarProgression.Regions;
        for (int i = 0; i < regions.Count; i++)
        {
            NaturalGrammarRegion region = regions[i];
            if (region != null &&
                string.Equals(region.grammarTopic, current.grammarTopic, System.StringComparison.OrdinalIgnoreCase) &&
                region.tier == current.grammarTopicTier)
            {
                return i;
            }
        }

        return 0;
    }

    void ShowPage(SettingsPage page)
    {
        generalPage.SetActive(page == SettingsPage.General);
        controlsPage.SetActive(page == SettingsPage.Controls);
        devPage.SetActive(page == SettingsPage.Dev);
    }

    static GameObject MakePanel(string name, Transform parent, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        GameUiTheme.StylePanel(go);
        var layout = go.GetComponent<VerticalLayoutGroup>();
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        return go;
    }

    static GameObject MakePage(Transform parent)
    {
        var go = new GameObject("Page", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = 500f;
        go.GetComponent<LayoutElement>().flexibleHeight = 1f;
        var layout = go.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 5f;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        return go;
    }

    static GameObject MakeHorizontal(Transform parent, float height)
    {
        var go = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = height;
        var layout = go.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        return go;
    }

    static TextMeshProUGUI MakeCycleRow(Transform parent, string prefix, UnityEngine.Events.UnityAction action)
    {
        Button button = MakeButton(parent, prefix, action, GameUiTheme.ButtonRole.Secondary);
        return button.GetComponentInChildren<TextMeshProUGUI>();
    }

    static Button MakeButton(Transform parent, string label, UnityEngine.Events.UnityAction action, GameUiTheme.ButtonRole role)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = 36f;
        var button = go.GetComponent<Button>();
        button.onClick.AddListener(action);
        GameUiTheme.StyleButton(button, role);
        MakeLabel(go.transform, label, 13f, 36f);
        return button;
    }

    static void SetButtonText(Button button, string value)
    {
        if (button != null)
            button.GetComponentInChildren<TextMeshProUGUI>().text = value;
    }

    static TextMeshProUGUI MakeLabel(Transform parent, string value, float size, float height)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = height;
        var label = go.GetComponent<TextMeshProUGUI>();
        label.text = value;
        label.alignment = TextAlignmentOptions.Center;
        label.verticalAlignment = VerticalAlignmentOptions.Middle;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.enableAutoSizing = true;
        label.fontSizeMin = Mathf.Max(8f, size - 4f);
        label.fontSizeMax = size;
        GameUiTheme.StyleText(label, size, parent.GetComponent<Button>() != null);
        return label;
    }

    static Toggle MakeToggle(Transform parent, string label, bool value)
    {
        var row = MakeHorizontal(parent, 32f);
        MakeLabel(row.transform, label, 13f, 32f).alignment = TextAlignmentOptions.Left;
        var go = new GameObject("Toggle", typeof(RectTransform), typeof(Image), typeof(Toggle), typeof(LayoutElement));
        go.transform.SetParent(row.transform, false);
        go.GetComponent<LayoutElement>().preferredWidth = 64f;
        var image = go.GetComponent<Image>();
        image.color = GameUiTheme.PanelRaised;
        var check = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
        check.transform.SetParent(go.transform, false);
        var rt = check.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.2f, 0.2f);
        rt.anchorMax = new Vector2(0.8f, 0.8f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        check.GetComponent<Image>().color = GameUiTheme.Accent;
        var toggle = go.GetComponent<Toggle>();
        toggle.targetGraphic = image;
        toggle.graphic = check.GetComponent<Image>();
        toggle.isOn = value;
        return toggle;
    }

    static Slider MakeSlider(Transform parent, float value)
    {
        var go = new GameObject("Slider", typeof(RectTransform), typeof(Slider), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = 24f;
        var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
        background.transform.SetParent(go.transform, false);
        var bgRt = background.GetComponent<RectTransform>();
        bgRt.anchorMin = new Vector2(0f, 0.3f);
        bgRt.anchorMax = new Vector2(1f, 0.7f);
        bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;
        background.GetComponent<Image>().color = GameUiTheme.PanelRaised;
        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(background.transform, false);
        var fillRt = fill.GetComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = fillRt.offsetMax = Vector2.zero;
        fill.GetComponent<Image>().color = GameUiTheme.Accent;
        var slider = go.GetComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = value;
        slider.fillRect = fillRt;
        slider.targetGraphic = fill.GetComponent<Image>();
        return slider;
    }

    void OnDestroy()
    {
        CancelRebind();
        controls?.Dispose();
    }
}
