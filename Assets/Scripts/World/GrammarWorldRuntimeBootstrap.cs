using System;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class GrammarWorldRuntimeBootstrap : MonoBehaviour
{
    public const string TownSceneName = "Town";
    public const string RouteSceneName = "Route";
    public const string GymSceneName = "Gym";
    public const string BattleSceneName = "Battle";
    public const string DefaultGrammarWorldSceneName = TownSceneName;

    sealed class RuntimeAreaDefinition
    {
        public NaturalGrammarRegion region;
        public int regionIndex;
        public SemanticZoneKind sceneKind;
        public string areaId = "";
        public string displayName = "";
        public TranslatorAssistMode assistMode = TranslatorAssistMode.Full;
        public Vector2 mapPosition;
        public readonly List<string> connectedAreaIds = new List<string>();
        public readonly List<GrammarScenePortalDefinition> portals = new List<GrammarScenePortalDefinition>();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void SubscribeToSceneLoads()
    {
        SceneManager.sceneLoaded -= HandleGrammarWorldSceneLoaded;
        SceneManager.sceneLoaded += HandleGrammarWorldSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void BootstrapInitialGrammarWorldScene()
    {
        HandleGrammarWorldSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    static void HandleGrammarWorldSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!scene.IsValid() || !IsGrammarWorldTemplateSceneName(scene.name))
            return;

        EnsureRuntimeNavMesh(scene);

        RuntimeAreaDefinition definition = ResolveRequestedAreaDefinition(scene.name);
        if (definition == null || definition.region == null)
            return;

        GrammarSceneController controller = FindComponentInScene<GrammarSceneController>(scene);
        GameObject root = controller != null ? controller.gameObject : new GameObject("RuntimeGrammarWorldArea");
        if (root.scene != scene)
            SceneManager.MoveGameObjectToScene(root, scene);
        if (controller == null)
            controller = root.AddComponent<GrammarSceneController>();
        controller.sceneKind = definition.sceneKind;
        controller.grammarTopic = definition.region.grammarTopic;
        controller.grammarTopicTier = definition.region.tier;
        controller.translatorAssist = definition.assistMode;
        controller.mapAreaId = definition.areaId;
        controller.mapDisplayName = definition.displayName;
        controller.mapPosition = definition.mapPosition;
        controller.connectedMapAreaIds = new List<string>(definition.connectedAreaIds);
        controller.portals = definition.portals;

        ProceduralGrammarSceneGenerator generator = root.GetComponent<ProceduralGrammarSceneGenerator>();
        if (generator == null)
            generator = root.AddComponent<ProceduralGrammarSceneGenerator>();
        generator.sceneController = controller;
        generator.generateOnStart = true;
        FirstRunOnboardingController.EnsureExists(root);
    }

    static void EnsureRuntimeNavMesh(Scene scene)
    {
        if (!scene.IsValid())
            return;

        NavMeshTriangulation existing = NavMesh.CalculateTriangulation();
        if (existing.vertices != null && existing.vertices.Length > 0)
            return;

        NavMeshSurface surface = FindComponentInScene<NavMeshSurface>(scene);
        if (surface == null)
        {
            GameObject navRoot = new GameObject("RuntimeGrammarNavMesh");
            SceneManager.MoveGameObjectToScene(navRoot, scene);
            surface = navRoot.AddComponent<NavMeshSurface>();
        }

        surface.collectObjects = CollectObjects.Volume;
        surface.center = new Vector3(0f, 2f, 0f);
        surface.size = new Vector3(84f, 10f, 84f);
        surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
        surface.layerMask = ~0;
        surface.ignoreNavMeshAgent = true;
        surface.ignoreNavMeshObstacle = true;
        surface.minRegionArea = 0.5f;
        surface.BuildNavMesh();
    }

    static T FindComponentInScene<T>(Scene scene) where T : Component
    {
        if (!scene.IsValid())
            return null;

        GameObject[] roots = scene.GetRootGameObjects();
        foreach (GameObject root in roots)
        {
            if (root == null)
                continue;
            T component = root.GetComponentInChildren<T>(true);
            if (component != null)
                return component;
        }

        return null;
    }

    static RuntimeAreaDefinition ResolveRequestedAreaDefinition(string sceneName)
    {
        bool sceneHasKind = TryResolveTemplateSceneKind(sceneName, out SemanticZoneKind templateKind);
        GrammarWorldProgressService progressService = GrammarWorldProgressService.Instance;
        string requestedAreaId = progressService != null ? progressService.Data.currentAreaId : "";
        if (!string.IsNullOrWhiteSpace(requestedAreaId) &&
            TryBuildAreaDefinition(requestedAreaId, out RuntimeAreaDefinition savedDefinition) &&
            (!sceneHasKind || savedDefinition.sceneKind == templateKind))
        {
            return savedDefinition;
        }

        NaturalGrammarRegion firstRegion = NaturalGrammarProgression.Regions.Count > 0 ? NaturalGrammarProgression.Regions[0] : null;
        if (firstRegion == null)
            return null;

        SemanticZoneKind fallbackKind = sceneHasKind ? templateKind : SemanticZoneKind.Town;
        string fallbackAreaId = GrammarWorldProgressService.BuildAreaId(fallbackKind, firstRegion.grammarTopic, firstRegion.tier);
        return TryBuildAreaDefinition(fallbackAreaId, out RuntimeAreaDefinition fallbackDefinition)
            ? fallbackDefinition
            : null;
    }

    public static bool IsGrammarWorldTemplateSceneName(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
            return false;
        return string.Equals(sceneName, TownSceneName, StringComparison.Ordinal) ||
               string.Equals(sceneName, RouteSceneName, StringComparison.Ordinal) ||
               string.Equals(sceneName, GymSceneName, StringComparison.Ordinal);
    }

    public static string ResolveSceneNameForAreaId(string areaId)
    {
        return TryResolveAreaKind(areaId, out SemanticZoneKind kind)
            ? ResolveSceneNameForKind(kind)
            : DefaultGrammarWorldSceneName;
    }

    public static string ResolveSceneNameForKind(SemanticZoneKind kind)
    {
        return kind switch
        {
            SemanticZoneKind.Route => RouteSceneName,
            SemanticZoneKind.Gym => GymSceneName,
            _ => TownSceneName,
        };
    }

    public static string ResolveFirstTownAreaId()
    {
        NaturalGrammarRegion firstRegion = NaturalGrammarProgression.Regions.Count > 0 ? NaturalGrammarProgression.Regions[0] : null;
        return firstRegion != null
            ? GrammarWorldProgressService.BuildAreaId(SemanticZoneKind.Town, firstRegion.grammarTopic, firstRegion.tier)
            : "";
    }

    public static bool TryResolveAreaKind(string areaId, out SemanticZoneKind kind)
    {
        kind = SemanticZoneKind.Town;
        if (string.IsNullOrWhiteSpace(areaId))
            return false;

        string[] parts = areaId.Split(':');
        return parts.Length >= 1 && Enum.TryParse(parts[0], ignoreCase: true, out kind);
    }

    static bool TryResolveTemplateSceneKind(string sceneName, out SemanticZoneKind kind)
    {
        kind = SemanticZoneKind.Town;
        if (string.Equals(sceneName, RouteSceneName, StringComparison.Ordinal))
        {
            kind = SemanticZoneKind.Route;
            return true;
        }
        if (string.Equals(sceneName, GymSceneName, StringComparison.Ordinal))
        {
            kind = SemanticZoneKind.Gym;
            return true;
        }
        if (string.Equals(sceneName, TownSceneName, StringComparison.Ordinal))
        {
            kind = SemanticZoneKind.Town;
            return true;
        }
        return false;
    }

    public static bool TryResolveAreaBlueprint(
        string areaId,
        out string displayName,
        out SemanticZoneKind sceneKind,
        out string grammarTopic,
        out int grammarTopicTier,
        out TranslatorAssistMode assistMode,
        out List<string> connectedAreaIds)
    {
        displayName = "";
        sceneKind = SemanticZoneKind.Town;
        grammarTopic = "";
        grammarTopicTier = 1;
        assistMode = TranslatorAssistMode.Full;
        connectedAreaIds = new List<string>();

        if (!TryBuildAreaDefinition(areaId, out RuntimeAreaDefinition definition) || definition == null || definition.region == null)
            return false;

        displayName = definition.displayName;
        sceneKind = definition.sceneKind;
        grammarTopic = definition.region.grammarTopic;
        grammarTopicTier = definition.region.tier;
        assistMode = definition.assistMode;
        connectedAreaIds = new List<string>(definition.connectedAreaIds);
        return true;
    }

    static bool TryBuildAreaDefinition(string areaId, out RuntimeAreaDefinition definition)
    {
        definition = null;
        if (!TryResolveRegion(areaId, out NaturalGrammarRegion region, out int regionIndex, out SemanticZoneKind sceneKind))
            return false;

        definition = new RuntimeAreaDefinition
        {
            region = region,
            regionIndex = regionIndex,
            sceneKind = sceneKind,
            areaId = GrammarWorldProgressService.BuildAreaId(sceneKind, region.grammarTopic, region.tier),
            displayName = ResolveDisplayName(region, sceneKind),
            assistMode = ResolveAssistMode(sceneKind),
            mapPosition = ResolveMapPosition(regionIndex, sceneKind),
        };

        NaturalGrammarRegion previousRegion = regionIndex > 0 ? NaturalGrammarProgression.Regions[regionIndex - 1] : null;
        NaturalGrammarRegion nextRegion = regionIndex + 1 < NaturalGrammarProgression.Regions.Count ? NaturalGrammarProgression.Regions[regionIndex + 1] : null;

        switch (sceneKind)
        {
            case SemanticZoneKind.Route:
                definition.connectedAreaIds.Add(GrammarWorldProgressService.BuildAreaId(SemanticZoneKind.Town, region.grammarTopic, region.tier));
                definition.connectedAreaIds.Add(GrammarWorldProgressService.BuildAreaId(SemanticZoneKind.Gym, region.grammarTopic, region.tier));
                break;
            case SemanticZoneKind.Gym:
                definition.connectedAreaIds.Add(GrammarWorldProgressService.BuildAreaId(SemanticZoneKind.Route, region.grammarTopic, region.tier));
                if (nextRegion != null)
                {
                    definition.connectedAreaIds.Add(
                        GrammarWorldProgressService.BuildAreaId(
                            SemanticZoneKind.Town,
                            nextRegion.grammarTopic,
                            nextRegion.tier));
                }
                break;
            default:
                definition.connectedAreaIds.Add(GrammarWorldProgressService.BuildAreaId(SemanticZoneKind.Route, region.grammarTopic, region.tier));
                if (previousRegion != null)
                {
                    definition.connectedAreaIds.Add(
                        GrammarWorldProgressService.BuildAreaId(
                            SemanticZoneKind.Gym,
                            previousRegion.grammarTopic,
                            previousRegion.tier));
                }
                break;
        }

        definition.portals.AddRange(BuildPortals(definition));
        return true;
    }

    static IEnumerable<GrammarScenePortalDefinition> BuildPortals(RuntimeAreaDefinition definition)
    {
        var portals = new List<GrammarScenePortalDefinition>();
        if (definition == null)
            return portals;

        Vector3[] positions = definition.sceneKind switch
        {
            SemanticZoneKind.Route => new[]
            {
                new Vector3(-18f, 1.25f, -8f),
                new Vector3(18f, 1.25f, 8f),
            },
            SemanticZoneKind.Gym => new[]
            {
                new Vector3(0f, 1.25f, -18f),
                new Vector3(0f, 1.25f, 18f),
            },
            _ => new[]
            {
                new Vector3(0f, 1.25f, 18f),
                new Vector3(0f, 1.25f, -18f),
            },
        };

        for (int i = 0; i < definition.connectedAreaIds.Count; i++)
        {
            string connectedAreaId = definition.connectedAreaIds[i];
            if (string.IsNullOrWhiteSpace(connectedAreaId))
                continue;

            portals.Add(new GrammarScenePortalDefinition
            {
                displayName = BuildPortalLabel(connectedAreaId),
                targetSceneName = ResolveSceneNameForAreaId(connectedAreaId),
                targetAreaId = connectedAreaId,
                requiresCurrentAreaCompleted = IsForwardProgressionPortal(definition, connectedAreaId),
                position = positions[Mathf.Clamp(i, 0, positions.Length - 1)],
                size = new Vector3(3.5f, 3f, 3.5f),
            });
        }

        return portals;
    }

    static bool IsForwardProgressionPortal(RuntimeAreaDefinition source, string targetAreaId)
    {
        if (source == null || string.IsNullOrWhiteSpace(targetAreaId))
            return false;

        if (!TryResolveRegion(targetAreaId, out _, out int targetRegionIndex, out SemanticZoneKind targetKind))
            return false;

        return source.sceneKind switch
        {
            SemanticZoneKind.Town => targetKind == SemanticZoneKind.Route &&
                                     targetRegionIndex == source.regionIndex,
            SemanticZoneKind.Route => targetKind == SemanticZoneKind.Gym &&
                                      targetRegionIndex == source.regionIndex,
            SemanticZoneKind.Gym => targetKind == SemanticZoneKind.Town &&
                                    targetRegionIndex == source.regionIndex + 1,
            _ => false,
        };
    }

    static string BuildPortalLabel(string areaId)
    {
        if (!TryResolveRegion(areaId, out NaturalGrammarRegion region, out _, out SemanticZoneKind kind) || region == null)
            return "Next Area";

        return kind switch
        {
            SemanticZoneKind.Route => $"Route: {region.displayName}",
            SemanticZoneKind.Gym => $"Gym: {region.displayName}",
            _ => $"Town: {region.displayName}",
        };
    }

    static bool TryResolveRegion(string areaId, out NaturalGrammarRegion region, out int regionIndex, out SemanticZoneKind sceneKind)
    {
        region = null;
        regionIndex = -1;
        sceneKind = SemanticZoneKind.Town;

        if (string.IsNullOrWhiteSpace(areaId))
            return false;

        string[] parts = areaId.Split(':');
        if (parts.Length < 3)
            return false;
        if (!Enum.TryParse(parts[0], ignoreCase: true, out sceneKind))
            return false;
        if (!int.TryParse(parts[2], out int tier))
            tier = 1;

        string topicKey = parts[1].Trim();
        IReadOnlyList<NaturalGrammarRegion> regions = NaturalGrammarProgression.Regions;
        for (int i = 0; i < regions.Count; i++)
        {
            NaturalGrammarRegion candidate = regions[i];
            if (candidate == null || candidate.tier != tier)
                continue;

            if (!string.Equals(
                    GrammarWorldProgressService.BuildAreaTopicKey(candidate.grammarTopic),
                    topicKey,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            region = candidate;
            regionIndex = i;
            return true;
        }

        return false;
    }

    static TranslatorAssistMode ResolveAssistMode(SemanticZoneKind sceneKind)
    {
        return sceneKind switch
        {
            SemanticZoneKind.Route => TranslatorAssistMode.Partial,
            SemanticZoneKind.Gym => TranslatorAssistMode.Off,
            _ => TranslatorAssistMode.Full,
        };
    }

    static string ResolveDisplayName(NaturalGrammarRegion region, SemanticZoneKind sceneKind)
    {
        string label = region != null && !string.IsNullOrWhiteSpace(region.displayName)
            ? region.displayName
            : "Grammar Area";
        return sceneKind switch
        {
            SemanticZoneKind.Route => $"{label} Route",
            SemanticZoneKind.Gym => $"{label} Gym",
            _ => label,
        };
    }

    static Vector2 ResolveMapPosition(int regionIndex, SemanticZoneKind sceneKind)
    {
        Vector2 townPosition = new Vector2(90f + regionIndex * 150f, 160f + (regionIndex % 2) * 105f);
        return sceneKind switch
        {
            SemanticZoneKind.Route => townPosition + new Vector2(60f, 48f),
            SemanticZoneKind.Gym => townPosition + new Vector2(0f, -58f),
            _ => townPosition,
        };
    }
}
