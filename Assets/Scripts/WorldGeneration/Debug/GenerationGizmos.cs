using UnityEngine;

[DisallowMultipleComponent]
public sealed class GenerationGizmos : MonoBehaviour
{
    public WorldGenerator generator;
    public bool drawObjectives;
    public bool drawChests;
    public bool drawTriggers;
    public bool drawSpawnPoints;
    public bool drawReachable;

    void OnDrawGizmosSelected()
    {
        if (generator == null)
            generator = GetComponentInParent<WorldGenerator>();
        WorldData data = generator != null ? generator.CurrentWorldData : null;
        if (data == null)
            return;

        if (drawReachable)
        {
            Gizmos.color = new Color(0.15f, 0.8f, 0.25f, 0.18f);
            for (int z = 0; z < data.resolution; z += 3)
                for (int x = 0; x < data.resolution; x += 3)
                    if (data.reachableMask[data.Index(x, z)] > 0)
                        Gizmos.DrawCube(data.GridToWorld(x, z) + Vector3.up * 0.1f, Vector3.one * 0.35f);
        }

        if (drawObjectives)
        {
            Gizmos.color = Color.yellow;
            foreach (ObjectivePoint point in data.objectives)
                Gizmos.DrawWireSphere(point.worldPosition + Vector3.up * 0.25f, 1.25f);
        }

        if (drawChests)
        {
            Gizmos.color = new Color(1f, 0.55f, 0.1f, 1f);
            foreach (ChestPoint point in data.chests)
                Gizmos.DrawWireCube(point.worldPosition + Vector3.up * 0.35f, Vector3.one * 0.75f);
        }

        if (drawTriggers)
        {
            Gizmos.color = new Color(1f, 0f, 0f, 0.45f);
            foreach (EnemyTriggerData point in data.enemyTriggers)
                Gizmos.DrawWireSphere(point.worldPosition, point.radius);
        }

        if (drawSpawnPoints)
        {
            Gizmos.color = Color.magenta;
            foreach (SpawnPointData point in data.spawnPoints)
                Gizmos.DrawSphere(point.worldPosition + Vector3.up * 0.1f, 0.2f);
        }
    }
}
