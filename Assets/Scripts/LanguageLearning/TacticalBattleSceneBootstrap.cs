using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class TacticalBattleSceneBootstrap : MonoBehaviour
{
    [Header("Scene Setup")]
    public bool createCameraAndLight = true;
    public Vector3 cameraPosition = new Vector3(0f, 8f, -8f);
    public Vector3 cameraEulerAngles = new Vector3(55f, 0f, 0f);

    [Header("Placeholder Renderer")]
    public bool renderPlaceholderCubes = true;
    public Transform boardAnchor;
    [Min(0.5f)] public float cellSize = 1.25f;
    public float boardHeight = 0.2f;
    public float floorCubeHeight = 0.08f;
    public float terrainCubeHeight = 0.45f;
    public float unitCubeHeight = 0.85f;

    Transform boardRoot;
    Material floorMaterial;
    Material playerMaterial;
    Material enemyMaterial;
    Material selectedCellMaterial;
    Material movementPreviewMaterial;
    Material attackPreviewMaterial;
    Material pendingAttackMaterial;
    readonly Dictionary<TacticalBattleCellType, Material> terrainMaterials = new Dictionary<TacticalBattleCellType, Material>();
    readonly List<Camera> camerasDisabledForBattle = new List<Camera>();

    void Awake()
    {
        if (createCameraAndLight)
            EnsureCameraAndLight();
    }

    void OnEnable()
    {
        TacticalBattleSceneTransfer.PayloadChanged += HandlePayloadChanged;
        RenderPayload(TacticalBattleSceneTransfer.CurrentPayload);
    }

    void OnDisable()
    {
        TacticalBattleSceneTransfer.PayloadChanged -= HandlePayloadChanged;
        ClearBoard();
        foreach (Camera camera in camerasDisabledForBattle)
        {
            if (camera != null)
                camera.enabled = true;
        }
        camerasDisabledForBattle.Clear();
    }

    void HandlePayloadChanged(TacticalBattleScenePayload payload)
    {
        RenderPayload(payload);
    }

    void RenderPayload(TacticalBattleScenePayload payload)
    {
        ClearBoard();
        if (!renderPlaceholderCubes || payload == null || payload.width < 1 || payload.height < 1)
            return;

        EnsureMaterials();
        var root = new GameObject("TacticalBattle_CubeBoard");
        boardRoot = root.transform;
        boardRoot.SetPositionAndRotation(ResolveBoardOrigin(), Quaternion.identity);

        if (payload.showDebugGrid)
        {
            for (int x = 0; x < payload.width; x++)
            {
                for (int y = 0; y < payload.height; y++)
                    CreateHexPrism($"Tile_{x}_{y}", payload, new TacticalBattlePosition(x, y), floorCubeHeight, floorMaterial, 0f);
            }
        }

        foreach (TacticalBattleTerrainPayload terrain in payload.terrain)
        {
            if (terrain == null)
                continue;

            var position = new TacticalBattlePosition(terrain.x, terrain.y);
            CreateCube($"{terrain.cellType}_{terrain.x}_{terrain.y}", payload, position, terrainCubeHeight, ResolveTerrainMaterial(terrain.cellType), floorCubeHeight + terrainCubeHeight * 0.5f);
        }

        foreach (TacticalBattlePosition cell in payload.playerAttackArc)
            CreateHexPrism($"AttackArc_{cell.x}_{cell.y}", payload, cell, floorCubeHeight * 1.6f, attackPreviewMaterial, floorCubeHeight * 0.75f);
        foreach (TacticalBattlePosition cell in payload.playerMovementPath)
            CreateHexPrism($"Movement_{cell.x}_{cell.y}", payload, cell, floorCubeHeight * 1.7f, movementPreviewMaterial, floorCubeHeight * 0.85f);
        if (payload.hasSelectedTacticalCell)
            CreateHexPrism($"Selected_{payload.selectedTacticalCell.x}_{payload.selectedTacticalCell.y}", payload, payload.selectedTacticalCell, floorCubeHeight * 1.8f, selectedCellMaterial, floorCubeHeight * 0.9f);
        foreach (TacticalBattlePosition cell in payload.enemyAttackArc)
            CreateHexPrism($"IncomingArc_{cell.x}_{cell.y}", payload, cell, floorCubeHeight * 2f, pendingAttackMaterial, floorCubeHeight);

        if (payload.enemyMaxHp > 0 && payload.enemyCurrentHp > 0)
            CreateCube($"Enemy_{payload.enemyNoun}_HP{payload.enemyCurrentHp}", payload, payload.enemyPosition, unitCubeHeight, enemyMaterial, floorCubeHeight + unitCubeHeight * 0.5f);
        if (payload.playerSummoned && payload.playerMaxHp > 0 && payload.playerCurrentHp > 0)
            CreateCube($"Player_HP{payload.playerCurrentHp}", payload, payload.playerPosition, unitCubeHeight, playerMaterial, floorCubeHeight + unitCubeHeight * 0.5f);
    }

    void ClearBoard()
    {
        if (boardRoot == null)
            return;

        Destroy(boardRoot.gameObject);
        boardRoot = null;
    }

    void CreateCube(string name, TacticalBattleScenePayload payload, TacticalBattlePosition position, float height, Material material, float yOffset)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(boardRoot, false);
        cube.transform.localPosition = GridToLocal(payload, position) + Vector3.up * yOffset;
        cube.transform.localScale = new Vector3(cellSize * 0.92f, Mathf.Max(0.02f, height), cellSize * 0.92f);

        Renderer renderer = cube.GetComponent<Renderer>();
        if (renderer != null && material != null)
            renderer.sharedMaterial = material;
    }

    void CreateHexPrism(string name, TacticalBattleScenePayload payload, TacticalBattlePosition position, float height, Material material, float yOffset)
    {
        var tile = new GameObject(name);
        tile.transform.SetParent(boardRoot, false);
        tile.transform.localPosition = GridToLocal(payload, position) + Vector3.up * yOffset;

        MeshFilter meshFilter = tile.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = tile.AddComponent<MeshRenderer>();
        meshFilter.sharedMesh = BuildHexPrismMesh(Mathf.Max(0.02f, height), cellSize * 0.5f);
        if (material != null)
            meshRenderer.sharedMaterial = material;
    }

    Vector3 GridToLocal(TacticalBattleScenePayload payload, TacticalBattlePosition position)
    {
        float horizontalSpacing = cellSize * 0.9f;
        float verticalSpacing = cellSize * 0.78f;
        float originX = -(payload.width - 1) * horizontalSpacing * 0.5f;
        float originZ = -(payload.height - 1) * verticalSpacing * 0.5f;
        float rowOffset = (position.y & 1) != 0 ? horizontalSpacing * 0.5f : 0f;
        return new Vector3(originX + position.x * horizontalSpacing + rowOffset, 0f, originZ + position.y * verticalSpacing);
    }

    Vector3 ResolveBoardOrigin()
    {
        Transform anchor = boardAnchor != null ? boardAnchor : transform;
        return anchor.position + Vector3.up * boardHeight;
    }

    void EnsureCameraAndLight()
    {
        Camera battleCamera = null;
        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
        {
            battleCamera = root.GetComponentInChildren<Camera>(true);
            if (battleCamera != null)
                break;
        }

        if (battleCamera == null)
        {
            var cameraObject = new GameObject("BattleSceneCamera", typeof(Camera), typeof(AudioListener));
            cameraObject.transform.position = cameraPosition;
            cameraObject.transform.rotation = Quaternion.Euler(cameraEulerAngles);
            cameraObject.tag = "MainCamera";
            battleCamera = cameraObject.GetComponent<Camera>();
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, gameObject.scene);
        }

        battleCamera.enabled = true;
        battleCamera.tag = "MainCamera";
        foreach (Camera camera in FindObjectsByType<Camera>(FindObjectsInactive.Exclude))
        {
            if (camera == battleCamera || !camera.enabled || camera.gameObject.scene == gameObject.scene)
                continue;

            camera.enabled = false;
            camerasDisabledForBattle.Add(camera);
        }

        if (FindAnyObjectByType<Light>() == null)
        {
            var lightObject = new GameObject("BattleSceneLight", typeof(Light));
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            Light light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
        }
    }

    void EnsureMaterials()
    {
        floorMaterial ??= CreateMaterial(new Color(0.18f, 0.2f, 0.22f, 1f), "Tactical Floor");
        playerMaterial ??= CreateMaterial(new Color(0.25f, 0.65f, 1f, 1f), "Tactical Player");
        enemyMaterial ??= CreateMaterial(new Color(1f, 0.32f, 0.28f, 1f), "Tactical Enemy");
        selectedCellMaterial ??= CreateMaterial(new Color(0.25f, 0.9f, 1f, 1f), "Tactical Selected Cell");
        movementPreviewMaterial ??= CreateMaterial(new Color(1f, 0.82f, 0.12f, 1f), "Tactical Movement Path");
        attackPreviewMaterial ??= CreateMaterial(new Color(1f, 0.72f, 0.18f, 1f), "Tactical Attack Arc");
        pendingAttackMaterial ??= CreateMaterial(new Color(1f, 0.15f, 0.08f, 1f), "Tactical Incoming Attack");
        EnsureTerrainMaterial(TacticalBattleCellType.Box, new Color(0.58f, 0.38f, 0.18f, 1f));
        EnsureTerrainMaterial(TacticalBattleCellType.Spikes, new Color(0.82f, 0.82f, 0.88f, 1f));
        EnsureTerrainMaterial(TacticalBattleCellType.Wall, new Color(0.46f, 0.48f, 0.52f, 1f));
        EnsureTerrainMaterial(TacticalBattleCellType.Roof, new Color(0.55f, 0.34f, 0.76f, 1f));
        EnsureTerrainMaterial(TacticalBattleCellType.Bridge, new Color(0.72f, 0.52f, 0.28f, 1f));
        EnsureTerrainMaterial(TacticalBattleCellType.Water, new Color(0.08f, 0.42f, 0.9f, 1f));
        EnsureTerrainMaterial(TacticalBattleCellType.Tree, new Color(0.16f, 0.5f, 0.2f, 1f));
        EnsureTerrainMaterial(TacticalBattleCellType.Rock, new Color(0.36f, 0.36f, 0.36f, 1f));
    }

    void EnsureTerrainMaterial(TacticalBattleCellType cellType, Color color)
    {
        if (!terrainMaterials.ContainsKey(cellType))
            terrainMaterials[cellType] = CreateMaterial(color, $"Tactical {cellType}");
    }

    Material ResolveTerrainMaterial(TacticalBattleCellType cellType)
    {
        EnsureMaterials();
        return terrainMaterials.TryGetValue(cellType, out Material material) ? material : floorMaterial;
    }

    static Material CreateMaterial(Color color, string name)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var material = new Material(shader) { name = name };
        material.color = color;
        return material;
    }

    static Mesh BuildHexPrismMesh(float height, float radius)
    {
        const int sides = 6;
        var vertices = new Vector3[sides * 2 + 2];
        var triangles = new List<int>(sides * 12);
        float halfHeight = height * 0.5f;

        vertices[0] = new Vector3(0f, -halfHeight, 0f);
        vertices[1] = new Vector3(0f, halfHeight, 0f);
        for (int i = 0; i < sides; i++)
        {
            float angle = Mathf.PI * 2f * i / sides + Mathf.PI / sides;
            float x = Mathf.Cos(angle) * radius;
            float z = Mathf.Sin(angle) * radius;
            vertices[2 + i] = new Vector3(x, -halfHeight, z);
            vertices[2 + sides + i] = new Vector3(x, halfHeight, z);
        }

        for (int i = 0; i < sides; i++)
        {
            int next = (i + 1) % sides;
            int bottom = 2 + i;
            int bottomNext = 2 + next;
            int top = 2 + sides + i;
            int topNext = 2 + sides + next;
            triangles.Add(0); triangles.Add(bottomNext); triangles.Add(bottom);
            triangles.Add(1); triangles.Add(top); triangles.Add(topNext);
            triangles.Add(bottom); triangles.Add(bottomNext); triangles.Add(topNext);
            triangles.Add(bottom); triangles.Add(topNext); triangles.Add(top);
        }

        var mesh = new Mesh { name = "Tactical Hex Tile" };
        mesh.vertices = vertices;
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
