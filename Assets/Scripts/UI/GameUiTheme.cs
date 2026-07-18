using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static class GameUiTheme
{
    const float PanelFrameInset = 14f;
    const float ButtonFrameInset = 4f;
    public static readonly Vector2 ReadableReferenceResolution = new Vector2(1280f, 720f);

    public static readonly Color Ink = new Color(0.045f, 0.055f, 0.075f, 1f);
    public static readonly Color Panel = new Color(0.105f, 0.085f, 0.135f, 0.94f);
    public static readonly Color PanelRaised = new Color(0.145f, 0.12f, 0.18f, 0.97f);
    public static readonly Color Surface = new Color(0.96f, 0.92f, 0.78f, 1f);
    public static readonly Color SurfaceLine = new Color(0.38f, 0.25f, 0.14f, 0.28f);
    public static readonly Color Text = new Color(0.96f, 0.98f, 1f, 1f);
    public static readonly Color TextMuted = new Color(0.74f, 0.78f, 0.86f, 1f);
    public static readonly Color Accent = new Color(0.28f, 0.88f, 0.84f, 1f);
    public static readonly Color Gold = new Color(1f, 0.72f, 0.26f, 1f);
    public static readonly Color Danger = new Color(0.86f, 0.24f, 0.24f, 1f);
    public static readonly Color Stroke = new Color(0.07f, 0.11f, 0.16f, 1f);

    public enum ButtonRole
    {
        Primary,
        Secondary,
        Quiet,
        Danger,
        Prime,
        LetterEmpty,
        LetterSaved
    }

    public enum HudLabelRole
    {
        Prompt,
        Status,
        Hint,
        Word
    }

    static TMP_FontAsset themedFont;

    public static void ConfigureReadableCanvasScaler(CanvasScaler scaler)
    {
        if (scaler == null) return;

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = ReadableReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
    }

    public static void ConfigureStandardGameplayCanvasScaler(CanvasScaler scaler)
    {
        if (scaler == null) return;

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
    }

    public static void StylePanel(GameObject panel, bool overlay = true)
    {
        if (panel == null) return;

        var image = panel.GetComponent<Image>() ?? panel.AddComponent<Image>();
        image.color = overlay ? Panel : PanelRaised;
        image.raycastTarget = true;

        var outline = panel.GetComponent<Outline>() ?? panel.AddComponent<Outline>();
        outline.effectColor = new Color(Gold.r, Gold.g, Gold.b, overlay ? 0.22f : 0.32f);
        outline.effectDistance = new Vector2(2f, -2f);

        var shadow = GetOrAddDropShadow(panel);
        shadow.effectColor = new Color(0f, 0f, 0f, overlay ? 0.42f : 0.52f);
        shadow.effectDistance = new Vector2(4f, -5f);

        EnsurePanelOrnaments(panel, overlay);
    }

    public static void StyleHudPanel(RectTransform panel, float alpha = 0.9f)
    {
        if (panel == null) return;

        var image = panel.GetComponent<Image>() ?? panel.gameObject.AddComponent<Image>();
        image.color = new Color(PanelRaised.r, PanelRaised.g, PanelRaised.b, alpha);
        image.raycastTarget = false;

        var outline = panel.GetComponent<Outline>() ?? panel.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(Gold.r, Gold.g, Gold.b, 0.22f);
        outline.effectDistance = new Vector2(2f, -2f);

        var shadow = GetOrAddDropShadow(panel.gameObject);
        shadow.effectColor = new Color(0f, 0f, 0f, 0.38f);
        shadow.effectDistance = new Vector2(3f, -4f);

        EnsurePanelOrnaments(panel.gameObject, true);
    }

    public static void StyleDrawingSurface(RectTransform surface, bool recorder)
    {
        if (surface == null) return;

        var image = surface.GetComponent<Image>() ?? surface.gameObject.AddComponent<Image>();
        image.color = recorder ? new Color(0.985f, 0.965f, 0.895f, 1f) : Surface;
        image.raycastTarget = true;

        var outline = surface.GetComponent<Outline>() ?? surface.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.05f, 0.07f, 0.10f, 0.45f);
        outline.effectDistance = new Vector2(2f, -2f);

        EnsureGuideLines(surface);
    }

    public static void StyleButton(Button button, ButtonRole role = ButtonRole.Secondary)
    {
        if (button == null) return;

        var image = button.GetComponent<Image>() ?? button.gameObject.AddComponent<Image>();
        image.color = RoleColor(role);
        image.raycastTarget = true;

        var colors = button.colors;
        colors.normalColor = RoleColor(role);
        colors.highlightedColor = Color.Lerp(RoleColor(role), Color.white, 0.12f);
        colors.pressedColor = Color.Lerp(RoleColor(role), Color.black, 0.18f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(0.22f, 0.25f, 0.29f, 0.62f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        var outline = button.GetComponent<Outline>() ?? button.gameObject.AddComponent<Outline>();
        Color trim = RoleTrimColor(role);
        outline.effectColor = new Color(trim.r, trim.g, trim.b, role == ButtonRole.Quiet ? 0.2f : 0.36f);
        outline.effectDistance = new Vector2(2f, -2f);

        var shadow = GetOrAddDropShadow(button.gameObject);
        shadow.effectColor = new Color(0f, 0f, 0f, role == ButtonRole.Quiet ? 0.32f : 0.44f);
        shadow.effectDistance = new Vector2(3f, -3f);

        EnsureButtonOrnaments(button, role);

        foreach (var tmp in button.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            StyleText(tmp, role == ButtonRole.LetterEmpty || role == ButtonRole.LetterSaved ? 24f : 16f, true);
            tmp.rectTransform.offsetMin = new Vector2(16f, 4f);
            tmp.rectTransform.offsetMax = new Vector2(-16f, -4f);
        }
    }

    public static void StyleText(TextMeshProUGUI text, float size = 0f, bool buttonText = false)
    {
        if (text == null) return;

        if (size > 0f)
            text.fontSize = size;

        TMP_FontAsset font = ThemedFont;
        if (font != null)
            text.font = font;

        text.color = buttonText ? Text : text.color == Color.white ? Text : text.color;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        text.characterSpacing = 0f;

        if (buttonText)
            text.fontStyle = FontStyles.Bold;

        var shadow = text.GetComponent<Shadow>() ?? text.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.35f);
        shadow.effectDistance = new Vector2(1f, -1f);
    }

    public static void StyleHudLabel(TextMeshProUGUI text, HudLabelRole role)
    {
        if (text == null) return;

        switch (role)
        {
            case HudLabelRole.Prompt:
                StyleText(text, 28f);
                text.color = Text;
                text.fontStyle = FontStyles.Bold;
                EnsureBackdrop(text, new Vector2(26f, 18f), new Color(0.06f, 0.10f, 0.16f, 0.84f));
                break;
            case HudLabelRole.Status:
                StyleText(text, 18f);
                text.color = TextMuted;
                text.fontStyle = FontStyles.Bold;
                EnsureBackdrop(text, new Vector2(18f, 12f), new Color(0.08f, 0.12f, 0.19f, 0.78f));
                break;
            case HudLabelRole.Hint:
                StyleText(text, 19f);
                text.color = Text;
                text.fontStyle = FontStyles.Normal;
                EnsureBackdrop(text, new Vector2(22f, 16f), new Color(0.08f, 0.12f, 0.19f, 0.82f));
                break;
            case HudLabelRole.Word:
                StyleText(text, 24f);
                text.color = Gold;
                text.fontStyle = FontStyles.Bold;
                EnsureBackdrop(text, new Vector2(22f, 16f), new Color(0.08f, 0.12f, 0.19f, 0.78f));
                break;
        }
    }

    public static void StyleScrollRect(ScrollRect scrollRect)
    {
        if (scrollRect == null) return;

        var viewport = scrollRect.viewport != null
            ? scrollRect.viewport
            : scrollRect.GetComponent<RectTransform>();

        if (viewport != null)
        {
            var image = viewport.GetComponent<Image>() ?? viewport.gameObject.AddComponent<Image>();
            image.color = new Color(0.03f, 0.04f, 0.065f, 0.42f);
        }

        StyleScrollbar(scrollRect.verticalScrollbar);
        StyleScrollbar(scrollRect.horizontalScrollbar);
    }

    public static void StyleVoiceBadgePanel(GameObject panel)
    {
        if (panel == null) return;

        var rectTransform = panel.GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            StyleHudPanel(rectTransform, 0.92f);
            var image = panel.GetComponent<Image>();
            if (image != null)
                image.raycastTarget = true;
        }
    }

    public static void StyleVoiceBadgeText(TextMeshProUGUI text, bool emphasized)
    {
        if (text == null) return;

        StyleText(text, emphasized ? 18f : 13f);
        text.color = emphasized ? Text : TextMuted;
        text.fontStyle = emphasized ? FontStyles.Bold : FontStyles.Normal;
        text.textWrappingMode = TextWrappingModes.Normal;
    }

    public static void StyleDrawingVoiceBadgeText(TextMeshProUGUI text, bool emphasized)
    {
        if (text == null) return;

        StyleText(text, emphasized ? 24f : 18f);
        text.color = emphasized ? Text : TextMuted;
        text.fontStyle = emphasized ? FontStyles.Bold : FontStyles.Normal;
        text.textWrappingMode = TextWrappingModes.Normal;
    }

    public static void StyleAllText(Transform root)
    {
        if (root == null) return;

        foreach (var tmp in root.GetComponentsInChildren<TextMeshProUGUI>(true))
            StyleText(tmp);
    }

    public static Color RoleColor(ButtonRole role)
    {
        switch (role)
        {
            case ButtonRole.Primary:
                return new Color(0.12f, 0.48f, 0.44f, 1f);
            case ButtonRole.Quiet:
                return new Color(0.16f, 0.14f, 0.21f, 0.92f);
            case ButtonRole.Danger:
                return new Color(0.56f, 0.13f, 0.12f, 1f);
            case ButtonRole.Prime:
                return new Color(0.58f, 0.36f, 0.10f, 1f);
            case ButtonRole.LetterSaved:
                return new Color(0.12f, 0.40f, 0.34f, 1f);
            case ButtonRole.LetterEmpty:
                return new Color(0.18f, 0.15f, 0.23f, 1f);
            default:
                return new Color(0.22f, 0.18f, 0.28f, 1f);
        }
    }

    static TMP_FontAsset ThemedFont
    {
        get
        {
            if (themedFont == null)
                themedFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/Oswald Bold SDF");

            return themedFont != null ? themedFont : TMP_Settings.defaultFontAsset;
        }
    }

    static Color RoleTrimColor(ButtonRole role)
    {
        switch (role)
        {
            case ButtonRole.Primary:
            case ButtonRole.LetterSaved:
                return Accent;
            case ButtonRole.Danger:
                return new Color(1f, 0.46f, 0.34f, 1f);
            case ButtonRole.Prime:
                return Gold;
            default:
                return new Color(Gold.r, Gold.g, Gold.b, 0.92f);
        }
    }

    static void EnsurePanelOrnaments(GameObject panel, bool overlay)
    {
        var panelRt = panel.GetComponent<RectTransform>();
        if (panelRt == null) return;

        const string frameName = "UIThemePanelFrame";
        Transform existing = panel.transform.Find(frameName);
        RectTransform frameRt;
        if (existing == null)
        {
            var frame = new GameObject(frameName, typeof(RectTransform), typeof(LayoutElement));
            frame.transform.SetParent(panel.transform, false);
            frame.transform.SetSiblingIndex(0);
            frame.GetComponent<LayoutElement>().ignoreLayout = true;
            frameRt = frame.GetComponent<RectTransform>();
        }
        else
        {
            frameRt = existing.GetComponent<RectTransform>();
            existing.SetSiblingIndex(0);
        }

        frameRt.anchorMin = Vector2.zero;
        frameRt.anchorMax = Vector2.one;
        frameRt.offsetMin = new Vector2(PanelFrameInset, PanelFrameInset);
        frameRt.offsetMax = new Vector2(-PanelFrameInset, -PanelFrameInset);
        frameRt.localScale = Vector3.one;
        frameRt.localRotation = Quaternion.identity;

        Color border = new Color(Gold.r, Gold.g, Gold.b, overlay ? 0.34f : 0.46f);
        Color accent = new Color(Accent.r, Accent.g, Accent.b, overlay ? 0.48f : 0.58f);
        EnsurePanelLine(frameRt, "TopRuneLine", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 2f), border);
        EnsurePanelLine(frameRt, "BottomRuneLine", Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 2f), border);
        EnsurePanelLine(frameRt, "LeftRuneLine", Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(2f, 0f), border);
        EnsurePanelLine(frameRt, "RightRuneLine", new Vector2(1f, 0f), Vector2.one, new Vector2(1f, 0.5f), new Vector2(2f, 0f), border);
        EnsurePanelLine(frameRt, "ArcaneAccent", new Vector2(0f, 1f), new Vector2(0.38f, 1f), new Vector2(0f, 1f), new Vector2(0f, 4f), accent);

        EnsurePanelCorner(frameRt, "CornerTL", new Vector2(0f, 1f), new Vector2(1f, -1f), border);
        EnsurePanelCorner(frameRt, "CornerTR", Vector2.one, new Vector2(-1f, -1f), border);
        EnsurePanelCorner(frameRt, "CornerBL", Vector2.zero, new Vector2(1f, 1f), border);
        EnsurePanelCorner(frameRt, "CornerBR", new Vector2(1f, 0f), new Vector2(-1f, 1f), border);
    }

    static void EnsureButtonOrnaments(Button button, ButtonRole role)
    {
        var buttonRt = button.GetComponent<RectTransform>();
        if (buttonRt == null) return;

        const string frameName = "UIThemeButtonFrame";
        Transform existing = button.transform.Find(frameName);
        RectTransform frameRt;
        if (existing == null)
        {
            var frame = new GameObject(frameName, typeof(RectTransform), typeof(LayoutElement));
            frame.transform.SetParent(button.transform, false);
            frame.GetComponent<LayoutElement>().ignoreLayout = true;
            frameRt = frame.GetComponent<RectTransform>();
        }
        else
        {
            frameRt = existing.GetComponent<RectTransform>();
        }

        frameRt.SetSiblingIndex(0);
        frameRt.anchorMin = Vector2.zero;
        frameRt.anchorMax = Vector2.one;
        frameRt.offsetMin = new Vector2(ButtonFrameInset, ButtonFrameInset);
        frameRt.offsetMax = new Vector2(-ButtonFrameInset, -ButtonFrameInset);
        frameRt.localScale = Vector3.one;
        frameRt.localRotation = Quaternion.identity;

        Color trim = RoleTrimColor(role);
        Color edge = new Color(trim.r, trim.g, trim.b, role == ButtonRole.Quiet ? 0.32f : 0.58f);
        Color glint = new Color(1f, 1f, 1f, role == ButtonRole.Primary ? 0.18f : 0.1f);
        Color foot = new Color(0f, 0f, 0f, 0.22f);

        EnsurePanelLine(frameRt, "TopEdge", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 2f), edge);
        EnsurePanelLine(frameRt, "BottomEdge", Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 2f), foot);
        EnsurePanelLine(frameRt, "LeftEdge", Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(2f, 0f), edge);
        EnsurePanelLine(frameRt, "RightEdge", new Vector2(1f, 0f), Vector2.one, new Vector2(1f, 0.5f), new Vector2(2f, 0f), foot);
        EnsurePanelLine(frameRt, "TopGlint", new Vector2(0.08f, 1f), new Vector2(0.46f, 1f), new Vector2(0f, 1f), new Vector2(0f, 4f), glint);
        EnsurePanelLine(frameRt, "LeftRune", new Vector2(0f, 0.22f), new Vector2(0f, 0.78f), new Vector2(0f, 0.5f), new Vector2(4f, 0f), edge);
        EnsurePanelLine(frameRt, "RightRune", new Vector2(1f, 0.22f), new Vector2(1f, 0.78f), new Vector2(1f, 0.5f), new Vector2(4f, 0f), edge);
    }

    static void EnsurePanelLine(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta, Color color)
    {
        var lineRt = EnsureDecorativeRect(parent, name, color);
        lineRt.anchorMin = anchorMin;
        lineRt.anchorMax = anchorMax;
        lineRt.pivot = pivot;
        lineRt.sizeDelta = sizeDelta;
        lineRt.anchoredPosition = Vector2.zero;
    }

    static void EnsurePanelCorner(RectTransform parent, string name, Vector2 anchor, Vector2 direction, Color color)
    {
        var cornerRt = EnsureDecorativeRect(parent, name, color);
        cornerRt.anchorMin = anchor;
        cornerRt.anchorMax = anchor;
        cornerRt.pivot = new Vector2(anchor.x, anchor.y);
        cornerRt.sizeDelta = new Vector2(18f, 18f);
        cornerRt.anchoredPosition = Vector2.zero;

        var armA = EnsureDecorativeRect(cornerRt, "ArmH", color);
        armA.anchorMin = new Vector2(0.5f, 0.5f);
        armA.anchorMax = new Vector2(0.5f, 0.5f);
        armA.pivot = new Vector2(0.5f, 0.5f);
        armA.sizeDelta = new Vector2(18f, 3f);
        armA.anchoredPosition = new Vector2(direction.x * 7.5f, 0f);

        var armB = EnsureDecorativeRect(cornerRt, "ArmV", color);
        armB.anchorMin = new Vector2(0.5f, 0.5f);
        armB.anchorMax = new Vector2(0.5f, 0.5f);
        armB.pivot = new Vector2(0.5f, 0.5f);
        armB.sizeDelta = new Vector2(3f, 18f);
        armB.anchoredPosition = new Vector2(0f, direction.y * 7.5f);
    }

    static RectTransform EnsureDecorativeRect(Transform parent, string name, Color color)
    {
        Transform existing = parent.Find(name);
        GameObject go;
        if (existing == null)
        {
            go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().ignoreLayout = true;
        }
        else
        {
            go = existing.gameObject;
        }

        var image = go.GetComponent<Image>() ?? go.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return go.GetComponent<RectTransform>();
    }

    static void StyleScrollbar(Scrollbar scrollbar)
    {
        if (scrollbar == null) return;

        var image = scrollbar.GetComponent<Image>();
        if (image != null)
            image.color = new Color(0.02f, 0.025f, 0.04f, 0.45f);

        if (scrollbar.handleRect != null)
        {
            var handle = scrollbar.handleRect.GetComponent<Image>();
            if (handle != null)
                handle.color = new Color(0.35f, 0.74f, 0.70f, 0.82f);
        }
    }

    static Shadow GetOrAddDropShadow(GameObject target)
    {
        foreach (var shadow in target.GetComponents<Shadow>())
        {
            if (shadow.GetType() == typeof(Shadow))
                return shadow;
        }

        return target.AddComponent<Shadow>();
    }

    static void EnsureGuideLines(RectTransform surface)
    {
        const string gridName = "UIThemeGuideLines";
        if (surface.Find(gridName) != null)
            return;

        var root = new GameObject(gridName, typeof(RectTransform));
        root.transform.SetParent(surface, false);
        var rt = root.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        for (int i = 1; i < 4; i++)
        {
            float t = i / 4f;
            MakeLine(root.transform, true, t, SurfaceLine, 1f);
            MakeLine(root.transform, false, t, SurfaceLine, 1f);
        }

        MakeLine(root.transform, false, 0.62f, new Color(0.03f, 0.38f, 0.34f, 0.42f), 2f);
    }

    static void EnsureBackdrop(TextMeshProUGUI text, Vector2 padding, Color color)
    {
        var parent = text.transform.parent;
        if (parent == null) return;

        string backdropName = text.name + "_ThemeBackdrop";
        var existing = parent.Find(backdropName);
        RectTransform backdropRt;
        Image image;

        if (existing == null)
        {
            var backdrop = new GameObject(backdropName, typeof(RectTransform), typeof(Image), typeof(Outline));
            backdrop.transform.SetParent(parent, false);
            backdrop.transform.SetSiblingIndex(0);
            backdropRt = backdrop.GetComponent<RectTransform>();
            image = backdrop.GetComponent<Image>();
            var outline = backdrop.GetComponent<Outline>();
            outline.effectColor = new Color(1f, 1f, 1f, 0.08f);
            outline.effectDistance = new Vector2(1f, -1f);
        }
        else
        {
            backdropRt = existing.GetComponent<RectTransform>();
            image = existing.GetComponent<Image>();
            existing.SetSiblingIndex(0);
        }

        var textRt = text.rectTransform;
        backdropRt.anchorMin = textRt.anchorMin;
        backdropRt.anchorMax = textRt.anchorMax;
        backdropRt.pivot = textRt.pivot;
        backdropRt.anchoredPosition = textRt.anchoredPosition;
        backdropRt.sizeDelta = textRt.sizeDelta + padding;
        backdropRt.localScale = Vector3.one;
        backdropRt.localRotation = Quaternion.identity;
        image.color = color;
        image.raycastTarget = false;
    }

    static void MakeLine(Transform parent, bool vertical, float normalizedPos, Color color, float thickness)
    {
        var line = new GameObject(vertical ? "V" : "H", typeof(RectTransform), typeof(Image));
        line.transform.SetParent(parent, false);
        var rt = line.GetComponent<RectTransform>();
        var image = line.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;

        if (vertical)
        {
            rt.anchorMin = new Vector2(normalizedPos, 0f);
            rt.anchorMax = new Vector2(normalizedPos, 1f);
            rt.sizeDelta = new Vector2(thickness, 0f);
        }
        else
        {
            rt.anchorMin = new Vector2(0f, normalizedPos);
            rt.anchorMax = new Vector2(1f, normalizedPos);
            rt.sizeDelta = new Vector2(0f, thickness);
        }

        rt.anchoredPosition = Vector2.zero;
    }
}
