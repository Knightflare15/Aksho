using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class BuddyHelpWindowRuntime : MonoBehaviour
{
    static BuddyHelpWindowRuntime instance;
    GameObject panel;
    TextMeshProUGUI label;

    public static BuddyHelpWindowRuntime EnsureExists()
    {
        if (instance != null) return instance;
        var go = new GameObject("BuddyHelpWindow");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<BuddyHelpWindowRuntime>();
        return instance;
    }

    void Awake()
    {
        var canvasGo = new GameObject("BuddyHelpCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 700;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        panel = new GameObject("BuddyHelpPanel", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        panel.transform.SetParent(canvasGo.transform, false);
        var rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f); rect.anchorMax = new Vector2(0f, 1f); rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(32f, -32f); rect.sizeDelta = new Vector2(560f, 230f);
        panel.GetComponent<UnityEngine.UI.Image>().color = new Color(0.08f, 0.05f, 0.04f, 0.96f);
        var textGo = new GameObject("BuddyHelpText", typeof(RectTransform));
        textGo.transform.SetParent(panel.transform, false);
        label = textGo.AddComponent<TextMeshProUGUI>();
        label.fontSize = 21f; label.color = Color.white; label.alignment = TextAlignmentOptions.TopLeft;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(18f, 18f); label.rectTransform.offsetMax = new Vector2(-18f, -18f);
        panel.SetActive(false);
    }

    public void Show(string message)
    {
        if (label == null || string.IsNullOrWhiteSpace(message)) return;
        label.text = $"Buddy\n\n{message}";
        panel.SetActive(true);
    }

    public void Hide()
    {
        if (panel != null)
            panel.SetActive(false);
    }
}
