using System.Collections.Generic;
using UnityEngine;

public static class WorldValidator
{
    static readonly int[] Dx = { 1, -1, 0, 0 };
    static readonly int[] Dz = { 0, 0, 1, -1 };

    public static void BuildReachability(WorldData data)
    {
        System.Array.Clear(data.reachableMask, 0, data.reachableMask.Length);
        ZoneData start = null;
        foreach (ZoneData zone in data.zones)
        {
            if (zone.kind == ZoneKind.Start)
            {
                start = zone;
                break;
            }
        }

        if (start == null)
            return;

        var queue = new Queue<Vector2Int>();
        if (!data.IsWalkable(start.gridPosition.x, start.gridPosition.y))
            return;

        data.reachableMask[data.Index(start.gridPosition.x, start.gridPosition.y)] = 255;
        queue.Enqueue(start.gridPosition);

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            for (int i = 0; i < 4; i++)
            {
                int x = current.x + Dx[i];
                int z = current.y + Dz[i];
                if (!data.InBounds(x, z))
                    continue;
                int index = data.Index(x, z);
                if (data.reachableMask[index] > 0 || !data.IsWalkable(x, z))
                    continue;
                data.reachableMask[index] = 255;
                queue.Enqueue(new Vector2Int(x, z));
            }
        }
    }

    public static bool ValidateGameplay(WorldData data)
    {
        foreach (ObjectivePoint objective in data.objectives)
            if (!WorldPlacementUtility.IsDryPlayableCell(data, objective.gridPosition.x, objective.gridPosition.y, 24f, 0.45f))
                return false;

        foreach (ChestPoint chest in data.chests)
            if (!WorldPlacementUtility.IsDryPlayableCell(data, chest.gridPosition.x, chest.gridPosition.y, 30f, 0.45f))
                return false;

        foreach (EnemyTriggerData trigger in data.enemyTriggers)
            if (!WorldPlacementUtility.IsDryPlayableCell(data, trigger.gridPosition.x, trigger.gridPosition.y, 28f, 0.45f))
                return false;

        foreach (SpawnPointData spawnPoint in data.spawnPoints)
            if (!WorldPlacementUtility.IsDryPlayableCell(data, spawnPoint.gridPosition.x, spawnPoint.gridPosition.y, 24f, 0.45f))
                return false;

        return true;
    }

    public static bool IsReachable(WorldData data, Vector2Int grid)
    {
        return data.InBounds(grid.x, grid.y) && data.reachableMask[data.Index(grid.x, grid.y)] > 0;
    }
}
