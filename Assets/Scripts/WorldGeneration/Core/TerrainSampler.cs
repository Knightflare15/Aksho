using UnityEngine;

public sealed class TerrainSampler
{
    readonly WorldData data;

    public TerrainSampler(WorldData data)
    {
        this.data = data;
    }

    public float SampleHeight(Vector3 worldPosition)
    {
        return data != null ? data.SampleHeightWorld(worldPosition) : worldPosition.y;
    }

    public Vector3 SampleNormal(Vector3 worldPosition)
    {
        return data != null ? data.SampleNormalWorld(worldPosition) : Vector3.up;
    }

    public bool IsWalkable(Vector3 worldPosition)
    {
        return data != null &&
               data.WorldToGrid(worldPosition, out int x, out int z) &&
               data.IsWalkable(x, z);
    }
}
