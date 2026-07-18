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
    void OnValidate()
    {
        AssignDefaultStylizedMaterials();

        gridWidth = Mathf.Max(1, gridWidth);
        gridHeight = Mathf.Max(1, gridHeight);
        cellSize = Mathf.Max(0.01f, cellSize);
        majorLineInterval = Mathf.Max(1, majorLineInterval);
        paintedCellHeightInCells = Mathf.Max(0.01f, paintedCellHeightInCells);
        maxPaintedCellHeight = Mathf.Max(1, maxPaintedCellHeight);
        brushSize = Mathf.Clamp(brushSize, 2, 50);
        brushStrength = Mathf.Clamp(brushStrength, 1, maxPaintedCellHeight);
        cliffHeightDifferenceThreshold = Mathf.Clamp(cliffHeightDifferenceThreshold, 1, 32);
        terrainChunkSizeCells = Mathf.Clamp(terrainChunkSizeCells, 4, 64);
        surfaceNetVerticalStepLevels = Mathf.Clamp(surfaceNetVerticalStepLevels, 1, 8);
        surfaceNetTopSubdivisions = Mathf.Clamp(surfaceNetTopSubdivisions, 1, 4);
        surfaceNetMaxSideSubdivisions = Mathf.Clamp(surfaceNetMaxSideSubdivisions, 1, 12);
        surfaceNetHeightSmoothingPasses = Mathf.Clamp(surfaceNetHeightSmoothingPasses, 0, 6);
        surfaceNetHeightSmoothingStrength = Mathf.Clamp01(surfaceNetHeightSmoothingStrength);
        surfaceNetCliffHeightDifferenceThreshold = Mathf.Clamp(surfaceNetCliffHeightDifferenceThreshold, 1, 32);
        surfaceNetCliffPlateauToleranceLevels = Mathf.Clamp(surfaceNetCliffPlateauToleranceLevels, 0, 12);
        surfaceNetCliffCornerRelaxStrength = Mathf.Clamp(surfaceNetCliffCornerRelaxStrength, 0f, 0.75f);
        surfaceNetMarchingSquaresCornerCut = Mathf.Clamp(surfaceNetMarchingSquaresCornerCut, 0.1f, 0.49f);
        surfaceBlendSubdivisions = Mathf.Clamp(surfaceBlendSubdivisions, 1, 16);
        surfaceBlendWidthCells = Mathf.Clamp(surfaceBlendWidthCells, 0.1f, 4f);
        surfaceBlendFalloffPower = Mathf.Clamp(surfaceBlendFalloffPower, 0.25f, 4f);
        surfaceNetWaterShoreSkirtCells = Mathf.Clamp(surfaceNetWaterShoreSkirtCells, 0.1f, 2f);
        surfaceNetWaterShoreBedOffsetCells = Mathf.Clamp(surfaceNetWaterShoreBedOffsetCells, -2f, 0f);
        surfaceNetCliffBevelWidthCells = Mathf.Clamp(surfaceNetCliffBevelWidthCells, 0f, 0.45f);
        waterShorelineFoamWidthCells = Mathf.Clamp(waterShorelineFoamWidthCells, 0.25f, 5f);
        shoreFoamStripWidthCells = Mathf.Clamp(shoreFoamStripWidthCells, 0.05f, 2f);
        terrainVertexWeldMaxEdgeCount = Mathf.Clamp(terrainVertexWeldMaxEdgeCount, 2, 6);
        optimizedTerrainVertexWeldDistance = Mathf.Clamp(optimizedTerrainVertexWeldDistance, 0.01f, 0.5f);
        optimizedTerrainWireYOffset = Mathf.Clamp(optimizedTerrainWireYOffset, 0f, 0.2f);
        waterPlaneHeightCells = Mathf.Clamp(waterPlaneHeightCells, -4f, 4f);
        topDownCameraHeight = Mathf.Max(1f, topDownCameraHeight);
        topDownPaddingCells = Mathf.Max(0f, topDownPaddingCells);
        freeCameraHeight = Mathf.Max(1f, freeCameraHeight);
        freeCameraBackDistance = Mathf.Max(1f, freeCameraBackDistance);
        freeCameraFieldOfView = Mathf.Clamp(freeCameraFieldOfView, 20f, 100f);
        freeCameraFogStartHorizontalRadius = Mathf.Max(1f, freeCameraFogStartHorizontalRadius);
        freeCameraFogEndHorizontalRadius = Mathf.Max(freeCameraFogStartHorizontalRadius + 0.1f, freeCameraFogEndHorizontalRadius);
        freeCameraFogClipPadding = Mathf.Max(0f, freeCameraFogClipPadding);
        NormalizePaintedCells();

        if (useOptimizedTerrainMesh)
            RebuildPaintedCellObjects();
        else
            UpdateExistingPaintedCellObjects();
    }

    void AssignDefaultStylizedMaterials()
    {
#if UNITY_EDITOR
        grassCellMaterial = grassCellMaterial != null
            ? grassCellMaterial
            : AssetDatabase.LoadAssetAtPath<Material>(DefaultGrassMaterialPath);
        dirtCellMaterial = dirtCellMaterial != null
            ? dirtCellMaterial
            : AssetDatabase.LoadAssetAtPath<Material>(DefaultDirtMaterialPath);
        rockCellMaterial = rockCellMaterial != null
            ? rockCellMaterial
            : AssetDatabase.LoadAssetAtPath<Material>(DefaultRockMaterialPath);
        waterPlaneMaterial = waterPlaneMaterial != null
            ? waterPlaneMaterial
            : AssetDatabase.LoadAssetAtPath<Material>(DefaultWaterMaterialPath);
        shoreFoamMaterial = shoreFoamMaterial != null
            ? shoreFoamMaterial
            : AssetDatabase.LoadAssetAtPath<Material>(DefaultShoreFoamMaterialPath);

        optimizedGrassTopMaterial = optimizedGrassTopMaterial != null ? optimizedGrassTopMaterial : grassCellMaterial;
        optimizedDirtTopMaterial = optimizedDirtTopMaterial != null ? optimizedDirtTopMaterial : dirtCellMaterial;
        optimizedGrassDirtBlendTopMaterial = optimizedGrassDirtBlendTopMaterial != null
            ? optimizedGrassDirtBlendTopMaterial
            : AssetDatabase.LoadAssetAtPath<Material>(DefaultGrassDirtBlendMaterialPath);
        optimizedRockTopMaterial = optimizedRockTopMaterial != null ? optimizedRockTopMaterial : rockCellMaterial;
        optimizedCliffSideMaterial = optimizedCliffSideMaterial != null
            ? optimizedCliffSideMaterial
            : AssetDatabase.LoadAssetAtPath<Material>(DefaultCliffMaterialPath);
        optimizedDirtCliffBlendSideMaterial = optimizedDirtCliffBlendSideMaterial != null
            ? optimizedDirtCliffBlendSideMaterial
            : AssetDatabase.LoadAssetAtPath<Material>(DefaultDirtCliffBlendMaterialPath);
        optimizedFallbackSideMaterial = dirtCellMaterial;
        optimizedTerrainWireMaterial = optimizedTerrainWireMaterial != null
            ? optimizedTerrainWireMaterial
            : AssetDatabase.LoadAssetAtPath<Material>(DefaultTerrainWireMaterialPath);
#endif
    }

    void Start()
    {
        if (!Application.isPlaying)
            return;

        if (lockCameraTopDownOnPlay)
            ApplyTopDownCameraLock();
    }

    void Update()
    {
        if (!Application.isPlaying)
            return;

        if (lockCameraTopDownOnPlay && WasFreeCameraTogglePressed())
            SetCameraFreeMode(!cameraFreeMode);

        if (!enableGameViewPainting || cameraFreeMode)
        {
            runtimeStrokeCells.Clear();
            return;
        }

        HandleGameViewPainting();
    }

    void LateUpdate()
    {
        if (!Application.isPlaying || !lockCameraTopDownOnPlay)
            return;

        if (cameraFreeMode)
        {
            ApplyFreeCameraFog(ResolvePlayModeCamera());
            return;
        }

        ApplyTopDownCameraLock();
    }

    void OnDisable()
    {
        RestoreDisabledCameraController();
        RestoreSceneFog();
    }

    void OnDestroy()
    {
        RestoreDisabledCameraController();
        RestoreSceneFog();
    }

    void SetCameraFreeMode(bool freeMode)
    {
        cameraFreeMode = freeMode;
        runtimeStrokeCells.Clear();

        if (cameraFreeMode)
        {
            Camera cameraToFree = ResolvePlayModeCamera();
            if (cameraToFree != null)
                ApplyFreeCameraPose(cameraToFree);

            RestoreDisabledCameraController(true);
            return;
        }

        ApplyTopDownCameraLock();
    }

    void ApplyTopDownCameraLock()
    {
        Camera cameraToLock = ResolvePlayModeCamera();
        if (cameraToLock == null)
            return;

        PrepareLockedCamera(cameraToLock);

        float halfWidth = WorldSize.x * 0.5f + topDownPaddingCells * CellSize;
        float halfHeight = WorldSize.y * 0.5f + topDownPaddingCells * CellSize;
        float aspect = Mathf.Max(0.01f, cameraToLock.aspect);

        cameraToLock.orthographic = true;
        cameraToLock.orthographicSize = Mathf.Max(halfHeight, halfWidth / aspect);
        cameraToLock.nearClipPlane = 0.1f;
        cameraToLock.farClipPlane = Mathf.Max(cameraToLock.farClipPlane, topDownCameraHeight + MaxPaintedCellWorldHeight + 100f);

        Vector3 gridCenter = transform.TransformPoint(new Vector3(0f, GridPlaneYOffset, 0f));
        cameraToLock.transform.position = gridCenter + transform.up * topDownCameraHeight;
        cameraToLock.transform.rotation = Quaternion.LookRotation(-transform.up, transform.forward);
        RestoreSceneFog();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void ApplyFreeCameraPose(Camera cameraToFree)
    {
        cameraToFree.orthographic = false;
        cameraToFree.fieldOfView = freeCameraFieldOfView;
        cameraToFree.nearClipPlane = 0.1f;

        Vector3 gridCenter = transform.TransformPoint(new Vector3(0f, GridPlaneYOffset, 0f));
        Vector3 freePosition = gridCenter - transform.forward * freeCameraBackDistance + transform.up * freeCameraHeight;
        cameraToFree.transform.position = freePosition;
        cameraToFree.transform.rotation = Quaternion.LookRotation((gridCenter - freePosition).normalized, transform.up);
        ApplyFreeCameraFog(cameraToFree);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    Camera ResolvePlayModeCamera()
    {
        if (playModeCamera != null)
            return playModeCamera;

        if (Camera.main != null)
            return Camera.main;

        return FindAnyObjectByType<Camera>();
    }

    void ApplyFreeCameraFog(Camera targetCamera)
    {
        if (targetCamera == null)
            return;

        if (!enableFreeCameraFog)
        {
            targetCamera.farClipPlane = Mathf.Max(targetCamera.farClipPlane, WorldSize.magnitude + freeCameraHeight + MaxPaintedCellWorldHeight + 100f);
            RestoreSceneFog();
            return;
        }

        SaveSceneFogIfNeeded();

        float verticalDistanceToGrid = Mathf.Abs(Vector3.Dot(
            targetCamera.transform.position - transform.TransformPoint(new Vector3(0f, GridPlaneYOffset, 0f)),
            transform.up));
        float maxVerticalSpan = Mathf.Max(verticalDistanceToGrid, Mathf.Abs(verticalDistanceToGrid - MaxPaintedCellWorldHeight));
        float fogStartDistance = Mathf.Sqrt(
            freeCameraFogStartHorizontalRadius * freeCameraFogStartHorizontalRadius +
            maxVerticalSpan * maxVerticalSpan);
        float fogEndDistance = Mathf.Sqrt(
            freeCameraFogEndHorizontalRadius * freeCameraFogEndHorizontalRadius +
            maxVerticalSpan * maxVerticalSpan);

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = freeCameraFogColor;
        RenderSettings.fogStartDistance = fogStartDistance;
        RenderSettings.fogEndDistance = fogEndDistance;
        targetCamera.farClipPlane = Mathf.Max(targetCamera.nearClipPlane + 1f, fogEndDistance + freeCameraFogClipPadding);
    }

    void SaveSceneFogIfNeeded()
    {
        if (savedFogStateValid)
            return;

        savedFogEnabled = RenderSettings.fog;
        savedFogMode = RenderSettings.fogMode;
        savedFogColor = RenderSettings.fogColor;
        savedFogStartDistance = RenderSettings.fogStartDistance;
        savedFogEndDistance = RenderSettings.fogEndDistance;
        savedFogDensity = RenderSettings.fogDensity;
        savedFogStateValid = true;
    }

    void RestoreSceneFog()
    {
        if (!savedFogStateValid)
            return;

        RenderSettings.fog = savedFogEnabled;
        RenderSettings.fogMode = savedFogMode;
        RenderSettings.fogColor = savedFogColor;
        RenderSettings.fogStartDistance = savedFogStartDistance;
        RenderSettings.fogEndDistance = savedFogEndDistance;
        RenderSettings.fogDensity = savedFogDensity;
        savedFogStateValid = false;
    }

    void PrepareLockedCamera(Camera cameraToLock)
    {
        if (lockedCamera == cameraToLock)
            return;

        RestoreDisabledCameraController();
        lockedCamera = cameraToLock;

        if (!disableCameraFlyControllerWhileLocked)
            return;

        disabledFlyController = cameraToLock.GetComponent<CameraFlyController>();
        if (disabledFlyController == null)
            return;

        disabledFlyControllerWasEnabled = disabledFlyController.enabled;
        disabledFlyController.enabled = false;
    }

    void RestoreDisabledCameraController(bool forceEnableFlyController = false)
    {
        if (disabledFlyController != null)
            disabledFlyController.enabled = forceEnableFlyController || disabledFlyControllerWasEnabled;

        disabledFlyController = null;
        disabledFlyControllerWasEnabled = false;
        lockedCamera = null;
    }

    void HandleGameViewPainting()
    {
        bool paintHeld = IsMouseButtonHeld(0);
        bool eraseHeld = IsMouseButtonHeld(1);
        if (!paintHeld && !eraseHeld)
        {
            runtimeStrokeCells.Clear();
            return;
        }

        Camera cameraForPainting = ResolvePlayModeCamera();
        if (cameraForPainting == null)
            return;

        Ray ray = cameraForPainting.ScreenPointToRay(ReadMousePosition());
        if (!TryGetCellFromWorldRay(ray, out Vector2Int cell))
            return;

        bool lowerHeight = eraseHeld || IsShiftHeld();
        GetBrushCells(cell, runtimeBrushCells);
        for (int i = 0; i < runtimeBrushCells.Count; i++)
        {
            Vector2Int brushCell = runtimeBrushCells[i];
            if (!runtimeStrokeCells.Add(brushCell))
                continue;

            if (lowerHeight)
                LowerCellHeight(brushCell, GetBrushAmountForCell(cell, brushCell));
            else
                RaiseCellHeight(brushCell, GetBrushAmountForCell(cell, brushCell));
        }
    }

    void EnsurePaintedCellCache()
    {
        if (paintedCellIndexByCell != null && paintedCellIndexByCell.Count == paintedCells.Count)
            return;

        NormalizePaintedCells();
        paintedCellIndexByCell = new Dictionary<Vector2Int, int>();
        for (int i = 0; i < paintedCells.Count; i++)
            paintedCellIndexByCell[paintedCells[i]] = i;
    }

    void NormalizePaintedCells()
    {
        if (paintedCells == null)
            paintedCells = new List<Vector2Int>();

        if (paintedCellHeights == null)
            paintedCellHeights = new List<int>();

        if (paintedCellSurfaces == null)
            paintedCellSurfaces = new List<SurfaceBrush>();

        while (paintedCellHeights.Count < paintedCells.Count)
            paintedCellHeights.Add(1);

        while (paintedCellHeights.Count > paintedCells.Count)
            paintedCellHeights.RemoveAt(paintedCellHeights.Count - 1);

        while (paintedCellSurfaces.Count < paintedCells.Count)
            paintedCellSurfaces.Add(surfaceBrush);

        while (paintedCellSurfaces.Count > paintedCells.Count)
            paintedCellSurfaces.RemoveAt(paintedCellSurfaces.Count - 1);

        HashSet<Vector2Int> uniqueCells = new HashSet<Vector2Int>();
        for (int i = paintedCells.Count - 1; i >= 0; i--)
        {
            Vector2Int cell = paintedCells[i];
            if (!ContainsCell(cell.x, cell.y) || !uniqueCells.Add(cell))
            {
                paintedCells.RemoveAt(i);
                paintedCellHeights.RemoveAt(i);
                paintedCellSurfaces.RemoveAt(i);
                continue;
            }

            paintedCellHeights[i] = Mathf.Clamp(paintedCellHeights[i], 1, MaxPaintedCellHeight);
        }

        paintedCellIndexByCell = null;
    }

    Transform EnsurePaintedCellsRoot()
    {
        if (paintedCellsRoot != null)
            return paintedCellsRoot;

        Transform existingRoot = transform.Find(PaintedCellsRootName);
        if (existingRoot != null)
        {
            paintedCellsRoot = existingRoot;
            return paintedCellsRoot;
        }

        GameObject rootObject = new GameObject(PaintedCellsRootName);
#if UNITY_EDITOR
        if (!Application.isPlaying)
            Undo.RegisterCreatedObjectUndo(rootObject, "Create Painted Cells Root");
#endif
        rootObject.transform.SetParent(transform, false);
        paintedCellsRoot = rootObject.transform;
        return paintedCellsRoot;
    }
}
