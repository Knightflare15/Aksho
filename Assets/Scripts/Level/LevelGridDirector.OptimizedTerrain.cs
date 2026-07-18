using System.Collections.Generic;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

#if UNITY_EDITOR
using UnityEditor;
#endif


public sealed partial class LevelGridDirector
{
    void RebuildOptimizedTerrainMeshes()
    {
        ClearOptimizedTerrainMeshes();
        EnsurePaintedCellCache();

        Transform root = EnsureOptimizedTerrainRoot();
        if (paintedCells.Count == 0)
        {
            CreateWaterPlane(root);
            return;
        }

        int chunkSize = Mathf.Clamp(terrainChunkSizeCells, 4, 64);
        int chunkIndex = 0;

        for (int chunkZ = 0; chunkZ < GridHeight; chunkZ += chunkSize)
        {
            for (int chunkX = 0; chunkX < GridWidth; chunkX += chunkSize)
            {
                TerrainChunkMeshBuilder builder = new TerrainChunkMeshBuilder();
                int maxX = Mathf.Min(GridWidth, chunkX + chunkSize);
                int maxZ = Mathf.Min(GridHeight, chunkZ + chunkSize);

                for (int z = chunkZ; z < maxZ; z++)
                {
                    for (int x = chunkX; x < maxX; x++)
                        AddTerrainCellFaces(builder, new Vector2Int(x, z));
                }

                if (!builder.HasGeometry)
                    continue;

                CreateOptimizedTerrainChunk(root, builder, chunkIndex, chunkX, chunkZ);
                chunkIndex++;
            }
        }

        CreateWaterPlane(root);
    }

    void CreateOptimizedTerrainChunk(Transform root, TerrainChunkMeshBuilder builder, int chunkIndex, int chunkX, int chunkZ)
    {
        GameObject chunkObject = new GameObject($"TerrainChunk_{chunkIndex:000}_{chunkX:000}_{chunkZ:000}");
#if UNITY_EDITOR
        if (!Application.isPlaying)
            Undo.RegisterCreatedObjectUndo(chunkObject, "Create Optimized Terrain Chunk");
#endif
        chunkObject.transform.SetParent(root, false);
        chunkObject.isStatic = true;

        Mesh mesh = new Mesh
        {
            name = chunkObject.name
        };

        if (builder.vertices.Count > 65000)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        if (weldOptimizedTerrainVertices)
            WeldOptimizedTerrainMeshBuilder(builder);

        mesh.SetVertices(builder.vertices);
        if (builder.colors.Count == builder.vertices.Count)
            mesh.SetColors(builder.colors);
        mesh.subMeshCount = (int)TerrainMeshMaterialSlot.Count;
        for (int i = 0; i < builder.triangles.Length; i++)
            mesh.SetTriangles(builder.triangles[i], i);

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        MeshFilter meshFilter = chunkObject.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = mesh;

        MeshRenderer meshRenderer = chunkObject.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterials = GetOptimizedTerrainMaterials();

        MeshCollider meshCollider = chunkObject.AddComponent<MeshCollider>();
        meshCollider.sharedMesh = mesh;

        CreateOptimizedTerrainWireOverlay(chunkObject.transform, mesh);
    }

    void CreateOptimizedTerrainWireOverlay(Transform chunkTransform, Mesh sourceMesh)
    {
        if (!drawOptimizedTerrainWireOverlay || chunkTransform == null || sourceMesh == null)
            return;

        int vertexCount = sourceMesh.vertexCount;
        if (vertexCount <= 0)
            return;

        HashSet<ulong> edges = new HashSet<ulong>();
        List<int> lineIndices = new List<int>();
        for (int submesh = 0; submesh < sourceMesh.subMeshCount; submesh++)
        {
            int[] triangles = sourceMesh.GetTriangles(submesh);
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                AddWireEdge(edges, lineIndices, triangles[i], triangles[i + 1], vertexCount);
                AddWireEdge(edges, lineIndices, triangles[i + 1], triangles[i + 2], vertexCount);
                AddWireEdge(edges, lineIndices, triangles[i + 2], triangles[i], vertexCount);
            }
        }

