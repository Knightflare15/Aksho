using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Kid-friendly target selection for spell combat. If an enemy or dormant spell
/// pillar is visible, the game quietly picks the best one near the center of the
/// screen and marks it.
/// </summary>
public sealed class PlayerAimAssist : MonoBehaviour
{
    [Header("Selection")]
    [Range(0.05f, 0.75f)] public float maxViewportCenterDistance = 0.48f;
    [Range(0f, 0.3f)] public float screenEdgePadding = 0.04f;
    [Min(0f)] public float switchCooldownSeconds = 0.18f;
    [Range(0.1f, 1f)] public float switchScoreRatio = 0.7f;
    public bool requireLineOfSight;

    [Header("Marker")]
    public Vector2 markerScreenOffset = new Vector2(0f, 52f);
    public Color markerColour = new Color(1f, 0.82f, 0.2f, 1f);
    public Color hintColour = new Color(1f, 0.92f, 0.55f, 0.96f);
    [Min(0.5f)] public float hintSeconds = 3.2f;

    private Canvas canvas;
    private RectTransform markerRoot;
    private TextMeshProUGUI markerLabel;
    private RectTransform hintPanel;
    private TextMeshProUGUI hintLabel;
    private SpellTarget selectedTarget;
    private SpellPillarObjective selectedPillar;
    private EnemyBestiaryIdentity selectedIdentity;
    private float nextSwitchAt;
    private float selectedScore = float.MaxValue;
    private float hintUntil;

    public SpellTarget SelectedTarget => IsUsable(selectedTarget) ? selectedTarget : null;
    public SpellPillarObjective SelectedPillar => IsUsable(selectedPillar) ? selectedPillar : null;
    public EnemyBestiaryIdentity SelectedIdentity => SelectedTarget != null ? selectedIdentity : null;
    public string SelectedWeaknessWord => SelectedIdentity != null ? selectedIdentity.WeaknessWord : "";

    void Awake()
    {
        EnsureUi();
    }

    void LateUpdate()
    {
        RefreshSelection();
        RefreshMarker();
        RefreshHint();
    }

    public bool TryGetSelectedTarget(out SpellTarget target)
    {
        target = SelectedTarget;
        return target != null;
    }

    public bool TryGetSelectedPillar(out SpellPillarObjective pillar)
    {
        pillar = SelectedPillar;
        return pillar != null;
    }

    public bool TryGetSelectedIdentity(out EnemyBestiaryIdentity identity)
    {
        identity = SelectedIdentity;
        return identity != null;
    }

    public void ShowWrongSpellHint(SpellTarget target, string usedSpell)
    {
        EnemyBestiaryIdentity identity = target != null
            ? target.GetComponent<EnemyBestiaryIdentity>()
            : SelectedIdentity;
        string weakness = identity != null ? identity.WeaknessWord : "";
        if (string.IsNullOrEmpty(weakness))
            return;

        string name = identity != null ? identity.DisplayName : "this enemy";
        string used = SpellRegistry.NormalizeWord(usedSpell);
        string prefix = string.IsNullOrEmpty(used) ? "Try" : $"{used} is weak here. Try";
        SetHint($"{prefix} {weakness} on {name}.");
    }

    public void SetHint(string message)
    {
        if (hintLabel == null)
            EnsureUi();

        hintLabel.text = message ?? "";
        hintUntil = Time.unscaledTime + hintSeconds;
    }

