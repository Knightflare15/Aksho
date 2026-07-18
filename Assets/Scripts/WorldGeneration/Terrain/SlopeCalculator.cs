using UnityEngine;

public static class SlopeCalculator
{
    public static void Calculate(WorldData data)
    {
        for (int z = 0; z < data.resolution; z++)
        {
            for (int x = 0; x < data.resolution; x++)
            {
                float left = Sample(data, x - 1, z);
                float right = Sample(data, x + 1, z);
                float down = Sample(data, x, z - 1);
                float up = Sample(data, x, z + 1);
                float dx = (right - left) / Mathf.Max(0.001f, data.gridSpacing * 2f);
                float dz = (up - down) / Mathf.Max(0.001f, data.gridSpacing * 2f);
                data.slope[data.Index(x, z)] = Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz)) * Mathf.Rad2Deg;
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
