using System;
using System.Collections.Generic;
using UnityEngine;

public static class BlueNoiseSampler
{
    public static List<Vector2Int> BuildCandidates(WorldData data, int step, System.Random rng)
    {
        step = Mathf.Max(1, step);
        var candidates = new List<Vector2Int>();
        for (int z = 2; z < data.resolution - 2; z += step)
        {
            for (int x = 2; x < data.resolution - 2; x += step)
            {
                int jitterX = rng.Next(-step / 2, step / 2 + 1);
                int jitterZ = rng.Next(-step / 2, step / 2 + 1);
                int px = Mathf.Clamp(x + jitterX, 1, data.resolution - 2);
                int pz = Mathf.Clamp(z + jitterZ, 1, data.resolution - 2);
                candidates.Add(new Vector2Int(px, pz));
            }
        }
        return candidates;
    }
}
