using System.Collections.Generic;
using UnityEngine;

public sealed class WorldData
{
    public int seed;
    public int worldSizeMeters;
    public int resolution;
    public float gridSpacing;
    public float waterLevel;

    public float[] height;
    public float[] landBlend;
    public byte[] landMask;
    public byte[] waterMask;
    public byte[] pathMask;
    public byte[] clearingMask;
    public byte[] cliffMask;
    public byte[] grassMask;
    public byte[] dirtMask;
    public byte[] rockMask;
    public byte[] sandMask;
    public byte[] safeMask;
    public byte[] dangerMask;
    public byte[] reachableMask;
    public float[] slope;
    public Vector3[] normals;

    public List<ZoneData> zones = new List<ZoneData>();
    public List<PathData> paths = new List<PathData>();
    public List<ObjectivePoint> objectives = new List<ObjectivePoint>();
    public List<ChestPoint> chests = new List<ChestPoint>();
    public List<EnemyTriggerData> enemyTriggers = new List<EnemyTriggerData>();
    public List<SpawnPointData> spawnPoints = new List<SpawnPointData>();

    public WorldData(int seed, int worldSizeMeters, float gridSpacing, float waterLevel)
    {
        this.seed = seed;
        this.worldSizeMeters = Mathf.Max(1, worldSizeMeters);
        this.gridSpacing = Mathf.Max(0.1f, gridSpacing);
        this.waterLevel = waterLevel;
        resolution = Mathf.RoundToInt(this.worldSizeMeters / this.gridSpacing) + 1;

        int count = resolution * resolution;
        height = new float[count];
        landBlend = new float[count];
        landMask = new byte[count];
        waterMask = new byte[count];
        pathMask = new byte[count];
        clearingMask = new byte[count];
        cliffMask = new byte[count];
        grassMask = new byte[count];
        dirtMask = new byte[count];
        rockMask = new byte[count];
        sandMask = new byte[count];
        safeMask = new byte[count];
        dangerMask = new byte[count];
        reachableMask = new byte[count];
        slope = new float[count];
        normals = new Vector3[count];
    }

    public int Index(int x, int z)
    {
        return z * resolution + x;
    }

    public bool InBounds(int x, int z)
    {
        return x >= 0 && z >= 0 && x < resolution && z < resolution;
    }

    public Vector3 GridToWorld(int x, int z)
    {
        float half = worldSizeMeters * 0.5f;
        int index = Index(Mathf.Clamp(x, 0, resolution - 1), Mathf.Clamp(z, 0, resolution - 1));
        return new Vector3(x * gridSpacing - half, height[index], z * gridSpacing - half);
    }

    public bool WorldToGrid(Vector3 pos, out int x, out int z)
    {
        float half = worldSizeMeters * 0.5f;
        x = Mathf.RoundToInt((pos.x + half) / gridSpacing);
        z = Mathf.RoundToInt((pos.z + half) / gridSpacing);
        return InBounds(x, z);
    }

    public float SampleHeightWorld(Vector3 pos)
    {
        if (!WorldToGrid(pos, out int x, out int z))
            return waterLevel;
        return height[Index(x, z)];
    }

    public Vector3 SampleNormalWorld(Vector3 pos)
    {
        if (!WorldToGrid(pos, out int x, out int z))
            return Vector3.up;
        return normals[Index(x, z)] == Vector3.zero ? Vector3.up : normals[Index(x, z)];
    }

    public bool IsWalkable(int x, int z)
    {
        if (!InBounds(x, z))
            return false;

        int index = Index(x, z);
        return landMask[index] > 0 &&
               waterMask[index] == 0 &&
               cliffMask[index] == 0 &&
               slope[index] <= 38f;
    }
}

public static class WorldPlacementUtility
{
    public static bool IsDryPlayableCell(WorldData data, int x, int z, float maxSlope, float minHeightAboveWater = 0.45f)
    {
        if (data == null || !data.InBounds(x, z))
            return false;

        int index = data.Index(x, z);
        return data.reachableMask[index] > 0 &&
               data.landMask[index] > 0 &&
               data.waterMask[index] == 0 &&
               data.cliffMask[index] == 0 &&
               data.height[index] >= data.waterLevel + minHeightAboveWater &&
               data.slope[index] <= maxSlope;
    }

    public static Vector3 GroundedPosition(WorldData data, int x, int z, float yOffset = 0f)
    {
        Vector3 position = data.GridToWorld(x, z);
        position.y = data.height[data.Index(x, z)] + yOffset;
        return position;
    }

    public static void GroundVisibleBounds(Transform target, float groundY, float extraOffset = 0f)
    {
        if (target == null)
            return;

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        bool found = false;
        Bounds bounds = default;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || renderer.GetComponent<TMPro.TextMeshPro>() != null)
                continue;
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (found)
            target.position += Vector3.up * (groundY + extraOffset - bounds.min.y);
        else
            target.position = new Vector3(target.position.x, groundY + extraOffset, target.position.z);
    }
}
