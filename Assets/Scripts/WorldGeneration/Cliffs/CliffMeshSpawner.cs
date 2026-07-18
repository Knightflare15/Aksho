using System;
using UnityEngine;

public static class CliffMeshSpawner
{
    public static void Spawn(WorldData data, WorldGenerationProfile profile, Transform parent, System.Random rng)
    {
        if (profile.cliffPrefabs == null || profile.cliffPrefabs.Length == 0 || profile.cliffs.maxCliffPieces <= 0)
            return;

        int spawned = 0;
        float minSpacingSqr = profile.cliffs.minPieceSpacing * profile.cliffs.minPieceSpacing;
        var positions = new System.Collections.Generic.List<Vector3>();
        for (int z = 2; z < data.resolution - 2 && spawned < profile.cliffs.maxCliffPieces; z += 2)
        {
            for (int x = 2; x < data.resolution - 2 && spawned < profile.cliffs.maxCliffPieces; x += 2)
            {
                int index = data.Index(x, z);
                if (data.cliffMask[index] == 0 || data.pathMask[index] > 80 || data.clearingMask[index] > 120)
                    continue;
                if (rng.NextDouble() > 0.18)
                    continue;

                Vector3 position = data.GridToWorld(x, z);
                bool tooClose = false;
                foreach (Vector3 placed in positions)
                {
                    if ((placed - position).sqrMagnitude < minSpacingSqr)
                    {
                        tooClose = true;
                        break;
                    }
                }
                if (tooClose)
                    continue;

                GameObject prefab = profile.cliffPrefabs[rng.Next(0, profile.cliffPrefabs.Length)];
                if (prefab == null)
                    continue;
                GameObject instance = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity, parent);
                instance.name = $"GeneratedCliff_{x}_{z}";
                Vector3 normal = data.normals[index] == Vector3.zero ? Vector3.up : data.normals[index];
                Vector3 downhill = Vector3.ProjectOnPlane(new Vector3(normal.x, 0f, normal.z), Vector3.up);
                if (downhill.sqrMagnitude < 0.001f)
                    downhill = UnityEngine.Random.insideUnitSphere;
                downhill.y = 0f;
                instance.transform.rotation = Quaternion.LookRotation(downhill.normalized, Vector3.up) *
                    Quaternion.Euler(0f, (float)rng.NextDouble() * 35f - 17.5f, 0f);
                float scale = Mathf.Lerp(profile.cliffs.randomScaleRange.x, profile.cliffs.randomScaleRange.y, (float)rng.NextDouble());
                instance.transform.localScale = Vector3.one * scale;
                positions.Add(position);
                spawned++;
            }
        }
    }
}
