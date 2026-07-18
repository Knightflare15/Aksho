using System.Collections.Generic;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public partial class ChallengeMode
{
    void EnsureVoiceStatusBadge()
    {
        if (voiceBadgeRoot != null || drawController == null || drawController.drawingPanel == null)
            return;

        var rootGo = new GameObject("VoiceStatusBadge", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(Button));
        voiceBadgeRoot = rootGo.GetComponent<RectTransform>();
        PlaceVoiceControlAboveDrawingPanel(voiceBadgeRoot, new Vector2(VoiceBadgeWidth, VoiceBadgeHeight), VoiceControlGap);
        voiceBadgePanel = rootGo.GetComponent<Image>();
        voiceBadgePanel.raycastTarget = true;
        voiceBadgeButton = rootGo.GetComponent<Button>();
        AddVoiceBadgeHoldHandlers(voiceBadgeButton);

        var dotGo = new GameObject("Dot", typeof(RectTransform), typeof(Image));
        dotGo.transform.SetParent(rootGo.transform, false);
        var dotRt = dotGo.GetComponent<RectTransform>();
        dotRt.anchorMin = new Vector2(0f, 0.5f);
        dotRt.anchorMax = new Vector2(0f, 0.5f);
        dotRt.pivot = new Vector2(0.5f, 0.5f);
        dotRt.anchoredPosition = new Vector2(34f, 0f);
        dotRt.sizeDelta = new Vector2(22f, 22f);
        voiceBadgeDot = dotGo.GetComponent<Image>();
        BrushStrokeStyle.ApplyDot(voiceBadgeDot, GameUiTheme.Accent);

        var stateGo = new GameObject("State", typeof(RectTransform), typeof(TextMeshProUGUI));
        stateGo.transform.SetParent(rootGo.transform, false);
        var stateRt = stateGo.GetComponent<RectTransform>();
        stateRt.anchorMin = new Vector2(0f, 0.52f);
        stateRt.anchorMax = new Vector2(1f, 1f);
        stateRt.offsetMin = new Vector2(64f, 0f);
        stateRt.offsetMax = new Vector2(-18f, -10f);
        voiceBadgeStateLabel = stateGo.GetComponent<TextMeshProUGUI>();
        voiceBadgeStateLabel.font = TMP_Settings.defaultFontAsset;
        voiceBadgeStateLabel.text = "LISTENING";
        voiceBadgeStateLabel.alignment = TextAlignmentOptions.Left;
        voiceBadgeStateLabel.verticalAlignment = VerticalAlignmentOptions.Bottom;

        var detailGo = new GameObject("Detail", typeof(RectTransform), typeof(TextMeshProUGUI));
        detailGo.transform.SetParent(rootGo.transform, false);
        var detailRt = detailGo.GetComponent<RectTransform>();
        detailRt.anchorMin = new Vector2(0f, 0f);
        detailRt.anchorMax = new Vector2(1f, 0.56f);
        detailRt.offsetMin = new Vector2(64f, 12f);
        detailRt.offsetMax = new Vector2(-18f, 0f);
        voiceBadgeDetailLabel = detailGo.GetComponent<TextMeshProUGUI>();
        voiceBadgeDetailLabel.font = TMP_Settings.defaultFontAsset;
        voiceBadgeDetailLabel.text = "";
        voiceBadgeDetailLabel.alignment = TextAlignmentOptions.Left;
        voiceBadgeDetailLabel.verticalAlignment = VerticalAlignmentOptions.Top;

        ApplyUITheme();
        HideVoiceStatusBadge();
    }

    void RefreshVoiceStatusBadge()
    {
        EnsureVoiceStatusBadge();
        if (voiceBadgeRoot == null)
            return;

        PlaceVoiceControlAboveDrawingPanel(voiceBadgeRoot, new Vector2(VoiceBadgeWidth, VoiceBadgeHeight), VoiceControlGap);

        if (!ShouldShowVoiceStatusBadge())
        {
            HideVoiceStatusBadge();
            return;
        }

        voiceBadgeRoot.gameObject.SetActive(true);

        var state = ResolveVoiceBadgeState();
        string detail = ResolveVoiceBadgeDetail(state);
        Color color = ResolveVoiceBadgeColor(state);

        if (voiceBadgeStateLabel != null)
            voiceBadgeStateLabel.text = ResolveVoiceBadgeTitle(state);
        if (voiceBadgeDetailLabel != null)
            voiceBadgeDetailLabel.text = detail;

        ApplyVoiceBadgeVisual(state, color);
        RefreshVoiceFallback();
    }

    void BeginVoiceBadgeHold()
    {
        if (PauseMenuController.IsPaused)
            return;

        if (speechUnlocked || voiceUnlockRecognizer == null)
            return;

        if (voiceUnlockRecognizer.IsListening)
            return;

        voiceOneShotFeedback = "";
        lastVoiceBadgeListenStartedAt = Time.unscaledTime;
        voiceUnlockRecognizer.StartListening(VoiceUnlockRecognizer.VoiceInputMode.WritingListenOnce);

        RefreshVoiceStatusBadge();
    }

    void EndVoiceBadgeHold()
    {
        if (voiceUnlockRecognizer == null || !voiceUnlockRecognizer.IsListening)
            return;

        voiceUnlockRecognizer.FinishListeningAttempt();
        RefreshVoiceStatusBadge();
    }

    void AddVoiceBadgeHoldHandlers(Button button)
    {
        EventTrigger trigger = button.gameObject.AddComponent<EventTrigger>();
        var down = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
        down.callback.AddListener(_ => BeginVoiceBadgeHold());
        trigger.triggers.Add(down);
        var up = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        up.callback.AddListener(_ => EndVoiceBadgeHold());
        trigger.triggers.Add(up);
        var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(_ => EndVoiceBadgeHold());
        trigger.triggers.Add(exit);
    }

    public void StartMobileVoiceUnlock()
    {
        BeginVoiceBadgeHold();
    }

    public void EndMobileVoiceUnlock() => EndVoiceBadgeHold();

    bool ShouldShowVoiceStatusBadge()
    {
        if (PauseMenuController.IsPaused)
            return false;

        if (!useSpellLessonSlice || !requireSpeechUnlock || drawController == null || !drawController.canDraw)
            return false;

        if (!speechUnlocked)
            return true;

        return speechUnlockedAt >= 0f && Time.unscaledTime - speechUnlockedAt <= 0.95f;
    }

    void HideVoiceStatusBadge()
    {
        if (voiceBadgeRoot != null)
        {
            voiceBadgeRoot.localScale = Vector3.one;
            voiceBadgeRoot.gameObject.SetActive(false);
        }
    }

    VoiceUnlockRecognizer.VoiceDisplayState ResolveVoiceBadgeState()
    {
        if (voiceUnlockRecognizer == null)
            return VoiceUnlockRecognizer.VoiceDisplayState.Unavailable;

        return voiceUnlockRecognizer.CurrentDisplayState;
    }

    string ResolveVoiceBadgeTitle(VoiceUnlockRecognizer.VoiceDisplayState state)
    {
        switch (state)
        {
            case VoiceUnlockRecognizer.VoiceDisplayState.Listening:
                return "LISTENING";
            case VoiceUnlockRecognizer.VoiceDisplayState.Heard:
                return "HEARD";
            case VoiceUnlockRecognizer.VoiceDisplayState.Fallback:
                return "FALLBACK";
            case VoiceUnlockRecognizer.VoiceDisplayState.Unavailable:
                return "UNAVAILABLE";
            case VoiceUnlockRecognizer.VoiceDisplayState.PermissionDenied:
                return "PERMISSION";
            case VoiceUnlockRecognizer.VoiceDisplayState.Error:
                return "TRY AGAIN";
            default:
                return "IDLE";
        }
    }

    string ResolveVoiceBadgeDetail(VoiceUnlockRecognizer.VoiceDisplayState state)
    {
        if (voiceUnlockRecognizer == null)
            return CanUseEditorSpeechFallback()
                ? $"Press {editorSpeechKey} to continue."
                : "Voice input is off here.";

        switch (state)
        {
            case VoiceUnlockRecognizer.VoiceDisplayState.Listening:
                return choosingForgeSpell
                    ? $"Say any unlocked {VoiceSelectionNoun()}."
                    : $"Say \"{targetWord}\".";
            case VoiceUnlockRecognizer.VoiceDisplayState.Heard:
                return string.IsNullOrWhiteSpace(voiceUnlockRecognizer.LastRecognizedText)
                    ? voiceUnlockRecognizer.StatusMessage
                    : $"Heard {voiceUnlockRecognizer.LastRecognizedText}.";
            case VoiceUnlockRecognizer.VoiceDisplayState.Fallback:
                return string.IsNullOrWhiteSpace(voiceUnlockRecognizer.ActivityDetail)
                    ? $"Editor key: {editorSpeechKey}"
                    : voiceUnlockRecognizer.ActivityDetail;
            case VoiceUnlockRecognizer.VoiceDisplayState.Unavailable:
                return CanUseEditorSpeechFallback()
                    ? $"Voice unavailable. Press {editorSpeechKey}."
                    : "Voice unavailable on this platform.";
            case VoiceUnlockRecognizer.VoiceDisplayState.PermissionDenied:
            case VoiceUnlockRecognizer.VoiceDisplayState.Error:
                return voiceUnlockRecognizer.StatusMessage;
            default:
                return string.IsNullOrWhiteSpace(voiceUnlockRecognizer.StatusMessage)
                    ? "Voice unlock idle."
                    : voiceUnlockRecognizer.StatusMessage;
        }
    }

    Color ResolveVoiceBadgeColor(VoiceUnlockRecognizer.VoiceDisplayState state)
    {
        switch (state)
        {
            case VoiceUnlockRecognizer.VoiceDisplayState.Listening:
                return GameUiTheme.Accent;
            case VoiceUnlockRecognizer.VoiceDisplayState.Heard:
                return GameUiTheme.Gold;
            case VoiceUnlockRecognizer.VoiceDisplayState.Fallback:
                return new Color(0.95f, 0.72f, 0.32f, 1f);
            case VoiceUnlockRecognizer.VoiceDisplayState.Unavailable:
            case VoiceUnlockRecognizer.VoiceDisplayState.PermissionDenied:
            case VoiceUnlockRecognizer.VoiceDisplayState.Error:
                return GameUiTheme.Danger;
            default:
                return GameUiTheme.TextMuted;
        }
    }

    void ApplyVoiceBadgeVisual(VoiceUnlockRecognizer.VoiceDisplayState state, Color color)
    {
        if (voiceBadgePanel == null || voiceBadgeDot == null)
            return;

        float pulse = 1f;
        if (state == VoiceUnlockRecognizer.VoiceDisplayState.Listening)
            pulse = 1f + Mathf.Sin(Time.unscaledTime * 7.5f) * 0.12f;
        else if (state == VoiceUnlockRecognizer.VoiceDisplayState.Heard)
            pulse = 1.06f + Mathf.Sin(Time.unscaledTime * 18f) * 0.07f;

        voiceBadgeRoot.localScale = Vector3.one * pulse;
        voiceBadgePanel.color = new Color(
            GameUiTheme.PanelRaised.r,
            GameUiTheme.PanelRaised.g,
            GameUiTheme.PanelRaised.b,
            state == VoiceUnlockRecognizer.VoiceDisplayState.Heard ? 0.97f : 0.92f);

        BrushStrokeStyle.ApplyDot(voiceBadgeDot, new Color(color.r, color.g, color.b, 0.95f));
        voiceBadgeDot.rectTransform.sizeDelta = state == VoiceUnlockRecognizer.VoiceDisplayState.Heard
            ? new Vector2(26f, 26f)
            : new Vector2(22f, 22f);

        if (voiceBadgeStateLabel != null)
            voiceBadgeStateLabel.color = color;
        if (voiceBadgeDetailLabel != null)
            voiceBadgeDetailLabel.color = state == VoiceUnlockRecognizer.VoiceDisplayState.Unavailable
                ? new Color(GameUiTheme.TextMuted.r, GameUiTheme.TextMuted.g, GameUiTheme.TextMuted.b, 0.95f)
                : GameUiTheme.Text;
    }

    void RefreshVoiceFallback()
    {
        if (speechUnlocked || voiceUnlockRecognizer == null || !ShouldOfferVoiceFallback())
        {
            HideVoiceFallback();
            return;
        }

        EnsureVoiceFallback();
        if (voiceFallbackRoot != null)
            voiceFallbackRoot.gameObject.SetActive(true);
    }

    bool ShouldOfferVoiceFallback()
    {
        if (!voiceUnlockRecognizer.IsAvailable)
            return true;

        return voiceUnlockRecognizer.CurrentDisplayState == VoiceUnlockRecognizer.VoiceDisplayState.PermissionDenied ||
               voiceUnlockRecognizer.ConsecutiveFailures >= 2;
    }

    void EnsureVoiceFallback()
    {
        if (drawController == null || drawController.drawingPanel == null)
            return;

        List<string> words = new List<string>();
        if (choosingForgeSpell && spellRegistry != null)
        {
            words.AddRange(GetForgeFallbackOptions());
        }
        else if (!string.IsNullOrEmpty(targetWord))
        {
            words.Add(targetWord);
        }

        string signature = $"{requestedForgeMode}|{string.Join("|", words)}";
        if (voiceFallbackRoot != null && voiceFallbackSignature == signature)
        {
            PlaceVoiceControlAboveDrawingPanel(
                voiceFallbackRoot,
                new Vector2(VoiceBadgeWidth, Mathf.Max(80f, words.Count * VoiceFallbackButtonHeight + 24f)),
                VoiceBadgeHeight + 32f);
            return;
        }

        if (voiceFallbackRoot != null)
            Destroy(voiceFallbackRoot.gameObject);
        voiceFallbackButtons.Clear();
        voiceFallbackSignature = signature;

        var rootGo = new GameObject("VoiceFallback", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        voiceFallbackRoot = rootGo.GetComponent<RectTransform>();
        PlaceVoiceControlAboveDrawingPanel(
            voiceFallbackRoot,
            new Vector2(VoiceBadgeWidth, Mathf.Max(80f, words.Count * VoiceFallbackButtonHeight + 24f)),
            VoiceBadgeHeight + 32f);
        GameUiTheme.StylePanel(rootGo);

        var layout = rootGo.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 12, 12);
        layout.spacing = 8f;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = true;

        foreach (string wordValue in words)
        {
            string word = wordValue;
            var buttonGo = new GameObject($"Fallback_{word}", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonGo.transform.SetParent(rootGo.transform, false);
            buttonGo.GetComponent<LayoutElement>().preferredHeight = VoiceFallbackButtonHeight;
            var button = buttonGo.GetComponent<Button>();
            button.onClick.AddListener(() => HandleVoiceFallbackSelected(word));
            GameUiTheme.StyleButton(button, GameUiTheme.ButtonRole.Secondary);

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(buttonGo.transform, false);
            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;
            var label = labelGo.GetComponent<TextMeshProUGUI>();
            label.text = word;
            label.alignment = TextAlignmentOptions.Center;
            label.verticalAlignment = VerticalAlignmentOptions.Middle;
            GameUiTheme.StyleText(label, 22f, true);
            voiceFallbackButtons.Add(button);
        }
    }

    RectTransform VoiceControlParent()
    {
        if (drawController == null || drawController.drawingPanel == null)
            return null;

        Transform parent = drawController.drawingPanel.parent;
        while (parent != null)
        {
            if (parent is RectTransform rectParent)
                return rectParent;

            parent = parent.parent;
        }

        Canvas canvas = drawController.drawingPanel.GetComponentInParent<Canvas>();
        return canvas != null ? canvas.transform as RectTransform : null;
    }

    void PlaceVoiceControlAboveDrawingPanel(RectTransform rect, Vector2 size, float bottomOffset)
    {
        if (rect == null || drawController == null || drawController.drawingPanel == null)
            return;

        RectTransform parent = VoiceControlParent();
        if (parent == null)
            return;

        if (rect.parent != parent)
            rect.SetParent(parent, false);

        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.sizeDelta = size;
        float stackedOffset = Mathf.Max(0f, bottomOffset - VoiceControlGap);
        float y = drawController.drawingPanel.anchoredPosition.y +
                  drawController.drawingPanel.rect.yMax -
                  size.y * 0.5f -
                  18f -
                  stackedOffset;
        float x = drawController.drawingPanel.anchoredPosition.x +
                  drawController.drawingPanel.rect.xMin +
                  24f;
        rect.anchoredPosition = new Vector2(x, y);
    }

    void HandleVoiceFallbackSelected(string word)
    {
        voiceUnlockRecognizer?.NotifyFallbackTriggered($"Selected: {word}");
        if (choosingForgeSpell)
            SetForgeTarget(word);
        else if (string.Equals(word, targetWord, System.StringComparison.OrdinalIgnoreCase))
            UnlockSpeechGate();
        HideVoiceFallback();
    }

    void HideVoiceFallback()
    {
        if (voiceFallbackRoot != null)
            voiceFallbackRoot.gameObject.SetActive(false);
    }
}
