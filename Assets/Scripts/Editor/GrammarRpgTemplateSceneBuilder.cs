#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public static class GrammarRpgTemplateSceneBuilder
{
    const string SceneFolder = "Assets/Scenes";
    static readonly HashSet<string> RetiredProductionSceneNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Level_1_Bat",
        "Test_Arena",
    };

    [MenuItem("The Script/Grammar Scenes/Create Four RPG Template Scenes", false, 68)]
    public static void CreateOrRefreshTemplateScenes()
    {
        Directory.CreateDirectory(SceneFolder);
        CreateWorldTemplateScene(GrammarWorldRuntimeBootstrap.TownSceneName, SemanticZoneKind.Town, new Color(0.19f, 0.36f, 0.24f, 1f));
        CreateWorldTemplateScene(GrammarWorldRuntimeBootstrap.RouteSceneName, SemanticZoneKind.Route, new Color(0.18f, 0.31f, 0.22f, 1f));
        CreateWorldTemplateScene(GrammarWorldRuntimeBootstrap.GymSceneName, SemanticZoneKind.Gym, new Color(0.28f, 0.24f, 0.28f, 1f));
        CreateBattleTemplateScene();
        EnsureScenesInBuildSettings();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[GrammarRpgTemplateSceneBuilder] Grammar RPG template scenes are ready.");
    }

    [MenuItem("The Script/Grammar Scenes/Repair Missing Template Player Animators", false, 69)]
    public static void RepairMissingTemplatePlayerAnimatorsBatchmode()
    {
        try
        {
            int repairedPlayers = RepairMissingTemplatePlayerAnimators();
            Debug.Log($"[GrammarRpgTemplateSceneBuilder] Player Animator repair complete: {repairedPlayers} player rig(s) changed.");
            if (Application.isBatchMode)
                EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(1);
                return;
            }

            throw;
        }
    }

    static void CreateWorldTemplateScene(string sceneName, SemanticZoneKind kind, Color groundColor)
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = sceneName;

        RenderSettings.ambientLight = new Color(0.48f, 0.52f, 0.56f, 1f);
        CreateDirectionalLight(new Vector3(48f, -32f, 0f));
        GameObject player = CreatePlayerRig(new Vector3(0f, 1.1f, -12f));
        CreateGround($"TemplateGround_{sceneName}", new Vector3(0f, -0.15f, 0f), new Vector3(72f, 0.3f, 72f), groundColor);
        CreateNavMeshSurface(sceneName);
        CreateGameplaySystems(player, kind);
        CreateGrammarRoot(kind);

        EditorSceneManager.SaveScene(scene, BuildScenePath(sceneName));
    }

    static void CreateBattleTemplateScene()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = GrammarWorldRuntimeBootstrap.BattleSceneName;

        RenderSettings.ambientLight = new Color(0.42f, 0.44f, 0.5f, 1f);
        CreateDirectionalLight(new Vector3(50f, -36f, 18f));
        CreateGround("TemplateGround_Battle", Vector3.zero, new Vector3(36f, 0.25f, 36f), new Color(0.18f, 0.2f, 0.23f, 1f));
        CreateNavMeshSurface(GrammarWorldRuntimeBootstrap.BattleSceneName, new Vector3(42f, 8f, 42f));

        GameObject cameraGo = new GameObject("Battle Camera", typeof(Camera), typeof(AudioListener));
        cameraGo.transform.SetPositionAndRotation(new Vector3(0f, 17f, -18f), Quaternion.Euler(56f, 0f, 0f));
        cameraGo.GetComponent<Camera>().fieldOfView = 45f;

        GameObject presentation = new GameObject("TacticalBattleScenePresentation", typeof(TacticalBattleSceneBootstrap));
        TacticalBattleSceneBootstrap bootstrap = presentation.GetComponent<TacticalBattleSceneBootstrap>();
        bootstrap.renderPlaceholderCubes = true;
        bootstrap.createCameraAndLight = false;

        EditorSceneManager.SaveScene(scene, BuildScenePath(GrammarWorldRuntimeBootstrap.BattleSceneName));
    }

    static void CreateGrammarRoot(SemanticZoneKind kind)
    {
        GameObject root = new GameObject(
            "RuntimeGrammarWorldArea",
            typeof(GrammarSceneController),
            typeof(ProceduralGrammarSceneGenerator),
            typeof(TranslatorBuddyService));
        GrammarSceneController controller = root.GetComponent<GrammarSceneController>();
        controller.sceneKind = kind;
        controller.translatorAssist = kind switch
        {
            SemanticZoneKind.Route => TranslatorAssistMode.Partial,
            SemanticZoneKind.Gym => TranslatorAssistMode.Off,
            _ => TranslatorAssistMode.Full,
        };

        NaturalGrammarRegion firstRegion = NaturalGrammarProgression.Regions.Count > 0 ? NaturalGrammarProgression.Regions[0] : null;
        if (firstRegion != null)
        {
            controller.grammarTopic = firstRegion.grammarTopic;
            controller.grammarTopicTier = firstRegion.tier;
            controller.mapAreaId = GrammarWorldProgressService.BuildAreaId(kind, firstRegion.grammarTopic, firstRegion.tier);
        }

        ProceduralGrammarSceneGenerator generator = root.GetComponent<ProceduralGrammarSceneGenerator>();
        generator.sceneController = controller;
        generator.generateOnStart = true;

        TranslatorBuddyService buddy = root.GetComponent<TranslatorBuddyService>();
        buddy.currentAssistMode = controller.translatorAssist;
        buddy.providerMode = TranslatorProviderMode.TextFallback;
        buddy.allowRemoteTextFallback = false;
        buddy.requireProductionReadyEndpoints = false;
        buddy.requestSpeechAudio = false;
        buddy.enableAiTutor = true;
    }

    static GameObject CreatePlayerRig(Vector3 position)
    {
        GameObject player = new GameObject(
            "Player",
            typeof(CharacterController),
            typeof(Animator),
            typeof(PlayerController),
            typeof(PlayerHealth),
            typeof(PlayerLearningProfile),
            typeof(PlayerAimAssist),
            typeof(WordActionHandler),
            typeof(SpellPerformanceTracker),
            typeof(CreatureCombatRegistry),
            typeof(CreatureCombatController),
            typeof(TacticalGrammarBattleController));
        player.transform.position = position;

        CharacterController character = player.GetComponent<CharacterController>();
        character.height = 1.8f;
        character.radius = 0.35f;
        character.center = new Vector3(0f, 0.9f, 0f);

        GameObject cameraGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        cameraGo.transform.SetParent(player.transform, false);
        cameraGo.transform.localPosition = new Vector3(0f, 1.55f, 0f);
        cameraGo.transform.localRotation = Quaternion.identity;
        Camera camera = cameraGo.GetComponent<Camera>();
        camera.fieldOfView = 67f;

        PlayerController controller = player.GetComponent<PlayerController>();
        controller.cameraTransform = cameraGo.transform;
        controller.animator = player.GetComponent<Animator>();

        CreatureCombatController combat = player.GetComponent<CreatureCombatController>();
        combat.registry = player.GetComponent<CreatureCombatRegistry>();
        combat.learningProfile = player.GetComponent<PlayerLearningProfile>();
        combat.aimAssist = player.GetComponent<PlayerAimAssist>();
        combat.tacticalBattle = player.GetComponent<TacticalGrammarBattleController>();

        TacticalGrammarBattleController tactical = player.GetComponent<TacticalGrammarBattleController>();
        tactical.registry = player.GetComponent<CreatureCombatRegistry>();
        tactical.learningProfile = player.GetComponent<PlayerLearningProfile>();
        tactical.aimAssist = player.GetComponent<PlayerAimAssist>();
        tactical.battleSceneName = GrammarWorldRuntimeBootstrap.BattleSceneName;

        return player;
    }

    static void CreateGameplaySystems(GameObject player, SemanticZoneKind kind)
    {
        GameObject systems = new GameObject(
            "GrammarGameplaySystems",
            typeof(EnemyWaveDirector),
            typeof(LevelObjectiveDirector),
            typeof(BattleEncounterController),
            typeof(EnemyAttackCoordinator));

        Transform arenaCenter = new GameObject("ArenaCenter").transform;
        arenaCenter.SetParent(systems.transform, false);

        EnemyWaveDirector waveDirector = systems.GetComponent<EnemyWaveDirector>();
        waveDirector.playerController = player.GetComponent<PlayerController>();
        waveDirector.playerHealth = player.GetComponent<PlayerHealth>();
        waveDirector.wordActionHandler = player.GetComponent<WordActionHandler>();
        waveDirector.creatureRegistry = player.GetComponent<CreatureCombatRegistry>();
        waveDirector.playerLearningProfile = player.GetComponent<PlayerLearningProfile>();
        waveDirector.objectiveDirector = systems.GetComponent<LevelObjectiveDirector>();
        waveDirector.battleEncounterController = systems.GetComponent<BattleEncounterController>();
        waveDirector.attackCoordinator = systems.GetComponent<EnemyAttackCoordinator>();
        waveDirector.spellRegistry = null;
        waveDirector.legacyWordSpellFallbackEnabled = false;
        waveDirector.arenaCenter = arenaCenter;
        waveDirector.autoStart = false;
        waveDirector.startWhenPlayerEntersArena = false;

        TacticalGrammarBattleController tactical = player.GetComponent<TacticalGrammarBattleController>();
        tactical.waveDirector = waveDirector;

        if (kind == SemanticZoneKind.Town)
            waveDirector.enabled = false;
    }

    static void CreateDirectionalLight(Vector3 eulerAngles)
    {
        GameObject lightGo = new GameObject("Directional Light", typeof(Light));
        lightGo.transform.rotation = Quaternion.Euler(eulerAngles);
        Light light = lightGo.GetComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.15f;
    }

    static void CreateGround(string name, Vector3 position, Vector3 scale, Color color)
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = name;
        ground.transform.SetPositionAndRotation(position, Quaternion.identity);
        ground.transform.localScale = scale;
        Renderer renderer = ground.GetComponent<Renderer>();
        if (renderer != null)
        {
            Material material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            material.color = color;
            renderer.sharedMaterial = material;
        }
    }

    static void CreateNavMeshSurface(string sceneName, Vector3? sizeOverride = null)
    {
        GameObject navRoot = new GameObject($"RuntimeGrammarNavMesh_{sceneName}", typeof(NavMeshSurface));
        NavMeshSurface surface = navRoot.GetComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Volume;
        surface.center = new Vector3(0f, 2f, 0f);
        surface.size = sizeOverride ?? new Vector3(84f, 10f, 84f);
        surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
        surface.layerMask = ~0;
        surface.ignoreNavMeshAgent = true;
        surface.ignoreNavMeshObstacle = true;
        surface.minRegionArea = 0.5f;
    }

    static void EnsureScenesInBuildSettings()
    {
        var requiredPaths = new[]
        {
            "Assets/Scenes/MainMenu.unity",
            BuildScenePath(GrammarWorldRuntimeBootstrap.TownSceneName),
            BuildScenePath(GrammarWorldRuntimeBootstrap.RouteSceneName),
            BuildScenePath(GrammarWorldRuntimeBootstrap.GymSceneName),
            BuildScenePath(GrammarWorldRuntimeBootstrap.BattleSceneName),
            "Assets/Scenes/Shop.unity",
        };
        var requiredPathSet = new HashSet<string>(requiredPaths, StringComparer.OrdinalIgnoreCase);

        var scenes = EditorBuildSettings.scenes
            .Where(scene => !RetiredProductionSceneNames.Contains(Path.GetFileNameWithoutExtension(scene.path)))
            .ToList();
        foreach (string path in requiredPaths)
        {
            if (!File.Exists(path))
                continue;
            if (scenes.Any(scene => string.Equals(scene.path, path, StringComparison.OrdinalIgnoreCase)))
                continue;
            scenes.Add(new EditorBuildSettingsScene(path, true));
        }

        foreach (EditorBuildSettingsScene scene in scenes)
        {
            if (requiredPathSet.Contains(scene.path))
                scene.enabled = true;
        }

        scenes = scenes
            .OrderBy(scene => ResolveBuildOrder(scene.path))
            .ThenBy(scene => scene.path)
            .ToList();
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    static int ResolveBuildOrder(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        if (name == "MainMenu")
            return 0;
        if (name == GrammarWorldRuntimeBootstrap.TownSceneName)
            return 1;
        if (name == GrammarWorldRuntimeBootstrap.RouteSceneName)
            return 2;
        if (name == GrammarWorldRuntimeBootstrap.GymSceneName)
            return 3;
        if (name == GrammarWorldRuntimeBootstrap.BattleSceneName)
            return 4;
        if (name == "Shop")
            return 90;
        return 50;
    }

    static int RepairMissingTemplatePlayerAnimators()
    {
        if (Enumerable.Range(0, SceneManager.sceneCount)
            .Select(SceneManager.GetSceneAt)
            .Any(scene => scene.isDirty))
        {
            throw new InvalidOperationException(
                "Animator repair stopped because an open scene has unsaved changes. Save or discard those changes before running the repair.");
        }

        string[] templatePaths =
        {
            BuildScenePath(GrammarWorldRuntimeBootstrap.TownSceneName),
            BuildScenePath(GrammarWorldRuntimeBootstrap.RouteSceneName),
            BuildScenePath(GrammarWorldRuntimeBootstrap.GymSceneName),
        };
        SceneSetup[] originalSetup = EditorSceneManager.GetSceneManagerSetup();
        int repairedPlayers = 0;
        try
        {
            foreach (string scenePath in templatePaths)
            {
                if (!File.Exists(scenePath))
                    throw new FileNotFoundException($"Template scene '{scenePath}' does not exist; repair will not create it.", scenePath);

                Scene scene = SceneManager.GetSceneByPath(scenePath);
                bool openedForRepair = !scene.IsValid() || !scene.isLoaded;
                if (openedForRepair)
                    scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

                bool sceneChanged = false;
                PlayerController[] players = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<PlayerController>(true))
                    .ToArray();
                if (players.Length == 0)
                    throw new InvalidOperationException($"Template scene '{scenePath}' has no PlayerController; Animator repair will not recreate the player rig.");

                foreach (PlayerController player in players)
                {
                    bool playerChanged = false;
                    Animator animator = player.GetComponentInChildren<Animator>(true);
                    if (animator == null)
                    {
                        animator = player.gameObject.AddComponent<Animator>();
                        playerChanged = true;
                        sceneChanged = true;
                    }

                    if (player.animator != animator)
                    {
                        player.animator = animator;
                        EditorUtility.SetDirty(player);
                        playerChanged = true;
                        sceneChanged = true;
                    }

                    if (playerChanged)
                        repairedPlayers++;
                }

                if (sceneChanged)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!EditorSceneManager.SaveScene(scene, scenePath))
                        throw new IOException($"Unity could not save repaired template scene '{scenePath}'.");
                }

                if (openedForRepair)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }
        finally
        {
            if (originalSetup != null && originalSetup.Length > 0)
                EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
        }

        return repairedPlayers;
    }

    static string BuildScenePath(string sceneName)
    {
        return $"{SceneFolder}/{sceneName}.unity";
    }
}
#endif
