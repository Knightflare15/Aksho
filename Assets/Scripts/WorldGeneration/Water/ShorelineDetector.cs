using UnityEngine;

public static class ShorelineDetector
{
    public static byte[] Build(WorldData data, float width)
    {
        byte[] shoreline = new byte[data.resolution * data.resolution];
        for (int z = 0; z < data.resolution; z++)
        {
            for (int x = 0; x < data.resolution; x++)
            {
                int index = data.Index(x, z);
                float distance = Mathf.Abs(data.height[index] - data.waterLevel);
                shoreline[index] = distance <= width ? (byte)Mathf.RoundToInt((1f - distance / width) * 255f) : (byte)0;
            }
        }
        return shoreline;
    }
}
