using System.Collections.Generic;
using UnityEngine;

public static class CoarseGridPathfinder
{
    struct Node
    {
        public Vector2Int position;
        public float g;
        public float f;
    }

    public static List<Vector2Int> FindPath(WorldData data, WorldGenerationProfile profile, Vector2Int start, Vector2Int goal)
    {
        int step = Mathf.Max(1, profile.coarseGridSpacing);
        Vector2Int coarseStart = new Vector2Int(start.x / step, start.y / step);
        Vector2Int coarseGoal = new Vector2Int(goal.x / step, goal.y / step);
        int coarseResolution = Mathf.CeilToInt(data.resolution / (float)step);

        var open = new List<Node>();
        var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        var cost = new Dictionary<Vector2Int, float>();
        open.Add(new Node { position = coarseStart, g = 0f, f = Heuristic(coarseStart, coarseGoal) });
        cost[coarseStart] = 0f;

        while (open.Count > 0)
        {
            int bestIndex = 0;
            float bestF = open[0].f;
            for (int i = 1; i < open.Count; i++)
            {
                if (open[i].f < bestF)
                {
                    bestF = open[i].f;
                    bestIndex = i;
                }
            }

            Node current = open[bestIndex];
            open.RemoveAt(bestIndex);
            if (current.position == coarseGoal)
                return Reconstruct(cameFrom, current.position, step);

            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0)
                        continue;
                    Vector2Int next = current.position + new Vector2Int(dx, dz);
                    if (next.x < 0 || next.y < 0 || next.x >= coarseResolution || next.y >= coarseResolution)
                        continue;

                    int fullX = Mathf.Clamp(next.x * step, 0, data.resolution - 1);
                    int fullZ = Mathf.Clamp(next.y * step, 0, data.resolution - 1);
                    float moveCost = Cost(data, profile, fullX, fullZ) * (dx != 0 && dz != 0 ? 1.4f : 1f);
                    if (moveCost >= 9999f)
                        continue;

                    float newCost = cost[current.position] + moveCost;
                    if (cost.TryGetValue(next, out float oldCost) && newCost >= oldCost)
                        continue;

                    cost[next] = newCost;
                    cameFrom[next] = current.position;
                    open.Add(new Node { position = next, g = newCost, f = newCost + Heuristic(next, coarseGoal) });
                }
            }
        }

        return new List<Vector2Int> { start, goal };
    }

    static float Cost(WorldData data, WorldGenerationProfile profile, int x, int z)
    {
        if (!data.InBounds(x, z))
            return 9999f;

        int index = data.Index(x, z);
        if (data.waterMask[index] > 0)
            return 60f;
        if (data.landMask[index] == 0)
            return 9999f;

        float edge = Mathf.Min(Mathf.Min(x, z), Mathf.Min(data.resolution - 1 - x, data.resolution - 1 - z));
        float edgePenalty = Mathf.InverseLerp(14f, 0f, edge) * 18f;
        float slopePenalty = Mathf.Max(0f, data.slope[index] - 14f) * 0.9f;
        float highPenalty = Mathf.InverseLerp(14f, 24f, data.height[index]) * 10f;
        return 1f + edgePenalty + slopePenalty + highPenalty;
    }

    static float Heuristic(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    static List<Vector2Int> Reconstruct(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int current, int step)
    {
        var reversed = new List<Vector2Int>();
        reversed.Add(current * step);
        while (cameFrom.TryGetValue(current, out Vector2Int previous))
        {
            current = previous;
            reversed.Add(current * step);
        }
        reversed.Reverse();
        return reversed;
    }
}
