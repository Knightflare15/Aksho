using UnityEngine;

public static class NormalBuilder
{
    public static void Build(WorldData data)
    {
        for (int z = 0; z < data.resolution; z++)
        {
            for (int x = 0; x < data.resolution; x++)
            {
                float left = Sample(data, x - 1, z);
                float right = Sample(data, x + 1, z);
                float down = Sample(data, x, z - 1);
                float up = Sample(data, x, z + 1);
                Vector3 normal = new Vector3(left - right, data.gridSpacing * 2f, down - up).normalized;
                data.normals[data.Index(x, z)] = normal.sqrMagnitude < 0.01f ? Vector3.up : normal;
            }
        }
    }

    static float Sample(WorldData data, int x, int z)
    {
        x = Mathf.Clamp(x, 0, data.resolution - 1);
        z = Mathf.Clamp(z, 0, data.resolution - 1);
        return data.height[data.Index(x, z)];
    }
}
