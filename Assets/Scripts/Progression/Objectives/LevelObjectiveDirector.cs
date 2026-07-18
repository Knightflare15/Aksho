using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Runtime stage-objective spawner.
///
/// Prefab authoring checklist:
/// - Pillar prefab: put SpellPillarObjective on the root. Child mesh/collider is enough. Child TMP label is optional.
/// - Coin prefab: optional visual used inside chest counting mini-games.
/// - Treasure chest prefab: optional interactable reward container. Empty uses a cube fallback.
/// - Exit prefab: put ExitPortalObjective on the root. Child mesh/collider is enough.
/// - Leave any prefab field empty to use the built-in primitive fallback.
/// </summary>
public class LevelObjectiveDirector : MonoBehaviour
{
    [Header("References")]
    public PlayerController playerController;
    public SpellRegistry spellRegistry;
    public EnemyWaveDirector waveDirector;
    public Transform arenaCenter;
    [Tooltip("Optional hierarchy bucket for spawned pillars, coins, and the exit portal.")]
    public Transform runtimeParent;

    [Header("Prefab Wiring")]
    [Tooltip("Optional custom pillar prefab. Required component: SpellPillarObjective on the root. Child mesh/collider is enough. TMP label is optional.")]
    public SpellPillarObjective pillarPrefab;
    [Tooltip("Optional custom coin prefab. Required component: CoinPickup on the root. Child mesh/collider is enough.")]
    public CoinPickup coinPrefab;
    [Tooltip("Optional custom treasure chest prefab. If empty, a cube fallback is used.")]
    public GameObject treasureChestPrefab;
    [Tooltip("Optional pronunciation clips for numbers 1 through 10. TTS/log fallback is used when clips are missing.")]
    public AudioClip[] numberPronunciationClips;
    [Tooltip("Optional custom exit prefab. Required component: ExitPortalObjective on the root. Child mesh/collider is enough.")]
    public ExitPortalObjective exitPortalPrefab;

    [Header("Pillars")]
    public int minPillars = 3;
    public int maxPillars = 5;
    public float idealPillarSpacing = 52f;
    public float minimumPillarSpacing = 24f;
    public float minimumPillarPlacementRadius = 35f;
    public float placementRadius = 160f;
    public int placementAttempts = 140;
    [Tooltip("Minimum horizontal distance from any non-runtime collider. Runtime enforces at least 5m.")]
    public float pillarClearanceRadius = 5f;
    [Tooltip("Legacy field kept for scene compatibility; pillar placement now checks horizontal clearance at any height.")]
    public float pillarClearanceHeight = 2.6f;
    [Tooltip("Legacy field kept for scene compatibility; pillar placement now checks horizontal clearance at any height.")]
    public float pillarClearancePadding = 0.2f;

    [Header("Treasure Chests")]
    [Range(0, 2)] public int fallbackCountingChestCount = 1;
    [Range(0, 2)] public int fallbackColorChestCount = 0;
    [Tooltip("Legacy field kept for scene compatibility. Teacher mission settings now drive chest counts.")]
    [Range(1, 2)] public int minRuntimeChests = 1;
    [Tooltip("Legacy field kept for scene compatibility. Teacher mission settings now drive chest counts.")]
    [Range(1, 2)] public int maxRuntimeChests = 2;
    public float minimumChestPlacementRadius = 12f;

    private readonly List<SpellPillarObjective> pillars = new List<SpellPillarObjective>();
    private readonly List<TreasureChestReward> treasureChests = new List<TreasureChestReward>();
    private ExitPortalObjective exitPortal;
    private bool stageActive;
    private bool exitOpen;

    public int PillarCount => pillars.Count;
    public int ActivatedPillarCount { get; private set; }
    public int RemainingPillarCount => Mathf.Max(0, PillarCount - ActivatedPillarCount);
    public int ExpectedWaves => Mathf.Max(1, PillarCount + 1);
    public bool ExitOpen => exitOpen;

    void Awake()
    {
        ResolveReferences();
    }

