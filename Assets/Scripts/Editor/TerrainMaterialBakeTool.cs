using System.IO;
using UnityEditor;
using UnityEngine;

public sealed class TerrainMaterialBakeTool : EditorWindow
{
    const string TerrainShaderName = "The Script/WorldGeneration/Mobile Terrain Blend";
    const string DefaultMaterialPath = "Assets/MyMaterials/MobileTerrainBlend.mat";
    const string OutputFolder = "Assets/Generated/TerrainBakes";

    [SerializeField] float waterLevel;
    [SerializeField] float shoreWidth = 0.35f;
    [SerializeField] float dirtSlopeAngle = 24f;
    [SerializeField] float cliffSlopeAngle = 48f;
    [SerializeField, Range(0f, 0.75f)] float flatDirtCoverage;
    [SerializeField] float flatDirtNoiseScale = 0.032f;
    [SerializeField, Range(0f, 1f)] float shoreDirtBlend = 0.18f;
    [SerializeField] bool splitCliffFaces;
    [SerializeField] Material terrainMaterial;

    [MenuItem("The Script/World Generation/Terrain Material Baker", false, 60)]
    [MenuItem("Assets/Create/The Script/World Generation/Terrain Material Baker", false, 90)]
    [MenuItem("GameObject/The Script/World Generation/Terrain Material Baker", false, 11)]
    public static void Open()
    {
        GetWindow<TerrainMaterialBakeTool>("Terrain Material Baker");
    }

    void OnEnable()
    {
        if (terrainMaterial == null)
            terrainMaterial = AssetDatabase.LoadAssetAtPath<Material>(DefaultMaterialPath);
        EnsureTerrainMaterialShader();
    }

