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
    public Vector3 GridToWorld(int cellX, int cellZ, bool cellCenter = true)
    {
        return transform.TransformPoint(GridToLocal(cellX, cellZ, cellCenter));
    }

    public Vector3 GridToLocal(int cellX, int cellZ, bool cellCenter = true)
    {
        float offset = cellCenter ? 0.5f : 0f;
        float localX = (cellX + offset) * CellSize - WorldSize.x * 0.5f;
        float localZ = (cellZ + offset) * CellSize - WorldSize.y * 0.5f;
        return new Vector3(localX, yOffset, localZ);
    }

    public Vector2Int WorldToGrid(Vector3 worldPosition)
    {
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        int cellX = Mathf.FloorToInt((local.x + WorldSize.x * 0.5f) / CellSize);
        int cellZ = Mathf.FloorToInt((local.z + WorldSize.y * 0.5f) / CellSize);
        return new Vector2Int(cellX, cellZ);
    }

    public bool ContainsCell(int cellX, int cellZ)
    {
        return cellX >= 0 && cellX < GridWidth && cellZ >= 0 && cellZ < GridHeight;
    }

    public void GetBrushCells(Vector2Int anchorCell, List<Vector2Int> results)
    {
        results.Clear();

        int size = BrushSize;
        int startOffset = -((size - 1) / 2);
        for (int z = 0; z < size; z++)
        {
            for (int x = 0; x < size; x++)
            {
                int cellX = anchorCell.x + startOffset + x;
                int cellZ = anchorCell.y + startOffset + z;
                if (ContainsCell(cellX, cellZ))
                    results.Add(new Vector2Int(cellX, cellZ));
            }
        }
    }

    public int GetBrushAmountForCell(Vector2Int anchorCell, Vector2Int brushCell)
    {
        int size = BrushSize;
        if (size <= 2)
            return BrushStrength;

        float radius = size * 0.5f;
        float sigma = Mathf.Max(0.01f, radius * 0.45f);
        Vector2 anchorCenter = new Vector2(anchorCell.x + 0.5f, anchorCell.y + 0.5f);
        Vector2 cellCenter = new Vector2(brushCell.x + 0.5f, brushCell.y + 0.5f);
        float distance = Vector2.Distance(anchorCenter, cellCenter);
        float normalizedDistance = distance / radius;

        if (normalizedDistance >= 1f)
            return 1;

        float gaussian = Mathf.Exp(-(distance * distance) / (2f * sigma * sigma));
        float edgeFloor = 1f / BrushStrength;
        float falloff = Mathf.Lerp(edgeFloor, 1f, gaussian);
        return Mathf.Clamp(Mathf.RoundToInt(BrushStrength * falloff), 1, BrushStrength);
    }

    public bool TryGetCellFromWorldRay(Ray ray, out Vector2Int cell)
    {
        Plane gridPlane = new Plane(
            transform.up,
            transform.TransformPoint(new Vector3(0f, GridPlaneYOffset, 0f)));

        if (!gridPlane.Raycast(ray, out float hitDistance))
        {
            cell = default;
            return false;
        }

        cell = WorldToGrid(ray.GetPoint(hitDistance));
        return ContainsCell(cell.x, cell.y);
    }

    public int GetCellHeight(Vector2Int cell)
    {
        EnsurePaintedCellCache();
        return paintedCellIndexByCell.TryGetValue(cell, out int index) ? paintedCellHeights[index] : 0;
    }

    public SurfaceBrush GetCellSurface(Vector2Int cell)
    {
        EnsurePaintedCellCache();
        return paintedCellIndexByCell.TryGetValue(cell, out int index) ? paintedCellSurfaces[index] : surfaceBrush;
    }

    public bool RaiseCellHeight(Vector2Int cell)
    {
        return RaiseCellHeight(cell, BrushStrength);
    }

    public bool RaiseCellHeight(Vector2Int cell, int amount)
    {
        if (!ContainsCell(cell.x, cell.y))
            return false;

        amount = Mathf.Clamp(amount, 1, MaxPaintedCellHeight);
        EnsurePaintedCellCache();
        if (paintedCellIndexByCell.TryGetValue(cell, out int existingIndex))
        {
            int currentHeight = paintedCellHeights[existingIndex];
            if (currentHeight >= MaxPaintedCellHeight)
                return false;

            paintedCellHeights[existingIndex] = Mathf.Min(MaxPaintedCellHeight, currentHeight + amount);
            paintedCellSurfaces[existingIndex] = surfaceBrush;
            RefreshCellAndNeighbors(cell);
            return true;
        }

        paintedCellIndexByCell.Add(cell, paintedCells.Count);
        paintedCells.Add(cell);
        paintedCellHeights.Add(Mathf.Min(MaxPaintedCellHeight, amount));
        paintedCellSurfaces.Add(surfaceBrush);
        RefreshCellAndNeighbors(cell);
        return true;
    }

    public bool LowerCellHeight(Vector2Int cell)
    {
        return LowerCellHeight(cell, BrushStrength);
    }

    public bool LowerCellHeight(Vector2Int cell, int amount)
    {
        amount = Mathf.Clamp(amount, 1, MaxPaintedCellHeight);
        EnsurePaintedCellCache();
        if (!paintedCellIndexByCell.TryGetValue(cell, out int index))
            return false;

        int currentHeight = paintedCellHeights[index];
        if (currentHeight > amount)
        {
            paintedCellHeights[index] = currentHeight - amount;
            RefreshCellAndNeighbors(cell);
            return true;
        }

        paintedCells.RemoveAt(index);
        paintedCellHeights.RemoveAt(index);
        paintedCellSurfaces.RemoveAt(index);
        paintedCellIndexByCell = null;

        if (useOptimizedTerrainMesh)
        {
            RebuildPaintedCellObjects();
            return true;
        }

        DestroyPaintedCellObject(cell);
        RefreshNeighborCellObjects(cell);
        return true;
    }

    public void ClearPaintedCells()
    {
        paintedCells.Clear();
        paintedCellHeights.Clear();
        paintedCellSurfaces.Clear();
        paintedCellIndexByCell?.Clear();

        if (paintedCellsRoot != null)
        {
            DestroyGameObject(paintedCellsRoot.gameObject);
            paintedCellsRoot = null;
        }

        ClearOptimizedTerrainMeshes();

    }

    public void RebuildPaintedCellObjects()
    {
        AssignDefaultStylizedMaterials();
        NormalizePaintedCells();

        if (useOptimizedTerrainMesh)
        {
            if (showDebugCubes)
                RebuildDebugCellObjects();
            else
                ClearDebugCellObjects();

            if (terrainMeshRenderMode == TerrainMeshRenderMode.SurfaceNet)
                RebuildSurfaceNetTerrainMesh();
            else
                RebuildOptimizedTerrainMeshes();
            return;
        }

        ClearOptimizedTerrainMeshes();
        RebuildDebugCellObjects();
    }

    void RebuildDebugCellObjects()
    {
        if (paintedCellsRoot != null)
        {
            for (int i = paintedCellsRoot.childCount - 1; i >= 0; i--)
                DestroyGameObject(paintedCellsRoot.GetChild(i).gameObject);
        }

        for (int i = 0; i < paintedCells.Count; i++)
            CreateOrUpdatePaintedCellObject(paintedCells[i]);
    }

    void ClearDebugCellObjects()
    {
        Transform root = paintedCellsRoot != null ? paintedCellsRoot : transform.Find(PaintedCellsRootName);
        if (root == null)
            return;

        DestroyGameObject(root.gameObject);
        paintedCellsRoot = null;
    }
}
