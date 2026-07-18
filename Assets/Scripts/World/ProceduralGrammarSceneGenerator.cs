using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

[Serializable]
public sealed class GrammarScenePrefabSet
{
    public GameObject templatePrefab;
    public GameObject[] buildingPrefabs;
    public GameObject[] roadPrefabs;
    public GameObject[] npcPrefabs;
    public GameObject[] treasurePrefabs;
    public GameObject[] arenaPropPrefabs;
    public GameObject[] wildEncounterTriggerPrefabs;
}

[DisallowMultipleComponent]
[AddComponentMenu("The Script/Grammar/Procedural Grammar Scene Generator")]
public sealed class ProceduralGrammarSceneGenerator : MonoBehaviour
{
    const string RootName = "GeneratedGrammarSceneRoot";

    [Header("Scene Source")]
    public GrammarSceneController sceneController;
    public GrammarScenePrefabSet prefabSet = new GrammarScenePrefabSet();
    public bool generateOnStart = true;
    public bool clearExistingGeneratedContent = true;
    [Min(0)] public int startDelayFrames = 1;

    [Header("Seed")]
    public int seed = 11037;
    public bool includeTopicInSeed = true;
    public bool randomizeRoutesEveryGeneration = true;

    [Header("Footprint")]
    public Vector2 townSize = new Vector2(64f, 64f);
    public Vector2 routeSize = new Vector2(92f, 92f);
    public float gymRadius = 28f;
    public float groundThickness = 0.25f;

    [Header("Counts")]
    [Min(0)] public int townBuildingCount = 12;
    [Min(0)] public int townNpcCount = 5;
    [Min(0)] public int routeNpcCount = 4;
    [Min(0)] public int routeTreasureCount = 5;
    [Min(0)] public int routeWildEncounterCount = 5;
    [Min(0)] public int gymPropCount = 8;

    [Header("Roads")]
    [Min(1)] public int townRoadColumns = 3;
    [Min(1)] public int townRoadRows = 3;
    [Min(0.5f)] public float roadWidth = 3.2f;
    [Min(0f)] public float lotSetback = 5f;
    [Min(0f)] public float placementJitter = 2.25f;

    [Header("Fallback Colors")]
    public Color groundColor = new Color(0.23f, 0.34f, 0.26f, 1f);
    public Color roadColor = new Color(0.34f, 0.31f, 0.27f, 1f);
    public Color buildingColor = new Color(0.46f, 0.48f, 0.52f, 1f);
    public Color treasureColor = new Color(1f, 0.74f, 0.22f, 1f);
    public Color arenaPropColor = new Color(0.64f, 0.42f, 0.25f, 1f);

    Transform generatedRoot;
    int runtimeGenerationSalt;

    void Start()
    {
        if (generateOnStart)
            StartCoroutine(GenerateAfterDelay());
    }

    IEnumerator GenerateAfterDelay()
    {
        int frames = Mathf.Max(0, startDelayFrames);
        for (int i = 0; i < frames; i++)
            yield return null;
        Generate();
    }

    [ContextMenu("Generate Grammar Scene")]
    public void Generate()
    {
        ResolveController();
        if (sceneController == null)
        {
            Debug.LogWarning("[ProceduralGrammarSceneGenerator] No GrammarSceneController found.", this);
            return;
        }

        if (clearExistingGeneratedContent)
            ClearGeneratedContent();

        runtimeGenerationSalt = sceneController.sceneKind == SemanticZoneKind.Route && randomizeRoutesEveryGeneration
            ? Environment.TickCount
            : 0;
        EnsureRoot();
        SpawnTemplateOrGround();

        switch (sceneController.sceneKind)
        {
            case SemanticZoneKind.Gym:
                GenerateGym();
                break;
            case SemanticZoneKind.Route:
                GenerateRoute();
                break;
            default:
                GenerateTown();
                break;
        }

        SpawnSceneMarker();
    }

    [ContextMenu("Clear Generated Grammar Scene")]
    public void ClearGeneratedContent()
    {
        Transform root = generatedRoot != null ? generatedRoot : transform.Find(RootName);
        if (root != null)
            DestroyGameObject(root.gameObject);
        generatedRoot = null;
    }

