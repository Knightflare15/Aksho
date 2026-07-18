using UnityEngine;

public static class TerrainPainter
{
    public static void Paint(WorldData data, WorldGenerationProfile profile)
    {
        for (int z = 0; z < data.resolution; z++)
        {
            for (int x = 0; x < data.resolution; x++)
            {
                int index = data.Index(x, z);
                float shoreBlend = Mathf.Clamp01(1f - Mathf.Abs(data.landBlend[index] - 0.48f) / 0.28f);
                float grass = data.landMask[index] > 0 ? Mathf.Lerp(0.85f, 1f, Mathf.Clamp01(data.landBlend[index])) : 0f;
                float dirt = Mathf.Pow(data.pathMask[index] / 255f, 1.35f) * 0.85f;
                float rock = Mathf.Max(data.cliffMask[index] / 255f, Mathf.InverseLerp(35f, 55f, data.slope[index]));
                float sand = Mathf.Max(
                    Mathf.InverseLerp(2.2f, 0f, Mathf.Abs(data.height[index] - profile.waterLevel)),
                    shoreBlend * 0.8f);

                if (data.waterMask[index] > 0)
                {
                    grass = 0f;
                    dirt = 0f;
                    rock *= 0.35f;
                    sand = Mathf.Max(sand, 0.9f);
                }

                if (data.clearingMask[index] > 0)
                {
                    grass = Mathf.Max(grass, 0.9f);
                    rock *= 0.35f;
                }

                dirt = Mathf.Max(dirt, Mathf.Pow(data.dirtMask[index] / 255f, 1.25f) * 0.9f);
                rock = Mathf.Max(rock, data.rockMask[index] / 255f);
                sand = Mathf.Max(sand, data.sandMask[index] / 255f);

                float total = Mathf.Max(0.001f, grass + dirt + rock + sand);
                data.grassMask[index] = ToByte(grass / total);
                data.dirtMask[index] = ToByte(dirt / total);
                data.rockMask[index] = ToByte(rock / total);
                data.sandMask[index] = ToByte(sand / total);
            }
        }
    }

    static byte ToByte(float value)
    {
        return (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(value) * 255f), 0, 255);
    }
}
