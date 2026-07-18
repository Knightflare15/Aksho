using System.Collections.Generic;
using UnityEngine;

public static class GrassBladeSpawner
{
    public static void Spawn(WorldData data, WorldGenerationProfile profile, Transform parent, System.Random rng)
    {
        GrassBladeSpawnProfile grassProfile = profile != null ? profile.grassBladeProfile : null;
        if (data == null || grassProfile == null || grassProfile.grassBladePrefab == null || parent == null)
            return;

        int step = Mathf.Max(1, Mathf.RoundToInt(1f / Mathf.Max(0.01f, grassProfile.densityPerSquareMeter)));
        List<Vector2Int> candidates = BlueNoiseSampler.BuildCandidates(data, step, rng);
        Shuffle(candidates, rng);

        int spawned = 0;
        foreach (Vector2Int candidate in candidates)
        {
            if (spawned >= grassProfile.maxInstances)
                break;
            if (!CanPlace(data, grassProfile, candidate))
                continue;

            Vector3 position = data.GridToWorld(candidate.x, candidate.y);
            Vector3 normal = data.SampleNormalWorld(position);
            SpawnInstance(grassProfile, parent, position, normal, rng, spawned);
            spawned++;
        }
    }

    public static GameObject SpawnInstance(
        GrassBladeSpawnProfile profile,
        Transform parent,
        Vector3 position,
        Vector3 normal,
        System.Random rng,
        int index)
    {
        if (profile == null || profile.grassBladePrefab == null)
            return null;

        position += Vector3.up * profile.yOffset;
        normal = normal.sqrMagnitude > 0.001f ? normal.normalized : Vector3.up;

        Quaternion surfaceRotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(Vector3.up, normal), profile.alignToNormal);
        Quaternion yaw = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
        float tiltX = Mathf.Lerp(-profile.randomTiltDegrees, profile.randomTiltDegrees, (float)rng.NextDouble());
        float tiltZ = Mathf.Lerp(-profile.randomTiltDegrees, profile.randomTiltDegrees, (float)rng.NextDouble());
        Quaternion tilt = Quaternion.Euler(tiltX, 0f, tiltZ);

        GameObject instance = Object.Instantiate(profile.grassBladePrefab, position, surfaceRotation * yaw * tilt, parent);
        instance.name = $"GeneratedGrassBlade_{index:0000}";
        float scale = Mathf.Lerp(profile.scaleRange.x, profile.scaleRange.y, (float)rng.NextDouble());
        instance.transform.localScale = Vector3.one * scale;
        ApplyMaterial(instance, profile.bladeMaterial);
        return instance;
    }

    static void ApplyMaterial(GameObject instance, Material material)
    {
        if (instance == null || material == null)
            return;

        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
            renderer.sharedMaterial = material;
    }

    static bool CanPlace(WorldData data, GrassBladeSpawnProfile profile, Vector2Int point)
    {
        if (!data.InBounds(point.x, point.y))
            return false;

        int index = data.Index(point.x, point.y);
        if (data.waterMask[index] > 0 || data.cliffMask[index] > 0)
            return false;
        if (data.slope[index] > profile.maxSlope)
            return false;
        if (data.grassMask[index] / 255f < profile.minGrassWeight)
            return false;
        if (profile.minDistanceFromPaths > 0f &&
            DistanceFieldBuilder.DistanceToPath(data, point.x, point.y, profile.minDistanceFromPaths + 1f) < profile.minDistanceFromPaths)
        {
            return false;
        }
        foreach (ObjectivePoint objective in data.objectives)
        {
            if (Vector2Int.Distance(objective.gridPosition, point) * data.gridSpacing < profile.minDistanceFromObjectives)
                return false;
        }

        return true;
    }

    static void Shuffle<T>(List<T> values, System.Random rng)
    {
        for (int i = values.Count - 1; i > 0; i--)
        {
            int swap = rng.Next(0, i + 1);
            T value = values[i];
            values[i] = values[swap];
            values[swap] = value;
        }
    }
}
