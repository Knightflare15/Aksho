using System;
using UnityEngine;

public static class IslandMaskGenerator
{
    public static void Generate(WorldData data, WorldGenerationProfile profile, System.Random rng)
    {
        float offsetX = (float)rng.NextDouble() * 1000f;
        float offsetZ = (float)rng.NextDouble() * 1000f;
        float phaseA = (float)rng.NextDouble() * Mathf.PI * 2f;
        float phaseB = (float)rng.NextDouble() * Mathf.PI * 2f;
        float axisX = Mathf.Lerp(0.86f, 1.16f, (float)rng.NextDouble());
        float axisZ = Mathf.Lerp(0.86f, 1.16f, (float)rng.NextDouble());
        float center = (data.resolution - 1) * 0.5f;
        float invCenter = 1f / Mathf.Max(1f, center);
        IslandSettings settings = profile.island;

        for (int z = 0; z < data.resolution; z++)
        {
            for (int x = 0; x < data.resolution; x++)
            {
                int index = data.Index(x, z);
                float nx = (x - center) * invCenter;
                float nz = (z - center) * invCenter;
                float warpX = Mathf.PerlinNoise(x * settings.distortionScale * 0.75f + offsetX + 19.3f, z * settings.distortionScale * 0.75f + offsetZ - 4.7f) * 2f - 1f;
                float warpZ = Mathf.PerlinNoise(x * settings.distortionScale * 0.75f - offsetX + 8.1f, z * settings.distortionScale * 0.75f - offsetZ - 27.5f) * 2f - 1f;
                nx += warpX * settings.domainWarpStrength;
                nz += warpZ * settings.domainWarpStrength;
                float distortion = Mathf.PerlinNoise(
                    x * settings.distortionScale + offsetX,
                    z * settings.distortionScale + offsetZ) * 2f - 1f;
                float angle = Mathf.Atan2(nz, nx);
                float lobes =
                    Mathf.Sin(angle * 3f + phaseA) * 0.58f +
                    Mathf.Sin(angle * 5f + phaseB) * 0.32f +
                    Mathf.Sin(angle * 7f - phaseA * 0.7f) * 0.1f;
                float lobeScale = 1f + lobes * settings.coastlineLobing;
                float shapedDistance = Mathf.Sqrt((nx / axisX) * (nx / axisX) + (nz / axisZ) * (nz / axisZ)) / Mathf.Max(0.65f, lobeScale);
                float distance = shapedDistance + distortion * settings.distortionStrength;
                float minimumPlayableRadius = Mathf.Clamp(settings.minimumPlayableRadius, 0.45f, 0.9f);
                float outerWaterRadius = Mathf.Clamp(Mathf.Max(settings.edgeWaterBias, minimumPlayableRadius + 0.12f), 0.58f, 0.98f);
                float innerLandRadius = Mathf.Clamp(outerWaterRadius - Mathf.Max(0.08f, settings.beachWidth), minimumPlayableRadius, outerWaterRadius);
                float landScore = 1f - Mathf.SmoothStep(innerLandRadius, outerWaterRadius, distance);
                landScore = Mathf.Lerp(1f, landScore, Mathf.Clamp01(settings.islandStrength));

                // Keep v1 useful as an arena generator without forcing a perfect circle.
                if (shapedDistance <= minimumPlayableRadius)
                    landScore = Mathf.Max(landScore, 0.9f);

                data.landBlend[index] = Mathf.Clamp01(landScore);
                data.landMask[index] = landScore > 0.42f ? (byte)255 : (byte)0;
            }
        }

        SmoothLandBlend(data, 2);
        for (int i = 0; i < data.landMask.Length; i++)
            data.landMask[i] = data.landBlend[i] > 0.40f ? (byte)255 : (byte)0;
    }

    static void SmoothLandBlend(WorldData data, int passes)
    {
        if (data == null || data.landBlend == null)
            return;

        float[] source = new float[data.landBlend.Length];
        float[] target = new float[data.landBlend.Length];
        for (int pass = 0; pass < Mathf.Max(0, passes); pass++)
        {
            System.Array.Copy(data.landBlend, source, data.landBlend.Length);
            System.Array.Copy(source, target, source.Length);
            for (int z = 1; z < data.resolution - 1; z++)
            {
                for (int x = 1; x < data.resolution - 1; x++)
                {
                    int index = data.Index(x, z);
                    target[index] = (source[index] * 4f +
                                     source[data.Index(x - 1, z)] +
                                     source[data.Index(x + 1, z)] +
                                     source[data.Index(x, z - 1)] +
                                     source[data.Index(x, z + 1)]) / 8f;
                }
            }

            System.Array.Copy(target, data.landBlend, target.Length);
        }
    }
}
