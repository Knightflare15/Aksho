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
    void AddSurfaceNetWaterShoreFace(
        TerrainChunkMeshBuilder builder,
        float[,] heightSamples,
        Vector2Int cell,
        Vector2Int direction,
        float minX,
        float maxX,
        float minZ,
        float maxZ,
        float start,
        float end)
    {
        float skirtDistance = Mathf.Max(0.05f, surfaceNetWaterShoreSkirtCells) * CellSize;
        float bedY = SurfaceNetHeightToLocalY(waterPlaneHeightCells + surfaceNetWaterShoreBedOffsetCells);

        if (direction.x > 0)
        {
            float highX = maxX;
            float lowX = maxX + skirtDistance;
            float startZ = Mathf.Lerp(minZ, maxZ, start);
            float endZ = Mathf.Lerp(minZ, maxZ, end);
            float highY0 = GetSurfaceNetEdgeY(cell, heightSamples, 1f, start);
            float highY1 = GetSurfaceNetEdgeY(cell, heightSamples, 1f, end);
            AddLayeredSurfaceNetWaterShoreFace(
                builder,
                new Vector3(highX, highY0, startZ),
                new Vector3(highX, highY1, endZ),
                new Vector3(lowX, bedY, startZ),
                new Vector3(lowX, bedY, endZ));
            return;
        }

        if (direction.x < 0)
        {
            float highX = minX;
            float lowX = minX - skirtDistance;
            float startZ = Mathf.Lerp(minZ, maxZ, start);
            float endZ = Mathf.Lerp(minZ, maxZ, end);
            float highY0 = GetSurfaceNetEdgeY(cell, heightSamples, 0f, start);
            float highY1 = GetSurfaceNetEdgeY(cell, heightSamples, 0f, end);
            AddLayeredSurfaceNetWaterShoreFace(
                builder,
                new Vector3(highX, highY0, startZ),
                new Vector3(highX, highY1, endZ),
                new Vector3(lowX, bedY, startZ),
                new Vector3(lowX, bedY, endZ));
            return;
        }

        if (direction.y > 0)
        {
            float highZ = maxZ;
            float lowZ = maxZ + skirtDistance;
            float startX = Mathf.Lerp(minX, maxX, start);
            float endX = Mathf.Lerp(minX, maxX, end);
            float highY0 = GetSurfaceNetEdgeY(cell, heightSamples, start, 1f);
            float highY1 = GetSurfaceNetEdgeY(cell, heightSamples, end, 1f);
            AddLayeredSurfaceNetWaterShoreFace(
                builder,
                new Vector3(startX, highY0, highZ),
                new Vector3(endX, highY1, highZ),
                new Vector3(startX, bedY, lowZ),
                new Vector3(endX, bedY, lowZ));
            return;
        }

        float backHighZ = minZ;
        float backLowZ = minZ - skirtDistance;
        float startBackX = Mathf.Lerp(minX, maxX, start);
        float endBackX = Mathf.Lerp(minX, maxX, end);
        float backHighY0 = GetSurfaceNetEdgeY(cell, heightSamples, start, 0f);
        float backHighY1 = GetSurfaceNetEdgeY(cell, heightSamples, end, 0f);
        AddLayeredSurfaceNetWaterShoreFace(
            builder,
            new Vector3(startBackX, backHighY0, backHighZ),
            new Vector3(endBackX, backHighY1, backHighZ),
            new Vector3(startBackX, bedY, backLowZ),
            new Vector3(endBackX, bedY, backLowZ));
    }

    void AddLayeredSurfaceNetWaterShoreFace(
        TerrainChunkMeshBuilder builder,
        Vector3 high0,
        Vector3 high1,
        Vector3 low0,
        Vector3 low1)
    {
        int edgeSegments = Mathf.Clamp(
            Mathf.Max(surfaceNetTopSubdivisions, Mathf.CeilToInt(Vector3.Distance(high0, high1) / Mathf.Max(0.25f, CellSize * 0.35f))),
            1,
            Mathf.Max(1, surfaceNetMaxSideSubdivisions));

        float drop0 = high0.y - low0.y;
        float drop1 = high1.y - low1.y;
        float lipDrop0 = CalculateSurfaceNetCliffBevelDrop(drop0) * 0.55f;
        float lipDrop1 = CalculateSurfaceNetCliffBevelDrop(drop1) * 0.55f;
        Vector3 lip0 = Vector3.Lerp(high0, low0, 0.18f);
        Vector3 lip1 = Vector3.Lerp(high1, low1, 0.18f);
        lip0.y = Mathf.Max(low0.y + PaintedCellHeight * 0.12f, high0.y - lipDrop0);
        lip1.y = Mathf.Max(low1.y + PaintedCellHeight * 0.12f, high1.y - lipDrop1);

        Vector3 shoulder0 = Vector3.Lerp(high0, low0, 0.58f);
        Vector3 shoulder1 = Vector3.Lerp(high1, low1, 0.58f);
        shoulder0.y = Mathf.Lerp(high0.y, low0.y, 0.68f);
        shoulder1.y = Mathf.Lerp(high1.y, low1.y, 0.68f);

        Vector3 toe0 = Vector3.Lerp(high0, low0, 0.9f);
        Vector3 toe1 = Vector3.Lerp(high1, low1, 0.9f);
        toe0.y = Mathf.Lerp(high0.y, low0.y, 0.92f);
        toe1.y = Mathf.Lerp(high1.y, low1.y, 0.92f);

        AddSubdividedDirtCliffBlendStrip(builder, lip0, high0, high1, lip1, edgeSegments, 0.7f, 1f);
        AddSubdividedDirtCliffBlendStrip(builder, shoulder0, lip0, lip1, shoulder1, edgeSegments, 0.28f, 0.7f);
        AddSubdividedDirtCliffBlendStrip(builder, toe0, shoulder0, shoulder1, toe1, edgeSegments, 0.08f, 0.28f);
        AddSubdividedDirtCliffBlendStrip(builder, low0, toe0, toe1, low1, edgeSegments, 0f, 0.08f);
    }

    void AddSubdividedDirtCliffBlendStrip(
        TerrainChunkMeshBuilder builder,
        Vector3 low0,
        Vector3 high0,
        Vector3 high1,
        Vector3 low1,
        int subdivisions,
        float lowDirtWeight,
        float highDirtWeight)
    {
        int segmentCount = Mathf.Max(1, subdivisions);
        for (int i = 0; i < segmentCount; i++)
        {
            float t0 = i / (float)segmentCount;
            float t1 = (i + 1) / (float)segmentCount;
            AddDirtCliffBlendQuad(
                builder,
                Vector3.Lerp(low0, low1, t0),
                Vector3.Lerp(high0, high1, t0),
                Vector3.Lerp(high0, high1, t1),
                Vector3.Lerp(low0, low1, t1),
                lowDirtWeight,
                highDirtWeight);
        }
    }

    void AddSurfaceNetDiagonalCliffFace(
        TerrainChunkMeshBuilder builder,
        Dictionary<int, SurfaceNetCliffCornerPatch> cliffCornerPatches,
        Vector2Int cell,
        int cornerX,
        int cornerZ)
    {
        if (!TryGetSurfaceNetCliffCornerPatch(cliffCornerPatches, cell, cornerX, cornerZ, out SurfaceNetCliffCornerPatch patch))
            return;

        AddDoubleSidedDirtCliffBlendQuad(
            builder,
            patch.lowHorizontalEdge,
            patch.highHorizontalEdge,
            patch.highVerticalEdge,
            patch.lowVerticalEdge);
    }

    void AddSurfaceNetDiagonalShoreFace(
        TerrainChunkMeshBuilder builder,
        float[,] heightSamples,
        Dictionary<int, SurfaceNetCliffCornerPatch> cliffCornerPatches,
        Vector2Int cell,
        int cornerX,
        int cornerZ)
    {
        if (TryGetSurfaceNetCliffCornerPatch(cliffCornerPatches, cell, cornerX, cornerZ, out _) ||
            !ShouldCutSurfaceNetShorelineCorner(cell, cornerX, cornerZ))
            return;

        float cut = Mathf.Clamp(surfaceNetMarchingSquaresCornerCut, 0.1f, 0.49f);
        float h00 = CalculateSurfaceNetCellCornerHeight(cell, 0, 0, heightSamples);
        float h01 = CalculateSurfaceNetCellCornerHeight(cell, 0, 1, heightSamples);
        float h11 = CalculateSurfaceNetCellCornerHeight(cell, 1, 1, heightSamples);
        float h10 = CalculateSurfaceNetCellCornerHeight(cell, 1, 0, heightSamples);
        float horizontalU = cornerX;
        float horizontalV = cornerZ == 0 ? cut : 1f - cut;
        float verticalU = cornerX == 0 ? cut : 1f - cut;
        float verticalV = cornerZ;
        float lowHeight = surfaceNetUseSlopedWaterShores
            ? waterPlaneHeightCells + surfaceNetWaterShoreBedOffsetCells
            : waterPlaneHeightCells;
        Vector3 highHorizontalEdge = SurfaceNetInterpolatedLocalPosition(cell, horizontalU, horizontalV, h00, h01, h11, h10);
        Vector3 highVerticalEdge = SurfaceNetInterpolatedLocalPosition(cell, verticalU, verticalV, h00, h01, h11, h10);

        if (surfaceNetUseSlopedWaterShores)
        {
            float skirtCells = Mathf.Max(0.05f, surfaceNetWaterShoreSkirtCells);
            int horizontalSign = cornerX == 0 ? -1 : 1;
            int verticalSign = cornerZ == 0 ? -1 : 1;
            AddDoubleSidedDirtCliffBlendQuad(
                builder,
                SurfaceNetSampleLocalPosition(cell.x + cornerX + horizontalSign * skirtCells, lowHeight, cell.y + horizontalV),
                highHorizontalEdge,
                highVerticalEdge,
                SurfaceNetSampleLocalPosition(cell.x + verticalU, lowHeight, cell.y + cornerZ + verticalSign * skirtCells));
            return;
        }

        AddDoubleSidedDirtCliffBlendTriangle(
            builder,
            SurfaceNetSampleLocalPosition(cell.x + cornerX, lowHeight, cell.y + cornerZ),
            highHorizontalEdge,
            highVerticalEdge);
    }

    float GetSurfaceNetEdgeY(Vector2Int cell, float[,] heightSamples, float u, float v)
    {
        float h00 = CalculateSurfaceNetCellCornerHeight(cell, 0, 0, heightSamples);
        float h01 = CalculateSurfaceNetCellCornerHeight(cell, 0, 1, heightSamples);
        float h11 = CalculateSurfaceNetCellCornerHeight(cell, 1, 1, heightSamples);
        float h10 = CalculateSurfaceNetCellCornerHeight(cell, 1, 0, heightSamples);
        float lowZHeight = Mathf.Lerp(h00, h10, u);
        float highZHeight = Mathf.Lerp(h01, h11, u);
        return SurfaceNetHeightToLocalY(Mathf.Lerp(lowZHeight, highZHeight, v));
    }

    bool ShouldAddSurfaceNetStitchFace(float highY0, float highY1, float lowY0, float lowY1)
    {
        const float epsilon = 0.001f;
        float highAverage = (highY0 + highY1) * 0.5f;
        float lowAverage = (lowY0 + lowY1) * 0.5f;
        return highAverage > lowAverage + epsilon &&
            (Mathf.Abs(highY0 - lowY0) > epsilon || Mathf.Abs(highY1 - lowY1) > epsilon);
    }

    void AddSurfaceNetFaces(TerrainChunkMeshBuilder builder, int[,,] vertexIndexByVoxel, float[,] heightSamples, float[] yLevels)
    {
        int yStepCount = yLevels.Length - 1;

        for (int z = 0; z <= GridHeight; z++)
        {
            for (int y = 0; y <= yStepCount; y++)
            {
                for (int x = 0; x < GridWidth; x++)
                {
                    float valueA = GetSurfaceNetValue(heightSamples, yLevels, x, y, z);
                    float valueB = GetSurfaceNetValue(heightSamples, yLevels, x + 1, y, z);
                    if ((valueA > 0f) == (valueB > 0f))
                        continue;

                    TryAddSurfaceNetQuad(
                        builder,
                        vertexIndexByVoxel,
                        x, y - 1, z - 1,
                        x, y - 1, z,
                        x, y, z,
                        x, y, z - 1,
                        valueA > valueB ? Vector3.right : Vector3.left);
                }
            }
        }

        for (int z = 0; z <= GridHeight; z++)
        {
            for (int y = 0; y < yStepCount; y++)
            {
                for (int x = 0; x <= GridWidth; x++)
                {
                    float valueA = GetSurfaceNetValue(heightSamples, yLevels, x, y, z);
                    float valueB = GetSurfaceNetValue(heightSamples, yLevels, x, y + 1, z);
                    if ((valueA > 0f) == (valueB > 0f))
                        continue;

                    Vector3 normal = valueA > valueB ? Vector3.up : Vector3.down;
                    if (normal.y < 0f)
                        continue;

                    TryAddSurfaceNetQuad(
                        builder,
                        vertexIndexByVoxel,
                        x - 1, y, z - 1,
                        x, y, z - 1,
                        x, y, z,
                        x - 1, y, z,
                        normal);
                }
            }
        }

        for (int z = 0; z < GridHeight; z++)
        {
            for (int y = 0; y <= yStepCount; y++)
            {
                for (int x = 0; x <= GridWidth; x++)
                {
                    float valueA = GetSurfaceNetValue(heightSamples, yLevels, x, y, z);
                    float valueB = GetSurfaceNetValue(heightSamples, yLevels, x, y, z + 1);
                    if ((valueA > 0f) == (valueB > 0f))
                        continue;

                    TryAddSurfaceNetQuad(
                        builder,
                        vertexIndexByVoxel,
                        x - 1, y - 1, z,
                        x, y - 1, z,
                        x, y, z,
                        x - 1, y, z,
                        valueA > valueB ? Vector3.forward : Vector3.back);
                }
            }
        }
    }

    bool TryAddSurfaceNetQuad(
        TerrainChunkMeshBuilder builder,
        int[,,] vertexIndexByVoxel,
        int ax,
        int ay,
        int az,
        int bx,
        int by,
        int bz,
        int cx,
        int cy,
        int cz,
        int dx,
        int dy,
        int dz,
        Vector3 desiredNormal)
    {
        if (!TryGetSurfaceNetVertexIndex(vertexIndexByVoxel, ax, ay, az, out int a) ||
            !TryGetSurfaceNetVertexIndex(vertexIndexByVoxel, bx, by, bz, out int b) ||
            !TryGetSurfaceNetVertexIndex(vertexIndexByVoxel, cx, cy, cz, out int c) ||
            !TryGetSurfaceNetVertexIndex(vertexIndexByVoxel, dx, dy, dz, out int d))
            return false;

        Vector3 faceNormal = Vector3.Cross(builder.vertices[b] - builder.vertices[a], builder.vertices[c] - builder.vertices[a]);
        if (Vector3.Dot(faceNormal, desiredNormal) < 0f)
        {
            int temp = b;
            b = d;
            d = temp;
            faceNormal = Vector3.Cross(builder.vertices[b] - builder.vertices[a], builder.vertices[c] - builder.vertices[a]);
        }

        TerrainMeshMaterialSlot slot = GetSurfaceNetMaterialSlot(faceNormal, (builder.vertices[a] + builder.vertices[b] + builder.vertices[c] + builder.vertices[d]) * 0.25f);
        List<int> triangles = builder.triangles[(int)slot];
        triangles.Add(a);
        triangles.Add(b);
        triangles.Add(c);
        triangles.Add(a);
        triangles.Add(c);
        triangles.Add(d);
        return true;
    }

    bool TryGetSurfaceNetVertexIndex(int[,,] vertexIndexByVoxel, int x, int y, int z, out int index)
    {
        if (x < 0 || x >= GridWidth || y < 0 || y >= vertexIndexByVoxel.GetLength(1) || z < 0 || z >= GridHeight)
        {
            index = -1;
            return false;
        }

        index = vertexIndexByVoxel[x, y, z];
        return index >= 0;
    }

    TerrainMeshMaterialSlot GetSurfaceNetMaterialSlot(Vector3 faceNormal, Vector3 localCenter)
    {
        Vector3 normal = faceNormal.sqrMagnitude > 0.0001f ? faceNormal.normalized : Vector3.up;
        if (normal.y > 0.28f)
        {
            Vector2Int cell = LocalToClampedGrid(localCenter);
            int heightLevel = Mathf.Max(1, Mathf.RoundToInt((localCenter.y - GridPlaneYOffset) / PaintedCellHeight));
            return GetMaterialSlot(GetTopFaceType(heightLevel, GetCellSurface(cell)));
        }

        return Mathf.Abs(normal.y) < 0.2f
            ? TerrainMeshMaterialSlot.CliffSide
            : TerrainMeshMaterialSlot.FallbackSide;
    }

    float GetSurfaceNetValue(float[,] heightSamples, float[] yLevels, int x, int y, int z)
    {
        if (x < 0 || x > GridWidth || y < 0 || y >= yLevels.Length || z < 0 || z > GridHeight)
            return -1f;

        return heightSamples[x, z] - yLevels[y];
    }

    Vector3 SurfaceNetSampleLocalPosition(int sampleX, float heightLevel, int sampleZ)
    {
        return SurfaceNetSampleLocalPosition((float)sampleX, heightLevel, sampleZ);
    }

    Vector3 SurfaceNetSampleLocalPosition(float sampleX, float heightLevel, float sampleZ)
    {
        float localX = sampleX * CellSize - WorldSize.x * 0.5f;
        float localZ = sampleZ * CellSize - WorldSize.y * 0.5f;
        return new Vector3(localX, SurfaceNetHeightToLocalY(heightLevel), localZ);
    }

    float SurfaceNetHeightToLocalY(float heightLevel)
    {
        return GridPlaneYOffset + Mathf.Max(0f, heightLevel) * PaintedCellHeight;
    }

    Vector2Int LocalToClampedGrid(Vector3 localPosition)
    {
        int cellX = Mathf.FloorToInt((localPosition.x + WorldSize.x * 0.5f) / CellSize);
        int cellZ = Mathf.FloorToInt((localPosition.z + WorldSize.y * 0.5f) / CellSize);
        return new Vector2Int(
            Mathf.Clamp(cellX, 0, GridWidth - 1),
            Mathf.Clamp(cellZ, 0, GridHeight - 1));
    }
}
