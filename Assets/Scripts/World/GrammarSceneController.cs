using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[Serializable]
public class GrammarNpcSpawnDefinition
{
    public string npcId = "npc";
    public string displayName = "Guide";
    public Vector3 position = Vector3.zero;
    public Vector3 eulerAngles = Vector3.zero;
    public GameObject prefabOverride;
    public List<LocalizedDialogueLine> dialogueLines = new List<LocalizedDialogueLine>();
    public bool startsTrainerBattle;
    [Min(1)] public int trainerEnemyCount = 1;
    public List<string> trainerEncounterNounFamilies = new List<string>();
    public List<GrammarPhrasePattern> trainerPracticePatterns = new List<GrammarPhrasePattern>();
    public List<string> trainerMasteryTags = new List<string>();
}

[Serializable]
public class GrammarScenePortalDefinition
{
    public string displayName = "Next Area";
    public string targetSceneName = "";
    public string targetAreaId = "";
    public bool requiresCurrentAreaCompleted;
    public Vector3 position = Vector3.zero;
    public Vector3 size = new Vector3(3f, 3f, 3f);
}

public class GrammarSceneController : MonoBehaviour
{
    [Header("Scene Identity")]
    public SemanticZoneKind sceneKind = SemanticZoneKind.Town;
    public string grammarTopic = "Greetings and Survival English";
    [Min(1)] public int grammarTopicTier = 1;
    public TranslatorAssistMode translatorAssist = TranslatorAssistMode.Full;

    [Header("Map")]
    public string mapAreaId = "";
    public string mapDisplayName = "";
    public Vector2 mapPosition = new Vector2(90f, 160f);
    public List<string> connectedMapAreaIds = new List<string>();

    [Header("Route Encounter Nouns")]
    public List<string> currentNounFamilies = new List<string>();
    public List<string> reviewNounFamilies = new List<string>();

    [Header("NPCs")]
    public Transform npcParent;
    public List<GrammarNpcSpawnDefinition> npcSpawns = new List<GrammarNpcSpawnDefinition>
    {
        new GrammarNpcSpawnDefinition
        {
            npcId = "topic-guide",
            displayName = "Topic Guide",
            position = new Vector3(2f, 0f, 3f),
            dialogueLines = new List<LocalizedDialogueLine>
            {
                new LocalizedDialogueLine
                {
                    lineId = "topic-guide-hello",
                    sourceText = "Welcome. Listen to the noun, then use a verb to act.",
                    sourceLanguage = "en",
                    expectedEnglishResponse = "I understand",
                },
            },
        },
    };

    [Header("Scene Links")]
    public List<GrammarScenePortalDefinition> portals = new List<GrammarScenePortalDefinition>();

    [Header("Fallback Visuals")]
    public Color townNpcColor = new Color(0.35f, 0.7f, 1f, 1f);
    public Color routeNpcColor = new Color(0.35f, 0.85f, 0.45f, 1f);
    public Color gymNpcColor = new Color(1f, 0.72f, 0.24f, 1f);

    void Start()
    {
        ApplyNaturalProgressionDefaults();
        ApplySceneAssistMode();
        if (!ShouldDeferNpcSpawningToProceduralGenerator())
            SpawnConfiguredNpcs();
        SpawnConfiguredPortals();
        GrammarWorldProgressService.Instance.RegisterCurrentScene(this);
    }

    void ApplyNaturalProgressionDefaults()
    {
        if (currentNounFamilies == null)
            currentNounFamilies = new List<string>();
        if (reviewNounFamilies == null)
            reviewNounFamilies = new List<string>();

        NaturalGrammarRegion region = NaturalGrammarProgression.Resolve(grammarTopic, grammarTopicTier);
        if (currentNounFamilies.Count == 0)
            currentNounFamilies.AddRange(region.currentNounFamilies ?? Array.Empty<string>());
        if (reviewNounFamilies.Count == 0)
            reviewNounFamilies.AddRange(region.reviewNounFamilies ?? Array.Empty<string>());
        HydrateDefaultNpcSpawnsFromNaturalProgression(region);
    }

    void HydrateDefaultNpcSpawnsFromNaturalProgression(NaturalGrammarRegion region)
    {
        if (region == null || !ShouldReplaceStockDefaultNpcSpawns())
            return;

        List<LocalizedDialogueLine> lines = NaturalGrammarProgression.BuildGeneratedDialogueSet(
            sceneKind,
            grammarTopic,
            grammarTopicTier,
            trainerBattle: sceneKind != SemanticZoneKind.Town && ShouldStartTrainerEncounter(region, sceneKind));
        if (lines == null || lines.Count == 0)
            return;

        npcSpawns = sceneKind == SemanticZoneKind.Gym
            ? BuildNaturalGymLeaderSpawn(region, lines)
            : BuildNaturalNpcSpawns(region, lines);
    }

