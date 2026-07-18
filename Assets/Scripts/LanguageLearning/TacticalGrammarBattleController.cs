using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed partial class TacticalGrammarBattleController : MonoBehaviour
{
    [Header("References")]
    public CreatureCombatRegistry registry;
    public EnemyWaveDirector waveDirector;
    public PlayerLearningProfile learningProfile;
    public PlayerAimAssist aimAssist;

    [Header("Tactical Battle")]
    public bool enableTacticalBattles;
    public bool suppressWorldCombatAgents = true;
    [Min(3)] public int gridWidth = TacticalGrammarBattleState.DefaultSize;
    [Min(3)] public int gridHeight = TacticalGrammarBattleState.DefaultSize;
    public TacticalBattlePosition playerSummonPosition = new TacticalBattlePosition(0, 2);
    public TacticalBattlePosition enemyPosition = new TacticalBattlePosition(4, 2);
    [Tooltip("Keeps forward/left/right/backward relative to the direction the player camera is looking.")]
    public bool followCameraFacing = true;

    [Header("Battle Scene")]
    public bool useDedicatedBattleScene;
    public string battleSceneName = "";
    public bool unloadBattleSceneOnEnd = true;

    [Header("Placeholder Board Visuals")]
    public bool renderPlaceholderCubes;
    [Tooltip("Debug only. Production combat keeps the hex topology invisible and renders only terrain, units, and action telegraphs.")]
    public bool showUnderlyingHexGrid;
    public Transform boardAnchor;
    [Min(0.5f)] public float cellSize = 1.25f;
    public float boardForwardOffset = 4.5f;
    public float boardHeightOffset = 0.05f;
    public float floorCubeHeight = 0.08f;
    public float terrainCubeHeight = 0.45f;
    public float unitCubeHeight = 0.85f;

    [Header("Real-Time Enemy Mind")]
    public bool enableRealtimeEnemyAI = true;
    [Tooltip("Uses saved level and combat history to slow the enemy for new or struggling learners.")]
    public bool adaptEnemyPaceToLearner = true;
    [Range(0f, 1f)] public float fallbackEnemyPace = 0.15f;
    [Tooltip("Extra opening time at advanced pace. New learners receive the novice opening grace below.")]
    [Min(0.25f)] public float enemyOpeningGraceSeconds = 1.8f;
    [Min(0.25f)] public float noviceEnemyOpeningGraceSeconds = 3.2f;
    [Tooltip("Fastest base decision rhythm, used for an advanced learner before enemy stat scaling.")]
    [Min(0.25f)] public float enemyDecisionIntervalSeconds = 2.2f;
    [Min(0.25f)] public float noviceEnemyDecisionIntervalSeconds = 4.8f;
    [Min(5f)] public float pronounCurseDurationSeconds = 30f;
    [Min(2)] public int enemyDecisionsPerCurse = 3;

    TacticalGrammarBattler battler;
    WaveDescriptor activeDescriptor;
    string status = "";
    int activeShield;
    float shieldExpiresAt = -999f;
    PendingEnemyAttack pendingEnemyAttack;
    Transform boardRoot;
    Material floorMaterial;
    Material playerMaterial;
    Material enemyMaterial;
    Material selectedCellMaterial;
    Material movementPreviewMaterial;
    Material attackPreviewMaterial;
    Material pendingAttackMaterial;
    readonly Dictionary<TacticalBattleCellType, Material> terrainMaterials = new Dictionary<TacticalBattleCellType, Material>();
    string loadedBattleSceneName = "";
    bool battleSceneLoading;
    float nextEnemyDecisionAt = float.PositiveInfinity;
    float activeCurseExpiresAt = -999f;
    int enemyDecisionCount;
    GrammarBattleCurse activeTacticalCurse = GrammarBattleCurse.None;

    public bool IsActive => enableTacticalBattles && battler != null && activeDescriptor != null;
    public TacticalGrammarBattleState State => battler != null ? battler.State : null;
    public string Status => status;
    public GrammarBattleCurse ActiveTacticalCurse => activeTacticalCurse;
    public event Action<string> OnStatus;

    void Awake()
    {
        ResolveReferences();
    }

    void OnEnable()
    {
        ResolveReferences();
        Subscribe();
    }

    void OnDisable()
    {
        Unsubscribe();
    }

    void Update()
    {
        if (!IsActive)
            return;

        SuppressWorldCombatAgents();
        UpdateFacingFromPlayerView();
        ResolvePendingEnemyAttackIfReady();
        UpdateActiveTacticalCurse();
        UpdateRealtimeEnemyMind();
    }

    public bool TryHandlePhrase(string phrase, PronunciationInsightResult? pronunciationInsight = null)
    {
        if (!enableTacticalBattles)
            return false;

        EnsureActiveBattle();
        if (!IsActive || string.IsNullOrWhiteSpace(phrase))
            return false;

        if (State.playerUnit == null)
            return TrySummon(phrase, pronunciationInsight);

        string curseComplianceMessage = "";
        GrammarBattleCurse curseAtInput = activeTacticalCurse;
        if (curseAtInput != GrammarBattleCurse.None)
        {
            if (!TryApplyTacticalCurseGate(phrase, curseAtInput, out string transformedPhrase, out string curseError))
            {
                Publish(curseError);
                RecordTacticalEvent(phrase, false, curseError, null, 0, pronunciationInsight, activeCurseOverride: curseAtInput);
                return true;
            }

            phrase = transformedPhrase;
            curseComplianceMessage = IsPronounCurse(curseAtInput)
                ? $" {FormatCurse(curseAtInput)} obeyed; {Mathf.Max(0f, activeCurseExpiresAt - Time.time):0} seconds remain."
                : $" {FormatCurse(curseAtInput)} obeyed; it remains for this battle.";
        }

        if (TryHandleConjunctionPhrase(phrase, pronunciationInsight))
            return true;

        TacticalBattleCommandResult result = battler.ExecutePlayerCommand(phrase, Time.time);
        if (!result.success)
        {
            Publish(result.message);
            RecordTacticalEvent(phrase, false, result.message, result.actionProfile, 0, pronunciationInsight);
            return true;
        }

        int damage = ResolveDamage(result, out string damageRejection);
        bool damagedEnemy = damage > 0 && TryDamageActiveEnemy(State.playerUnit.noun, result.actionProfile?.verb, damage);
        if (result.actionProfile != null)
            ApplyActionProtection(result.actionProfile);
        TacticalEnemyTurnResult enemyTurn = ResolveEnemyResponse(damagedEnemy, result.actionProfile);
        RefreshBoardVisuals();

        string outcome = damagedEnemy
            ? $"{result.message} Hit the enemy for {damage}."
            : result.message;
        if (!damagedEnemy && !string.IsNullOrWhiteSpace(damageRejection))
            outcome = $"{outcome} {damageRejection}";
        if (!string.IsNullOrWhiteSpace(enemyTurn.message))
            outcome = $"{outcome} {enemyTurn.message}";
        outcome += curseComplianceMessage;
        Publish(outcome);
        learningProfile?.RecordSpokenCast(phrase, true, false, 0f, null, Array.Empty<byte>(), false, false);
        RecordTacticalEvent(phrase, true, damagedEnemy ? "tactical_hit" : "tactical_position", result.actionProfile, damagedEnemy ? damage : 0, pronunciationInsight);
        return true;
    }

    bool TryHandleConjunctionPhrase(string phrase, PronunciationInsightResult? pronunciationInsight)
    {
        if (!TryBuildConjunctionClauses(registry, phrase, out string conjunction, out List<string> clauses, out string rejectionMessage))
            return false;

        if (clauses.Count == 0)
        {
            Publish(rejectionMessage);
            RecordTacticalEvent(phrase, false, rejectionMessage, null, 0, pronunciationInsight, conjunction);
            return true;
        }

        if (conjunction == "BECAUSE")
            return TryHandleBecausePhrase(phrase, clauses[0], conjunction, pronunciationInsight);

        if (conjunction == "OR")
            return TryHandleOrPhrase(phrase, clauses, conjunction, pronunciationInsight);

        var outcomes = new List<string>();
        int totalDamage = 0;
        TacticalBattleActionProfile lastProfile = null;
        foreach (string clause in clauses)
        {
            TacticalBattleCommandResult result = battler.ExecutePlayerCommand(clause, Time.time);
            if (!result.success)
            {
                Publish(result.message);
                RecordTacticalEvent(phrase, false, result.message, result.actionProfile, 0, pronunciationInsight, conjunction);
                RefreshBoardVisuals();
                return true;
            }

            int damage = ResolveDamage(result, out string damageRejection);
            bool damagedEnemy = damage > 0 && TryDamageActiveEnemy(State.playerUnit.noun, result.actionProfile?.verb, damage);
            if (result.actionProfile != null)
                ApplyActionProtection(result.actionProfile);
            lastProfile = result.actionProfile;
            totalDamage += damagedEnemy ? damage : 0;
            outcomes.Add(damagedEnemy
                ? $"{result.message} Hit for {damage}."
                : $"{result.message}{(string.IsNullOrWhiteSpace(damageRejection) ? "" : $" {damageRejection}")}");
            RecordTacticalEvent(clause, true, damagedEnemy ? "tactical_hit" : "tactical_position", result.actionProfile, damagedEnemy ? damage : 0, pronunciationInsight, conjunction);
        }

        TacticalEnemyTurnResult enemyTurn = ResolveEnemyResponse(totalDamage > 0, lastProfile);
        RefreshBoardVisuals();
        string outcome = $"Chained verbs with {conjunction}: {string.Join(" ", outcomes)}";
        if (!string.IsNullOrWhiteSpace(enemyTurn.message))
            outcome = $"{outcome} {enemyTurn.message}";
        Publish(outcome);
        learningProfile?.RecordSpokenCast(phrase, true, false, 0f, null, Array.Empty<byte>(), false, false);
        return true;
    }

    bool TryHandleOrPhrase(string phrase, List<string> clauses, string conjunction, PronunciationInsightResult? pronunciationInsight)
    {
        foreach (string clause in clauses)
        {
            TacticalBattleCommandResult result = battler.ExecutePlayerCommand(clause, Time.time);
            if (!result.success)
                continue;

            int damage = ResolveDamage(result, out string damageRejection);
            bool damagedEnemy = damage > 0 && TryDamageActiveEnemy(State.playerUnit.noun, result.actionProfile?.verb, damage);
            if (result.actionProfile != null)
                ApplyActionProtection(result.actionProfile);
            TacticalEnemyTurnResult enemyTurn = ResolveEnemyResponse(damagedEnemy, result.actionProfile);
            RefreshBoardVisuals();
            string outcome = damagedEnemy
                ? $"Chose an OR action. {result.message} Hit the enemy for {damage}."
                : $"Chose an OR action. {result.message}";
            if (!damagedEnemy && !string.IsNullOrWhiteSpace(damageRejection))
                outcome = $"{outcome} {damageRejection}";
            if (!string.IsNullOrWhiteSpace(enemyTurn.message))
                outcome = $"{outcome} {enemyTurn.message}";
            Publish(outcome);
            learningProfile?.RecordSpokenCast(phrase, true, false, 0f, null, Array.Empty<byte>(), false, false);
            RecordTacticalEvent(clause, true, damagedEnemy ? "tactical_hit" : "tactical_position", result.actionProfile, damagedEnemy ? damage : 0, pronunciationInsight, conjunction);
            return true;
        }

        string message = "Neither OR action worked for this battle.";
        Publish(message);
        RecordTacticalEvent(phrase, false, message, null, 0, pronunciationInsight, conjunction);
        RefreshBoardVisuals();
        return true;
    }

    bool TryHandleBecausePhrase(string phrase, string actionClause, string conjunction, PronunciationInsightResult? pronunciationInsight)
    {
        TacticalBattleCommandResult result = battler.ExecutePlayerCommand(actionClause, Time.time);
        if (!result.success)
        {
            Publish(result.message);
            RecordTacticalEvent(phrase, false, result.message, result.actionProfile, 0, pronunciationInsight, conjunction);
            RefreshBoardVisuals();
            return true;
        }

        int damage = ResolveDamage(result, out string damageRejection);
        bool damagedEnemy = damage > 0 && TryDamageActiveEnemy(State.playerUnit.noun, result.actionProfile?.verb, damage);
        if (result.actionProfile != null)
            ApplyActionProtection(result.actionProfile);
        TacticalEnemyTurnResult enemyTurn = ResolveEnemyResponse(damagedEnemy, result.actionProfile);
        RefreshBoardVisuals();
        string outcome = damagedEnemy
            ? $"{result.message} The BECAUSE reason was accepted. Hit the enemy for {damage}."
            : $"{result.message} The BECAUSE reason was accepted.";
        if (!damagedEnemy && !string.IsNullOrWhiteSpace(damageRejection))
            outcome = $"{outcome} {damageRejection}";
        if (!string.IsNullOrWhiteSpace(enemyTurn.message))
            outcome = $"{outcome} {enemyTurn.message}";
        Publish(outcome);
        learningProfile?.RecordSpokenCast(phrase, true, false, 0f, null, Array.Empty<byte>(), false, false);
        RecordTacticalEvent(actionClause, true, damagedEnemy ? "tactical_hit" : "tactical_position", result.actionProfile, damagedEnemy ? damage : 0, pronunciationInsight, conjunction);
        return true;
    }

    public bool TryMitigateIncomingDamage(int incomingDamage, out int mitigatedDamage, out string mitigationMessage)
    {
        mitigatedDamage = Mathf.Max(0, incomingDamage);
        mitigationMessage = "";
        if (!IsActive || incomingDamage <= 0 || State.playerUnit == null)
            return false;

        if (Time.time > shieldExpiresAt || activeShield <= 0)
        {
            activeShield = 0;
            return false;
        }

        int blocked = Mathf.Min(activeShield, incomingDamage);
        activeShield = Mathf.Max(0, activeShield - blocked);
        mitigatedDamage = Mathf.Max(0, incomingDamage - blocked);
        mitigationMessage = $"shield blocked {blocked}";
        Publish(activeShield > 0
            ? $"Shield blocked {blocked} damage ({activeShield} shield left)."
            : $"Shield blocked {blocked} damage and broke.");
        return true;
    }

    public string BuildHudSummary()
    {
        if (!IsActive || State == null)
            return "";

        var sb = new StringBuilder();
        sb.AppendLine("Tactical Battle");
        TacticalBattleUnit player = State.playerUnit;
        TacticalBattleUnit enemy = State.enemyUnit;
        if (player != null)
            sb.AppendLine($"{player.noun}  PP {player.currentPp}/{player.stats.maxPp}  HP {player.currentHp}/{player.stats.maxHp}");
        else
            sb.AppendLine("Press F: say + write a noun to summon");
        if (activeShield > 0 && Time.time <= shieldExpiresAt)
            sb.AppendLine($"Shield {activeShield} for {Mathf.Max(0f, shieldExpiresAt - Time.time):0.0}s");
        if (enableRealtimeEnemyAI && player != null && enemy != null)
            sb.AppendLine(BuildEnemyPaceGauge());
        if (pendingEnemyAttack.active)
            sb.AppendLine($"Incoming attack {pendingEnemyAttack.damage} dmg speed {pendingEnemyAttack.attackSpeed:0.#} in {Mathf.Max(0f, pendingEnemyAttack.hitsAt - Time.time):0.0}s");
        if (activeTacticalCurse != GrammarBattleCurse.None)
            sb.AppendLine(IsPronounCurse(activeTacticalCurse)
                ? $"Curse: {FormatCurse(activeTacticalCurse)} ({Mathf.Max(0f, activeCurseExpiresAt - Time.time):0.0}s)"
                : $"Curse: {FormatCurse(activeTacticalCurse)} (until battle ends)");
        else if (enableRealtimeEnemyAI && player != null)
            sb.AppendLine($"Enemy thinking in {Mathf.Max(0f, nextEnemyDecisionAt - Time.time):0.0}s");
        if (enemy != null)
            sb.AppendLine($"Enemy {enemy.noun}  HP {enemy.currentHp}/{enemy.stats.maxHp}{(showUnderlyingHexGrid ? $" at {FormatPosition(enemy.position)}" : "")}");
        if (battler != null && battler.HasSelectedTacticalCell)
            sb.AppendLine(showUnderlyingHexGrid ? $"Aiming toward {FormatPosition(battler.SelectedTacticalCell)}" : "Aim direction locked");
        else if (player != null && !showUnderlyingHexGrid)
            sb.AppendLine("Aim: camera look direction");

        if (showUnderlyingHexGrid)
        {
            for (int y = State.height - 1; y >= 0; y--)
            {
                for (int x = 0; x < State.width; x++)
                {
                    var position = new TacticalBattlePosition(x, y);
                    if (player != null && player.position.x == x && player.position.y == y)
                        sb.Append("P ");
                    else if (enemy != null && enemy.position.x == x && enemy.position.y == y)
                        sb.Append("E ");
                    else
                        sb.Append(CellGlyph(State.GetCell(position))).Append(' ');
                }
                sb.AppendLine();
            }
        }

        if (!string.IsNullOrWhiteSpace(status))
            sb.AppendLine(status);
        sb.Append("Try: rat runs left · rat jumps over the rock · rat hides behind the wall");
        return sb.ToString();
    }

    public List<string> BuildVoiceKeywords()
    {
        ResolveReferences();
        var result = new List<string>();
        if (registry == null || registry.Nouns == null)
            return result;

        foreach (NounDefinition noun in registry.Nouns)
        {
            if (noun == null)
                continue;

            string token = CreaturePhraseUtility.NormalizeToken(noun.canonicalNoun);
            if (string.IsNullOrEmpty(token))
                continue;

            AddUnique(result, token);
            AddUnique(result, $"THE {token} HIDES BEHIND THE WALL");
            AddUnique(result, $"THE {token} HIDES BEHIND THE BOX");
            AddUnique(result, $"THE {token} JUMPS OVER THE ROCK");
            AddUnique(result, $"THE {token} RUNS AROUND THE SPIKES");
            AddUnique(result, $"THE {token} RUNS AROUND THE WATER");
            AddUnique(result, $"THE {token} WALKS FORWARD");
            AddUnique(result, $"THE {token} RUNS LEFT");
            AddUnique(result, $"THE {token} RUNS RIGHT");
            AddUnique(result, $"THE {token} DODGES BACKWARD");
            AddUnique(result, $"THE {token} RUNS TOWARD THE WALL");
            AddUnique(result, $"THE {token} RUNS AWAY FROM THE WALL");
            AddUnique(result, $"THE {token} SCRATCHES FAST");
            AddUnique(result, $"THE {token} BITES");
        }

        return result;
    }

    bool TrySummon(string phrase, PronunciationInsightResult? pronunciationInsight)
    {
        if (!CanSummonForActiveEnemy(phrase, out string summonError))
        {
            Publish(summonError);
            learningProfile?.RecordSpokenCast(phrase, false, false, 0f, null, Array.Empty<byte>(), false, false);
            RecordTacticalEvent(phrase, false, summonError, null, 0, pronunciationInsight);
            return true;
        }

        TacticalBattleCommandResult result = battler.SummonPlayer(phrase, ClampPosition(playerSummonPosition));
        if (result.success)
            ScheduleNextEnemyDecision(ResolveEnemyOpeningGraceSeconds());
        Publish(result.message);
        RefreshBoardVisuals();
        learningProfile?.RecordSpokenCast(phrase, result.success, false, 0f, null, Array.Empty<byte>(), false, false);
        RecordTacticalEvent(phrase, result.success, result.success ? "tactical_summon" : result.message, result.actionProfile, 0, pronunciationInsight);
        return true;
    }

    bool CanSummonForActiveEnemy(string phrase, out string error)
    {
        error = "";
        if (battler?.State?.enemyUnit == null || registry == null)
            return true;

        if (!registry.TryParsePhrase(phrase, out CreaturePhraseParseResult parsed) ||
            parsed.kind != CreaturePhraseKind.NounSummon ||
            parsed.noun == null)
        {
            return true;
        }

        string enemyFamily = CreaturePhraseUtility.NormalizeToken(battler.State.enemyUnit.noun);
        string summonFamily = CreaturePhraseUtility.NormalizeToken(parsed.noun.canonicalNoun);
        if (string.IsNullOrEmpty(enemyFamily) ||
            enemyFamily == "ENEMY" ||
            registry.AreInSameNounFamily(enemyFamily, summonFamily))
            return true;

        if (registry.TryGetNoun(enemyFamily, out NounDefinition enemyDefinition) && enemyDefinition != null)
        {
            string acceptedForms = string.Join(", ", enemyDefinition.AcceptedForms());
            error = string.IsNullOrWhiteSpace(acceptedForms)
                ? $"This battle needs the {enemyFamily} noun family."
                : $"This battle needs the {enemyFamily} noun family. Try: {acceptedForms}.";
        }
        else
        {
            error = $"This battle needs the {enemyFamily} noun family.";
        }

        return false;
    }

    void BeginBattle(WaveDescriptor descriptor)
    {
        if (!enableTacticalBattles || descriptor == null)
            return;

        ResolveReferences();
        activeDescriptor = descriptor;
        battler = new TacticalGrammarBattler(registry, TacticalGrammarBattleState.DefaultSize, TacticalGrammarBattleState.DefaultSize);
        PopulateTerrain(descriptor);
        battler.SetEnemyUnit(ResolveEnemyNoun(descriptor), ClampPosition(enemyPosition));
        activeShield = 0;
        shieldExpiresAt = -999f;
        pendingEnemyAttack = default;
        enemyDecisionCount = 0;
        ClearTacticalCurse();
        nextEnemyDecisionAt = float.PositiveInfinity;
        TacticalBattleSceneTransfer.SetPayload(BuildScenePayload(descriptor));
        TryLoadBattleScene();
        RefreshBoardVisuals();
        SuppressWorldCombatAgents();
        Publish("Tactical grid ready. Say and write a noun to summon.");
    }

    void EndBattle(WaveDescriptor descriptor, EncounterOutcome outcome)
    {
        if (descriptor != activeDescriptor)
            return;

        battler = null;
        activeDescriptor = null;
        nextEnemyDecisionAt = float.PositiveInfinity;
        ClearTacticalCurse();
        status = "";
        ClearBoardVisuals();
        if (unloadBattleSceneOnEnd)
            TryUnloadBattleScene();
        TacticalBattleSceneTransfer.Clear();
    }

    void EnsureActiveBattle()
    {
        ResolveReferences();
        if (IsActive || waveDirector == null || waveDirector.CurrentWaveDescriptor == null)
            return;

        BeginBattle(waveDirector.CurrentWaveDescriptor);
    }

    public bool TryTurnPlayerTowardCell(int x, int y)
    {
        EnsureActiveBattle();
        if (!IsActive || State?.playerUnit == null)
            return false;

        bool selected = battler.TrySelectTacticalCell(new TacticalBattlePosition(x, y), out string error);
        if (selected)
        {
            Publish($"Facing toward ({x},{y}). Attacks use the cone; movement previews and commands follow this heading.");
            RefreshBoardVisuals();
        }
        else if (!string.IsNullOrWhiteSpace(error))
        {
            Publish(error);
        }
        return selected;
    }

    public bool TryTurnPlayerTowardWorldPoint(Vector3 worldPoint)
    {
        EnsureActiveBattle();
        if (!IsActive || State == null)
            return false;

        Vector3 local = worldPoint - ResolveBoardOrigin();
        TacticalBattlePosition closest = default;
        float closestDistance = float.MaxValue;
        for (int x = 0; x < State.width; x++)
        {
            for (int y = 0; y < State.height; y++)
            {
                var candidate = new TacticalBattlePosition(x, y);
                float distance = Vector3.SqrMagnitude(GridToLocal(candidate) - local);
                if (distance < closestDistance)
                {
                    closest = candidate;
                    closestDistance = distance;
                }
            }
        }

        if (!State.IsInside(closest))
            return false;

        return TryTurnPlayerTowardCell(closest.x, closest.y);
    }

    void UpdateFacingFromPlayerView()
    {
        if (!followCameraFacing || battler == null || State?.playerUnit == null || battler.HasSelectedTacticalCell)
            return;

        Camera camera = Camera.main;
        if (camera == null)
            return;
        Vector3 forward = camera.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f)
            return;
        forward.Normalize();

        Vector3 playerWorld = ResolveBoardOrigin() + GridToLocal(State.playerUnit.position);
        Vector3 targetWorld = playerWorld + forward * Mathf.Max(cellSize * 4f, 4f);
        Vector3 local = targetWorld - ResolveBoardOrigin();
        TacticalBattlePosition closest = State.playerUnit.position;
        float closestDistance = float.MaxValue;
        for (int x = 0; x < State.width; x++)
        {
            for (int y = 0; y < State.height; y++)
            {
                var candidate = new TacticalBattlePosition(x, y);
                float distance = (GridToLocal(candidate) - local).sqrMagnitude;
                if (distance >= closestDistance)
                    continue;
                closestDistance = distance;
                closest = candidate;
            }
        }

        if (closest.x == State.playerUnit.position.x && closest.y == State.playerUnit.position.y)
            return;
        TacticalBattlePosition before = State.playerFacing;
        if (battler.FacePlayerToward(closest) && (before.x != State.playerFacing.x || before.y != State.playerFacing.y))
            RefreshBoardVisuals();
    }

    string ResolveEnemyNoun(WaveDescriptor descriptor)
    {
        if (descriptor?.enemyDefinition != null)
        {
            string noun = CreaturePhraseUtility.NormalizeToken(descriptor.enemyDefinition.EffectiveCreatureFamilyNoun);
            if (!string.IsNullOrEmpty(noun))
                return noun;
        }

        if (descriptor?.encounterNounFamilies != null)
        {
            foreach (string noun in descriptor.encounterNounFamilies)
            {
                string normalized = CreaturePhraseUtility.NormalizeToken(noun);
                if (!string.IsNullOrEmpty(normalized))
                    return normalized;
            }
        }

        SpellTarget target = ResolveTarget();
        return target != null ? CreaturePhraseUtility.NormalizeToken(target.RequiredCreatureNoun) : "ENEMY";
    }

    int ResolveDamage(TacticalBattleCommandResult result, out string rejectionMessage)
    {
        rejectionMessage = "";
        if (result == null || !result.success || result.actionProfile == null || State.playerUnit == null)
            return 0;

        if (!result.actionProfile.AttackAction)
            return 0;

        if (!battler.CanPlayerAttackTarget(result.actionProfile, out string reason))
        {
            rejectionMessage = $"No damage: {reason}";
            return 0;
        }

        return battler.ResolvePlayerAttackDamage(result.actionProfile);
    }

    bool TryDamageActiveEnemy(string noun, string verb, int damage)
    {
        bool damagedGridEnemy = false;
        if (State?.enemyUnit != null && State.enemyUnit.currentHp > 0 && damage > 0)
        {
            State.enemyUnit.currentHp = Mathf.Max(0, State.enemyUnit.currentHp - damage);
            damagedGridEnemy = true;
        }

        SpellTarget target = ResolveTarget();
        if (damagedGridEnemy && target != null)
            target.ReceiveDirectDamage(damage, verb);
        return damagedGridEnemy;
    }

    SpellTarget ResolveTarget()
    {
        ResolveReferences();
        if (aimAssist != null && aimAssist.TryGetSelectedTarget(out SpellTarget selected) && selected != null && !selected.IsDefeated)
            return selected;

        SpellTarget[] targets = FindObjectsByType<SpellTarget>(FindObjectsInactive.Exclude);
        SpellTarget best = null;
        float bestDistance = float.MaxValue;
        Vector3 origin = transform.position;
        foreach (SpellTarget target in targets)
        {
            if (target == null || target.IsDefeated)
                continue;
            float distance = Vector3.Distance(origin, target.transform.position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = target;
            }
        }

        return best;
    }

    void ApplyActionProtection(TacticalBattleActionProfile profile)
    {
        if (profile == null)
            return;

        if (!profile.DefenseAction || profile.shieldAmount <= 0)
            return;

        activeShield = Mathf.Max(activeShield, profile.shieldAmount);
        shieldExpiresAt = Mathf.Max(shieldExpiresAt, Time.time + profile.shieldDurationSeconds);
    }

    void RecordTacticalEvent(
        string phrase,
        bool success,
        string outcome,
        TacticalBattleActionProfile profile,
        int damage,
        PronunciationInsightResult? pronunciationInsight,
        string commandConjunction = "",
        GrammarBattleCurse activeCurseOverride = GrammarBattleCurse.None)
    {
        CurriculumSessionManager.Instance?.RecordGrammarBattleEvent(
            phrase,
            GrammarPhrasePattern.FullSentence,
            activeCurseOverride,
            profile != null ? profile.verb : "",
            profile != null && profile.movementAction ? "Movement" : "Tactical",
            success,
            outcome,
            damageDealt: Mathf.Max(0, damage),
            ppSpent: profile != null ? Mathf.Max(0, profile.ppCost) : 0,
            errorCategory: success ? "" : "tactical_rejected",
            enemyNounFamily: State?.enemyUnit != null ? State.enemyUnit.noun : "",
            commandPreposition: profile != null ? profile.preposition : "",
            commandConjunction: commandConjunction,
            pronunciationInsight: pronunciationInsight);
    }

    void ResolveReferences()
    {
        registry ??= GetComponent<CreatureCombatRegistry>() ?? GetComponentInParent<CreatureCombatRegistry>() ?? FindAnyObjectByType<CreatureCombatRegistry>();
        if (registry == null)
            registry = gameObject.AddComponent<CreatureCombatRegistry>();
        waveDirector ??= FindAnyObjectByType<EnemyWaveDirector>();
        learningProfile ??= GetComponent<PlayerLearningProfile>() ?? GetComponentInParent<PlayerLearningProfile>() ?? FindAnyObjectByType<PlayerLearningProfile>();
        aimAssist ??= GetComponent<PlayerAimAssist>() ?? GetComponentInParent<PlayerAimAssist>() ?? FindAnyObjectByType<PlayerAimAssist>();
    }

    void SuppressWorldCombatAgents()
    {
        if (!suppressWorldCombatAgents)
            return;

        EnemyAgentBase[] agents = FindObjectsByType<EnemyAgentBase>(FindObjectsInactive.Exclude);
        foreach (EnemyAgentBase agent in agents)
        {
            if (agent == null || !agent.enabled)
                continue;
            agent.enabled = false;
        }
    }

    public static bool AnyActiveTacticalBattle()
    {
        TacticalGrammarBattleController[] controllers = FindObjectsByType<TacticalGrammarBattleController>(FindObjectsInactive.Exclude);
        foreach (TacticalGrammarBattleController controller in controllers)
        {
            if (controller != null && controller.IsActive)
                return true;
        }

        return false;
    }

    void Subscribe()
    {
        if (waveDirector == null)
            return;

        waveDirector.OnWaveStarted -= BeginBattle;
        waveDirector.OnEncounterEnded -= EndBattle;
        waveDirector.OnWaveStarted += BeginBattle;
        waveDirector.OnEncounterEnded += EndBattle;
    }

    void Unsubscribe()
    {
        if (waveDirector == null)
            return;

        waveDirector.OnWaveStarted -= BeginBattle;
        waveDirector.OnEncounterEnded -= EndBattle;
    }

    TacticalBattlePosition ClampPosition(TacticalBattlePosition position)
    {
        return new TacticalBattlePosition(
            Mathf.Clamp(position.x, 0, TacticalGrammarBattleState.DefaultSize - 1),
            Mathf.Clamp(position.y, 0, TacticalGrammarBattleState.DefaultSize - 1));
    }

    void Publish(string message)
    {
        status = message ?? "";
        OnStatus?.Invoke(status);
        if (!string.IsNullOrWhiteSpace(status))
            Debug.Log($"[TacticalGrammarBattle] {status}", this);
    }

    static string FormatPosition(TacticalBattlePosition position) => $"({position.x},{position.y})";

    static char CellGlyph(TacticalBattleCellType cellType)
    {
        return cellType switch
        {
            TacticalBattleCellType.Box => 'B',
            TacticalBattleCellType.Spikes => '^',
            TacticalBattleCellType.Wall => 'W',
            TacticalBattleCellType.Roof => 'R',
            TacticalBattleCellType.Bridge => '=',
            TacticalBattleCellType.Water => '~',
            TacticalBattleCellType.Tree => 'T',
            TacticalBattleCellType.Rock => 'O',
            _ => '.',
        };
    }

    static void AddUnique(List<string> values, string value)
    {
        string normalized = CreaturePhraseUtility.NormalizeToken(value);
        if (!string.IsNullOrEmpty(normalized) && !values.Contains(normalized))
            values.Add(normalized);
    }

    struct TacticalEnemyTurnResult
    {
        public string message;
    }

    struct PendingEnemyAttack
    {
        public bool active;
        public int damage;
        public float attackSpeed;
        public float hitsAt;
        public float WindowSeconds => Mathf.Max(0f, hitsAt - Time.time);
    }
}