    public void BeginStage()
    {
        ResolveReferences();
        ClearStageObjects();
        stageActive = true;
        exitOpen = false;
        ActivatedPillarCount = 0;

        SpawnPillars();
        SpawnTreasureChests();
        CreateExitPortal();

        Debug.Log($"[LevelObjectiveDirector] Stage objective started with {PillarCount} pillar(s) and {treasureChests.Count} chest(s).");
    }

    public void ClearStageObjects()
    {
        foreach (SpellPillarObjective pillar in pillars)
        {
            if (pillar == null)
                continue;

            pillar.OnActivated -= HandlePillarActivated;
            Destroy(pillar.gameObject);
        }

        foreach (TreasureChestReward chest in treasureChests)
        {
            if (chest != null)
                Destroy(chest.gameObject);
        }

        if (exitPortal != null)
        {
            exitPortal.OnPlayerEscaped -= HandlePlayerEscaped;
            Destroy(exitPortal.gameObject);
        }

        pillars.Clear();
        treasureChests.Clear();
        exitPortal = null;
        stageActive = false;
        exitOpen = false;
        ActivatedPillarCount = 0;
    }

    public string BuildObjectiveStatus()
    {
        if (!stageActive)
            return "Objective waiting.";

        if (exitOpen)
        {
            if (waveDirector != null && waveDirector.EscapeEscalated)
                return "ESCAPE SURGE ESCALATED - RETURN TO CENTER";
            if (waveDirector != null)
                return $"EXIT OPEN - RETURN TO CENTER  {Mathf.CeilToInt(waveDirector.EscapeSecondsRemaining)}s";
            return "EXIT OPEN - RETURN TO CENTER";
        }

        var lines = new System.Text.StringBuilder();
        lines.Append("Pillars ").Append(ActivatedPillarCount).Append("/").Append(PillarCount);
        Vector3 playerPosition = playerController != null ? playerController.transform.position : Vector3.zero;
        foreach (SpellPillarObjective pillar in pillars)
        {
            if (pillar == null || pillar.IsActivated)
                continue;

            int distance = playerController != null
                ? Mathf.RoundToInt(Vector3.Distance(playerPosition, pillar.transform.position))
                : 0;
            string state = pillar.State == SpellPillarObjective.PillarState.Defending ? " DEFEND" : "";
            lines.AppendLine().Append(pillar.requiredSpellWord).Append(state).Append(" ").Append(distance).Append("m");
        }

        return lines.ToString();
    }

    void SpawnPillars()
    {
        int stage = RunProgressionManager.Instance != null ? RunProgressionManager.Instance.StageNumber : 1;
        int targetCount = Mathf.Clamp(minPillars + Mathf.Max(0, stage - 1) / 2, minPillars, maxPillars);
        List<string> words = BuildPillarWords(targetCount);
        List<Vector3> positions = FindObjectivePositions(
            words.Count,
            Mathf.Max(0f, minimumPillarPlacementRadius),
            true);

        int count = Mathf.Min(words.Count, positions.Count);
        if (count < words.Count)
            Debug.LogWarning($"[LevelObjectiveDirector] Only placed {count}/{words.Count} pillars; arena NavMesh may be small.");

        for (int i = 0; i < count; i++)
        {
            SpellPillarObjective pillar = CreatePillar(positions[i], words[i]);
            pillar.OnActivated += HandlePillarActivated;
            pillars.Add(pillar);
        }
    }

    void SpawnTreasureChests()
    {
        int countingCount = GetConfiguredChestCount(ChestMiniGameKind.Counting);
        int colorCount = GetConfiguredChestCount(ChestMiniGameKind.Color);
        int totalCount = countingCount + colorCount;
        List<Vector3> positions = FindObjectivePositions(totalCount, Mathf.Max(0f, minimumChestPlacementRadius), false);
        if (positions.Count < totalCount)
            Debug.LogWarning($"[LevelObjectiveDirector] Only placed {positions.Count}/{totalCount} treasure chests; arena NavMesh may be small.");

        int positionIndex = 0;
        for (int i = 0; i < countingCount && positionIndex < positions.Count; i++, positionIndex++)
            treasureChests.Add(CreateTreasureChest(positions[positionIndex], positionIndex, ChestMiniGameKind.Counting));
        for (int i = 0; i < colorCount && positionIndex < positions.Count; i++, positionIndex++)
            treasureChests.Add(CreateTreasureChest(positions[positionIndex], positionIndex, ChestMiniGameKind.Color));
    }

