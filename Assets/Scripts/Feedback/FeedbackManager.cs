using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class FeedbackManager : MonoBehaviour
{
    [Header("References")]
    public Transform cameraTransform;
    public RectTransform drawingPanel;
    public Image panelBackground;
    public AudioSource feedbackAudioSource;

    [Header("Screen Shake")]
    public float shakeBaseIntensity = 6f;
    public float shakeDuration = 0.35f;
    public float shakeDecaySpeed = 8f;

    [Header("Panel Wobble")]
    public float wobbleScalePunch = 0.08f;
    public float wobbleDuration = 0.4f;

    [Header("Colours")]
    public Color colourCorrect = new Color(0.2f, 0.95f, 0.3f);
    public Color colourWarm = new Color(1f, 0.85f, 0.1f);
    public Color colourWrong = new Color(1f, 0.35f, 0.2f);
    public Color colourNeutral = new Color(1f, 0.9725f, 0.8627f);
    public Color colourSuccess = new Color(1f, 0.92f, 0.3f);
    public Color colourGuide = new Color(0.5f, 0.85f, 1f, 1f);

    [Header("Audio")]
    public AudioClip guideOnTrackClip;
    public AudioClip guideDriftClip;
    public AudioClip guideRecoverClip;

    [Header("Haptics (Mobile)")]
    public bool hapticsEnabled = true;
    public int hapticShortMs = 40;
    public int hapticLongMs = 120;
    public float hapticMinimumInterval = 0.08f;

    [Header("Live Drawing Drift Feedback")]
    [Range(0f, 1f)] public float liveDriftShakeMultiplier = 0.18f;
    [Range(0f, 1f)] public float liveOffTrackShakeMultiplier = 0.45f;
    [Range(0f, 1f)] public float liveDriftHapticIntensity = 0.28f;
    [Range(0f, 1f)] public float liveOffTrackHapticIntensity = 0.72f;

    [Header("Testing Popups")]
    public bool largeFeedbackPopupsEnabled = true;
    public float largeFeedbackPopupDuration = 3.4f;
    public Vector2 largeFeedbackPopupSize = new Vector2(420f, 138f);
    public Vector2 largeFeedbackPopupOffset = new Vector2(-32f, -32f);
    public float largeFeedbackTitleSize = 24f;
    public float largeFeedbackBodySize = 16f;

    public enum Severity { None, Warm, Wrong, VeryWrong }
    public enum GuidanceState { Hidden, OnTrack, Drifting, OffTrack }

    Vector3 cameraOrigin;
    Vector3 panelOriginalScale;
    Color panelBaseColour;
    Coroutine shakeRoutine;
    Coroutine wobbleRoutine;
    Coroutine backgroundRoutine;
    Coroutine strokeRoutine;
    Coroutine delayedWobbleRoutine;
    Coroutine popupRoutine;
    Canvas popupCanvas;
    RectTransform popupRoot;
    CanvasGroup popupCanvasGroup;
    Image popupPanel;
    TextMeshProUGUI popupTitle;
    TextMeshProUGUI popupBody;
    readonly List<GameObject> flashingStrokes = new List<GameObject>();
    FeedbackCue activeCue = FeedbackCue.Guidance;
    float activeCueUntil = float.NegativeInfinity;
    float lastHapticAt = float.NegativeInfinity;
    IHapticsProvider hapticsProvider;

    void Awake()
    {
        hapticsProvider = HapticsProviderFactory.Create();
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
        if (cameraTransform != null)
            cameraOrigin = cameraTransform.localPosition;
        if (drawingPanel != null)
            panelOriginalScale = drawingPanel.localScale;
        if (panelBackground != null)
            panelBaseColour = panelBackground.color;
    }

    void OnDisable() => CancelAndRestoreFeedback();
    void OnDestroy() => CancelAndRestoreFeedback();

    public void SetHapticsProvider(IHapticsProvider provider) =>
        hapticsProvider = provider ?? new NoOpHapticsProvider();

    public void PlayWrongFeedback(Severity severity, List<GameObject> strokes)
    {
        if (severity == Severity.None)
            return;

        FeedbackCue cue = severity == Severity.Warm
            ? FeedbackCue.Warm
            : severity == Severity.Wrong ? FeedbackCue.Wrong : FeedbackCue.VeryWrong;
        if (!BeginCue(new FeedbackRequest(cue, strokes), 0.55f))
            return;

        float multiplier = severity == Severity.Warm ? 0.4f : severity == Severity.Wrong ? 1f : 1.8f;
        Color colour = severity == Severity.Warm ? colourWarm : colourWrong;
        TriggerShake(shakeBaseIntensity * multiplier);
        TriggerWobble(wobbleScalePunch * multiplier);
        StartStrokeFlash(strokes, colour, 0.5f);
        StartBackgroundFlash(colour, 0.3f);
        TriggerHaptic(severity == Severity.Warm ? hapticShortMs / 2 : severity == Severity.Wrong ? hapticShortMs : hapticLongMs,
            severity == Severity.Warm ? 0.55f : severity == Severity.Wrong ? 0.82f : 1f);
    }

    public void PlayCorrectFeedback(List<GameObject> strokes)
    {
        if (!BeginCue(new FeedbackRequest(FeedbackCue.Correct, strokes), 0.65f))
            return;
        StartStrokeFlash(strokes, colourCorrect, 0.6f);
        TriggerWobble(wobbleScalePunch * 0.5f);
        StartBackgroundFlash(colourCorrect, 0.25f);
        TriggerHaptic(hapticShortMs / 3, 0.35f);
    }

    public void PlaySuccessFeedback()
    {
        if (!BeginCue(new FeedbackRequest(FeedbackCue.Success), 0.9f))
            return;
        TriggerWobble(wobbleScalePunch * 2f);
        StartBackgroundFlash(colourSuccess, 0.6f);
        TriggerHaptic(hapticShortMs, 0.7f);
        delayedWobbleRoutine = StartCoroutine(DelayedWobble(0.25f, wobbleScalePunch * 1.2f));
    }

    public void PlayGuidanceFeedback(GuidanceState state, List<GameObject> strokes)
    {
        if (state == GuidanceState.Hidden || state == GuidanceState.OnTrack ||
            !BeginCue(new FeedbackRequest(FeedbackCue.Guidance, strokes), 0.22f))
            return;

        if (state == GuidanceState.Drifting)
        {
            StartStrokeFlash(strokes, colourWarm, 0.16f);
            StartBackgroundFlash(colourWarm, 0.08f);
            TriggerShake(shakeBaseIntensity * liveDriftShakeMultiplier);
            TriggerHaptic(hapticShortMs / 5, liveDriftHapticIntensity);
            PlayClip(guideDriftClip, 0.15f);
        }
        else
        {
            StartStrokeFlash(strokes, colourWrong, 0.2f);
            StartBackgroundFlash(colourWrong, 0.12f);
            TriggerShake(shakeBaseIntensity * liveOffTrackShakeMultiplier);
            TriggerHaptic(hapticShortMs / 2, liveOffTrackHapticIntensity);
            PlayClip(guideRecoverClip != null ? guideRecoverClip : guideDriftClip, 0.2f);
        }
    }

    public void ShowLargeFeedbackPopup(string title, string body, Color accent, float duration = -1f)
    {
        if (!largeFeedbackPopupsEnabled)
        {
            Debug.Log($"[Pronunciation] Large feedback popup blocked: FeedbackManager.largeFeedbackPopupsEnabled=false title='{title}' body='{body}'");
            return;
        }

        EnsurePopup();
        if (popupRoot == null || popupCanvasGroup == null)
        {
            Debug.LogWarning($"[Pronunciation] Large feedback popup blocked: popup UI missing after EnsurePopup. root={popupRoot != null} canvasGroup={popupCanvasGroup != null} title='{title}' body='{body}'");
            return;
        }

        popupTitle.text = string.IsNullOrWhiteSpace(title) ? "Feedback" : title.Trim();
        popupBody.text = string.IsNullOrWhiteSpace(body) ? "" : body.Trim();
        popupTitle.color = Color.Lerp(accent, Color.white, 0.18f);
        popupBody.color = GameUiTheme.Text;
        popupPanel.color = new Color(0.035f, 0.045f, 0.07f, 0.96f);
        PositionPopupRoot();

        var outline = popupPanel.GetComponent<Outline>() ?? popupPanel.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(accent.r, accent.g, accent.b, 0.95f);
        outline.effectDistance = new Vector2(4f, -4f);

        popupRoot.gameObject.SetActive(true);
        RestartRoutine(ref popupRoutine, LargePopupRoutine(duration > 0f ? duration : largeFeedbackPopupDuration));
        Debug.Log($"[Pronunciation] Large feedback popup shown title='{popupTitle.text}' body='{popupBody.text}' duration={(duration > 0f ? duration : largeFeedbackPopupDuration):0.00}s");
    }

    public void ResetStrokeColours(List<GameObject> strokes)
    {
        if (strokes == null) return;
        foreach (var go in strokes) SetGoColour(go, colourNeutral);
    }

    bool BeginCue(FeedbackRequest request, float duration)
    {
        if (Time.unscaledTime < activeCueUntil && request.Cue < activeCue)
            return false;
        CancelVisualFeedback();
        activeCue = request.Cue;
        activeCueUntil = Time.unscaledTime + Mathf.Max(0f, duration);
        return true;
    }

    void TriggerShake(float intensity)
    {
        intensity *= AccessibilitySettings.ShakeIntensity;
        if (cameraTransform == null || intensity <= 0f || shakeDuration <= 0f) return;
        cameraOrigin = cameraTransform.localPosition;
        RestartRoutine(ref shakeRoutine, ShakeRoutine(intensity));
    }

    IEnumerator ShakeRoutine(float intensity)
    {
        float elapsed = 0f;
        while (elapsed < shakeDuration && cameraTransform != null)
        {
            float remaining = 1f - elapsed / shakeDuration;
            cameraTransform.localPosition = cameraOrigin + (Vector3)Random.insideUnitCircle * intensity * remaining;
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        if (cameraTransform != null) cameraTransform.localPosition = cameraOrigin;
        shakeRoutine = null;
    }

    void TriggerWobble(float punch)
    {
        if (drawingPanel == null || punch <= 0f || wobbleDuration <= 0f) return;
        RestartRoutine(ref wobbleRoutine, WobbleRoutine(punch));
    }

    IEnumerator WobbleRoutine(float punch)
    {
        float elapsed = 0f;
        while (elapsed < wobbleDuration && drawingPanel != null)
        {
            float scale = 1f + punch * Mathf.Sin(elapsed / wobbleDuration * Mathf.PI);
            drawingPanel.localScale = panelOriginalScale * scale;
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        if (drawingPanel != null) drawingPanel.localScale = panelOriginalScale;
        wobbleRoutine = null;
    }

    IEnumerator DelayedWobble(float delay, float punch)
    {
        yield return new WaitForSecondsRealtime(delay);
        TriggerWobble(punch);
        delayedWobbleRoutine = null;
    }

    void StartStrokeFlash(List<GameObject> strokes, Color colour, float duration)
    {
        StopRoutine(ref strokeRoutine);
        RestoreFlashingStrokes();
        if (strokes == null || strokes.Count == 0) return;
        strokeRoutine = StartCoroutine(FlashStrokes(strokes, colour, duration));
    }

    IEnumerator FlashStrokes(List<GameObject> strokes, Color flashColour, float duration)
    {
        foreach (var go in strokes)
            if (go != null)
                flashingStrokes.Add(go);
        foreach (var go in flashingStrokes) SetGoColour(go, flashColour);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            Color colour = Color.Lerp(flashColour, colourNeutral, duration <= 0f ? 1f : elapsed / duration);
            foreach (var go in flashingStrokes) SetGoColour(go, colour);
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        RestoreFlashingStrokes();
        strokeRoutine = null;
    }

    void StartBackgroundFlash(Color colour, float duration)
    {
        if (panelBackground == null) return;
        RestartRoutine(ref backgroundRoutine, FlashBackground(colour, duration));
    }

    IEnumerator FlashBackground(Color flashColour, float duration)
    {
        Color target = new Color(flashColour.r, flashColour.g, flashColour.b, 0.25f);
        panelBackground.color = target;
        float elapsed = 0f;
        while (elapsed < duration && panelBackground != null)
        {
            panelBackground.color = Color.Lerp(target, panelBaseColour, duration <= 0f ? 1f : elapsed / duration);
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        if (panelBackground != null) panelBackground.color = panelBaseColour;
        backgroundRoutine = null;
    }

    void TriggerHaptic(int milliseconds, float intensity = 1f)
    {
        if (!hapticsEnabled || !AccessibilitySettings.VibrationEnabled || milliseconds <= 0 ||
            Time.unscaledTime - lastHapticAt < hapticMinimumInterval)
            return;
        lastHapticAt = Time.unscaledTime;
        hapticsProvider?.Vibrate(milliseconds, intensity);
    }

    void CancelVisualFeedback()
    {
        bool restoreCamera = shakeRoutine != null;
        bool restorePanel = wobbleRoutine != null || delayedWobbleRoutine != null;
        bool restoreBackground = backgroundRoutine != null;
        StopRoutine(ref shakeRoutine);
        StopRoutine(ref wobbleRoutine);
        StopRoutine(ref backgroundRoutine);
        StopRoutine(ref strokeRoutine);
        StopRoutine(ref delayedWobbleRoutine);
        if (restoreCamera && cameraTransform != null) cameraTransform.localPosition = cameraOrigin;
        if (restorePanel && drawingPanel != null) drawingPanel.localScale = panelOriginalScale;
        if (restoreBackground && panelBackground != null) panelBackground.color = panelBaseColour;
        RestoreFlashingStrokes();
    }

    void CancelAndRestoreFeedback()
    {
        CancelVisualFeedback();
        HideLargeFeedbackPopup();
        activeCue = FeedbackCue.Guidance;
        activeCueUntil = float.NegativeInfinity;
    }

    void EnsurePopup()
    {
        if (popupRoot != null)
            return;

        Transform parent = null;
        Canvas ownerCanvas = drawingPanel != null ? drawingPanel.GetComponentInParent<Canvas>() : null;
        if (ownerCanvas != null)
        {
            parent = ownerCanvas.transform;
        }
        else
        {
            var canvasGo = new GameObject("LargeFeedbackPopupCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            popupCanvas = canvasGo.GetComponent<Canvas>();
            popupCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            popupCanvas.sortingOrder = 5000;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            parent = canvasGo.transform;
        }

        var root = new GameObject("LargeFeedbackPopup", typeof(RectTransform), typeof(CanvasGroup));
        root.transform.SetParent(parent, false);
        popupRoot = root.GetComponent<RectTransform>();
        PositionPopupRoot();
        popupCanvasGroup = root.GetComponent<CanvasGroup>();
        popupCanvasGroup.blocksRaycasts = false;
        popupCanvasGroup.interactable = false;

        var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(VerticalLayoutGroup));
        panelGo.transform.SetParent(root.transform, false);
        var panelRt = panelGo.GetComponent<RectTransform>();
        panelRt.anchorMin = Vector2.zero;
        panelRt.anchorMax = Vector2.one;
        panelRt.offsetMin = Vector2.zero;
        panelRt.offsetMax = Vector2.zero;
        popupPanel = panelGo.GetComponent<Image>();
        popupPanel.raycastTarget = false;
        var layout = panelGo.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 14, 14);
        layout.spacing = 5f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        float titleSize = Mathf.Clamp(largeFeedbackTitleSize, 18f, 26f);
        float bodySize = Mathf.Clamp(largeFeedbackBodySize, 13f, 18f);
        popupTitle = MakePopupText(panelGo.transform, "Title", titleSize, FontStyles.Bold, 30f);
        popupBody = MakePopupText(panelGo.transform, "Body", bodySize, FontStyles.Bold, 66f);
        root.SetActive(false);
    }

    void PositionPopupRoot()
    {
        if (popupRoot == null)
            return;

        popupRoot.anchorMin = Vector2.one;
        popupRoot.anchorMax = Vector2.one;
        popupRoot.pivot = Vector2.one;
        popupRoot.anchoredPosition = largeFeedbackPopupOffset;
        popupRoot.sizeDelta = new Vector2(
            Mathf.Clamp(largeFeedbackPopupSize.x, 300f, 620f),
            Mathf.Clamp(largeFeedbackPopupSize.y, 108f, 240f));
    }

    TextMeshProUGUI MakePopupText(Transform parent, string name, float size, FontStyles style, float preferredHeight)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement), typeof(Shadow));
        go.transform.SetParent(parent, false);
        var label = go.GetComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.Left;
        label.verticalAlignment = VerticalAlignmentOptions.Middle;
        label.fontSize = size;
        label.fontStyle = style;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.enableAutoSizing = true;
        label.fontSizeMax = size;
        label.fontSizeMin = Mathf.Max(12f, size * 0.52f);
        label.characterSpacing = 0f;
        label.raycastTarget = false;
        var layout = go.GetComponent<LayoutElement>();
        layout.preferredHeight = preferredHeight;
        var shadow = go.GetComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
        shadow.effectDistance = new Vector2(2f, -2f);
        return label;
    }

    IEnumerator LargePopupRoutine(float duration)
    {
        float fadeIn = 0.12f;
        float fadeOut = 0.24f;
        float elapsed = 0f;

        while (elapsed < fadeIn)
        {
            SetPopupVisual(elapsed / fadeIn);
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        SetPopupVisual(1f);
        yield return new WaitForSecondsRealtime(Mathf.Max(0.05f, duration - fadeIn - fadeOut));

        elapsed = 0f;
        while (elapsed < fadeOut)
        {
            SetPopupVisual(1f - elapsed / fadeOut);
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (popupCanvasGroup != null)
            popupCanvasGroup.alpha = 0f;
        if (popupRoot != null)
        {
            popupRoot.localScale = Vector3.one;
            popupRoot.gameObject.SetActive(false);
        }
        popupRoutine = null;
    }

    void SetPopupVisual(float visible)
    {
        if (popupCanvasGroup == null || popupRoot == null)
            return;

        visible = Mathf.Clamp01(visible);
        popupCanvasGroup.alpha = visible;
        float scale = Mathf.Lerp(0.92f, 1f, Mathf.SmoothStep(0f, 1f, visible));
        popupRoot.localScale = Vector3.one * scale;
    }

    void HideLargeFeedbackPopup()
    {
        StopRoutine(ref popupRoutine);
        if (popupCanvasGroup != null)
            popupCanvasGroup.alpha = 0f;
        if (popupRoot != null)
        {
            popupRoot.localScale = Vector3.one;
            popupRoot.gameObject.SetActive(false);
        }
    }

    void RestoreFlashingStrokes()
    {
        foreach (var go in flashingStrokes) SetGoColour(go, colourNeutral);
        flashingStrokes.Clear();
    }

    void RestartRoutine(ref Coroutine handle, IEnumerator routine)
    {
        StopRoutine(ref handle);
        handle = StartCoroutine(routine);
    }

    void StopRoutine(ref Coroutine routine)
    {
        if (routine == null) return;
        StopCoroutine(routine);
        routine = null;
    }

    static void SetGoColour(GameObject go, Color colour)
    {
        if (go == null) return;
        var image = go.GetComponent<Image>();
        if (image != null) { image.color = colour; return; }
        var renderer = go.GetComponent<SpriteRenderer>();
        if (renderer != null) renderer.color = colour;
    }

    void PlayClip(AudioClip clip, float volumeScale)
    {
        if (feedbackAudioSource != null && clip != null)
            feedbackAudioSource.PlayOneShot(clip, Mathf.Clamp01(volumeScale));
    }
}
