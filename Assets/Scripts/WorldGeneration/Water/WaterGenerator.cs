using UnityEngine;

public static class WaterGenerator
{
    public static void Generate(WorldData data, WorldGenerationProfile profile, Transform parent)
    {
        if (profile.waterPrefab != null)
        {
            GameObject instance = Object.Instantiate(profile.waterPrefab, parent);
            instance.name = "GeneratedWater";
            instance.transform.position = new Vector3(0f, profile.waterLevel, 0f);
            return;
        }

        var water = new GameObject("GeneratedWaterPlane");
        water.transform.SetParent(parent, false);
        MeshFilter filter = water.AddComponent<MeshFilter>();
        MeshRenderer renderer = water.AddComponent<MeshRenderer>();

        float half = data.worldSizeMeters * 0.5f;
        var mesh = new Mesh { name = "GeneratedWaterPlane" };
        mesh.vertices = new[]
        {
            new Vector3(-half, profile.waterLevel, -half),
            new Vector3(half, profile.waterLevel, -half),
            new Vector3(-half, profile.waterLevel, half),
            new Vector3(half, profile.waterLevel, half),
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
        };
        mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        filter.sharedMesh = mesh;
        renderer.sharedMaterial = profile.waterMaterial;
    }
}