    int GetConfiguredChestCount(ChestMiniGameKind kind)
    {
        MissionAssignment mission = CurriculumSessionManager.Instance != null
            ? CurriculumSessionManager.Instance.CurrentMission
            : null;
        if (mission != null)
        {
            return kind == ChestMiniGameKind.Color
                ? Mathf.Clamp(mission.colorChestCount, 0, 2)
                : Mathf.Clamp(mission.countingChestCount, 0, 2);
        }

        return kind == ChestMiniGameKind.Color
            ? Mathf.Clamp(fallbackColorChestCount, 0, 2)
            : Mathf.Clamp(fallbackCountingChestCount, 0, 2);
    }

    void CreateExitPortal()
    {
        Vector3 position = playerController != null ? playerController.SpawnPosition : transform.position;
        if (NavMesh.SamplePosition(position, out NavMeshHit hit, 8f, NavMesh.AllAreas))
            position = hit.position;

        exitPortal = CreateExitPortalInstance(position);
        exitPortal.OnPlayerEscaped += HandlePlayerEscaped;
        exitPortal.SetOpen(false);
    }

    List<string> BuildPillarWords(int count)
    {
        var words = new List<string>();
        if (spellRegistry == null)
            return new List<string> { "CAT" };

        int stage = RunProgressionManager.Instance != null ? RunProgressionManager.Instance.StageNumber : 1;
        List<string> unlocked = spellRegistry.GetUnlockedWords(stage);
        if (unlocked.Count == 0)
            unlocked.Add(spellRegistry.ResolveLessonWord("CAT", stage));

        string recommended = waveDirector != null ? waveDirector.GetRecommendedSpellWord() : "";
        if (!string.IsNullOrEmpty(recommended) && unlocked.Contains(recommended))
            words.Add(recommended);

        while (words.Count < count)
            words.Add(unlocked[Random.Range(0, unlocked.Count)]);

        return words;
    }

    List<Vector3> FindObjectivePositions(int count, float minRadius, bool spreadOut)
    {
        var positions = new List<Vector3>();
        if (count <= 0)
            return positions;

        Vector3 center = arenaCenter != null
            ? arenaCenter.position
            : playerController != null
                ? playerController.transform.position
                : transform.position;

        float preferredSpacing = spreadOut ? idealPillarSpacing : Mathf.Max(4f, minimumPillarSpacing * 0.4f);
        float fallbackSpacing = spreadOut ? minimumPillarSpacing : 2f;
        float[] spacings = { preferredSpacing, preferredSpacing * 0.65f, fallbackSpacing };
        float maxPlacementRadius = Mathf.Max(1f, placementRadius);
        foreach (float spacing in spacings)
        {
            var combined = new List<Vector3>(positions);
            TryFillPositions(
                count,
                center,
                Mathf.Max(0f, spacing),
                Mathf.Max(0f, minRadius),
                maxPlacementRadius,
                combined);
            if (combined.Count >= count)
                return combined;

            positions = combined;
        }

        return new List<Vector3>(positions);
    }

