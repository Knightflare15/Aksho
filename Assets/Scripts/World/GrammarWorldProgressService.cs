using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[Serializable]
public sealed class GrammarMapAreaState
{
    public string areaId = "";
    public string displayName = "";
    public string sceneName = "";
    public SemanticZoneKind sceneKind = SemanticZoneKind.Town;
    public GrammarConceptId conceptId = GrammarConceptId.None;
    public string grammarTopic = "";
    public int grammarTopicTier = 1;
    public Vector2 mapPosition;
    public bool explored;
    public bool visible;
    public bool objectiveCompleted;
    public bool encounterCompleted;
    public List<string> connectedAreaIds = new List<string>();
}

[Serializable]
public sealed class GrammarWorldProgressData
{
    public string currentAreaId = "";
    public string currentSceneName = "";
    public Vector3 lastPlayerPosition;
    public bool hasLastPlayerPosition;
    public int encountersCompleted;
    public List<GrammarMapAreaState> areas = new List<GrammarMapAreaState>();
    public List<string> completedAreaIds = new List<string>();
    public List<string> clearedGymAreaIds = new List<string>();
    public List<string> unlockedGrammarPatterns = new List<string>();
    public List<string> unlockedVocabulary = new List<string>();
    public List<string> unlockedConceptIds = new List<string>();
    public List<string> masteredConceptIds = new List<string>();
    public List<string> completedDialogueTaskKeys = new List<string>();
    public bool campaignCompleted;
    public string campaignCompletedAtUtc = "";
}

[DisallowMultipleComponent]
public sealed partial class GrammarWorldProgressService : MonoBehaviour
{
    const string FileName = "grammar_world_progress.json";

    static GrammarWorldProgressService instance;

