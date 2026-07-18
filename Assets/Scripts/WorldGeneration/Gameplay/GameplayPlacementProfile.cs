using UnityEngine;

[CreateAssetMenu(fileName = "GameplayPlacementProfile", menuName = "The Script/World Generation/Gameplay Placement Profile")]
public sealed class GameplayPlacementProfile : ScriptableObject
{
    public int objectivePillarCount = 3;
    public int minChests = 4;
    public int maxChests = 8;
    public int minEnemyTriggers = 8;
    public int maxEnemyTriggers = 14;
    public float minPillarDistanceFromStart = 35f;
    public float minPillarDistanceBetweenPillars = 30f;
    public float pillarClearingRadius = 12f;
    public float startClearingRadius = 10f;
    public float maxObjectiveSlope = 18f;
    public float maxChestSlope = 25f;
    public float maxSpawnSlope = 18f;
    public float minChestDistanceFromPath = 2f;
    public float maxChestDistanceFromPath = 25f;
    public float minTriggerDistanceFromStart = 25f;
    public float spawnPointRadiusMin = 8f;
    public float spawnPointRadiusMax = 18f;

    static GameplayPlacementProfile defaultRuntime;
    public static GameplayPlacementProfile DefaultRuntime
    {
        get
        {
            if (defaultRuntime == null)
            {
                defaultRuntime = CreateInstance<GameplayPlacementProfile>();
                defaultRuntime.name = "Runtime Gameplay Placement Defaults";
            }
            return defaultRuntime;
        }
    }
}
