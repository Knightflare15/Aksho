using System.Collections;
using System.Diagnostics;
using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("The Script/World Generation/World Generator")]
public sealed class WorldGenerator : MonoBehaviour
{
    const string RootName = "GeneratedWorldRoot";

    [Header("Generation")]
    [SerializeField] WorldGenerationProfile profile;
    [SerializeField] int seed = 12345;
    [SerializeField] bool generateOnStart;

    [SerializeField, HideInInspector] Transform generatedRoot;
    [SerializeField, HideInInspector] Transform terrainRoot;
    [SerializeField, HideInInspector] Transform waterRoot;
    [SerializeField, HideInInspector] Transform cliffsRoot;
    [SerializeField, HideInInspector] Transform propsRoot;
    [SerializeField, HideInInspector] Transform gameplayRoot;
    [SerializeField, HideInInspector] Transform debugRoot;

    Coroutine activeGeneration;

    public WorldGenerationProfile Profile => profile;
    public WorldData CurrentWorldData { get; private set; }
    public int Seed => seed;

    void Start()
    {
        if (generateOnStart)
            GenerateWorld(seed);
    }

    [ContextMenu("Generate")]
    public void GenerateFromInspector()
    {
        GenerateWorld(seed);
    }

    [ContextMenu("Regenerate")]
    public void Regenerate()
    {
        GenerateWorld(seed);
    }

    [ContextMenu("Clear World")]
    public void ClearWorld()
    {
        if (activeGeneration != null)
        {
            StopCoroutine(activeGeneration);
            activeGeneration = null;
        }

        Transform root = generatedRoot != null && generatedRoot != transform
            ? generatedRoot
            : transform.Find(RootName);
        if (root != null)
            DestroyGameObject(root.gameObject);

        generatedRoot = null;
        terrainRoot = null;
        waterRoot = null;
        cliffsRoot = null;
        propsRoot = null;
        gameplayRoot = null;
        debugRoot = null;
        CurrentWorldData = null;
    }

    public void GenerateWorld(int seed)
    {
        this.seed = seed;
        ClearWorld();
        EnsureProfile();
        EnsureRoots();
        CurrentWorldData = BuildWorldData(profile, seed);
        SpawnWorldObjects(CurrentWorldData);
    }

    public IEnumerator GenerateWorldCoroutine(int seed)
    {
        this.seed = seed;
        ClearWorld();
        EnsureProfile();
        EnsureRoots();

        var timer = Stopwatch.StartNew();
        int budget = Mathf.Max(1, profile.maxGenerationMillisecondsPerFrame);
        SeedManager seeds = new SeedManager(seed);
        WorldData data = new WorldData(seed, profile.worldSizeMeters, profile.terrainGridSpacing, profile.waterLevel);
        CurrentWorldData = data;

        IslandMaskGenerator.Generate(data, profile, seeds.terrainRng);
        yield return YieldIfOverBudget(timer, budget);
        HeightmapGenerator.Generate(data, profile, seeds.terrainRng);
        yield return YieldIfOverBudget(timer, budget);
        SlopeCalculator.Calculate(data);
        ZonePlacer.Place(data, profile, seeds.layoutRng);
        ClearingCarver.Carve(data, profile);
        yield return YieldIfOverBudget(timer, budget);
        SlopeCalculator.Calculate(data);
        PathGenerator.Generate(data, profile);
        ClearingCarver.FlattenPaths(data, profile);
        yield return YieldIfOverBudget(timer, budget);
        SlopeCalculator.Calculate(data);
        CliffDetector.Detect(data, profile);
        NormalBuilder.Build(data);
        TerrainPainter.Paint(data, profile);
        WorldValidator.BuildReachability(data);
        ObjectivePlacer.PlaceData(data);
        ChestPlacer.PlaceData(data, profile.GameplayProfile, seeds.gameplayRng);
        EnemyTriggerPlacer.PlaceData(data, profile.GameplayProfile, seeds.gameplayRng);
        SpawnPointPlacer.PlaceData(data, profile.GameplayProfile, seeds.gameplayRng);
        GameplayPlacementValidator.Validate(data);
        yield return YieldIfOverBudget(timer, budget);
        SpawnWorldObjects(data);
        activeGeneration = null;
    }

    public void SetProfile(WorldGenerationProfile profile)
    {
        this.profile = profile;
    }

