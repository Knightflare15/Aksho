using UnityEngine;

public static class ClearingCarver
{
    public static void Carve(WorldData data, WorldGenerationProfile profile)
    {
        foreach (ZoneData zone in data.zones)
            CarveZone(data, zone);
    }

    public static void FlattenPaths(WorldData data, WorldGenerationProfile profile)
    {
        foreach (PathData path in data.paths)
        {
            foreach (Vector2Int point in path.points)
                FlattenPathPoint(data, profile, point);
        }
    }

    static void CarveZone(WorldData data, ZoneData zone)
    {
        int radiusCells = Mathf.CeilToInt(zone.radius / data.gridSpacing) + 2;
        for (int z = zone.gridPosition.y - radiusCells; z <= zone.gridPosition.y + radiusCells; z++)
        {
            for (int x = zone.gridPosition.x - radiusCells; x <= zone.gridPosition.x + radiusCells; x++)
            {
                if (!data.InBounds(x, z))
                    continue;
                float distance = Vector2.Distance(new Vector2(x, z), new Vector2(zone.gridPosition.x, zone.gridPosition.y)) * data.gridSpacing;
                float edgeNoise = Mathf.PerlinNoise(x * 0.23f + data.seed * 0.01f, z * 0.23f - data.seed * 0.01f) * 1.8f - 0.9f;
                float radius = zone.radius + edgeNoise;
                float falloff = 1f - Mathf.SmoothStep(radius * 0.35f, radius, distance);
                if (falloff <= 0f)
                    continue;

                int index = data.Index(x, z);
                data.height[index] = Mathf.Lerp(data.height[index], zone.desiredHeight, falloff * 0.86f);
                data.clearingMask[index] = (byte)Mathf.Max(data.clearingMask[index], Mathf.RoundToInt(falloff * 255f));
                if (data.height[index] > data.waterLevel + 0.2f)
                {
                    data.landMask[index] = 255;
                    data.waterMask[index] = 0;
                }
            }
        }

        zone.worldPosition = data.GridToWorld(zone.gridPosition.x, zone.gridPosition.y);
    }

    static void FlattenPathPoint(WorldData data, WorldGenerationProfile profile, Vector2Int point)
    {
        int radiusCells = Mathf.CeilToInt(profile.paths.outerBlendWidth / data.gridSpacing);
        float target = AverageHeight(data, point, Mathf.Max(1, radiusCells / 2));
        for (int z = point.y - radiusCells; z <= point.y + radiusCells; z++)
        {
            for (int x = point.x - radiusCells; x <= point.x + radiusCells; x++)
            {
                if (!data.InBounds(x, z))
                    continue;

                float distance = Vector2.Distance(new Vector2(x, z), new Vector2(point.x, point.y)) * data.gridSpacing;
                if (distance > profile.paths.outerBlendWidth)
                    continue;

                float falloff = 1f - Mathf.SmoothStep(profile.paths.innerDirtWidth, profile.paths.outerBlendWidth, distance);
                int index = data.Index(x, z);
                float variation = (Mathf.PerlinNoise(x * 0.4f + data.seed, z * 0.4f - data.seed) - 0.5f) * 0.16f;
                data.height[index] = Mathf.Lerp(data.height[index], target + variation, falloff * profile.paths.flattenStrength);
                data.pathMask[index] = (byte)Mathf.Max(data.pathMask[index], Mathf.RoundToInt(falloff * 255f));
                if (distance <= profile.paths.innerDirtWidth)
                    data.dirtMask[index] = 255;
            }
        }
    }

    static float AverageHeight(WorldData data, Vector2Int point, int radius)
    {
        float total = 0f;
        int count = 0;
        for (int z = point.y - radius; z <= point.y + radius; z++)
        {
            for (int x = point.x - radius; x <= point.x + radius; x++)
            {
                if (!data.InBounds(x, z))
                    continue;
                total += data.height[data.Index(x, z)];
                count++;
            }
        }
        return count == 0 ? data.height[data.Index(point.x, point.y)] : total / count;
    }
}
