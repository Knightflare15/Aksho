using System.Collections.Generic;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

#if UNITY_EDITOR
using UnityEditor;
#endif


public sealed partial class LevelGridDirector
{
    void RebuildSurfaceNetTerrainMesh()
    {
        ClearOptimizedTerrainMeshes();
        EnsurePaintedCellCache();
        if (paintedCells.Count == 0)
            return;

        float[,] heightSamples = BuildSurfaceNetHeightSamples();
        Dictionary<int, SurfaceNetCliffCornerPatch> cliffCornerPatches =
            BuildSurfaceNetCliffCornerPatches(heightSamples);
        TerrainChunkMeshBuilder builder = new TerrainChunkMeshBuilder();
        AddSurfaceNetTopFaces(builder, heightSamples, cliffCornerPatches);
        if (surfaceNetEmitSideFaces)
            AddSurfaceNetCliffFaces(builder, heightSamples, cliffCornerPatches);
        Transform root = EnsureOptimizedTerrainRoot();
        if (builder.HasGeometry)
            CreateOptimizedTerrainChunk(root, builder, 0, 0, 0);
        CreateWaterPlane(root);
    }

    float[,] BuildSurfaceNetHeightSamples()
    {
        float[,] samples = new float[GridWidth + 1, GridHeight + 1];

        for (int z = 0; z <= GridHeight; z++)
        {
            for (int x = 0; x <= GridWidth; x++)
                samples[x, z] = CalculateSurfaceNetGridHeight(x, z);
        }

        int passes = Mathf.Clamp(surfaceNetHeightSmoothingPasses, 0, 6);
        float strength = Mathf.Clamp01(surfaceNetHeightSmoothingStrength);
        for (int pass = 0; pass < passes; pass++)
            SmoothSurfaceNetHeightSamples(samples, strength);

        return samples;
    }

    float CalculateSurfaceNetGridHeight(int gridX, int gridZ)
    {
        float total = 0f;
        float minHeight = float.MaxValue;
        float maxHeight = float.MinValue;
        int count = 0;
        for (int z = -1; z <= 0; z++)
        {
            for (int x = -1; x <= 0; x++)
            {
                Vector2Int cell = new Vector2Int(gridX + x, gridZ + z);
                float height = ContainsCell(cell.x, cell.y) ? GetCellHeight(cell) : 0f;
                total += height;
                minHeight = Mathf.Min(minHeight, height);
                maxHeight = Mathf.Max(maxHeight, height);
                count++;
            }
        }

        float average = count > 0 ? total / count : 0f;
        if (!surfaceNetPreserveCliffs || maxHeight - minHeight < surfaceNetCliffHeightDifferenceThreshold)
            return average;

        return maxHeight;
    }

