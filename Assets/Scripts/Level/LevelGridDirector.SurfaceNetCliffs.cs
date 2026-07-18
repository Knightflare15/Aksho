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
    Dictionary<int, SurfaceNetCliffCornerPatch> BuildSurfaceNetCliffCornerPatches(float[,] heightSamples)
    {
        Dictionary<int, SurfaceNetCliffCornerPatch> patches = new Dictionary<int, SurfaceNetCliffCornerPatch>();
        if (!surfaceNetMarchingSquaresCliffCorners)
            return patches;

        for (int z = 0; z < GridHeight; z++)
        {
            for (int x = 0; x < GridWidth; x++)
            {
                Vector2Int cell = new Vector2Int(x, z);
                if (GetCellHeight(cell) <= 0)
                    continue;

                TryAddSurfaceNetCliffCornerPatch(patches, heightSamples, cell, 0, 0);
                TryAddSurfaceNetCliffCornerPatch(patches, heightSamples, cell, 0, 1);
                TryAddSurfaceNetCliffCornerPatch(patches, heightSamples, cell, 1, 1);
                TryAddSurfaceNetCliffCornerPatch(patches, heightSamples, cell, 1, 0);
            }
        }

        return patches;
    }

    void TryAddSurfaceNetCliffCornerPatch(
        Dictionary<int, SurfaceNetCliffCornerPatch> patches,
        float[,] heightSamples,
        Vector2Int cell,
        int cornerX,
        int cornerZ)
    {
        if (!TryCreateSurfaceNetCliffCornerPatch(heightSamples, cell, cornerX, cornerZ, out SurfaceNetCliffCornerPatch patch))
            return;

        patches[GetSurfaceNetCliffCornerPatchKey(cell, cornerX, cornerZ)] = patch;
    }

    bool TryCreateSurfaceNetCliffCornerPatch(
        float[,] heightSamples,
        Vector2Int cell,
        int cornerX,
        int cornerZ,
        out SurfaceNetCliffCornerPatch patch)
    {
        patch = default;

        int ownHeight = GetCellHeight(cell);
        if (ownHeight <= 0)
            return false;

        int horizontalSign = cornerX == 0 ? -1 : 1;
        int verticalSign = cornerZ == 0 ? -1 : 1;
        Vector2Int horizontalDirection = new Vector2Int(horizontalSign, 0);
        Vector2Int verticalDirection = new Vector2Int(0, verticalSign);
        Vector2Int horizontalCell = cell + horizontalDirection;
        Vector2Int verticalCell = cell + verticalDirection;
        Vector2Int diagonalCell = cell + horizontalDirection + verticalDirection;

        if (!TryGetSurfaceNetLandCliffDrop(cell, horizontalDirection, out int horizontalHeight) ||
            !TryGetSurfaceNetLandCliffDrop(cell, verticalDirection, out int verticalHeight))
            return false;

        int diagonalHeight = GetCellHeight(diagonalCell);
        if (diagonalHeight <= 0)
            return false;

        int lowerTolerance = Mathf.Max(0, surfaceNetCliffCornerLowerHeightToleranceLevels);
        int lowestLowerHeight = Mathf.Min(horizontalHeight, Mathf.Min(verticalHeight, diagonalHeight));
        int highestLowerHeight = Mathf.Max(horizontalHeight, Mathf.Max(verticalHeight, diagonalHeight));
        if (highestLowerHeight - lowestLowerHeight > lowerTolerance)
            return false;

        int lowAverage = Mathf.RoundToInt((horizontalHeight + verticalHeight + diagonalHeight) / 3f);
        if (ownHeight - lowAverage < surfaceNetCliffHeightDifferenceThreshold)
            return false;

        if (!IsSurfaceNetStablePlateauCell(cell, ownHeight, horizontalDirection, verticalDirection))
            return false;

        float cut = Mathf.Clamp(surfaceNetMarchingSquaresCornerCut, 0.1f, 0.49f);
        float h00 = CalculateSurfaceNetCellCornerHeight(cell, 0, 0, heightSamples);
        float h01 = CalculateSurfaceNetCellCornerHeight(cell, 0, 1, heightSamples);
        float h11 = CalculateSurfaceNetCellCornerHeight(cell, 1, 1, heightSamples);
        float h10 = CalculateSurfaceNetCellCornerHeight(cell, 1, 0, heightSamples);

        float horizontalU = cornerX;
        float horizontalV = cornerZ == 0 ? cut : 1f - cut;
        float verticalU = cornerX == 0 ? cut : 1f - cut;
        float verticalV = cornerZ;

        patch = new SurfaceNetCliffCornerPatch
        {
            cell = cell,
            cornerX = cornerX,
            cornerZ = cornerZ,
            highHorizontalEdge = SurfaceNetInterpolatedLocalPosition(cell, horizontalU, horizontalV, h00, h01, h11, h10),
            highVerticalEdge = SurfaceNetInterpolatedLocalPosition(cell, verticalU, verticalV, h00, h01, h11, h10),
            lowHorizontalEdge = GetLowerCliffCornerEdgePosition(
                cell,
                horizontalCell,
                heightSamples,
                cornerX,
                cornerZ,
                true,
                cut),
            lowVerticalEdge = GetLowerCliffCornerEdgePosition(
                cell,
                verticalCell,
                heightSamples,
                cornerX,
                cornerZ,
                false,
                cut),
            lowCorner = SurfaceNetSampleLocalPosition(
                cell.x + cornerX,
                GetSurfaceNetCornerNeighborHeight(diagonalCell),
                cell.y + cornerZ),
            lowerSlot = GetLowerCliffCornerMaterialSlot(horizontalCell, verticalCell, diagonalCell)
        };

        return true;
    }

    bool TryGetSurfaceNetLandCliffDrop(Vector2Int cell, Vector2Int direction, out int neighborHeight)
    {
        neighborHeight = 0;
        Vector2Int neighbor = cell + direction;
        if (!ContainsCell(neighbor.x, neighbor.y))
            return false;

        if (!IsSupportedSurfaceNetCliffEdge(cell, direction, out _, out neighborHeight, out int difference))
            return false;

        return neighborHeight > 0 && difference > 0;
    }

    bool IsSurfaceNetStablePlateauCell(
        Vector2Int cell,
        int expectedHeight,
        Vector2Int cliffHorizontalDirection,
        Vector2Int cliffVerticalDirection)
    {
        int tolerance = Mathf.Max(0, surfaceNetCliffPlateauToleranceLevels);
        return IsSurfaceNetHeightWithinTolerance(cell - cliffHorizontalDirection, expectedHeight, tolerance) &&
            IsSurfaceNetHeightWithinTolerance(cell - cliffVerticalDirection, expectedHeight, tolerance) &&
            IsSurfaceNetHeightWithinTolerance(
                cell - cliffHorizontalDirection - cliffVerticalDirection,
                expectedHeight,
                tolerance);
    }

    bool IsSurfaceNetHeightWithinTolerance(Vector2Int cell, int expectedHeight, int tolerance)
    {
        if (!ContainsCell(cell.x, cell.y))
            return true;

        int height = GetCellHeight(cell);
        if (height <= 0)
            return true;

        return Mathf.Abs(height - expectedHeight) <= tolerance;
    }

    bool TryGetSurfaceNetCliffCornerPatch(
        Dictionary<int, SurfaceNetCliffCornerPatch> patches,
        Vector2Int cell,
        int cornerX,
        int cornerZ,
        out SurfaceNetCliffCornerPatch patch)
    {
        return patches.TryGetValue(GetSurfaceNetCliffCornerPatchKey(cell, cornerX, cornerZ), out patch);
    }

    int GetSurfaceNetCliffCornerPatchKey(Vector2Int cell, int cornerX, int cornerZ)
    {
        int cornerIndex = (cornerZ != 0 ? 2 : 0) + (cornerX != 0 ? 1 : 0);
        return ((cell.y * GridWidth) + cell.x) * 4 + cornerIndex;
    }

    bool IsSurfaceNetCliffCorner(Vector2Int cell, int cornerX, int cornerZ)
    {
        if (!surfaceNetMarchingSquaresCliffCorners)
            return false;

        int ownHeight = GetCellHeight(cell);
        if (ownHeight <= 0)
            return false;

        Vector2Int horizontalDirection = new Vector2Int(cornerX == 0 ? -1 : 1, 0);
        Vector2Int verticalDirection = new Vector2Int(0, cornerZ == 0 ? -1 : 1);
        return TryGetSurfaceNetCliffDrop(cell, horizontalDirection, out _) &&
            TryGetSurfaceNetCliffDrop(cell, verticalDirection, out _);
    }

    float GetSurfaceNetCornerNeighborHeight(Vector2Int cell)
    {
        int height = GetCellHeight(cell);
        return height > 0 ? height : Mathf.Max(0f, waterPlaneHeightCells);
    }

    bool ShouldSeparateSurfaceNetCornerCell(Vector2Int ownerCell, Vector2Int sampleCell)
    {
        Vector2Int delta = sampleCell - ownerCell;
        if (Mathf.Abs(delta.x) + Mathf.Abs(delta.y) != 1)
            return false;

        return IsSupportedSurfaceNetCliffEdge(ownerCell, delta, out _, out _, out _);
    }

    bool IsSupportedSurfaceNetCliffEdge(Vector2Int cell, Vector2Int direction, out int height, out int neighborHeight, out int difference)
    {
        height = 0;
        neighborHeight = 0;
        difference = 0;

        Vector2Int neighbor = cell + direction;
        height = GetCellHeight(cell);
        if (height <= 0)
            return false;

        bool neighborExists = ContainsCell(neighbor.x, neighbor.y);
        neighborHeight = neighborExists ? GetCellHeight(neighbor) : 0;
        difference = height - neighborHeight;
        if (neighborHeight <= 0)
            return difference > 0;

        if (Mathf.Abs(difference) < surfaceNetCliffHeightDifferenceThreshold)
            return false;

        if (!surfaceNetRequireCliffPlateaus)
            return true;

        int tolerance = Mathf.Max(0, surfaceNetCliffPlateauToleranceLevels);
        if (difference > 0)
        {
            int highSupport = GetSupportedSurfaceNetHeight(cell - direction, height);
            int lowSupport = GetSupportedSurfaceNetHeight(neighbor + direction, neighborHeight);
            return Mathf.Abs(highSupport - height) <= tolerance &&
                Mathf.Abs(lowSupport - neighborHeight) <= tolerance;
        }

        int neighborHighSupport = GetSupportedSurfaceNetHeight(neighbor + direction, neighborHeight);
        int cellLowSupport = GetSupportedSurfaceNetHeight(cell - direction, height);
        return Mathf.Abs(neighborHighSupport - neighborHeight) <= tolerance &&
            Mathf.Abs(cellLowSupport - height) <= tolerance;
    }

    int GetSupportedSurfaceNetHeight(Vector2Int cell, int fallbackHeight)
    {
        if (!ContainsCell(cell.x, cell.y))
            return fallbackHeight;

        int height = GetCellHeight(cell);
        return height > 0 ? height : fallbackHeight;
    }

    void AddSurfaceNetCliffFaces(
        TerrainChunkMeshBuilder builder,
        float[,] heightSamples,
        Dictionary<int, SurfaceNetCliffCornerPatch> cliffCornerPatches)
    {
        for (int z = 0; z < GridHeight; z++)
        {
            for (int x = 0; x < GridWidth; x++)
            {
                Vector2Int cell = new Vector2Int(x, z);
                int height = GetCellHeight(cell);
                if (height <= 0)
                    continue;

                AddSurfaceNetCliffFace(builder, heightSamples, cliffCornerPatches, cell, new Vector2Int(1, 0));
                AddSurfaceNetCliffFace(builder, heightSamples, cliffCornerPatches, cell, new Vector2Int(-1, 0));
                AddSurfaceNetCliffFace(builder, heightSamples, cliffCornerPatches, cell, new Vector2Int(0, 1));
                AddSurfaceNetCliffFace(builder, heightSamples, cliffCornerPatches, cell, new Vector2Int(0, -1));
                AddSurfaceNetDiagonalCliffFace(builder, cliffCornerPatches, cell, 0, 0);
                AddSurfaceNetDiagonalCliffFace(builder, cliffCornerPatches, cell, 0, 1);
                AddSurfaceNetDiagonalCliffFace(builder, cliffCornerPatches, cell, 1, 1);
                AddSurfaceNetDiagonalCliffFace(builder, cliffCornerPatches, cell, 1, 0);
                AddSurfaceNetDiagonalShoreFace(builder, heightSamples, cliffCornerPatches, cell, 0, 0);
                AddSurfaceNetDiagonalShoreFace(builder, heightSamples, cliffCornerPatches, cell, 0, 1);
                AddSurfaceNetDiagonalShoreFace(builder, heightSamples, cliffCornerPatches, cell, 1, 1);
                AddSurfaceNetDiagonalShoreFace(builder, heightSamples, cliffCornerPatches, cell, 1, 0);
            }
        }
    }

    void AddSurfaceNetCliffFace(
        TerrainChunkMeshBuilder builder,
        float[,] heightSamples,
        Dictionary<int, SurfaceNetCliffCornerPatch> cliffCornerPatches,
        Vector2Int cell,
        Vector2Int direction)
    {
        int height = GetCellHeight(cell);
        if (height <= 0)
            return;

        Vector2Int neighbor = cell + direction;
        bool hasLandNeighbor = ContainsCell(neighbor.x, neighbor.y) && GetCellHeight(neighbor) > 0;
        float minX = cell.x * CellSize - WorldSize.x * 0.5f;
        float maxX = minX + CellSize;
        float minZ = cell.y * CellSize - WorldSize.y * 0.5f;
        float maxZ = minZ + CellSize;
        float waterY = SurfaceNetHeightToLocalY(waterPlaneHeightCells);
        float cut = Mathf.Clamp(surfaceNetMarchingSquaresCornerCut, 0.1f, 0.49f);

        if (direction.x > 0)
        {
            float edgeX = maxX;
            float startV = ShouldTrimSurfaceNetSideCorner(cliffCornerPatches, cell, 1, 0) ? cut : 0f;
            float endV = ShouldTrimSurfaceNetSideCorner(cliffCornerPatches, cell, 1, 1) ? 1f - cut : 1f;
            if (startV >= endV)
                return;

            if (!hasLandNeighbor && surfaceNetUseSlopedWaterShores)
            {
                AddSurfaceNetWaterShoreFace(builder, heightSamples, cell, direction, minX, maxX, minZ, maxZ, startV, endV);
                return;
            }

            float highY0 = GetSurfaceNetEdgeY(cell, heightSamples, 1f, startV);
            float highY1 = GetSurfaceNetEdgeY(cell, heightSamples, 1f, endV);
            float lowY0 = hasLandNeighbor ? GetSurfaceNetEdgeY(neighbor, heightSamples, 0f, startV) : waterY;
            float lowY1 = hasLandNeighbor ? GetSurfaceNetEdgeY(neighbor, heightSamples, 0f, endV) : waterY;
            if (!ShouldAddSurfaceNetStitchFace(highY0, highY1, lowY0, lowY1))
                return;

            float startZ = Mathf.Lerp(minZ, maxZ, startV);
            float endZ = Mathf.Lerp(minZ, maxZ, endV);
            AddSurfaceNetSideStrip(
                builder,
                TerrainMeshMaterialSlot.CliffSide,
                new Vector3(edgeX, lowY0, startZ),
                new Vector3(edgeX, highY0, startZ),
                new Vector3(edgeX, highY1, endZ),
                new Vector3(edgeX, lowY1, endZ),
                CalculateSurfaceNetSideSubdivisions(highY0, highY1, lowY0, lowY1));
            return;
        }

        if (direction.x < 0)
        {
            float edgeX = minX;
            float startV = ShouldTrimSurfaceNetSideCorner(cliffCornerPatches, cell, 0, 1) ? 1f - cut : 1f;
            float endV = ShouldTrimSurfaceNetSideCorner(cliffCornerPatches, cell, 0, 0) ? cut : 0f;
            if (endV >= startV)
                return;

            if (!hasLandNeighbor && surfaceNetUseSlopedWaterShores)
            {
                AddSurfaceNetWaterShoreFace(builder, heightSamples, cell, direction, minX, maxX, minZ, maxZ, startV, endV);
                return;
            }

            float highY0 = GetSurfaceNetEdgeY(cell, heightSamples, 0f, startV);
            float highY1 = GetSurfaceNetEdgeY(cell, heightSamples, 0f, endV);
            float lowY0 = hasLandNeighbor ? GetSurfaceNetEdgeY(neighbor, heightSamples, 1f, startV) : waterY;
            float lowY1 = hasLandNeighbor ? GetSurfaceNetEdgeY(neighbor, heightSamples, 1f, endV) : waterY;
            if (!ShouldAddSurfaceNetStitchFace(highY0, highY1, lowY0, lowY1))
                return;

            float startZ = Mathf.Lerp(minZ, maxZ, startV);
            float endZ = Mathf.Lerp(minZ, maxZ, endV);
            AddSurfaceNetSideStrip(
                builder,
                TerrainMeshMaterialSlot.CliffSide,
                new Vector3(edgeX, lowY0, startZ),
                new Vector3(edgeX, highY0, startZ),
                new Vector3(edgeX, highY1, endZ),
                new Vector3(edgeX, lowY1, endZ),
                CalculateSurfaceNetSideSubdivisions(highY0, highY1, lowY0, lowY1));
            return;
        }

        if (direction.y > 0)
        {
            float edgeZ = maxZ;
            float startU = ShouldTrimSurfaceNetSideCorner(cliffCornerPatches, cell, 1, 1) ? 1f - cut : 1f;
            float endU = ShouldTrimSurfaceNetSideCorner(cliffCornerPatches, cell, 0, 1) ? cut : 0f;
            if (endU >= startU)
                return;

            if (!hasLandNeighbor && surfaceNetUseSlopedWaterShores)
            {
                AddSurfaceNetWaterShoreFace(builder, heightSamples, cell, direction, minX, maxX, minZ, maxZ, startU, endU);
                return;
            }

            float highY0 = GetSurfaceNetEdgeY(cell, heightSamples, startU, 1f);
            float highY1 = GetSurfaceNetEdgeY(cell, heightSamples, endU, 1f);
            float lowY0 = hasLandNeighbor ? GetSurfaceNetEdgeY(neighbor, heightSamples, startU, 0f) : waterY;
            float lowY1 = hasLandNeighbor ? GetSurfaceNetEdgeY(neighbor, heightSamples, endU, 0f) : waterY;
            if (!ShouldAddSurfaceNetStitchFace(highY0, highY1, lowY0, lowY1))
                return;

            float startX = Mathf.Lerp(minX, maxX, startU);
            float endX = Mathf.Lerp(minX, maxX, endU);
            AddSurfaceNetSideStrip(
                builder,
                TerrainMeshMaterialSlot.CliffSide,
                new Vector3(startX, lowY0, edgeZ),
                new Vector3(startX, highY0, edgeZ),
                new Vector3(endX, highY1, edgeZ),
                new Vector3(endX, lowY1, edgeZ),
                CalculateSurfaceNetSideSubdivisions(highY0, highY1, lowY0, lowY1));
            return;
        }

        float backEdgeZ = minZ;
        float backStartU = ShouldTrimSurfaceNetSideCorner(cliffCornerPatches, cell, 0, 0) ? cut : 0f;
        float backEndU = ShouldTrimSurfaceNetSideCorner(cliffCornerPatches, cell, 1, 0) ? 1f - cut : 1f;
        if (backStartU >= backEndU)
            return;

        if (!hasLandNeighbor && surfaceNetUseSlopedWaterShores)
        {
            AddSurfaceNetWaterShoreFace(builder, heightSamples, cell, direction, minX, maxX, minZ, maxZ, backStartU, backEndU);
            return;
        }

        float backHighY0 = GetSurfaceNetEdgeY(cell, heightSamples, backStartU, 0f);
        float backHighY1 = GetSurfaceNetEdgeY(cell, heightSamples, backEndU, 0f);
        float backLowY0 = hasLandNeighbor ? GetSurfaceNetEdgeY(neighbor, heightSamples, backStartU, 1f) : waterY;
        float backLowY1 = hasLandNeighbor ? GetSurfaceNetEdgeY(neighbor, heightSamples, backEndU, 1f) : waterY;
        if (!ShouldAddSurfaceNetStitchFace(backHighY0, backHighY1, backLowY0, backLowY1))
            return;

        float backStartX = Mathf.Lerp(minX, maxX, backStartU);
        float backEndX = Mathf.Lerp(minX, maxX, backEndU);
        AddSurfaceNetSideStrip(
            builder,
            TerrainMeshMaterialSlot.CliffSide,
            new Vector3(backStartX, backLowY0, backEdgeZ),
            new Vector3(backStartX, backHighY0, backEdgeZ),
            new Vector3(backEndX, backHighY1, backEdgeZ),
            new Vector3(backEndX, backLowY1, backEdgeZ),
            CalculateSurfaceNetSideSubdivisions(backHighY0, backHighY1, backLowY0, backLowY1));
    }

    bool ShouldTrimSurfaceNetSideCorner(
        Dictionary<int, SurfaceNetCliffCornerPatch> cliffCornerPatches,
        Vector2Int cell,
        int cornerX,
        int cornerZ)
    {
        return TryGetSurfaceNetCliffCornerPatch(cliffCornerPatches, cell, cornerX, cornerZ, out _) ||
            ShouldCutSurfaceNetShorelineCorner(cell, cornerX, cornerZ);
    }

    int CalculateSurfaceNetSideSubdivisions(float highY0, float highY1, float lowY0, float lowY1)
    {
        float maxDrop = Mathf.Max(Mathf.Abs(highY0 - lowY0), Mathf.Abs(highY1 - lowY1));
        float cellHeight = Mathf.Max(0.01f, PaintedCellHeight);
        int dropLevels = Mathf.CeilToInt(maxDrop / cellHeight);
        int dynamicSegments = Mathf.Max(surfaceNetTopSubdivisions, Mathf.CeilToInt(dropLevels / 2f));
        return Mathf.Clamp(dynamicSegments, 1, Mathf.Max(1, surfaceNetMaxSideSubdivisions));
    }

    void AddSurfaceNetSideStrip(
        TerrainChunkMeshBuilder builder,
        TerrainMeshMaterialSlot slot,
        Vector3 low0,
        Vector3 high0,
        Vector3 high1,
        Vector3 low1,
        int subdivisions)
    {
        int segmentCount = Mathf.Max(1, subdivisions);
        for (int i = 0; i < segmentCount; i++)
        {
            float t0 = i / (float)segmentCount;
            float t1 = (i + 1) / (float)segmentCount;
            Vector3 v0 = Vector3.Lerp(low0, low1, t0);
            Vector3 v1 = Vector3.Lerp(high0, high1, t0);
            Vector3 v2 = Vector3.Lerp(high0, high1, t1);
            Vector3 v3 = Vector3.Lerp(low0, low1, t1);
            if (slot == TerrainMeshMaterialSlot.CliffSide)
            {
                if (!TryAddBeveledCliffSegment(builder, v0, v1, v2, v3))
                    AddDirtCliffBlendQuad(builder, v0, v1, v2, v3);
            }
            else
            {
                AddQuad(builder, slot, v0, v1, v2, v3);
            }
        }
    }

    bool TryAddBeveledCliffSegment(
        TerrainChunkMeshBuilder builder,
        Vector3 low0,
        Vector3 high0,
        Vector3 high1,
        Vector3 low1)
    {
        float bevelWidth = Mathf.Clamp(surfaceNetCliffBevelWidthCells, 0f, 0.45f) * CellSize;
        if (bevelWidth <= 0.0001f)
            return false;

        float drop0 = high0.y - low0.y;
        float drop1 = high1.y - low1.y;
        float maxDrop = Mathf.Max(drop0, drop1);
        if (maxDrop <= PaintedCellHeight * 0.35f)
            return false;

        Vector3 faceNormal = Vector3.Cross(high0 - low0, high1 - low0);
        faceNormal.y = 0f;
        if (faceNormal.sqrMagnitude <= 0.0001f)
            return false;

        faceNormal.Normalize();
        float bevelDrop0 = CalculateSurfaceNetCliffBevelDrop(drop0);
        float bevelDrop1 = CalculateSurfaceNetCliffBevelDrop(drop1);
        if (bevelDrop0 <= 0f && bevelDrop1 <= 0f)
            return false;

        Vector3 bevelLow0 = high0 + faceNormal * bevelWidth;
        Vector3 bevelLow1 = high1 + faceNormal * bevelWidth;
        bevelLow0.y = Mathf.Max(low0.y + PaintedCellHeight * 0.08f, high0.y - bevelDrop0);
        bevelLow1.y = Mathf.Max(low1.y + PaintedCellHeight * 0.08f, high1.y - bevelDrop1);

        Vector3 sideLow0 = low0 + faceNormal * bevelWidth;
        Vector3 sideLow1 = low1 + faceNormal * bevelWidth;
        AddDirtCliffBlendQuad(builder, bevelLow0, high0, high1, bevelLow1, 0.68f, 1f);
        AddDirtCliffBlendQuad(builder, sideLow0, bevelLow0, bevelLow1, sideLow1, 0f, 0.68f);
        return true;
    }

    float CalculateSurfaceNetCliffBevelDrop(float drop)
    {
        if (drop <= PaintedCellHeight * 0.35f)
            return 0f;

        float targetDrop = Mathf.Clamp(
            drop * 0.22f,
            PaintedCellHeight * 0.4f,
            PaintedCellHeight * 1.75f);
        return Mathf.Min(targetDrop, Mathf.Max(0f, drop - PaintedCellHeight * 0.12f));
    }
}
