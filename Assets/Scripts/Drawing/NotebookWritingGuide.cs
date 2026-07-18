using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public sealed class NotebookWritingGuide
{
    public struct NotebookSlot
    {
        public Rect slotRect;
        public float topY;
        public float midlineY;
        public float baselineY;
        public float bottomY;
    }

    readonly RectTransform panel;
    readonly TemplateLibrary templateLibrary;
    readonly RectTransform root;
    readonly List<GameObject> dynamicChildren = new List<GameObject>();

    string targetWord = "";
    int activeIndex = -1;
    bool developerDiagnosticsVisible;

    public NotebookWritingGuide(RectTransform drawingPanel, TemplateLibrary library)
    {
        panel = drawingPanel;
        templateLibrary = library;

        var rootGo = new GameObject("NotebookWritingGuide", typeof(RectTransform));
        root = rootGo.GetComponent<RectTransform>();
        root.SetParent(panel, false);
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;
        root.SetAsFirstSibling();
        SetThemeGuideLinesVisible(false);
    }

    public void SetTarget(string word, int letterIndex)
    {
        targetWord = SpellRegistry.NormalizeWord(word);
        activeIndex = Mathf.Max(0, letterIndex);
        Refresh();
    }

    public void SetDeveloperDiagnosticsVisible(bool visible)
    {
        if (developerDiagnosticsVisible == visible)
            return;

        developerDiagnosticsVisible = visible;
        if (root != null && root.gameObject.activeSelf && !string.IsNullOrEmpty(targetWord))
            Refresh();
    }

    public void Hide()
    {
        if (root != null)
            root.gameObject.SetActive(false);
        SetThemeGuideLinesVisible(true);
    }

    public void Show()
    {
        if (root != null)
            root.gameObject.SetActive(true);
        SetThemeGuideLinesVisible(false);
    }

    public static NotebookSlot CalculateSlot(Rect panelRect, string word, int letterIndex)
    {
        string normalized = SpellRegistry.NormalizeWord(word);
        int count = Mathf.Max(1, normalized.Length);
        float usableWidth = Mathf.Max(120f, panelRect.width * 0.82f);
        float maxSlotWidth = count == 1 ? Mathf.Min(usableWidth, 420f) : 220f;
        float slotWidth = Mathf.Clamp(usableWidth / count, 72f, maxSlotWidth);
        float totalWidth = slotWidth * count;
        float startX = panelRect.center.x - totalWidth * 0.5f;
        int index = Mathf.Clamp(letterIndex, 0, count - 1);

        float writingHeight = Mathf.Clamp(panelRect.height * 0.76f, 280f, Mathf.Max(320f, panelRect.height * 0.88f));
        float centerY = panelRect.center.y - panelRect.height * 0.06f;
        float topY = centerY + writingHeight * 0.5f;
        float bottomY = centerY - writingHeight * 0.5f;
        float baselineY = bottomY + writingHeight * 0.28f;
        float midlineY = bottomY + writingHeight * 0.62f;

        return new NotebookSlot
        {
            slotRect = new Rect(startX + index * slotWidth, bottomY, slotWidth, writingHeight),
            topY = topY,
            midlineY = midlineY,
            baselineY = baselineY,
            bottomY = bottomY
        };
    }

    public static Rect CalculateTemplateFrame(Rect panelRect, string word, int letterIndex)
    {
        NotebookSlot slot = CalculateSlot(panelRect, word, letterIndex);
        float writingHeight = Mathf.Max(1f, slot.topY - slot.bottomY);
        float insetY = Mathf.Max(6f, writingHeight * 0.04f);
        float width = slot.slotRect.width * 0.86f;
        float height = Mathf.Max(1f, writingHeight - insetY * 2f);
        return new Rect(
            slot.slotRect.center.x - width * 0.5f,
            slot.bottomY + insetY,
            width,
            height);
    }

    void Refresh()
    {
        if (panel == null || root == null)
            return;

        Show();
        Clear();
        Rect rect = panel.rect;
        NotebookSlot activeSlot = CalculateSlot(rect, targetWord, activeIndex);

        MakeLine("UpperBound", activeSlot.topY, new Color(0.10f, 0.22f, 0.42f, 0.70f), 2.6f);
        MakeLine("LowerBound", activeSlot.bottomY, new Color(0.10f, 0.22f, 0.42f, 0.70f), 2.6f);

        if (!developerDiagnosticsVisible)
            return;

        MakeSlotFill(activeSlot.slotRect, new Color(0.15f, 0.85f, 0.35f, 0.08f));
        MakeLine("Midline", activeSlot.midlineY, new Color(0.55f, 0.90f, 1f, 0.45f), 1.8f);
        MakeLine("Baseline", activeSlot.baselineY, new Color(0.15f, 0.85f, 0.35f, 0.60f), 2.2f);
        MakeVerticalLine("SlotLeft", activeSlot.slotRect.xMin, activeSlot.slotRect.yMin, activeSlot.slotRect.height, new Color(0.15f, 0.85f, 0.35f, 0.48f), 2f);
        MakeVerticalLine("SlotRight", activeSlot.slotRect.xMax, activeSlot.slotRect.yMin, activeSlot.slotRect.height, new Color(0.15f, 0.85f, 0.35f, 0.48f), 2f);
    }

    void MakeSlotFill(Rect rect, Color color)
    {
        var go = new GameObject("ActiveSlotFill", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(root, false);
        dynamicChildren.Add(go);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = rect.center;
        rt.sizeDelta = rect.size;
        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        go.transform.SetAsFirstSibling();
    }

    void MakeLine(string name, float y, Color color, float thickness)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(root, false);
        dynamicChildren.Add(go);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = new Vector2(0f, thickness);
        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }

    void MakeVerticalLine(string name, float x, float y, float height, Color color, float thickness)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(root, false);
        dynamicChildren.Add(go);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y + height * 0.5f);
        rt.sizeDelta = new Vector2(thickness, height);
        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }

    void Clear()
    {
        foreach (GameObject child in dynamicChildren)
            if (child != null)
                Object.Destroy(child);
        dynamicChildren.Clear();
    }

    void SetThemeGuideLinesVisible(bool visible)
    {
        if (panel == null)
            return;

        Transform themeGuideLines = panel.Find("UIThemeGuideLines");
        if (themeGuideLines != null)
            themeGuideLines.gameObject.SetActive(visible);
    }
}
