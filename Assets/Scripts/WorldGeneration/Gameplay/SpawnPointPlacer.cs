using System;
using UnityEngine;

public static class SpawnPointPlacer
{
    public static void PlaceData(WorldData data, GameplayPlacementProfile profile, System.Random rng)
    {
        data.spawnPoints.Clear();
        foreach (EnemyTriggerData trigger in data.enemyTriggers)
        {
            trigger.spawnPoints.Clear();
            int count = rng.Next(4, 9);
            for (int i = 0; i < count; i++)
            {
                if (!TryFindSpawnPoint(data, profile, rng, trigger, out Vector2Int grid))
                    continue;
                var spawn = new SpawnPointData
                {
                    gridPosition = grid,
                    worldPosition = WorldPlacementUtility.GroundedPosition(data, grid.x, grid.y),
                    trigger = trigger,
                };
                trigger.spawnPoints.Add(spawn);
                data.spawnPoints.Add(spawn);
            }
        }
    }

    static bool TryFindSpawnPoint(WorldData data, GameplayPlacementProfile profile, System.Random rng, EnemyTriggerData trigger, out Vector2Int grid)
    {
        for (int attempt = 0; attempt < 120; attempt++)
        {
            float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
            float radius = Mathf.Lerp(profile.spawnPointRadiusMin, profile.spawnPointRadiusMax, (float)rng.NextDouble());
            int x = trigger.gridPosition.x + Mathf.RoundToInt(Mathf.Cos(angle) * radius / data.gridSpacing);
            int z = trigger.gridPosition.y + Mathf.RoundToInt(Mathf.Sin(angle) * radius / data.gridSpacing);
            if (!data.InBounds(x, z))
                continue;
            if (!WorldPlacementUtility.IsDryPlayableCell(data, x, z, profile.maxSpawnSlope, 0.65f))
                continue;
            grid = new Vector2Int(x, z);
            return true;
        }

        grid = default;
        return false;
    }
}
