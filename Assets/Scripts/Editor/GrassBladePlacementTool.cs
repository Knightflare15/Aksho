using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed class GrassBladePlacementTool : EditorWindow
{
    const string DefaultPrefabPath = "Assets/GrassBlade.fbx";
    const string DefaultProfilePath = "Assets/Generated/GrassBladeSpawnProfile.asset";
    const string DefaultMaterialPath = "Assets/Generated/GrassBlade.mat";
    const string GrassBladeShaderName = "The Script/WorldGeneration/Grass Blade";
    const string RootName = "GeneratedGrassBlades";

    [SerializeField] GrassBladeSpawnProfile profile;

    [MenuItem("The Script/World Generation/Grass Blade Spawner", false, 61)]
    [MenuItem("GameObject/The Script/World Generation/Grass Blade Spawner", false, 12)]
    public static void Open()
    {
        GetWindow<GrassBladePlacementTool>("Grass Blade Spawner");
    }

    void OnEnable()
    {
        if (profile == null)
            profile = AssetDatabase.LoadAssetAtPath<GrassBladeSpawnProfile>(DefaultProfilePath);
    }

    void OnSelectionChange()
    {
        Repaint();
    }

    void OnGUI()
    {
        MeshFilter selectedFilter = GetSelectedMeshFilter();
        Mesh sourceMesh = selectedFilter != null ? selectedFilter.sharedMesh : null;

        EditorGUILayout.LabelField("Selected Mesh", EditorStyles.boldLabel);
        if (selectedFilter == null || sourceMesh == null)
        {
            EditorGUILayout.HelpBox("Select a baked terrain mesh object. The mesh must have vertex colors from Terrain Material Baker.", MessageType.Info);
        }
        else
        {
            EditorGUILayout.LabelField("Object", selectedFilter.gameObject.name);
            EditorGUILayout.LabelField("Mesh", sourceMesh.name);
            EditorGUILayout.LabelField("Vertex Colors", sourceMesh.colors != null && sourceMesh.colors.Length == sourceMesh.vertexCount ? "Found" : "Missing");
        }

        EditorGUILayout.Space();
        profile = (GrassBladeSpawnProfile)EditorGUILayout.ObjectField("Grass Blade Profile", profile, typeof(GrassBladeSpawnProfile), false);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Create Default Profile"))
                profile = CreateDefaultProfile();
            if (GUILayout.Button("Ping Profile") && profile != null)
                EditorGUIUtility.PingObject(profile);
        }

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(profile == null || selectedFilter == null || sourceMesh == null))
        {
            if (GUILayout.Button("Spawn Grass On Selected Mesh"))
                SpawnOnSelectedMesh(selectedFilter, sourceMesh);
            if (GUILayout.Button("Clear Spawned Grass"))
                ClearExistingGrass(selectedFilter.transform);
        }
    }

    static MeshFilter GetSelectedMeshFilter()
    {
        GameObject selected = Selection.activeGameObject;
        return selected != null ? selected.GetComponent<MeshFilter>() : null;
    }

    GrassBladeSpawnProfile CreateDefaultProfile()
    {
        EnsureFolder("Assets/Generated");

        GrassBladeSpawnProfile asset = AssetDatabase.LoadAssetAtPath<GrassBladeSpawnProfile>(DefaultProfilePath);
        if (asset == null)
        {
            asset = CreateInstance<GrassBladeSpawnProfile>();
            asset.grassBladePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultPrefabPath);
            ApplyRecommendedDefaults(asset);
            AssetDatabase.CreateAsset(asset, DefaultProfilePath);
        }
        else if (asset.grassBladePrefab == null)
        {
            asset.grassBladePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultPrefabPath);
            EditorUtility.SetDirty(asset);
        }
        ApplyRecommendedDefaults(asset);

        AssetDatabase.SaveAssets();
        EditorGUIUtility.PingObject(asset);
        return asset;
    }

    static void ApplyRecommendedDefaults(GrassBladeSpawnProfile asset)
    {
        if (asset == null)
            return;

        asset.bladeMaterial = EnsureDefaultMaterial();
        asset.scaleRange = new Vector2(0.28f, 0.55f);
        asset.yOffset = 0.01f;
        asset.alignToNormal = 0.35f;
        asset.randomTiltDegrees = 8f;
        EditorUtility.SetDirty(asset);
    }

    static Material EnsureDefaultMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(DefaultMaterialPath);
        Shader shader = Shader.Find(GrassBladeShaderName);
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");

        if (material == null)
        {
            material = new Material(shader) { name = "GrassBlade" };
            AssetDatabase.CreateAsset(material, DefaultMaterialPath);
        }
        else if (shader != null && material.shader != shader)
        {
            material.shader = shader;
        }

        if (material != null)
        {
            material.SetColor("_BaseColor", new Color(0.10f, 0.34f, 0.08f, 1f));
            if (material.HasProperty("_TipColor"))
                material.SetColor("_TipColor", new Color(0.42f, 0.72f, 0.22f, 1f));
            if (material.HasProperty("_WindStrength"))
                material.SetFloat("_WindStrength", 0.08f);
            if (material.HasProperty("_WindSpeed"))
                material.SetFloat("_WindSpeed", 2.2f);
            if (material.HasProperty("_WindScale"))
                material.SetFloat("_WindScale", 0.9f);
            EditorUtility.SetDirty(material);
        }

        return material;
    }

    void SpawnOnSelectedMesh(MeshFilter filter, Mesh mesh)
    {
        if (profile == null || profile.grassBladePrefab == null)
        {
            Debug.LogWarning("Assign a GrassBladeSpawnProfile with a grassBladePrefab before spawning grass.", this);
            return;
        }

        Color[] colors = mesh.colors;
        if (colors == null || colors.Length != mesh.vertexCount)
        {
            Debug.LogWarning("Selected mesh has no vertex colors. Bake it with Terrain Material Baker first.", filter);
            return;
        }

        ClearExistingGrass(filter.transform);
        Transform root = CreateGrassRoot(filter.transform);
        List<TriangleSample> samples = BuildSamples(filter.transform, mesh, colors, profile);
        if (samples.Count == 0)
        {
            Debug.LogWarning("No grass-weighted triangles found. Check that the terrain baker wrote grass into vertex color R.", filter);
            return;
        }

        float totalWeight = 0f;
        foreach (TriangleSample sample in samples)
            totalWeight += sample.weight;

        var rng = new System.Random(profile.seed);
        int targetCount = Mathf.Clamp(Mathf.RoundToInt(totalWeight), 0, profile.maxInstances);
        int spawned = 0;
        int attempts = Mathf.Max(targetCount * 4, 32);
        for (int i = 0; i < attempts && spawned < targetCount; i++)
        {
            TriangleSample sample = PickSample(samples, totalWeight, rng);
            Vector3 position = RandomPoint(sample.a, sample.b, sample.c, rng);
            GrassBladeSpawner.SpawnInstance(profile, root, position, sample.normal, rng, spawned);
            spawned++;
        }

        Selection.activeTransform = root;
        Debug.Log($"Spawned {spawned} grass blades on {filter.name}.", root);
    }

    static List<TriangleSample> BuildSamples(Transform transform, Mesh mesh, Color[] colors, GrassBladeSpawnProfile profile)
    {
        Vector3[] vertices = mesh.vertices;
        var samples = new List<TriangleSample>();

        for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
        {
            int[] triangles = mesh.GetTriangles(submesh);
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int ia = triangles[i];
                int ib = triangles[i + 1];
                int ic = triangles[i + 2];
                if (!Valid(vertices, colors, ia) || !Valid(vertices, colors, ib) || !Valid(vertices, colors, ic))
                    continue;

                float grassWeight = (colors[ia].r + colors[ib].r + colors[ic].r) / 3f;
                if (grassWeight < profile.minGrassWeight)
                    continue;

                Vector3 a = transform.TransformPoint(vertices[ia]);
                Vector3 b = transform.TransformPoint(vertices[ib]);
                Vector3 c = transform.TransformPoint(vertices[ic]);
                Vector3 cross = Vector3.Cross(b - a, c - a);
                float area = cross.magnitude * 0.5f;
                if (area <= 0.0001f)
                    continue;

                Vector3 normal = cross.normalized;
                if (normal.y < 0f)
                    normal = -normal;
                float slope = Vector3.Angle(normal, Vector3.up);
                if (slope > profile.maxSlope)
                    continue;

                samples.Add(new TriangleSample
                {
                    a = a,
                    b = b,
                    c = c,
                    normal = normal,
                    weight = area * grassWeight * Mathf.Max(0.01f, profile.densityPerSquareMeter),
                });
            }
        }

        return samples;
    }

    static TriangleSample PickSample(List<TriangleSample> samples, float totalWeight, System.Random rng)
    {
        float target = (float)rng.NextDouble() * totalWeight;
        float running = 0f;
        for (int i = 0; i < samples.Count; i++)
        {
            running += samples[i].weight;
            if (running >= target)
                return samples[i];
        }

        return samples[samples.Count - 1];
    }

    static Vector3 RandomPoint(Vector3 a, Vector3 b, Vector3 c, System.Random rng)
    {
        float u = (float)rng.NextDouble();
        float v = (float)rng.NextDouble();
        if (u + v > 1f)
        {
            u = 1f - u;
            v = 1f - v;
        }

        return a + (b - a) * u + (c - a) * v;
    }

    static Transform CreateGrassRoot(Transform parent)
    {
        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Spawn Grass Blades");
        root.transform.SetParent(parent, false);
        return root.transform;
    }

    static void ClearExistingGrass(Transform parent)
    {
        Transform existing = parent.Find(RootName);
        if (existing != null)
            Undo.DestroyObjectImmediate(existing.gameObject);
    }

    static bool Valid(Vector3[] vertices, Color[] colors, int index)
    {
        return index >= 0 && index < vertices.Length && index < colors.Length;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        string[] parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    struct TriangleSample
    {
        public Vector3 a;
        public Vector3 b;
        public Vector3 c;
        public Vector3 normal;
        public float weight;
    }
}
