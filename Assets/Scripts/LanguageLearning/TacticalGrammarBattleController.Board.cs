using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed partial class TacticalGrammarBattleController : MonoBehaviour
{
    void RefreshBoardVisuals()
    {
        SyncBattleScenePayload();

        // Once the dedicated scene is ready, it owns presentation. The source scene
        // continues to own the state and only renders a local fallback if loading failed.
        if (useDedicatedBattleScene && IsDedicatedBattleSceneLoaded())
        {
            ClearBoardVisuals();
            return;
        }

        if (!renderPlaceholderCubes || !IsActive || State == null)
        {
            ClearBoardVisuals();
            return;
        }

        EnsureMaterials();
        ClearBoardVisuals();
        var root = new GameObject("TacticalGrammarBattle_CubeBoard");
        boardRoot = root.transform;
        boardRoot.SetPositionAndRotation(ResolveBoardOrigin(), Quaternion.identity);

        for (int x = 0; x < State.width; x++)
        {
            for (int y = 0; y < State.height; y++)
            {
                TacticalBattlePosition position = new TacticalBattlePosition(x, y);
                if (showUnderlyingHexGrid)
                    CreateHexPrism($"Tile_{x}_{y}", position, floorCubeHeight, floorMaterial, 0f);

                TacticalBattleCellType cell = State.GetCell(position);
                if (cell != TacticalBattleCellType.Empty)
                    CreateCube($"{cell}_{x}_{y}", position, terrainCubeHeight, ResolveTerrainMaterial(cell), floorCubeHeight + terrainCubeHeight * 0.5f);
            }
        }

        if (State.enemyUnit != null)
            CreateCube($"Enemy_{State.enemyUnit.noun}", State.enemyUnit.position, unitCubeHeight, enemyMaterial, floorCubeHeight + unitCubeHeight * 0.5f);
        if (State.playerUnit != null)
        {
            if (battler.HasSelectedTacticalCell && State.IsInside(battler.SelectedTacticalCell))
                CreateHexPrism($"Selected_{battler.SelectedTacticalCell.x}_{battler.SelectedTacticalCell.y}", battler.SelectedTacticalCell, floorCubeHeight * 1.7f, selectedCellMaterial, floorCubeHeight * 0.8f);
            if (battler.HasSelectedTacticalCell)
            {
                foreach (TacticalBattlePosition movementCell in battler.GetPlayerMovementPreviewCells(TacticalGrammarBattler.MaxMovementCells, "RUN"))
                    CreateHexPrism($"Movement_{movementCell.x}_{movementCell.y}", movementCell, floorCubeHeight * 1.65f, movementPreviewMaterial, floorCubeHeight * 0.82f);
            }
            foreach (TacticalBattlePosition arcCell in battler.GetPlayerAttackArcCells(TacticalGrammarBattler.MinAttackRangeCells))
                CreateHexPrism($"AttackArc_{arcCell.x}_{arcCell.y}", arcCell, floorCubeHeight * 1.6f, attackPreviewMaterial, floorCubeHeight * 0.75f);
        }
        if (pendingEnemyAttack.active && State.enemyUnit != null)
        {
            foreach (TacticalBattlePosition arcCell in battler.GetEnemyAttackArcCells(TacticalGrammarBattler.EnemyAttackRangeCells))
                CreateHexPrism($"IncomingArc_{arcCell.x}_{arcCell.y}", arcCell, floorCubeHeight * 2f, pendingAttackMaterial, floorCubeHeight);
        }
        if (State.playerUnit != null)
            CreateCube($"Player_{State.playerUnit.noun}", State.playerUnit.position, unitCubeHeight, playerMaterial, floorCubeHeight + unitCubeHeight * 0.5f);
    }

    bool IsDedicatedBattleSceneLoaded()
    {
        if (string.IsNullOrWhiteSpace(loadedBattleSceneName))
            return false;

        Scene scene = SceneManager.GetSceneByName(loadedBattleSceneName);
        return scene.isLoaded;
    }

    void SyncBattleScenePayload()
    {
        if (!IsActive || !TacticalBattleSceneTransfer.HasPayload || State == null)
            return;

        TacticalBattleScenePayload payload = TacticalBattleSceneTransfer.CurrentPayload;
        payload.playerSummoned = State.playerUnit != null;
        payload.playerPosition = State.playerUnit != null ? State.playerUnit.position : payload.playerStart;
        payload.enemyPosition = State.enemyUnit != null ? State.enemyUnit.position : payload.enemyStart;
        payload.hasSelectedTacticalCell = battler != null && battler.HasSelectedTacticalCell;
        payload.selectedTacticalCell = battler != null ? battler.SelectedTacticalCell : default;
        payload.playerCurrentHp = State.playerUnit != null ? State.playerUnit.currentHp : 0;
        payload.playerMaxHp = State.playerUnit != null ? State.playerUnit.stats.maxHp : 0;
        payload.enemyCurrentHp = State.enemyUnit != null ? State.enemyUnit.currentHp : 0;
        payload.enemyMaxHp = State.enemyUnit != null ? State.enemyUnit.stats.maxHp : 0;
        payload.pendingEnemyAttack = pendingEnemyAttack.active;
        payload.showDebugGrid = showUnderlyingHexGrid;
        payload.activeCurse = activeTacticalCurse.ToString();

        payload.terrain.Clear();
        for (int x = 0; x < State.width; x++)
        {
            for (int y = 0; y < State.height; y++)
            {
                TacticalBattleCellType cell = State.terrain[x, y];
                if (cell != TacticalBattleCellType.Empty)
                {
                    payload.terrain.Add(new TacticalBattleTerrainPayload
                    {
                        x = x,
                        y = y,
                        cellType = cell,
                    });
                }
            }
        }

        payload.playerAttackArc.Clear();
        if (State.playerUnit != null)
            payload.playerAttackArc.AddRange(battler.GetPlayerAttackArcCells(TacticalGrammarBattler.MinAttackRangeCells));

        payload.playerMovementPath.Clear();
        if (State.playerUnit != null && battler.HasSelectedTacticalCell)
            payload.playerMovementPath.AddRange(battler.GetPlayerMovementPreviewCells(TacticalGrammarBattler.MaxMovementCells, "RUN"));

        payload.enemyAttackArc.Clear();
        if (pendingEnemyAttack.active && State.enemyUnit != null)
            payload.enemyAttackArc.AddRange(battler.GetEnemyAttackArcCells(TacticalGrammarBattler.EnemyAttackRangeCells));

        TacticalBattleSceneTransfer.NotifyPayloadChanged();
    }

    void ClearBoardVisuals()
    {
        if (boardRoot == null)
            return;

        Destroy(boardRoot.gameObject);
        boardRoot = null;
    }

    void CreateCube(string name, TacticalBattlePosition gridPosition, float height, Material material, float yOffset)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(boardRoot, false);
        cube.transform.localPosition = GridToLocal(gridPosition) + Vector3.up * yOffset;
        cube.transform.localScale = new Vector3(cellSize * 0.92f, Mathf.Max(0.02f, height), cellSize * 0.92f);

        Renderer renderer = cube.GetComponent<Renderer>();
        if (renderer != null && material != null)
            renderer.sharedMaterial = material;
    }

    void CreateHexPrism(string name, TacticalBattlePosition gridPosition, float height, Material material, float yOffset)
    {
        var tile = new GameObject(name);
        tile.transform.SetParent(boardRoot, false);
        tile.transform.localPosition = GridToLocal(gridPosition) + Vector3.up * yOffset;

        MeshFilter meshFilter = tile.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = tile.AddComponent<MeshRenderer>();
        meshFilter.sharedMesh = BuildHexPrismMesh(Mathf.Max(0.02f, height), cellSize * 0.5f);
        if (material != null)
            meshRenderer.sharedMaterial = material;
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

            triangles.Add(0);
            triangles.Add(bottomNext);
            triangles.Add(bottom);

            triangles.Add(1);
            triangles.Add(top);
            triangles.Add(topNext);

            triangles.Add(bottom);
            triangles.Add(bottomNext);
            triangles.Add(topNext);
            triangles.Add(bottom);
            triangles.Add(topNext);
            triangles.Add(top);
        }

        var mesh = new Mesh { name = "Tactical Hex Tile" };
        mesh.vertices = vertices;
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    Vector3 GridToLocal(TacticalBattlePosition position)
    {
        float horizontalSpacing = cellSize * 0.9f;
        float verticalSpacing = cellSize * 0.78f;
        float originX = -(State.width - 1) * horizontalSpacing * 0.5f;
        float originZ = -(State.height - 1) * verticalSpacing * 0.5f;
        float rowOffset = (position.y & 1) != 0 ? horizontalSpacing * 0.5f : 0f;
        return new Vector3(originX + position.x * horizontalSpacing + rowOffset, 0f, originZ + position.y * verticalSpacing);
    }

    Vector3 ResolveBoardOrigin()
    {
        Transform anchor = boardAnchor != null ? boardAnchor : transform;
        Vector3 forward = anchor.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.forward;
        return anchor.position + forward.normalized * boardForwardOffset + Vector3.up * boardHeightOffset;
    }

    void EnsureMaterials()
    {
        floorMaterial ??= CreateMaterial(new Color(0.18f, 0.2f, 0.22f, 1f), "Tactical Floor");
        playerMaterial ??= CreateMaterial(new Color(0.25f, 0.65f, 1f, 1f), "Tactical Player");
        enemyMaterial ??= CreateMaterial(new Color(1f, 0.32f, 0.28f, 1f), "Tactical Enemy");
        selectedCellMaterial ??= CreateMaterial(new Color(0.25f, 0.9f, 1f, 0.82f), "Tactical Selected Cell");
        movementPreviewMaterial ??= CreateMaterial(new Color(1f, 0.82f, 0.12f, 0.76f), "Tactical Movement Path");
        attackPreviewMaterial ??= CreateMaterial(new Color(1f, 0.72f, 0.18f, 0.72f), "Tactical Attack Arc");
        pendingAttackMaterial ??= CreateMaterial(new Color(1f, 0.15f, 0.08f, 0.72f), "Tactical Incoming Attack");
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
}
