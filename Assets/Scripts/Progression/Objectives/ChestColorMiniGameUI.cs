using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class ChestColorMiniGameUI : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    static ChestColorMiniGameUI instance;

    Canvas canvas;
    RectTransform viewRoot;
    RectTransform root;
    RectTransform colorBox;
    RectTransform buttonGrid;
    TextMeshProUGUI titleLabel;
    TextMeshProUGUI statusLabel;
    TextMeshProUGUI resultLabel;
    Button hintButton;
    Button continueButton;
    Button speakButton;
    VoiceUnlockRecognizer recognizer;
    PronunciationSpeaker speaker;
    TreasureChestReward activeChest;
    readonly List<GameObject> colorVisuals = new List<GameObject>();
    readonly List<Button> colorButtons = new List<Button>();

    ColorWordDefinition targetColor;
    string selectedColor = "";
    string spokenColor = "";
    int rewardAwarded;
    bool colorCorrect;
    bool speechProofSucceeded;
    bool speechResolved;
    bool hintUsed;
    bool pauseLockHeld;
    bool speechHoldActive;
    float openedAt;
    float speechStartedAt = -1f;

    public static ChestColorMiniGameUI EnsureExists()
    {
        if (instance != null)
            return instance;

        ChestColorMiniGameUI existing = FindAnyObjectByType<ChestColorMiniGameUI>();
        if (existing != null)
        {
            instance = existing;
            return instance;
        }

        var go = new GameObject("ChestColorMiniGameUI");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<ChestColorMiniGameUI>();
        return instance;
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        speaker = PronunciationSpeaker.EnsureExists();
        Build();
        SetOpen(false);
    }

    void OnDestroy()
    {
        if (recognizer != null)
            recognizer.OnRecognitionResolved -= HandleRecognitionResolved;
        if (pauseLockHeld)
        {
            PauseMenuController.EndModalPause();
            pauseLockHeld = false;
        }
        if (instance == this)
            instance = null;
    }

    public void Open(TreasureChestReward chest)
    {
        if (chest == null || ChestMiniGameState.IsOpen || !PauseMenuController.CanOpenBlockingModal)
            return;

        EnsureBuilt();
        activeChest = chest;
        targetColor = ColorWordUtility.RandomColor();
        selectedColor = "";
        spokenColor = "";
        rewardAwarded = 0;
        colorCorrect = false;
        speechProofSucceeded = false;
        speechResolved = false;
        hintUsed = false;
        openedAt = Time.unscaledTime;
        speechStartedAt = -1f;
        speechHoldActive = false;

        titleLabel.text = "Name the Color";
        statusLabel.text = "Which color is glowing?";
        resultLabel.text = "";
        hintButton.gameObject.SetActive(false);
        continueButton.gameObject.SetActive(false);
        speakButton.gameObject.SetActive(false);
        SetColorButtonsInteractable(true);
        BuildColorVisuals(targetColor);
        SetOpen(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void HandleColorSelected(string colorName)
    {
        selectedColor = ColorWordUtility.Normalize(colorName);
        colorCorrect = string.Equals(selectedColor, targetColor.Name, System.StringComparison.OrdinalIgnoreCase);
        rewardAwarded = colorCorrect
            ? Mathf.Clamp(activeChest != null ? activeChest.coinCount : 3, 1, 10)
            : Mathf.CeilToInt(Mathf.Clamp(activeChest != null ? activeChest.coinCount : 3, 1, 10) * 0.5f);
        SetColorButtonsInteractable(false);
        resultLabel.text = colorCorrect
            ? "Good eye. Now say the color."
            : $"That was {targetColor.Name}. Now say {selectedColor}.";
        BeginSpeechProof();
    }

    void BeginSpeechProof()
    {
        EnsureRecognizer();
        statusLabel.text = $"Hold Speak, then say: {selectedColor}";
        speechStartedAt = -1f;
        if (recognizer == null || !recognizer.IsAvailable)
        {
            ResolveSpeech(false, "");
            return;
        }

        recognizer.ConfigureKeywords(
            ColorWordUtility.BuildKeywords(),
            ColorWordUtility.BuildAliases(),
            selectedColor,
            false);
        speakButton.gameObject.SetActive(true);
        speakButton.interactable = true;
    }

    void BeginSpeechHold()
    {
        if (speechResolved || speechHoldActive || recognizer == null || !recognizer.IsAvailable)
            return;

        speechHoldActive = true;
        speechStartedAt = Time.unscaledTime;
        statusLabel.text = "Listening... release when you finish.";
        recognizer.StartListening(VoiceUnlockRecognizer.VoiceInputMode.Manual);
    }

    void EndSpeechHold()
    {
        if (!speechHoldActive)
            return;

        speechHoldActive = false;
        statusLabel.text = "Processing your response...";
        recognizer?.FinishListeningAttempt();
    }

    void Update()
    {
        if (!IsOpen || string.IsNullOrEmpty(selectedColor) || speechResolved || recognizer == null)
            return;

        if (recognizer.CurrentDisplayState == VoiceUnlockRecognizer.VoiceDisplayState.PermissionDenied ||
            recognizer.CurrentDisplayState == VoiceUnlockRecognizer.VoiceDisplayState.Unavailable ||
            recognizer.CurrentDisplayState == VoiceUnlockRecognizer.VoiceDisplayState.Error ||
            speechStartedAt >= 0f && Time.unscaledTime - speechStartedAt > 10.5f)
        {
            ResolveSpeech(false, recognizer.LastRecognizedText);
        }
    }

    void HandleRecognitionResolved(VoiceUnlockRecognizer.RecognitionEvent result)
    {
        if (!IsOpen || speechResolved || string.IsNullOrEmpty(selectedColor))
            return;

        bool recognized = result.Recognized &&
                          string.Equals(ColorWordUtility.Normalize(result.Text), selectedColor, System.StringComparison.OrdinalIgnoreCase);
        ResolveSpeech(recognized, string.IsNullOrWhiteSpace(result.RawText) ? result.Text : result.RawText);
    }

    void ResolveSpeech(bool success, string rawSpeech)
    {
        speechResolved = true;
        speechHoldActive = false;
        if (speakButton != null)
            speakButton.gameObject.SetActive(false);
        speechProofSucceeded = success;
        spokenColor = rawSpeech ?? "";

        WorldEconomyService.EnsureExists().AddCoins(rewardAwarded);

        if (colorCorrect && !speechProofSucceeded)
        {
            statusLabel.text = "Listen once, then try the color next time.";
            resultLabel.text = $"You earned {rewardAwarded} coins. Speech proof needs practice.";
            hintButton.gameObject.SetActive(true);
            continueButton.gameObject.SetActive(true);
            return;
        }

        statusLabel.text = speechProofSucceeded ? "Speech proof accepted." : "Speech proof needs practice.";
        resultLabel.text = colorCorrect
            ? $"You earned {rewardAwarded} coins."
            : $"Color guess was off. You earned {rewardAwarded} coins.";
        continueButton.gameObject.SetActive(true);
    }

    void HandleHintPressed()
    {
        if (string.IsNullOrEmpty(selectedColor))
            return;

        hintUsed = true;
        speaker ??= PronunciationSpeaker.EnsureExists();
        speaker.Speak(selectedColor, null);
    }

    void HandleContinuePressed()
    {
        RecordAttempt();
        Close();
    }

    void RecordAttempt()
    {
        if (string.IsNullOrEmpty(selectedColor))
            return;

        CurriculumSessionManager.Instance?.RecordColorMiniGameAttempt(
            activeChest != null ? activeChest.chestCategory : "RuntimeChest",
            targetColor.Name,
            selectedColor,
            spokenColor,
            colorCorrect,
            speechProofSucceeded,
            hintUsed,
            rewardAwarded,
            Time.unscaledTime - openedAt,
            BuildOutcomeStatus(),
            GetLastPronunciationInsight(),
            GetLastPronunciationAudio());
    }

    PronunciationInsightResult? GetLastPronunciationInsight()
    {
        if (recognizer == null)
            return null;

        PronunciationInsightResult insight = recognizer.LastPronunciationInsight;
        bool hasInsight = !string.IsNullOrWhiteSpace(insight.TargetWord) ||
                          !string.IsNullOrWhiteSpace(insight.RawRecognizedText) ||
                          (insight.Segments != null && insight.Segments.Count > 0);
        return hasInsight ? insight : null;
    }

    byte[] GetLastPronunciationAudio()
    {
        return recognizer != null
            ? recognizer.GetLastCapturedPronunciationWav()
            : System.Array.Empty<byte>();
    }

    string BuildOutcomeStatus()
    {
        if (string.IsNullOrWhiteSpace(selectedColor))
            return "seen_ignored";
        if (!colorCorrect)
            return "opened_wrong_answer";
        if (!speechProofSucceeded)
            return "opened_correct_pronunciation_failed";
        return "opened_correct";
    }

    void Close()
    {
        recognizer?.StopListening();
        ClearColorVisuals();
        activeChest = null;
        SetOpen(false);
        if (!TemplateRecorderUI.IsOpen && !GrimoireUI.IsOpen && !RunEndScreenController.IsOpen && !PauseMenuController.IsPaused)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void SetOpen(bool open)
    {
        if (IsOpen == open)
        {
            if (viewRoot != null)
                viewRoot.gameObject.SetActive(open);
            return;
        }

        if (open)
        {
            if (!PauseMenuController.TryBeginModalPause())
                return;
            pauseLockHeld = true;
        }
        else if (pauseLockHeld)
        {
            PauseMenuController.EndModalPause();
            pauseLockHeld = false;
        }

        IsOpen = open;
        if (viewRoot != null)
            viewRoot.gameObject.SetActive(open);
    }

    void SetColorButtonsInteractable(bool interactable)
    {
        foreach (Button button in colorButtons)
            if (button != null)
                button.interactable = interactable;
    }

    void EnsureRecognizer()
    {
        if (recognizer == null)
        {
            recognizer = gameObject.GetComponent<VoiceUnlockRecognizer>();
            if (recognizer == null)
                recognizer = gameObject.AddComponent<VoiceUnlockRecognizer>();
            recognizer.OnRecognitionResolved += HandleRecognitionResolved;
        }
    }

    void EnsureBuilt()
    {
        if (canvas == null || root == null)
            Build();
    }

    void Build()
    {
        if (canvas != null)
            return;

        var canvasGo = new GameObject("ChestColorCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        viewRoot = canvasGo.GetComponent<RectTransform>();
        canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 650;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        var backdrop = new GameObject("Backdrop", typeof(RectTransform), typeof(Image));
        backdrop.transform.SetParent(canvasGo.transform, false);
        RectTransform backdropRt = backdrop.GetComponent<RectTransform>();
        backdropRt.anchorMin = Vector2.zero;
        backdropRt.anchorMax = Vector2.one;
        backdropRt.offsetMin = Vector2.zero;
        backdropRt.offsetMax = Vector2.zero;
        backdrop.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.42f);

        var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(Outline));
        panelGo.transform.SetParent(canvasGo.transform, false);
        root = panelGo.GetComponent<RectTransform>();
        root.anchorMin = new Vector2(0.5f, 0.5f);
        root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0.5f);
        root.sizeDelta = new Vector2(960f, 760f);
        root.anchoredPosition = Vector2.zero;
        GameUiTheme.StylePanel(panelGo, false);

        titleLabel = MakeText(root, "Title", 40f, FontStyles.Bold, GameUiTheme.Gold, TextAlignmentOptions.Center);
        titleLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
        titleLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
        titleLabel.rectTransform.offsetMin = new Vector2(40f, -92f);
        titleLabel.rectTransform.offsetMax = new Vector2(-40f, -24f);

        statusLabel = MakeText(root, "Status", 24f, FontStyles.Bold, GameUiTheme.Text, TextAlignmentOptions.Center);
        statusLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
        statusLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
        statusLabel.rectTransform.offsetMin = new Vector2(42f, -140f);
        statusLabel.rectTransform.offsetMax = new Vector2(-42f, -94f);

        var boxGo = new GameObject("ColorBox", typeof(RectTransform), typeof(Image), typeof(Outline));
        boxGo.transform.SetParent(root, false);
        colorBox = boxGo.GetComponent<RectTransform>();
        colorBox.anchorMin = new Vector2(0.5f, 1f);
        colorBox.anchorMax = new Vector2(0.5f, 1f);
        colorBox.pivot = new Vector2(0.5f, 1f);
        colorBox.anchoredPosition = new Vector2(0f, -160f);
        colorBox.sizeDelta = new Vector2(720f, 255f);
        Image boxImage = boxGo.GetComponent<Image>();
        boxImage.color = new Color(0.06f, 0.10f, 0.16f, 0.58f);
        Outline boxOutline = boxGo.GetComponent<Outline>();
        boxOutline.effectColor = new Color(GameUiTheme.Gold.r, GameUiTheme.Gold.g, GameUiTheme.Gold.b, 0.34f);
        boxOutline.effectDistance = new Vector2(2f, -2f);

        resultLabel = MakeText(root, "Result", 22f, FontStyles.Bold, GameUiTheme.Text, TextAlignmentOptions.Center);
        resultLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
        resultLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
        resultLabel.rectTransform.offsetMin = new Vector2(42f, -448f);
        resultLabel.rectTransform.offsetMax = new Vector2(-42f, -398f);

        var gridGo = new GameObject("ColorButtons", typeof(RectTransform), typeof(GridLayoutGroup));
        gridGo.transform.SetParent(root, false);
        buttonGrid = gridGo.GetComponent<RectTransform>();
        buttonGrid.anchorMin = new Vector2(0.5f, 0f);
        buttonGrid.anchorMax = new Vector2(0.5f, 0f);
        buttonGrid.pivot = new Vector2(0.5f, 0f);
        buttonGrid.anchoredPosition = new Vector2(0f, 118f);
        buttonGrid.sizeDelta = new Vector2(620f, 150f);
        GridLayoutGroup grid = gridGo.GetComponent<GridLayoutGroup>();
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        grid.cellSize = new Vector2(176f, 62f);
        grid.spacing = new Vector2(18f, 18f);
        grid.childAlignment = TextAnchor.MiddleCenter;

        foreach (ColorWordDefinition color in ColorWordUtility.All)
            colorButtons.Add(MakeColorButton(buttonGrid, color));

        hintButton = MakeCommandButton(root, "Hint", new Vector2(-150f, 40f), new Vector2(240f, 62f), GameUiTheme.ButtonRole.Secondary);
        hintButton.onClick.AddListener(HandleHintPressed);

        continueButton = MakeCommandButton(root, "Continue", new Vector2(150f, 40f), new Vector2(240f, 62f), GameUiTheme.ButtonRole.Primary);
        continueButton.onClick.AddListener(HandleContinuePressed);

        speakButton = MakeCommandButton(root, "Hold Speak", new Vector2(0f, 112f), new Vector2(260f, 62f), GameUiTheme.ButtonRole.Primary);
        AddHoldToTalkHandlers(speakButton);
        speakButton.gameObject.SetActive(false);
    }

    Button MakeColorButton(Transform parent, ColorWordDefinition color)
    {
        Button button = MakeButton(parent, System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(color.Name), GameUiTheme.ButtonRole.Secondary);
        string capturedName = color.Name;
        button.onClick.AddListener(() => HandleColorSelected(capturedName));
        return button;
    }

    Button MakeCommandButton(Transform parent, string text, Vector2 anchoredPosition, Vector2 size, GameUiTheme.ButtonRole role)
    {
        Button button = MakeButton(parent, text, role);
        RectTransform rt = button.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = size;
        return button;
    }

    Button MakeButton(Transform parent, string text, GameUiTheme.ButtonRole role)
    {
        var go = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        Button button = go.GetComponent<Button>();
        GameUiTheme.StyleButton(button, role);

        TextMeshProUGUI label = MakeText(go.GetComponent<RectTransform>(), "Label", 22f, FontStyles.Bold, GameUiTheme.Text, TextAlignmentOptions.Center);
        label.text = text;
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = Vector2.zero;
        label.rectTransform.offsetMax = Vector2.zero;
        label.verticalAlignment = VerticalAlignmentOptions.Middle;
        return button;
    }

    void AddHoldToTalkHandlers(Button button)
    {
        EventTrigger trigger = button.gameObject.AddComponent<EventTrigger>();
        var down = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
        down.callback.AddListener(_ => BeginSpeechHold());
        trigger.triggers.Add(down);
        var up = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        up.callback.AddListener(_ => EndSpeechHold());
        trigger.triggers.Add(up);
        var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(_ => EndSpeechHold());
        trigger.triggers.Add(exit);
    }

    TextMeshProUGUI MakeText(Transform parent, string name, float size, FontStyles style, Color color, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        text.text = "";
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        GameUiTheme.StyleText(text, size);
        return text;
    }

    void BuildColorVisuals(ColorWordDefinition color)
    {
        ClearColorVisuals();
        for (int i = 0; i < 5; i++)
            CreateColorToken(i, color);
    }

    void CreateColorToken(int index, ColorWordDefinition color)
    {
        var go = new GameObject($"ColorToken_{index + 1}", typeof(RectTransform), typeof(Image), typeof(Outline));
        go.transform.SetParent(colorBox, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = index == 0 ? new Vector2(104f, 104f) : new Vector2(64f, 64f);

        float angle = index * 1.256637f;
        float radius = index == 0 ? 0f : 112f;
        rt.anchoredPosition = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius * 0.55f);
        rt.localRotation = Quaternion.Euler(0f, 0f, Random.Range(-18f, 18f));

        Image image = go.GetComponent<Image>();
        image.color = color.Color;
        Outline outline = go.GetComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.58f);
        outline.effectDistance = new Vector2(3f, -3f);
        colorVisuals.Add(go);
    }

    void ClearColorVisuals()
    {
        foreach (GameObject visual in colorVisuals)
            if (visual != null)
                Destroy(visual);
        colorVisuals.Clear();
    }
}
