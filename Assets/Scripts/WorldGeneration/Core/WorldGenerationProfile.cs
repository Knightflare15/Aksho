using UnityEngine;

[CreateAssetMenu(fileName = "WorldGenerationProfile", menuName = "The Script/World Generation/Profile")]
public sealed class WorldGenerationProfile : ScriptableObject
{
    [Header("World")]
    public int worldSizeMeters = 150;
    public float terrainGridSpacing = 1f;
    [Range(1, 4)] public int terrainVisualSubdivisions = 2;
    public int chunkSizeMeters = 30;
    public int coarseGridSpacing = 2;
    public float waterLevel = 0f;
    public int maxGenerationMillisecondsPerFrame = 5;

    [Header("Noise")]
    public WorldNoiseSettings macroTerrain = new WorldNoiseSettings { scale = 0.018f, amplitude = 7f, octaves = 2 };
    public WorldNoiseSettings rollingHills = new WorldNoiseSettings { scale = 0.055f, amplitude = 3.5f, octaves = 2 };
    public WorldNoiseSettings detailVariation = new WorldNoiseSettings { scale = 0.18f, amplitude = 0.75f, octaves = 1 };

    [Header("Island")]
    public IslandSettings island = new IslandSettings();

    [Header("Paths")]
    public PathSettings paths = new PathSettings();

    [Header("Cliffs")]
    public CliffSettings cliffs = new CliffSettings();

    [Header("Placement")]
    public PropSpawnProfile propProfile;
    public GrassBladeSpawnProfile grassBladeProfile;
    public GameplayPlacementProfile gameplayPlacementProfile;

    [Header("Materials")]
    public Material terrainMaterial;
    public Material waterMaterial;
    public Material cliffMaterial;

    [Header("Prefabs")]
    public GameObject[] cliffPrefabs;
    public SpellPillarObjective objectivePillarPrefab;
    public GameObject treasureChestPrefab;
    public GameObject enemyTriggerPrefab;
    public GameObject waterPrefab;

    public GameplayPlacementProfile GameplayProfile => gameplayPlacementProfile != null
        ? gameplayPlacementProfile
        : GameplayPlacementProfile.DefaultRuntime;
}

[System.Serializable]
public class WorldNoiseSettings
{
    public float scale = 0.05f;
    public float amplitude = 1f;
    [Range(1, 6)] public int octaves = 2;
    [Range(0.1f, 0.9f)] public float persistence = 0.5f;
    [Range(1f, 4f)] public float lacunarity = 2f;
}

[System.Serializable]
public class IslandSettings
{
    [Range(0f, 1f)] public float islandStrength = 1f;
    [Range(0f, 1f)] public float edgeWaterBias = 0.95f;
    public float distortionScale = 0.035f;
    public float distortionStrength = 0.08f;
    public float beachWidth = 0.16f;
    [Range(0.45f, 0.9f)] public float minimumPlayableRadius = 0.72f;
    [Range(0f, 0.35f)] public float coastlineLobing = 0.18f;
    [Range(0f, 0.25f)] public float domainWarpStrength = 0.1f;
}

[System.Serializable]
public class PathSettings
{
    public float innerDirtWidth = 2.5f;
    public float outerBlendWidth = 4.5f;
    public float flattenStrength = 0.56f;
    public float maxPathSlope = 32f;
}

[System.Serializable]
public class CliffSettings
{
    public float slopeThreshold = 42f;
    public int maxCliffPieces = 55;
    public float minPieceSpacing = 5f;
    public Vector2 randomScaleRange = new Vector2(0.85f, 1.35f);
}
