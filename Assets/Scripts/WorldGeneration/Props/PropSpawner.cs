using System;
using System.Collections.Generic;
using UnityEngine;

public static class PropSpawner
{
    public static void Spawn(WorldData data, WorldGenerationProfile profile, Transform parent, System.Random rng)
    {
        PropSpawnProfile propProfile = profile.propProfile;
        if (propProfile == null || propProfile.rules == null || propProfile.rules.Length == 0)
            return;

        List<Vector2Int> candidates = BlueNoiseSampler.BuildCandidates(data, propProfile.candidateGridStep, rng);
        Shuffle(candidates, rng);

        foreach (PropRule rule in propProfile.rules)
        {
            if (rule == null || rule.prefabs == null || rule.prefabs.Length == 0)
                continue;
            int target = rng.Next(Mathf.Min(rule.minCount, rule.maxCount), Mathf.Max(rule.minCount, rule.maxCount) + 1);
            int spawned = 0;
            foreach (Vector2Int candidate in candidates)
            {
                if (spawned >= target)
                    break;
                if (!CanPlace(data, rule, candidate))
                    continue;
                GameObject prefab = rule.prefabs[rng.Next(0, rule.prefabs.Length)];
                if (prefab == null)
                    continue;
                Vector3 position = data.GridToWorld(candidate.x, candidate.y);
                GameObject instance = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity, parent);
                instance.name = $"GeneratedProp_{rule.category}_{candidate.x}_{candidate.y}";
                instance.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                float scale = Mathf.Lerp(rule.scaleRange.x, rule.scaleRange.y, (float)rng.NextDouble());
                instance.transform.localScale = Vector3.one * scale;
                WorldPlacementUtility.GroundVisibleBounds(instance.transform, data.height[data.Index(candidate.x, candidate.y)]);
                spawned++;
            }
        }
    }

    static bool CanPlace(WorldData data, PropRule rule, Vector2Int point)
    {
        int index = data.Index(point.x, point.y);
        if (!WorldPlacementUtility.IsDryPlayableCell(data, point.x, point.y, rule.maxSlope, 0.65f))
            return false;
        if (data.slope[index] < rule.minSlope || data.slope[index] > rule.maxSlope)
            return false;
        if (DistanceFieldBuilder.DistanceToPath(data, point.x, point.y, rule.minDistanceFromPath + 1f) < rule.minDistanceFromPath)
            return false;
        foreach (ObjectivePoint objective in data.objectives)
        {
            if (Vector2Int.Distance(objective.gridPosition, point) * data.gridSpacing < rule.minDistanceFromObjectives)
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
