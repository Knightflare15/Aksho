using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class SpellCombatHud : MonoBehaviour
{
    void BuildPhoneticsPanel(Transform parent)
    {
        var panel = new GameObject("PhoneticsPanel", typeof(RectTransform), typeof(Image), typeof(Outline));
        panel.transform.SetParent(parent, false);
        _phoneticsPanel = panel.GetComponent<RectTransform>();
        _phoneticsPanel.anchorMin = new Vector2(1f, 0.5f);
        _phoneticsPanel.anchorMax = new Vector2(1f, 0.5f);
        _phoneticsPanel.pivot = new Vector2(1f, 0.5f);
        _phoneticsPanel.anchoredPosition = new Vector2(-34f, 150f);
        _phoneticsPanel.sizeDelta = new Vector2(360f, 206f);

        GameUiTheme.StyleHudPanel(_phoneticsPanel, 0.9f);

        _phoneticsLabel = MakeLabel("Phonetics", panel.transform, new Vector2(16f, -14f), 19f, FontStyles.Bold, GameUiTheme.Text);
        if (_phoneticsLabel != null)
        {
            _phoneticsLabel.alignment = TextAlignmentOptions.TopLeft;
            _phoneticsLabel.textWrappingMode = TextWrappingModes.Normal;
            _phoneticsLabel.overflowMode = TextOverflowModes.Ellipsis;
            _phoneticsLabel.rectTransform.anchorMin = new Vector2(0f, 0f);
            _phoneticsLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            _phoneticsLabel.rectTransform.pivot = new Vector2(0f, 1f);
            _phoneticsLabel.rectTransform.offsetMin = new Vector2(16f, 12f);
            _phoneticsLabel.rectTransform.offsetMax = new Vector2(-16f, -12f);
        }
    }

    void RefreshPhoneticsHud(bool showSidePanel)
    {
        if (_phoneticsPanel == null)
            return;

        bool visible = showSidePanel &&
                       _phoneticDisplayState != null &&
                       _phoneticDisplayState.IsFeedbackVisible(Time.unscaledTime);
        _phoneticsPanel.gameObject.SetActive(visible);
        if (!visible || _phoneticsLabel == null)
            return;

        _phoneticsLabel.text = _phoneticDisplayState.BuildHudText();
    }

    void BuildCombatWordsPanel(Transform parent)
    {
        var panel = new GameObject("CombatWordsPanel", typeof(RectTransform), typeof(Image), typeof(Outline));
        panel.transform.SetParent(parent, false);
        _combatWordsPanel = panel.GetComponent<RectTransform>();
        _combatWordsPanel.anchorMin = new Vector2(0f, 1f);
        _combatWordsPanel.anchorMax = new Vector2(0f, 1f);
        _combatWordsPanel.pivot = new Vector2(0f, 1f);
        _combatWordsPanel.anchoredPosition = new Vector2(34f, -258f);
        _combatWordsPanel.sizeDelta = new Vector2(500f, 286f);
        GameUiTheme.StyleHudPanel(_combatWordsPanel, 0.92f);

        _combatWordsLabel = MakeLabel(
            "CombatWords",
            panel.transform,
            new Vector2(16f, -14f),
            18f,
            FontStyles.Bold,
            GameUiTheme.Text);
        if (_combatWordsLabel != null)
        {
            _combatWordsLabel.alignment = TextAlignmentOptions.TopLeft;
            _combatWordsLabel.textWrappingMode = TextWrappingModes.Normal;
            _combatWordsLabel.overflowMode = TextOverflowModes.Ellipsis;
            _combatWordsLabel.enableAutoSizing = true;
            _combatWordsLabel.fontSizeMin = 13f;
            _combatWordsLabel.fontSizeMax = 18f;
            _combatWordsLabel.rectTransform.anchorMin = Vector2.zero;
            _combatWordsLabel.rectTransform.anchorMax = Vector2.one;
            _combatWordsLabel.rectTransform.pivot = new Vector2(0f, 1f);
            _combatWordsLabel.rectTransform.offsetMin = new Vector2(16f, 12f);
            _combatWordsLabel.rectTransform.offsetMax = new Vector2(-16f, -12f);
        }

        panel.SetActive(false);
    }

    void RefreshCombatWordsHud(bool showCombatHud)
    {
        if (_combatWordsPanel == null)
            return;

        bool visible = showCombatHud &&
                       _wordActionHandler != null &&
                       _wordActionHandler.IsGrammarBattleFlowActive;
        _combatWordsPanel.gameObject.SetActive(visible);
        if (!visible || _combatWordsLabel == null)
            return;

        CreatureCombatController combat = _wordActionHandler.creatureCombat;
        CreatureCombatRegistry registry = combat != null
            ? combat.registry
            : null;
        registry ??= GetComponent<CreatureCombatRegistry>() ??
                     GetComponentInParent<CreatureCombatRegistry>() ??
                     FindAnyObjectByType<CreatureCombatRegistry>();
        if (registry == null)
        {
            _combatWordsLabel.text = "";
            _combatWordsPanel.gameObject.SetActive(false);
            return;
        }

        string activeNoun = "";
        TacticalGrammarBattleController tactical = _wordActionHandler.tacticalBattle;
        if (tactical != null && tactical.IsActive && tactical.State?.playerUnit != null)
            activeNoun = tactical.State.playerUnit.noun;
        else if (combat?.ActiveCreature != null && !combat.ActiveCreature.IsDefeated)
            activeNoun = combat.ActiveCreature.CanonicalNoun;

        string desiredNoun = "";
        if (_aimAssist != null &&
            _aimAssist.TryGetSelectedTarget(out SpellTarget selectedTarget) &&
            selectedTarget != null)
        {
            desiredNoun = selectedTarget.RequiredCreatureNoun;
        }

        _combatWordsLabel.text = CombatLanguageGuide.BuildHudText(registry, activeNoun, desiredNoun);
    }

    void BuildGrimoireHint(Transform parent)
    {
        var go = new GameObject("BestiaryHint", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        _grimoireHintPanel = go.GetComponent<RectTransform>();
        _grimoireHintPanel.anchorMin = new Vector2(1f, 0.5f);
        _grimoireHintPanel.anchorMax = new Vector2(1f, 0.5f);
        _grimoireHintPanel.pivot = new Vector2(1f, 0.5f);
        _grimoireHintPanel.anchoredPosition = new Vector2(-28f, 0f);
        _grimoireHintPanel.sizeDelta = new Vector2(86f, 86f);

        _grimoireHintImage = go.GetComponent<Image>();
        _grimoireHintImage.color = new Color(1f, 0.81f, 0.32f, 0.92f);

        _grimoireHintButton = go.GetComponent<Button>();
        _grimoireHintButton.onClick.AddListener(OpenVisibleBestiaryEntry);

        var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(TextMeshProUGUI));
        iconGo.transform.SetParent(go.transform, false);
        TextMeshProUGUI icon = iconGo.GetComponent<TextMeshProUGUI>();
        icon.text = "G";
        icon.font = TMP_Settings.defaultFontAsset;
        icon.fontSize = 34f;
        icon.fontStyle = FontStyles.Bold;
        icon.color = Color.white;
        icon.raycastTarget = false;
        icon.alignment = TextAlignmentOptions.Center;
        icon.rectTransform.anchorMin = Vector2.zero;
        icon.rectTransform.anchorMax = Vector2.one;
        icon.rectTransform.offsetMin = Vector2.zero;
        icon.rectTransform.offsetMax = Vector2.zero;

        go.SetActive(false);
    }

    void RefreshGrimoireHint(bool showCombatHud)
    {
        if (_grimoireHintPanel == null)
            return;

        _visibleBestiaryTarget = showCombatHud ? FindVisibleBestiaryTarget() : null;
        bool shouldShow = _visibleBestiaryTarget != null;
        _grimoireHintPanel.gameObject.SetActive(shouldShow);
        if (!shouldShow || _grimoireHintImage == null)
            return;

        float alpha = Mathf.Lerp(0.38f, 1f, (Mathf.Sin(Time.unscaledTime * 7f) + 1f) * 0.5f);
        _grimoireHintImage.color = new Color(1f, 0.81f, 0.32f, alpha);
    }

    EnemyBestiaryIdentity FindVisibleBestiaryTarget()
    {
        if (_aimAssist != null && _aimAssist.TryGetSelectedIdentity(out EnemyBestiaryIdentity selectedIdentity))
            return selectedIdentity;

        Camera camera = Camera.main;
        if (camera == null)
            return null;

        EnemyBestiaryIdentity[] identities = FindObjectsByType<EnemyBestiaryIdentity>(FindObjectsInactive.Exclude);
        EnemyBestiaryIdentity best = null;
        float bestDistance = float.MaxValue;

        foreach (EnemyBestiaryIdentity identity in identities)
        {
            if (identity == null || !identity.gameObject.activeInHierarchy)
                continue;

            SpellTarget target = identity.GetComponent<SpellTarget>();
            if (target != null && target.IsDefeated)
                continue;

            Vector3 viewport = camera.WorldToViewportPoint(identity.transform.position + Vector3.up * 0.7f);
            if (viewport.z <= 0f || viewport.x < 0.08f || viewport.x > 0.92f || viewport.y < 0.08f || viewport.y > 0.92f)
                continue;

            float distance = viewport.z;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = identity;
            }
        }

        return best;
    }

    void OpenVisibleBestiaryEntry()
    {
        TryOpenVisibleBestiaryEntry();
    }

    public bool TryOpenVisibleBestiaryEntry()
    {
        EnemyBestiaryIdentity target = _visibleBestiaryTarget;
        if (target == null)
            target = FindVisibleBestiaryTarget();
        if (target == null)
            return false;

        _visibleBestiaryTarget = target;

        GrimoireUI grimoire = GetComponent<GrimoireUI>() ?? FindAnyObjectByType<GrimoireUI>();
        if (grimoire != null)
        {
            grimoire.OpenBestiaryFor(target);
            return true;
        }

        return false;
    }

    void BuildCrosshair(Transform parent)
    {
        var root = new GameObject("Crosshair", typeof(RectTransform));
        root.transform.SetParent(parent, false);
        _crosshairRoot = root.GetComponent<RectTransform>();
        _crosshairRoot.anchorMin = new Vector2(0.5f, 0.5f);
        _crosshairRoot.anchorMax = new Vector2(0.5f, 0.5f);
        _crosshairRoot.pivot = new Vector2(0.5f, 0.5f);
        _crosshairRoot.sizeDelta = new Vector2(48f, 48f);
        _crosshairRoot.anchoredPosition = Vector2.zero;

        MakeCrosshairTick(_crosshairRoot, "Top", new Vector2(0f, 12f), new Vector2(3f, 10f));
        MakeCrosshairTick(_crosshairRoot, "Bottom", new Vector2(0f, -12f), new Vector2(3f, 10f));
        MakeCrosshairTick(_crosshairRoot, "Left", new Vector2(-12f, 0f), new Vector2(10f, 3f));
        MakeCrosshairTick(_crosshairRoot, "Right", new Vector2(12f, 0f), new Vector2(10f, 3f));

        var center = new GameObject("Core", typeof(RectTransform), typeof(Image));
        center.transform.SetParent(_crosshairRoot, false);
        var centerRt = center.GetComponent<RectTransform>();
        centerRt.anchorMin = new Vector2(0.5f, 0.5f);
        centerRt.anchorMax = new Vector2(0.5f, 0.5f);
        centerRt.pivot = new Vector2(0.5f, 0.5f);
        centerRt.anchoredPosition = Vector2.zero;
        centerRt.sizeDelta = new Vector2(6f, 6f);
        BrushStrokeStyle.ApplyDot(center.GetComponent<Image>(), new Color(GameUiTheme.Gold.r, GameUiTheme.Gold.g, GameUiTheme.Gold.b, 0.95f));
    }

    void BuildAmmoPanel(Transform parent)
    {
        var panel = new GameObject("AmmoPanel", typeof(RectTransform), typeof(Image), typeof(Outline));
        panel.transform.SetParent(parent, false);
        _ammoPanel = panel.GetComponent<RectTransform>();
        _ammoPanel.anchorMin = new Vector2(1f, 0f);
        _ammoPanel.anchorMax = new Vector2(1f, 0f);
        _ammoPanel.pivot = new Vector2(1f, 0f);
        _ammoPanel.anchoredPosition = new Vector2(-34f, 30f);
        _ammoPanel.sizeDelta = new Vector2(330f, 172f);

        GameUiTheme.StyleHudPanel(_ammoPanel, 0.9f);
        _ammoLabel = MakeLabel("Ammo", panel.transform, new Vector2(16f, -14f), 24f, FontStyles.Bold, GameUiTheme.Gold);
        if (_ammoLabel != null)
        {
            _ammoLabel.alignment = TextAlignmentOptions.TopLeft;
            _ammoLabel.textWrappingMode = TextWrappingModes.NoWrap;
            _ammoLabel.rectTransform.anchorMin = new Vector2(0f, 0f);
            _ammoLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            _ammoLabel.rectTransform.pivot = new Vector2(0f, 1f);
            _ammoLabel.rectTransform.offsetMin = new Vector2(16f, 12f);
            _ammoLabel.rectTransform.offsetMax = new Vector2(-16f, -12f);
        }
    }

    void BuildHealthPanel(Transform parent)
    {
        if (_playerHealth == null)
            return;

        var panel = new GameObject("HealthPanel", typeof(RectTransform), typeof(Image), typeof(Outline));
        panel.transform.SetParent(parent, false);
        _hpPanel = panel.GetComponent<RectTransform>();
        _hpPanel.anchorMin = new Vector2(0f, 0f);
        _hpPanel.anchorMax = new Vector2(0f, 0f);
        _hpPanel.pivot = new Vector2(0f, 0f);
        _hpPanel.anchoredPosition = new Vector2(34f, 30f);
        _hpPanel.sizeDelta = new Vector2(272f, 98f);

        GameUiTheme.StyleHudPanel(_hpPanel, 0.9f);

        _hpLabel = MakeLabel("Health", panel.transform, new Vector2(16f, -14f), 28f, FontStyles.Bold, GameUiTheme.Text);
        if (_hpLabel != null)
        {
            _hpLabel.alignment = TextAlignmentOptions.Left;
            _hpLabel.rectTransform.anchorMin = new Vector2(0f, 0f);
            _hpLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            _hpLabel.rectTransform.pivot = new Vector2(0f, 0.5f);
            _hpLabel.rectTransform.offsetMin = new Vector2(16f, 12f);
            _hpLabel.rectTransform.offsetMax = new Vector2(-16f, -12f);
            _hpLabel.verticalAlignment = VerticalAlignmentOptions.Middle;
        }
    }

    void BuildObjectivePanel(Transform parent)
    {
        var panel = new GameObject("ObjectivePanel", typeof(RectTransform), typeof(Image), typeof(Outline));
        panel.transform.SetParent(parent, false);
        _objectivePanel = panel.GetComponent<RectTransform>();
        _objectivePanel.anchorMin = new Vector2(0.5f, 1f);
        _objectivePanel.anchorMax = new Vector2(0.5f, 1f);
        _objectivePanel.pivot = new Vector2(0.5f, 1f);
        _objectivePanel.anchoredPosition = new Vector2(0f, -34f);
        _objectivePanel.sizeDelta = new Vector2(420f, 160f);

        GameUiTheme.StyleHudPanel(_objectivePanel, 0.88f);

        _objectiveLabel = MakeLabel("Objective", panel.transform, new Vector2(16f, -14f), 22f, FontStyles.Bold, GameUiTheme.Text);
        if (_objectiveLabel != null)
        {
            _objectiveLabel.alignment = TextAlignmentOptions.TopLeft;
            _objectiveLabel.textWrappingMode = TextWrappingModes.Normal;
            _objectiveLabel.rectTransform.anchorMin = new Vector2(0f, 0f);
            _objectiveLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            _objectiveLabel.rectTransform.pivot = new Vector2(0f, 1f);
            _objectiveLabel.rectTransform.offsetMin = new Vector2(16f, 12f);
            _objectiveLabel.rectTransform.offsetMax = new Vector2(-16f, -12f);
        }
    }

    void BuildDevPanel(Transform parent)
    {
        var panel = new GameObject("DevWavePanel", typeof(RectTransform), typeof(Image), typeof(Outline));
        panel.transform.SetParent(parent, false);
        _devPanel = panel.GetComponent<RectTransform>();
        _devPanel.anchorMin = new Vector2(0f, 1f);
        _devPanel.anchorMax = new Vector2(0f, 1f);
        _devPanel.pivot = new Vector2(0f, 1f);
        _devPanel.anchoredPosition = new Vector2(34f, -34f);
        _devPanel.sizeDelta = new Vector2(420f, 208f);

        GameUiTheme.StyleHudPanel(_devPanel, 0.92f);

        _devTitleLabel = MakeLabel("DevTitle", panel.transform, new Vector2(16f, -14f), 24f, FontStyles.Bold, GameUiTheme.Gold);
        if (_devTitleLabel != null)
            _devTitleLabel.text = "Encounter Metrics";

        _devMetricsLabel = MakeLabel("DevMetrics", panel.transform, new Vector2(16f, -48f), 17f, FontStyles.Normal, GameUiTheme.Text);
        if (_devMetricsLabel != null)
        {
            _devMetricsLabel.alignment = TextAlignmentOptions.TopLeft;
            _devMetricsLabel.textWrappingMode = TextWrappingModes.Normal;
            _devMetricsLabel.overflowMode = TextOverflowModes.Overflow;
            _devMetricsLabel.rectTransform.anchorMin = new Vector2(0f, 0f);
            _devMetricsLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            _devMetricsLabel.rectTransform.pivot = new Vector2(0f, 1f);
            _devMetricsLabel.rectTransform.offsetMin = new Vector2(16f, 14f);
            _devMetricsLabel.rectTransform.offsetMax = new Vector2(-16f, -46f);
        }
    }
}