    void GenerateTown()
    {
        System.Random rng = CreateRng(0x544F574E);
        SpawnTownRoadGrid(rng);
        SpawnBuildings(rng, townBuildingCount, townSize, false);
        SpawnGeneratedNpcs(rng, townNpcCount, false);
    }

    void GenerateRoute()
    {
        System.Random rng = CreateRng(0x524F5554);
        GrammarEncounterMode encounterMode = NaturalGrammarProgression.ResolveEncounterMode(sceneController.grammarTopic, sceneController.grammarTopicTier);
        bool combatUnlocked = NaturalGrammarProgression.IsTacticalCombatUnlocked(sceneController.grammarTopic, sceneController.grammarTopicTier) ||
            encounterMode == GrammarEncounterMode.NounRecognition;
        SpawnRouteTrail(rng);
        SpawnGeneratedNpcs(rng, routeNpcCount, combatUnlocked);
        SpawnTreasures(rng, routeTreasureCount, routeSize);
        if (combatUnlocked)
            SpawnWildEncounterTriggers(rng, routeWildEncounterCount, routeSize);
    }

    void GenerateGym()
    {
        System.Random rng = CreateRng(0x47594D21);
        SpawnGymArena(rng);
        SpawnArenaProps(rng, gymPropCount);
        SpawnGymLeader(rng);
    }

    void SpawnTemplateOrGround()
    {
        if (prefabSet != null && prefabSet.templatePrefab != null)
        {
            GameObject template = Instantiate(prefabSet.templatePrefab, generatedRoot);
            template.name = $"Template_{sceneController.sceneKind}";
            return;
        }

        Vector2 footprint = ResolveFootprint();
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = $"FallbackGround_{sceneController.sceneKind}";
        ground.transform.SetParent(generatedRoot, false);
        ground.transform.localPosition = new Vector3(0f, -groundThickness * 0.5f, 0f);
        ground.transform.localScale = new Vector3(footprint.x, groundThickness, footprint.y);
        ApplyMaterial(ground, ResolveGroundColor());
    }

    void SpawnTownRoadGrid(System.Random rng)
    {
        List<float> columns = BuildAxisLines(townRoadColumns, townSize.x, rng);
        List<float> rows = BuildAxisLines(townRoadRows, townSize.y, rng);
        foreach (float x in columns)
            SpawnRoad(new Vector3(x, 0.03f, 0f), new Vector4(roadWidth, 0.08f, townSize.y + roadWidth, 1f));
        foreach (float z in rows)
            SpawnRoad(new Vector3(0f, 0.04f, z), new Vector4(townSize.x + roadWidth, 0.08f, roadWidth, 1f));
    }

    void SpawnRouteTrail(System.Random rng)
    {
        int segments = 7;
        float length = routeSize.y * 0.86f / segments;
        float z = -routeSize.y * 0.43f + length * 0.5f;
        float x = 0f;
        for (int i = 0; i < segments; i++)
        {
            x += RandomRange(rng, -5f, 5f);
            SpawnRoad(new Vector3(x, 0.04f, z + i * length), new Vector4(roadWidth * 1.4f, 0.08f, length + 3f, 1f));
        }
    }

    void SpawnGymArena(System.Random rng)
    {
        GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "FallbackGymArena";
        ring.transform.SetParent(generatedRoot, false);
        ring.transform.localPosition = new Vector3(0f, 0.02f, 0f);
        ring.transform.localScale = new Vector3(gymRadius * 1.8f, 0.08f, gymRadius * 1.8f);
        ApplyMaterial(ring, ResolveRoadColor());

        SpawnRoad(new Vector3(0f, 0.08f, -gymRadius * 0.55f), new Vector4(roadWidth * 1.8f, 0.1f, gymRadius * 0.9f, 1f));
    }

