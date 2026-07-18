using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class ChestCountingMiniGameUI : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    static ChestCountingMiniGameUI instance;

    Canvas canvas;
    RectTransform viewRoot;
    RectTransform root;
    RectTransform coinBox;
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
    readonly List<GameObject> coinVisuals = new List<GameObject>();
    readonly List<GameObject> worldCoinVisuals = new List<GameObject>();
    readonly List<Button> numberButtons = new List<Button>();

    int targetCount;
    int selectedCount;
    int rewardAwarded;
    bool countCorrect;
    bool speechProofSucceeded;
    bool speechResolved;
    bool hintUsed;
    bool pauseLockHeld;
    bool speechHoldActive;
    string spokenNumber = "";
    float openedAt;
    float speechStartedAt = -1f;
    Transform worldCoinRoot;

    public static ChestCountingMiniGameUI EnsureExists()
    {
        if (instance != null)
            return instance;

        ChestCountingMiniGameUI existing = FindAnyObjectByType<ChestCountingMiniGameUI>();
        if (existing != null)
        {
            instance = existing;
            return instance;
        }

        var go = new GameObject("ChestCountingMiniGameUI");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<ChestCountingMiniGameUI>();
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
        targetCount = Mathf.Clamp(chest.coinCount, 1, 10);
        selectedCount = 0;
        rewardAwarded = 0;
        countCorrect = false;
        speechProofSucceeded = false;
        speechResolved = false;
        hintUsed = false;
        spokenNumber = "";
        openedAt = Time.unscaledTime;
        speechStartedAt = -1f;
        speechHoldActive = false;

        titleLabel.text = "Count the Coins";
        statusLabel.text = "How many coins are in the chest?";
        resultLabel.text = "";
        hintButton.gameObject.SetActive(false);
        continueButton.gameObject.SetActive(false);
        speakButton.gameObject.SetActive(false);
        SetNumberButtonsInteractable(true);
        BuildCoinVisuals(targetCount, chest.visualCoinPrefab);
        SetOpen(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void HandleNumberSelected(int number)
    {
        selectedCount = Mathf.Clamp(number, 1, 10);
        countCorrect = selectedCount == targetCount;
        rewardAwarded = countCorrect ? targetCount : Mathf.CeilToInt(targetCount * 0.5f);
        SetNumberButtonsInteractable(false);
        resultLabel.text = countCorrect
            ? "Good counting. Now say the number."
            : $"There were {targetCount}. Now say {CountingNumberUtility.ToWord(selectedCount)}.";
        BeginSpeechProof();
    }

    void BeginSpeechProof()
    {
        EnsureRecognizer();
        string selectedWord = CountingNumberUtility.ToWord(selectedCount);
        statusLabel.text = $"Hold Speak, then say: {selectedWord}";
        speechStartedAt = -1f;
        if (recognizer == null || !recognizer.IsAvailable)
        {
            ResolveSpeech(false, "");
            return;
        }

        recognizer.ConfigureKeywords(
            CountingNumberUtility.BuildKeywords(),
            CountingNumberUtility.BuildAliases(),
            selectedWord,
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
        if (!IsOpen || selectedCount <= 0 || speechResolved || recognizer == null)
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
        if (!IsOpen || speechResolved || selectedCount <= 0)
            return;

        bool recognized = result.Recognized &&
                          CountingNumberUtility.TryParse(result.Text, out int spoken) &&
                          spoken == selectedCount;
        ResolveSpeech(recognized, string.IsNullOrWhiteSpace(result.RawText) ? result.Text : result.RawText);
    }

    void ResolveSpeech(bool success, string rawSpeech)
    {
        speechResolved = true;
        speechHoldActive = false;
        if (speakButton != null)
            speakButton.gameObject.SetActive(false);
        speechProofSucceeded = success;
        spokenNumber = rawSpeech ?? "";

        WorldEconomyService.EnsureExists().AddCoins(rewardAwarded);

        if (countCorrect && !speechProofSucceeded)
        {
            statusLabel.text = "Listen once, then try the number next time.";
            resultLabel.text = $"You earned {rewardAwarded} coins. Speech proof needs practice.";
            hintButton.gameObject.SetActive(true);
            continueButton.gameObject.SetActive(true);
            return;
        }

        statusLabel.text = speechProofSucceeded ? "Speech proof accepted." : "Speech proof needs practice.";
        resultLabel.text = countCorrect
            ? $"You earned {rewardAwarded} coins."
            : $"Counting was off. You earned {rewardAwarded} coins.";
        continueButton.gameObject.SetActive(true);
    }

    void HandleHintPressed()
    {
        if (selectedCount <= 0)
            return;

        hintUsed = true;
        string word = CountingNumberUtility.ToWord(selectedCount);
        speaker ??= PronunciationSpeaker.EnsureExists();
        speaker.Speak(word, activeChest != null ? activeChest.GetNumberClip(selectedCount) : null);
    }

    void HandleContinuePressed()
    {
        RecordAttempt();
        Close();
    }

    void RecordAttempt()
    {
        if (selectedCount <= 0)
            return;

        CurriculumSessionManager.Instance?.RecordCountingMiniGameAttempt(
            activeChest != null ? activeChest.chestCategory : "RuntimeChest",
            targetCount,
            selectedCount,
            spokenNumber,
            countCorrect,
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
        if (selectedCount <= 0)
            return "seen_ignored";
        if (!countCorrect)
            return "opened_wrong_answer";
        if (!speechProofSucceeded)
            return "opened_correct_pronunciation_failed";
        return "opened_correct";
    }

    void Close()
    {
        recognizer?.StopListening();
        ClearCoinVisuals();
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

    void SetNumberButtonsInteractable(bool interactable)
    {
        foreach (Button button in numberButtons)
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

        var canvasGo = new GameObject("ChestCountingCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
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

        var boxGo = new GameObject("CoinBox", typeof(RectTransform), typeof(Image), typeof(Outline));
        boxGo.transform.SetParent(root, false);
        coinBox = boxGo.GetComponent<RectTransform>();
        coinBox.anchorMin = new Vector2(0.5f, 1f);
        coinBox.anchorMax = new Vector2(0.5f, 1f);
        coinBox.pivot = new Vector2(0.5f, 1f);
        coinBox.anchoredPosition = new Vector2(0f, -160f);
        coinBox.sizeDelta = new Vector2(720f, 255f);
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

        var gridGo = new GameObject("NumberButtons", typeof(RectTransform), typeof(GridLayoutGroup));
        gridGo.transform.SetParent(root, false);
        buttonGrid = gridGo.GetComponent<RectTransform>();
        buttonGrid.anchorMin = new Vector2(0.5f, 0f);
        buttonGrid.anchorMax = new Vector2(0.5f, 0f);
        buttonGrid.pivot = new Vector2(0.5f, 0f);
        buttonGrid.anchoredPosition = new Vector2(0f, 118f);
        buttonGrid.sizeDelta = new Vector2(620f, 150f);
        GridLayoutGroup grid = gridGo.GetComponent<GridLayoutGroup>();
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 5;
        grid.cellSize = new Vector2(112f, 62f);
        grid.spacing = new Vector2(14f, 18f);
        grid.childAlignment = TextAnchor.MiddleCenter;

        for (int i = 1; i <= 10; i++)
            numberButtons.Add(MakeNumberButton(buttonGrid, i));

        hintButton = MakeCommandButton(root, "Hint", new Vector2(-150f, 40f), new Vector2(240f, 62f), GameUiTheme.ButtonRole.Secondary);
        hintButton.onClick.AddListener(HandleHintPressed);

        continueButton = MakeCommandButton(root, "Continue", new Vector2(150f, 40f), new Vector2(240f, 62f), GameUiTheme.ButtonRole.Primary);
        continueButton.onClick.AddListener(HandleContinuePressed);

        speakButton = MakeCommandButton(root, "Hold Speak", new Vector2(0f, 112f), new Vector2(260f, 62f), GameUiTheme.ButtonRole.Primary);
        AddHoldToTalkHandlers(speakButton);
        speakButton.gameObject.SetActive(false);
    }

    Button MakeNumberButton(Transform parent, int value)
    {
        Button button = MakeButton(parent, value.ToString(), GameUiTheme.ButtonRole.Secondary);
        int captured = value;
        button.onClick.AddListener(() => HandleNumberSelected(captured));
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

        TextMeshProUGUI label = MakeText(go.GetComponent<RectTransform>(), "Label", 24f, FontStyles.Bold, GameUiTheme.Text, TextAlignmentOptions.Center);
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

    void BuildCoinVisuals(int count, CoinPickup coinPrefab)
    {
        ClearCoinVisuals();
        List<Vector2> positions = BuildNonOverlappingCoinPositions(count);
        for (int i = 0; i < count; i++)
            CreateUiCoin(i, positions[i]);
        BuildWorldCoinVisuals(count, coinPrefab);
    }

    List<Vector2> BuildNonOverlappingCoinPositions(int count)
    {
        const float coinDiameter = 58f;
        const float spacing = 70f;
        const float halfWidth = 310f;
        const float halfHeight = 92f;
        var positions = new List<Vector2>();

        for (int attempt = 0; attempt < 250 && positions.Count < count; attempt++)
        {
            Vector2 candidate = new Vector2(
                Random.Range(-halfWidth, halfWidth),
                Random.Range(-halfHeight, halfHeight));
            if (HasCoinSpacing(candidate, positions, spacing))
                positions.Add(candidate);
        }

        if (positions.Count >= count)
            return positions;

        var grid = new List<Vector2>();
        for (int row = 0; row < 2; row++)
        {
            float y = row == 0 ? -38f : 38f;
            for (int col = 0; col < 5; col++)
            {
                float x = -168f + col * 84f;
                grid.Add(new Vector2(x, y));
            }
        }

        for (int i = 0; i < grid.Count && positions.Count < count; i++)
        {
            if (HasCoinSpacing(grid[i], positions, coinDiameter))
                positions.Add(grid[i]);
        }

        while (positions.Count < count)
            positions.Add(grid[Mathf.Clamp(positions.Count, 0, grid.Count - 1)]);

        return positions;
    }

    static bool HasCoinSpacing(Vector2 candidate, List<Vector2> positions, float spacing)
    {
        float minSqr = spacing * spacing;
        foreach (Vector2 position in positions)
        {
            if ((candidate - position).sqrMagnitude < minSqr)
                return false;
        }

        return true;
    }

    void CreateUiCoin(int index, Vector2 anchoredPosition)
    {
        var go = new GameObject($"CoinToken_{index + 1}", typeof(RectTransform), typeof(Image), typeof(Outline));
        go.transform.SetParent(coinBox, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(58f, 58f);
        rt.anchoredPosition = anchoredPosition;
        rt.localRotation = Quaternion.Euler(0f, 0f, Random.Range(-18f, 18f));

        Image image = go.GetComponent<Image>();
        image.color = GameUiTheme.Gold;
        Outline outline = go.GetComponent<Outline>();
        outline.effectColor = new Color(0.32f, 0.18f, 0.04f, 0.78f);
        outline.effectDistance = new Vector2(3f, -3f);

        var mark = new GameObject("Mark", typeof(RectTransform), typeof(TextMeshProUGUI));
        mark.transform.SetParent(go.transform, false);
        TextMeshProUGUI label = mark.GetComponent<TextMeshProUGUI>();
        label.text = "$";
        label.fontSize = 30f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.verticalAlignment = VerticalAlignmentOptions.Middle;
        label.color = new Color(0.45f, 0.25f, 0.04f, 1f);
        label.raycastTarget = false;
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = Vector2.zero;
        label.rectTransform.offsetMax = Vector2.zero;
        coinVisuals.Add(go);
    }

    void BuildWorldCoinVisuals(int count, CoinPickup coinPrefab)
    {
        Camera camera = Camera.main;
        if (camera == null)
            return;

        var rootGo = new GameObject("ChestCountingWorldCoins");
        worldCoinRoot = rootGo.transform;
        worldCoinRoot.SetParent(camera.transform, false);
        worldCoinRoot.localPosition = new Vector3(0f, -0.18f, 2.1f);
        worldCoinRoot.localRotation = Quaternion.identity;

        for (int i = 0; i < count; i++)
        {
            Vector3 local = new Vector3(
                Random.Range(-0.62f, 0.62f),
                Random.Range(-0.22f, 0.22f),
                Random.Range(-0.04f, 0.04f));
            GameObject coin = coinPrefab != null
                ? Instantiate(coinPrefab.gameObject, worldCoinRoot)
                : GameObject.CreatePrimitive(PrimitiveType.Sphere);
            coin.name = $"CountingCoinPrefab_{i + 1}";
            coin.transform.SetParent(worldCoinRoot, false);
            coin.transform.localPosition = local;
            coin.transform.localRotation = Quaternion.Euler(90f, 0f, Random.Range(0f, 360f));
            coin.transform.localScale = Vector3.one * 0.14f;

            foreach (CoinPickup pickup in coin.GetComponentsInChildren<CoinPickup>(true))
                pickup.enabled = false;
            foreach (Collider collider in coin.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (Rigidbody body in coin.GetComponentsInChildren<Rigidbody>(true))
                body.isKinematic = true;

            Renderer renderer = coin.GetComponentInChildren<Renderer>(true);
            if (renderer != null && coinPrefab == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                renderer.material = new Material(shader) { color = GameUiTheme.Gold };
            }
            worldCoinVisuals.Add(coin);
        }
    }

    void ClearCoinVisuals()
    {
        foreach (GameObject visual in coinVisuals)
            if (visual != null)
                Destroy(visual);
        coinVisuals.Clear();

        foreach (GameObject visual in worldCoinVisuals)
            if (visual != null)
                Destroy(visual);
        worldCoinVisuals.Clear();

        if (worldCoinRoot != null)
            Destroy(worldCoinRoot.gameObject);
        worldCoinRoot = null;
    }
}