    void OnSelectionChange()
    {
        Repaint();
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("Selected Mesh", EditorStyles.boldLabel);
        MeshFilter selectedFilter = GetSelectedMeshFilter();
        Mesh sourceMesh = selectedFilter != null ? selectedFilter.sharedMesh : null;

        if (selectedFilter == null || sourceMesh == null)
        {
            EditorGUILayout.HelpBox("Select a scene object with a MeshFilter to bake terrain vertex colors.", MessageType.Info);
        }
        else
        {
            EditorGUILayout.LabelField("Object", selectedFilter.gameObject.name);
            EditorGUILayout.LabelField("Source Mesh", sourceMesh.name);
            EditorGUILayout.LabelField("Vertices", sourceMesh.vertexCount.ToString());
            EditorGUILayout.LabelField("Submeshes", sourceMesh.subMeshCount.ToString());
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Classification", EditorStyles.boldLabel);
        waterLevel = EditorGUILayout.FloatField("Water Level", waterLevel);
        using (new EditorGUI.DisabledScope(selectedFilter == null || sourceMesh == null))
        {
            if (GUILayout.Button("Estimate Water Level From Selected Mesh"))
                waterLevel = EstimateWaterLevel(selectedFilter, sourceMesh);
        }
        shoreWidth = EditorGUILayout.Slider("Shore Width", shoreWidth, 0f, 8f);
        dirtSlopeAngle = EditorGUILayout.Slider("Dirt Slope Angle", dirtSlopeAngle, 0f, 89f);
        cliffSlopeAngle = EditorGUILayout.Slider("Cliff Slope Angle", cliffSlopeAngle, 1f, 89f);
        if (cliffSlopeAngle < dirtSlopeAngle)
            cliffSlopeAngle = dirtSlopeAngle;
        flatDirtCoverage = EditorGUILayout.Slider("Flat Dirt Coverage", flatDirtCoverage, 0f, 0.75f);
        flatDirtNoiseScale = EditorGUILayout.Slider("Flat Dirt Noise Scale", flatDirtNoiseScale, 0.005f, 0.35f);
        shoreDirtBlend = EditorGUILayout.Slider("Shore Dirt Blend", shoreDirtBlend, 0f, 1f);
        splitCliffFaces = EditorGUILayout.Toggle("Split Cliff Vertices", splitCliffFaces);
        if (GUILayout.Button("Reset To Clean Test Defaults"))
            ResetBakeDefaults();

        terrainMaterial = (Material)EditorGUILayout.ObjectField("Terrain Material", terrainMaterial, typeof(Material), false);
        EnsureTerrainMaterialShader();

        EditorGUILayout.Space();
        if (terrainMaterial != null && terrainMaterial.shader != null)
            EditorGUILayout.LabelField("Shader", terrainMaterial.shader.name);
        EditorGUILayout.HelpBox("Bakes vertex colors as R=Grass, G=Dirt, B=Cliff/Rock, A=Shore/Sand. Leave Split Cliff Vertices off for clean shader testing; the shader now detects steep cliffs from normals.", MessageType.None);

        using (new EditorGUI.DisabledScope(selectedFilter == null || sourceMesh == null))
        {
            if (GUILayout.Button("Bake Selected Mesh"))
                BakeSelectedMesh(selectedFilter, sourceMesh);
        }
    }

    MeshFilter GetSelectedMeshFilter()
    {
        GameObject selected = Selection.activeGameObject;
        return selected != null ? selected.GetComponent<MeshFilter>() : null;
    }

    void BakeSelectedMesh(MeshFilter filter, Mesh sourceMesh)
    {
        EnsureOutputFolder();

        Mesh bakedMesh = splitCliffFaces
            ? BuildCliffSplitMesh(sourceMesh, filter.transform)
            : Object.Instantiate(sourceMesh);
        bakedMesh.name = $"{SanitizeFileName(sourceMesh.name)}_TerrainBake";
        if (!splitCliffFaces)
            bakedMesh.colors = BuildVertexColors(sourceMesh, filter.transform);
        bakedMesh.RecalculateBounds();

        string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{OutputFolder}/{bakedMesh.name}.asset");
        AssetDatabase.CreateAsset(bakedMesh, assetPath);
        AssetDatabase.SaveAssets();

        Undo.RecordObject(filter, "Bake Terrain Material Mesh");
        Mesh previousMesh = filter.sharedMesh;
        filter.sharedMesh = bakedMesh;
        EditorUtility.SetDirty(filter);

        MeshCollider meshCollider = filter.GetComponent<MeshCollider>();
        if (meshCollider != null && meshCollider.sharedMesh == previousMesh)
        {
            Undo.RecordObject(meshCollider, "Bake Terrain Material Mesh Collider");
            meshCollider.sharedMesh = bakedMesh;
            EditorUtility.SetDirty(meshCollider);
        }

        MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
        if (renderer != null && terrainMaterial != null)
        {
            EnsureTerrainMaterialShader();
            Undo.RecordObject(renderer, "Assign Terrain Material");
            renderer.sharedMaterial = terrainMaterial;
            EditorUtility.SetDirty(renderer);
        }

        EditorGUIUtility.PingObject(bakedMesh);
        Debug.Log($"Baked terrain vertex colors to {assetPath}", bakedMesh);
    }

    Color[] BuildVertexColors(Mesh mesh, Transform meshTransform)
    {
        Vector3[] vertices = mesh.vertices;
        Vector4[] accumulated = new Vector4[vertices.Length];

        for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
        {
            int[] triangles = mesh.GetTriangles(submesh);
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int a = triangles[i];
                int b = triangles[i + 1];
                int c = triangles[i + 2];
                if (!IsValidIndex(vertices, a) || !IsValidIndex(vertices, b) || !IsValidIndex(vertices, c))
                    continue;

                Vector3 worldA = meshTransform.TransformPoint(vertices[a]);
                Vector3 worldB = meshTransform.TransformPoint(vertices[b]);
                Vector3 worldC = meshTransform.TransformPoint(vertices[c]);
                Vector3 cross = Vector3.Cross(worldB - worldA, worldC - worldA);
                float areaWeight = Mathf.Max(0.0001f, cross.magnitude * 0.5f);
                Vector3 normal = FaceNormal(cross);
                Vector3 center = (worldA + worldB + worldC) / 3f;
                Vector4 weights = ClassifyFace(center, normal) * areaWeight;

                accumulated[a] += weights;
                accumulated[b] += weights;
                accumulated[c] += weights;
            }
        }

        Color[] colors = new Color[vertices.Length];
        SmoothAccumulatedWeightsByPosition(vertices, meshTransform, accumulated);
        for (int i = 0; i < colors.Length; i++)
        {
            Vector4 weights = accumulated[i];
            float total = weights.x + weights.y + weights.z + weights.w;
            if (total <= 0.0001f)
            {
                colors[i] = new Color(1f, 0f, 0f, 0f);
                continue;
            }

            weights /= total;
            colors[i] = new Color(weights.x, weights.y, weights.z, weights.w);
        }

        return colors;
    }

