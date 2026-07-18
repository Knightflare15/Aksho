using UnityEditor;
using UnityEngine;

public static class WorldGenerationSceneMenu
{
    [MenuItem("GameObject/The Script/World Generation/World Generator", false, 10)]
    [MenuItem("The Script/World Generation/Create World Generator In Scene", false, 50)]
    public static void CreateWorldGenerator()
    {
        var generatorObject = new GameObject("ProceduralWorldGenerator");
        Undo.RegisterCreatedObjectUndo(generatorObject, "Create World Generator");

        WorldGenerator generator = generatorObject.AddComponent<WorldGenerator>();
        if (Selection.activeTransform != null)
            generatorObject.transform.SetParent(Selection.activeTransform, false);

        Selection.activeObject = generatorObject;
        EditorGUIUtility.PingObject(generator);
    }

    [MenuItem("The Script/Grammar Scenes/Create Class 1-2 World Scaffold", false, 69)]
    public static void CreateClassOneTwoWorldScaffold()
    {
        GameObject scaffoldRoot = new GameObject("Class12WorldScaffold");
        Undo.RegisterCreatedObjectUndo(scaffoldRoot, "Create Class 1-2 World Scaffold");

        var stagedControllers = new System.Collections.Generic.List<GrammarSceneController>();
        const float stepX = 96f;
        const float laneZ = 84f;

        for (int i = 0; i < NaturalGrammarProgression.Regions.Count; i++)
        {
            NaturalGrammarRegion region = NaturalGrammarProgression.Regions[i];
            if (region == null)
                continue;

            float x = i * stepX;
            stagedControllers.Add(CreateGrammarSceneRoot(scaffoldRoot.transform, region, SemanticZoneKind.Town, new Vector3(x, 0f, 0f), includeWorldGenerator: false));
            stagedControllers.Add(CreateGrammarSceneRoot(scaffoldRoot.transform, region, SemanticZoneKind.Route, new Vector3(x, 0f, laneZ), includeWorldGenerator: false));
            stagedControllers.Add(CreateGrammarSceneRoot(scaffoldRoot.transform, region, SemanticZoneKind.Gym, new Vector3(x, 0f, laneZ * 2f), includeWorldGenerator: false));
        }

        ConfigureWorldConnections(stagedControllers);
        Selection.activeObject = scaffoldRoot;
        EditorGUIUtility.PingObject(scaffoldRoot);
    }

    [MenuItem("The Script/Grammar Scenes/Create Procedural Town Setup", false, 70)]
    public static void CreateProceduralTownSetup()
    {
        CreateGrammarSceneSetup(SemanticZoneKind.Town, TranslatorAssistMode.Full, "Greetings and Survival English", 1, includeWorldGenerator: false);
    }

    [MenuItem("The Script/Grammar Scenes/Create Procedural Route Setup", false, 71)]
    public static void CreateProceduralRouteSetup()
    {
        CreateGrammarSceneSetup(SemanticZoneKind.Route, TranslatorAssistMode.Partial, "Greetings and Survival English", 1, includeWorldGenerator: true);
    }

    [MenuItem("The Script/Grammar Scenes/Create Procedural Gym Setup", false, 72)]
    public static void CreateProceduralGymSetup()
    {
        CreateGrammarSceneSetup(SemanticZoneKind.Gym, TranslatorAssistMode.Off, "Greetings and Survival English", 1, includeWorldGenerator: false);
    }

    static void CreateGrammarSceneSetup(
        SemanticZoneKind kind,
        TranslatorAssistMode assistMode,
        string topic,
        int tier,
        bool includeWorldGenerator)
    {
        GameObject root = new GameObject($"Procedural{kind}Scene");
        Undo.RegisterCreatedObjectUndo(root, $"Create Procedural {kind} Scene");

        GrammarSceneController controller = root.AddComponent<GrammarSceneController>();
        controller.sceneKind = kind;
        controller.grammarTopic = topic;
        controller.grammarTopicTier = tier;
        controller.translatorAssist = assistMode;

        ProceduralGrammarSceneGenerator generator = root.AddComponent<ProceduralGrammarSceneGenerator>();
        generator.sceneController = controller;

        if (includeWorldGenerator)
        {
            GameObject worldRoot = new GameObject("ProceduralRouteWorldGenerator");
            Undo.RegisterCreatedObjectUndo(worldRoot, "Create Route World Generator");
            worldRoot.transform.SetParent(root.transform, false);
            WorldGenerator worldGenerator = worldRoot.AddComponent<WorldGenerator>();
            worldGenerator.GenerateFromInspector();
        }

        Selection.activeObject = root;
        EditorGUIUtility.PingObject(root);
    }

