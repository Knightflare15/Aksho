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
    void AddTerrainCellFaces(TerrainChunkMeshBuilder builder, Vector2Int cell)
    {
        int heightLevel = GetCellHeight(cell);
        if (heightLevel <= 0)
            return;

        SurfaceBrush cellSurface = GetCellSurface(cell);
        float minX = cell.x * CellSize - WorldSize.x * 0.5f;
        float maxX = minX + CellSize;
        float minZ = cell.y * CellSize - WorldSize.y * 0.5f;
        float maxZ = minZ + CellSize;
        float topY = GridPlaneYOffset + heightLevel * PaintedCellHeight;

        TerrainMeshMaterialSlot topSlot = GetMaterialSlot(GetTopFaceType(heightLevel, cellSurface));
        Vector3 v0 = new Vector3(minX, topY, minZ);
        Vector3 v1 = new Vector3(minX, topY, maxZ);
        Vector3 v2 = new Vector3(maxX, topY, maxZ);
        Vector3 v3 = new Vector3(maxX, topY, minZ);
        if (surfaceBlendEnabled && IsGrassOrDirtTopSlot(topSlot))
        {
            AddQuad(
                builder,
                TerrainMeshMaterialSlot.GrassDirtBlendTop,
                v0,
                v1,
                v2,
                v3,
                CalculateSurfaceBlendDirtWeight(cell.x, cell.y),
                CalculateSurfaceBlendDirtWeight(cell.x, cell.y + 1f),
                CalculateSurfaceBlendDirtWeight(cell.x + 1f, cell.y + 1f),
                CalculateSurfaceBlendDirtWeight(cell.x + 1f, cell.y));
        }
        else
        {
            AddQuad(builder, topSlot, v0, v1, v2, v3);
        }

        AddTerrainSideFace(builder, cell, heightLevel, cellSurface, new Vector2Int(1, 0), minX, maxX, minZ, maxZ);
        AddTerrainSideFace(builder, cell, heightLevel, cellSurface, new Vector2Int(-1, 0), minX, maxX, minZ, maxZ);
        AddTerrainSideFace(builder, cell, heightLevel, cellSurface, new Vector2Int(0, 1), minX, maxX, minZ, maxZ);
        AddTerrainSideFace(builder, cell, heightLevel, cellSurface, new Vector2Int(0, -1), minX, maxX, minZ, maxZ);
    }

    void AddTerrainSideFace(
        TerrainChunkMeshBuilder builder,
        Vector2Int cell,
        int heightLevel,
        SurfaceBrush cellSurface,
        Vector2Int direction,
        float minX,
        float maxX,
        float minZ,
        float maxZ)
    {
        Vector2Int neighbor = cell + direction;
        int neighborHeight = ContainsCell(neighbor.x, neighbor.y) ? GetCellHeight(neighbor) : 0;
        if (neighborHeight >= heightLevel)
            return;

        float bottomY = GridPlaneYOffset + neighborHeight * PaintedCellHeight;
        float topY = GridPlaneYOffset + heightLevel * PaintedCellHeight;
        TerrainMeshMaterialSlot slot = GetMaterialSlot(GetSideFaceType(heightLevel, neighborHeight, cellSurface));

        if (direction.x > 0)
        {
            Vector3 v0 = new Vector3(maxX, bottomY, minZ);
            Vector3 v1 = new Vector3(maxX, topY, minZ);
            Vector3 v2 = new Vector3(maxX, topY, maxZ);
            Vector3 v3 = new Vector3(maxX, bottomY, maxZ);
            if (slot == TerrainMeshMaterialSlot.CliffSide)
            {
                if (!TryAddBeveledCliffSegment(builder, v0, v1, v2, v3))
                    AddDirtCliffBlendQuad(builder, v0, v1, v2, v3);
            }
            else
            {
                AddQuad(builder, slot, v0, v1, v2, v3);
            }
            return;
        }

        if (direction.x < 0)
        {
            Vector3 v0 = new Vector3(minX, bottomY, maxZ);
            Vector3 v1 = new Vector3(minX, topY, maxZ);
            Vector3 v2 = new Vector3(minX, topY, minZ);
            Vector3 v3 = new Vector3(minX, bottomY, minZ);
            if (slot == TerrainMeshMaterialSlot.CliffSide)
            {
                if (!TryAddBeveledCliffSegment(builder, v0, v1, v2, v3))
                    AddDirtCliffBlendQuad(builder, v0, v1, v2, v3);
            }
            else
            {
                AddQuad(builder, slot, v0, v1, v2, v3);
            }
            return;
        }

        if (direction.y > 0)
        {
            Vector3 v0 = new Vector3(maxX, bottomY, maxZ);
            Vector3 v1 = new Vector3(maxX, topY, maxZ);
            Vector3 v2 = new Vector3(minX, topY, maxZ);
            Vector3 v3 = new Vector3(minX, bottomY, maxZ);
            if (slot == TerrainMeshMaterialSlot.CliffSide)
            {
                if (!TryAddBeveledCliffSegment(builder, v0, v1, v2, v3))
                    AddDirtCliffBlendQuad(builder, v0, v1, v2, v3);
            }
            else
            {
                AddQuad(builder, slot, v0, v1, v2, v3);
            }
            return;
        }

        Vector3 backV0 = new Vector3(minX, bottomY, minZ);
        Vector3 backV1 = new Vector3(minX, topY, minZ);
        Vector3 backV2 = new Vector3(maxX, topY, minZ);
        Vector3 backV3 = new Vector3(maxX, bottomY, minZ);
        if (slot == TerrainMeshMaterialSlot.CliffSide)
        {
            if (!TryAddBeveledCliffSegment(builder, backV0, backV1, backV2, backV3))
                AddDirtCliffBlendQuad(builder, backV0, backV1, backV2, backV3);
        }
        else
        {
            AddQuad(builder, slot, backV0, backV1, backV2, backV3);
        }
    }

    void AddDirtCliffBlendQuad(
        TerrainChunkMeshBuilder builder,
        Vector3 low0,
        Vector3 high0,
        Vector3 high1,
        Vector3 low1)
    {
        AddDirtCliffBlendQuad(builder, low0, high0, high1, low1, 0f, 1f);
    }

    void AddDirtCliffBlendQuad(
        TerrainChunkMeshBuilder builder,
        Vector3 low0,
        Vector3 high0,
        Vector3 high1,
        Vector3 low1,
        float lowDirtWeight,
        float highDirtWeight)
    {
        AddQuad(
            builder,
            TerrainMeshMaterialSlot.DirtCliffBlendSide,
            low0,
            high0,
            high1,
            low1,
            lowDirtWeight,
            highDirtWeight,
            highDirtWeight,
            lowDirtWeight);
    }

    void AddDoubleSidedDirtCliffBlendQuad(
        TerrainChunkMeshBuilder builder,
        Vector3 low0,
        Vector3 high0,
        Vector3 high1,
        Vector3 low1)
    {
        AddDirtCliffBlendQuad(builder, low0, high0, high1, low1);
        AddQuad(
            builder,
            TerrainMeshMaterialSlot.DirtCliffBlendSide,
            low1,
            high1,
            high0,
            low0,
            0f,
            1f,
            1f,
            0f);
    }

    void AddDoubleSidedDirtCliffBlendTriangle(
        TerrainChunkMeshBuilder builder,
        Vector3 low,
        Vector3 high0,
        Vector3 high1)
    {
        AddTriangle(builder, TerrainMeshMaterialSlot.DirtCliffBlendSide, low, high0, high1, 0f, 1f, 1f);
        AddTriangle(builder, TerrainMeshMaterialSlot.DirtCliffBlendSide, high1, high0, low, 1f, 1f, 0f);
    }

    void AddQuad(
        TerrainChunkMeshBuilder builder,
        TerrainMeshMaterialSlot slot,
        Vector3 v0,
        Vector3 v1,
        Vector3 v2,
        Vector3 v3)
    {
        AddQuad(builder, slot, v0, v1, v2, v3, 0f, 0f, 0f, 0f);
    }

    void AddQuad(
        TerrainChunkMeshBuilder builder,
        TerrainMeshMaterialSlot slot,
        Vector3 v0,
        Vector3 v1,
        Vector3 v2,
        Vector3 v3,
        float dirtWeight0,
        float dirtWeight1,
        float dirtWeight2,
        float dirtWeight3)
    {
        int start = builder.vertices.Count;
        builder.vertices.Add(v0);
        builder.vertices.Add(v1);
        builder.vertices.Add(v2);
        builder.vertices.Add(v3);
        builder.colors.Add(EncodeSurfaceBlendWeight(dirtWeight0));
        builder.colors.Add(EncodeSurfaceBlendWeight(dirtWeight1));
        builder.colors.Add(EncodeSurfaceBlendWeight(dirtWeight2));
        builder.colors.Add(EncodeSurfaceBlendWeight(dirtWeight3));

        List<int> triangles = builder.triangles[(int)slot];
        triangles.Add(start);
        triangles.Add(start + 1);
        triangles.Add(start + 2);
        triangles.Add(start);
        triangles.Add(start + 2);
        triangles.Add(start + 3);
    }

    void AddTriangle(
        TerrainChunkMeshBuilder builder,
        TerrainMeshMaterialSlot slot,
        Vector3 v0,
        Vector3 v1,
        Vector3 v2)
    {
        AddTriangle(builder, slot, v0, v1, v2, 0f, 0f, 0f);
    }

    void AddTriangle(
        TerrainChunkMeshBuilder builder,
        TerrainMeshMaterialSlot slot,
        Vector3 v0,
        Vector3 v1,
        Vector3 v2,
        float dirtWeight0,
        float dirtWeight1,
        float dirtWeight2)
    {
        int start = builder.vertices.Count;
        builder.vertices.Add(v0);
        builder.vertices.Add(v1);
        builder.vertices.Add(v2);
        builder.colors.Add(EncodeSurfaceBlendWeight(dirtWeight0));
        builder.colors.Add(EncodeSurfaceBlendWeight(dirtWeight1));
        builder.colors.Add(EncodeSurfaceBlendWeight(dirtWeight2));

        List<int> triangles = builder.triangles[(int)slot];
        triangles.Add(start);
        triangles.Add(start + 1);
        triangles.Add(start + 2);
    }

    void AddDoubleSidedQuad(
        TerrainChunkMeshBuilder builder,
        TerrainMeshMaterialSlot slot,
        Vector3 v0,
        Vector3 v1,
        Vector3 v2,
        Vector3 v3)
    {
        AddQuad(builder, slot, v0, v1, v2, v3);
        AddQuad(builder, slot, v3, v2, v1, v0);
    }

    void AddDoubleSidedTriangle(
        TerrainChunkMeshBuilder builder,
        TerrainMeshMaterialSlot slot,
        Vector3 v0,
        Vector3 v1,
        Vector3 v2)
    {
        AddTriangle(builder, slot, v0, v1, v2);
        AddTriangle(builder, slot, v2, v1, v0);
    }

    static Color EncodeSurfaceBlendWeight(float dirtWeight)
    {
        return new Color(1f, 1f, 1f, Mathf.Clamp01(dirtWeight));
    }

    TerrainMeshFaceType GetTopFaceType(int heightLevel, SurfaceBrush cellSurface)
    {
        VisibleCellSurface visibleSurface = ResolveVisibleSurface(heightLevel, cellSurface);
        return visibleSurface switch
        {
            VisibleCellSurface.Rock => TerrainMeshFaceType.RockTop,
            VisibleCellSurface.Dirt => TerrainMeshFaceType.DirtTop,
            _ => TerrainMeshFaceType.GrassTop,
        };
    }

    TerrainMeshFaceType GetSideFaceType(int heightLevel, int neighborHeight, SurfaceBrush cellSurface)
    {
        if (heightLevel - neighborHeight >= cliffHeightDifferenceThreshold)
            return TerrainMeshFaceType.CliffSide;

        return ResolveVisibleSurface(heightLevel, cellSurface) switch
        {
            VisibleCellSurface.Rock => TerrainMeshFaceType.RockSide,
            VisibleCellSurface.Dirt => TerrainMeshFaceType.DirtSide,
            _ => TerrainMeshFaceType.GrassSide,
        };
    }

    static TerrainMeshMaterialSlot GetMaterialSlot(TerrainMeshFaceType faceType)
    {
        return faceType switch
        {
            TerrainMeshFaceType.GrassTop => TerrainMeshMaterialSlot.GrassTop,
            TerrainMeshFaceType.DirtTop => TerrainMeshMaterialSlot.DirtTop,
            TerrainMeshFaceType.RockTop => TerrainMeshMaterialSlot.RockTop,
            TerrainMeshFaceType.CliffSide => TerrainMeshMaterialSlot.CliffSide,
            _ => TerrainMeshMaterialSlot.FallbackSide,
        };
    }
}
