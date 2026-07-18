#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Creates the deliberately small, authored production content set used by the
/// grammar-RPG scenes.  The generated assets live in source control; this tool
/// is idempotent so content can be refreshed without hand-editing scene YAML.
/// </summary>
public static class ProductionContentBootstrap
{
    const string ResourcesFolder = "Assets/Resources";
    const string ContentFolder = "Assets/Content/Production";
    const string PrefabFolder = ContentFolder + "/Prefabs";
    public const string FirebaseFunctionsBaseUrl = "https://us-central1-the-script-dea4f.cloudfunctions.net";

    [MenuItem("The Script/Production/Create Authored Game Content", false, 20)]
    public static void CreateOrRefresh()
    {
        EnsureFolders();
        GameObject impactEffect = EnsureEffectPrefab("BuddyImpactEffect", PrimitiveType.Cylinder, new Vector3(0.48f, 0.12f, 0.48f));
        SummonedCreatureActor creaturePrefab = EnsureCreaturePrefab();
        EnemyCatalog enemies = CreateOrRefreshEnemyCatalog();
        CreateOrRefreshCreatureCatalog(creaturePrefab, impactEffect);
        ConfigureProductionScenes(enemies);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[ProductionContentBootstrap] Authored grammar-creature catalogs, scene props, and scene-owned Buddy configuration are ready. Legacy word-spell systems are disabled in production scenes.");
    }

    public static void RunBatchmode()
    {
        CreateOrRefresh();
        EditorApplication.Exit(0);
    }

    static void EnsureFolders()
    {
        Directory.CreateDirectory(ResourcesFolder);
        Directory.CreateDirectory(ContentFolder);
        Directory.CreateDirectory(PrefabFolder);
    }

    static SpellCatalog CreateOrRefreshSpellCatalog(GameObject projectile, GameObject castEffect, GameObject impactEffect)
    {
        const string path = ResourcesFolder + "/SpellCatalog_Main.asset";
        SpellCatalog catalog = AssetDatabase.LoadAssetAtPath<SpellCatalog>(path);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<SpellCatalog>();
            AssetDatabase.CreateAsset(catalog, path);
        }

        catalog.spellDefinitions = new List<SpellDefinition>();
        AddSpell(catalog, "CAT", 1, "First CVC spell: clear short a.", new Color(1f, 0.92f, 0.35f), projectile, castEffect, impactEffect);
        AddSpell(catalog, "HEN", 2, "Short e CVC spell.", new Color(1f, 0.79f, 0.46f), projectile, castEffect, impactEffect);
        AddSpell(catalog, "PIG", 3, "Short i CVC spell.", new Color(0.84f, 0.63f, 1f), projectile, castEffect, impactEffect);
        AddSpell(catalog, "HOP", 4, "Short o CVC spell.", new Color(0.56f, 0.93f, 0.55f), projectile, castEffect, impactEffect);
        AddSpell(catalog, "BUG", 5, "Short u CVC spell.", new Color(0.51f, 0.87f, 1f), projectile, castEffect, impactEffect);
        AddSpell(catalog, "SHIP", 6, "Digraph sh spelling spell.", new Color(0.55f, 0.84f, 1f), projectile, castEffect, impactEffect);
        AddSpell(catalog, "THIN", 7, "Digraph th spelling spell.", new Color(0.67f, 0.97f, 0.72f), projectile, castEffect, impactEffect);
        AddSpell(catalog, "CRAB", 8, "Initial blend spelling spell.", new Color(1f, 0.65f, 0.65f), projectile, castEffect, impactEffect);
        AddSpell(catalog, "BARK", 9, "R-controlled vowel spelling spell.", new Color(0.98f, 0.76f, 0.42f), projectile, castEffect, impactEffect);
        AddSpell(catalog, "STONE", 10, "Silent-e spelling spell.", new Color(0.85f, 0.9f, 1f), projectile, castEffect, impactEffect);

