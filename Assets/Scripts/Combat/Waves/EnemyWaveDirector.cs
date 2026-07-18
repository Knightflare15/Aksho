using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
#if UNITY_EDITOR
using UnityEditor;
#endif

public partial class EnemyWaveDirector : MonoBehaviour
{
    [Header("References")]
    public PlayerController playerController;
    public PlayerHealth playerHealth;
    public WordActionHandler wordActionHandler;
    public SpellRegistry spellRegistry;
    public CreatureCombatRegistry creatureRegistry;
    public BattleEncounterController battleEncounterController;
    public SpellPerformanceTracker spellPerformanceTracker;
    public PlayerLearningProfile playerLearningProfile;
    public LevelObjectiveDirector objectiveDirector;
    public EnemyAttackCoordinator attackCoordinator;
    public Transform arenaCenter;

    [Header("Legacy Compatibility")]
    [Tooltip("Permits the retired word-projectile registry only in an explicitly maintained legacy/sandbox scene.")]
    public bool legacyWordSpellFallbackEnabled;

    [Header("Progression")]
    [Min(1)] public int currentLevel = 1;
    public bool loadProgressFromProfile = true;
    public bool useObjectiveEscapeMode;
    public EnemyCatalog enemyCatalog;

    [Header("Activation")]
    public bool autoStart;
    public bool startWhenPlayerEntersArena = true;
    public float initialDelay = 1.25f;

    [Header("Exploration Pressure")]
    public float openingGraceSeconds = 25f;
    public float minPressureInterval = 35f;
    public float maxPressureInterval = 50f;
    public int earlyPressureEnemyCap = 3;
    public int latePressureEnemyCap = 5;

    [Header("Kid-Friendly Pacing")]
    public bool useKidFriendlyPacing = true;
    public float schoolOpeningGraceSeconds = 20f;
    public float schoolMinPressureInterval = 30f;
    public float schoolMaxPressureInterval = 45f;
    public int schoolEncounterEnemyCap = 4;
    public float optionalEncounterCooldownSeconds = 18f;

    [Header("Pillar Defense")]
    public int pillarDefenseBaseCount = 4;
    public int pillarDefenseMaxCount = 7;
    public float pressureConversionRadius = 55f;

    [Header("Escape Surge")]
    public float escapeEscalationSeconds = 60f;
    public float escapeBatchInterval = 12f;
    public float escalatedEscapeBatchInterval = 6f;
    public int escapeEnemyCap = 8;
    public int escalatedEscapeEnemyCap = 12;

    [Header("Spawn Placement")]
    public float minSpawnDistance = 18f;
    public float maxSpawnDistance = 42f;
    public float navMeshSampleRadius = 8f;
    public int spawnAttemptsPerEnemy = 40;
    public bool avoidVisibleSpawnPoints = true;

    [Header("Leashing")]
    public float leashDistance = 75f;
    public float leashDelaySeconds = 8f;
    public float leashCheckInterval = 0.5f;

    [Header("Retry / Transition")]
    public float retryDelaySeconds = 1.15f;
    public float levelAdvanceDelaySeconds = 1.6f;

    [Header("Creature Zones")]
    public bool lockPlayerDuringEncounters = true;
    [Min(4f)] public float encounterLockRadius = 24f;

    sealed class EnemyEncounterState
    {
        public EncounterType type;
        public Vector3 anchor;
        public float farSince = -1f;
    }

    readonly List<SpellTarget> aliveEnemies = new List<SpellTarget>();
    readonly Dictionary<SpellTarget, EnemyEncounterState> enemyStates = new Dictionary<SpellTarget, EnemyEncounterState>();
    readonly List<EnemyDefinition> emptyEnemyDefinitions = new List<EnemyDefinition>();
    readonly List<EnemyDefinition> runtimeFamilyEnemyDefinitions = new List<EnemyDefinition>();

    WaveDescriptor activeEncounter;
    SpellPillarObjective activePillar;
    EncounterPhase phase = EncounterPhase.Waiting;
    bool arenaActivated;
    bool activationRoutineStarted;
    bool isSpawningEncounter;
    bool isResettingArena;
    bool deathHookRegistered;
    bool loggedArenaNavMeshNotReady;
    Coroutine arenaResetRoutine;
    Coroutine levelAdvanceRoutine;
    int encountersStarted;
    int activeEncounterDefeats;
    int encounterStartingHp;
    float encounterStartedAt;
    float nextPressureAt;
    float nextEscapeBatchAt;
    float nextOptionalEncounterAllowedAt;
    float escapeStartedAt;
    float nextLeashCheckAt;
    string lastPlannedPrimaryWord = "";
    int consecutivePrimaryPlans;
    bool loggedMissingEnemyCatalog;
    EncounterLockZone activeEncounterLock;
    SemanticZoneKind pendingSemanticZoneKind = SemanticZoneKind.Route;
    string pendingGrammarTopic = "";
    int pendingGrammarTopicTier = 1;
    readonly List<string> pendingEncounterNounFamilies = new List<string>();
    readonly List<GrammarPhrasePattern> pendingPracticePatterns = new List<GrammarPhrasePattern>();
    readonly List<string> pendingMasteryTags = new List<string>();

    public int CurrentWave => encountersStarted;
    public int CurrentLevel => Mathf.Max(1, currentLevel);
    public int TotalWaves => Mathf.Max(1, encountersStarted);
    public int AliveEnemyCount => aliveEnemies.Count;
    public int WavesSpawnedThisStage => encountersStarted;
    public bool ArenaActivated => arenaActivated;
    public bool ArenaReadyForEnemySpawning => IsArenaReadyForEnemySpawning(false);
    public EncounterPhase CurrentPhase => phase;
    public WaveDescriptor CurrentWaveDescriptor => activeEncounter;
    public WaveDescriptor UpcomingWaveDescriptor => null;
    public float EscapeSecondsRemaining => phase == EncounterPhase.Escape
        ? Mathf.Max(0f, escapeEscalationSeconds - (Time.time - escapeStartedAt))
        : 0f;
    public bool EscapeEscalated => phase == EncounterPhase.Escape && EscapeSecondsRemaining <= 0f;

    public event System.Action<WaveDescriptor> OnWaveStarted;
    public event System.Action<WaveDescriptor, EncounterOutcome> OnEncounterEnded;
    public event System.Action<int> OnLevelCompleted;


}
