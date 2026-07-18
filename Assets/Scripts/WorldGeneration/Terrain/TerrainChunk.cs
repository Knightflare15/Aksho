using UnityEngine;

[DisallowMultipleComponent]
public sealed class TerrainChunk : MonoBehaviour
{
    public Vector2Int chunkCoord;
    public MeshFilter meshFilter;
    public MeshRenderer meshRenderer;
    public MeshCollider meshCollider;

    public void Assign(Mesh mesh, Material material)
    {
        meshFilter = meshFilter != null ? meshFilter : gameObject.GetComponent<MeshFilter>();
        if (meshFilter == null)
            meshFilter = gameObject.AddComponent<MeshFilter>();
        meshRenderer = meshRenderer != null ? meshRenderer : gameObject.GetComponent<MeshRenderer>();
        if (meshRenderer == null)
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
        meshCollider = meshCollider != null ? meshCollider : gameObject.GetComponent<MeshCollider>();
        if (meshCollider == null)
            meshCollider = gameObject.AddComponent<MeshCollider>();

        meshFilter.sharedMesh = mesh;
        meshCollider.sharedMesh = mesh;
        if (material != null)
            meshRenderer.sharedMaterial = material;
    }
}