    bool ShouldReplaceStockDefaultNpcSpawns()
    {
        if (npcSpawns == null || npcSpawns.Count == 0)
            return true;
        if (npcSpawns.Count != 1)
            return false;

        GrammarNpcSpawnDefinition spawn = npcSpawns[0];
        if (spawn == null)
            return true;
        if (!string.Equals(spawn.npcId, "topic-guide", StringComparison.OrdinalIgnoreCase))
            return false;
        if (spawn.dialogueLines == null || spawn.dialogueLines.Count != 1)
            return false;

        LocalizedDialogueLine line = spawn.dialogueLines[0];
        return line == null ||
               string.Equals(line.lineId, "topic-guide-hello", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(line.sourceText, "Welcome. Listen to the noun, then use a verb to act.", StringComparison.Ordinal);
    }

    List<GrammarNpcSpawnDefinition> BuildNaturalNpcSpawns(NaturalGrammarRegion region, List<LocalizedDialogueLine> lines)
    {
        var spawns = new List<GrammarNpcSpawnDefinition>();
        bool trainerBattle = sceneKind == SemanticZoneKind.Route && IsTacticalCommandRegion(region);
        for (int i = 0; i < lines.Count; i++)
        {
            string displayName = sceneKind == SemanticZoneKind.Route
                ? NaturalGrammarProgression.ResolveRouteNpcName(grammarTopic, grammarTopicTier, i)
                : NaturalGrammarProgression.ResolveTownNpcName(grammarTopic, grammarTopicTier, i);

            spawns.Add(new GrammarNpcSpawnDefinition
            {
                npcId = $"natural-{sceneKind.ToString().ToLowerInvariant()}-{i + 1}",
                displayName = displayName,
                position = ResolveNaturalNpcPosition(i, lines.Count),
                eulerAngles = new Vector3(0f, 180f + i * 23f, 0f),
                dialogueLines = new List<LocalizedDialogueLine> { lines[i] },
                startsTrainerBattle = trainerBattle,
                trainerEnemyCount = trainerBattle
                    ? NaturalGrammarProgression.ResolveTrainerBattleEnemyCount(grammarTopic, grammarTopicTier, i)
                    : 1,
                trainerEncounterNounFamilies = trainerBattle
                    ? NaturalGrammarProgression.BuildTrainerBattleNounFamilies(grammarTopic, grammarTopicTier, i)
                    : new List<string>(),
                trainerPracticePatterns = trainerBattle
                    ? NaturalGrammarProgression.BuildTrainerBattlePracticePatterns(grammarTopic, grammarTopicTier, i)
                    : new List<GrammarPhrasePattern>(),
                trainerMasteryTags = trainerBattle
                    ? NaturalGrammarProgression.BuildTrainerBattleMasteryTags(grammarTopic, grammarTopicTier, i)
                    : new List<string>(),
            });
        }

        return spawns;
    }

    List<GrammarNpcSpawnDefinition> BuildNaturalGymLeaderSpawn(NaturalGrammarRegion region, List<LocalizedDialogueLine> lines)
    {
        bool tacticalCombatUnlocked = IsTacticalCommandRegion(region);
        bool bossEncounter = ShouldStartTrainerEncounter(region, SemanticZoneKind.Gym);
        int poolIndex = 1;
        return new List<GrammarNpcSpawnDefinition>
        {
            new GrammarNpcSpawnDefinition
            {
                npcId = "natural-gym-leader",
                displayName = NaturalGrammarProgression.ResolveGymLeaderName(grammarTopic, grammarTopicTier),
                position = new Vector3(0f, 0f, 5.5f),
                eulerAngles = new Vector3(0f, 180f, 0f),
                dialogueLines = lines,
                startsTrainerBattle = bossEncounter,
                trainerEnemyCount = bossEncounter
                    ? Mathf.Max(4, NaturalGrammarProgression.ResolveTrainerBattleEnemyCount(grammarTopic, grammarTopicTier, poolIndex))
                    : 1,
                trainerEncounterNounFamilies = bossEncounter
                    ? NaturalGrammarProgression.BuildTrainerBattleNounFamilies(grammarTopic, grammarTopicTier, poolIndex)
                    : NaturalGrammarProgression.BuildCurrentNounFamilies(grammarTopic, grammarTopicTier),
                trainerPracticePatterns = bossEncounter
                    ? NaturalGrammarProgression.BuildTrainerBattlePracticePatterns(grammarTopic, grammarTopicTier, poolIndex)
                    : new List<GrammarPhrasePattern>(),
                trainerMasteryTags = bossEncounter
                    ? NaturalGrammarProgression.BuildTrainerBattleMasteryTags(grammarTopic, grammarTopicTier, poolIndex)
                    : new List<string>(),
            },
        };
    }

    static bool IsTacticalCommandRegion(NaturalGrammarRegion region)
    {
        return region != null &&
               region.encounterMode == GrammarEncounterMode.TacticalCommand &&
               region.combatUnlocked;
    }

    static bool ShouldStartTrainerEncounter(NaturalGrammarRegion region, SemanticZoneKind sceneKind)
    {
        if (region == null || sceneKind == SemanticZoneKind.Town)
            return false;

        if (region.encounterMode == GrammarEncounterMode.TacticalCommand)
            return region.combatUnlocked;

        // Noun recognition already uses creature nouns and encounter pools, so
        // its Gym should culminate in a boss-style encounter instead of ending
        // as dialogue-only checks.
        return sceneKind == SemanticZoneKind.Gym &&
               region.encounterMode == GrammarEncounterMode.NounRecognition;
    }

    Vector3 ResolveNaturalNpcPosition(int index, int count)
    {
        if (sceneKind == SemanticZoneKind.Route)
            return new Vector3(index % 2 == 0 ? -3f : 3f, 0f, -6f + index * 4f);

        float radius = sceneKind == SemanticZoneKind.Gym ? 5.5f : 4.5f;
        float angle = count <= 1 ? 90f : 35f + index * (110f / Mathf.Max(1, count - 1));
        float radians = angle * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(radians) * radius, 0f, Mathf.Sin(radians) * radius);
    }

