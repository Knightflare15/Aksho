using System.Collections.Generic;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public sealed partial class LevelGridDirector : MonoBehaviour
{
    const string PaintedCellsRootName = "PaintedCells";
    const string OptimizedTerrainRootName = "OptimizedTerrain";
    const string WaterPlaneName = "WaterPlane";
    const string ShoreFoamName = "ShoreFoam";
    const int DirtHeightThreshold = 65;
    const int RockHeightThreshold = 85;

#if UNITY_EDITOR
    const string DefaultGrassMaterialPath = "Assets/MyPrefabs/Tiles/Grass/grass_stylized.mat";
    const string DefaultDirtMaterialPath = "Assets/MyPrefabs/Tiles/Dirt/dirt_stylized.mat";
    const string DefaultGrassDirtBlendMaterialPath = "Assets/MyPrefabs/Tiles/Grass/grass_dirt_blend_stylized.mat";
    const string DefaultDirtCliffBlendMaterialPath = "Assets/MyPrefabs/Tiles/Rock/dirt_cliff_blend_stylized.mat";
    const string DefaultCliffMaterialPath = "Assets/MyPrefabs/Tiles/Rock/cliff_warm_rock_stylized.mat";
    const string DefaultRockMaterialPath = "Assets/MyPrefabs/Tiles/Rock/rock_stylized.mat";
    const string DefaultWaterMaterialPath = "Assets/MyPrefabs/Tiles/Water/water_stylized.mat";
    const string DefaultShoreFoamMaterialPath = "Assets/MyPrefabs/Tiles/Water/shore_foam_stylized.mat";
    const string DefaultTerrainWireMaterialPath = "Assets/MyPrefabs/Tiles/Grass/terrain_wire_overlay.mat";
#endif

    public enum SurfaceBrush
    {
        Grass = 0,
        Dirt = 1
    }

    enum VisibleCellSurface
    {
        Grass,
        Dirt,
        Rock
    }

    enum TerrainMeshMaterialSlot
    {
        GrassTop = 0,
        DirtTop = 1,
        RockTop = 2,
        CliffSide = 3,
        DirtCliffBlendSide = 4,
        FallbackSide = 5,
        GrassDirtBlendTop = 6,
        Count = 7
    }

    enum TerrainMeshFaceType
    {
        GrassTop,
        DirtTop,
        RockTop,
        GrassSide,
        DirtSide,
        RockSide,
        CliffSide
    }

    enum TerrainMeshRenderMode
    {
        ExposedFaces = 0,
        SurfaceNet = 1
    }

    struct SurfaceNetCliffCornerPatch
    {
        public Vector2Int cell;
        public int cornerX;
        public int cornerZ;
        public Vector3 highHorizontalEdge;
        public Vector3 highVerticalEdge;
        public Vector3 lowHorizontalEdge;
        public Vector3 lowVerticalEdge;
        public Vector3 lowCorner;
        public TerrainMeshMaterialSlot lowerSlot;
    }

    sealed class TerrainChunkMeshBuilder
    {
        public readonly List<Vector3> vertices = new List<Vector3>();
        public readonly List<Color> colors = new List<Color>();
        public readonly List<int>[] triangles;

        public TerrainChunkMeshBuilder()
        {
            triangles = new List<int>[(int)TerrainMeshMaterialSlot.Count];
            for (int i = 0; i < triangles.Length; i++)
                triangles[i] = new List<int>();
        }

        public bool HasGeometry => vertices.Count > 0;
    }

    [Header("Grid")]
    [SerializeField, Min(1)] int gridWidth = 150;
    [SerializeField, Min(1)] int gridHeight = 150;
    [SerializeField, Min(0.01f)] float cellSize = 1f;
    [SerializeField, Min(1)] int majorLineInterval = 10;

    [Header("Painted Land Cells")]
    [SerializeField, Min(0.01f)] float paintedCellHeightInCells = 0.25f;
    [SerializeField, Range(2, 50)] int brushSize = 2;
    [SerializeField, Range(1, 25)] int brushStrength = 1;
    [SerializeField, Min(1)] int maxPaintedCellHeight = 100;
    [SerializeField] SurfaceBrush surfaceBrush = SurfaceBrush.Grass;
    [SerializeField] Material grassCellMaterial;
    [SerializeField] Material dirtCellMaterial;
    [SerializeField] Material rockCellMaterial;
    [SerializeField] Color grassCellColor = new Color(0.22f, 0.58f, 0.2f, 1f);
    [SerializeField] Color dirtCellColor = new Color(0.48f, 0.28f, 0.13f, 1f);
    [SerializeField] Color rockCellColor = new Color(0.45f, 0.45f, 0.45f, 1f);
    [SerializeField] Transform paintedCellsRoot;
    [SerializeField] List<Vector2Int> paintedCells = new List<Vector2Int>();
    [SerializeField] List<int> paintedCellHeights = new List<int>();
    [SerializeField] List<SurfaceBrush> paintedCellSurfaces = new List<SurfaceBrush>();

    [Header("Block Optimization")]
    [SerializeField] bool cullFullyHiddenBlocks = true;

    [Header("Optimized Terrain Mesh")]
    [SerializeField] bool useOptimizedTerrainMesh = true;
    [SerializeField] TerrainMeshRenderMode terrainMeshRenderMode = TerrainMeshRenderMode.SurfaceNet;
    [SerializeField] bool showDebugCubes;
    [SerializeField, Range(1, 32)] int cliffHeightDifferenceThreshold = 5;
    [SerializeField, Range(4, 64)] int terrainChunkSizeCells = 16;
    [SerializeField, Range(1, 8)] int surfaceNetVerticalStepLevels = 2;
    [SerializeField, Range(1, 4)] int surfaceNetTopSubdivisions = 4;
    [SerializeField, Range(0, 6)] int surfaceNetHeightSmoothingPasses = 3;
    [SerializeField, Range(0f, 1f)] float surfaceNetHeightSmoothingStrength = 0.35f;
    [SerializeField] bool surfaceNetEmitSideFaces = true;
    [SerializeField, Range(1, 12)] int surfaceNetMaxSideSubdivisions = 12;
    [SerializeField] bool surfaceNetPreserveCliffs = true;
    [SerializeField, Range(1, 32)] int surfaceNetCliffHeightDifferenceThreshold = 5;
    [SerializeField] bool surfaceNetRequireCliffPlateaus = true;
    [SerializeField, Range(0, 12)] int surfaceNetCliffPlateauToleranceLevels = 3;
    [SerializeField, Range(0f, 0.75f)] float surfaceNetCliffCornerRelaxStrength;
    [SerializeField] bool surfaceNetMarchingSquaresTopCorners = true;
    [SerializeField] bool surfaceNetMarchingSquaresCliffCorners = true;
    [SerializeField, Range(0, 12)] int surfaceNetCliffCornerLowerHeightToleranceLevels = 2;
    [SerializeField, Range(0.1f, 0.49f)] float surfaceNetMarchingSquaresCornerCut = 0.49f;
    [SerializeField] bool surfaceNetUseSlopedWaterShores = true;
    [SerializeField, Range(0.1f, 2f)] float surfaceNetWaterShoreSkirtCells = 1.35f;
    [SerializeField, Range(-2f, 0f)] float surfaceNetWaterShoreBedOffsetCells = -0.55f;
    [SerializeField, Range(0f, 0.45f)] float surfaceNetCliffBevelWidthCells = 0.28f;
    [SerializeField] bool createWaterPlane = true;
    [SerializeField, Range(-4f, 4f)] float waterPlaneHeightCells = 0f;
    [SerializeField] Material waterPlaneMaterial;
    [SerializeField] Material shoreFoamMaterial;
    [SerializeField] Color waterPlaneColor = new Color(0.18f, 0.56f, 0.78f, 1f);
    [SerializeField] Material optimizedGrassTopMaterial;
    [SerializeField] Material optimizedDirtTopMaterial;
    [SerializeField] Material optimizedGrassDirtBlendTopMaterial;
    [SerializeField] bool surfaceBlendEnabled = true;
    [SerializeField, Range(1, 16)] int surfaceBlendSubdivisions = 10;
    [SerializeField, Range(0.1f, 4f)] float surfaceBlendWidthCells = 1.6f;
    [SerializeField, Range(0.25f, 4f)] float surfaceBlendFalloffPower = 1.35f;
    [SerializeField] Material optimizedRockTopMaterial;
    [SerializeField] Material optimizedCliffSideMaterial;
    [SerializeField] Material optimizedDirtCliffBlendSideMaterial;
    [SerializeField] Material optimizedFallbackSideMaterial;
    [SerializeField, Range(0.25f, 5f)] float waterShorelineFoamWidthCells = 2f;
    [SerializeField, Range(0.05f, 2f)] float shoreFoamStripWidthCells = 0.75f;
    [SerializeField] bool weldOptimizedTerrainVertices = true;
    [SerializeField] bool weldOnlySimpleTerrainVertices = true;
    [SerializeField, Range(2, 6)] int terrainVertexWeldMaxEdgeCount = 4;
    [SerializeField, Range(0.01f, 0.5f)] float optimizedTerrainVertexWeldDistance = 0.08f;
    [SerializeField] bool drawOptimizedTerrainWireOverlay;
    [SerializeField] Material optimizedTerrainWireMaterial;
    [SerializeField] Color optimizedTerrainWireColor = new Color(0f, 0f, 0f, 0.55f);
    [SerializeField, Range(0f, 0.2f)] float optimizedTerrainWireYOffset = 0.025f;
    [SerializeField] Transform optimizedTerrainRoot;

    [Header("Play Mode")]
    [SerializeField] bool lockCameraTopDownOnPlay = true;
    [SerializeField] bool enableGameViewPainting = true;
    [SerializeField] Camera playModeCamera;
    [SerializeField, Min(1f)] float topDownCameraHeight = 160f;
    [SerializeField, Min(0f)] float topDownPaddingCells = 8f;
    [SerializeField, Min(1f)] float freeCameraHeight = 55f;
    [SerializeField, Min(1f)] float freeCameraBackDistance = 65f;
    [SerializeField, Range(20f, 100f)] float freeCameraFieldOfView = 60f;
    [SerializeField] bool disableCameraFlyControllerWhileLocked = true;
    [SerializeField] bool drawRuntimeGridInPlay;
    [SerializeField] Material runtimeGridLineMaterial;

    [Header("Free Camera Fog")]
    [SerializeField] bool enableFreeCameraFog = true;
    [SerializeField, Min(1f)] float freeCameraFogStartHorizontalRadius = 22f;
    [SerializeField, Min(1f)] float freeCameraFogEndHorizontalRadius = 30f;
    [SerializeField, Min(0f)] float freeCameraFogClipPadding = 2f;
    [SerializeField] Color freeCameraFogColor = new Color(0.42f, 0.39f, 0.35f, 1f);

    [Header("Gizmos")]
    [SerializeField] bool drawGrid;
    [SerializeField] float yOffset = 0f;
    [SerializeField] Color minorLineColor = new Color(0.25f, 0.65f, 0.75f, 0.16f);
    [SerializeField] Color majorLineColor = new Color(0.25f, 0.8f, 1f, 0.34f);
    [SerializeField] Color centerAxisColor = new Color(1f, 0.9f, 0.3f, 0.65f);
    [SerializeField] Color boundsColor = new Color(1f, 1f, 1f, 0.45f);

    public int GridWidth => Mathf.Max(1, gridWidth);
    public int GridHeight => Mathf.Max(1, gridHeight);
    public float CellSize => Mathf.Max(0.01f, cellSize);
    public Vector2 WorldSize => new Vector2(GridWidth * CellSize, GridHeight * CellSize);
    public float GridPlaneYOffset => yOffset;
    public float PaintedCellHeight => Mathf.Max(0.01f, paintedCellHeightInCells) * CellSize;
    public float MaxPaintedCellWorldHeight => PaintedCellHeight * MaxPaintedCellHeight;
    public int BrushSize => Mathf.Clamp(brushSize, 2, 50);
    public int BrushStrength => Mathf.Clamp(brushStrength, 1, MaxPaintedCellHeight);
    public int MaxPaintedCellHeight => Mathf.Max(1, maxPaintedCellHeight);
    public SurfaceBrush ActiveSurfaceBrush => surfaceBrush;
    public int PaintedCellCount => paintedCells.Count;
    static Material fallbackRuntimeGridLineMaterial;
    static Material fallbackGrassCellMaterial;
    static Material fallbackDirtCellMaterial;
    static Material fallbackRockCellMaterial;
    static Material fallbackCliffSideMaterial;
    static Material fallbackTerrainSideMaterial;
    static Material fallbackWaterPlaneMaterial;
    static Material fallbackShoreFoamMaterial;
    static Material fallbackOptimizedTerrainWireMaterial;
    static Material fallbackGrassDirtBlendTopMaterial;
    static Material fallbackDirtCliffBlendSideMaterial;
    Camera lockedCamera;
    CameraFlyController disabledFlyController;
    bool disabledFlyControllerWasEnabled;
    bool cameraFreeMode;
    bool savedFogStateValid;
    bool savedFogEnabled;
    FogMode savedFogMode;
    Color savedFogColor;
    float savedFogStartDistance;
    float savedFogEndDistance;
    float savedFogDensity;
    Dictionary<Vector2Int, int> paintedCellIndexByCell;
    readonly HashSet<Vector2Int> runtimeStrokeCells = new HashSet<Vector2Int>();
    readonly List<Vector2Int> runtimeBrushCells = new List<Vector2Int>();


}
