using System;
using UnityEngine;

public static class ZonePlacer
{
    public static void Place(WorldData data, WorldGenerationProfile profile, System.Random rng)
    {
        GameplayPlacementProfile gameplay = profile.GameplayProfile;
        data.zones.Clear();

        Vector2Int start = FindStart(data, gameplay);
        data.zones.Add(CreateZone(data, ZoneKind.Start, start, gameplay.startClearingRadius));

        int objectiveCount = Mathf.Max(1, gameplay.objectivePillarCount);
        for (int i = 0; i < objectiveCount; i++)
        {
            Vector2Int point = FindObjective(data, rng, start, gameplay, i, objectiveCount);
            data.zones.Add(CreateZone(data, ZoneKind.Objective, point, gameplay.pillarClearingRadius));
        }
    }

    static Vector2Int FindStart(WorldData data, GameplayPlacementProfile gameplay)
    {
        Vector2Int center = new Vector2Int(data.resolution / 2, data.resolution / 2);
        Vector2Int best = center;
        float bestScore = float.MaxValue;
        for (int z = 8; z < data.resolution - 8; z++)
        {
            for (int x = 8; x < data.resolution - 8; x++)
            {
                int index = data.Index(x, z);
                if (data.landMask[index] == 0 || data.waterMask[index] > 0)
                    continue;
                float slopePenalty = data.slope[index] * 2f;
                float score = Vector2Int.Distance(center, new Vector2Int(x, z)) + slopePenalty;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = new Vector2Int(x, z);
                }
            }
        }
        return best;
    }

    static Vector2Int FindObjective(
        WorldData data,
        System.Random rng,
        Vector2Int start,
        GameplayPlacementProfile gameplay,
        int objectiveIndex,
        int objectiveCount)
    {
        Vector2Int best = start;
        float bestScore = float.MinValue;
        float baseAngle = (objectiveIndex / Mathf.Max(1f, objectiveCount)) * Mathf.PI * 2f + Mathf.PI * 0.5f;
        float sectorAngle = baseAngle + ((float)rng.NextDouble() - 0.5f) * 0.35f;
        Vector2 sectorDirection = new Vector2(Mathf.Cos(sectorAngle), Mathf.Sin(sectorAngle));
        Vector2 startPoint = new Vector2(start.x, start.y);
        float idealDistance = Mathf.Clamp(data.worldSizeMeters * 0.31f, gameplay.minPillarDistanceFromStart, data.worldSizeMeters * 0.42f);

        for (int z = 10; z < data.resolution - 10; z++)
        {
            for (int x = 10; x < data.resolution - 10; x++)
            {
                if (!IsValidObjectiveCell(data, x, z, gameplay))
                    continue;

                Vector2Int candidate = new Vector2Int(x, z);
                float startDistance = Vector2Int.Distance(start, candidate) * data.gridSpacing;
                if (startDistance < gameplay.minPillarDistanceFromStart)
                    continue;

                bool tooClose = false;
                foreach (ZoneData zone in data.zones)
                {
                    if (zone.kind == ZoneKind.Objective &&
                        Vector2Int.Distance(zone.gridPosition, candidate) * data.gridSpacing < gameplay.minPillarDistanceBetweenPillars)
                    {
                        tooClose = true;
                        break;
                    }
                }
                if (tooClose)
                    continue;

                Vector2 direction = new Vector2(x, z) - startPoint;
                if (direction.sqrMagnitude < 0.001f)
                    continue;
                direction.Normalize();

                int index = data.Index(x, z);
                float sectorScore = Mathf.Clamp01((Vector2.Dot(direction, sectorDirection) + 1f) * 0.5f);
                float distanceScore = 1f - Mathf.Clamp01(Mathf.Abs(startDistance - idealDistance) / Mathf.Max(1f, idealDistance));
                float interiorScore = Mathf.Clamp01((data.landBlend[index] - 0.68f) / 0.32f);
                float flatScore = 1f - Mathf.Clamp01(data.slope[index] / Mathf.Max(1f, gameplay.maxObjectiveSlope));
                float patchScore = HasFlatPatch(data, x, z, gameplay.pillarClearingRadius * 0.55f, gameplay.maxObjectiveSlope + 4f) ? 1f : 0f;
                float score = sectorScore * 45f + distanceScore * 28f + interiorScore * 18f + flatScore * 12f + patchScore * 20f;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }
        }

        if (best == start)
            best = FindFallbackLand(data, start);

        return best;
    }

    static bool IsValidObjectiveCell(WorldData data, int x, int z, GameplayPlacementProfile gameplay)
    {
        if (!data.InBounds(x, z))
            return false;
        if (x < 10 || z < 10 || x > data.resolution - 11 || z > data.resolution - 11)
            return false;
        int index = data.Index(x, z);
        return data.landMask[index] > 0 &&
               data.waterMask[index] == 0 &&
               data.height[index] >= data.waterLevel + 1.2f &&
               data.landBlend[index] >= 0.72f &&
               data.slope[index] <= gameplay.maxObjectiveSlope;
    }

    static bool HasFlatPatch(WorldData data, int centerX, int centerZ, float radiusMeters, float maxSlope)
    {
        int radius = Mathf.Max(2, Mathf.RoundToInt(radiusMeters / Mathf.Max(0.001f, data.gridSpacing)));
        int valid = 0;
        int total = 0;
        for (int z = centerZ - radius; z <= centerZ + radius; z++)
        {
            for (int x = centerX - radius; x <= centerX + radius; x++)
            {
                if (!data.InBounds(x, z))
                    return false;
                if (Vector2.Distance(new Vector2(centerX, centerZ), new Vector2(x, z)) > radius)
                    continue;
                total++;
                int index = data.Index(x, z);
                if (data.landMask[index] > 0 &&
                    data.waterMask[index] == 0 &&
                    data.height[index] >= data.waterLevel + 1.1f &&
                    data.landBlend[index] >= 0.68f &&
                    data.slope[index] <= maxSlope)
                {
                    valid++;
                }
            }
        }

        return total > 0 && valid / (float)total >= 0.82f;
    }

    static Vector2Int FindFallbackLand(WorldData data, Vector2Int start)
    {
        Vector2Int best = start;
        float bestDistance = 0f;
        for (int z = 8; z < data.resolution - 8; z++)
        {
            for (int x = 8; x < data.resolution - 8; x++)
            {
                int index = data.Index(x, z);
                if (data.landMask[index] == 0 || data.waterMask[index] > 0 || data.slope[index] > 28f)
                    continue;
                float distance = Vector2Int.Distance(start, new Vector2Int(x, z));
                if (distance > bestDistance)
                {
                    bestDistance = distance;
                    best = new Vector2Int(x, z);
                }
            }
        }
        return best;
    }

    static ZoneData CreateZone(WorldData data, ZoneKind kind, Vector2Int grid, float radius)
    {
        Vector3 world = data.GridToWorld(grid.x, grid.y);
        SemanticZoneKind semanticKind = kind == ZoneKind.Start ? SemanticZoneKind.Town : SemanticZoneKind.Gym;
        return new ZoneData
        {
            kind = kind,
            semanticKind = semanticKind,
            gridPosition = grid,
            worldPosition = world,
            radius = radius,
            desiredHeight = Mathf.Max(data.waterLevel + 1.2f, world.y),
            grammarTopic = kind == ZoneKind.Start ? "Grammar Town" : "Grammar Gym",
            translatorAssist = semanticKind == SemanticZoneKind.Town
                ? TranslatorAssistMode.Full
                : TranslatorAssistMode.Off,
            encounterNounFamilies = new System.Collections.Generic.List<string> { "RAT", "CAT", "DOG", "BUG" },
        };
    }
}