    Mesh BuildCliffSplitMesh(Mesh sourceMesh, Transform meshTransform)
    {
        Vector3[] sourceVertices = sourceMesh.vertices;
        Vector2[] sourceUv = sourceMesh.uv;
        Vector2[] sourceUv2 = sourceMesh.uv2;
        Color[] smoothColors = BuildVertexColors(sourceMesh, meshTransform);

        var vertices = new System.Collections.Generic.List<Vector3>();
        var normals = new System.Collections.Generic.List<Vector3>();
        var colors = new System.Collections.Generic.List<Color>();
        var uvs = new System.Collections.Generic.List<Vector2>();
        var uv2s = new System.Collections.Generic.List<Vector2>();
        var triangles = new System.Collections.Generic.List<int>();

        bool hasUv = sourceUv != null && sourceUv.Length == sourceVertices.Length;
        bool hasUv2 = sourceUv2 != null && sourceUv2.Length == sourceVertices.Length;

        for (int submesh = 0; submesh < sourceMesh.subMeshCount; submesh++)
        {
            int[] sourceTriangles = sourceMesh.GetTriangles(submesh);
            for (int i = 0; i + 2 < sourceTriangles.Length; i += 3)
            {
                int a = sourceTriangles[i];
                int b = sourceTriangles[i + 1];
                int c = sourceTriangles[i + 2];
                if (!IsValidIndex(sourceVertices, a) || !IsValidIndex(sourceVertices, b) || !IsValidIndex(sourceVertices, c))
                    continue;

                Vector3 worldA = meshTransform.TransformPoint(sourceVertices[a]);
                Vector3 worldB = meshTransform.TransformPoint(sourceVertices[b]);
                Vector3 worldC = meshTransform.TransformPoint(sourceVertices[c]);
                Vector3 worldCross = Vector3.Cross(worldB - worldA, worldC - worldA);
                Vector3 worldNormal = FaceNormal(worldCross);
                Vector3 objectNormal = meshTransform.InverseTransformDirection(worldNormal).normalized;
                Vector3 center = (worldA + worldB + worldC) / 3f;
                Vector4 faceWeights = ClassifyFace(center, worldNormal);
                bool isCliffFace = faceWeights.z >= faceWeights.x && faceWeights.z >= faceWeights.y && faceWeights.z >= faceWeights.w;
                Color faceColor = ToColor(faceWeights);

                AddHardFaceVertex(a, sourceVertices, hasUv ? sourceUv : null, hasUv2 ? sourceUv2 : null, objectNormal, isCliffFace ? faceColor : smoothColors[a], vertices, normals, colors, uvs, uv2s, triangles);
                AddHardFaceVertex(b, sourceVertices, hasUv ? sourceUv : null, hasUv2 ? sourceUv2 : null, objectNormal, isCliffFace ? faceColor : smoothColors[b], vertices, normals, colors, uvs, uv2s, triangles);
                AddHardFaceVertex(c, sourceVertices, hasUv ? sourceUv : null, hasUv2 ? sourceUv2 : null, objectNormal, isCliffFace ? faceColor : smoothColors[c], vertices, normals, colors, uvs, uv2s, triangles);
            }
        }

        var mesh = new Mesh { name = $"{sourceMesh.name}_TerrainBake" };
        if (vertices.Count > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetColors(colors);
        if (uvs.Count == vertices.Count)
            mesh.SetUVs(0, uvs);
        if (uv2s.Count == vertices.Count)
            mesh.SetUVs(1, uv2s);
        mesh.SetTriangles(triangles, 0);
        return mesh;
    }

    Vector4 ClassifyFace(Vector3 center, Vector3 normal)
    {
        float slopeAngle = Vector3.Angle(normal, Vector3.up);

        if (slopeAngle >= cliffSlopeAngle)
            return new Vector4(0f, 0f, 1f, 0f);

        if (shoreWidth > 0f && Mathf.Abs(center.y - waterLevel) <= shoreWidth)
            return Normalize(new Vector4(0f, shoreDirtBlend, 0f, 1f - shoreDirtBlend));

        if (slopeAngle >= dirtSlopeAngle)
            return new Vector4(0.08f, 0.92f, 0f, 0f);

        float dirt = FlatDirtWeight(center);
        return Normalize(new Vector4(1f - dirt, dirt, 0f, 0f));
    }

    float FlatDirtWeight(Vector3 center)
    {
        if (flatDirtCoverage <= 0f)
            return 0f;

        float scale = Mathf.Max(0.001f, flatDirtNoiseScale);
        float noiseA = Mathf.PerlinNoise(center.x * scale + 17.31f, center.z * scale - 9.47f);
        float noiseB = Mathf.PerlinNoise(center.x * scale * 2.3f - 41.7f, center.z * scale * 2.3f + 23.8f);
        float noise = Mathf.Lerp(noiseA, noiseB, 0.35f);
        float threshold = 1f - flatDirtCoverage;
        float dirt = Mathf.SmoothStep(threshold - 0.12f, threshold + 0.08f, noise);
        return Mathf.Clamp01(dirt * 0.32f);
    }

    static Vector3 FaceNormal(Vector3 fallbackCross)
    {
        Vector3 normal = fallbackCross.sqrMagnitude > 0.000001f ? fallbackCross.normalized : Vector3.up;

        if (normal.y < 0f)
            normal = -normal;
        return normal;
    }

    void ResetBakeDefaults()
    {
        shoreWidth = 0.35f;
        dirtSlopeAngle = 24f;
        cliffSlopeAngle = 48f;
        flatDirtCoverage = 0f;
        flatDirtNoiseScale = 0.032f;
        shoreDirtBlend = 0.18f;
        splitCliffFaces = false;
    }

    static void SmoothAccumulatedWeightsByPosition(Vector3[] vertices, Transform meshTransform, Vector4[] accumulated)
    {
        var totals = new System.Collections.Generic.Dictionary<Vector3Int, Vector4>();
        var counts = new System.Collections.Generic.Dictionary<Vector3Int, int>();
        const float precision = 1000f;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 world = meshTransform.TransformPoint(vertices[i]);
            Vector3Int key = new Vector3Int(
                Mathf.RoundToInt(world.x * precision),
                Mathf.RoundToInt(world.y * precision),
                Mathf.RoundToInt(world.z * precision));

            if (totals.TryGetValue(key, out Vector4 total))
            {
                totals[key] = total + accumulated[i];
                counts[key] = counts[key] + 1;
            }
            else
            {
                totals.Add(key, accumulated[i]);
                counts.Add(key, 1);
            }
        }

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 world = meshTransform.TransformPoint(vertices[i]);
            Vector3Int key = new Vector3Int(
                Mathf.RoundToInt(world.x * precision),
                Mathf.RoundToInt(world.y * precision),
                Mathf.RoundToInt(world.z * precision));
            accumulated[i] = totals[key] / Mathf.Max(1, counts[key]);
        }
    }

    static void AddHardFaceVertex(
        int sourceIndex,
        Vector3[] sourceVertices,
        Vector2[] sourceUv,
        Vector2[] sourceUv2,
        Vector3 normal,
        Color color,
        System.Collections.Generic.List<Vector3> vertices,
        System.Collections.Generic.List<Vector3> normals,
        System.Collections.Generic.List<Color> colors,
        System.Collections.Generic.List<Vector2> uvs,
        System.Collections.Generic.List<Vector2> uv2s,
        System.Collections.Generic.List<int> triangles)
    {
        triangles.Add(vertices.Count);
        vertices.Add(sourceVertices[sourceIndex]);
        normals.Add(normal);
        colors.Add(color);
        if (sourceUv != null)
            uvs.Add(sourceUv[sourceIndex]);
        if (sourceUv2 != null)
            uv2s.Add(sourceUv2[sourceIndex]);
    }

    static Color ToColor(Vector4 value)
    {
        value = Normalize(value);
        return new Color(value.x, value.y, value.z, value.w);
    }

    static Vector4 Normalize(Vector4 value)
    {
        float total = value.x + value.y + value.z + value.w;
        return total <= 0.0001f ? new Vector4(1f, 0f, 0f, 0f) : value / total;
    }

    void EnsureTerrainMaterialShader()
    {
        if (terrainMaterial == null)
            return;

        Shader terrainShader = Shader.Find(TerrainShaderName);
        if (terrainShader == null || terrainMaterial.shader == terrainShader)
            return;

        terrainMaterial.shader = terrainShader;
        EditorUtility.SetDirty(terrainMaterial);
    }

    static bool IsValidIndex(Vector3[] vertices, int index)
    {
        return index >= 0 && index < vertices.Length;
    }

    static float EstimateWaterLevel(MeshFilter filter, Mesh mesh)
    {
        Vector3[] vertices = mesh.vertices;
        if (vertices == null || vertices.Length == 0)
            return 0f;

        float minY = float.MaxValue;
        float maxY = float.MinValue;
        for (int i = 0; i < vertices.Length; i++)
        {
            float y = filter.transform.TransformPoint(vertices[i]).y;
            minY = Mathf.Min(minY, y);
            maxY = Mathf.Max(maxY, y);
        }

        return Mathf.Lerp(minY, maxY, 0.08f);
    }

    static void EnsureOutputFolder()
    {
        string[] parts = OutputFolder.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "TerrainMesh";

        foreach (char invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');
        return value;
    }
}