    void RefreshSelection()
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            ClearSelection();
            return;
        }

        SpellTarget bestTarget = null;
        SpellPillarObjective bestPillar = null;
        EnemyBestiaryIdentity bestIdentity = null;
        float bestScore = float.MaxValue;

        foreach (SpellTarget target in FindObjectsByType<SpellTarget>())
        {
            if (!IsUsable(target))
                continue;

            if (!TryScoreTarget(camera, target, out float score))
                continue;

            if (score < bestScore)
            {
                bestScore = score;
                bestTarget = target;
                bestPillar = null;
                bestIdentity = target.GetComponent<EnemyBestiaryIdentity>();
            }
        }

        foreach (SpellPillarObjective pillar in FindObjectsByType<SpellPillarObjective>())
        {
            if (!IsUsable(pillar))
                continue;

            if (!TryScoreTarget(camera, pillar, out float score))
                continue;

            if (score < bestScore)
            {
                bestScore = score;
                bestTarget = null;
                bestPillar = pillar;
                bestIdentity = null;
            }
        }

        if (bestTarget == null && bestPillar == null)
        {
            ClearSelection();
            return;
        }

        float currentScore = float.MaxValue;
        bool currentStillUsable =
            IsUsable(selectedTarget) && TryScoreTarget(camera, selectedTarget, out currentScore) ||
            IsUsable(selectedPillar) && TryScoreTarget(camera, selectedPillar, out currentScore);
        if (!currentStillUsable)
        {
            Select(bestTarget, bestPillar, bestIdentity, bestScore);
            return;
        }

        selectedScore = currentScore;
        if (bestTarget == selectedTarget && bestPillar == selectedPillar)
            return;

        if (Time.unscaledTime < nextSwitchAt)
            return;

        if (bestScore <= currentScore * Mathf.Clamp01(switchScoreRatio))
            Select(bestTarget, bestPillar, bestIdentity, bestScore);
    }

    bool TryScoreTarget(Camera camera, SpellTarget target, out float score)
    {
        return TryScoreTarget(camera, target, target.GetAimPoint(), out score);
    }

    bool TryScoreTarget(Camera camera, SpellPillarObjective pillar, out float score)
    {
        return TryScoreTarget(camera, pillar, pillar.GetAimPoint(), out score);
    }

    bool TryScoreTarget(Camera camera, Component target, Vector3 aimPoint, out float score)
    {
        score = float.MaxValue;
        Vector3 viewport = camera.WorldToViewportPoint(aimPoint);
        if (viewport.z <= 0f)
            return false;

        float padding = Mathf.Clamp(screenEdgePadding, 0f, 0.45f);
        if (viewport.x < padding || viewport.x > 1f - padding ||
            viewport.y < padding || viewport.y > 1f - padding)
            return false;

        Vector2 fromCenter = new Vector2(viewport.x - 0.5f, viewport.y - 0.5f);
        float centerDistance = fromCenter.magnitude;
        if (centerDistance > maxViewportCenterDistance)
            return false;

        if (requireLineOfSight && IsOccluded(camera, target, aimPoint))
            return false;

        score = centerDistance * 10f + viewport.z * 0.015f;
        return true;
    }

    bool IsOccluded(Camera camera, Component target, Vector3 aimPoint)
    {
        Vector3 origin = camera.transform.position;
        Vector3 direction = aimPoint - origin;
        if (direction.sqrMagnitude < 0.001f)
            return false;

        if (!Physics.Raycast(origin, direction.normalized, out RaycastHit hit, direction.magnitude, ~0, QueryTriggerInteraction.Ignore))
            return false;

        if (hit.collider == null)
            return false;

        SpellTarget spellTarget = target as SpellTarget;
        if (spellTarget != null)
            return hit.collider.GetComponentInParent<SpellTarget>() != spellTarget;

        SpellPillarObjective pillar = target as SpellPillarObjective;
        if (pillar != null)
            return hit.collider.GetComponentInParent<SpellPillarObjective>() != pillar;

        return true;
    }

    void Select(SpellTarget target, SpellPillarObjective pillar, EnemyBestiaryIdentity identity, float score)
    {
        selectedTarget = target;
        selectedPillar = pillar;
        selectedIdentity = target != null
            ? (identity != null ? identity : target.GetComponent<EnemyBestiaryIdentity>())
            : null;
        selectedScore = score;
        nextSwitchAt = Time.unscaledTime + switchCooldownSeconds;
    }

    void ClearSelection()
    {
        selectedTarget = null;
        selectedPillar = null;
        selectedIdentity = null;
        selectedScore = float.MaxValue;
    }

    void RefreshMarker()
    {
        EnsureUi();
        if (markerRoot == null)
            return;

        Camera camera = Camera.main;
        SpellTarget target = SelectedTarget;
        SpellPillarObjective pillar = target == null ? SelectedPillar : null;
        if (camera == null || (target == null && pillar == null))
        {
            markerRoot.gameObject.SetActive(false);
            return;
        }

        Vector3 aimPoint = target != null ? target.GetAimPoint() : pillar.GetAimPoint();
        Vector3 screen = camera.WorldToScreenPoint(aimPoint);
        if (screen.z <= 0f)
        {
            markerRoot.gameObject.SetActive(false);
            return;
        }

        markerRoot.gameObject.SetActive(true);
        markerRoot.position = screen + new Vector3(markerScreenOffset.x, markerScreenOffset.y, 0f);
        float pulse = Mathf.Lerp(0.82f, 1.12f, (Mathf.Sin(Time.unscaledTime * 8f) + 1f) * 0.5f);
        markerRoot.localScale = Vector3.one * pulse;

        if (markerLabel != null)
            markerLabel.text = "V";
    }

    void RefreshHint()
    {
        EnsureUi();
        if (hintPanel == null)
            return;

        bool show = Time.unscaledTime <= hintUntil && !string.IsNullOrWhiteSpace(hintLabel?.text);
        hintPanel.gameObject.SetActive(show);
    }

    void EnsureUi()
    {
        if (canvas == null)
        {
            var root = new GameObject("AimAssistCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 380;

            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        if (markerRoot == null)
            BuildMarker(canvas.transform);
        if (hintPanel == null)
            BuildHint(canvas.transform);
    }

    void BuildMarker(Transform parent)
    {
        var go = new GameObject("SelectedEnemyMarker", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        markerRoot = go.GetComponent<RectTransform>();
        markerRoot.sizeDelta = new Vector2(132f, 92f);

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(go.transform, false);
        markerLabel = labelGo.GetComponent<TextMeshProUGUI>();
        markerLabel.font = TMP_Settings.defaultFontAsset;
        markerLabel.fontSize = 30f;
        markerLabel.fontStyle = FontStyles.Bold;
        markerLabel.alignment = TextAlignmentOptions.Center;
        markerLabel.color = markerColour;
        markerLabel.raycastTarget = false;
        markerLabel.enableAutoSizing = true;
        markerLabel.fontSizeMin = 20f;
        markerLabel.fontSizeMax = 34f;
        markerLabel.rectTransform.anchorMin = Vector2.zero;
        markerLabel.rectTransform.anchorMax = Vector2.one;
        markerLabel.rectTransform.offsetMin = Vector2.zero;
        markerLabel.rectTransform.offsetMax = Vector2.zero;

        go.SetActive(false);
    }

    void BuildHint(Transform parent)
    {
        var panel = new GameObject("SpellHint", typeof(RectTransform), typeof(Image), typeof(Outline));
        panel.transform.SetParent(parent, false);
        hintPanel = panel.GetComponent<RectTransform>();
        hintPanel.anchorMin = new Vector2(0.5f, 0f);
        hintPanel.anchorMax = new Vector2(0.5f, 0f);
        hintPanel.pivot = new Vector2(0.5f, 0f);
        hintPanel.anchoredPosition = new Vector2(0f, 222f);
        hintPanel.sizeDelta = new Vector2(640f, 76f);

        Image image = panel.GetComponent<Image>();
        image.color = new Color(0.07f, 0.11f, 0.18f, 0.92f);
        image.raycastTarget = false;

        Outline outline = panel.GetComponent<Outline>();
        outline.effectColor = hintColour;
        outline.effectDistance = new Vector2(2f, -2f);

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(panel.transform, false);
        hintLabel = labelGo.GetComponent<TextMeshProUGUI>();
        hintLabel.font = TMP_Settings.defaultFontAsset;
        hintLabel.fontSize = 25f;
        hintLabel.fontStyle = FontStyles.Bold;
        hintLabel.alignment = TextAlignmentOptions.Center;
        hintLabel.color = hintColour;
        hintLabel.raycastTarget = false;
        hintLabel.enableAutoSizing = true;
        hintLabel.fontSizeMin = 18f;
        hintLabel.fontSizeMax = 27f;
        hintLabel.textWrappingMode = TextWrappingModes.Normal;
        hintLabel.rectTransform.anchorMin = Vector2.zero;
        hintLabel.rectTransform.anchorMax = Vector2.one;
        hintLabel.rectTransform.offsetMin = new Vector2(16f, 6f);
        hintLabel.rectTransform.offsetMax = new Vector2(-16f, -6f);

        panel.SetActive(false);
    }

    static bool IsUsable(SpellTarget target)
    {
        return target != null && target.gameObject.activeInHierarchy && !target.IsDefeated;
    }

    static bool IsUsable(SpellPillarObjective pillar)
    {
        return pillar != null && pillar.gameObject.activeInHierarchy && pillar.IsTargetable;
    }
}