    readonly Dictionary<string, GrammarMapAreaState> areaLookup = new Dictionary<string, GrammarMapAreaState>(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<EnemyWaveDirector> hookedDirectors = new HashSet<EnemyWaveDirector>();
    GrammarWorldProgressData data;
    public event Action<GrammarMapAreaState> OnAreaCompleted;
    public event Action OnCampaignCompleted;

    public static GrammarWorldProgressService Instance => EnsureExists();
    public GrammarWorldProgressData Data
    {
        get
        {
            EnsureLoaded();
            return data;
        }
    }

    public void ResetWorldProgress()
    {
        data = new GrammarWorldProgressData();
        areaLookup.Clear();
        SeedDefaultMapIfNeeded();
        RebuildLookup();
        Save();
    }

    public void ReloadForActiveProfile()
    {
        data = null;
        areaLookup.Clear();
        EnsureLoaded();
    }

    public void PrepareCurrentAreaEncounterRetry()
    {
        EnsureLoaded();
        if (string.IsNullOrWhiteSpace(data.currentAreaId))
            return;

        GrammarMapAreaState area = EnsureArea(data.currentAreaId);
        if (!area.objectiveCompleted)
            area.encounterCompleted = false;
        data.hasLastPlayerPosition = false;
        Save();
    }

    public static bool HasSavedWorldProgress()
    {
        string path = PlayerSaveSlots.GetSaveFilePath(FileName);
        if (!File.Exists(path))
            return false;

        try
        {
            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
                return false;

            GrammarWorldProgressData saved = JsonUtility.FromJson<GrammarWorldProgressData>(json);
            return saved != null &&
                (!string.IsNullOrWhiteSpace(saved.currentSceneName) ||
                 !string.IsNullOrWhiteSpace(saved.currentAreaId) ||
                 saved.hasLastPlayerPosition);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[GrammarWorldProgress] Could not inspect saved world progress: {ex.Message}");
            return false;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        EnsureExists();
    }

    public static GrammarWorldProgressService EnsureExists()
    {
        if (instance != null)
            return instance;

        instance = FindAnyObjectByType<GrammarWorldProgressService>();
        if (instance != null)
        {
            PreserveAcrossScenes(instance.gameObject);
            return instance;
        }

        GameObject go = new GameObject("GrammarWorldProgressService");
        instance = go.AddComponent<GrammarWorldProgressService>();
        PreserveAcrossScenes(go);
        return instance;
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        PreserveAcrossScenes(gameObject);
        EnsureLoaded();
    }

    void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    static void PreserveAcrossScenes(GameObject target)
    {
        if (Application.isPlaying && target != null)
            DontDestroyOnLoad(target);
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        foreach (EnemyWaveDirector director in hookedDirectors)
        {
            if (director != null)
                director.OnEncounterEnded -= HandleEncounterEnded;
        }
        hookedDirectors.Clear();
    }

    void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureLoaded();
        AttachToSceneDirector();
    }

    public void RegisterCurrentScene(GrammarSceneController controller)
    {
        if (controller == null)
            return;

        // Registering a scene is an explicit ownership handoff. This also
        // prevents stale edit-mode service instances from receiving progress
        // and telemetry for a newly loaded world scene.
        instance = this;
        EnsureLoaded();
        Scene scene = controller.gameObject.scene;
        string sceneName = scene.IsValid() ? scene.name : SceneManager.GetActiveScene().name;
        string areaId = ResolveAreaId(controller);
        bool shouldRestorePosition =
            data.hasLastPlayerPosition &&
            controller.sceneKind != SemanticZoneKind.Route &&
            string.Equals(data.currentAreaId, areaId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(data.currentSceneName, sceneName, StringComparison.OrdinalIgnoreCase);
        GrammarMapAreaState area = EnsureArea(areaId);
        area.displayName = ResolveDisplayName(controller);
        area.sceneName = sceneName;
        area.sceneKind = controller.sceneKind;
        area.conceptId = ResolveConceptId(controller.grammarTopic, controller.grammarTopicTier);
        area.grammarTopic = controller.grammarTopic ?? "";
        area.grammarTopicTier = Mathf.Max(1, controller.grammarTopicTier);
        area.mapPosition = controller.mapPosition;
        area.visible = true;
        area.explored = true;

        if (controller.connectedMapAreaIds != null)
        {
            foreach (string connected in controller.connectedMapAreaIds)
                AddConnection(area, connected);
        }

        SyncCompletionLists(area);
        if (area.objectiveCompleted)
            RevealConnected(area);
        data.currentAreaId = area.areaId;
        data.currentSceneName = sceneName;
        if (shouldRestorePosition)
            RestorePlayerPosition(data.lastPlayerPosition);
        CapturePlayerPosition();
        Save();
        AttachToSceneDirector();
    }

    public bool TryLoadSavedScene()
    {
        EnsureLoaded();
        string sceneName = data != null ? data.currentSceneName : "";
        if (!string.IsNullOrWhiteSpace(sceneName) && Application.CanStreamedLevelBeLoaded(sceneName))
        {
            SceneManager.LoadScene(sceneName);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(sceneName))
            Debug.LogWarning($"[GrammarWorldProgress] Saved scene '{sceneName}' is not in Build Settings.");

        string areaId = data != null ? data.currentAreaId : "";
        if (!string.IsNullOrWhiteSpace(areaId))
        {
            string resolvedSceneName = GrammarWorldRuntimeBootstrap.ResolveSceneNameForAreaId(areaId);
            if (Application.CanStreamedLevelBeLoaded(resolvedSceneName))
            {
                data.currentSceneName = resolvedSceneName;
                Save();
                SceneManager.LoadScene(resolvedSceneName);
                return true;
            }

            Debug.LogWarning($"[GrammarWorldProgress] Resolved scene '{resolvedSceneName}' for area '{areaId}' is not in Build Settings.");
        }

        string firstTownAreaId = GrammarWorldRuntimeBootstrap.ResolveFirstTownAreaId();
        if (!string.IsNullOrWhiteSpace(firstTownAreaId))
            return TryPrepareAndLoadArea(firstTownAreaId, clearSavedPosition: true);

        return false;
    }

    public bool TryPrepareAndLoadArea(string areaId, bool clearSavedPosition = true)
    {
        EnsureLoaded();
        string normalizedAreaId = CanonicalizeAreaId(areaId);
        if (string.IsNullOrWhiteSpace(normalizedAreaId))
            return false;

        string sceneName = GrammarWorldRuntimeBootstrap.ResolveSceneNameForAreaId(normalizedAreaId);
        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogWarning($"[GrammarWorldProgress] Area '{normalizedAreaId}' needs scene '{sceneName}', but it is not in Build Settings.");
            return false;
        }

        PrepareAreaTransition(normalizedAreaId, sceneName, clearSavedPosition);
        SceneManager.LoadScene(sceneName);
        return true;
    }

    public bool IsGymCleared(string gymAreaId)
    {
        EnsureLoaded();
        string normalized = CanonicalizeAreaId(gymAreaId);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        data.clearedGymAreaIds ??= new List<string>();
        foreach (string clearedGymAreaId in data.clearedGymAreaIds)
        {
            if (string.Equals(CanonicalizeAreaId(clearedGymAreaId), normalized, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (data.areas == null)
            return false;

        foreach (GrammarMapAreaState area in data.areas)
        {
            if (area == null || !string.Equals(CanonicalizeAreaId(area.areaId), normalized, StringComparison.OrdinalIgnoreCase))
                continue;
            return area.sceneKind == SemanticZoneKind.Gym && area.objectiveCompleted;
        }
        return false;
    }

    public bool IsCurrentAreaObjectiveCompleted()
    {
        EnsureLoaded();
        return IsAreaObjectiveCompleted(data != null ? data.currentAreaId : "");
    }

    public bool IsAreaObjectiveCompleted(string areaId)
    {
        EnsureLoaded();
        string normalized = CanonicalizeAreaId(areaId);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        data.completedAreaIds ??= new List<string>();
        foreach (string completedAreaId in data.completedAreaIds)
        {
            if (string.Equals(CanonicalizeAreaId(completedAreaId), normalized, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (data.areas == null)
            return false;

        foreach (GrammarMapAreaState area in data.areas)
        {
            if (area != null &&
                area.objectiveCompleted &&
                string.Equals(CanonicalizeAreaId(area.areaId), normalized, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public void PrepareAreaTransition(string areaId, string sceneName, bool clearSavedPosition = true)
    {
        EnsureLoaded();
        string normalizedAreaId = CanonicalizeAreaId(areaId);
        if (string.IsNullOrWhiteSpace(normalizedAreaId))
            return;

        GrammarMapAreaState area = EnsureArea(normalizedAreaId);
        area.visible = true;
        if (!string.IsNullOrWhiteSpace(sceneName))
            area.sceneName = sceneName.Trim();

        data.currentAreaId = area.areaId;
        data.currentSceneName = string.IsNullOrWhiteSpace(sceneName)
            ? area.sceneName ?? ""
            : sceneName.Trim();
        if (clearSavedPosition)
            data.hasLastPlayerPosition = false;
        Save();
    }

    public void Save()
    {
        EnsureLoaded();
        CapturePlayerPosition();
        PlayerSaveSlots.EnsureActiveSlotDirectory();
        File.WriteAllText(PlayerSaveSlots.GetSaveFilePath(FileName), JsonUtility.ToJson(data, true));
    }

    public static string BuildAreaId(SemanticZoneKind kind, string grammarTopic, int grammarTopicTier)
    {
        string topic = BuildAreaTopicKey(grammarTopic);
        if (string.IsNullOrEmpty(topic))
            topic = kind.ToString().ToUpperInvariant();
        return $"{kind.ToString().ToUpperInvariant()}:{topic}:{Mathf.Max(1, grammarTopicTier)}";
    }

    public static string BuildAreaTopicKey(string grammarTopic)
    {
        string normalized = SpellRegistry.NormalizeWord(grammarTopic);
        if (string.IsNullOrEmpty(normalized))
            return "";

        var builder = new StringBuilder(normalized.Length);
        foreach (char character in normalized)
        {
            if (char.IsLetterOrDigit(character))
                builder.Append(char.ToUpperInvariant(character));
        }

        return builder.ToString();
    }

    public static string ResolveAreaId(GrammarSceneController controller)
    {
        if (controller == null)
            return "";
        if (!string.IsNullOrWhiteSpace(controller.mapAreaId))
            return controller.mapAreaId.Trim();

        string topic = controller.grammarTopic;
        int tier = controller.grammarTopicTier;
        if (controller.sceneKind == SemanticZoneKind.Route &&
            GrammarRouteContext.Instance != null &&
            !string.IsNullOrWhiteSpace(GrammarRouteContext.Instance.sourceGrammarTopic))
        {
            topic = GrammarRouteContext.Instance.sourceGrammarTopic;
            tier = GrammarRouteContext.Instance.sourceGrammarTopicTier;
        }

        return BuildAreaId(controller.sceneKind, topic, tier);
    }

    static string ResolveDisplayName(GrammarSceneController controller)
    {
        if (controller == null)
            return "";
        if (!string.IsNullOrWhiteSpace(controller.mapDisplayName))
            return controller.mapDisplayName.Trim();
        string topic = string.IsNullOrWhiteSpace(controller.grammarTopic) ? "Grammar" : controller.grammarTopic.Trim();
        return controller.sceneKind switch
        {
            SemanticZoneKind.Gym => $"{topic} Gym",
            SemanticZoneKind.Route => $"{topic} Route",
            _ => $"{topic} Town",
        };
    }

    void AttachToSceneDirector()
    {
        foreach (EnemyWaveDirector director in FindObjectsByType<EnemyWaveDirector>(FindObjectsInactive.Exclude))
        {
            if (director == null || hookedDirectors.Contains(director))
                continue;
            director.OnEncounterEnded += HandleEncounterEnded;
            hookedDirectors.Add(director);
        }
    }

    void CapturePlayerPosition()
    {
        PlayerController player = FindAnyObjectByType<PlayerController>();
        if (player == null || data == null)
            return;

        data.lastPlayerPosition = player.transform.position;
        data.hasLastPlayerPosition = true;
    }

    static void RestorePlayerPosition(Vector3 position)
    {
        PlayerController player = FindAnyObjectByType<PlayerController>();
        if (player == null)
            return;

        CharacterController character = player.GetComponent<CharacterController>();
        if (character != null)
            character.enabled = false;
        player.transform.position = position;
        if (character != null)
            character.enabled = true;
    }

    void BackfillUnlockedRewardsFromCompletedAreas()
    {
        if (data == null || data.areas == null)
            return;

        foreach (GrammarMapAreaState area in data.areas)
        {
            if (area == null)
                continue;
            if (area.objectiveCompleted ||
                (data.completedAreaIds != null && data.completedAreaIds.Contains(area.areaId)))
            {
                UnlockRegionRewards(area);
            }
        }
    }

    GrammarMapAreaState EnsureArea(string areaId)
    {
        if (string.IsNullOrWhiteSpace(areaId))
            areaId = "UNKNOWN";
        RebuildLookup();
        if (areaLookup.TryGetValue(areaId, out GrammarMapAreaState existing))
            return existing;

        var area = new GrammarMapAreaState
        {
            areaId = areaId,
            displayName = areaId,
            visible = true,
            explored = false,
        };
        data.areas.Add(area);
        areaLookup[area.areaId] = area;
        return area;
    }

    void RevealConnected(GrammarMapAreaState area)
    {
        if (area == null || area.connectedAreaIds == null)
            return;

        foreach (string connectedId in area.connectedAreaIds)
        {
            if (string.IsNullOrWhiteSpace(connectedId))
                continue;
            GrammarMapAreaState connected = EnsureArea(connectedId);
            connected.visible = true;
        }
    }

    void AddConnection(GrammarMapAreaState area, string connectedId)
    {
        if (area == null || string.IsNullOrWhiteSpace(connectedId))
            return;
        if (area.connectedAreaIds == null)
            area.connectedAreaIds = new List<string>();
        string normalized = connectedId.Trim();
        if (!area.connectedAreaIds.Contains(normalized))
            area.connectedAreaIds.Add(normalized);
    }

}

public sealed class GrammarMapUI : MonoBehaviour
{
    static GrammarMapUI instance;

    Canvas canvas;
    RectTransform panel;
    RectTransform content;
    readonly List<GameObject> spawnedItems = new List<GameObject>();

    public static GrammarMapUI EnsureExists()
    {
        if (instance != null)
            return instance;

        GrammarMapUI existing = FindAnyObjectByType<GrammarMapUI>();
        if (existing != null)
        {
            instance = existing;
            return instance;
        }

        GameObject go = new GameObject("GrammarMapUI");
        instance = go.AddComponent<GrammarMapUI>();
        DontDestroyOnLoad(go);
        return instance;
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        Build();
        Hide();
    }

    void Update()
    {
        if (panel != null && panel.gameObject.activeSelf && Input.GetKeyDown(KeyCode.Escape))
            Hide();
    }

    public void Open()
    {
        Build();
        Refresh();
        panel.gameObject.SetActive(true);
    }

    public void Hide()
    {
        if (panel != null)
            panel.gameObject.SetActive(false);
    }

    void Refresh()
    {
        foreach (GameObject item in spawnedItems)
            Destroy(item);
        spawnedItems.Clear();

        GrammarWorldProgressData progress = GrammarWorldProgressService.Instance.Data;
        if (progress == null || progress.areas == null)
            return;
        LearnerScheduleRecommendation recommendation = CurriculumSessionManager.EnsureExists()
            .RefreshLearnerRecommendation(false);
        string recommendedAreaId = ResolveRecommendedAreaId(progress, recommendation);

        foreach (GrammarMapAreaState area in progress.areas)
        {
            if (area == null || !area.visible)
                continue;

            foreach (string connectedId in area.connectedAreaIds)
            {
                GrammarMapAreaState other = progress.areas.Find(x => x != null && x.areaId == connectedId);
                if (other == null || !other.visible)
                    continue;
                SpawnRouteLine(area, other, area.explored && other.explored);
            }
        }

        foreach (GrammarMapAreaState area in progress.areas)
        {
            if (area == null || !area.visible)
                continue;
            SpawnAreaNode(
                area,
                string.Equals(progress.currentAreaId, area.areaId, StringComparison.OrdinalIgnoreCase),
                string.Equals(recommendedAreaId, area.areaId, StringComparison.OrdinalIgnoreCase));
        }
    }

    static string ResolveRecommendedAreaId(GrammarWorldProgressData progress, LearnerScheduleRecommendation recommendation)
    {
        if (progress?.areas == null || recommendation == null || string.IsNullOrWhiteSpace(recommendation.conceptId))
            return "";

        SemanticZoneKind preferredZone = recommendation.activityType switch
        {
            "challenge" => SemanticZoneKind.Gym,
            "retrieval_review" => SemanticZoneKind.Route,
            "guided_practice" => SemanticZoneKind.Route,
            _ => SemanticZoneKind.Town,
        };
        GrammarMapAreaState fallback = null;
        foreach (GrammarMapAreaState area in progress.areas)
        {
            if (area == null || !area.visible ||
                !string.Equals(area.conceptId.ToString(), recommendation.conceptId, StringComparison.OrdinalIgnoreCase))
                continue;
            fallback ??= area;
            if (area.sceneKind == preferredZone)
                return area.areaId;
        }
        return fallback?.areaId ?? "";
    }

    void SpawnRouteLine(GrammarMapAreaState from, GrammarMapAreaState to, bool explored)
    {
        Vector2 delta = to.mapPosition - from.mapPosition;
        float length = delta.magnitude;
        if (length <= 0.01f)
            return;

        GameObject go = new GameObject($"Route_{from.areaId}_to_{to.areaId}", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(content, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = (from.mapPosition + to.mapPosition) * 0.5f;
        rt.sizeDelta = new Vector2(length, explored ? 14f : 8f);
        rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        Image image = go.GetComponent<Image>();
        image.color = explored ? new Color(1f, 0.56f, 0.34f, 1f) : new Color(0.52f, 0.6f, 0.66f, 0.45f);
        spawnedItems.Add(go);
    }

    void SpawnAreaNode(GrammarMapAreaState area, bool current, bool recommended)
    {
        GameObject go = new GameObject($"Area_{area.areaId}", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(content, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = area.mapPosition;
        rt.sizeDelta = area.sceneKind == SemanticZoneKind.Route ? new Vector2(76f, 30f) : new Vector2(58f, 42f);
        Image image = go.GetComponent<Image>();
        image.color = ResolveNodeColor(area, current, recommended);
        spawnedItems.Add(go);

        GameObject labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(go.transform, false);
        RectTransform labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = new Vector2(0.5f, 1f);
        labelRt.anchorMax = new Vector2(0.5f, 1f);
        labelRt.pivot = new Vector2(0.5f, 0f);
        labelRt.anchoredPosition = new Vector2(0f, 4f);
        labelRt.sizeDelta = new Vector2(150f, 44f);
        TextMeshProUGUI label = labelGo.GetComponent<TextMeshProUGUI>();
        label.text = recommended ? $"★ {area.displayName}\nRecommended" : area.displayName;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.Normal;
        GameUiTheme.StyleText(label, current || recommended ? 18f : 15f, current || recommended);
        label.color = recommended
            ? GameUiTheme.Gold
            : area.explored ? GameUiTheme.Text : new Color(0.7f, 0.75f, 0.8f, 0.78f);
    }

    static Color ResolveNodeColor(GrammarMapAreaState area, bool current, bool recommended)
    {
        if (current)
            return new Color(1f, 0.9f, 0.28f, 1f);
        if (recommended)
            return new Color(0.22f, 0.78f, 0.46f, 1f);
        if (!area.explored)
            return new Color(0.48f, 0.54f, 0.62f, 0.62f);
        return area.sceneKind switch
        {
            SemanticZoneKind.Gym => new Color(1f, 0.24f, 0.28f, 1f),
            SemanticZoneKind.Route => new Color(0.26f, 0.54f, 1f, 1f),
            _ => new Color(0.96f, 0.28f, 0.36f, 1f),
        };
    }

    void Build()
    {
        if (canvas != null)
            return;

        GameObject root = new GameObject("GrammarMapCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1010;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        GameObject shade = new GameObject("MapShade", typeof(RectTransform), typeof(Image));
        shade.transform.SetParent(root.transform, false);
        RectTransform shadeRt = shade.GetComponent<RectTransform>();
        shadeRt.anchorMin = Vector2.zero;
        shadeRt.anchorMax = Vector2.one;
        shadeRt.offsetMin = shadeRt.offsetMax = Vector2.zero;
        shade.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);
        panel = shadeRt;

        GameObject frame = new GameObject("MapFrame", typeof(RectTransform), typeof(Image));
        frame.transform.SetParent(shade.transform, false);
        RectTransform frameRt = frame.GetComponent<RectTransform>();
        frameRt.anchorMin = frameRt.anchorMax = new Vector2(0.5f, 0.5f);
        frameRt.pivot = new Vector2(0.5f, 0.5f);
        frameRt.sizeDelta = new Vector2(1220f, 720f);
        GameUiTheme.StylePanel(frame);

        TextMeshProUGUI title = MakeText(frameRt, "Region Map", 30f, true, new Vector2(0f, 312f), new Vector2(900f, 48f));
        title.color = GameUiTheme.Gold;

        GameObject closeGo = new GameObject("Close", typeof(RectTransform), typeof(Image), typeof(Button));
        closeGo.transform.SetParent(frame.transform, false);
        RectTransform closeRt = closeGo.GetComponent<RectTransform>();
        closeRt.anchorMin = closeRt.anchorMax = new Vector2(1f, 1f);
        closeRt.pivot = new Vector2(1f, 1f);
        closeRt.anchoredPosition = new Vector2(-24f, -22f);
        closeRt.sizeDelta = new Vector2(120f, 46f);
        Button close = closeGo.GetComponent<Button>();
        close.onClick.AddListener(Hide);
        GameUiTheme.StyleButton(close, GameUiTheme.ButtonRole.Secondary);
        MakeText(closeRt, "Close", 18f, true, Vector2.zero, closeRt.sizeDelta);

        GameObject scrollGo = new GameObject("MapScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(Mask));
        scrollGo.transform.SetParent(frame.transform, false);
        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchorMin = Vector2.zero;
        scrollRt.anchorMax = Vector2.one;
        scrollRt.offsetMin = new Vector2(42f, 42f);
        scrollRt.offsetMax = new Vector2(-42f, -92f);
        scrollGo.GetComponent<Image>().color = new Color(0.08f, 0.2f, 0.26f, 0.94f);
        scrollGo.GetComponent<Mask>().showMaskGraphic = true;

        GameObject contentGo = new GameObject("MapContent", typeof(RectTransform));
        contentGo.transform.SetParent(scrollGo.transform, false);
        content = contentGo.GetComponent<RectTransform>();
        content.anchorMin = Vector2.zero;
        content.anchorMax = Vector2.zero;
        content.pivot = Vector2.zero;
        content.sizeDelta = new Vector2(1600f, 640f);

        ScrollRect scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.content = content;
        scroll.horizontal = true;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Elastic;
    }

    static TextMeshProUGUI MakeText(RectTransform parent, string text, float size, bool bold, Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        GameObject go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = sizeDelta;
        TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.alignment = TextAlignmentOptions.Center;
        GameUiTheme.StyleText(label, size, bold);
        return label;
    }
}
