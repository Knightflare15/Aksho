using System;
using UnityEngine;

public static class HeightmapGenerator
{
    public static void Generate(WorldData data, WorldGenerationProfile profile, System.Random rng)
    {
        float macroX = (float)rng.NextDouble() * 2000f;
        float macroZ = (float)rng.NextDouble() * 2000f;
        float hillX = (float)rng.NextDouble() * 2000f;
        float hillZ = (float)rng.NextDouble() * 2000f;
        float detailX = (float)rng.NextDouble() * 2000f;
        float detailZ = (float)rng.NextDouble() * 2000f;
        float center = (data.resolution - 1) * 0.5f;
        float invCenter = 1f / Mathf.Max(1f, center);

        for (int z = 0; z < data.resolution; z++)
        {
            for (int x = 0; x < data.resolution; x++)
            {
                int index = data.Index(x, z);
                float nx = (x - center) * invCenter;
                float nz = (z - center) * invCenter;
                float radial = Mathf.Clamp01(1f - Mathf.Sqrt(nx * nx + nz * nz));
                float macro = Fractal(x, z, profile.macroTerrain, macroX, macroZ) * 0.28f;
                float hills = Fractal(x, z, profile.rollingHills, hillX, hillZ) * 0.55f;
                float detail = Fractal(x, z, profile.detailVariation, detailX, detailZ);
                float islandLift = Mathf.SmoothStep(0f, 1f, radial);
                float land = Mathf.Clamp01(data.landBlend[index]);
                float landShape = Mathf.SmoothStep(0.08f, 0.92f, land);
                float terrainHeight = profile.waterLevel + 1.15f + islandLift * 8.5f + macro + hills + detail;
                float seabedHeight = profile.waterLevel - Mathf.Lerp(1.65f, 3.4f, 1f - radial);
                float height = Mathf.Lerp(seabedHeight, terrainHeight, landShape);

                if (land > 0.42f)
                {
                    float minimumLandHeight = profile.waterLevel + Mathf.Lerp(0.18f, 3.4f, Mathf.SmoothStep(0.42f, 1f, land));
                    height = Mathf.Max(height, minimumLandHeight);
                }

                data.height[index] = height;
                data.waterMask[index] = height <= profile.waterLevel ? (byte)255 : (byte)0;
            }
        }

        SmoothHeightField(data, profile.waterLevel, 2);
        RefreshMasksFromHeight(data, profile.waterLevel);

        // Rebuild the masks after every shore pass. Raising a sea neighbour can
        // turn it into the new shoreline; treating it as land on the following
        // pass prevents a hidden cliff one cell farther out.
        for (int pass = 0; pass < 5; pass++)
        {
            SoftenCoastlineHeight(data, 1.6f, 1);
            RefreshMasksFromHeight(data, profile.waterLevel);
        }
    }

    static void SoftenCoastlineHeight(WorldData data, float maxAdjacentDrop, int passes)
    {
        if (data == null || data.height == null || data.landMask == null)
            return;

        float[] source = new float[data.height.Length];
        float[] target = new float[data.height.Length];
        for (int pass = 0; pass < Mathf.Max(0, passes); pass++)
        {
            System.Array.Copy(data.height, source, source.Length);
            System.Array.Copy(source, target, source.Length);
            for (int z = 1; z < data.resolution - 1; z++)
            {
                for (int x = 1; x < data.resolution - 1; x++)
                {
                    int index = data.Index(x, z);
                    if (data.landMask[index] == 0)
                        continue;

                    RaiseCoastNeighbor(data, source, target, x, z, x - 1, z, maxAdjacentDrop);
                    RaiseCoastNeighbor(data, source, target, x, z, x + 1, z, maxAdjacentDrop);
                    RaiseCoastNeighbor(data, source, target, x, z, x, z - 1, maxAdjacentDrop);
                    RaiseCoastNeighbor(data, source, target, x, z, x, z + 1, maxAdjacentDrop);
                }
            }

            System.Array.Copy(target, data.height, target.Length);
        }
    }

    static void RaiseCoastNeighbor(
        WorldData data,
        float[] source,
        float[] target,
        int landX,
        int landZ,
        int neighborX,
        int neighborZ,
        float maxAdjacentDrop)
    {
        int neighbor = data.Index(neighborX, neighborZ);
        if (data.landMask[neighbor] > 0)
            return;

        int land = data.Index(landX, landZ);
        target[neighbor] = Mathf.Max(target[neighbor], source[land] - Mathf.Max(0.1f, maxAdjacentDrop));
    }

    static float Fractal(int x, int z, WorldNoiseSettings settings, float offsetX, float offsetZ)
    {
        float value = 0f;
        float amplitude = settings.amplitude;
        float frequency = Mathf.Max(0.0001f, settings.scale);
        float maxAmplitude = 0f;
        int octaves = Mathf.Clamp(settings.octaves, 1, 6);

        for (int i = 0; i < octaves; i++)
        {
            value += (Mathf.PerlinNoise(x * frequency + offsetX, z * frequency + offsetZ) * 2f - 1f) * amplitude;
            maxAmplitude += amplitude;
            amplitude *= settings.persistence;
            frequency *= settings.lacunarity;
        }

        return maxAmplitude <= 0f ? 0f : value;
    }

    static void SmoothHeightField(WorldData data, float waterLevel, int passes)
    {
        float[] source = new float[data.height.Length];
        float[] target = new float[data.height.Length];

        for (int pass = 0; pass < passes; pass++)
        {
            System.Array.Copy(data.height, source, data.height.Length);
            System.Array.Copy(data.height, target, data.height.Length);

            for (int z = 1; z < data.resolution - 1; z++)
            {
                for (int x = 1; x < data.resolution - 1; x++)
                {
                    int index = data.Index(x, z);
                    float land = data.landBlend[index];
                    float shoreInfluence = 1f - Mathf.Abs(land - 0.42f) / 0.42f;
                    shoreInfluence = Mathf.Clamp01(shoreInfluence);
                    float average =
                        source[data.Index(x - 1, z)] +
                        source[data.Index(x + 1, z)] +
                        source[data.Index(x, z - 1)] +
                        source[data.Index(x, z + 1)] +
                        source[index] * 2f;
                    average /= 6f;
                    target[index] = Mathf.Lerp(source[index], average, 0.55f * shoreInfluence);
                }
            }

            System.Array.Copy(target, data.height, data.height.Length);
        }
    }

    static void RefreshMasksFromHeight(WorldData data, float waterLevel)
    {
        for (int i = 0; i < data.height.Length; i++)
        {
            data.waterMask[i] = data.height[i] <= waterLevel ? (byte)255 : (byte)0;
            data.landMask[i] = data.height[i] > waterLevel + 0.08f ? (byte)255 : (byte)0;
        }
    }
}