    void SmoothSurfaceNetHeightSamples(float[,] samples, float strength)
    {
        if (strength <= 0f)
            return;

        float[,] next = new float[GridWidth + 1, GridHeight + 1];
        for (int z = 0; z <= GridHeight; z++)
        {
            for (int x = 0; x <= GridWidth; x++)
            {
                float total = 0f;
                int count = 0;
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int sampleX = x + dx;
                        int sampleZ = z + dz;
                        if (sampleX < 0 || sampleX > GridWidth || sampleZ < 0 || sampleZ > GridHeight)
                            continue;

                        float neighbor = samples[sampleX, sampleZ];
                        if (surfaceNetPreserveCliffs &&
                            Mathf.Abs(neighbor - samples[x, z]) >= surfaceNetCliffHeightDifferenceThreshold)
                            continue;

                        total += neighbor;
                        count++;
                    }
                }

                float average = count > 0 ? total / count : samples[x, z];
                next[x, z] = Mathf.Lerp(samples[x, z], average, strength);
            }
        }

        for (int z = 0; z <= GridHeight; z++)
        {
            for (int x = 0; x <= GridWidth; x++)
                samples[x, z] = next[x, z];
        }
    }

    bool TryCreateSurfaceNetVertex(float[,] heightSamples, float[] yLevels, int x, int y, int z, out Vector3 vertex)
    {
        float[] values = new float[8];
        Vector3[] positions = new Vector3[8];
        bool hasSolid = false;
        bool hasEmpty = false;

        for (int corner = 0; corner < 8; corner++)
        {
            int sampleX = x + ((corner & 1) != 0 ? 1 : 0);
            int sampleY = y + ((corner & 2) != 0 ? 1 : 0);
            int sampleZ = z + ((corner & 4) != 0 ? 1 : 0);
            float value = heightSamples[sampleX, sampleZ] - yLevels[sampleY];
            values[corner] = value;
            positions[corner] = SurfaceNetSampleLocalPosition(sampleX, yLevels[sampleY], sampleZ);

            if (value > 0f)
                hasSolid = true;
            else
                hasEmpty = true;
        }

        if (!hasSolid || !hasEmpty)
        {
            vertex = default;
            return false;
        }

        Vector3 total = Vector3.zero;
        int crossingCount = 0;
        AddSurfaceNetEdgeCrossing(values, positions, 0, 1, ref total, ref crossingCount);
        AddSurfaceNetEdgeCrossing(values, positions, 2, 3, ref total, ref crossingCount);
        AddSurfaceNetEdgeCrossing(values, positions, 4, 5, ref total, ref crossingCount);
        AddSurfaceNetEdgeCrossing(values, positions, 6, 7, ref total, ref crossingCount);
        AddSurfaceNetEdgeCrossing(values, positions, 0, 2, ref total, ref crossingCount);
        AddSurfaceNetEdgeCrossing(values, positions, 1, 3, ref total, ref crossingCount);
        AddSurfaceNetEdgeCrossing(values, positions, 4, 6, ref total, ref crossingCount);
        AddSurfaceNetEdgeCrossing(values, positions, 5, 7, ref total, ref crossingCount);
        AddSurfaceNetEdgeCrossing(values, positions, 0, 4, ref total, ref crossingCount);
        AddSurfaceNetEdgeCrossing(values, positions, 1, 5, ref total, ref crossingCount);
        AddSurfaceNetEdgeCrossing(values, positions, 2, 6, ref total, ref crossingCount);
        AddSurfaceNetEdgeCrossing(values, positions, 3, 7, ref total, ref crossingCount);

        vertex = crossingCount > 0 ? total / crossingCount : GridToLocal(x, z);
        return true;
    }

    void AddSurfaceNetEdgeCrossing(
        float[] values,
        Vector3[] positions,
        int a,
        int b,
        ref Vector3 total,
        ref int crossingCount)
    {
        bool solidA = values[a] > 0f;
        bool solidB = values[b] > 0f;
        if (solidA == solidB)
            return;

        float denominator = values[a] - values[b];
        float t = Mathf.Abs(denominator) > 0.0001f ? values[a] / denominator : 0.5f;
        total += Vector3.Lerp(positions[a], positions[b], Mathf.Clamp01(t));
        crossingCount++;
    }

    void AddSurfaceNetTopFaces(
        TerrainChunkMeshBuilder builder,
        float[,] heightSamples,
        Dictionary<int, SurfaceNetCliffCornerPatch> cliffCornerPatches)
    {
        for (int z = 0; z < GridHeight; z++)
        {
            for (int x = 0; x < GridWidth; x++)
            {
                Vector2Int cell = new Vector2Int(x, z);
                int heightLevel = GetCellHeight(cell);
                if (heightLevel <= 0)
                    continue;

                TerrainMeshMaterialSlot slot = GetMaterialSlot(GetTopFaceType(heightLevel, GetCellSurface(cell)));
                AddSubdividedSurfaceNetTopFace(builder, slot, cell, heightSamples, cliffCornerPatches);
            }
        }
    }

    void AddSubdividedSurfaceNetTopFace(
        TerrainChunkMeshBuilder builder,
        TerrainMeshMaterialSlot slot,
        Vector2Int cell,
        float[,] heightSamples,
        Dictionary<int, SurfaceNetCliffCornerPatch> cliffCornerPatches)
    {
        float h00 = CalculateSurfaceNetCellCornerHeight(cell, 0, 0, heightSamples);
        float h01 = CalculateSurfaceNetCellCornerHeight(cell, 0, 1, heightSamples);
        float h11 = CalculateSurfaceNetCellCornerHeight(cell, 1, 1, heightSamples);
        float h10 = CalculateSurfaceNetCellCornerHeight(cell, 1, 0, heightSamples);

        if (TryAddWeightedSurfaceBlendTopFace(
                builder,
                slot,
                cell,
                cliffCornerPatches,
                h00,
                h01,
                h11,
                h10))
            return;

        if (surfaceNetMarchingSquaresTopCorners &&
            TryAddMarchingSquaresTopFace(
                builder,
                slot,
                cell,
                cliffCornerPatches,
                h00,
                h01,
                h11,
                h10))
            return;

        int subdivisions = Mathf.Clamp(surfaceNetTopSubdivisions, 1, 4);
        for (int z = 0; z < subdivisions; z++)
        {
            float v0 = z / (float)subdivisions;
            float v1 = (z + 1) / (float)subdivisions;
            for (int x = 0; x < subdivisions; x++)
            {
                float u0 = x / (float)subdivisions;
                float u1 = (x + 1) / (float)subdivisions;
                AddQuad(
                    builder,
                    slot,
                    SurfaceNetInterpolatedLocalPosition(cell, u0, v0, h00, h01, h11, h10),
                    SurfaceNetInterpolatedLocalPosition(cell, u0, v1, h00, h01, h11, h10),
                    SurfaceNetInterpolatedLocalPosition(cell, u1, v1, h00, h01, h11, h10),
                    SurfaceNetInterpolatedLocalPosition(cell, u1, v0, h00, h01, h11, h10));
            }
        }
    }

    bool TryAddWeightedSurfaceBlendTopFace(
        TerrainChunkMeshBuilder builder,
        TerrainMeshMaterialSlot currentSlot,
        Vector2Int cell,
        Dictionary<int, SurfaceNetCliffCornerPatch> cliffCornerPatches,
        float h00,
        float h01,
        float h11,
        float h10)
    {
        if (!surfaceBlendEnabled || !IsGrassOrDirtTopSlot(currentSlot))
            return false;

        if (ShouldTrimSurfaceNetSideCorner(cliffCornerPatches, cell, 0, 0) ||
            ShouldTrimSurfaceNetSideCorner(cliffCornerPatches, cell, 0, 1) ||
            ShouldTrimSurfaceNetSideCorner(cliffCornerPatches, cell, 1, 1) ||
            ShouldTrimSurfaceNetSideCorner(cliffCornerPatches, cell, 1, 0))
            return false;

        bool nearTransition = HasNearbySurfaceBlendTransition(cell);
        int subdivisions = nearTransition
            ? Mathf.Clamp(surfaceBlendSubdivisions, 1, 16)
            : Mathf.Clamp(surfaceNetTopSubdivisions, 1, 4);
        for (int z = 0; z < subdivisions; z++)
        {
            float v0 = z / (float)subdivisions;
            float v1 = (z + 1) / (float)subdivisions;
            for (int x = 0; x < subdivisions; x++)
            {
                float u0 = x / (float)subdivisions;
                float u1 = (x + 1) / (float)subdivisions;
                AddWeightedSurfaceBlendQuad(
                    builder,
                    cell,
                    h00,
                    h01,
                    h11,
                    h10,
                    u0,
                    v0,
                    u0,
                    v1,
                    u1,
                    v1,
                    u1,
                    v0);
            }
        }

        return true;
    }

    void AddWeightedSurfaceBlendQuad(
        TerrainChunkMeshBuilder builder,
        Vector2Int cell,
        float h00,
        float h01,
        float h11,
        float h10,
        float u0,
        float v0,
        float u1,
        float v1,
        float u2,
        float v2,
        float u3,
        float v3)
    {
        AddQuad(
            builder,
            TerrainMeshMaterialSlot.GrassDirtBlendTop,
            SurfaceNetInterpolatedLocalPosition(cell, u0, v0, h00, h01, h11, h10),
            SurfaceNetInterpolatedLocalPosition(cell, u1, v1, h00, h01, h11, h10),
            SurfaceNetInterpolatedLocalPosition(cell, u2, v2, h00, h01, h11, h10),
            SurfaceNetInterpolatedLocalPosition(cell, u3, v3, h00, h01, h11, h10),
            CalculateSurfaceBlendDirtWeight(cell.x + u0, cell.y + v0),
            CalculateSurfaceBlendDirtWeight(cell.x + u1, cell.y + v1),
            CalculateSurfaceBlendDirtWeight(cell.x + u2, cell.y + v2),
            CalculateSurfaceBlendDirtWeight(cell.x + u3, cell.y + v3));
    }

    bool HasNearbySurfaceBlendTransition(Vector2Int cell)
    {
        if (!TryGetSurfaceBlendDirtValue(cell, out bool centerDirt))
            return false;

        int radius = Mathf.Max(1, Mathf.CeilToInt(surfaceBlendWidthCells));
        for (int z = -radius; z <= radius; z++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                if (x == 0 && z == 0)
                    continue;

                Vector2Int neighbor = new Vector2Int(cell.x + x, cell.y + z);
                if (TryGetSurfaceBlendDirtValue(neighbor, out bool neighborDirt) && neighborDirt != centerDirt)
                    return true;
            }
        }

        return false;
    }

    float CalculateSurfaceBlendDirtWeight(float gridX, float gridZ)
    {
        float width = Mathf.Max(0.1f, surfaceBlendWidthCells);
        float falloffPower = Mathf.Max(0.25f, surfaceBlendFalloffPower);
        int radius = Mathf.Max(1, Mathf.CeilToInt(width));
        int centerX = Mathf.FloorToInt(gridX);
        int centerZ = Mathf.FloorToInt(gridZ);
        float dirtTotal = 0f;
        float total = 0f;

        for (int z = -radius; z <= radius; z++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                Vector2Int sampleCell = new Vector2Int(centerX + x, centerZ + z);
                if (!TryGetSurfaceBlendDirtValue(sampleCell, out bool isDirt))
                    continue;

                float sampleX = sampleCell.x + 0.5f;
                float sampleZ = sampleCell.y + 0.5f;
                float distance = Vector2.Distance(new Vector2(gridX, gridZ), new Vector2(sampleX, sampleZ));
                float influence = Mathf.Pow(Mathf.Clamp01(1f - distance / width), falloffPower);
                if (influence <= 0f)
                    continue;

                total += influence;
                if (isDirt)
                    dirtTotal += influence;
            }
        }

        if (total > 0.0001f)
            return Mathf.Clamp01(dirtTotal / total);

        Vector2Int fallbackCell = new Vector2Int(
            Mathf.Clamp(centerX, 0, GridWidth - 1),
            Mathf.Clamp(centerZ, 0, GridHeight - 1));
        return TryGetSurfaceBlendDirtValue(fallbackCell, out bool fallbackDirt) && fallbackDirt ? 1f : 0f;
    }

    bool TryGetSurfaceBlendDirtValue(Vector2Int cell, out bool isDirt)
    {
        isDirt = false;
        if (!ContainsCell(cell.x, cell.y))
            return false;

        int height = GetCellHeight(cell);
        if (height <= 0)
            return false;

        TerrainMeshMaterialSlot slot = GetMaterialSlot(GetTopFaceType(height, GetCellSurface(cell)));
        if (!IsGrassOrDirtTopSlot(slot))
            return false;

        isDirt = slot == TerrainMeshMaterialSlot.DirtTop;
        return true;
    }
}
