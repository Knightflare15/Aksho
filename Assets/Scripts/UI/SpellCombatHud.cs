using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(WordActionHandler))]
public partial class SpellCombatHud : MonoBehaviour
{
    private const int MaxShotsShown = 8;

    private WordActionHandler _wordActionHandler;
    private PlayerController _playerController;
    private PlayerHealth _playerHealth;
    private PlayerHurtbox _playerHurtbox;
    private EnemyWaveDirector _waveDirector;
    private SpellPerformanceTracker _spellPerformanceTracker;
    private LevelObjectiveDirector _objectiveDirector;
    private RunProgressionManager _runProgression;
    private PlayerAimAssist _aimAssist;
    private PhoneticDisplayState _phoneticDisplayState;

    private Canvas _canvas;
    private RectTransform _crosshairRoot;
    private RectTransform _ammoPanel;
    private RectTransform _hpPanel;
    private RectTransform _objectivePanel;
    private RectTransform _devPanel;
    private RectTransform _phoneticsPanel;
    private RectTransform _combatWordsPanel;
    private RectTransform _grimoireHintPanel;
    private Button _grimoireHintButton;
    private Image _grimoireHintImage;
    private TextMeshProUGUI _ammoLabel;
    private TextMeshProUGUI _hpLabel;
    private TextMeshProUGUI _objectiveLabel;
    private TextMeshProUGUI _devTitleLabel;
    private TextMeshProUGUI _devMetricsLabel;
    private TextMeshProUGUI _phoneticsLabel;
    private TextMeshProUGUI _combatWordsLabel;
    private EnemyBestiaryIdentity _visibleBestiaryTarget;

    void Awake()
    {
        ResolveReferences();
        EnsureHud();
    }

    void LateUpdate()
    {
        if (_wordActionHandler == null)
            return;

        ResolveReferences();

        EnsureHud();

        bool showCombatHud = !TemplateRecorderUI.IsOpen &&
                             !GrimoireUI.IsOpen &&
                             !ChestMiniGameState.IsOpen &&
                             (_playerController == null || !_playerController.IsDrawingMode);
        bool showSidePanels = !TemplateRecorderUI.IsOpen &&
                              !GrimoireUI.IsOpen &&
                              !ChestMiniGameState.IsOpen;

        if (_crosshairRoot != null)
            _crosshairRoot.gameObject.SetActive(showCombatHud);

        if (_ammoPanel != null)
            _ammoPanel.gameObject.SetActive(showCombatHud);

        if (_hpPanel != null)
            _hpPanel.gameObject.SetActive(showCombatHud && _playerHealth != null);

        if (_objectivePanel != null)
            _objectivePanel.gameObject.SetActive(showCombatHud);

        if (_devPanel != null)
            _devPanel.gameObject.SetActive(showCombatHud);

        RefreshPhoneticsHud(showSidePanels);
        RefreshGrimoireHint(showCombatHud);
        RefreshCombatWordsHud(showCombatHud);

        if (!showCombatHud)
            return;

        RefreshAmmoHud();
        RefreshHealthHud();
        RefreshObjectiveHud();
        RefreshDevHud();
    }

