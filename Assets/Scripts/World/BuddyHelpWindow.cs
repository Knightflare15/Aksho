using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;


// Kept in the gameplay assembly so dynamically generated dialogue UI can always
// resolve it during Unity's script compilation pass.
sealed class BuddyHelpWindow
{
    static BuddyHelpWindow instance;
    GameObject panel;
    TextMeshProUGUI label;

    public static BuddyHelpWindow EnsureExists()
    {
        if (instance != null) return instance;
        instance = new BuddyHelpWindow();
        instance.Build();
        return instance;
    }

    public static void HideIfExists()
    {
        instance?.Hide();
    }

    void Build()
    {
        GameObject canvasGo = new GameObject("BuddyHelpCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        if (Application.isPlaying)
            UnityEngine.Object.DontDestroyOnLoad(canvasGo);
        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 700;
        canvas.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvas.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920f, 1080f);
        panel = new GameObject("BuddyHelpPanel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(canvasGo.transform, false);
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f); rect.anchorMax = new Vector2(0f, 1f); rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(32f, -32f); rect.sizeDelta = new Vector2(560f, 230f);
        panel.GetComponent<Image>().color = new Color(0.08f, 0.05f, 0.04f, 0.96f);
        GameObject textGo = new GameObject("BuddyHelpText", typeof(RectTransform));
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
        // The dedicated runtime component owns the actual canvas; this bridge
        // prevents dialogue UI from depending on editor compilation order.
        if (label == null) Build();
        label.text = $"Buddy\n\n{message}";
        panel.SetActive(true);
    }

    public void Hide()
    {
        if (panel != null)
            panel.SetActive(false);
    }
}
