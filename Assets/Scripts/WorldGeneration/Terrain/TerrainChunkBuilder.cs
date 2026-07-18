using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static class TerrainChunkBuilder
{
    public static List<TerrainChunk> Build(WorldData data, WorldGenerationProfile profile, Transform parent, Material material)
    {
        var chunks = new List<TerrainChunk>();
        int chunkSize = Mathf.Max(4, Mathf.RoundToInt(profile.chunkSizeMeters / data.gridSpacing));
        int chunkCount = Mathf.CeilToInt((data.resolution - 1) / (float)chunkSize);

        for (int cz = 0; cz < chunkCount; cz++)
        {
            for (int cx = 0; cx < chunkCount; cx++)
            {
                int startX = cx * chunkSize;
                int startZ = cz * chunkSize;
                int endX = Mathf.Min(startX + chunkSize, data.resolution - 1);
                int endZ = Mathf.Min(startZ + chunkSize, data.resolution - 1);
                Mesh mesh = BuildMesh(data, profile, startX, startZ, endX, endZ);
                var chunkObject = new GameObject($"TerrainChunk_{cx}_{cz}");
                chunkObject.transform.SetParent(parent, false);
                var chunk = chunkObject.AddComponent<TerrainChunk>();
                chunk.chunkCoord = new Vector2Int(cx, cz);
                chunk.Assign(mesh, material);
                chunks.Add(chunk);
            }
        }

        return chunks;
    }

    static Mesh BuildMesh(WorldData data, WorldGenerationProfile profile, int startX, int startZ, int endX, int endZ)
    {
        int subdivisions = Mathf.Clamp(profile.terrainVisualSubdivisions, 1, 4);
        int cellsX = endX - startX;
        int cellsZ = endZ - startZ;
        int width = cellsX * subdivisions + 1;
        int depth = cellsZ * subdivisions + 1;
        var vertices = new Vector3[width * depth];
        var normals = new Vector3[vertices.Length];
        var colors = new Color[vertices.Length];
        var triangles = new int[(width - 1) * (depth - 1) * 6];

        int v = 0;
        float half = data.worldSizeMeters * 0.5f;
        for (int vz = 0; vz < depth; vz++)
        {
            float gridZ = startZ + vz / (float)subdivisions;
            for (int vx = 0; vx < width; vx++)
            {
                float gridX = startX + vx / (float)subdivisions;
                float height = SampleHeight(data, gridX, gridZ);
                vertices[v] = new Vector3(gridX * data.gridSpacing - half, height, gridZ * data.gridSpacing - half);
                normals[v] = SampleNormal(data, gridX, gridZ);
                colors[v] = SampleColor(data, gridX, gridZ);
                v++;
            }
        }

        int t = 0;
        for (int z = 0; z < depth - 1; z++)
        {
            for (int x = 0; x < width - 1; x++)
            {
                int a = z * width + x;
                int b = a + 1;
                int c = a + width;
                int d = c + 1;
                triangles[t++] = a;
                triangles[t++] = c;
                triangles[t++] = b;
                triangles[t++] = b;
                triangles[t++] = c;
                triangles[t++] = d;
            }
        }

        var mesh = new Mesh { name = $"GeneratedTerrain_{startX}_{startZ}" };
        mesh.indexFormat = vertices.Length <= 65535 ? IndexFormat.UInt16 : IndexFormat.UInt32;
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.colors = colors;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    static float SampleHeight(WorldData data, float x, float z)
    {
        int x0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, data.resolution - 1);
        int z0 = Mathf.Clamp(Mathf.FloorToInt(z), 0, data.resolution - 1);
        int x1 = Mathf.Clamp(x0 + 1, 0, data.resolution - 1);
        int z1 = Mathf.Clamp(z0 + 1, 0, data.resolution - 1);
        float tx = Mathf.Clamp01(x - x0);
        float tz = Mathf.Clamp01(z - z0);
        float a = Mathf.Lerp(data.height[data.Index(x0, z0)], data.height[data.Index(x1, z0)], tx);
        float b = Mathf.Lerp(data.height[data.Index(x0, z1)], data.height[data.Index(x1, z1)], tx);
        return Mathf.Lerp(a, b, tz);
    }

    static Vector3 SampleNormal(WorldData data, float x, float z)
    {
        int x0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, data.resolution - 1);
        int z0 = Mathf.Clamp(Mathf.FloorToInt(z), 0, data.resolution - 1);
        int x1 = Mathf.Clamp(x0 + 1, 0, data.resolution - 1);
        int z1 = Mathf.Clamp(z0 + 1, 0, data.resolution - 1);
        float tx = Mathf.Clamp01(x - x0);
        float tz = Mathf.Clamp01(z - z0);
        Vector3 a = Vector3.Lerp(data.normals[data.Index(x0, z0)], data.normals[data.Index(x1, z0)], tx);
        Vector3 b = Vector3.Lerp(data.normals[data.Index(x0, z1)], data.normals[data.Index(x1, z1)], tx);
        Vector3 normal = Vector3.Lerp(a, b, tz).normalized;
        return normal.sqrMagnitude < 0.01f ? Vector3.up : normal;
    }

    static Color SampleColor(WorldData data, float x, float z)
    {
        int x0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, data.resolution - 1);
        int z0 = Mathf.Clamp(Mathf.FloorToInt(z), 0, data.resolution - 1);
        int x1 = Mathf.Clamp(x0 + 1, 0, data.resolution - 1);
        int z1 = Mathf.Clamp(z0 + 1, 0, data.resolution - 1);
        float tx = Mathf.Clamp01(x - x0);
        float tz = Mathf.Clamp01(z - z0);
        Color c00 = CellColor(data, x0, z0);
        Color c10 = CellColor(data, x1, z0);
        Color c01 = CellColor(data, x0, z1);
        Color c11 = CellColor(data, x1, z1);
        Color a = Color.Lerp(c00, c10, tx);
        Color b = Color.Lerp(c01, c11, tx);
        return Color.Lerp(a, b, tz);
    }

    static Color CellColor(WorldData data, int x, int z)
    {
        int index = data.Index(x, z);
        return new Color(
            data.grassMask[index] / 255f,
            data.dirtMask[index] / 255f,
            data.rockMask[index] / 255f,
            data.sandMask[index] / 255f);
    }
}