    bool ShouldDeferNpcSpawningToProceduralGenerator()
    {
        ProceduralGrammarSceneGenerator generator = GetComponent<ProceduralGrammarSceneGenerator>();
        return generator != null && generator.enabled && generator.generateOnStart;
    }

    void ApplySceneAssistMode()
    {
        TranslatorBuddyService buddy = TranslatorBuddyService.EnsureExists();
        buddy.SetAssistMode(translatorAssist);
    }

    void SpawnConfiguredNpcs()
    {
        if (npcSpawns == null)
            return;

        if (npcParent == null)
        {
            GameObject parent = new GameObject("Runtime Grammar NPCs");
            npcParent = parent.transform;
        }

        foreach (GrammarNpcSpawnDefinition spawn in npcSpawns)
        {
            if (spawn == null)
                continue;

            GameObject instance = spawn.prefabOverride != null
                ? Instantiate(spawn.prefabOverride, spawn.position, Quaternion.Euler(spawn.eulerAngles), npcParent)
                : GameObject.CreatePrimitive(PrimitiveType.Cube);
            instance.name = $"NPC_{spawn.displayName}_{spawn.npcId}";
            if (spawn.prefabOverride == null)
            {
                instance.transform.SetParent(npcParent, true);
                instance.transform.SetPositionAndRotation(spawn.position, Quaternion.Euler(spawn.eulerAngles));
                instance.transform.localScale = new Vector3(1.1f, 1.8f, 1.1f);
                ApplyFallbackNpcMaterial(instance);
            }

            GrammarNpc npc = instance.GetComponent<GrammarNpc>();
            if (npc == null)
                npc = instance.AddComponent<GrammarNpc>();
            npc.Configure(spawn, sceneKind, grammarTopic, grammarTopicTier, translatorAssist);
        }
    }

    void SpawnConfiguredPortals()
    {
        if (portals == null)
            return;

        foreach (GrammarScenePortalDefinition portalDefinition in portals)
        {
            if (portalDefinition == null ||
                (string.IsNullOrWhiteSpace(portalDefinition.targetSceneName) &&
                 string.IsNullOrWhiteSpace(portalDefinition.targetAreaId)))
                continue;

            GameObject portal = GameObject.CreatePrimitive(PrimitiveType.Cube);
            string portalTargetLabel = !string.IsNullOrWhiteSpace(portalDefinition.targetAreaId)
                ? portalDefinition.targetAreaId
                : portalDefinition.targetSceneName;
            portal.name = $"ScenePortal_{portalDefinition.displayName}_{portalTargetLabel}";
            portal.transform.SetPositionAndRotation(portalDefinition.position, Quaternion.identity);
            portal.transform.localScale = portalDefinition.size;

            Collider collider = portal.GetComponent<Collider>();
            if (collider != null)
                collider.isTrigger = true;

            Renderer renderer = portal.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                renderer.material.color = new Color(0.65f, 0.55f, 1f, 0.5f);
            }

            GrammarScenePortal portalComponent = portal.AddComponent<GrammarScenePortal>();
            portalComponent.displayName = portalDefinition.displayName;
            portalComponent.targetSceneName = portalDefinition.targetSceneName;
            portalComponent.targetAreaId = portalDefinition.targetAreaId;
            portalComponent.requiresCurrentAreaCompleted = portalDefinition.requiresCurrentAreaCompleted;
            portalComponent.ConfigureRouteContext(sceneKind, grammarTopic, grammarTopicTier, currentNounFamilies, reviewNounFamilies);
        }
    }

    void ApplyFallbackNpcMaterial(GameObject instance)
    {
        Renderer renderer = instance.GetComponentInChildren<Renderer>();
        if (renderer == null)
            return;

        renderer.material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        renderer.material.color = sceneKind switch
        {
            SemanticZoneKind.Town => townNpcColor,
            SemanticZoneKind.Gym => gymNpcColor,
            _ => routeNpcColor,
        };
    }

    // Kept on the scene controller as the stable assessment boundary used by
    // curriculum validation and editor tests. The dialogue UI owns the actual
    // typed-answer workflow, so both paths share its punctuation-preserving
    // normalizer instead of falling back to speech-keyword normalization.
    static string NormalizeTypedSentence(string response) =>
        NpcDialogueUI.NormalizeTypedSentence(response);
}