    void TryFillPositions(int count, Vector3 center, float spacing, float minRadius, float maxPlacementRadius, List<Vector3> positions)
    {
        for (int attempt = 0; attempt < Mathf.Max(placementAttempts, count * 12) && positions.Count < count; attempt++)
        {
            Vector2 direction = Random.insideUnitCircle;
            if (direction.sqrMagnitude < 0.001f)
                direction = Vector2.right;
            direction.Normalize();

            float maxRadius = Mathf.Max(minRadius + 1f, maxPlacementRadius);
            float innerRadius = minRadius > 0f ? minRadius : Mathf.Min(6f, maxRadius * 0.25f);
            float radius = Random.Range(innerRadius, Mathf.Max(innerRadius + 1f, maxRadius));
            Vector3 candidate = center + new Vector3(direction.x, 0f, direction.y) * radius;
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 8f, NavMesh.AllAreas))
                continue;

            if (!HasEnoughSpacing(hit.position, positions, spacing))
                continue;

            if (!IsPillarPlacementClear(hit.position))
                continue;

            positions.Add(hit.position);
        }
    }

    bool HasEnoughSpacing(Vector3 candidate, List<Vector3> positions, float spacing)
    {
        foreach (Vector3 position in positions)
        {
            if (Vector3.Distance(candidate, position) < spacing)
                return false;
        }

        return true;
    }

    bool IsPillarPlacementClear(Vector3 candidate)
    {
        float clearance = Mathf.Max(5f, pillarClearanceRadius);
        Collider supportCollider = FindSupportCollider(candidate);
        Collider[] overlaps = Physics.OverlapBox(
            candidate,
            new Vector3(clearance, 512f, clearance),
            Quaternion.identity,
            ~0,
            QueryTriggerInteraction.Ignore);
        foreach (Collider overlap in overlaps)
        {
            if (overlap == null)
                continue;
            if (overlap == supportCollider)
                continue;

            Transform overlapTransform = overlap.transform;
            if (runtimeParent != null && overlapTransform.IsChildOf(runtimeParent))
                continue;

            if (playerController != null &&
                (overlapTransform == playerController.transform || overlapTransform.IsChildOf(playerController.transform)))
                continue;

            if (GetHorizontalDistance(candidate, overlap.bounds) < clearance)
                return false;
        }

        return true;
    }

    static Collider FindSupportCollider(Vector3 candidate)
    {
        if (Physics.Raycast(candidate + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 6f, ~0, QueryTriggerInteraction.Ignore))
            return hit.collider;

        return null;
    }

    static float GetHorizontalDistance(Vector3 point, Bounds bounds)
    {
        float dx = Mathf.Max(Mathf.Abs(point.x - bounds.center.x) - bounds.extents.x, 0f);
        float dz = Mathf.Max(Mathf.Abs(point.z - bounds.center.z) - bounds.extents.z, 0f);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    void HandlePillarActivated(SpellPillarObjective pillar)
    {
        ActivatedPillarCount = 0;
        foreach (SpellPillarObjective current in pillars)
        {
            if (current != null && current.IsActivated)
                ActivatedPillarCount++;
        }

        if (ActivatedPillarCount >= PillarCount)
            OpenExit();
    }

    void OpenExit()
    {
        if (exitOpen)
            return;

        exitOpen = true;
        exitPortal?.SetOpen(true);
        waveDirector?.BeginEscapeSurge();
        Debug.Log("[LevelObjectiveDirector] All pillars active. Exit opened.");
    }

    void HandlePlayerEscaped()
    {
        if (!exitOpen || waveDirector == null)
            return;

        stageActive = false;
        waveDirector.CompleteCurrentLevelFromObjective(ExpectedWaves);
    }

    void ResolveReferences()
    {
        playerController = playerController != null ? playerController : FindAnyObjectByType<PlayerController>();
        spellRegistry = spellRegistry != null ? spellRegistry : FindAnyObjectByType<SpellRegistry>();
        waveDirector = waveDirector != null ? waveDirector : FindAnyObjectByType<EnemyWaveDirector>();
        if (arenaCenter == null && waveDirector != null)
            arenaCenter = waveDirector.arenaCenter;
        if (arenaCenter == null && playerController != null)
            arenaCenter = playerController.transform;
        EnsureRuntimeParent();
    }

    void EnsureRuntimeParent()
    {
        bool parentWouldMoveWithPlayer =
            runtimeParent == transform ||
            playerController != null &&
            runtimeParent != null &&
            runtimeParent.IsChildOf(playerController.transform);

        if (runtimeParent != null && !parentWouldMoveWithPlayer)
            return;

        GameObject bucket = GameObject.Find("RuntimeStageObjects");
        if (bucket == null)
            bucket = new GameObject("RuntimeStageObjects");

        runtimeParent = bucket.transform;
    }

    SpellPillarObjective CreatePillar(Vector3 position, string spellWord)
    {
        if (pillarPrefab == null)
        {
            SpellPillarObjective pillar = SpellPillarObjective.Create(position, spellWord);
            pillar.waveDirector = waveDirector;
            if (runtimeParent != null)
                pillar.transform.SetParent(runtimeParent, true);
            return pillar;
        }

        SpellPillarObjective instance = Instantiate(pillarPrefab, position, pillarPrefab.transform.rotation, runtimeParent);
        GroundCustomPillar(instance, position.y);
        instance.name = $"{pillarPrefab.name}_{SpellRegistry.NormalizeWord(spellWord)}";
        instance.Initialize(spellWord, waveDirector);
        return instance;
    }

    TreasureChestReward CreateTreasureChest(Vector3 position, int index, ChestMiniGameKind kind)
    {
        GameObject chestObject;
        if (treasureChestPrefab != null)
        {
            chestObject = Instantiate(treasureChestPrefab, position, treasureChestPrefab.transform.rotation, runtimeParent);
            GroundCustomObject(chestObject.transform, position.y);
        }
        else
        {
            chestObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chestObject.name = "FallbackTreasureChest";
            chestObject.transform.SetParent(runtimeParent, true);
            chestObject.transform.position = position + Vector3.up * 0.42f;
            chestObject.transform.localScale = new Vector3(1.25f, 0.84f, 0.9f);
            Renderer renderer = chestObject.GetComponent<Renderer>();
            if (renderer != null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                renderer.material = new Material(shader) { color = new Color(0.52f, 0.32f, 0.12f, 1f) };
            }
        }

        chestObject.name = $"Runtime{kind}TreasureChest_{index + 1}";
        TreasureChestReward chest = chestObject.GetComponent<TreasureChestReward>();
        if (chest == null)
            chest = chestObject.AddComponent<TreasureChestReward>();
        chest.Initialize(
            kind,
            Random.Range(1, 11),
            $"Stage{kind}Chest_{index + 1}",
            coinPrefab,
            numberPronunciationClips);
        return chest;
    }

    static void GroundCustomPillar(SpellPillarObjective pillar, float groundY)
    {
        GroundCustomObject(pillar.transform, groundY, renderer => renderer.GetComponent<TMPro.TextMeshPro>() == null);
    }

    static void GroundCustomObject(Transform target, float groundY)
    {
        GroundCustomObject(target, groundY, _ => true);
    }

    static void GroundCustomObject(Transform target, float groundY, System.Predicate<Renderer> includeRenderer)
    {
        if (target == null)
            return;

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        bool foundVisual = false;
        Bounds visualBounds = default;

        foreach (Renderer renderer in renderers)
        {
            if (includeRenderer != null && !includeRenderer(renderer))
                continue;

            if (!foundVisual)
            {
                visualBounds = renderer.bounds;
                foundVisual = true;
            }
            else
            {
                visualBounds.Encapsulate(renderer.bounds);
            }
        }

        if (foundVisual)
            target.position += Vector3.up * (groundY - visualBounds.min.y);
    }

    ExitPortalObjective CreateExitPortalInstance(Vector3 position)
    {
        if (exitPortalPrefab == null)
        {
            ExitPortalObjective portal = ExitPortalObjective.Create(position);
            if (runtimeParent != null)
                portal.transform.SetParent(runtimeParent, true);
            return portal;
        }

        ExitPortalObjective instance = Instantiate(exitPortalPrefab, position, exitPortalPrefab.transform.rotation, runtimeParent);
        instance.Initialize();
        return instance;
    }
}
