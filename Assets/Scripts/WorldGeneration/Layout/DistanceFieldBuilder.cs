using UnityEngine;

public static class DistanceFieldBuilder
{
    public static float DistanceToPath(WorldData data, int x, int z, float maxDistance)
    {
        float best = maxDistance;
        foreach (PathData path in data.paths)
        {
            foreach (Vector2Int point in path.points)
            {
                float distance = Vector2.Distance(new Vector2(x, z), new Vector2(point.x, point.y)) * data.gridSpacing;
                if (distance < best)
                    best = distance;
            }
        }
        return best;
    }
}
