#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class GrammarRpgPlayableReadinessCheck
{
    static readonly string[] RequiredScenes =
    {
        "MainMenu",
        GrammarWorldRuntimeBootstrap.TownSceneName,
        GrammarWorldRuntimeBootstrap.RouteSceneName,
        GrammarWorldRuntimeBootstrap.GymSceneName,
        GrammarWorldRuntimeBootstrap.BattleSceneName,
        "Shop",
    };

    [MenuItem("The Script/Grammar Scenes/Validate Playable Fallback Flow", false, 75)]
    public static void ValidateFromMenu()
    {
        List<string> errors = CollectErrors();
        LogResult(errors);
    }

    public static void RunBatchmode()
    {
        List<string> errors = CollectErrors();
        LogResult(errors);
        if (Application.isBatchMode)
            EditorApplication.Exit(errors.Count == 0 ? 0 : 1);
    }

    public static List<string> CollectErrors()
    {
        var errors = new List<string>();
        ValidateBuildSettings(errors);
        ValidateMainMenu(errors);
        ValidateGrammarScenes(errors);
        ValidateAuthoredCombatContent(errors);
        return errors;
    }

    static void ValidateBuildSettings(List<string> errors)
    {
        HashSet<string> enabledScenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => System.IO.Path.GetFileNameWithoutExtension(scene.path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string sceneName in RequiredScenes)
        {
            if (!enabledScenes.Contains(sceneName))
                errors.Add($"Required scene '{sceneName}' is not enabled in Build Settings.");
        }
    }

    static void ValidateMainMenu(List<string> errors)
    {
        if (!TryOpenScene("MainMenu", errors))
            return;

        if (UnityEngine.Object.FindAnyObjectByType<MainMenuController>() == null)
            errors.Add("MainMenu has no MainMenuController.");

        bool hadManager = UnityEngine.Object.FindAnyObjectByType<WorldSessionManager>() != null;
        WorldSessionManager manager = WorldSessionManager.EnsureExists();
        if (manager == null)
            errors.Add("MainMenu cannot create a WorldSessionManager for Play/Continue flow.");
        if (!hadManager && manager != null)
            UnityEngine.Object.DestroyImmediate(manager.gameObject);
    }

    static void ValidateGrammarScenes(List<string> errors)
    {
        foreach (string sceneName in new[]
        {
            GrammarWorldRuntimeBootstrap.TownSceneName,
            GrammarWorldRuntimeBootstrap.RouteSceneName,
            GrammarWorldRuntimeBootstrap.GymSceneName,
        })
        {
            if (!TryOpenScene(sceneName, errors))
                continue;

            GrammarSceneController controller = UnityEngine.Object.FindAnyObjectByType<GrammarSceneController>();
            if (controller == null)
            {
                errors.Add($"{sceneName} has no GrammarSceneController.");
                continue;
            }

            ProceduralGrammarSceneGenerator generator = UnityEngine.Object.FindAnyObjectByType<ProceduralGrammarSceneGenerator>();
            if (generator == null)
                errors.Add($"{sceneName} has no ProceduralGrammarSceneGenerator for authored world content.");
            else
                ValidateAuthoredScenePrefabs(sceneName, generator, errors);

            if (UnityEngine.Object.FindAnyObjectByType<PlayerController>() == null)
                errors.Add($"{sceneName} has no PlayerController.");

            PlayerController player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
            if (player != null && player.GetComponentInChildren<Animator>(true) == null)
                errors.Add($"{sceneName} player has no Animator.");

            TranslatorBuddyService buddy = UnityEngine.Object.FindAnyObjectByType<TranslatorBuddyService>();
            if (buddy == null)
            {
                errors.Add($"{sceneName} has no scene-owned TranslatorBuddyService.");
            }
            else
            {
                TranslatorAssistMode expectedAssist = controller.translatorAssist;
                if (buddy.currentAssistMode != expectedAssist)
                    errors.Add($"{sceneName} Buddy assist mode is {buddy.currentAssistMode}, expected {expectedAssist}.");
                if (!buddy.enableAiTutor)
                    errors.Add($"{sceneName} Buddy has AI tutoring disabled.");
            }

            if (sceneName != GrammarWorldRuntimeBootstrap.TownSceneName &&
                UnityEngine.Object.FindAnyObjectByType<EnemyWaveDirector>() == null)
            {
                errors.Add($"{sceneName} has no EnemyWaveDirector for trainer/encounter battles.");
            }
        }

    }

    static void ValidateAuthoredScenePrefabs(string sceneName, ProceduralGrammarSceneGenerator generator, List<string> errors)
    {
        GrammarScenePrefabSet prefabs = generator.prefabSet;
        if (prefabs == null || prefabs.templatePrefab == null)
            errors.Add($"{sceneName} has no authored world template prefab.");
        if (prefabs == null || prefabs.npcPrefabs == null || prefabs.npcPrefabs.Length == 0)
            errors.Add($"{sceneName} has no authored NPC prefab.");
        if (controllerNeedsRouteProps(generator) &&
            (prefabs == null || prefabs.roadPrefabs == null || prefabs.roadPrefabs.Length == 0))
        {
            errors.Add($"{sceneName} has no authored route/road prefab.");
        }
    }

    static bool controllerNeedsRouteProps(ProceduralGrammarSceneGenerator generator)
    {
        return generator != null && generator.sceneController != null &&
               generator.sceneController.sceneKind != SemanticZoneKind.Gym;
    }

    static void ValidateAuthoredCombatContent(List<string> errors)
    {
        EnemyCatalog enemyCatalog = Resources.Load<EnemyCatalog>("EnemyCatalog_Main");
        CreatureCombatCatalog catalog = Resources.Load<CreatureCombatCatalog>("CreatureCombatCatalog_Main");
        if (enemyCatalog == null)
            errors.Add("Authored EnemyCatalog_Main resource is missing.");
        if (catalog == null)
            errors.Add("Authored CreatureCombatCatalog_Main resource is missing.");
        if (enemyCatalog == null || catalog == null)
            return;

        GameObject registryObject = new GameObject("ReadinessCheckCreatureCombatRegistry");
        CreatureCombatRegistry registry = registryObject.AddComponent<CreatureCombatRegistry>();
        registry.catalog = catalog;

        try
        {
            ValidateAuthoredCombatCatalog(catalog, registry, errors);
            ValidateAuthoredEnemyCatalog(enemyCatalog, registry, errors);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(registryObject);
        }
    }

    static void ValidateAuthoredEnemyCatalog(EnemyCatalog catalog, CreatureCombatRegistry registry, List<string> errors)
    {
        if (catalog.enemyDefinitions == null || catalog.enemyDefinitions.Count == 0)
        {
            errors.Add("Authored enemy catalog has no enemies.");
            return;
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (EnemyDefinition enemy in catalog.enemyDefinitions)
        {
            if (enemy == null)
            {
                errors.Add("Authored enemy catalog contains a null enemy.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(enemy.enemyId) || !ids.Add(enemy.enemyId))
                errors.Add($"Authored enemy has a missing or duplicate id: '{enemy.enemyId}'.");
            if (enemy.prefabOverride == null)
                errors.Add($"Authored enemy '{enemy.enemyId}' has no prefab.");
            if (enemy.maxHp < 1)
                errors.Add($"Authored enemy '{enemy.enemyId}' has invalid HP.");
            if (!registry.TryGetNoun(enemy.EffectiveCreatureFamilyNoun, out _))
                errors.Add($"Authored enemy '{enemy.enemyId}' has no matching creature noun '{enemy.EffectiveCreatureFamilyNoun}'.");
        }
    }

    static void ValidateAuthoredCombatCatalog(CreatureCombatCatalog catalog, CreatureCombatRegistry registry, List<string> errors)
    {
        if (!registry.TryGetVerb("ATTACK", out _))
            errors.Add("Authored creature catalog does not recognize ATTACK.");
        if (!registry.TryGetVerb("DEFEND", out _))
            errors.Add("Authored creature catalog does not recognize DEFEND.");
        if (!registry.TryGetVerb("MOVE", out _))
            errors.Add("Authored creature catalog does not recognize MOVE.");

        NounDefinition firstNoun = catalog.nouns != null && catalog.nouns.Count > 0 ? catalog.nouns[0] : null;
        if (firstNoun == null)
        {
            errors.Add("Authored creature catalog has no summonable nouns.");
            return;
        }

        foreach (string verb in new[] { "ATTACK", "RUN", "BLOCK", "HIDE" })
        {
            if (!firstNoun.AllowsVerb(verb))
                errors.Add($"Authored noun '{firstNoun.canonicalNoun}' cannot use required verb '{verb}'.");
        }

        var battler = new TacticalGrammarBattler(registry);
        TacticalBattleCommandResult summon = battler.SummonPlayer(firstNoun.canonicalNoun, new TacticalBattlePosition(0, 2));
        if (!summon.success)
            errors.Add($"Tactical authored battle could not summon '{firstNoun.canonicalNoun}': {summon.message}");

        foreach (string command in new[]
        {
            $"{firstNoun.canonicalNoun} attacks",
            $"{firstNoun.canonicalNoun} moves across bridge",
            $"{firstNoun.canonicalNoun} moves through bridge",
            $"{firstNoun.canonicalNoun} defends",
        })
        {
            TacticalBattleCommandResult result = battler.ExecutePlayerCommand(command);
            if (!result.success)
                errors.Add($"Tactical authored command failed: '{command}' -> {result.message}");
        }
    }

    static bool TryOpenScene(string sceneName, List<string> errors)
    {
        string path = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .FirstOrDefault(path => string.Equals(
                System.IO.Path.GetFileNameWithoutExtension(path),
                sceneName,
                StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(path))
        {
            errors.Add($"Scene '{sceneName}' is not enabled in Build Settings.");
            return false;
        }

        EditorSceneManager.OpenScene(path);
        return true;
    }

    static void LogResult(List<string> errors)
    {
        if (errors.Count == 0)
        {
            Debug.Log("[GrammarRpgPlayableReadinessCheck] PASS: login/start, authored scenes, scene-owned Buddy, authored catalogs, and tactical commands are structurally playable.");
            return;
        }

        foreach (string error in errors)
            Debug.LogError($"[GrammarRpgPlayableReadinessCheck] {error}");
        Debug.LogError($"[GrammarRpgPlayableReadinessCheck] FAIL: {errors.Count} issue(s).");
    }
}
#endif
