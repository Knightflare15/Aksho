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
    Transform EnsureOptimizedTerrainRoot()
    {
        if (optimizedTerrainRoot != null)
            return optimizedTerrainRoot;

        Transform existingRoot = transform.Find(OptimizedTerrainRootName);
        if (existingRoot != null)
        {
            optimizedTerrainRoot = existingRoot;
            return optimizedTerrainRoot;
        }

        GameObject rootObject = new GameObject(OptimizedTerrainRootName);
#if UNITY_EDITOR
        if (!Application.isPlaying)
            Undo.RegisterCreatedObjectUndo(rootObject, "Create Optimized Terrain Root");
#endif
        rootObject.transform.SetParent(transform, false);
        optimizedTerrainRoot = rootObject.transform;
        return optimizedTerrainRoot;
    }

    void ClearOptimizedTerrainMeshes()
    {
        Transform root = optimizedTerrainRoot != null ? optimizedTerrainRoot : transform.Find(OptimizedTerrainRootName);
        if (root == null)
            return;

        DestroyGameObject(root.gameObject);
        optimizedTerrainRoot = null;
    }

    void CreateOrUpdatePaintedCellObject(Vector2Int cell)
    {
        if (GetCellHeight(cell) <= 0)
        {
            DestroyPaintedCellObject(cell);
            return;
        }

        Transform root = EnsurePaintedCellsRoot();
        string objectName = GetPaintedCellObjectName(cell);
        Transform existingCell = root.Find(objectName);

        GameObject cellObject;
        if (existingCell != null)
        {
            cellObject = existingCell.gameObject;
        }
        else
        {
            cellObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cellObject.name = objectName;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                Undo.RegisterCreatedObjectUndo(cellObject, "Paint Level Cell");
#endif
            cellObject.transform.SetParent(root, false);
        }

        int heightLevel = GetCellHeight(cell);
        ApplyPaintedCellTransform(cellObject.transform, cell, heightLevel);
        ApplyPaintedCellMaterial(cellObject, heightLevel, GetCellSurface(cell));
    }

    void RefreshCellAndNeighbors(Vector2Int cell)
    {
        if (useOptimizedTerrainMesh)
        {
            RebuildPaintedCellObjects();
            return;
        }

        CreateOrUpdatePaintedCellObject(cell);
        RefreshNeighborCellObjects(cell);
    }

    void RefreshNeighborCellObjects(Vector2Int cell)
    {
        RefreshPaintedCellObjectIfNeeded(new Vector2Int(cell.x - 1, cell.y));
        RefreshPaintedCellObjectIfNeeded(new Vector2Int(cell.x + 1, cell.y));
        RefreshPaintedCellObjectIfNeeded(new Vector2Int(cell.x, cell.y - 1));
        RefreshPaintedCellObjectIfNeeded(new Vector2Int(cell.x, cell.y + 1));
    }

    void RefreshPaintedCellObjectIfNeeded(Vector2Int cell)
    {
        if (!ContainsCell(cell.x, cell.y) || GetCellHeight(cell) <= 0)
            return;

        CreateOrUpdatePaintedCellObject(cell);
    }

    void DestroyPaintedCellObject(Vector2Int cell)
    {
        Transform root = paintedCellsRoot != null ? paintedCellsRoot : transform.Find(PaintedCellsRootName);
        if (root == null)
            return;

        Transform existingCell = root.Find(GetPaintedCellObjectName(cell));
        if (existingCell != null)
            DestroyGameObject(existingCell.gameObject);
    }

    void UpdateExistingPaintedCellObjects()
    {
        Transform root = paintedCellsRoot != null ? paintedCellsRoot : transform.Find(PaintedCellsRootName);
        if (root == null)
            return;

        paintedCellsRoot = root;
        for (int i = 0; i < paintedCells.Count; i++)
        {
            Transform cellObject = root.Find(GetPaintedCellObjectName(paintedCells[i]));
            if (cellObject == null)
                continue;

            ApplyPaintedCellTransform(cellObject, paintedCells[i], paintedCellHeights[i]);
            ApplyPaintedCellMaterial(cellObject.gameObject, paintedCellHeights[i], paintedCellSurfaces[i]);
        }
    }

    void ApplyPaintedCellTransform(Transform cellTransform, Vector2Int cell, int heightLevel)
    {
        heightLevel = Mathf.Clamp(heightLevel, 1, MaxPaintedCellHeight);
        int hiddenBaseLevels = GetFullyHiddenBaseLevels(cell, heightLevel);
        int visibleLevels = Mathf.Max(1, heightLevel - hiddenBaseLevels);
        float hiddenBaseHeight = PaintedCellHeight * hiddenBaseLevels;
        float height = PaintedCellHeight * visibleLevels;
        Vector3 localCenter = GridToLocal(cell.x, cell.y);
        localCenter.y += hiddenBaseHeight + height * 0.5f;

        cellTransform.localPosition = localCenter;
        cellTransform.localRotation = Quaternion.identity;
        cellTransform.localScale = new Vector3(CellSize, height, CellSize);
    }

    int GetFullyHiddenBaseLevels(Vector2Int cell, int heightLevel)
    {
        if (!cullFullyHiddenBlocks || heightLevel <= 1)
            return 0;

        int hiddenLevels = heightLevel - 1;
        hiddenLevels = Mathf.Min(hiddenLevels, GetCellHeight(new Vector2Int(cell.x - 1, cell.y)));
        hiddenLevels = Mathf.Min(hiddenLevels, GetCellHeight(new Vector2Int(cell.x + 1, cell.y)));
        hiddenLevels = Mathf.Min(hiddenLevels, GetCellHeight(new Vector2Int(cell.x, cell.y - 1)));
        hiddenLevels = Mathf.Min(hiddenLevels, GetCellHeight(new Vector2Int(cell.x, cell.y + 1)));
        return Mathf.Clamp(hiddenLevels, 0, heightLevel - 1);
    }

    void ApplyPaintedCellMaterial(GameObject cellObject, int heightLevel, SurfaceBrush cellSurface)
    {
        MeshRenderer renderer = cellObject.GetComponent<MeshRenderer>();
        if (renderer != null)
            renderer.sharedMaterial = GetCellMaterial(heightLevel, cellSurface);
    }

    Material GetCellMaterial(int heightLevel, SurfaceBrush cellSurface)
    {
        VisibleCellSurface visibleSurface = ResolveVisibleSurface(heightLevel, cellSurface);
        return visibleSurface switch
        {
            VisibleCellSurface.Rock => rockCellMaterial != null
                ? rockCellMaterial
                : GetFallbackCellMaterial(ref fallbackRockCellMaterial, rockCellColor, "Level Grid Rock"),
            VisibleCellSurface.Dirt => dirtCellMaterial != null
                ? dirtCellMaterial
                : GetFallbackCellMaterial(ref fallbackDirtCellMaterial, dirtCellColor, "Level Grid Dirt"),
            _ => grassCellMaterial != null
                ? grassCellMaterial
                : GetFallbackCellMaterial(ref fallbackGrassCellMaterial, grassCellColor, "Level Grid Grass"),
        };
    }

    Material GetWaterPlaneMaterial()
    {
        return waterPlaneMaterial != null
            ? waterPlaneMaterial
            : GetFallbackCellMaterial(ref fallbackWaterPlaneMaterial, waterPlaneColor, "Level Grid Water");
    }

    Material GetShoreFoamMaterial()
    {
        if (shoreFoamMaterial != null)
            return shoreFoamMaterial;

        if (fallbackShoreFoamMaterial != null)
            return fallbackShoreFoamMaterial;

        Shader shader = Shader.Find("The Script/Terrain/Stylized Shore Foam");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            return GetWaterPlaneMaterial();

        fallbackShoreFoamMaterial = new Material(shader)
        {
            name = "Level Grid Shore Foam",
            hideFlags = HideFlags.HideAndDontSave
        };
        return fallbackShoreFoamMaterial;
    }

    static VisibleCellSurface ResolveVisibleSurface(int heightLevel, SurfaceBrush cellSurface)
    {
        if (heightLevel > RockHeightThreshold)
            return VisibleCellSurface.Rock;

        if (heightLevel > DirtHeightThreshold)
            return VisibleCellSurface.Dirt;

        return cellSurface == SurfaceBrush.Dirt ? VisibleCellSurface.Dirt : VisibleCellSurface.Grass;
    }

    static Material GetFallbackCellMaterial(ref Material material, Color color, string materialName)
    {
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            if (shader == null)
                return null;

            material = new Material(shader)
            {
                name = materialName,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        SetMaterialColor(material, color);
        return material;
    }

    static void SetMaterialColor(Material material, Color color)
    {
        if (material == null)
            return;

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);

        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
    }

    static string GetPaintedCellObjectName(Vector2Int cell)
    {
        return $"LandCell_{cell.x:000}_{cell.y:000}";
    }

    static void DestroyGameObject(GameObject target)
    {
        if (target == null)
            return;

        if (Application.isPlaying)
        {
            Destroy(target);
            return;
        }

#if UNITY_EDITOR
        Undo.DestroyObjectImmediate(target);
#else
        DestroyImmediate(target);
#endif
    }
}
