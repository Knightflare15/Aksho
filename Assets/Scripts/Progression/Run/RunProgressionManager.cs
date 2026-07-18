using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class RunProgressionManager : MonoBehaviour
{
    public enum RunGameMode
    {
        WorldGoalPractice,
        DailyRun = WorldGoalPractice,
        Sandbox
    }

    public static RunProgressionManager Instance { get; private set; }
    public static bool HasActiveInstance => Instance != null;

    [Header("Scenes")]
    public string mainMenuSceneName = "MainMenu";
    public string shopSceneName = "Shop";
    public string grammarWorldSceneName = GrammarWorldRuntimeBootstrap.DefaultGrammarWorldSceneName;
    public List<string> arenaSceneNames = new List<string> { "Level_1_Bat" };

    [Header("Run Completion")]
    [Min(1)] public int finalStageNumber = 5;

    [Header("Economy")]
    public int baseUpgradePrice = 8;
    public int pricePerStage = 4;
    [Range(0.1f, 1f)] public float shopPriceMultiplier = 0.6f;

    private readonly List<string> shuffledArenaCycle = new List<string>();
    private readonly Dictionary<RunUpgradeType, int> upgradeStacks = new Dictionary<RunUpgradeType, int>();
    private int arenaCycleIndex;
    private int stagesCompleted;
    private int enemiesDefeated;
    private int coinsCollected;
    private int coinsSpent;
    private int upgradesPurchased;
    private float runStartedAt;
    private CurriculumSessionManager curriculumSession;

    public int StageNumber { get; private set; } = 1;
    public int Coins { get; private set; }
    public int PendingWaveDebtTier { get; private set; }
    public int StoredCurrentHealth { get; private set; } = -1;
    public bool RunActive { get; private set; }
    public RunSummary LastRunSummary { get; private set; }
    public RunGameMode CurrentGameMode { get; private set; } = RunGameMode.WorldGoalPractice;
    public bool SchoolModeActive => curriculumSession != null && curriculumSession.IsSchoolModeActive;
    public bool SandboxModeActive =>
        CurrentGameMode == RunGameMode.Sandbox &&
        curriculumSession != null &&
        curriculumSession.IsDevSandboxMissionConfigured;
    public float ElapsedSeconds => runStartedAt > 0f ? Mathf.Max(0f, Time.unscaledTime - runStartedAt) : 0f;
    public float RemainingSeconds => SchoolModeActive ? curriculumSession.RemainingSeconds : 0f;
    public int CurrentSubArenaIndex => SchoolModeActive ? curriculumSession.CurrentSubArenaIndex : 1;
    public int SubArenasCleared => SchoolModeActive ? curriculumSession.SubArenasCleared : stagesCompleted;
    public int FullLoopsCleared => SchoolModeActive ? curriculumSession.FullLoopsCleared : 0;

    public int ExtraSpellbookSlots => GetUpgradeStacks(RunUpgradeType.SpellbookSlot);
    public int MaxAmmoBonus => GetUpgradeStacks(RunUpgradeType.MaxAmmo);
    public int MaxHealthBonus => GetUpgradeStacks(RunUpgradeType.MaxHealth);
    public float MoveSpeedMultiplier => 1f + GetUpgradeStacks(RunUpgradeType.MoveSpeed) * 0.1f;
    public float CoinMagnetRadius => 2.3f + GetUpgradeStacks(RunUpgradeType.CoinMagnet) * 1.2f;
    public int ShopChoiceCount => 3 + Mathf.Min(1, GetUpgradeStacks(RunUpgradeType.ShopChoice));

    public event System.Action<int> OnCoinsChanged;
    public event System.Action OnUpgradesChanged;
    public event System.Action OnRunStateChanged;
    public event System.Action<RunSummary> OnRunEnded;

    public static RunProgressionManager EnsureExists()
    {
        if (Instance != null)
            return Instance;

        Instance = FindAnyObjectByType<RunProgressionManager>();
        if (Instance != null)
        {
            PreserveAcrossScenes(Instance.gameObject);
            return Instance;
        }

        var go = new GameObject("RunProgressionManager");
        Instance = go.AddComponent<RunProgressionManager>();
        PreserveAcrossScenes(go);
        return Instance;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        PreserveAcrossScenes(gameObject);
        EnsureArenaSceneList();
        curriculumSession = CurriculumSessionManager.EnsureExists();
        Coins = PersistentCoinWallet.Coins;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    static void PreserveAcrossScenes(GameObject target)
    {
        // Unity rejects DontDestroyOnLoad in edit-mode tests. Runtime persistence is
        // still required for the actual scene-to-scene game flow.
        if (Application.isPlaying && target != null)
            DontDestroyOnLoad(target);
    }

    public void StartNewRun()
    {
        StartWorldGoalPractice();
    }

    public void StartWorldGoalPractice()
    {
        CurrentGameMode = RunGameMode.WorldGoalPractice;
        curriculumSession = CurriculumSessionManager.EnsureExists();
        if (curriculumSession != null && curriculumSession.schoolModeEnabled && !curriculumSession.HasStudentSession)
        {
            RunActive = false;
            Debug.LogWarning("[RunProgressionManager] World goal practice requires a logged-in student session. Sandbox runs remain available.");
            OnRunStateChanged?.Invoke();
            return;
        }

        curriculumSession?.ClearDevSandboxMission();
        StartRunAtStage(1);
    }

    [Obsolete("Use StartWorldGoalPractice for the weekly RPG goal flow.")]
    public void StartDailyRun()
    {
        StartWorldGoalPractice();
    }

    public void StartSandboxRun()
    {
        CurrentGameMode = RunGameMode.Sandbox;
        StartRunAtStage(1);
    }

    public void StartLocalDemoWorldGoalPractice()
    {
        CurrentGameMode = RunGameMode.WorldGoalPractice;
        curriculumSession = CurriculumSessionManager.EnsureExists();
        if (curriculumSession == null || !curriculumSession.IsDevSandboxMissionConfigured)
        {
            RunActive = false;
            Debug.LogWarning("[RunProgressionManager] Local demo world goal requires a configured sandbox mission.");
            OnRunStateChanged?.Invoke();
            return;
        }

        StartRunAtStage(1);
    }

    public void ResetForPlayerSelection()
    {
        CurrentGameMode = RunGameMode.WorldGoalPractice;
        StageNumber = 1;
        Coins = PersistentCoinWallet.Coins;
        PendingWaveDebtTier = 0;
        StoredCurrentHealth = -1;
        RunActive = false;
        runStartedAt = 0f;
        upgradeStacks.Clear();
        shuffledArenaCycle.Clear();
        arenaCycleIndex = 0;
        ResetRunStatistics();
        OnCoinsChanged?.Invoke(Coins);
        OnUpgradesChanged?.Invoke();
        OnRunStateChanged?.Invoke();
    }

    public void StartRunAtStage(int stage)
    {
        curriculumSession = CurriculumSessionManager.EnsureExists();
        if (curriculumSession != null && curriculumSession.schoolModeEnabled)
        {
            if (CurrentGameMode == RunGameMode.Sandbox)
            {
                if (!curriculumSession.IsDevSandboxMissionConfigured)
                {
                    Debug.LogWarning("[RunProgressionManager] Sandbox run requested without a configured sandbox mission. Falling back to world goal practice.");
                    CurrentGameMode = RunGameMode.WorldGoalPractice;
                    if (!curriculumSession.HasStudentSession)
                    {
                        RunActive = false;
                        Debug.LogWarning("[RunProgressionManager] World goal practice requires a logged-in student session. Sandbox runs remain available.");
                        OnRunStateChanged?.Invoke();
                        return;
                    }

                    MissionAssignment worldGoalPractice = curriculumSession.LoadWorldGoalPractice();
                    if (worldGoalPractice == null)
                    {
                        RunActive = false;
                        Debug.LogWarning("[RunProgressionManager] No world goal practice session is assigned. Sandbox runs remain available.");
                        OnRunStateChanged?.Invoke();
                        return;
                    }
                }
            }
            else
            {
                bool localDemoWorldGoal = curriculumSession.IsDevSandboxMissionConfigured;
                if (!curriculumSession.HasStudentSession && !localDemoWorldGoal)
                {
                    RunActive = false;
                    Debug.LogWarning("[RunProgressionManager] World goal practice requires a logged-in student session. Sandbox runs remain available.");
                    OnRunStateChanged?.Invoke();
                    return;
                }

                if (!localDemoWorldGoal)
                {
                    curriculumSession.ClearDevSandboxMission();
                    MissionAssignment worldGoalPractice = curriculumSession.LoadWorldGoalPractice();
                    if (worldGoalPractice == null)
                    {
                        RunActive = false;
                        Debug.LogWarning("[RunProgressionManager] No world goal practice session is assigned. Sandbox runs remain available.");
                        OnRunStateChanged?.Invoke();
                        return;
                    }
                }
            }

            curriculumSession.BeginMissionTimer();
        }

        StageNumber = Mathf.Max(1, stage);
        Coins = PersistentCoinWallet.Coins;
        PendingWaveDebtTier = 0;
        StoredCurrentHealth = -1;
        RunActive = true;
        runStartedAt = Time.unscaledTime;
        upgradeStacks.Clear();
        ResetRunStatistics();
        ShuffleArenaCycle();
        OnCoinsChanged?.Invoke(Coins);
        OnUpgradesChanged?.Invoke();
        OnRunStateChanged?.Invoke();
        if (CurrentGameMode == RunGameMode.WorldGoalPractice)
            LoadCurrentGrammarWorldScene(clearSavedPosition: true);
        else
            LoadNextArenaScene();
    }

    public void ContinueRunFromSave()
    {
        CurrentGameMode = RunGameMode.WorldGoalPractice;
        CurriculumSessionManager.Instance?.ClearDevSandboxMission();

        GrammarWorldProgressService progress = GrammarWorldProgressService.Instance;
        if (progress != null && progress.TryLoadSavedScene())
        {
            EnsureRunActive();
            return;
        }

        if (!RunActive)
        {
            PlayerLearningProfile.TryReadSummary(out _, out int savedLevel);
            StartRunAtStage(savedLevel);
        }
        else
        {
            LoadCurrentGrammarWorldScene(clearSavedPosition: false);
        }
    }

    public void EnsureRunActive()
    {
        Coins = PersistentCoinWallet.Coins;
        if (RunActive)
            return;

        curriculumSession = CurriculumSessionManager.EnsureExists();
        if (curriculumSession != null && curriculumSession.schoolModeEnabled && !curriculumSession.HasWorldGoalPractice)
        {
            if (!curriculumSession.HasStudentSession)
            {
                RunActive = false;
                Debug.LogWarning("[RunProgressionManager] World goal practice requires a logged-in student session. Sandbox runs remain available.");
                OnRunStateChanged?.Invoke();
                return;
            }

            CurrentGameMode = RunGameMode.WorldGoalPractice;
            MissionAssignment worldGoalPractice = curriculumSession.LoadWorldGoalPractice();
            if (worldGoalPractice == null)
            {
                RunActive = false;
                Debug.LogWarning("[RunProgressionManager] No world goal practice session is assigned. Sandbox runs remain available.");
                OnRunStateChanged?.Invoke();
                return;
            }
        }

        StageNumber = Mathf.Max(1, StageNumber);
        RunActive = true;
        if (runStartedAt <= 0f)
            runStartedAt = Time.unscaledTime;
        EnsureArenaSceneList();
        if (shuffledArenaCycle.Count == 0)
            ShuffleArenaCycle();
        OnRunStateChanged?.Invoke();
    }

    public void EnsureWalletBalanceFresh()
    {
        int walletCoins = PersistentCoinWallet.Coins;
        if (Coins == walletCoins)
            return;

        Coins = walletCoins;
        OnCoinsChanged?.Invoke(Coins);
    }

    public void AddCoins(int amount)
    {
        if (amount <= 0)
            return;

        Coins += amount;
        PersistentCoinWallet.AddCoins(amount);
        Coins = PersistentCoinWallet.Coins;
        coinsCollected += amount;
        OnCoinsChanged?.Invoke(Coins);
    }

    public bool TrySpendCoins(int amount)
    {
        amount = Mathf.Max(0, amount);
        Coins = PersistentCoinWallet.Coins;
        if (!PersistentCoinWallet.TrySpendCoins(amount))
            return false;

        Coins = PersistentCoinWallet.Coins;
        coinsSpent += amount;
        OnCoinsChanged?.Invoke(Coins);
        return true;
    }

    public void CompleteStage(int wavesSpawned, int expectedWaves)
    {
        EnsureRunActive();
        int completedStage = StageNumber;
        int extraWaves = Mathf.Max(0, wavesSpawned - Mathf.Max(1, expectedWaves));
        PendingWaveDebtTier = Mathf.Clamp(extraWaves / 2, 0, 3);
        stagesCompleted++;
        if (SchoolModeActive)
            curriculumSession.AdvanceSubArena();

        if (SchoolModeActive && curriculumSession.RemainingSeconds <= 0f)
        {
            EndRun(RunEndReason.TimeUp);
            return;
        }

        if (ShouldEndAfterStage(completedStage))
        {
            EndRun(RunEndReason.Victory);
            return;
        }

        StageNumber++;
        if (StoredCurrentHealth <= 0)
            StoredCurrentHealth = -1;
        OnRunStateChanged?.Invoke();
        if (SchoolModeActive)
            LoadNextArenaScene();
        else
            LoadShopSceneOrArenaFallback();
    }

    public void ResetRunAfterDeath()
    {
        if (CurrentGameMode == RunGameMode.Sandbox && curriculumSession != null && curriculumSession.IsDevSandboxMissionConfigured)
            StartSandboxRun();
        else
            StartWorldGoalPractice();
    }

    public bool ShouldEndAfterStage(int completedStage)
    {
        if (SchoolModeActive)
            return false;

        return completedStage >= Mathf.Max(1, finalStageNumber);
    }

    public void RecordEnemyDefeated()
    {
        if (RunActive)
            enemiesDefeated++;
    }

    public void EndRun(RunEndReason reason)
    {
        if (!RunActive)
            return;

        RunActive = false;
        LastRunSummary = new RunSummary
        {
            reason = reason,
            stagesCompleted = stagesCompleted,
            subarenasCleared = SubArenasCleared,
            fullLoopsCleared = FullLoopsCleared,
            enemiesDefeated = enemiesDefeated,
            coinsCollected = coinsCollected,
            coinsSpent = coinsSpent,
            upgradesPurchased = upgradesPurchased,
            elapsedSeconds = ElapsedSeconds,
            configuredDurationSeconds = SchoolModeActive ? curriculumSession.MissionDurationSeconds : 0,
            missionId = SchoolModeActive && curriculumSession.CurrentMission != null ? curriculumSession.CurrentMission.missionId : "",
        };
        curriculumSession?.SubmitRunSummary(LastRunSummary);
        OnRunStateChanged?.Invoke();
        OnRunEnded?.Invoke(LastRunSummary);
    }

    public void ReturnToMainMenu()
    {
        if (RunActive)
            EndRun(RunEndReason.Abandoned);

        ResetForPlayerSelection();
        if (!string.IsNullOrWhiteSpace(mainMenuSceneName) && Application.CanStreamedLevelBeLoaded(mainMenuSceneName))
            SceneManager.LoadScene(mainMenuSceneName);
        else
            Debug.LogWarning($"[RunProgressionManager] Main menu scene '{mainMenuSceneName}' is not in Build Settings.");
    }

    public int ConsumeWaveDebtTier()
    {
        int value = PendingWaveDebtTier;
        PendingWaveDebtTier = 0;
        return value;
    }

    public int PeekWaveDebtTier()
    {
        return PendingWaveDebtTier;
    }

    public List<RunUpgradeOffer> GenerateShopOffers()
    {
        return new List<RunUpgradeOffer>();
    }

    public bool TryPurchase(RunUpgradeOffer offer, PlayerHealth playerHealth = null)
    {
        return false;
    }

    public int GetUpgradeStacks(RunUpgradeType type)
    {
        return upgradeStacks.TryGetValue(type, out int stacks) ? stacks : 0;
    }

    public bool CanApplyUpgrade(RunUpgradeType type)
    {
        return type switch
        {
            RunUpgradeType.SpellbookSlot => GetUpgradeStacks(type) < 2,
            RunUpgradeType.MaxAmmo => GetUpgradeStacks(type) < 2,
            RunUpgradeType.MaxHealth => GetUpgradeStacks(type) < 6,
            RunUpgradeType.MoveSpeed => GetUpgradeStacks(type) < 4,
            RunUpgradeType.CoinMagnet => GetUpgradeStacks(type) < 4,
            RunUpgradeType.ShopChoice => GetUpgradeStacks(type) < 1,
            RunUpgradeType.HealToFull => true,
            _ => false,
        };
    }

    public void LoadNextArenaScene()
    {
        if (CurrentGameMode == RunGameMode.WorldGoalPractice)
        {
            LoadCurrentGrammarWorldScene(clearSavedPosition: false);
            return;
        }

        EnsureRunActive();
        EnsureArenaSceneList();
        string sceneName = GetNextArenaSceneName();
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogWarning("[RunProgressionManager] No arena scene configured.");
            return;
        }

        SceneManager.LoadScene(sceneName);
    }

    void LoadCurrentGrammarWorldScene(bool clearSavedPosition)
    {
        EnsureRunActive();
        GrammarWorldProgressService progress = GrammarWorldProgressService.Instance;
        string areaId = progress != null ? progress.Data.currentAreaId : "";
        if (string.IsNullOrWhiteSpace(areaId) && curriculumSession != null && curriculumSession.CurrentWorldGoal != null)
            areaId = curriculumSession.CurrentWorldGoal.targetAreaId;
        if (string.IsNullOrWhiteSpace(areaId))
            areaId = GrammarWorldRuntimeBootstrap.ResolveFirstTownAreaId();

        if (progress != null && progress.TryPrepareAndLoadArea(areaId, clearSavedPosition))
            return;

        string fallbackSceneName = !string.IsNullOrWhiteSpace(grammarWorldSceneName)
            ? grammarWorldSceneName
            : GrammarWorldRuntimeBootstrap.DefaultGrammarWorldSceneName;
        if (!string.IsNullOrWhiteSpace(fallbackSceneName) && Application.CanStreamedLevelBeLoaded(fallbackSceneName))
        {
            SceneManager.LoadScene(fallbackSceneName);
            return;
        }

        Debug.LogWarning("[RunProgressionManager] No grammar world template scene is configured in Build Settings.");
    }

    void ApplyUpgrade(RunUpgradeType type, PlayerHealth playerHealth)
    {
        if (type == RunUpgradeType.HealToFull)
        {
            if (playerHealth == null)
                playerHealth = FindAnyObjectByType<PlayerHealth>();
            if (playerHealth != null)
                playerHealth.RestoreFull();
            else
                StoredCurrentHealth = -1;
            OnUpgradesChanged?.Invoke();
            return;
        }

        upgradeStacks.TryGetValue(type, out int current);
        upgradeStacks[type] = current + 1;
        OnUpgradesChanged?.Invoke();
    }

    public void RecordPlayerHealth(int currentHp)
    {
        StoredCurrentHealth = Mathf.Max(0, currentHp);
    }

    public int ResolveStartingHealth(int maxHp)
    {
        if (StoredCurrentHealth <= 0)
            return Mathf.Max(1, maxHp);

        return Mathf.Clamp(StoredCurrentHealth, 1, Mathf.Max(1, maxHp));
    }

    void AddOfferIfAvailable(List<RunUpgradeOffer> offers, RunUpgradeType type, string name, string description)
    {
        if (!CanApplyUpgrade(type))
            return;

        offers.Add(new RunUpgradeOffer(type, name, description, GetPriceFor(type)));
    }

    int GetPriceFor(RunUpgradeType type)
    {
        int stagePrice = baseUpgradePrice + Mathf.Max(0, StageNumber - 1) * pricePerStage;
        int stackPrice = GetUpgradeStacks(type) * 5;
        int typePremium = type switch
        {
            RunUpgradeType.SpellbookSlot => 8,
            RunUpgradeType.ShopChoice => 10,
            RunUpgradeType.MaxHealth => 4,
            RunUpgradeType.HealToFull => -2,
            _ => 0,
        };

        int fullPrice = Mathf.Max(3, stagePrice + stackPrice + typePremium);
        return Mathf.Max(1, Mathf.RoundToInt(fullPrice * shopPriceMultiplier));
    }

    void LoadShopSceneOrArenaFallback()
    {
        if (!string.IsNullOrWhiteSpace(shopSceneName) && Application.CanStreamedLevelBeLoaded(shopSceneName))
        {
            SceneManager.LoadScene(shopSceneName);
            return;
        }

        Debug.LogWarning($"[RunProgressionManager] Shop scene '{shopSceneName}' is not in Build Settings. Loading next arena directly.");
        LoadNextArenaScene();
    }

    string GetNextArenaSceneName()
    {
        if (SchoolModeActive)
        {
            if (!string.IsNullOrWhiteSpace(grammarWorldSceneName) &&
                Application.CanStreamedLevelBeLoaded(grammarWorldSceneName))
                return grammarWorldSceneName;

            return curriculumSession.GetCurrentSubArenaSceneName(arenaSceneNames);
        }

        if (shuffledArenaCycle.Count == 0)
            ShuffleArenaCycle();

        if (shuffledArenaCycle.Count == 0)
            return "";

        if (arenaCycleIndex >= shuffledArenaCycle.Count)
            ShuffleArenaCycle();

        string sceneName = shuffledArenaCycle[Mathf.Clamp(arenaCycleIndex, 0, shuffledArenaCycle.Count - 1)];
        arenaCycleIndex++;
        return sceneName;
    }

    void ShuffleArenaCycle()
    {
        EnsureArenaSceneList();
        shuffledArenaCycle.Clear();
        shuffledArenaCycle.AddRange(arenaSceneNames);
        for (int i = 0; i < shuffledArenaCycle.Count; i++)
        {
            int swapIndex = UnityEngine.Random.Range(i, shuffledArenaCycle.Count);
            (shuffledArenaCycle[i], shuffledArenaCycle[swapIndex]) = (shuffledArenaCycle[swapIndex], shuffledArenaCycle[i]);
        }

        arenaCycleIndex = 0;
    }

    void EnsureArenaSceneList()
    {
        if (arenaSceneNames == null)
            arenaSceneNames = new List<string>();

        if (arenaSceneNames.Count == 0)
            arenaSceneNames.Add("Level_1_Bat");
    }

    void ResetRunStatistics()
    {
        stagesCompleted = 0;
        enemiesDefeated = 0;
        coinsCollected = 0;
        coinsSpent = 0;
        upgradesPurchased = 0;
        LastRunSummary = null;
    }
}
