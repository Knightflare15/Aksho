#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class ProductionBuildValidator : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    [MenuItem("The Script/Validate Production Content")]
    public static void ValidateFromMenu()
    {
        List<ContentValidationIssue> issues = CollectIssues();
        LogIssues(issues);
        int errors = issues.Count(issue => issue.severity == ContentValidationSeverity.Error);
        int warnings = issues.Count - errors;
        Debug.Log($"[ProductionBuildValidator] Validation complete: {errors} error(s), {warnings} warning(s).");
    }

    [MenuItem("The Script/Validate Strict Release Readiness")]
    public static void ValidateStrictFromMenu()
    {
        List<ContentValidationIssue> issues = CollectIssues();
        LogIssues(issues);
        if (issues.Count == 0)
        {
            Debug.Log("[ProductionBuildValidator] Strict release readiness passed with no errors or warnings.");
            return;
        }

        Debug.LogError($"[ProductionBuildValidator] Strict release readiness blocked by {issues.Count} issue(s). Signed releases require zero warnings.");
    }

    public static void ThrowIfStrictReleaseBlocked()
    {
        List<ContentValidationIssue> issues = CollectIssues();
        LogIssues(issues);
        if (issues.Count > 0)
            throw new BuildFailedException($"Strict release validation failed with {issues.Count} issue(s). Resolve every error and warning before signing.");
    }

    public static void RunBatchmode()
    {
        List<ContentValidationIssue> issues = CollectIssues();
        LogIssues(issues);
        int errors = issues.Count(issue => issue.severity == ContentValidationSeverity.Error);
        int warnings = issues.Count - errors;
        Debug.Log($"[ProductionBuildValidator] Validation complete: {errors} error(s), {warnings} warning(s).");
        if (Application.isBatchMode)
            EditorApplication.Exit(errors == 0 ? 0 : 1);
    }

    public static void RunStrictBatchmode()
    {
        List<ContentValidationIssue> issues = CollectIssues();
        LogIssues(issues);
        Debug.Log($"[ProductionBuildValidator] Strict release validation complete: {issues.Count} blocking issue(s).");
        if (Application.isBatchMode)
            EditorApplication.Exit(issues.Count == 0 ? 0 : 1);
    }

    public void OnPreprocessBuild(BuildReport report)
    {
        List<ContentValidationIssue> issues = CollectIssues();
        LogIssues(issues);
        List<ContentValidationIssue> errors = issues
            .Where(issue => issue.severity == ContentValidationSeverity.Error)
            .ToList();
        if (errors.Count > 0)
            throw new BuildFailedException($"Production validation failed with {errors.Count} error(s). Run The Script/Validate Production Content.");
    }

    static List<ContentValidationIssue> CollectIssues()
    {
        var issues = new List<ContentValidationIssue>();
        EnemyCatalog enemyCatalog = Resources.Load<EnemyCatalog>("EnemyCatalog_Main");
        CreatureCombatCatalog creatureCatalog = Resources.Load<CreatureCombatCatalog>("CreatureCombatCatalog_Main");

        if (enemyCatalog == null)
            issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, "Authored Resources/EnemyCatalog_Main asset is missing."));
        if (creatureCatalog == null)
            issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, "Authored Resources/CreatureCombatCatalog_Main asset is missing."));
        ValidateGrammarCreatureEnemyCatalog(enemyCatalog, creatureCatalog, issues);
        ValidateLanguageLearningContentAssets(issues);
        ValidateAuthoredGrammarProgression(issues);
        ValidateBuildScenes(issues);
        ValidatePackagedAssetBudget(issues);
        ValidateEnabledSceneContent(issues);
        return issues;
    }

    static void ValidatePackagedAssetBudget(List<ContentValidationIssue> issues)
    {
        const string zipaPath = "Assets/StreamingAssets/Zipa/model.int8.onnx";
        const string voskPath = "Assets/StreamingAssets/VoskModel";
        const string generatedAudioPath = "Assets/Resources/Audio/NpcDialogue";

        if (File.Exists(zipaPath))
        {
            issues.Add(new ContentValidationIssue(
                ContentValidationSeverity.Error,
                "The inactive ZIPA model is inside StreamingAssets and adds about 296 MB to every player. Keep model sources under ContentSource and deliver only a licensed, selected runtime model as an optional pack."));
        }

        if (Directory.Exists(voskPath))
        {
            issues.Add(new ContentValidationIssue(
                ContentValidationSeverity.Error,
                "The desktop-only Vosk model is inside StreamingAssets and will be copied into mobile builds. Keep it under ContentSource/SpeechModels and package it only for desktop."));
        }

        if (Directory.Exists(generatedAudioPath) &&
            Directory.EnumerateFiles(generatedAudioPath, "gen-*.wav", SearchOption.TopDirectoryOnly).Any())
        {
            issues.Add(new ContentValidationIssue(
                ContentValidationSeverity.Error,
                "Generated dialogue WAVs are inside Resources and therefore inflate every install. Use authored core clips plus device TTS, or ship generated voices as downloadable regional packs."));
        }

        long resourcesAudioBytes = Directory.Exists("Assets/Resources/Audio")
            ? Directory.EnumerateFiles("Assets/Resources/Audio", "*", SearchOption.AllDirectories)
                .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .Sum(path => new FileInfo(path).Length)
            : 0L;
        const long coreAudioBudgetBytes = 40L * 1024L * 1024L;
        if (resourcesAudioBytes > coreAudioBudgetBytes)
        {
            issues.Add(new ContentValidationIssue(
                ContentValidationSeverity.Warning,
                $"Core Resources audio is {resourcesAudioBytes / (1024f * 1024f):0.0} MB; keep the always-installed audio budget below 40 MB and deliver the rest on demand."));
        }
    }

    static void ValidateGrammarCreatureEnemyCatalog(
        EnemyCatalog enemyCatalog,
        CreatureCombatCatalog creatureCatalog,
        List<ContentValidationIssue> issues)
    {
        if (enemyCatalog == null || creatureCatalog == null)
            return;

        GameObject registryObject = new GameObject("ProductionBuildValidatorCreatureRegistry");
        CreatureCombatRegistry registry = registryObject.AddComponent<CreatureCombatRegistry>();
        registry.catalog = creatureCatalog;
        try
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (EnemyDefinition enemy in enemyCatalog.enemyDefinitions ?? new List<EnemyDefinition>())
            {
                if (enemy == null)
                {
                    issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, "Authored enemy catalog contains a null entry.", enemyCatalog));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(enemy.enemyId) || !ids.Add(enemy.enemyId))
                    issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Authored enemy has a missing or duplicate id: '{enemy.enemyId}'.", enemyCatalog));
                if (enemy.prefabOverride == null)
                    issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Authored enemy '{enemy.enemyId}' has no prefab.", enemyCatalog));
                if (enemy.maxHp < 1)
                    issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Authored enemy '{enemy.enemyId}' has invalid HP.", enemyCatalog));
                if (!registry.TryGetNoun(enemy.EffectiveCreatureFamilyNoun, out _))
                    issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Authored enemy '{enemy.enemyId}' has no matching creature noun '{enemy.EffectiveCreatureFamilyNoun}'.", enemyCatalog));
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(registryObject);
        }
    }

    static List<T> LoadAssets<T>() where T : UnityEngine.Object
    {
        return AssetDatabase.FindAssets($"t:{typeof(T).Name}")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<T>)
            .Where(asset => asset != null)
            .ToList();
    }

    static void ValidateBuildScenes(List<ContentValidationIssue> issues)
    {
        string[] enabledScenesInOrder = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => System.IO.Path.GetFileNameWithoutExtension(scene.path))
            .ToArray();
        var enabledScenes = new HashSet<string>(enabledScenesInOrder, StringComparer.OrdinalIgnoreCase);

        if (!enabledScenes.Contains("MainMenu"))
            issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, "MainMenu is not enabled in Build Settings."));
        if (!enabledScenes.Contains("Shop"))
            issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, "Shop is not enabled in Build Settings."));
        string[] grammarTemplateScenes =
        {
            GrammarWorldRuntimeBootstrap.TownSceneName,
            GrammarWorldRuntimeBootstrap.RouteSceneName,
            GrammarWorldRuntimeBootstrap.GymSceneName,
        };
        foreach (string templateScene in grammarTemplateScenes)
        {
            if (!enabledScenes.Contains(templateScene))
            {
                issues.Add(new ContentValidationIssue(
                    ContentValidationSeverity.Error,
                    $"Grammar RPG template scene '{templateScene}' is not enabled in Build Settings."));
            }
        }
        if (!enabledScenes.Contains(GrammarWorldRuntimeBootstrap.BattleSceneName))
            issues.Add(new ContentValidationIssue(
                ContentValidationSeverity.Error,
                $"Dedicated tactical battle scene '{GrammarWorldRuntimeBootstrap.BattleSceneName}' is not enabled in Build Settings."));

        foreach (string retired in new[] { "Level_1_Bat", "Test_Arena" })
            if (enabledScenes.Contains(retired))
                issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Retired legacy scene '{retired}' must not be enabled in production Build Settings."));
        if (enabledScenesInOrder.Length > 0 &&
            !string.Equals(enabledScenesInOrder[0], "MainMenu", StringComparison.OrdinalIgnoreCase))
            issues.Add(new ContentValidationIssue(ContentValidationSeverity.Warning, "MainMenu should be the first enabled scene in Build Settings."));

        foreach (var duplicate in enabledScenesInOrder
                     .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
            issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Scene '{duplicate.Key}' is enabled multiple times in Build Settings."));

    }

    static void ValidateEnabledSceneContent(List<ContentValidationIssue> issues)
    {
        if (AnyOpenSceneIsDirty())
        {
            issues.Add(new ContentValidationIssue(
                ContentValidationSeverity.Warning,
                "Deep scene validation skipped because one or more open scenes have unsaved changes."));
            return;
        }

        SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            foreach (EditorBuildSettingsScene buildScene in EditorBuildSettings.scenes.Where(scene => scene.enabled))
            {
                string sceneName = System.IO.Path.GetFileNameWithoutExtension(buildScene.path);
                Scene scene = EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Additive);
                ValidateScene(scene, sceneName, issues);
                EditorSceneManager.CloseScene(scene, true);
            }
        }
        finally
        {
            if (setup != null && setup.Length > 0)
                EditorSceneManager.RestoreSceneManagerSetup(setup);
        }
    }

    static void ValidateLanguageLearningContentAssets(List<ContentValidationIssue> issues)
    {
        string[] databaseGuids = AssetDatabase.FindAssets("t:ContentDatabase");
        if (databaseGuids == null || databaseGuids.Length == 0)
        {
            return;
        }

        foreach (string guid in databaseGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            ScriptableObject database = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (database == null)
                continue;

            ValidateSerializedLanguageContentDatabase(database, issues);
        }
    }

    static void ValidateAuthoredGrammarProgression(List<ContentValidationIssue> issues)
    {
        CreatureCombatCatalog catalog = Resources.Load<CreatureCombatCatalog>("CreatureCombatCatalog_Main");
        var registryObject = new GameObject("ProductionValidationCreatureRegistry");
        CreatureCombatRegistry registry = registryObject.AddComponent<CreatureCombatRegistry>();
        registry.catalog = catalog != null ? catalog : CreatureCombatCatalog.CreateRuntimeDefault();
        try
        {
            foreach (string issue in GrammarRpgContentValidation.ValidateRegions(
                         NaturalGrammarProgression.Regions,
                         registry,
                         NaturalGrammarProgression.DialogueTasks))
            {
                issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Authored grammar progression: {issue}"));
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(registryObject);
        }
    }

    static void ValidateSerializedLanguageContentDatabase(ScriptableObject database, List<ContentValidationIssue> issues)
    {
        var serialized = new SerializedObject(database);
        SerializedProperty concepts = serialized.FindProperty("grammarConcepts");
        SerializedProperty dialogues = serialized.FindProperty("dialogueLines");
        if (concepts == null || concepts.arraySize == 0)
            issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, "Language learning content has no grammar concepts.", database));
        if (dialogues == null || dialogues.arraySize == 0)
        {
            issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, "Language learning content has no dialogue lines.", database));
            return;
        }
        if (dialogues.arraySize < 12)
            issues.Add(new ContentValidationIssue(ContentValidationSeverity.Warning, "Language learning content has fewer than 12 dialogue lines; this is still demo-scale.", database));

        var conceptIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        if (concepts != null)
        {
            for (int i = 0; i < concepts.arraySize; i++)
            {
                SerializedProperty concept = concepts.GetArrayElementAtIndex(i);
                string conceptId = ReadString(concept, "conceptId");
                if (string.IsNullOrWhiteSpace(conceptId))
                    issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Grammar concept entry {i} has no id.", database));
                else if (!conceptIds.Add(conceptId.Trim()))
                    issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Grammar concept '{conceptId}' is duplicated.", database));
            }
        }

        var dialogueIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < dialogues.arraySize; i++)
        {
            SerializedProperty line = dialogues.GetArrayElementAtIndex(i);
            string id = ReadString(line, "dialogueId");
            string label = string.IsNullOrWhiteSpace(id) ? $"entry {i}" : id.Trim();
            if (string.IsNullOrWhiteSpace(id))
                issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Dialogue line entry {i} has no stable id.", database));
            else if (!dialogueIds.Add(id.Trim()))
                issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Dialogue line '{id}' is duplicated.", database));
            if (string.IsNullOrWhiteSpace(ReadString(line, "npcId")))
                issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Dialogue line '{label}' has no npcId.", database));
            if (string.IsNullOrWhiteSpace(ReadString(line, "text")))
                issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Dialogue line '{label}' has no NPC text.", database));
            if (ReadArraySize(line, "taskTypes") == 0)
                issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Dialogue line '{label}' has no task types.", database));
            if (ReadArraySize(line, "expectedAnswers") == 0 && ReadArraySize(line, "allowedAnswers") == 0)
                issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Dialogue line '{label}' has no accepted answer.", database));
            string conceptId = ReadString(line, "conceptId");
            if (!string.IsNullOrWhiteSpace(conceptId) && conceptIds.Count > 0 && !conceptIds.Contains(conceptId))
                issues.Add(new ContentValidationIssue(ContentValidationSeverity.Warning, $"Dialogue line '{label}' references unknown concept '{conceptId}'.", database));
            if (ReadArraySize(line, "hints") == 0 && string.IsNullOrWhiteSpace(ReadString(line, "localLanguageHint")))
                issues.Add(new ContentValidationIssue(ContentValidationSeverity.Warning, $"Dialogue line '{label}' has no hint support.", database));
        }
    }

    static string ReadString(SerializedProperty parent, string relativeName)
    {
        SerializedProperty property = parent?.FindPropertyRelative(relativeName);
        return property != null ? property.stringValue : "";
    }

    static int ReadArraySize(SerializedProperty parent, string relativeName)
    {
        SerializedProperty property = parent?.FindPropertyRelative(relativeName);
        return property != null && property.isArray ? property.arraySize : 0;
    }

    static bool AnyOpenSceneIsDirty()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.isDirty)
                return true;
        }

        return false;
    }

    static void ValidateScene(Scene scene, string sceneName, List<ContentValidationIssue> issues)
    {
        if (!scene.IsValid())
            return;

        bool isFrontendScene = sceneName == "MainMenu" || sceneName == "Shop";
        bool isDedicatedBattleScene = sceneName == GrammarWorldRuntimeBootstrap.BattleSceneName;
        bool isGrammarTemplateScene = GrammarWorldRuntimeBootstrap.IsGrammarWorldTemplateSceneName(sceneName);
        GameObject[] roots = scene.GetRootGameObjects();
        LevelObjectiveDirector[] objectiveDirectors = FindComponentsInScene<LevelObjectiveDirector>(roots);
        EnemyWaveDirector[] waveDirectors = FindComponentsInScene<EnemyWaveDirector>(roots);
        TranslatorBuddyService[] translatorBuddies = FindComponentsInScene<TranslatorBuddyService>(roots);
        GrammarSceneController[] grammarScenes = FindComponentsInScene<GrammarSceneController>(roots);

        foreach (LevelObjectiveDirector director in objectiveDirectors)
            ContentValidation.ValidateObjectiveDirector(director, issues);

        ValidateTranslatorBuddySceneState(sceneName, grammarScenes, translatorBuddies, issues);

        if (sceneName == "MainMenu")
            ValidateMainMenuFirebaseConfiguration(roots, issues);

        if (isFrontendScene || isDedicatedBattleScene)
            return;

        PlayerController[] players = FindComponentsInScene<PlayerController>(roots);
        CombatArenaTrigger[] triggers = FindComponentsInScene<CombatArenaTrigger>(roots);

        if (players.Length == 0)
            issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Playable scene '{sceneName}' has no PlayerController."));
        foreach (PlayerController player in players)
        {
            if (player.GetComponentInChildren<Animator>(true) == null)
                issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Playable scene '{sceneName}' player has no Animator.", player));
        }
        foreach (WordActionHandler handler in FindComponentsInScene<WordActionHandler>(roots))
            if (handler.legacyWordSpellCastingEnabled)
                issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Grammar scene '{sceneName}' enables retired spell-page combat.", handler));
        foreach (TacticalGrammarBattleController battle in FindComponentsInScene<TacticalGrammarBattleController>(roots))
        {
            if (!battle.useDedicatedBattleScene)
                continue;

            if (string.IsNullOrWhiteSpace(battle.battleSceneName))
            {
                issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Grammar scene '{sceneName}' enables dedicated tactical battles but has no battle scene name.", battle));
            }
            else if (!IsSceneEnabledInBuildSettings(battle.battleSceneName))
            {
                issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Grammar scene '{sceneName}' references tactical battle scene '{battle.battleSceneName}', but it is not enabled in Build Settings.", battle));
            }
        }
        if (FindComponentsInScene<SpellCombatHud>(roots).Length > 0 || FindComponentsInScene<GrimoireUI>(roots).Length > 0)
            issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Grammar scene '{sceneName}' contains retired spellbook UI."));
        if (isGrammarTemplateScene)
        {
            if (grammarScenes.Length == 0)
                issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Grammar template scene '{sceneName}' has no authored GrammarSceneController."));

            foreach (ProceduralGrammarSceneGenerator generator in FindComponentsInScene<ProceduralGrammarSceneGenerator>(roots))
                GrammarSliceAssetCoverageValidator.ValidateSceneGenerator(generator, issues);
        }
        else
        {
            if (waveDirectors.Length == 0)
                issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Arena scene '{sceneName}' has no EnemyWaveDirector."));
            if (objectiveDirectors.Length == 0)
                issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Arena scene '{sceneName}' has no LevelObjectiveDirector."));
            if (triggers.Length == 0)
                issues.Add(new ContentValidationIssue(ContentValidationSeverity.Warning, $"Arena scene '{sceneName}' has no CombatArenaTrigger."));
        }

        foreach (EnemyWaveDirector director in waveDirectors)
        {
            if (director.enemyCatalog == null)
                issues.Add(new ContentValidationIssue(ContentValidationSeverity.Warning, $"Arena scene '{sceneName}' has an EnemyWaveDirector without an assigned EnemyCatalog.", director));
            if (director.legacyWordSpellFallbackEnabled)
                issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Arena scene '{sceneName}' enables the retired word-spell fallback.", director));
            if (director.arenaCenter == null)
                issues.Add(new ContentValidationIssue(ContentValidationSeverity.Warning, $"Arena scene '{sceneName}' has an EnemyWaveDirector without an Arena Center.", director));
        }
    }

    static void ValidateTranslatorBuddySceneState(
        string sceneName,
        GrammarSceneController[] grammarScenes,
        TranslatorBuddyService[] translatorBuddies,
        List<ContentValidationIssue> issues)
    {
        bool grammarSceneNeedsBuddy = grammarScenes != null && grammarScenes.Length > 0;
        if (grammarSceneNeedsBuddy && (translatorBuddies == null || translatorBuddies.Length == 0))
        {
            issues.Add(new ContentValidationIssue(
                ContentValidationSeverity.Error,
                $"Grammar scene '{sceneName}' has no scene-authored TranslatorBuddyService."));
            return;
        }

        if (translatorBuddies == null)
            return;

        foreach (TranslatorBuddyService buddy in translatorBuddies)
        {
            if (buddy == null)
                continue;

            if (!buddy.enableAiTutor)
                issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Translator Buddy in scene '{sceneName}' has AI tutoring disabled.", buddy));

            if (grammarScenes != null && grammarScenes.Length > 0)
            {
                TranslatorAssistMode expected = grammarScenes[0].translatorAssist;
                if (buddy.currentAssistMode != expected)
                {
                    issues.Add(new ContentValidationIssue(
                        ContentValidationSeverity.Error,
                        $"Translator Buddy in scene '{sceneName}' has assist mode {buddy.currentAssistMode}; expected {expected}.",
                        buddy));
                }
            }

            if (buddy.allowRemoteTextFallback)
                issues.Add(new ContentValidationIssue(ContentValidationSeverity.Warning, $"Translator Buddy in scene '{sceneName}' allows remote text fallback.", buddy));
        }
    }

    static void ValidateMainMenuFirebaseConfiguration(GameObject[] roots, List<ContentValidationIssue> issues)
    {
        MainMenuController menu = FindComponentsInScene<MainMenuController>(roots).FirstOrDefault();
        if (menu == null)
        {
            issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, "MainMenu has no MainMenuController."));
            return;
        }

        var serialized = new SerializedObject(menu);
        string projectId = serialized.FindProperty("firebaseProjectId")?.stringValue ?? "";
        string functionsUrl = serialized.FindProperty("firebaseFunctionsBaseUrl")?.stringValue ?? "";
        if (string.IsNullOrWhiteSpace(projectId))
            issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, "MainMenu Firebase project id is empty.", menu));
        if (!System.Uri.TryCreate(functionsUrl, System.UriKind.Absolute, out System.Uri endpoint) || endpoint.Scheme != "https")
            issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, "MainMenu Firebase Functions URL must be an HTTPS absolute URL.", menu));
    }

    static T[] FindComponentsInScene<T>(GameObject[] roots) where T : Component
    {
        return roots
            .SelectMany(root => root.GetComponentsInChildren<T>(true))
            .ToArray();
    }

    static bool IsSceneEnabledInBuildSettings(string sceneReference)
    {
        if (string.IsNullOrWhiteSpace(sceneReference))
            return false;

        string normalizedReference = sceneReference.Trim().Replace('\\', '/');
        string referencedSceneName = Path.GetFileNameWithoutExtension(normalizedReference);
        foreach (EditorBuildSettingsScene buildScene in EditorBuildSettings.scenes)
        {
            if (!buildScene.enabled || string.IsNullOrWhiteSpace(buildScene.path))
                continue;

            string normalizedBuildPath = buildScene.path.Replace('\\', '/');
            if (string.Equals(normalizedBuildPath, normalizedReference, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    Path.GetFileNameWithoutExtension(normalizedBuildPath),
                    referencedSceneName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    static void LogIssues(IEnumerable<ContentValidationIssue> issues)
    {
        foreach (ContentValidationIssue issue in issues)
        {
            if (issue.severity == ContentValidationSeverity.Error)
                Debug.LogError($"[ProductionBuildValidator] {issue.message}", issue.context);
            else
                Debug.LogWarning($"[ProductionBuildValidator] {issue.message}", issue.context);
        }
    }
}
#endif