    static GrammarSceneController CreateGrammarSceneRoot(
        Transform parent,
        NaturalGrammarRegion region,
        SemanticZoneKind kind,
        Vector3 localPosition,
        bool includeWorldGenerator)
    {
        GameObject root = new GameObject($"{region.displayName} {kind}");
        Undo.RegisterCreatedObjectUndo(root, $"Create {region.displayName} {kind}");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;

        GrammarSceneController controller = root.AddComponent<GrammarSceneController>();
        controller.sceneKind = kind;
        controller.grammarTopic = region.grammarTopic;
        controller.grammarTopicTier = region.tier;
        controller.translatorAssist = ResolveAssistMode(kind);
        controller.mapAreaId = GrammarWorldProgressService.BuildAreaId(kind, region.grammarTopic, region.tier);
        controller.mapDisplayName = ResolveDisplayName(region, kind);
        controller.mapPosition = new Vector2(localPosition.x, localPosition.z);
        controller.connectedMapAreaIds = new System.Collections.Generic.List<string>();

        ProceduralGrammarSceneGenerator generator = root.AddComponent<ProceduralGrammarSceneGenerator>();
        generator.sceneController = controller;

        if (includeWorldGenerator)
        {
            GameObject worldRoot = new GameObject($"{region.displayName} Route World Generator");
            Undo.RegisterCreatedObjectUndo(worldRoot, "Create Route World Generator");
            worldRoot.transform.SetParent(root.transform, false);
            WorldGenerator worldGenerator = worldRoot.AddComponent<WorldGenerator>();
            worldGenerator.GenerateFromInspector();
        }

        return controller;
    }

    static void ConfigureWorldConnections(System.Collections.Generic.List<GrammarSceneController> controllers)
    {
        if (controllers == null)
            return;

        for (int i = 0; i < NaturalGrammarProgression.Regions.Count; i++)
        {
            NaturalGrammarRegion region = NaturalGrammarProgression.Regions[i];
            if (region == null)
                continue;

            GrammarSceneController town = FindController(controllers, region.tier, SemanticZoneKind.Town);
            GrammarSceneController route = FindController(controllers, region.tier, SemanticZoneKind.Route);
            GrammarSceneController gym = FindController(controllers, region.tier, SemanticZoneKind.Gym);
            NaturalGrammarRegion previous = i > 0 ? NaturalGrammarProgression.Regions[i - 1] : null;
            NaturalGrammarRegion next = i + 1 < NaturalGrammarProgression.Regions.Count ? NaturalGrammarProgression.Regions[i + 1] : null;

            if (town != null)
            {
                AddConnection(town, route);
                if (previous != null)
                    AddConnection(town, GrammarWorldProgressService.BuildAreaId(SemanticZoneKind.Gym, previous.grammarTopic, previous.tier));
            }

            if (route != null)
            {
                AddConnection(route, town);
                AddConnection(route, gym);
            }

            if (gym != null)
            {
                AddConnection(gym, route);
                if (next != null)
                    AddConnection(gym, GrammarWorldProgressService.BuildAreaId(SemanticZoneKind.Town, next.grammarTopic, next.tier));
            }
        }
    }

    static GrammarSceneController FindController(
        System.Collections.Generic.List<GrammarSceneController> controllers,
        int tier,
        SemanticZoneKind kind)
    {
        foreach (GrammarSceneController controller in controllers)
        {
            if (controller != null &&
                controller.sceneKind == kind &&
                controller.grammarTopicTier == tier)
                return controller;
        }
        return null;
    }

    static void AddConnection(GrammarSceneController source, GrammarSceneController destination)
    {
        if (source == null || destination == null)
            return;
        AddConnection(source, destination.mapAreaId);
    }

    static void AddConnection(GrammarSceneController source, string targetAreaId)
    {
        if (source == null || string.IsNullOrWhiteSpace(targetAreaId))
            return;

        source.connectedMapAreaIds ??= new System.Collections.Generic.List<string>();
        if (!source.connectedMapAreaIds.Contains(targetAreaId))
            source.connectedMapAreaIds.Add(targetAreaId);
    }

    static TranslatorAssistMode ResolveAssistMode(SemanticZoneKind kind)
    {
        return kind switch
        {
            SemanticZoneKind.Route => TranslatorAssistMode.Partial,
            SemanticZoneKind.Gym => TranslatorAssistMode.Off,
            _ => TranslatorAssistMode.Full,
        };
    }

    static string ResolveDisplayName(NaturalGrammarRegion region, SemanticZoneKind kind)
    {
        string label = region != null && !string.IsNullOrWhiteSpace(region.displayName)
            ? region.displayName
            : "Grammar Area";
        return kind switch
        {
            SemanticZoneKind.Route => $"{label} Route",
            SemanticZoneKind.Gym => $"{label} Gym",
            _ => label,
        };
    }
}