        if (lineIndices.Count == 0)
            return;

        GameObject wireObject = new GameObject("WireOverlay");
#if UNITY_EDITOR
        if (!Application.isPlaying)
            Undo.RegisterCreatedObjectUndo(wireObject, "Create Optimized Terrain Wire Overlay");
#endif
        wireObject.transform.SetParent(chunkTransform, false);
        wireObject.transform.localPosition = Vector3.up * optimizedTerrainWireYOffset;
        wireObject.transform.localRotation = Quaternion.identity;
        wireObject.transform.localScale = Vector3.one;

        Mesh wireMesh = new Mesh
        {
            name = $"{sourceMesh.name}_WireOverlay"
        };
        if (vertexCount > 65000)
            wireMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        wireMesh.SetVertices(sourceMesh.vertices);
        wireMesh.SetIndices(lineIndices, MeshTopology.Lines, 0);
        wireMesh.RecalculateBounds();

        MeshFilter wireFilter = wireObject.AddComponent<MeshFilter>();
        wireFilter.sharedMesh = wireMesh;

        MeshRenderer wireRenderer = wireObject.AddComponent<MeshRenderer>();
        wireRenderer.sharedMaterial = GetOptimizedTerrainWireMaterial();
        wireRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        wireRenderer.receiveShadows = false;
    }

    static void AddWireEdge(HashSet<ulong> edges, List<int> lineIndices, int a, int b, int vertexCount)
    {
        if (a < 0 || b < 0 || a >= vertexCount || b >= vertexCount || a == b)
            return;

        ulong edgeKey = MakeTerrainEdgeKey(a, b);
        if (!edges.Add(edgeKey))
            return;

        lineIndices.Add(a);
        lineIndices.Add(b);
    }

    void WeldOptimizedTerrainMeshBuilder(TerrainChunkMeshBuilder builder)
    {
        float weldDistance = Mathf.Max(0.001f, optimizedTerrainVertexWeldDistance);
        float weldDistanceSquared = weldDistance * weldDistance;
        int[] vertexEdgeCounts = BuildTerrainVertexEdgeCounts(builder);
        Dictionary<Vector3Int, List<int>> bucketToVertexIndices = new Dictionary<Vector3Int, List<int>>();
        List<Vector3> weldedVertices = new List<Vector3>(builder.vertices.Count);
        List<Color> weldedColors = new List<Color>(builder.colors.Count);
        List<int> weldedEdgeCounts = new List<int>(builder.vertices.Count);
        int[] remap = new int[builder.vertices.Count];

        for (int i = 0; i < builder.vertices.Count; i++)
        {
            Vector3 vertex = builder.vertices[i];
            Vector3Int bucket = QuantizeTerrainWeldBucket(vertex, weldDistance);
            int weldedIndex = -1;
            bool canWeldVertex = CanWeldTerrainVertex(i, vertexEdgeCounts);

            for (int z = -1; z <= 1 && weldedIndex < 0 && canWeldVertex; z++)
            {
                for (int y = -1; y <= 1 && weldedIndex < 0; y++)
                {
                    for (int x = -1; x <= 1 && weldedIndex < 0; x++)
                    {
                        Vector3Int nearbyBucket = new Vector3Int(bucket.x + x, bucket.y + y, bucket.z + z);
                        if (!bucketToVertexIndices.TryGetValue(nearbyBucket, out List<int> candidates))
                            continue;

                        for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
                        {
                            int candidate = candidates[candidateIndex];
                            if (weldOnlySimpleTerrainVertices &&
                                weldedEdgeCounts[candidate] > terrainVertexWeldMaxEdgeCount)
                                continue;

                            if ((weldedVertices[candidate] - vertex).sqrMagnitude > weldDistanceSquared)
                                continue;

                            weldedIndex = candidate;
                            break;
                        }
                    }
                }
            }

            if (weldedIndex < 0)
            {
                weldedIndex = weldedVertices.Count;
                weldedVertices.Add(vertex);
                weldedColors.Add(i < builder.colors.Count ? builder.colors[i] : EncodeSurfaceBlendWeight(0f));
                weldedEdgeCounts.Add(i < vertexEdgeCounts.Length ? vertexEdgeCounts[i] : 0);
                if (!bucketToVertexIndices.TryGetValue(bucket, out List<int> verticesInBucket))
                {
                    verticesInBucket = new List<int>();
                    bucketToVertexIndices[bucket] = verticesInBucket;
                }

                verticesInBucket.Add(weldedIndex);
            }

            remap[i] = weldedIndex;
        }

        builder.vertices.Clear();
        builder.vertices.AddRange(weldedVertices);
        builder.colors.Clear();
        builder.colors.AddRange(weldedColors);

        for (int submesh = 0; submesh < builder.triangles.Length; submesh++)
        {
            List<int> triangles = builder.triangles[submesh];
            for (int i = 0; i < triangles.Count; i++)
                triangles[i] = remap[triangles[i]];
        }
    }

    bool CanWeldTerrainVertex(int vertexIndex, int[] vertexEdgeCounts)
    {
        if (!weldOnlySimpleTerrainVertices)
            return true;

        if (vertexIndex < 0 || vertexIndex >= vertexEdgeCounts.Length)
            return false;

        return vertexEdgeCounts[vertexIndex] <= terrainVertexWeldMaxEdgeCount;
    }

    int[] BuildTerrainVertexEdgeCounts(TerrainChunkMeshBuilder builder)
    {
        HashSet<ulong>[] edgesByVertex = new HashSet<ulong>[builder.vertices.Count];
        for (int submesh = 0; submesh < builder.triangles.Length; submesh++)
        {
            List<int> triangles = builder.triangles[submesh];
            for (int i = 0; i + 2 < triangles.Count; i += 3)
            {
                AddTerrainVertexEdge(edgesByVertex, triangles[i], triangles[i + 1]);
                AddTerrainVertexEdge(edgesByVertex, triangles[i + 1], triangles[i + 2]);
                AddTerrainVertexEdge(edgesByVertex, triangles[i + 2], triangles[i]);
            }
        }

        int[] edgeCounts = new int[builder.vertices.Count];
        for (int i = 0; i < edgesByVertex.Length; i++)
            edgeCounts[i] = edgesByVertex[i]?.Count ?? 0;

        return edgeCounts;
    }

    static void AddTerrainVertexEdge(HashSet<ulong>[] edgesByVertex, int a, int b)
    {
        if (a < 0 || b < 0 || a >= edgesByVertex.Length || b >= edgesByVertex.Length || a == b)
            return;

        ulong edgeKey = MakeTerrainEdgeKey(a, b);
        if (edgesByVertex[a] == null)
            edgesByVertex[a] = new HashSet<ulong>();
        if (edgesByVertex[b] == null)
            edgesByVertex[b] = new HashSet<ulong>();

        edgesByVertex[a].Add(edgeKey);
        edgesByVertex[b].Add(edgeKey);
    }

    static ulong MakeTerrainEdgeKey(int a, int b)
    {
        uint min = (uint)Mathf.Min(a, b);
        uint max = (uint)Mathf.Max(a, b);
        return ((ulong)min << 32) | max;
    }

    static Vector3Int QuantizeTerrainWeldBucket(Vector3 vertex, float weldDistance)
    {
        return new Vector3Int(
            Mathf.FloorToInt(vertex.x / weldDistance),
            Mathf.FloorToInt(vertex.y / weldDistance),
            Mathf.FloorToInt(vertex.z / weldDistance));
    }

    void CreateWaterPlane(Transform root)
    {
        if (!createWaterPlane || root == null)
            return;

        GameObject waterObject = new GameObject(WaterPlaneName);
#if UNITY_EDITOR
        if (!Application.isPlaying)
            Undo.RegisterCreatedObjectUndo(waterObject, "Create Water Plane");
#endif
        waterObject.transform.SetParent(root, false);

        float halfWidth = WorldSize.x * 0.5f;
        float halfHeight = WorldSize.y * 0.5f;
        float waterY = GridPlaneYOffset + waterPlaneHeightCells * PaintedCellHeight;

        int xSegments = Mathf.Max(1, GridWidth);
        int zSegments = Mathf.Max(1, GridHeight);
        List<Vector3> vertices = new List<Vector3>((xSegments + 1) * (zSegments + 1));
        List<Color> colors = new List<Color>((xSegments + 1) * (zSegments + 1));
        List<int> triangles = new List<int>(xSegments * zSegments * 6);

        for (int z = 0; z <= zSegments; z++)
        {
            float gridZ = z / (float)zSegments * GridHeight;
            float localZ = gridZ * CellSize - halfHeight;
            for (int x = 0; x <= xSegments; x++)
            {
                float gridX = x / (float)xSegments * GridWidth;
                float localX = gridX * CellSize - halfWidth;
                vertices.Add(new Vector3(localX, waterY, localZ));
                colors.Add(EncodeSurfaceBlendWeight(CalculateWaterShorelineFoamWeight(gridX, gridZ)));
            }
        }

        for (int z = 0; z < zSegments; z++)
        {
            for (int x = 0; x < xSegments; x++)
            {
                int row = z * (xSegments + 1);
                int nextRow = (z + 1) * (xSegments + 1);
                int v0 = row + x;
                int v1 = nextRow + x;
                int v2 = nextRow + x + 1;
                int v3 = row + x + 1;
                triangles.Add(v0);
                triangles.Add(v1);
                triangles.Add(v2);
                triangles.Add(v0);
                triangles.Add(v2);
                triangles.Add(v3);
            }
        }

        Mesh mesh = new Mesh
        {
            name = WaterPlaneName
        };
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        MeshFilter meshFilter = waterObject.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = mesh;

        MeshRenderer meshRenderer = waterObject.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = GetWaterPlaneMaterial();

        CreateShoreFoamMesh(root, waterY);
    }

    void CreateShoreFoamMesh(Transform root, float waterY)
    {
        if (root == null || paintedCells.Count == 0)
            return;

        List<Vector3> vertices = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<Color> colors = new List<Color>();
        List<int> triangles = new List<int>();
        float stripWidth = Mathf.Max(0.05f, shoreFoamStripWidthCells) * CellSize;
        float foamY = waterY + Mathf.Max(0.01f, optimizedTerrainWireYOffset + 0.01f);

        for (int z = 0; z < GridHeight; z++)
        {
            for (int x = 0; x < GridWidth; x++)
            {
                Vector2Int cell = new Vector2Int(x, z);
                if (GetCellHeight(cell) <= 0)
                    continue;

                AddShoreFoamEdge(vertices, uvs, colors, triangles, cell, Vector2Int.right, foamY, stripWidth);
                AddShoreFoamEdge(vertices, uvs, colors, triangles, cell, Vector2Int.left, foamY, stripWidth);
                AddShoreFoamEdge(vertices, uvs, colors, triangles, cell, Vector2Int.up, foamY, stripWidth);
                AddShoreFoamEdge(vertices, uvs, colors, triangles, cell, Vector2Int.down, foamY, stripWidth);
            }
        }

        if (vertices.Count == 0)
            return;

        GameObject foamObject = new GameObject(ShoreFoamName);
#if UNITY_EDITOR
        if (!Application.isPlaying)
            Undo.RegisterCreatedObjectUndo(foamObject, "Create Shore Foam");
#endif
        foamObject.transform.SetParent(root, false);

        Mesh mesh = new Mesh
        {
            name = ShoreFoamName
        };
        if (vertices.Count > 65000)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        MeshFilter meshFilter = foamObject.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = mesh;

        MeshRenderer meshRenderer = foamObject.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = GetShoreFoamMaterial();
    }

    void AddShoreFoamEdge(
        List<Vector3> vertices,
        List<Vector2> uvs,
        List<Color> colors,
        List<int> triangles,
        Vector2Int cell,
        Vector2Int direction,
        float foamY,
        float stripWidth)
    {
        if (IsSurfaceNetLandCell(cell + direction))
            return;

        float minX = cell.x * CellSize - WorldSize.x * 0.5f;
        float maxX = minX + CellSize;
        float minZ = cell.y * CellSize - WorldSize.y * 0.5f;
        float maxZ = minZ + CellSize;
        Vector3 edge0;
        Vector3 edge1;
        Vector3 outward = new Vector3(direction.x, 0f, direction.y);

        if (direction.x > 0)
        {
            edge0 = new Vector3(maxX, foamY, minZ);
            edge1 = new Vector3(maxX, foamY, maxZ);
        }
        else if (direction.x < 0)
        {
            edge0 = new Vector3(minX, foamY, maxZ);
            edge1 = new Vector3(minX, foamY, minZ);
        }
        else if (direction.y > 0)
        {
            edge0 = new Vector3(maxX, foamY, maxZ);
            edge1 = new Vector3(minX, foamY, maxZ);
        }
        else
        {
            edge0 = new Vector3(minX, foamY, minZ);
            edge1 = new Vector3(maxX, foamY, minZ);
        }

        Vector3 outer0 = edge0 + outward * stripWidth;
        Vector3 outer1 = edge1 + outward * stripWidth;
        float phase = Mathf.Repeat((cell.x * 0.37f + cell.y * 0.61f + direction.x * 0.19f + direction.y * 0.23f), 1f);
        AddShoreFoamQuad(vertices, uvs, colors, triangles, edge0, edge1, outer1, outer0, phase);
    }

    static void AddShoreFoamQuad(
        List<Vector3> vertices,
        List<Vector2> uvs,
        List<Color> colors,
        List<int> triangles,
        Vector3 inner0,
        Vector3 inner1,
        Vector3 outer1,
        Vector3 outer0,
        float phase)
    {
        int start = vertices.Count;
        float edgeLength = Vector3.Distance(inner0, inner1);
        vertices.Add(inner0);
        vertices.Add(inner1);
        vertices.Add(outer1);
        vertices.Add(outer0);
        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(edgeLength, 0f));
        uvs.Add(new Vector2(edgeLength, 1f));
        uvs.Add(new Vector2(0f, 1f));
        colors.Add(new Color(phase, 1f, 1f, 1f));
        colors.Add(new Color(phase, 1f, 1f, 1f));
        colors.Add(new Color(phase, 1f, 1f, 0f));
        colors.Add(new Color(phase, 1f, 1f, 0f));
        triangles.Add(start);
        triangles.Add(start + 1);
        triangles.Add(start + 2);
        triangles.Add(start);
        triangles.Add(start + 2);
        triangles.Add(start + 3);
    }

    float CalculateWaterShorelineFoamWeight(float gridX, float gridZ)
    {
        float width = Mathf.Max(0.25f, waterShorelineFoamWidthCells);
        int radius = Mathf.Max(1, Mathf.CeilToInt(width));
        int centerX = Mathf.FloorToInt(gridX);
        int centerZ = Mathf.FloorToInt(gridZ);
        float strongest = 0f;

        for (int z = centerZ - radius; z <= centerZ + radius; z++)
        {
            for (int x = centerX - radius; x <= centerX + radius; x++)
            {
                Vector2Int cell = new Vector2Int(x, z);
                if (!IsShorelineLandCell(cell))
                    continue;

                float distance = DistanceToGridCellRect(gridX, gridZ, x, z);
                float influence = Mathf.Clamp01(1f - distance / width);
                strongest = Mathf.Max(strongest, influence * influence);
            }
        }

        return strongest;
    }

    bool IsShorelineLandCell(Vector2Int cell)
    {
        if (!ContainsCell(cell.x, cell.y) || GetCellHeight(cell) <= 0)
            return false;

        return !IsSurfaceNetLandCell(cell + Vector2Int.right) ||
            !IsSurfaceNetLandCell(cell + Vector2Int.left) ||
            !IsSurfaceNetLandCell(cell + Vector2Int.up) ||
            !IsSurfaceNetLandCell(cell + Vector2Int.down);
    }

    static float DistanceToGridCellRect(float gridX, float gridZ, int cellX, int cellZ)
    {
        float dx = Mathf.Max(Mathf.Max(cellX - gridX, 0f), gridX - (cellX + 1f));
        float dz = Mathf.Max(Mathf.Max(cellZ - gridZ, 0f), gridZ - (cellZ + 1f));
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
}