        foreach (string word in new[]
        {
            "ANT", "DOG", "DUCK", "FISH", "OWL", "RAT", "BOX", "MAP", "NET", "SUN", "TOP", "VAN", "WEB", "ZIP"
        })
        {
            if (!catalog.spellDefinitions.Exists(spell => string.Equals(spell.word, word, StringComparison.OrdinalIgnoreCase)))
                AddSpell(catalog, word, 1, "Starter vocabulary spell for the authored grammar route.", Color.Lerp(new Color(0.45f, 0.78f, 1f), new Color(1f, 0.72f, 0.35f), (word[0] - 'A') / 25f), projectile, castEffect, impactEffect);
        }

        EditorUtility.SetDirty(catalog);
        return catalog;
    }

    static void AddSpell(SpellCatalog catalog, string word, int unlockLevel, string focus, Color colour, GameObject projectile, GameObject castEffect, GameObject impactEffect)
    {
        catalog.spellDefinitions.Add(new SpellDefinition
        {
            word = word,
            unlockLevel = unlockLevel,
            instructionalFocus = focus,
            projectileColour = colour,
            projectilePrefab = projectile,
            castEffectPrefab = castEffect,
            impactEffectPrefab = impactEffect,
            areaImpactEffectPrefab = impactEffect,
            projectileSpeed = 16f,
            fallbackShots = 3,
            specialAreaRadius = 5f,
        });
    }

    static EnemyCatalog CreateOrRefreshEnemyCatalog()
    {
        const string path = ResourcesFolder + "/EnemyCatalog_Main.asset";
        EnemyCatalog catalog = AssetDatabase.LoadAssetAtPath<EnemyCatalog>(path);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<EnemyCatalog>();
            AssetDatabase.CreateAsset(catalog, path);
        }

        catalog.enemyDefinitions = new List<EnemyDefinition>();
        AddEnemy(catalog, "mossy_rat", "Mossy Rat", "CAT", "RAT", 1, 3, new Color(0.39f, 0.72f, 0.48f));
        AddEnemy(catalog, "lantern_hen", "Lantern Hen", "HEN", "HEN", 2, 4, new Color(1f, 0.69f, 0.32f));
        AddEnemy(catalog, "puddle_pig", "Puddle Pig", "PIG", "PIG", 3, 5, new Color(0.96f, 0.58f, 0.75f));
        AddEnemy(catalog, "hedge_hopper", "Hedge Hopper", "HOP", "RABBIT", 4, 5, new Color(0.42f, 0.82f, 0.52f));
        AddEnemy(catalog, "book_bug", "Book Bug", "BUG", "ANT", 5, 6, new Color(0.42f, 0.67f, 0.95f));
        AddEnemy(catalog, "harbor_ship", "Harbor Ship", "SHIP", "FISH", 6, 6, new Color(0.35f, 0.58f, 0.9f));
        AddEnemy(catalog, "thistle_owl", "Thistle Owl", "THIN", "OWL", 7, 7, new Color(0.62f, 0.72f, 0.95f));
        AddEnemy(catalog, "crag_crab", "Crag Crab", "CRAB", "CRAB", 8, 8, new Color(0.92f, 0.44f, 0.38f));
        AddEnemy(catalog, "bark_hound", "Bark Hound", "BARK", "DOG", 9, 8, new Color(0.67f, 0.45f, 0.25f));
        AddEnemy(catalog, "stone_golem", "Stone Golem", "STONE", "ROCK", 10, 10, new Color(0.56f, 0.61f, 0.68f));
        EditorUtility.SetDirty(catalog);

        // An older main catalog lived at Assets/ before the Resources catalog
        // was introduced. Keep it authored too so validation cannot silently
        // select a placeholder-only duplicate in the editor.
        foreach (string guid in AssetDatabase.FindAssets("t:EnemyCatalog"))
        {
            string candidatePath = AssetDatabase.GUIDToAssetPath(guid);
            EnemyCatalog duplicate = AssetDatabase.LoadAssetAtPath<EnemyCatalog>(candidatePath);
            if (duplicate == null || duplicate == catalog)
                continue;
            duplicate.enemyDefinitions = new List<EnemyDefinition>(catalog.enemyDefinitions);
            EditorUtility.SetDirty(duplicate);
        }
        return catalog;
    }

    static void AddEnemy(EnemyCatalog catalog, string id, string name, string weakness, string creatureFamily, int unlockLevel, int hp, Color colour)
    {
        catalog.enemyDefinitions.Add(new EnemyDefinition
        {
            enemyId = id,
            displayName = name,
            // This obsolete serialized field remains populated for save compatibility only.
            // Production combat reads creatureFamilyNoun and grammar commands.
            weaknessSpell = creatureFamily,
            creatureFamilyNoun = creatureFamily,
            unlockLevel = unlockLevel,
            maxHp = hp,
            hitsToDefeat = 2,
            baseSpawnWeight = 1f,
            learningFocus = $"{name} practises the {creatureFamily.ToLowerInvariant()} noun family and grammar battle commands.",
            prefabOverride = EnsureEnemyPrefab(id, name, weakness, creatureFamily, hp, colour),
        });
    }

    static CreatureCombatCatalog CreateOrRefreshCreatureCatalog(SummonedCreatureActor prefab, GameObject effectPrefab)
    {
        const string path = ResourcesFolder + "/CreatureCombatCatalog_Main.asset";
        CreatureCombatCatalog catalog = AssetDatabase.LoadAssetAtPath<CreatureCombatCatalog>(path);
        if (catalog == null)
        {
            catalog = CreatureCombatCatalog.CreateRuntimeDefault();
            catalog.name = "CreatureCombatCatalog_Main";
            AssetDatabase.CreateAsset(catalog, path);
        }

        foreach (NounDefinition noun in catalog.nouns ?? new List<NounDefinition>())
        {
            if (noun != null && noun.IsCreatureNoun)
                noun.prefabOverride = prefab;
        }
        foreach (VerbActionDefinition verb in catalog.verbs ?? new List<VerbActionDefinition>())
        {
            if (verb != null && verb.role == BattleActionRole.Offense)
                verb.effectPrefab = effectPrefab;
        }
        EditorUtility.SetDirty(catalog);
        return catalog;
    }

    static void ConfigureProductionScenes(EnemyCatalog enemies)
    {
        GameObject template = EnsureEffectPrefab("WorldTemplateGround", PrimitiveType.Cube, new Vector3(100f, 0.25f, 100f));
        GameObject building = EnsureEffectPrefab("TownBuilding", PrimitiveType.Cube, new Vector3(6f, 5f, 6f));
        GameObject road = EnsureEffectPrefab("RouteRoad", PrimitiveType.Cube, new Vector3(4f, 0.12f, 14f));
        GameObject npc = EnsureEffectPrefab("GrammarNpc", PrimitiveType.Capsule, new Vector3(1f, 1.8f, 1f));
        GameObject treasure = EnsureEffectPrefab("RouteTreasure", PrimitiveType.Cube, new Vector3(1.1f, 0.7f, 0.9f));
        GameObject arenaProp = EnsureEffectPrefab("GymArenaProp", PrimitiveType.Cylinder, new Vector3(1.3f, 1.8f, 1.3f));
        GameObject encounter = EnsureEffectPrefab("WildEncounterMarker", PrimitiveType.Sphere, Vector3.one * 1.6f);
        SpellPillarObjective pillar = EnsureObjectivePrefab<SpellPillarObjective>("SpellPillar", PrimitiveType.Cylinder);
        CoinPickup coin = EnsureObjectivePrefab<CoinPickup>("CountingCoin", PrimitiveType.Sphere);
        ExitPortalObjective portal = EnsureObjectivePrefab<ExitPortalObjective>("ExitPortal", PrimitiveType.Cylinder);

        foreach (string scenePath in new[] { "Assets/Scenes/MainMenu.unity", "Assets/Scenes/Town.unity", "Assets/Scenes/Route.unity", "Assets/Scenes/Gym.unity", "Assets/Scenes/Shop.unity" })
        {
            if (!File.Exists(scenePath))
                continue;

            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            MainMenuController mainMenu = UnityEngine.Object.FindAnyObjectByType<MainMenuController>();
            if (mainMenu != null)
            {
                var serializedMenu = new SerializedObject(mainMenu);
                SerializedProperty endpoint = serializedMenu.FindProperty("firebaseFunctionsBaseUrl");
                if (endpoint != null)
                {
                    endpoint.stringValue = FirebaseFunctionsBaseUrl;
                    serializedMenu.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            foreach (WordActionHandler handler in UnityEngine.Object.FindObjectsByType<WordActionHandler>(FindObjectsInactive.Include))
            {
                handler.legacyWordSpellCastingEnabled = false;
                handler.spellRegistry = null;
            }
            foreach (ChallengeMode challenge in UnityEngine.Object.FindObjectsByType<ChallengeMode>(FindObjectsInactive.Include))
            {
                challenge.legacySpellLessonsEnabled = false;
                challenge.useSpellLessonSlice = false;
            }
            foreach (EnemyWaveDirector director in UnityEngine.Object.FindObjectsByType<EnemyWaveDirector>(FindObjectsInactive.Include))
            {
                director.enemyCatalog = enemies;
                director.legacyWordSpellFallbackEnabled = false;
                director.spellRegistry = null;
            }
            foreach (TacticalGrammarBattleController battle in UnityEngine.Object.FindObjectsByType<TacticalGrammarBattleController>(FindObjectsInactive.Include))
            {
                battle.useDedicatedBattleScene = true;
                battle.battleSceneName = GrammarWorldRuntimeBootstrap.BattleSceneName;
                battle.enableTacticalBattles = true;
                battle.renderPlaceholderCubes = true;
            }
            foreach (SpellCombatHud hud in UnityEngine.Object.FindObjectsByType<SpellCombatHud>(FindObjectsInactive.Include))
                UnityEngine.Object.DestroyImmediate(hud);
            foreach (GrimoireUI grimoire in UnityEngine.Object.FindObjectsByType<GrimoireUI>(FindObjectsInactive.Include))
                UnityEngine.Object.DestroyImmediate(grimoire);
            foreach (SpellRegistry registry in UnityEngine.Object.FindObjectsByType<SpellRegistry>(FindObjectsInactive.Include))
                UnityEngine.Object.DestroyImmediate(registry);
            foreach (LevelObjectiveDirector director in UnityEngine.Object.FindObjectsByType<LevelObjectiveDirector>(FindObjectsInactive.Include))
            {
                director.pillarPrefab = pillar;
                director.coinPrefab = coin;
                director.exitPortalPrefab = portal;
            }
            foreach (ProceduralGrammarSceneGenerator generator in UnityEngine.Object.FindObjectsByType<ProceduralGrammarSceneGenerator>(FindObjectsInactive.Include))
            {
                generator.prefabSet = new GrammarScenePrefabSet
                {
                    templatePrefab = template,
                    buildingPrefabs = new[] { building },
                    roadPrefabs = new[] { road },
                    npcPrefabs = new[] { npc },
                    treasurePrefabs = new[] { treasure },
                    arenaPropPrefabs = new[] { arenaProp },
                    wildEncounterTriggerPrefabs = new[] { encounter },
                };
            }

            GrammarSceneController grammarScene = UnityEngine.Object.FindAnyObjectByType<GrammarSceneController>();
            if (grammarScene != null)
                ConfigureSceneBuddy(grammarScene);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }

    static void ConfigureSceneBuddy(GrammarSceneController grammarScene)
    {
        TranslatorBuddyService buddy = UnityEngine.Object.FindAnyObjectByType<TranslatorBuddyService>();
        if (buddy == null)
            buddy = new GameObject("SceneOwnedBuddyConfiguration", typeof(TranslatorBuddyService)).GetComponent<TranslatorBuddyService>();

        buddy.currentAssistMode = grammarScene.sceneKind switch
        {
            SemanticZoneKind.Route => TranslatorAssistMode.Partial,
            SemanticZoneKind.Gym => TranslatorAssistMode.Off,
            _ => TranslatorAssistMode.Full,
        };
        // Dialogue translation remains locally cached, while adaptive coaching uses
        // the authenticated Firebase callable endpoint after student sign-in.
        buddy.providerMode = TranslatorProviderMode.TextFallback;
        buddy.requireProductionReadyEndpoints = false;
        buddy.allowRemoteTextFallback = false;
        buddy.enableAiTutor = true;
        buddy.requestSpeechAudio = false;
    }

    static GameObject EnsureProjectilePrefab()
    {
        const string path = PrefabFolder + "/SpellProjectile.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab != null)
            return prefab;

        GameObject root = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        root.name = "SpellProjectile";
        root.transform.localScale = Vector3.one * 0.28f;
        root.AddComponent<SpellProjectile>();
        prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        UnityEngine.Object.DestroyImmediate(root);
        return prefab;
    }

    static GameObject EnsureEffectPrefab(string name, PrimitiveType primitiveType, Vector3 scale)
    {
        string path = PrefabFolder + "/" + name + ".prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab != null)
            return prefab;

        GameObject root = GameObject.CreatePrimitive(primitiveType);
        root.name = name;
        root.transform.localScale = scale;
        prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        UnityEngine.Object.DestroyImmediate(root);
        return prefab;
    }

    static SummonedCreatureActor EnsureCreaturePrefab()
    {
        const string path = PrefabFolder + "/SummonedCreature.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab != null)
            return prefab.GetComponent<SummonedCreatureActor>();

        GameObject root = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        root.name = "SummonedCreature";
        SummonedCreatureActor actor = root.AddComponent<SummonedCreatureActor>();
        prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        UnityEngine.Object.DestroyImmediate(root);
        return prefab.GetComponent<SummonedCreatureActor>();
    }

    static SpellTarget EnsureEnemyPrefab(string id, string displayName, string weakness, string creatureFamily, int hp, Color colour)
    {
        string path = PrefabFolder + "/Enemy_" + id + ".prefab";
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null)
            return existing.GetComponent<SpellTarget>();

        GameObject root = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        root.name = displayName;
        root.transform.localScale = new Vector3(1.1f, 1.15f, 1.1f);
        Renderer renderer = root.GetComponent<Renderer>();
        if (renderer != null)
            renderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { color = colour };
        Collider body = root.GetComponent<Collider>();
        SpellTarget target = root.AddComponent<SpellTarget>();
        target.requiredSpell = weakness;
        target.requiredCreatureNoun = creatureFamily;
        target.maxHp = hp;
        target.hitsToDefeat = 2;
        target.targetCollider = body;
        root.AddComponent<NavMeshAgent>();
        root.AddComponent<Animator>();
        root.AddComponent<CombatActor>();
        root.AddComponent<CombatHurtbox>();
        root.AddComponent<AttackHitboxController>();
        root.AddComponent<CheeseEnemyAgent>();
        GameObject hitbox = new GameObject("MeleeHitbox", typeof(BoxCollider), typeof(CombatHitbox));
        hitbox.transform.SetParent(root.transform, false);
        hitbox.transform.localPosition = new Vector3(0f, 0.85f, 0.85f);
        hitbox.transform.localScale = new Vector3(0.8f, 0.9f, 0.8f);
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        UnityEngine.Object.DestroyImmediate(root);
        return prefab.GetComponent<SpellTarget>();
    }

    static T EnsureObjectivePrefab<T>(string name, PrimitiveType primitiveType) where T : Component
    {
        string path = PrefabFolder + "/" + name + ".prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab != null)
            return prefab.GetComponent<T>();

        GameObject root = GameObject.CreatePrimitive(primitiveType);
        root.name = name;
        T component = root.AddComponent<T>();
        prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        UnityEngine.Object.DestroyImmediate(root);
        return prefab.GetComponent<T>();
    }
}
#endif
