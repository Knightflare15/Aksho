using System;
using UnityEngine;

public static class ChestPlacer
{
    public static void PlaceData(WorldData data, GameplayPlacementProfile profile, System.Random rng)
    {
        data.chests.Clear();
        int target = rng.Next(profile.minChests, profile.maxChests + 1);
        for (int i = 0; i < target; i++)
        {
            if (!TryFindChest(data, profile, rng, i, out Vector2Int grid))
                continue;
            data.chests.Add(new ChestPoint
            {
                gridPosition = grid,
                worldPosition = WorldPlacementUtility.GroundedPosition(data, grid.x, grid.y),
                category = ResolveCategory(i),
            });
        }
    }

    public static void Spawn(WorldData data, WorldGenerationProfile profile, Transform parent)
    {
        foreach (ChestPoint chest in data.chests)
        {
            GameObject instance;
            if (profile.treasureChestPrefab != null)
            {
                instance = UnityEngine.Object.Instantiate(profile.treasureChestPrefab, chest.worldPosition, Quaternion.identity, parent);
                WorldPlacementUtility.GroundVisibleBounds(instance.transform, chest.worldPosition.y);
            }
            else
            {
                instance = GameObject.CreatePrimitive(PrimitiveType.Cube);
                instance.name = "GeneratedTreasureChest";
                instance.transform.SetParent(parent, false);
                instance.transform.position = chest.worldPosition + Vector3.up * 0.35f;
                instance.transform.localScale = new Vector3(1.2f, 0.7f, 0.8f);
                Renderer renderer = instance.GetComponent<Renderer>();
                if (renderer != null)
                    renderer.sharedMaterial = null;
            }
            instance.name = $"GeneratedChest_{chest.category}_{chest.gridPosition.x}_{chest.gridPosition.y}";
        }
    }

    static bool TryFindChest(WorldData data, GameplayPlacementProfile profile, System.Random rng, int index, out Vector2Int grid)
    {
        for (int attempt = 0; attempt < 500; attempt++)
        {
            int x = rng.Next(6, data.resolution - 6);
            int z = rng.Next(6, data.resolution - 6);
            if (!WorldPlacementUtility.IsDryPlayableCell(data, x, z, profile.maxChestSlope, 0.65f))
                continue;
            float pathDistance = DistanceFieldBuilder.DistanceToPath(data, x, z, profile.maxChestDistanceFromPath + 10f);
            if (index % 2 == 0)
            {
                if (pathDistance < profile.minChestDistanceFromPath || pathDistance > profile.maxChestDistanceFromPath)
                    continue;
            }
            else if (pathDistance < profile.minChestDistanceFromPath * 2f)
            {
                continue;
            }

            bool tooClose = false;
            foreach (ChestPoint chest in data.chests)
            {
                if (Vector2Int.Distance(chest.gridPosition, new Vector2Int(x, z)) < 8f)
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose)
                continue;

            grid = new Vector2Int(x, z);
            return true;
        }

        grid = default;
        return false;
    }

    static string ResolveCategory(int index)
    {
        switch (index % 5)
        {
            case 0: return "PathSide";
            case 1: return "Hidden";
            case 2: return "HighLedge";
            case 3: return "PillarReward";
            default: return "Ambush";
        }
    }
}