    void EnsureHud()
    {
        if (_canvas != null)
        {
            if (_hpPanel == null && _playerHealth != null)
                BuildHealthPanel(_canvas.transform);
            if (_objectivePanel == null)
                BuildObjectivePanel(_canvas.transform);
            if (_phoneticsPanel == null)
                BuildPhoneticsPanel(_canvas.transform);
            if (_combatWordsPanel == null)
                BuildCombatWordsPanel(_canvas.transform);
            return;
        }

        var root = new GameObject("SpellCombatHudCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);

        _canvas = root.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 300;

        var scaler = root.GetComponent<CanvasScaler>();
        GameUiTheme.ConfigureStandardGameplayCanvasScaler(scaler);

        BuildCrosshair(root.transform);
        BuildAmmoPanel(root.transform);
        BuildHealthPanel(root.transform);
        BuildObjectivePanel(root.transform);
        BuildDevPanel(root.transform);
        BuildPhoneticsPanel(root.transform);
        BuildCombatWordsPanel(root.transform);
        BuildGrimoireHint(root.transform);
    }

    void ResolveReferences()
    {
        _wordActionHandler ??= GetComponent<WordActionHandler>() ?? GetComponentInParent<WordActionHandler>();
        _playerController ??= GetComponent<PlayerController>() ?? GetComponentInParent<PlayerController>() ?? FindAnyObjectByType<PlayerController>();
        _playerHealth ??= GetComponent<PlayerHealth>() ?? GetComponentInParent<PlayerHealth>() ?? FindAnyObjectByType<PlayerHealth>();
        _playerHurtbox ??= GetComponent<PlayerHurtbox>() ?? GetComponentInParent<PlayerHurtbox>() ?? FindAnyObjectByType<PlayerHurtbox>();
        _waveDirector ??= GetComponent<EnemyWaveDirector>() ?? GetComponentInParent<EnemyWaveDirector>() ?? FindAnyObjectByType<EnemyWaveDirector>();
        _spellPerformanceTracker ??= GetComponent<SpellPerformanceTracker>() ?? GetComponentInParent<SpellPerformanceTracker>() ?? FindAnyObjectByType<SpellPerformanceTracker>();
        _objectiveDirector ??= GetComponent<LevelObjectiveDirector>() ?? GetComponentInParent<LevelObjectiveDirector>() ?? FindAnyObjectByType<LevelObjectiveDirector>();
        _aimAssist ??= GetComponent<PlayerAimAssist>() ?? GetComponentInParent<PlayerAimAssist>() ?? FindAnyObjectByType<PlayerAimAssist>();
        _phoneticDisplayState ??= GetComponent<PhoneticDisplayState>() ?? GetComponentInParent<PhoneticDisplayState>() ?? FindAnyObjectByType<PhoneticDisplayState>();
        _runProgression ??= RunProgressionManager.Instance;
    }

    void RefreshAmmoHud()
    {
        if (_ammoLabel == null)
            return;

        var sb = new System.Text.StringBuilder();
        if (_wordActionHandler.IsGrammarBattleFlowActive)
        {
            string tacticalBoard = _wordActionHandler.TacticalBoardSummary;
            if (!string.IsNullOrWhiteSpace(tacticalBoard))
                sb.Append(tacticalBoard);
            else
            {
                sb.AppendLine("Grammar Battle");
                sb.AppendLine(_wordActionHandler.ActiveCreatureSummary);
            }
            string grammarStatus = _wordActionHandler.StatusHint;
            if (!string.IsNullOrEmpty(grammarStatus))
                sb.AppendLine(grammarStatus);
            if (_wordActionHandler.IsAttackCastHeld || _wordActionHandler.IsAttackListenActive)
            {
                string keywords = _wordActionHandler.ConfiguredCastKeywords;
                string detail = _wordActionHandler.AttackListenStatusDetail;
                string prefix = _wordActionHandler.IsListeningForCast ? "LISTENING" : detail.ToUpperInvariant();
                sb.AppendLine(string.IsNullOrWhiteSpace(keywords)
                    ? $"{prefix}..."
                    : $"{prefix}: {keywords}");
                string guess = _wordActionHandler.LiveVoiceGuess;
                if (!string.IsNullOrWhiteSpace(guess))
                    sb.AppendLine("Heard: ").Append(guess.ToUpperInvariant());
            }

            _ammoLabel.text = sb.ToString();
            return;
        }

        int slotCount = Mathf.Max(WordActionHandler.DefaultSpellbookSlotCount, _wordActionHandler.SlotCount);
        for (int i = 0; i < slotCount; i++)
        {
            if (!_wordActionHandler.TryGetSlotState(
                    i,
                    out string spellWord,
                    out int currentAmmo,
                    out int maxAmmo,
                    out bool isSelected,
                    out bool isEmpty))
                continue;

            string marker = isSelected ? ">" : " ";
            string pageText = isEmpty
                ? "EMPTY"
                : $"{spellWord} {Mathf.Clamp(currentAmmo, 0, MaxShotsShown)}/{Mathf.Clamp(Mathf.Max(currentAmmo, maxAmmo), 0, MaxShotsShown)}";
            sb.Append(marker).Append(" P").Append(i + 1).Append("  ").Append(pageText);
            if (isSelected && isEmpty)
                sb.Append("  F");
            if (i < slotCount - 1)
                sb.AppendLine();
        }

        string status = _wordActionHandler.StatusHint;
        if (!string.IsNullOrEmpty(status))
            sb.AppendLine().Append(status);
        if (_wordActionHandler.IsAttackCastHeld || _wordActionHandler.IsAttackListenActive)
        {
            string keywords = _wordActionHandler.ConfiguredCastKeywords;
            string detail = _wordActionHandler.AttackListenStatusDetail;
            string prefix = _wordActionHandler.IsListeningForCast ? "ATTACK LISTENING" : $"ATTACK {detail.ToUpperInvariant()}";
            sb.AppendLine().Append(string.IsNullOrWhiteSpace(keywords)
                ? $"{prefix}..."
                : $"{prefix}: {keywords}");
            string guess = _wordActionHandler.LiveVoiceGuess;
            if (!string.IsNullOrWhiteSpace(guess))
                sb.AppendLine().Append("Vosk: ").Append(guess.ToUpperInvariant());
        }

        _ammoLabel.text = sb.ToString();
    }

    void RefreshHealthHud()
    {
        if (_playerHealth == null)
            return;

        if (_hpLabel != null)
            _hpLabel.text = $"HP {_playerHealth.CurrentHp}/{_playerHealth.maxHp}";
    }

    void RefreshObjectiveHud()
    {
        if (_objectiveLabel == null)
            return;

        string coins = _runProgression != null ? $"Coins {_runProgression.Coins}" : "Coins 0";
        string stage = _runProgression != null && _runProgression.SchoolModeActive
            ? $"World Goal Practice  {FormatSeconds(Mathf.CeilToInt(_runProgression.RemainingSeconds))}  Area {_runProgression.CurrentSubArenaIndex}/3"
            : _runProgression != null ? $"Stage {_runProgression.StageNumber}" : "Stage 1";
        string objective = _objectiveDirector != null
            ? _objectiveDirector.BuildObjectiveStatus()
            : "Objective waiting.";

        _objectiveLabel.text = $"{stage}  {coins}\n{objective}";
    }

    static string FormatSeconds(int seconds)
    {
        seconds = Mathf.Max(0, seconds);
        return $"{seconds / 60:0}:{seconds % 60:00}";
    }

    void RefreshDevHud()
    {
        if (_devMetricsLabel == null)
            return;

        WaveDescriptor currentWave = _waveDirector != null ? _waveDirector.CurrentWaveDescriptor : null;
        WaveDescriptor focusWave = currentWave;

        if (_devTitleLabel != null)
        {
            if (_waveDirector == null)
                _devTitleLabel.text = "Encounter Metrics";
            else if (focusWave != null)
                _devTitleLabel.text = $"Stage {_waveDirector.CurrentLevel}  {focusWave.encounterType}";
            else
                _devTitleLabel.text = $"Stage {_waveDirector.CurrentLevel}  {_waveDirector.CurrentPhase}";
        }

        if (_waveDirector == null || focusWave == null)
        {
            _devMetricsLabel.text = "No active encounter.";
            return;
        }

        string spellWord = BuildWaveSpellSummary(focusWave);
        string enemyName = BuildWaveEnemySummary(focusWave);
        int alive = _waveDirector.AliveEnemyCount;
        int total = focusWave.enemyCount;
        float adaptiveWeight = _spellPerformanceTracker != null
            ? _spellPerformanceTracker.GetDifficultyWeight(spellWord)
            : focusWave.difficultyWeight;

        string statsBlock = "No stats yet.";
        if (_spellPerformanceTracker != null &&
            _spellPerformanceTracker.TryGetStats(spellWord, out SpellPerformanceTracker.SpellStats stats))
        {
            float successRate = stats.attempts > 0
                ? (stats.successes / (float)stats.attempts) * 100f
                : 0f;
            statsBlock =
                $"Attempts {stats.attempts}  Success {successRate:0}%\n" +
                $"Avg score {stats.averageLetterScore:0.0}  Avg tries {stats.averageTriesPerLetter:0.0}\n" +
                $"Wrong letters {stats.wrongLetters}  Gifted {stats.giftedLetters}";
        }

        string phase = _waveDirector.CurrentPhase.ToString();
        string hurtboxState = _playerHurtbox != null && _playerHurtbox.TriggerCollider != null
            ? "Ready"
            : "Missing";
        string lastDamageSource = _playerHealth != null && !string.IsNullOrEmpty(_playerHealth.LastDamageSource)
            ? _playerHealth.LastDamageSource
            : "-";
        _devMetricsLabel.text =
            $"{phase}: {enemyName}\n" +
            $"Spell focus: {spellWord}\n" +
            $"Enemies: {alive}/{total} alive\n" +
            $"Adaptive weight: {adaptiveWeight:0.00}\n" +
            $"Player hurtbox: {hurtboxState}  Last hit: {lastDamageSource}\n" +
            $"{statsBlock}";
    }

    void MakeCrosshairTick(Transform parent, string name, Vector2 anchoredPosition, Vector2 size)
    {
        var tick = new GameObject(name, typeof(RectTransform), typeof(Image));
        tick.transform.SetParent(parent, false);
        var tickRt = tick.GetComponent<RectTransform>();
        tickRt.anchorMin = new Vector2(0.5f, 0.5f);
        tickRt.anchorMax = new Vector2(0.5f, 0.5f);
        tickRt.pivot = new Vector2(0.5f, 0.5f);
        tickRt.anchoredPosition = anchoredPosition;
        tickRt.sizeDelta = size;

        var image = tick.GetComponent<Image>();
        image.color = new Color(GameUiTheme.Text.r, GameUiTheme.Text.g, GameUiTheme.Text.b, 0.92f);
        image.raycastTarget = false;
    }

    TextMeshProUGUI MakeLabel(string name, Transform parent, Vector2 anchoredPosition, float fontSize, FontStyles fontStyle, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = new Vector2(-32f, 28f);

        var text = go.GetComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.color = color;
        text.alignment = TextAlignmentOptions.Left;
        text.raycastTarget = false;
        return text;
    }

    string BuildWaveEnemySummary(WaveDescriptor wave)
    {
        if (wave == null)
            return "Enemy";

        List<EnemyDefinition> definitions = wave.enemyDefinitions != null && wave.enemyDefinitions.Count > 0
            ? wave.enemyDefinitions
            : new List<EnemyDefinition> { wave.enemyDefinition };

        var names = new List<string>();
        foreach (EnemyDefinition definition in definitions)
        {
            if (definition == null || names.Contains(definition.displayName))
                continue;

            names.Add(definition.displayName);
            if (names.Count >= 2)
                break;
        }

        return names.Count > 0 ? string.Join(" + ", names) : "Enemy";
    }

    string BuildWaveSpellSummary(WaveDescriptor wave)
    {
        if (wave == null)
            return "";

        List<EnemyDefinition> definitions = wave.enemyDefinitions != null && wave.enemyDefinitions.Count > 0
            ? wave.enemyDefinitions
            : new List<EnemyDefinition> { wave.enemyDefinition };

        var words = new List<string>();
        foreach (EnemyDefinition definition in definitions)
        {
            if (definition == null)
                continue;

            string word = SpellRegistry.NormalizeWord(definition.weaknessSpell);
            if (string.IsNullOrEmpty(word) || words.Contains(word))
                continue;

            words.Add(word);
            if (words.Count >= 2)
                break;
        }

        if (words.Count == 0 && !string.IsNullOrEmpty(wave.targetSpellWord))
            words.Add(SpellRegistry.NormalizeWord(wave.targetSpellWord));

        return words.Count > 0 ? string.Join("/", words) : "";
    }
}
