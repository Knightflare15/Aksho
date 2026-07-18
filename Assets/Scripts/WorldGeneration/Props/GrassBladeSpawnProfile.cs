using UnityEngine;

[CreateAssetMenu(fileName = "GrassBladeSpawnProfile", menuName = "The Script/World Generation/Grass Blade Spawn Profile")]
public sealed class GrassBladeSpawnProfile : ScriptableObject
{
    [Header("Prefab")]
    public GameObject grassBladePrefab;
    public Material bladeMaterial;

    [Header("Placement")]
    public int maxInstances = 1200;
    public float densityPerSquareMeter = 0.35f;
    [Range(0f, 1f)] public float minGrassWeight = 0.55f;
    public float maxSlope = 32f;
    public float minDistanceFromPaths = 1.2f;
    public float minDistanceFromObjectives = 3f;
    public int seed = 2718;

    [Header("Transform")]
    public Vector2 scaleRange = new Vector2(0.28f, 0.55f);
    public float yOffset = 0.01f;
    [Range(0f, 1f)] public float alignToNormal = 0.45f;
    public float randomTiltDegrees = 6f;
}