    void SpawnBuildings(System.Random rng, int count, Vector2 footprint, bool trainerBuildings)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 position = PickEdgeBiasedPosition(rng, footprint, lotSetback);
            Vector3 scale = new Vector3(RandomRange(rng, 4f, 8f), RandomRange(rng, 3.5f, 7f), RandomRange(rng, 4f, 8f));
            GameObject instance = SpawnFromSet(prefabSet?.buildingPrefabs, "GeneratedBuilding", position, Quaternion.Euler(0f, RandomRange(rng, 0f, 360f), 0f));
            if (instance == null)
            {
                instance = GameObject.CreatePrimitive(PrimitiveType.Cube);
                instance.name = $"FallbackBuilding_{i:00}";
                instance.transform.SetParent(generatedRoot, false);
                instance.transform.SetPositionAndRotation(position + Vector3.up * (scale.y * 0.5f), Quaternion.Euler(0f, RandomRange(rng, 0f, 360f), 0f));
                instance.transform.localScale = scale;
                ApplyMaterial(instance, ResolveBuildingColor());
            }
            else
            {
                GroundPlacedObject(instance.transform, position);
            }
        }
    }

    void SpawnGeneratedNpcs(System.Random rng, int count, bool routeTrainerBattles)
    {
        Vector2 footprint = ResolveFootprint();
        bool combatUnlocked = NaturalGrammarProgression.IsTacticalCombatUnlocked(sceneController.grammarTopic, sceneController.grammarTopicTier);
        bool trainerBattlesEnabled = routeTrainerBattles && combatUnlocked;
        string[] requiredTasks = NaturalGrammarProgression.GetDialogueTaskIds(
            sceneController.sceneKind,
            sceneController.grammarTopic,
            sceneController.grammarTopicTier);
        int authoredTaskCount = requiredTasks != null ? requiredTasks.Length : 0;
        int spawnCount = authoredTaskCount > 0
            ? authoredTaskCount
            : Mathf.Max(1, count);
        for (int i = 0; i < spawnCount; i++)
        {
            Vector3 position = PickOpenPosition(rng, footprint, 6f);
            string displayName = sceneController.sceneKind == SemanticZoneKind.Route
                ? NaturalGrammarProgression.ResolveRouteNpcName(sceneController.grammarTopic, sceneController.grammarTopicTier, i)
                : NaturalGrammarProgression.ResolveTownNpcName(sceneController.grammarTopic, sceneController.grammarTopicTier, i);
            var dialogueLines = new List<LocalizedDialogueLine>
            {
                NaturalGrammarProgression.BuildGeneratedDialogue(
                    sceneController.sceneKind,
                    sceneController.grammarTopic,
                    sceneController.grammarTopicTier,
                    i,
                    trainerBattlesEnabled),
            };

            var definition = new GrammarNpcSpawnDefinition
            {
                npcId = $"generated-{sceneController.sceneKind.ToString().ToLowerInvariant()}-npc-{i}",
                displayName = displayName,
                position = position,
                eulerAngles = new Vector3(0f, RandomRange(rng, 0f, 360f), 0f),
                prefabOverride = PickPrefab(prefabSet?.npcPrefabs, rng),
                dialogueLines = dialogueLines,
                startsTrainerBattle = trainerBattlesEnabled,
                trainerEnemyCount = trainerBattlesEnabled
                    ? NaturalGrammarProgression.ResolveTrainerBattleEnemyCount(sceneController.grammarTopic, sceneController.grammarTopicTier, i)
                    : 1,
                trainerEncounterNounFamilies = trainerBattlesEnabled
                    ? NaturalGrammarProgression.BuildTrainerBattleNounFamilies(sceneController.grammarTopic, sceneController.grammarTopicTier, i)
                    : new List<string>(),
                trainerPracticePatterns = trainerBattlesEnabled
                    ? NaturalGrammarProgression.BuildTrainerBattlePracticePatterns(sceneController.grammarTopic, sceneController.grammarTopicTier, i)
                    : new List<GrammarPhrasePattern>(),
                trainerMasteryTags = trainerBattlesEnabled
                    ? NaturalGrammarProgression.BuildTrainerBattleMasteryTags(sceneController.grammarTopic, sceneController.grammarTopicTier, i)
                    : new List<string>(),
            };

            SpawnNpc(definition, ResolveGeneratedNpcAssistMode(sceneController.sceneKind));
        }
    }

    void SpawnGymLeader(System.Random rng)
    {
        GrammarEncounterMode encounterMode = NaturalGrammarProgression.ResolveEncounterMode(
            sceneController.grammarTopic,
            sceneController.grammarTopicTier);
        bool startsBossEncounter =
            NaturalGrammarProgression.IsTacticalCombatUnlocked(sceneController.grammarTopic, sceneController.grammarTopicTier) ||
            encounterMode == GrammarEncounterMode.NounRecognition;
        int leaderPoolIndex = 1;
        var definition = new GrammarNpcSpawnDefinition
        {
            npcId = "generated-gym-leader",
            displayName = NaturalGrammarProgression.ResolveGymLeaderName(sceneController.grammarTopic, sceneController.grammarTopicTier),
            position = new Vector3(0f, 0f, gymRadius * 0.42f),
            eulerAngles = new Vector3(0f, 180f, 0f),
            prefabOverride = PickPrefab(prefabSet?.npcPrefabs, rng),
            startsTrainerBattle = startsBossEncounter,
            trainerEnemyCount = startsBossEncounter
                ? Mathf.Max(4, NaturalGrammarProgression.ResolveTrainerBattleEnemyCount(sceneController.grammarTopic, sceneController.grammarTopicTier, leaderPoolIndex))
                : 1,
            dialogueLines = NaturalGrammarProgression.BuildGeneratedDialogueSet(
                SemanticZoneKind.Gym,
                sceneController.grammarTopic,
                sceneController.grammarTopicTier,
                startsBossEncounter),
            trainerEncounterNounFamilies = startsBossEncounter
                ? NaturalGrammarProgression.BuildTrainerBattleNounFamilies(sceneController.grammarTopic, sceneController.grammarTopicTier, leaderPoolIndex)
                : NaturalGrammarProgression.BuildCurrentNounFamilies(sceneController.grammarTopic, sceneController.grammarTopicTier),
            trainerPracticePatterns = startsBossEncounter
                ? NaturalGrammarProgression.BuildTrainerBattlePracticePatterns(sceneController.grammarTopic, sceneController.grammarTopicTier, leaderPoolIndex)
                : new List<GrammarPhrasePattern>(),
            trainerMasteryTags = startsBossEncounter
                ? NaturalGrammarProgression.BuildTrainerBattleMasteryTags(sceneController.grammarTopic, sceneController.grammarTopicTier, leaderPoolIndex)
                : new List<string>(),
        };
        SpawnNpc(definition, forceAssistMode: TranslatorAssistMode.Off);
    }

    void SpawnNpc(GrammarNpcSpawnDefinition definition, TranslatorAssistMode? forceAssistMode = null)
    {
        GameObject instance = definition.prefabOverride != null
            ? Instantiate(definition.prefabOverride, definition.position, Quaternion.Euler(definition.eulerAngles), generatedRoot)
            : GameObject.CreatePrimitive(PrimitiveType.Cube);

        instance.name = $"GeneratedNPC_{definition.displayName}_{definition.npcId}";
        if (definition.prefabOverride == null)
        {
            instance.transform.SetParent(generatedRoot, true);
            instance.transform.SetPositionAndRotation(definition.position, Quaternion.Euler(definition.eulerAngles));
            instance.transform.localScale = new Vector3(1.1f, 1.8f, 1.1f);
            ApplyMaterial(instance, ResolveNpcColor());
        }
        else
        {
            GroundPlacedObject(instance.transform, definition.position);
        }

        GrammarNpc npc = instance.GetComponent<GrammarNpc>();
        if (npc == null)
            npc = instance.AddComponent<GrammarNpc>();
        npc.Configure(
            definition,
            sceneController.sceneKind,
            sceneController.grammarTopic,
            sceneController.grammarTopicTier,
            forceAssistMode ?? sceneController.translatorAssist);
    }

    static TranslatorAssistMode ResolveGeneratedNpcAssistMode(SemanticZoneKind zoneKind)
    {
        return zoneKind switch
        {
            SemanticZoneKind.Gym => TranslatorAssistMode.Off,
            SemanticZoneKind.Route => TranslatorAssistMode.Partial,
            _ => TranslatorAssistMode.Full,
        };
    }

    void SpawnTreasures(System.Random rng, int count, Vector2 footprint)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 position = PickOpenPosition(rng, footprint, 7f);
            GameObject instance = SpawnFromSet(prefabSet?.treasurePrefabs, "GeneratedRouteTreasure", position, Quaternion.identity);
            if (instance == null)
            {
                instance = GameObject.CreatePrimitive(PrimitiveType.Cube);
                instance.name = $"FallbackRouteTreasure_{i:00}";
                instance.transform.SetParent(generatedRoot, false);
                instance.transform.position = position + Vector3.up * 0.35f;
                instance.transform.localScale = new Vector3(1.1f, 0.7f, 0.9f);
                ApplyMaterial(instance, ResolveAccentColor());
            }
            else
            {
                GroundPlacedObject(instance.transform, position);
            }
        }
    }

    void SpawnWildEncounterTriggers(System.Random rng, int count, Vector2 footprint)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 position = PickOpenPosition(rng, footprint, 8f);
            GameObject instance = SpawnFromSet(prefabSet?.wildEncounterTriggerPrefabs, "GeneratedWildEncounterTrigger", position, Quaternion.identity);
            if (instance == null)
            {
                instance = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                instance.name = $"FallbackWildEncounterTrigger_{i:00}";
                instance.transform.SetParent(generatedRoot, false);
                instance.transform.position = position + Vector3.up * 0.4f;
                instance.transform.localScale = Vector3.one * 1.6f;
                Color accent = ResolveAccentColor();
                ApplyMaterial(instance, new Color(accent.r, accent.g, accent.b, 0.55f));
            }
            else
            {
                GroundPlacedObject(instance.transform, position);
            }

            Collider collider = instance.GetComponent<Collider>();
            if (collider != null)
                collider.isTrigger = true;

            EnemyWaveTrigger trigger = instance.GetComponent<EnemyWaveTrigger>();
            if (trigger == null)
                trigger = instance.AddComponent<EnemyWaveTrigger>();
            trigger.semanticZoneKind = SemanticZoneKind.Route;
            trigger.grammarTopic = sceneController.grammarTopic;
            trigger.grammarTopicTier = sceneController.grammarTopicTier;
            trigger.translatorAssist = TranslatorAssistMode.Partial;
            trigger.enemyCount = NaturalGrammarProgression.ResolveWildEncounterEnemyCount(
                sceneController.grammarTopic,
                sceneController.grammarTopicTier,
                i);
            trigger.practicePatterns = NaturalGrammarProgression.BuildWildEncounterPracticePatterns(
                sceneController.grammarTopic,
                sceneController.grammarTopicTier,
                i);
            trigger.masteryTags = NaturalGrammarProgression.BuildWildEncounterMasteryTags(
                sceneController.grammarTopic,
                sceneController.grammarTopicTier,
                i);
            List<string> configuredNouns = NaturalGrammarProgression.BuildWildEncounterNounFamilies(
                sceneController.grammarTopic,
                sceneController.grammarTopicTier,
                i);
            trigger.encounterNounFamilies = GrammarRouteContext.Instance.ResolveEncounterNounFamilies(
                FindAnyObjectByType<CreatureCombatRegistry>(),
                SemanticZoneKind.Route,
                sceneController.grammarTopic,
                sceneController.grammarTopicTier,
                configuredNouns);
        }
    }

    void SpawnArenaProps(System.Random rng, int count)
    {
        for (int i = 0; i < count; i++)
        {
            float angle = (Mathf.PI * 2f * i / Mathf.Max(1, count)) + RandomRange(rng, -0.18f, 0.18f);
            float radius = RandomRange(rng, gymRadius * 0.62f, gymRadius * 0.9f);
            Vector3 position = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            GameObject instance = SpawnFromSet(prefabSet?.arenaPropPrefabs, "GeneratedGymProp", position, Quaternion.Euler(0f, -angle * Mathf.Rad2Deg, 0f));
            if (instance == null)
            {
                instance = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                instance.name = $"FallbackGymProp_{i:00}";
                instance.transform.SetParent(generatedRoot, false);
                instance.transform.position = position + Vector3.up * 1f;
                instance.transform.localScale = new Vector3(1.2f, 2f, 1.2f);
                ApplyMaterial(instance, ResolveAccentColor());
            }
            else
            {
                GroundPlacedObject(instance.transform, position);
            }
        }
    }

    void SpawnRoad(Vector3 position, Vector4 scale)
    {
        GameObject instance = SpawnFromSet(prefabSet?.roadPrefabs, "GeneratedRoad", position, Quaternion.identity);
        if (instance == null)
        {
            instance = GameObject.CreatePrimitive(PrimitiveType.Cube);
            instance.name = "FallbackRoad";
            instance.transform.SetParent(generatedRoot, false);
            instance.transform.localPosition = position;
            instance.transform.localScale = new Vector3(scale.x, scale.y, scale.z);
            ApplyMaterial(instance, ResolveRoadColor());
        }
        else
        {
            instance.transform.localScale = new Vector3(scale.x, scale.y, scale.z);
            GroundPlacedObject(instance.transform, position);
        }
    }

    GameObject SpawnFromSet(GameObject[] prefabs, string fallbackName, Vector3 position, Quaternion rotation)
    {
        GameObject prefab = PickPrefab(prefabs, CreateRng(position.GetHashCode()));
        if (prefab == null)
            return null;

        GameObject instance = Instantiate(prefab, position, rotation, generatedRoot);
        instance.name = fallbackName;
        return instance;
    }

    GameObject PickPrefab(GameObject[] prefabs, System.Random rng)
    {
        if (prefabs == null || prefabs.Length == 0 || rng == null)
            return null;

        List<GameObject> usable = new List<GameObject>();
        foreach (GameObject prefab in prefabs)
            if (prefab != null)
                usable.Add(prefab);
        return usable.Count == 0 ? null : usable[rng.Next(0, usable.Count)];
    }

    Vector3 PickEdgeBiasedPosition(System.Random rng, Vector2 footprint, float inset)
    {
        bool nearHorizontalRoad = rng.NextDouble() < 0.5;
        float x = RandomRange(rng, -footprint.x * 0.45f, footprint.x * 0.45f);
        float z = RandomRange(rng, -footprint.y * 0.45f, footprint.y * 0.45f);
        if (nearHorizontalRoad)
            z = Mathf.Round(z / Mathf.Max(1f, footprint.y / Mathf.Max(1, townRoadRows))) * (footprint.y / Mathf.Max(1, townRoadRows)) + RandomSign(rng) * inset;
        else
            x = Mathf.Round(x / Mathf.Max(1f, footprint.x / Mathf.Max(1, townRoadColumns))) * (footprint.x / Mathf.Max(1, townRoadColumns)) + RandomSign(rng) * inset;
        x += RandomRange(rng, -placementJitter, placementJitter);
        z += RandomRange(rng, -placementJitter, placementJitter);
        return GroundPosition(new Vector3(
            Mathf.Clamp(x, -footprint.x * 0.48f, footprint.x * 0.48f),
            0f,
            Mathf.Clamp(z, -footprint.y * 0.48f, footprint.y * 0.48f)));
    }

    Vector3 PickOpenPosition(System.Random rng, Vector2 footprint, float inset)
    {
        float x = RandomRange(rng, -footprint.x * 0.5f + inset, footprint.x * 0.5f - inset);
        float z = RandomRange(rng, -footprint.y * 0.5f + inset, footprint.y * 0.5f - inset);
        return GroundPosition(new Vector3(x, 0f, z));
    }

    Vector3 GroundPosition(Vector3 position)
    {
        Vector3 rayStart = position + Vector3.up * 80f;
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 160f, ~0, QueryTriggerInteraction.Ignore))
            return hit.point;
        return position;
    }

    void GroundPlacedObject(Transform target, Vector3 position)
    {
        if (target == null)
            return;
        target.position = GroundPosition(position);
    }

    void SpawnSceneMarker()
    {
        NaturalGrammarRegion region = ResolveRegion();
        string title = !string.IsNullOrWhiteSpace(region?.displayName)
            ? region.displayName
            : !string.IsNullOrWhiteSpace(sceneController?.mapDisplayName)
                ? sceneController.mapDisplayName
                : sceneController != null ? sceneController.grammarTopic : "Grammar Area";

        GameObject markerRoot = new GameObject("SceneMarker");
        markerRoot.transform.SetParent(generatedRoot, false);

        Vector2 footprint = ResolveFootprint();
        float zOffset = sceneController != null && sceneController.sceneKind == SemanticZoneKind.Gym
            ? gymRadius * 0.72f
            : footprint.y * 0.5f - 5f;
        markerRoot.transform.localPosition = new Vector3(0f, 0f, zOffset);
        markerRoot.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cube);
        post.name = "SceneMarker_Post";
        post.transform.SetParent(markerRoot.transform, false);
        post.transform.localPosition = new Vector3(0f, 1.8f, 0f);
        post.transform.localScale = new Vector3(0.38f, 3.6f, 0.38f);
        ApplyMaterial(post, ResolveBuildingColor());

        GameObject board = GameObject.CreatePrimitive(PrimitiveType.Cube);
        board.name = "SceneMarker_Board";
        board.transform.SetParent(markerRoot.transform, false);
        board.transform.localPosition = new Vector3(0f, 3.45f, 0f);
        board.transform.localScale = new Vector3(8.2f, 2.6f, 0.24f);
        ApplyMaterial(board, ResolveAccentColor());

        GameObject trim = GameObject.CreatePrimitive(PrimitiveType.Cube);
        trim.name = "SceneMarker_Trim";
        trim.transform.SetParent(markerRoot.transform, false);
        trim.transform.localPosition = new Vector3(0f, 2.25f, 0.08f);
        trim.transform.localScale = new Vector3(8.7f, 0.28f, 0.14f);
        ApplyMaterial(trim, ResolveRoadColor());

        TextMeshPro titleLabel = CreateMarkerText(
            markerRoot.transform,
            "SceneMarker_Title",
            title,
            new Vector3(0f, 3.82f, -0.14f),
            3.8f,
            Color.white);
        titleLabel.fontStyle = FontStyles.Bold;

        CreateMarkerText(
            markerRoot.transform,
            "SceneMarker_Subtitle",
            ResolveMarkerSubtitle(region),
            new Vector3(0f, 3.12f, -0.14f),
            2.2f,
            new Color(0.97f, 0.97f, 0.97f, 1f));
    }

    Vector2 ResolveFootprint()
    {
        return sceneController != null && sceneController.sceneKind == SemanticZoneKind.Route
            ? routeSize
            : sceneController != null && sceneController.sceneKind == SemanticZoneKind.Gym
                ? Vector2.one * gymRadius * 2f
                : townSize;
    }

    Color ResolveNpcColor()
    {
        if (sceneController == null)
            return Color.white;
        return sceneController.sceneKind switch
        {
            SemanticZoneKind.Gym => sceneController.gymNpcColor,
            SemanticZoneKind.Route => sceneController.routeNpcColor,
            _ => sceneController.townNpcColor,
        };
    }

    NaturalGrammarRegion ResolveRegion()
    {
        if (sceneController == null)
            return NaturalGrammarProgression.Resolve("", 1);
        return NaturalGrammarProgression.ResolveByTopicOrTier(sceneController.grammarTopic, sceneController.grammarTopicTier);
    }

    Color ResolveGroundColor()
    {
        NaturalGrammarRegion region = ResolveRegion();
        return region != null && region.groundTint.a > 0f ? region.groundTint : groundColor;
    }

    Color ResolveRoadColor()
    {
        NaturalGrammarRegion region = ResolveRegion();
        return region != null && region.roadTint.a > 0f ? region.roadTint : roadColor;
    }

    Color ResolveBuildingColor()
    {
        NaturalGrammarRegion region = ResolveRegion();
        return region != null && region.buildingTint.a > 0f ? region.buildingTint : buildingColor;
    }

    Color ResolveAccentColor()
    {
        NaturalGrammarRegion region = ResolveRegion();
        return region != null && region.accentTint.a > 0f ? region.accentTint : treasureColor;
    }

    string ResolveMarkerSubtitle(NaturalGrammarRegion region)
    {
        string zoneLabel = sceneController != null ? sceneController.sceneKind.ToString() : "Town";
        string assistLabel = sceneController != null
            ? sceneController.translatorAssist switch
            {
                TranslatorAssistMode.Full => "Full Buddy",
                TranslatorAssistMode.Partial => "Partial Buddy",
                _ => "Buddy Off",
            }
            : "Full Buddy";

        string conceptLabel = region != null
            ? region.conceptId switch
            {
                GrammarConceptId.Greetings => "Greetings",
                GrammarConceptId.Alphabet => "Alphabet",
                GrammarConceptId.VowelsConsonants => "Vowels and Consonants",
                GrammarConceptId.SentenceStartEnd => "Sentence Basics",
                GrammarConceptId.BasicNouns => "Nouns",
                GrammarConceptId.BasicVerbs => "Verbs",
                GrammarConceptId.Articles => "Articles",
                GrammarConceptId.Pronouns => "Pronouns",
                GrammarConceptId.Plurals => "Plurals",
                GrammarConceptId.Adjectives => "Adjectives",
                GrammarConceptId.BasicPrepositions => "Prepositions",
                _ => region.grammarTopic,
            }
            : sceneController != null ? sceneController.grammarTopic : "Grammar";

        return $"{zoneLabel} | {assistLabel} | {conceptLabel}";
    }

    static TextMeshPro CreateMarkerText(Transform parent, string objectName, string value, Vector3 localPosition, float fontSize, Color color)
    {
        GameObject label = new GameObject(objectName);
        label.transform.SetParent(parent, false);
        label.transform.localPosition = localPosition;
        label.transform.localRotation = Quaternion.identity;
        label.transform.localScale = Vector3.one * 0.18f;

        TextMeshPro text = label.AddComponent<TextMeshPro>();
        text.text = value;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.sortingOrder = 4;
        return text;
    }

    System.Random CreateRng(int salt)
    {
        ResolveController();
        int tier = sceneController != null ? sceneController.grammarTopicTier : 1;
        string topic = sceneController != null ? sceneController.grammarTopic : "";
        SemanticZoneKind kind = sceneController != null ? sceneController.sceneKind : SemanticZoneKind.Town;
        int mixedSalt = runtimeGenerationSalt == 0 ? salt : MixSeed(salt, runtimeGenerationSalt);
        return new System.Random(ResolveDeterministicSeed(kind, topic, tier, seed, includeTopicInSeed, mixedSalt));
    }

    public static int ResolveDeterministicSeed(
        SemanticZoneKind sceneKind,
        string grammarTopic,
        int grammarTopicTier,
        int baseSeed,
        bool includeTopic,
        int salt = 0)
    {
        unchecked
        {
            int value = MixSeed(baseSeed, salt);
            value = MixSeed(value, (int)sceneKind * 9973);
            value = MixSeed(value, Mathf.Max(1, grammarTopicTier) * 7919);
            if (includeTopic && !string.IsNullOrWhiteSpace(grammarTopic))
            {
                foreach (char c in grammarTopic)
                    value = MixSeed(value, c);
            }
            return Mathf.Abs(value == int.MinValue ? 0 : value);
        }
    }

    static int MixSeed(int seedValue, int salt)
    {
        unchecked
        {
            uint value = (uint)seedValue;
            value ^= (uint)salt + 0x9E3779B9u + (value << 6) + (value >> 2);
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return (int)(value & 0x7FFFFFFF);
        }
    }

    public static List<float> BuildAxisLines(int count, float size, System.Random rng)
    {
        var result = new List<float>();
        int lineCount = Mathf.Max(1, count);
        float span = Mathf.Max(1f, size);
        if (lineCount == 1)
        {
            result.Add(0f);
            return result;
        }

        for (int i = 0; i < lineCount; i++)
        {
            float t = lineCount == 1 ? 0.5f : i / (lineCount - 1f);
            float centered = Mathf.Lerp(-span * 0.35f, span * 0.35f, t);
            float jitter = rng != null ? RandomRange(rng, -span * 0.035f, span * 0.035f) : 0f;
            result.Add(centered + jitter);
        }
        return result;
    }

    void ResolveController()
    {
        if (sceneController == null)
            sceneController = GetComponent<GrammarSceneController>();
        if (sceneController == null)
            sceneController = FindAnyObjectByType<GrammarSceneController>();
    }

    void EnsureRoot()
    {
        if (generatedRoot != null)
            return;

        GameObject root = new GameObject(RootName);
        root.transform.SetParent(transform, false);
        generatedRoot = root.transform;
    }

    static float RandomRange(System.Random rng, float min, float max)
    {
        if (rng == null)
            return min;
        return Mathf.Lerp(min, max, (float)rng.NextDouble());
    }

    static float RandomSign(System.Random rng)
    {
        return rng != null && rng.NextDouble() < 0.5 ? -1f : 1f;
    }

    static void ApplyMaterial(GameObject target, Color color)
    {
        Renderer renderer = target != null ? target.GetComponentInChildren<Renderer>() : null;
        if (renderer == null)
            return;
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (shader == null)
            return;
        renderer.material = new Material(shader) { color = color };
    }

    static void DestroyGameObject(GameObject target)
    {
        if (target == null)
            return;
        if (Application.isPlaying)
            Destroy(target);
        else
            DestroyImmediate(target);
    }
}
