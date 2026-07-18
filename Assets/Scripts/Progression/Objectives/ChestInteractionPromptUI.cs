using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class ChestInteractionPromptUI : MonoBehaviour
{
    static ChestInteractionPromptUI instance;

    Canvas canvas;
    RectTransform panel;
    TextMeshProUGUI label;
    Button button;
    TreasureChestReward target;
    int shownFrame = -1;

    public static ChestInteractionPromptUI EnsureExists()
    {
        if (instance != null)
            return instance;

        ChestInteractionPromptUI existing = FindAnyObjectByType<ChestInteractionPromptUI>();
        if (existing != null)
        {
            instance = existing;
            return instance;
        }

        var go = new GameObject("ChestInteractionPromptUI");
        instance = go.AddComponent<ChestInteractionPromptUI>();
        DontDestroyOnLoad(go);
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
        Build();
        Hide();
    }

    void LateUpdate()
    {
        if (shownFrame != Time.frameCount)
            Hide();
    }

    public void Show(TreasureChestReward chest)
    {
        if (chest == null || ChestMiniGameState.IsOpen)
            return;

        EnsureBuilt();
        target = chest;
        shownFrame = Time.frameCount;
        if (panel != null)
            panel.gameObject.SetActive(true);
        if (label != null)
            label.text = "Open Chest";
    }

    public void Hide()
    {
        target = null;
        if (panel != null)
            panel.gameObject.SetActive(false);
    }

    void HandlePressed()
    {
        target?.TryOpen();
    }

    void EnsureBuilt()
    {
        if (canvas == null || panel == null)
            Build();
    }

    void Build()
    {
        if (canvas != null)
            return;

        var root = new GameObject("ChestInteractionCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 420;

        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        var panelGo = new GameObject("Prompt", typeof(RectTransform), typeof(Image), typeof(Button));
        panelGo.transform.SetParent(root.transform, false);
        panel = panelGo.GetComponent<RectTransform>();
        panel.anchorMin = new Vector2(0.5f, 0f);
        panel.anchorMax = new Vector2(0.5f, 0f);
        panel.pivot = new Vector2(0.5f, 0f);
        panel.anchoredPosition = new Vector2(0f, 190f);
        panel.sizeDelta = new Vector2(300f, 72f);

        button = panelGo.GetComponent<Button>();
        button.onClick.AddListener(HandlePressed);
        GameUiTheme.StyleButton(button, GameUiTheme.ButtonRole.Primary);

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(panelGo.transform, false);
        label = labelGo.GetComponent<TextMeshProUGUI>();
        label.text = "Open Chest";
        label.alignment = TextAlignmentOptions.Center;
        label.verticalAlignment = VerticalAlignmentOptions.Middle;
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = Vector2.zero;
        label.rectTransform.offsetMax = Vector2.zero;
        GameUiTheme.StyleText(label, 22f, true);
    }
}
