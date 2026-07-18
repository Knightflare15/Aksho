using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class GrammarSliceAssetCoverageValidator
{
    [MenuItem("The Script/Grammar Scenes/Validate Class 1-2 Slice Asset Coverage", false, 74)]
    public static void ValidateMenu()
    {
        List<ContentValidationIssue> issues = CollectIssues();
        int errors = issues.Count(issue => issue.severity == ContentValidationSeverity.Error);
        int warnings = issues.Count(issue => issue.severity == ContentValidationSeverity.Warning);

        Debug.Log($"[GrammarSliceAssetCoverageValidator] Validation complete: {errors} error(s), {warnings} warning(s).");
        LogIssues(issues);
    }

    public static List<ContentValidationIssue> CollectIssues()
    {
        var issues = new List<ContentValidationIssue>();
        ValidateOpenSceneGenerators(issues);
        ValidateCreatureCatalogAssets(issues);
        return issues;
    }

    static void ValidateOpenSceneGenerators(List<ContentValidationIssue> issues)
    {
        ProceduralGrammarSceneGenerator[] generators =
            Object.FindObjectsByType<ProceduralGrammarSceneGenerator>(FindObjectsInactive.Include);
        if (generators == null || generators.Length == 0)
        {
            issues.Add(new ContentValidationIssue(
                ContentValidationSeverity.Warning,
                "No ProceduralGrammarSceneGenerator is loaded. Open the Class 1-2 scaffold scene to audit town/route/gym prefab coverage."));
            return;
        }

        foreach (ProceduralGrammarSceneGenerator generator in generators)
            ValidateSceneGenerator(generator, issues);
    }

    static void ValidateCreatureCatalogAssets(List<ContentValidationIssue> issues)
    {
        List<CreatureCombatCatalog> catalogs = LoadAssets<CreatureCombatCatalog>();
        if (catalogs.Count == 0)
        {
            issues.Add(new ContentValidationIssue(
                ContentValidationSeverity.Warning,
                "No CreatureCombatCatalog asset exists. Class 1-2 combat will fall back to runtime defaults with no authored creature prefabs or verb effects."));
            ValidateCreatureCombatCatalog(CreatureCombatCatalog.CreateRuntimeDefault(), "Runtime defaults", issues);
            return;
        }

        foreach (CreatureCombatCatalog catalog in catalogs)
            ValidateCreatureCombatCatalog(catalog, catalog.name, issues);
    }

    internal static void ValidateSceneGenerator(ProceduralGrammarSceneGenerator generator, List<ContentValidationIssue> issues)
    {
        if (generator == null)
        {
            issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, "A Class 1-2 scene generator reference is null."));
            return;
        }

        GrammarSceneController controller = generator.sceneController != null
            ? generator.sceneController
            : generator.GetComponent<GrammarSceneController>();
        if (controller == null)
        {
            issues.Add(new ContentValidationIssue(
                ContentValidationSeverity.Error,
                $"Grammar scene generator '{generator.name}' has no GrammarSceneController.",
                generator));
            return;
        }

        string label = $"{controller.mapDisplayName} ({controller.sceneKind})";
        GrammarScenePrefabSet prefabSet = generator.prefabSet ?? new GrammarScenePrefabSet();

        if (prefabSet.templatePrefab == null)
        {
            issues.Add(new ContentValidationIssue(
                ContentValidationSeverity.Warning,
                $"{label} uses fallback generated ground because Template Prefab is not assigned.",
                generator));
        }

        switch (controller.sceneKind)
        {
            case SemanticZoneKind.Town:
                RequirePrefabSet(prefabSet.buildingPrefabs, $"{label} is missing town building prefabs.", generator, issues);
                RequirePrefabSet(prefabSet.roadPrefabs, $"{label} is missing town road prefabs.", generator, issues);
                RequirePrefabSet(prefabSet.npcPrefabs, $"{label} is missing town NPC prefabs.", generator, issues);
                break;

            case SemanticZoneKind.Route:
                RequirePrefabSet(prefabSet.roadPrefabs, $"{label} is missing route road prefabs.", generator, issues);
                RequirePrefabSet(prefabSet.npcPrefabs, $"{label} is missing route NPC/trainer prefabs.", generator, issues);
                RequirePrefabSet(prefabSet.treasurePrefabs, $"{label} is missing route treasure prefabs.", generator, issues);
                if (NaturalGrammarProgression.IsTacticalCombatUnlocked(controller.grammarTopic, controller.grammarTopicTier))
                    RequirePrefabSet(prefabSet.wildEncounterTriggerPrefabs, $"{label} is missing wild encounter trigger prefabs.", generator, issues);
                break;

            case SemanticZoneKind.Gym:
                RequirePrefabSet(prefabSet.npcPrefabs, $"{label} is missing gym leader prefabs.", generator, issues);
                RequirePrefabSet(prefabSet.arenaPropPrefabs, $"{label} is missing gym arena prop prefabs.", generator, issues);
                break;
        }
    }

    internal static void ValidateCreatureCombatCatalog(CreatureCombatCatalog catalog, string catalogLabel, List<ContentValidationIssue> issues)
    {
        if (catalog == null)
        {
            issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Creature catalog '{catalogLabel}' is null."));
            return;
        }

        HashSet<string> requiredNouns = CollectRequiredSliceNouns(catalog);
        HashSet<string> requiredVerbs = CollectRequiredSliceVerbs(catalog);
        HashSet<string> requiredAdjectives = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BIG", "SMALL" };

        foreach (string nounToken in requiredNouns.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            NounDefinition noun = FindNoun(catalog, nounToken);
            if (noun == null)
            {
                issues.Add(new ContentValidationIssue(
                    ContentValidationSeverity.Error,
                    $"Creature catalog '{catalogLabel}' is missing the Class 1-2 noun '{nounToken}'.",
                    catalog));
                continue;
            }

            if (noun.IsCreatureNoun && noun.prefabOverride == null)
            {
                issues.Add(new ContentValidationIssue(
                    ContentValidationSeverity.Warning,
                    $"Creature catalog '{catalogLabel}' noun '{nounToken}' has no summon prefab. Combat will spawn the placeholder cube.",
                    catalog));
            }
        }

        ValidateSummonableNounVerbCoverage(catalog, catalogLabel, issues);

        foreach (string verbToken in requiredVerbs.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            VerbActionDefinition verb = FindVerb(catalog, verbToken);
            if (verb == null)
            {
                issues.Add(new ContentValidationIssue(
                    ContentValidationSeverity.Error,
                    $"Creature catalog '{catalogLabel}' is missing the Class 1-2 verb '{verbToken}'.",
                    catalog));
                continue;
            }

            if (string.IsNullOrWhiteSpace(verb.animationTrigger))
            {
                issues.Add(new ContentValidationIssue(
                    ContentValidationSeverity.Warning,
                    $"Creature catalog '{catalogLabel}' verb '{verbToken}' has no animation trigger assigned.",
                    catalog));
            }

            if (verb.role == BattleActionRole.Offense && verb.effectPrefab == null)
            {
                issues.Add(new ContentValidationIssue(
                    ContentValidationSeverity.Warning,
                    $"Creature catalog '{catalogLabel}' offense verb '{verbToken}' has no effect prefab.",
                    catalog));
            }
        }

        foreach (string adjectiveToken in requiredAdjectives.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            if (FindModifier(catalog, adjectiveToken, ModifierGrammarRole.Adjective) != null)
                continue;

            issues.Add(new ContentValidationIssue(
                ContentValidationSeverity.Error,
                $"Creature catalog '{catalogLabel}' is missing the Class 1-2 adjective modifier '{adjectiveToken}'.",
                catalog));
        }
    }

    static void ValidateSummonableNounVerbCoverage(
        CreatureCombatCatalog catalog,
        string catalogLabel,
        List<ContentValidationIssue> issues)
    {
        if (catalog?.nouns == null)
            return;

        foreach (NounDefinition noun in catalog.nouns)
        {
            if (noun == null)
                continue;

            string nounToken = CreaturePhraseUtility.NormalizeToken(noun.canonicalNoun);
            if (string.IsNullOrEmpty(nounToken))
                continue;
            if (!noun.IsCreatureNoun)
                continue;

            if (noun.moveSet == null || noun.moveSet.Count < 3)
            {
                issues.Add(new ContentValidationIssue(
                    ContentValidationSeverity.Error,
                    $"Creature catalog '{catalogLabel}' noun '{nounToken}' needs at least three verb slots: attack, movement, and defense.",
                    catalog));
                continue;
            }

            if (!noun.HasRequiredVerbCategory(CreatureVerbCategory.Attack, catalog.verbs))
                AddMissingVerbCategoryIssue(catalog, catalogLabel, nounToken, "attack", issues);
            if (!noun.HasRequiredVerbCategory(CreatureVerbCategory.Movement, catalog.verbs))
                AddMissingVerbCategoryIssue(catalog, catalogLabel, nounToken, "movement", issues);
            if (!noun.HasRequiredVerbCategory(CreatureVerbCategory.Defense, catalog.verbs))
                AddMissingVerbCategoryIssue(catalog, catalogLabel, nounToken, "defense", issues);
        }
    }

    static void AddMissingVerbCategoryIssue(
        CreatureCombatCatalog catalog,
        string catalogLabel,
        string nounToken,
        string categoryLabel,
        List<ContentValidationIssue> issues)
    {
        issues.Add(new ContentValidationIssue(
            ContentValidationSeverity.Error,
            $"Creature catalog '{catalogLabel}' noun '{nounToken}' is missing a {categoryLabel} verb. Every summonable noun must map at least one attack, one movement action, and one defense action.",
            catalog));
    }

    static HashSet<string> CollectRequiredSliceNouns(CreatureCombatCatalog catalog)
    {
        var nouns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (NaturalGrammarRegion region in NaturalGrammarProgression.Regions)
        {
            if (region == null || !region.combatUnlocked)
                continue;

            CollectEncounterPoolNouns(region.wildEncounterPools, nouns);
            CollectEncounterPoolNouns(region.trainerBattlePools, nouns);

            foreach (string token in region.vocabularyPool ?? Array.Empty<string>())
            {
                NounDefinition noun = FindNoun(catalog, token);
                if (noun != null)
                    nouns.Add(CreaturePhraseUtility.NormalizeToken(noun.canonicalNoun));
            }
        }

        return nouns;
    }

    static HashSet<string> CollectRequiredSliceVerbs(CreatureCombatCatalog catalog)
    {
        var verbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (NaturalGrammarRegion region in NaturalGrammarProgression.Regions)
        {
            if (region == null || !region.combatUnlocked)
                continue;

            foreach (string token in region.vocabularyPool ?? Array.Empty<string>())
            {
                VerbActionDefinition verb = FindVerb(catalog, token);
                if (verb != null)
                    verbs.Add(CreaturePhraseUtility.NormalizeToken(verb.verb));
            }
        }

        return verbs;
    }

    static void CollectEncounterPoolNouns(GrammarEncounterPoolDefinition[] pools, HashSet<string> nouns)
    {
        if (pools == null || nouns == null)
            return;

        foreach (GrammarEncounterPoolDefinition pool in pools)
        {
            if (pool?.nounFamilies == null)
                continue;

            foreach (string noun in pool.nounFamilies)
            {
                string normalized = CreaturePhraseUtility.NormalizeToken(noun);
                if (!string.IsNullOrEmpty(normalized))
                    nouns.Add(normalized);
            }
        }
    }

    static void RequirePrefabSet(GameObject[] prefabs, string message, Object context, List<ContentValidationIssue> issues)
    {
        if (!HasPrefabAssignment(prefabs))
            issues.Add(new ContentValidationIssue(ContentValidationSeverity.Warning, message, context));
    }

    static bool HasPrefabAssignment(GameObject[] prefabs)
    {
        if (prefabs == null || prefabs.Length == 0)
            return false;

        foreach (GameObject prefab in prefabs)
        {
            if (prefab != null)
                return true;
        }

        return false;
    }

    static NounDefinition FindNoun(CreatureCombatCatalog catalog, string token)
    {
        if (catalog?.nouns == null)
            return null;

        foreach (NounDefinition noun in catalog.nouns)
        {
            if (noun != null && noun.Matches(token))
                return noun;
        }

        return null;
    }

    static VerbActionDefinition FindVerb(CreatureCombatCatalog catalog, string token)
    {
        if (catalog?.verbs == null)
            return null;

        foreach (VerbActionDefinition verb in catalog.verbs)
        {
            if (verb != null && verb.Matches(token))
                return verb;
        }

        return null;
    }

    static ModifierDefinition FindModifier(CreatureCombatCatalog catalog, string token, ModifierGrammarRole role)
    {
        if (catalog?.modifiers == null)
            return null;

        foreach (ModifierDefinition modifier in catalog.modifiers)
        {
            if (modifier != null && modifier.role == role && modifier.Matches(token))
                return modifier;
        }

        return null;
    }

    static List<T> LoadAssets<T>() where T : Object
    {
        return AssetDatabase.FindAssets($"t:{typeof(T).Name}")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<T>)
            .Where(asset => asset != null)
            .ToList();
    }

    static void LogIssues(IEnumerable<ContentValidationIssue> issues)
    {
        foreach (ContentValidationIssue issue in issues)
        {
            if (issue == null)
                continue;

            if (issue.severity == ContentValidationSeverity.Error)
                Debug.LogError(issue.message, issue.context);
            else
                Debug.LogWarning(issue.message, issue.context);
        }
    }
}
