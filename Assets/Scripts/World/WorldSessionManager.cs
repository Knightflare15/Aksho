using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Owns the one continuous Grammar RPG session. Teacher goals observe this
/// world; they never select a mode, gate startup, or choose the spawn area.
/// </summary>
[DisallowMultipleComponent]
public sealed class WorldSessionManager : MonoBehaviour
{
    public static WorldSessionManager Instance { get; private set; }

    [Header("Scenes")]
    public string mainMenuSceneName = "MainMenu";
    public string shopSceneName = "Shop";

    public bool SessionActive { get; private set; }

    public static WorldSessionManager EnsureExists()
    {
        if (Instance != null)
            return Instance;

        Instance = FindAnyObjectByType<WorldSessionManager>();
        if (Instance != null)
        {
            Preserve(Instance.gameObject);
            return Instance;
        }

        GameObject root = new GameObject("WorldSessionManager");
        Instance = root.AddComponent<WorldSessionManager>();
        Preserve(root);
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
        Preserve(gameObject);
        WorldEconomyService.EnsureExists();
        WorldGoalTracker.EnsureExists();
        CurriculumSessionManager.EnsureExists().LoadOptionalWorldGoal();
    }

    static void Preserve(GameObject target)
    {
        if (Application.isPlaying && target != null)
            DontDestroyOnLoad(target);
    }

    public void Play()
    {
        if (GrammarWorldProgressService.HasSavedWorldProgress())
            ContinueGame();
        else
            StartNewGame();
    }

    public void StartNewGame()
    {
        GrammarWorldProgressService progress = GrammarWorldProgressService.Instance;
        progress.ResetWorldProgress();
        SessionActive = true;
        LoadArea(GrammarWorldRuntimeBootstrap.ResolveFirstTownAreaId(), true);
    }

    public void ContinueGame()
    {
        SessionActive = true;
        GrammarWorldProgressService progress = GrammarWorldProgressService.Instance;
        if (progress.TryLoadSavedScene())
            return;

        LoadArea(GrammarWorldRuntimeBootstrap.ResolveFirstTownAreaId(), true);
    }

    public void RetryCurrentEncounter()
    {
        SessionActive = true;
        GrammarWorldProgressService progress = GrammarWorldProgressService.Instance;
        progress.PrepareCurrentAreaEncounterRetry();
        string areaId = progress.Data.currentAreaId;
        if (!LoadArea(areaId, false))
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void OpenShop()
    {
        if (Application.CanStreamedLevelBeLoaded(shopSceneName))
            SceneManager.LoadScene(shopSceneName);
        else
            Debug.LogWarning($"[WorldSession] Shop scene '{shopSceneName}' is not in Build Settings.");
    }

    public bool TravelToAreaForDevelopment(SemanticZoneKind kind, NaturalGrammarRegion region)
    {
        if (region == null)
            return false;

        string areaId = GrammarWorldProgressService.BuildAreaId(kind, region.grammarTopic, region.tier);
        return TravelToAreaForDevelopment(areaId);
    }

    public bool TravelToAreaForDevelopment(string areaId)
    {
        SessionActive = true;
        return LoadArea(areaId, true);
    }

    public void ReturnToMainMenu()
    {
        SessionActive = false;
        Time.timeScale = 1f;
        if (Application.CanStreamedLevelBeLoaded(mainMenuSceneName))
            SceneManager.LoadScene(mainMenuSceneName);
        else
            Debug.LogWarning($"[WorldSession] Main menu scene '{mainMenuSceneName}' is not in Build Settings.");
    }

    bool LoadArea(string areaId, bool clearSavedPosition)
    {
        return !string.IsNullOrWhiteSpace(areaId) &&
               GrammarWorldProgressService.Instance.TryPrepareAndLoadArea(areaId, clearSavedPosition);
    }
}
