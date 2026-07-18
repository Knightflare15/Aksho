using System.Collections.Generic;
using UnityEngine;

public static class PathGenerator
{
    public static void Generate(WorldData data, WorldGenerationProfile profile)
    {
        data.paths.Clear();
        ZoneData start = null;
        var objectives = new List<ZoneData>();
        foreach (ZoneData zone in data.zones)
        {
            if (zone.kind == ZoneKind.Start)
                start = zone;
            else if (zone.kind == ZoneKind.Objective)
                objectives.Add(zone);
        }

        if (start == null || objectives.Count == 0)
            return;

        ZoneData current = start;
        for (int i = 0; i < objectives.Count; i++)
        {
            List<Vector2Int> points = CoarseGridPathfinder.FindPath(data, profile, current.gridPosition, objectives[i].gridPosition);
            PathData path = new PathData();
            Densify(points, path.points);
            data.paths.Add(path);
            current = objectives[i];
        }
    }

    static void Densify(List<Vector2Int> coarse, List<Vector2Int> result)
    {
        result.Clear();
        if (coarse == null || coarse.Count == 0)
            return;

        result.Add(coarse[0]);
        for (int i = 1; i < coarse.Count; i++)
        {
            Vector2Int a = coarse[i - 1];
            Vector2Int b = coarse[i];
            int steps = Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
            for (int s = 1; s <= steps; s++)
            {
                float t = s / (float)steps;
                result.Add(new Vector2Int(
                    Mathf.RoundToInt(Mathf.Lerp(a.x, b.x, t)),
                    Mathf.RoundToInt(Mathf.Lerp(a.y, b.y, t))));
            }
        }
    }
}