    public static WorldData BuildWorldData(WorldGenerationProfile profile, int seed)
    {
        if (profile == null)
            profile = CreateRuntimeDefaultProfile();
        NormalizeProfileForArena(profile);

        SeedManager seeds = new SeedManager(seed);
        WorldData data = new WorldData(seed, profile.worldSizeMeters, profile.terrainGridSpacing, profile.waterLevel);
        IslandMaskGenerator.Generate(data, profile, seeds.terrainRng);
        HeightmapGenerator.Generate(data, profile, seeds.terrainRng);
        SlopeCalculator.Calculate(data);
        ZonePlacer.Place(data, profile, seeds.layoutRng);
        ClearingCarver.Carve(data, profile);
        SlopeCalculator.Calculate(data);
        PathGenerator.Generate(data, profile);
        ClearingCarver.FlattenPaths(data, profile);
        SlopeCalculator.Calculate(data);
        CliffDetector.Detect(data, profile);
        NormalBuilder.Build(data);
        TerrainPainter.Paint(data, profile);
        WorldValidator.BuildReachability(data);
        ObjectivePlacer.PlaceData(data);
        ChestPlacer.PlaceData(data, profile.GameplayProfile, seeds.gameplayRng);
        EnemyTriggerPlacer.PlaceData(data, profile.GameplayProfile, seeds.gameplayRng);
        SpawnPointPlacer.PlaceData(data, profile.GameplayProfile, seeds.gameplayRng);
        GameplayPlacementValidator.Validate(data);
        return data;
    }

    void SpawnWorldObjects(WorldData data)
    {
        Material terrainMaterial = profile.terrainMaterial != null ? profile.terrainMaterial : CreateFallbackTerrainMaterial();
        TerrainChunkBuilder.Build(data, profile, terrainRoot, terrainMaterial);
        WaterGenerator.Generate(data, profile, waterRoot);
        CliffMeshSpawner.Spawn(data, profile, cliffsRoot, new SeedManager(seed).propRng);
        ObjectivePlacer.Spawn(data, profile, gameplayRoot);
        ChestPlacer.Spawn(data, profile, gameplayRoot);
        EnemyTriggerPlacer.Spawn(data, profile, gameplayRoot);
        PropSpawner.Spawn(data, profile, propsRoot, new SeedManager(seed).propRng);
        GrassBladeSpawner.Spawn(data, profile, propsRoot, new SeedManager(seed).propRng);
        StampSpawner.Spawn(data, profile, propsRoot);

        GenerationGizmos gizmos = debugRoot.gameObject.GetComponent<GenerationGizmos>();
        if (gizmos == null)
            gizmos = debugRoot.gameObject.AddComponent<GenerationGizmos>();
        gizmos.generator = this;
        WorldDebugOverlay overlay = debugRoot.gameObject.GetComponent<WorldDebugOverlay>();
        if (overlay == null)
            overlay = debugRoot.gameObject.AddComponent<WorldDebugOverlay>();
        overlay.generator = this;
    }

    IEnumerator YieldIfOverBudget(Stopwatch timer, int budgetMs)
    {
        if (timer.ElapsedMilliseconds < budgetMs)
            yield break;
        yield return null;
        timer.Restart();
    }

    void EnsureProfile()
    {
        if (profile == null)
            profile = CreateRuntimeDefaultProfile();
        NormalizeProfileForArena(profile);
    }

    static WorldGenerationProfile CreateRuntimeDefaultProfile()
    {
        WorldGenerationProfile runtimeProfile = ScriptableObject.CreateInstance<WorldGenerationProfile>();
        runtimeProfile.name = "Runtime World Generation Defaults";
        return runtimeProfile;
    }

    static void NormalizeProfileForArena(WorldGenerationProfile profile)
    {
        if (profile == null || profile.island == null)
            return;

        profile.island.minimumPlayableRadius = Mathf.Max(profile.island.minimumPlayableRadius, 0.75f);
        profile.island.edgeWaterBias = Mathf.Max(profile.island.edgeWaterBias, 0.9f);
        profile.island.beachWidth = Mathf.Max(profile.island.beachWidth, 0.14f);
        profile.island.distortionStrength = Mathf.Min(profile.island.distortionStrength, 0.1f);
        profile.island.coastlineLobing = Mathf.Max(profile.island.coastlineLobing, 0.16f);
        profile.island.domainWarpStrength = Mathf.Max(profile.island.domainWarpStrength, 0.08f);
    }

    void EnsureRoots()
    {
        GameObject rootObject = new GameObject(RootName);
        rootObject.transform.SetParent(transform, false);
        generatedRoot = rootObject.transform;
        terrainRoot = CreateChildRoot(generatedRoot, "TerrainChunks");
        waterRoot = CreateChildRoot(generatedRoot, "Water");
        cliffsRoot = CreateChildRoot(generatedRoot, "Cliffs");
        propsRoot = CreateChildRoot(generatedRoot, "Props");
        gameplayRoot = CreateChildRoot(generatedRoot, "Gameplay");
        debugRoot = CreateChildRoot(generatedRoot, "Debug");
    }

    static Transform CreateChildRoot(Transform parent, string name)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(parent, false);
        return child.transform;
    }

    static Material CreateFallbackTerrainMaterial()
    {
        Shader shader = Shader.Find("The Script/WorldGeneration/Mobile Terrain Blend");
        if (shader == null)
            shader = Shader.Find("The Script/Level Grid Vertex Color");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Standard");
        return shader != null ? new Material(shader) { name = "Generated Mobile Terrain Blend" } : null;
    }

    static void DestroyGameObject(GameObject target)
    {
        if (target == null)
            return;
        if (Application.isPlaying)
            Destroy(target);
        else
            DestroyImmediate(target);
    }
}
