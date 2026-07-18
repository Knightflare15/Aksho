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
    bool TryAddMarchingSquaresTopFace(
        TerrainChunkMeshBuilder builder,
        TerrainMeshMaterialSlot currentSlot,
        Vector2Int cell,
        Dictionary<int, SurfaceNetCliffCornerPatch> cliffCornerPatches,
        float h00,
        float h01,
        float h11,
        float h10)
    {
        TerrainMeshMaterialSlot? bottomLeftSlot = GetMarchingSquaresCornerSlot(cell, currentSlot, -1, -1);
        TerrainMeshMaterialSlot? topLeftSlot = GetMarchingSquaresCornerSlot(cell, currentSlot, -1, 1);
        TerrainMeshMaterialSlot? topRightSlot = GetMarchingSquaresCornerSlot(cell, currentSlot, 1, 1);
        TerrainMeshMaterialSlot? bottomRightSlot = GetMarchingSquaresCornerSlot(cell, currentSlot, 1, -1);
        bool cutBottomLeftCliff = TryGetSurfaceNetCliffCornerPatch(cliffCornerPatches, cell, 0, 0, out SurfaceNetCliffCornerPatch bottomLeftPatch);
        bool cutTopLeftCliff = TryGetSurfaceNetCliffCornerPatch(cliffCornerPatches, cell, 0, 1, out SurfaceNetCliffCornerPatch topLeftPatch);
        bool cutTopRightCliff = TryGetSurfaceNetCliffCornerPatch(cliffCornerPatches, cell, 1, 1, out SurfaceNetCliffCornerPatch topRightPatch);
        bool cutBottomRightCliff = TryGetSurfaceNetCliffCornerPatch(cliffCornerPatches, cell, 1, 0, out SurfaceNetCliffCornerPatch bottomRightPatch);
        bool cutBottomLeftShore = ShouldCutSurfaceNetShorelineCorner(cell, 0, 0);
        bool cutTopLeftShore = ShouldCutSurfaceNetShorelineCorner(cell, 0, 1);
        bool cutTopRightShore = ShouldCutSurfaceNetShorelineCorner(cell, 1, 1);
        bool cutBottomRightShore = ShouldCutSurfaceNetShorelineCorner(cell, 1, 0);
        bool cutBottomLeft = bottomLeftSlot.HasValue || cutBottomLeftCliff || cutBottomLeftShore;
        bool cutTopLeft = topLeftSlot.HasValue || cutTopLeftCliff || cutTopLeftShore;
        bool cutTopRight = topRightSlot.HasValue || cutTopRightCliff || cutTopRightShore;
        bool cutBottomRight = bottomRightSlot.HasValue || cutBottomRightCliff || cutBottomRightShore;
        if (!cutBottomLeft && !cutTopLeft && !cutTopRight && !cutBottomRight)
            return false;

        float cut = Mathf.Clamp(surfaceNetMarchingSquaresCornerCut, 0.1f, 0.49f);
        if (bottomLeftSlot.HasValue && !cutBottomLeftCliff)
        {
            AddTriangle(
                builder,
                bottomLeftSlot.Value,
                SurfaceNetInterpolatedLocalPosition(cell, 0f, 0f, h00, h01, h11, h10),
                SurfaceNetInterpolatedLocalPosition(cell, 0f, cut, h00, h01, h11, h10),
                SurfaceNetInterpolatedLocalPosition(cell, cut, 0f, h00, h01, h11, h10));
        }
        else if (cutBottomLeftCliff)
        {
            AddSurfaceNetLowerCliffCornerFill(builder, bottomLeftPatch);
        }

        if (topLeftSlot.HasValue && !cutTopLeftCliff)
        {
            AddTriangle(
                builder,
                topLeftSlot.Value,
                SurfaceNetInterpolatedLocalPosition(cell, 0f, 1f - cut, h00, h01, h11, h10),
                SurfaceNetInterpolatedLocalPosition(cell, 0f, 1f, h00, h01, h11, h10),
                SurfaceNetInterpolatedLocalPosition(cell, cut, 1f, h00, h01, h11, h10));
        }
        else if (cutTopLeftCliff)
        {
            AddSurfaceNetLowerCliffCornerFill(builder, topLeftPatch);
        }

        if (topRightSlot.HasValue && !cutTopRightCliff)
        {
            AddTriangle(
                builder,
                topRightSlot.Value,
                SurfaceNetInterpolatedLocalPosition(cell, 1f - cut, 1f, h00, h01, h11, h10),
                SurfaceNetInterpolatedLocalPosition(cell, 1f, 1f, h00, h01, h11, h10),
                SurfaceNetInterpolatedLocalPosition(cell, 1f, 1f - cut, h00, h01, h11, h10));
        }
        else if (cutTopRightCliff)
        {
            AddSurfaceNetLowerCliffCornerFill(builder, topRightPatch);
        }

        if (bottomRightSlot.HasValue && !cutBottomRightCliff)
        {
            AddTriangle(
                builder,
                bottomRightSlot.Value,
                SurfaceNetInterpolatedLocalPosition(cell, 1f, cut, h00, h01, h11, h10),
                SurfaceNetInterpolatedLocalPosition(cell, 1f, 0f, h00, h01, h11, h10),
                SurfaceNetInterpolatedLocalPosition(cell, 1f - cut, 0f, h00, h01, h11, h10));
        }
        else if (cutBottomRightCliff)
        {
            AddSurfaceNetLowerCliffCornerFill(builder, bottomRightPatch);
        }

        List<Vector2> polygon = BuildMarchingSquaresTopPolygon(
            cutBottomLeft,
            cutTopLeft,
            cutTopRight,
            cutBottomRight,
            cut);
        Vector2 center = CalculatePolygonCenter(polygon);
        Vector3 centerPosition = SurfaceNetInterpolatedLocalPosition(cell, center.x, center.y, h00, h01, h11, h10);
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % polygon.Count];
            AddTriangle(
                builder,
                currentSlot,
                centerPosition,
                SurfaceNetInterpolatedLocalPosition(cell, a.x, a.y, h00, h01, h11, h10),
                SurfaceNetInterpolatedLocalPosition(cell, b.x, b.y, h00, h01, h11, h10));
        }

        return true;
    }

    bool TryAddMarchingSquaresSurfaceBlendTopFace(
        TerrainChunkMeshBuilder builder,
        TerrainMeshMaterialSlot currentSlot,
        Vector2Int cell,
        Dictionary<int, SurfaceNetCliffCornerPatch> cliffCornerPatches,
        float h00,
        float h01,
        float h11,
        float h10)
    {
        if (currentSlot != TerrainMeshMaterialSlot.GrassTop && currentSlot != TerrainMeshMaterialSlot.DirtTop)
            return false;

        if (ShouldTrimSurfaceNetSideCorner(cliffCornerPatches, cell, 0, 0) ||
            ShouldTrimSurfaceNetSideCorner(cliffCornerPatches, cell, 0, 1) ||
            ShouldTrimSurfaceNetSideCorner(cliffCornerPatches, cell, 1, 1) ||
            ShouldTrimSurfaceNetSideCorner(cliffCornerPatches, cell, 1, 0))
            return false;

        TerrainMeshMaterialSlot bottomLeftSlot = ResolveMarchingSquaresSurfaceCornerSlot(cell, currentSlot, -1, -1);
        TerrainMeshMaterialSlot topLeftSlot = ResolveMarchingSquaresSurfaceCornerSlot(cell, currentSlot, -1, 1);
        TerrainMeshMaterialSlot topRightSlot = ResolveMarchingSquaresSurfaceCornerSlot(cell, currentSlot, 1, 1);
        TerrainMeshMaterialSlot bottomRightSlot = ResolveMarchingSquaresSurfaceCornerSlot(cell, currentSlot, 1, -1);

        if (!IsGrassOrDirtTopSlot(bottomLeftSlot) ||
            !IsGrassOrDirtTopSlot(topLeftSlot) ||
            !IsGrassOrDirtTopSlot(topRightSlot) ||
            !IsGrassOrDirtTopSlot(bottomRightSlot))
            return false;

        bool bottomLeftDirt = bottomLeftSlot == TerrainMeshMaterialSlot.DirtTop;
        bool topLeftDirt = topLeftSlot == TerrainMeshMaterialSlot.DirtTop;
        bool topRightDirt = topRightSlot == TerrainMeshMaterialSlot.DirtTop;
        bool bottomRightDirt = bottomRightSlot == TerrainMeshMaterialSlot.DirtTop;
        int dirtCase = GetMarchingSquaresCase(bottomLeftDirt, topLeftDirt, topRightDirt, bottomRightDirt);
        if (dirtCase == 0 || dirtCase == 15)
            return false;

        AddMarchingSquaresMaterialCase(
            builder,
            TerrainMeshMaterialSlot.DirtTop,
            cell,
            dirtCase,
            h00,
            h01,
            h11,
            h10);
        AddMarchingSquaresMaterialCase(
            builder,
            TerrainMeshMaterialSlot.GrassTop,
            cell,
            dirtCase ^ 15,
            h00,
            h01,
            h11,
            h10);
        return true;
    }

    TerrainMeshMaterialSlot ResolveMarchingSquaresSurfaceCornerSlot(
        Vector2Int cell,
        TerrainMeshMaterialSlot currentSlot,
        int horizontalDirection,
        int verticalDirection)
    {
        TerrainMeshMaterialSlot horizontalSlot = currentSlot;
        TerrainMeshMaterialSlot verticalSlot = currentSlot;
        TerrainMeshMaterialSlot diagonalSlot = currentSlot;

        if (TryGetTopMaterialSlot(cell + new Vector2Int(horizontalDirection, 0), out TerrainMeshMaterialSlot resolvedHorizontalSlot))
            horizontalSlot = resolvedHorizontalSlot;

        if (TryGetTopMaterialSlot(cell + new Vector2Int(0, verticalDirection), out TerrainMeshMaterialSlot resolvedVerticalSlot))
            verticalSlot = resolvedVerticalSlot;

        if (TryGetTopMaterialSlot(cell + new Vector2Int(horizontalDirection, verticalDirection), out TerrainMeshMaterialSlot resolvedDiagonalSlot))
            diagonalSlot = resolvedDiagonalSlot;

        if (verticalSlot == diagonalSlot && verticalSlot != currentSlot)
            return verticalSlot;

        if (horizontalSlot == diagonalSlot && horizontalSlot != currentSlot)
            return horizontalSlot;

        if (horizontalSlot == verticalSlot && horizontalSlot != currentSlot)
            return horizontalSlot;

        return currentSlot;
    }

    static bool IsGrassOrDirtTopSlot(TerrainMeshMaterialSlot slot)
    {
        return slot == TerrainMeshMaterialSlot.GrassTop || slot == TerrainMeshMaterialSlot.DirtTop;
    }

    static int GetMarchingSquaresCase(bool bottomLeft, bool topLeft, bool topRight, bool bottomRight)
    {
        int value = 0;
        if (bottomLeft)
            value |= 1;
        if (topLeft)
            value |= 2;
        if (topRight)
            value |= 4;
        if (bottomRight)
            value |= 8;
        return value;
    }

    void AddMarchingSquaresMaterialCase(
        TerrainChunkMeshBuilder builder,
        TerrainMeshMaterialSlot slot,
        Vector2Int cell,
        int marchingCase,
        float h00,
        float h01,
        float h11,
        float h10)
    {
        switch (marchingCase)
        {
            case 0:
                return;
            case 1:
                AddMarchingSquaresMaterialPolygon(builder, slot, cell, h00, h01, h11, h10, new Vector2(0f, 0f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0f));
                return;
            case 2:
                AddMarchingSquaresMaterialPolygon(builder, slot, cell, h00, h01, h11, h10, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 0.5f));
                return;
            case 3:
                AddMarchingSquaresMaterialPolygon(builder, slot, cell, h00, h01, h11, h10, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0f));
                return;
            case 4:
                AddMarchingSquaresMaterialPolygon(builder, slot, cell, h00, h01, h11, h10, new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(0.5f, 1f));
                return;
            case 5:
                AddMarchingSquaresMaterialPolygon(builder, slot, cell, h00, h01, h11, h10, new Vector2(0f, 0f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0f));
                AddMarchingSquaresMaterialPolygon(builder, slot, cell, h00, h01, h11, h10, new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(0.5f, 1f));
                return;
            case 6:
                AddMarchingSquaresMaterialPolygon(builder, slot, cell, h00, h01, h11, h10, new Vector2(0f, 0.5f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0.5f));
                return;
            case 7:
                AddMarchingSquaresMaterialPolygon(builder, slot, cell, h00, h01, h11, h10, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0f));
                return;
            case 8:
                AddMarchingSquaresMaterialPolygon(builder, slot, cell, h00, h01, h11, h10, new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(1f, 0.5f));
                return;
            case 9:
                AddMarchingSquaresMaterialPolygon(builder, slot, cell, h00, h01, h11, h10, new Vector2(0f, 0f), new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0f));
                return;
            case 10:
                AddMarchingSquaresMaterialPolygon(builder, slot, cell, h00, h01, h11, h10, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 0.5f));
                AddMarchingSquaresMaterialPolygon(builder, slot, cell, h00, h01, h11, h10, new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(1f, 0.5f));
                return;
            case 11:
                AddMarchingSquaresMaterialPolygon(builder, slot, cell, h00, h01, h11, h10, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(1f, 0.5f), new Vector2(1f, 0f));
                return;
            case 12:
                AddMarchingSquaresMaterialPolygon(builder, slot, cell, h00, h01, h11, h10, new Vector2(0.5f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));
                return;
            case 13:
                AddMarchingSquaresMaterialPolygon(builder, slot, cell, h00, h01, h11, h10, new Vector2(0f, 0f), new Vector2(0f, 0.5f), new Vector2(0.5f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f));
                return;
            case 14:
                AddMarchingSquaresMaterialPolygon(builder, slot, cell, h00, h01, h11, h10, new Vector2(0f, 0.5f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));
                return;
            case 15:
                AddMarchingSquaresMaterialPolygon(builder, slot, cell, h00, h01, h11, h10, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f));
                return;
        }
    }

    void AddMarchingSquaresMaterialPolygon(
        TerrainChunkMeshBuilder builder,
        TerrainMeshMaterialSlot slot,
        Vector2Int cell,
        float h00,
        float h01,
        float h11,
        float h10,
        params Vector2[] points)
    {
        if (points == null || points.Length < 3)
            return;

        Vector2 center = Vector2.zero;
        for (int i = 0; i < points.Length; i++)
            center += points[i];
        center /= points.Length;

        Vector3 centerPosition = SurfaceNetInterpolatedLocalPosition(cell, center.x, center.y, h00, h01, h11, h10);
        for (int i = 0; i < points.Length; i++)
        {
            Vector2 a = points[i];
            Vector2 b = points[(i + 1) % points.Length];
            AddTriangle(
                builder,
                slot,
                centerPosition,
                SurfaceNetInterpolatedLocalPosition(cell, a.x, a.y, h00, h01, h11, h10),
                SurfaceNetInterpolatedLocalPosition(cell, b.x, b.y, h00, h01, h11, h10));
        }
    }

    TerrainMeshMaterialSlot? GetMarchingSquaresCornerSlot(
        Vector2Int cell,
        TerrainMeshMaterialSlot currentSlot,
        int horizontalDirection,
        int verticalDirection)
    {
        Vector2Int horizontalCell = cell + new Vector2Int(horizontalDirection, 0);
        Vector2Int verticalCell = cell + new Vector2Int(0, verticalDirection);
        Vector2Int diagonalCell = cell + new Vector2Int(horizontalDirection, verticalDirection);
        if (!TryGetTopMaterialSlot(horizontalCell, out TerrainMeshMaterialSlot horizontalSlot) ||
            !TryGetTopMaterialSlot(verticalCell, out TerrainMeshMaterialSlot verticalSlot) ||
            !TryGetTopMaterialSlot(diagonalCell, out TerrainMeshMaterialSlot diagonalSlot))
            return null;

        if (surfaceBlendEnabled &&
            IsGrassOrDirtTopSlot(currentSlot) &&
            IsGrassOrDirtTopSlot(horizontalSlot) &&
            IsGrassOrDirtTopSlot(verticalSlot) &&
            IsGrassOrDirtTopSlot(diagonalSlot))
            return null;

        if (horizontalSlot != verticalSlot || horizontalSlot != diagonalSlot || horizontalSlot == currentSlot)
            return null;

        return horizontalSlot;
    }

    bool ShouldCutSurfaceNetShorelineCorner(Vector2Int cell, int cornerX, int cornerZ)
    {
        if (!surfaceNetMarchingSquaresTopCorners || GetCellHeight(cell) <= 0)
            return false;

        Vector2Int horizontalDirection = new Vector2Int(cornerX == 0 ? -1 : 1, 0);
        Vector2Int verticalDirection = new Vector2Int(0, cornerZ == 0 ? -1 : 1);
        bool horizontalLand = IsSurfaceNetLandCell(cell + horizontalDirection);
        bool verticalLand = IsSurfaceNetLandCell(cell + verticalDirection);
        bool diagonalLand = IsSurfaceNetLandCell(cell + horizontalDirection + verticalDirection);

        return !diagonalLand && (!horizontalLand || !verticalLand);
    }

    bool IsSurfaceNetLandCell(Vector2Int cell)
    {
        return ContainsCell(cell.x, cell.y) && GetCellHeight(cell) > 0;
    }

    void AddSurfaceNetLowerCliffCornerFill(TerrainChunkMeshBuilder builder, SurfaceNetCliffCornerPatch patch)
    {
        if (patch.cornerX == 0 && patch.cornerZ == 0)
        {
            AddDoubleSidedTriangle(builder, patch.lowerSlot, patch.lowCorner, patch.lowHorizontalEdge, patch.lowVerticalEdge);
            return;
        }

        if (patch.cornerX == 0 && patch.cornerZ == 1)
        {
            AddDoubleSidedTriangle(builder, patch.lowerSlot, patch.lowHorizontalEdge, patch.lowCorner, patch.lowVerticalEdge);
            return;
        }

        if (patch.cornerX == 1 && patch.cornerZ == 1)
        {
            AddDoubleSidedTriangle(builder, patch.lowerSlot, patch.lowVerticalEdge, patch.lowCorner, patch.lowHorizontalEdge);
            return;
        }

        AddDoubleSidedTriangle(builder, patch.lowerSlot, patch.lowVerticalEdge, patch.lowHorizontalEdge, patch.lowCorner);
    }

    TerrainMeshMaterialSlot GetLowerCliffCornerMaterialSlot(Vector2Int horizontalCell, Vector2Int verticalCell, Vector2Int diagonalCell)
    {
        if (TryGetTopMaterialSlot(diagonalCell, out TerrainMeshMaterialSlot diagonalSlot))
            return diagonalSlot;

        if (TryGetTopMaterialSlot(horizontalCell, out TerrainMeshMaterialSlot horizontalSlot))
            return horizontalSlot;

        if (TryGetTopMaterialSlot(verticalCell, out TerrainMeshMaterialSlot verticalSlot))
            return verticalSlot;

        return TerrainMeshMaterialSlot.FallbackSide;
    }

    Vector3 GetLowerCliffCornerEdgePosition(
        Vector2Int highCell,
        Vector2Int lowerCell,
        float[,] heightSamples,
        int cornerX,
        int cornerZ,
        bool horizontalNeighbor,
        float cut)
    {
        float highU;
        float highV;
        float lowerU;
        float lowerV;
        if (horizontalNeighbor)
        {
            highU = cornerX;
            highV = cornerZ == 0 ? cut : 1f - cut;
            lowerU = cornerX == 0 ? 1f : 0f;
            lowerV = highV;
        }
        else
        {
            highU = cornerX == 0 ? cut : 1f - cut;
            highV = cornerZ;
            lowerU = highU;
            lowerV = cornerZ == 0 ? 1f : 0f;
        }

        Vector3 position = SurfaceNetSampleLocalPosition(
            highCell.x + highU,
            0f,
            highCell.y + highV);
        if (GetCellHeight(lowerCell) > 0)
            position.y = GetSurfaceNetEdgeY(lowerCell, heightSamples, lowerU, lowerV);
        else
            position.y = SurfaceNetHeightToLocalY(waterPlaneHeightCells);

        return position;
    }

    bool TryGetTopMaterialSlot(Vector2Int cell, out TerrainMeshMaterialSlot slot)
    {
        slot = TerrainMeshMaterialSlot.FallbackSide;
        int height = GetCellHeight(cell);
        if (height <= 0)
            return false;

        slot = GetMaterialSlot(GetTopFaceType(height, GetCellSurface(cell)));
        return true;
    }

    List<Vector2> BuildMarchingSquaresTopPolygon(
        bool cutBottomLeft,
        bool cutTopLeft,
        bool cutTopRight,
        bool cutBottomRight,
        float cut)
    {
        List<Vector2> points = new List<Vector2>(8);
        AddUniquePoint(points, cutBottomLeft ? new Vector2(0f, cut) : new Vector2(0f, 0f));
        AddUniquePoint(points, cutTopLeft ? new Vector2(0f, 1f - cut) : new Vector2(0f, 1f));
        AddUniquePoint(points, cutTopLeft ? new Vector2(cut, 1f) : new Vector2(0f, 1f));
        AddUniquePoint(points, cutTopRight ? new Vector2(1f - cut, 1f) : new Vector2(1f, 1f));
        AddUniquePoint(points, cutTopRight ? new Vector2(1f, 1f - cut) : new Vector2(1f, 1f));
        AddUniquePoint(points, cutBottomRight ? new Vector2(1f, cut) : new Vector2(1f, 0f));
        AddUniquePoint(points, cutBottomRight ? new Vector2(1f - cut, 0f) : new Vector2(1f, 0f));
        AddUniquePoint(points, cutBottomLeft ? new Vector2(cut, 0f) : new Vector2(0f, 0f));
        return points;
    }

    static void AddUniquePoint(List<Vector2> points, Vector2 point)
    {
        const float epsilon = 0.0001f;
        if (points.Count > 0 && Vector2.SqrMagnitude(points[points.Count - 1] - point) <= epsilon)
            return;

        if (points.Count > 1 && Vector2.SqrMagnitude(points[0] - point) <= epsilon)
            return;

        points.Add(point);
    }

    static Vector2 CalculatePolygonCenter(List<Vector2> polygon)
    {
        Vector2 total = Vector2.zero;
        for (int i = 0; i < polygon.Count; i++)
            total += polygon[i];

        return polygon.Count > 0 ? total / polygon.Count : new Vector2(0.5f, 0.5f);
    }

    Vector3 SurfaceNetInterpolatedLocalPosition(
        Vector2Int cell,
        float u,
        float v,
        float h00,
        float h01,
        float h11,
        float h10)
    {
        float lowZHeight = Mathf.Lerp(h00, h10, u);
        float highZHeight = Mathf.Lerp(h01, h11, u);
        float height = Mathf.Lerp(lowZHeight, highZHeight, v);
        return SurfaceNetSampleLocalPosition(cell.x + u, height, cell.y + v);
    }

    float CalculateSurfaceNetCellCornerHeight(Vector2Int cell, int cornerX, int cornerZ, float[,] fallbackSamples)
    {
        int gridX = cell.x + cornerX;
        int gridZ = cell.y + cornerZ;
        int ownHeight = GetCellHeight(cell);
        if (!surfaceNetPreserveCliffs || ownHeight <= 0)
            return fallbackSamples[gridX, gridZ];

        if (TryCalculateRelaxedSurfaceNetCliffCorner(cell, cornerX, cornerZ, ownHeight, out float relaxedCornerHeight))
            return relaxedCornerHeight;

        float total = 0f;
        int count = 0;
        for (int z = -1; z <= 0; z++)
        {
            for (int x = -1; x <= 0; x++)
            {
                Vector2Int sampleCell = new Vector2Int(gridX + x, gridZ + z);
                if (!ContainsCell(sampleCell.x, sampleCell.y))
                    continue;

                int sampleHeight = GetCellHeight(sampleCell);
                if (sampleHeight <= 0 || ShouldSeparateSurfaceNetCornerCell(cell, sampleCell))
                    continue;

                total += sampleHeight;
                count++;
            }
        }

        return count > 0 ? total / count : ownHeight;
    }

    bool TryCalculateRelaxedSurfaceNetCliffCorner(
        Vector2Int cell,
        int cornerX,
        int cornerZ,
        int ownHeight,
        out float relaxedHeight)
    {
        relaxedHeight = ownHeight;
        float strength = Mathf.Clamp01(surfaceNetCliffCornerRelaxStrength);
        if (strength <= 0f)
            return false;

        Vector2Int horizontalDirection = new Vector2Int(cornerX == 0 ? -1 : 1, 0);
        Vector2Int verticalDirection = new Vector2Int(0, cornerZ == 0 ? -1 : 1);
        if (!TryGetSurfaceNetCliffDrop(cell, horizontalDirection, out int horizontalNeighborHeight) ||
            !TryGetSurfaceNetCliffDrop(cell, verticalDirection, out int verticalNeighborHeight))
            return false;

        Vector2Int diagonalCell = cell + horizontalDirection + verticalDirection;
        float diagonalHeight = GetSurfaceNetCornerNeighborHeight(diagonalCell);
        float lowerAverage = (horizontalNeighborHeight + verticalNeighborHeight + diagonalHeight) / 3f;
        relaxedHeight = Mathf.Lerp(ownHeight, lowerAverage, strength);
        return true;
    }

    bool TryGetSurfaceNetCliffDrop(Vector2Int cell, Vector2Int direction, out int neighborHeight)
    {
        neighborHeight = 0;
        if (!IsSupportedSurfaceNetCliffEdge(cell, direction, out _, out neighborHeight, out int difference))
            return false;

        return difference > 0;
    }
}
