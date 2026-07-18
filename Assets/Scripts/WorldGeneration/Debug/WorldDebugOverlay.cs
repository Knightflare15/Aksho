using UnityEngine;

[DisallowMultipleComponent]
public sealed class WorldDebugOverlay : MonoBehaviour
{
    public WorldGenerator generator;
    public bool showPath;
    public bool showClearings;
    public bool showCliffs;
    public bool showReachable;

    void OnDrawGizmosSelected()
    {
        if (generator == null)
            generator = GetComponentInParent<WorldGenerator>();
        WorldData data = generator != null ? generator.CurrentWorldData : null;
        if (data == null)
            return;

        for (int z = 0; z < data.resolution; z += 2)
        {
            for (int x = 0; x < data.resolution; x += 2)
            {
                int index = data.Index(x, z);
                Color color = default;
                bool draw = false;
                if (showPath && data.pathMask[index] > 0)
                {
                    color = new Color(0.55f, 0.32f, 0.12f, 0.45f);
                    draw = true;
                }
                else if (showClearings && data.clearingMask[index] > 0)
                {
                    color = new Color(0.2f, 0.9f, 0.25f, 0.35f);
                    draw = true;
                }
                else if (showCliffs && data.cliffMask[index] > 0)
                {
                    color = new Color(0.4f, 0.4f, 0.4f, 0.55f);
                    draw = true;
                }
                else if (showReachable && data.reachableMask[index] > 0)
                {
                    color = new Color(0.1f, 0.45f, 1f, 0.22f);
                    draw = true;
                }

                if (!draw)
                    continue;
                Gizmos.color = color;
                Gizmos.DrawCube(data.GridToWorld(x, z) + Vector3.up * 0.08f, new Vector3(1.5f, 0.05f, 1.5f));
            }
        }
    }
}
