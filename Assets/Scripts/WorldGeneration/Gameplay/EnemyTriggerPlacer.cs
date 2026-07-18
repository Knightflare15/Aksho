using System;
using UnityEngine;

public static class EnemyTriggerPlacer
{
    public static void PlaceData(WorldData data, GameplayPlacementProfile profile, System.Random rng)
    {
        data.enemyTriggers.Clear();
        int target = rng.Next(profile.minEnemyTriggers, profile.maxEnemyTriggers + 1);
        foreach (ObjectivePoint objective in data.objectives)
        {
            if (data.enemyTriggers.Count >= target)
                break;
            AddTrigger(data, objective.gridPosition, "ObjectiveApproach");
        }

        int guard = 0;
        while (data.enemyTriggers.Count < target && guard++ < 900)
        {
            Vector2Int candidate;
            if (!TryFindFlowPoint(data, profile, rng, out candidate))
                break;
            AddTrigger(data, candidate, ResolveCategory(data.enemyTriggers.Count));
        }
    }

    public static void Spawn(WorldData data, WorldGenerationProfile profile, Transform parent)
    {
        foreach (EnemyTriggerData trigger in data.enemyTriggers)
        {
            GameObject triggerObject = profile.enemyTriggerPrefab != null
                ? UnityEngine.Object.Instantiate(profile.enemyTriggerPrefab, trigger.worldPosition, Quaternion.identity, parent)
                : new GameObject("GeneratedEnemyTrigger");

            triggerObject.transform.SetParent(parent, true);
            triggerObject.transform.position = trigger.worldPosition;
            triggerObject.name = $"GeneratedEnemyTrigger_{trigger.category}_{trigger.gridPosition.x}_{trigger.gridPosition.y}";
            HideEditorIcon(triggerObject);

            EnemyWaveTrigger waveTrigger = triggerObject.GetComponent<EnemyWaveTrigger>();
            if (waveTrigger == null)
                waveTrigger = triggerObject.AddComponent<EnemyWaveTrigger>();
            waveTrigger.triggerRadius = trigger.radius;
            waveTrigger.enemyCount = Mathf.Clamp(trigger.spawnPoints.Count, 3, 8);
            waveTrigger.semanticZoneKind = trigger.semanticKind;
            waveTrigger.grammarTopic = trigger.grammarTopic;
            waveTrigger.translatorAssist = trigger.translatorAssist;
            waveTrigger.encounterNounFamilies = new System.Collections.Generic.List<string>(trigger.encounterNounFamilies);

            GrammarRegion region = triggerObject.GetComponent<GrammarRegion>();
            if (region == null)
                region = triggerObject.AddComponent<GrammarRegion>();
            region.zoneKind = trigger.semanticKind;
            region.grammarTopic = trigger.grammarTopic;
            region.translatorAssist = trigger.translatorAssist;
            region.encounterNounFamilies = new System.Collections.Generic.List<string>(trigger.encounterNounFamilies);

            SphereCollider sphere = triggerObject.GetComponent<SphereCollider>();
            if (sphere == null)
                sphere = triggerObject.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.radius = trigger.radius;

            for (int i = 0; i < trigger.spawnPoints.Count; i++)
            {
                SpawnPointData spawnPoint = trigger.spawnPoints[i];
                var spawnObject = new GameObject($"SpawnPoint_{i:00}");
                spawnObject.transform.SetParent(triggerObject.transform, true);
                spawnObject.transform.position = spawnPoint.worldPosition;
                HideEditorIcon(spawnObject);
            }
        }
    }

    static void HideEditorIcon(GameObject target)
    {
#if UNITY_EDITOR
        UnityEditor.EditorGUIUtility.SetIconForObject(target, null);
#endif
    }

    static void AddTrigger(WorldData data, Vector2Int grid, string category)
    {
        var trigger = new EnemyTriggerData
        {
            gridPosition = grid,
            worldPosition = WorldPlacementUtility.GroundedPosition(data, grid.x, grid.y),
            radius = 5f,
            category = category,
            semanticKind = SemanticZoneKind.Route,
            grammarTopic = ResolveGrammarTopic(category),
            translatorAssist = TranslatorAssistMode.Partial,
            encounterNounFamilies = ResolveNounFamilies(category),
        };
        data.enemyTriggers.Add(trigger);
    }

    static string ResolveGrammarTopic(string category)
    {
        switch (category)
        {
            case "ObjectiveApproach": return "Route practice: nouns and verbs";
            case "ChestAmbush": return "Route practice: adjectives";
            case "Chokepoint": return "Route practice: articles and pronouns";
            default: return "Route practice";
        }
    }

    static System.Collections.Generic.List<string> ResolveNounFamilies(string category)
    {
        switch (category)
        {
            case "ChestAmbush":
                return new System.Collections.Generic.List<string> { "BUG", "RAT" };
            case "Chokepoint":
                return new System.Collections.Generic.List<string> { "DOG", "CAT" };
            default:
                return new System.Collections.Generic.List<string> { "RAT", "CAT", "DOG", "BUG" };
        }
    }

    static bool TryFindFlowPoint(WorldData data, GameplayPlacementProfile profile, System.Random rng, out Vector2Int grid)
    {
        ZoneData start = null;
        foreach (ZoneData zone in data.zones)
            if (zone.kind == ZoneKind.Start)
                start = zone;

        for (int attempt = 0; attempt < 300; attempt++)
        {
            PathData path = data.paths.Count > 0 ? data.paths[rng.Next(0, data.paths.Count)] : null;
            if (path == null || path.points.Count == 0)
                break;
            Vector2Int basePoint = path.points[rng.Next(0, path.points.Count)];
            int x = basePoint.x + rng.Next(-10, 11);
            int z = basePoint.y + rng.Next(-10, 11);
            if (!data.InBounds(x, z))
                continue;
            if (!WorldPlacementUtility.IsDryPlayableCell(data, x, z, 24f, 0.65f))
                continue;
            if (start != null && Vector2Int.Distance(start.gridPosition, new Vector2Int(x, z)) * data.gridSpacing < profile.minTriggerDistanceFromStart)
                continue;
            bool tooClose = false;
            foreach (EnemyTriggerData trigger in data.enemyTriggers)
            {
                if (Vector2Int.Distance(trigger.gridPosition, new Vector2Int(x, z)) < 12f)
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose)
                continue;
            grid = new Vector2Int(x, z);
            return true;
        }

        grid = default;
        return false;
    }

    static string ResolveCategory(int index)
    {
        switch (index % 4)
        {
            case 0: return "PathSegment";
            case 1: return "ChestAmbush";
            case 2: return "Chokepoint";
            default: return "EnemyClearing";
        }
    }
}
