#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public static class GrammarRpgCodeLevelPlaythroughCheck
{
    [MenuItem("The Script/Grammar Scenes/Run Code-Level RPG Playthrough", false, 76)]
    public static void ValidateFromMenu()
    {
        List<string> errors = Run();
        LogResult(errors);
    }

    public static void RunBatchmode()
    {
        List<string> errors = Run();
        LogResult(errors);
        if (Application.isBatchMode)
            EditorApplication.Exit(errors.Count == 0 ? 0 : 1);
    }

    public static List<string> Run()
    {
        var errors = new List<string>();
        string previousProfile = PlayerSaveSlots.ActiveProfileId;
        string testProfile = $"code-playthrough-{Guid.NewGuid():N}";

        PlayerSaveSlots.SelectProfile(testProfile);
        PlayerSaveSlots.DeleteActiveSlot();

        GameObject progressObject = new GameObject("CodeLevelPlaythroughProgress");
        GameObject combatObject = new GameObject("CodeLevelPlaythroughCombat");
        GrammarWorldProgressService progress = null;

        try
        {
            progress = progressObject.AddComponent<GrammarWorldProgressService>();
            progress.ResetWorldProgress();

            var registry = combatObject.AddComponent<CreatureCombatRegistry>();
            registry.catalog = CreatureCombatCatalog.CreateRuntimeDefault();

            var combat = combatObject.AddComponent<CreatureCombatController>();
            combat.registry = registry;
            combat.summonCooldownSeconds = 0f;
            combat.enabledForPhrases = true;

            ValidateContentContract(registry, errors);
            ValidateWorldLoop(progress, registry, combat, errors);
        }
        catch (Exception ex)
        {
            errors.Add($"Code-level playthrough crashed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(combatObject);
            UnityEngine.Object.DestroyImmediate(progressObject);
            PlayerSaveSlots.DeleteActiveSlot();
            PlayerSaveSlots.SelectProfile(previousProfile);
        }

        return errors;
    }

    static void ValidateContentContract(CreatureCombatRegistry registry, List<string> errors)
    {
        List<string> contentErrors = GrammarRpgContentValidation.ValidateRegions(
            NaturalGrammarProgression.Regions,
            registry,
            NaturalGrammarProgression.DialogueTasks);

        foreach (string error in contentErrors)
            errors.Add($"Content contract: {error}");
    }

    static void ValidateWorldLoop(
        GrammarWorldProgressService progress,
        CreatureCombatRegistry registry,
        CreatureCombatController combat,
        List<string> errors)
    {
        IReadOnlyList<NaturalGrammarRegion> regions = NaturalGrammarProgression.Regions;
        for (int i = 0; i < regions.Count; i++)
        {
            NaturalGrammarRegion region = regions[i];
            string townId = GrammarWorldProgressService.BuildAreaId(SemanticZoneKind.Town, region.grammarTopic, region.tier);
            string routeId = GrammarWorldProgressService.BuildAreaId(SemanticZoneKind.Route, region.grammarTopic, region.tier);
            string gymId = GrammarWorldProgressService.BuildAreaId(SemanticZoneKind.Gym, region.grammarTopic, region.tier);
            string nextTownId = i + 1 < regions.Count
                ? GrammarWorldProgressService.BuildAreaId(SemanticZoneKind.Town, regions[i + 1].grammarTopic, regions[i + 1].tier)
                : "";

            RegisterVirtualScene(progress, SemanticZoneKind.Town, region, townId, routeId);
            ExpectCompleteAfterDialogue(progress, region, SemanticZoneKind.Town, townId, requiresEncounter: false, errors);

            RegisterVirtualScene(progress, SemanticZoneKind.Route, region, routeId, townId, gymId);
            bool routeRequiresEncounter = RegionRequiresEncounter(region, SemanticZoneKind.Route);
            ExpectCompleteAfterDialogue(progress, region, SemanticZoneKind.Route, routeId, routeRequiresEncounter, errors);
            ValidateCombatForRegion(progress, region, registry, combat, routeId, errors);
            if (routeRequiresEncounter)
                CompleteEncounter(progress);
            ExpectAreaCompleted(progress, routeId, $"Route {region.displayName}", errors);

            RegisterVirtualScene(progress, SemanticZoneKind.Gym, region, gymId, routeId, nextTownId);
            bool gymRequiresEncounter = RegionRequiresEncounter(region, SemanticZoneKind.Gym);
            ExpectCompleteAfterDialogue(progress, region, SemanticZoneKind.Gym, gymId, gymRequiresEncounter, errors);
            if (gymRequiresEncounter)
                CompleteEncounter(progress);
            ExpectAreaCompleted(progress, gymId, $"Gym {region.displayName}", errors);
            ExpectGymRewards(progress, region, gymId, errors);

            if (!string.IsNullOrWhiteSpace(nextTownId))
                ExpectVisible(progress, nextTownId, $"Next town after {region.displayName}", errors);
        }
    }

    static void RegisterVirtualScene(
        GrammarWorldProgressService progress,
        SemanticZoneKind kind,
        NaturalGrammarRegion region,
        string areaId,
        params string[] connections)
    {
        GameObject sceneObject = new GameObject($"Virtual_{kind}_{region.tier}");
        try
        {
            var controller = sceneObject.AddComponent<GrammarSceneController>();
            controller.mapAreaId = areaId;
            controller.sceneKind = kind;
            controller.grammarTopic = region.grammarTopic;
            controller.grammarTopicTier = region.tier;
            controller.mapDisplayName = kind == SemanticZoneKind.Town
                ? region.displayName
                : $"{region.displayName} {kind}";
            controller.connectedMapAreaIds = new List<string>();
            foreach (string connection in connections)
                if (!string.IsNullOrWhiteSpace(connection))
                    controller.connectedMapAreaIds.Add(connection);

            progress.RegisterCurrentScene(controller);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(sceneObject);
        }
    }

    static void ExpectCompleteAfterDialogue(
        GrammarWorldProgressService progress,
        NaturalGrammarRegion region,
        SemanticZoneKind kind,
        string areaId,
        bool requiresEncounter,
        List<string> errors)
    {
        bool tasksComplete = CompleteDialogueTasks(progress, region, kind);
        if (!tasksComplete)
            errors.Add($"{kind} {region.displayName}: required dialogue tasks did not all complete.");

        GrammarMapAreaState area = FindArea(progress, areaId);
        if (area == null)
        {
            errors.Add($"{kind} {region.displayName}: area '{areaId}' was not registered.");
            return;
        }

        if (requiresEncounter)
        {
            if (area.objectiveCompleted)
                errors.Add($"{kind} {region.displayName}: completed before its required encounter.");
        }
        else if (!area.objectiveCompleted)
        {
            errors.Add($"{kind} {region.displayName}: did not complete after required dialogue.");
        }
    }

    static bool CompleteDialogueTasks(
        GrammarWorldProgressService progress,
        NaturalGrammarRegion region,
        SemanticZoneKind kind)
    {
        bool trainerBattle = kind != SemanticZoneKind.Town &&
            region.encounterMode == GrammarEncounterMode.TacticalCommand;
        bool tasksComplete = false;
        foreach (LocalizedDialogueLine line in NaturalGrammarProgression.BuildGeneratedDialogueSet(
                     kind,
                     region.grammarTopic,
                     region.tier,
                     trainerBattle))
        {
            tasksComplete = progress.RegisterCurrentAreaDialogueTaskCompleted(
                line,
                kind,
                region.grammarTopic,
                region.tier);
        }

        return tasksComplete;
    }

    static void ValidateCombatForRegion(
        GrammarWorldProgressService progress,
        NaturalGrammarRegion region,
        CreatureCombatRegistry registry,
        CreatureCombatController combat,
        string routeId,
        List<string> errors)
    {
        if (!region.combatUnlocked)
            return;

        progress.PrepareAreaTransition(routeId, GrammarWorldRuntimeBootstrap.RouteSceneName);
        combat.ClearActiveCreature();
        combat.activeCurse = GrammarBattleCurse.None;

        foreach (string phrase in BuildCombatTranscriptSamples(region))
        {
            if (!registry.TryParsePhrase(phrase, out CreaturePhraseParseResult parsed))
            {
                errors.Add($"{region.displayName}: combat transcript '{phrase}' did not parse.");
                continue;
            }

            bool handled = combat.TryHandlePhrase(phrase);
            if (!handled)
                errors.Add($"{region.displayName}: combat transcript '{phrase}' parsed as {parsed.pattern} but was not handled.");
        }

        if (registry.TryParsePhrase("blue toaster", out _))
            errors.Add($"{region.displayName}: invalid combat transcript unexpectedly parsed.");

        if (region.conceptId == GrammarConceptId.BasicPrepositions)
            ValidatePrepositionTactics(registry, errors);
    }

    static IEnumerable<string> BuildCombatTranscriptSamples(NaturalGrammarRegion region)
    {
        yield return "rat";

        if (RegionUnlocks(region, GrammarPhrasePattern.VerbOnly))
            yield return "bite";
        if (RegionUnlocks(region, GrammarPhrasePattern.NounVerbPresent))
            yield return "rat bites";
        if (RegionUnlocks(region, GrammarPhrasePattern.DeterminerNoun))
            yield return "a rat";
        if (RegionUnlocks(region, GrammarPhrasePattern.PronounVerbPresent))
            yield return "he bites";
        if (RegionUnlocks(region, GrammarPhrasePattern.AdjectiveNoun))
            yield return "big rat";
        if (RegionUnlocks(region, GrammarPhrasePattern.DeterminerAdjectiveNoun))
            yield return "a big rat";
    }

    static void ValidatePrepositionTactics(CreatureCombatRegistry registry, List<string> errors)
    {
        ValidateTacticalCommand(
            registry,
            "rat jumps over the wall",
            battler =>
            {
                battler.SetTerrain(new TacticalBattlePosition(1, 0), TacticalBattleCellType.Wall);
                battler.SetEnemyUnit("CAT", new TacticalBattlePosition(4, 0));
            },
            errors);

        ValidateTacticalCommand(
            registry,
            "rat runs through the bridge",
            battler =>
            {
                // Keep the authored bridge inside RUN's two-cell movement budget;
                // the check validates THROUGH resolution, not a range failure.
                battler.SetTerrain(new TacticalBattlePosition(2, 0), TacticalBattleCellType.Bridge);
                battler.SetEnemyUnit("CAT", new TacticalBattlePosition(4, 4));
            },
            errors);

        ValidateTacticalCommand(
            registry,
            "rat runs beside the rock",
            battler =>
            {
                battler.SetTerrain(new TacticalBattlePosition(2, 2), TacticalBattleCellType.Rock);
                battler.SetEnemyUnit("CAT", new TacticalBattlePosition(4, 4));
            },
            errors);
    }

    static void ValidateTacticalCommand(
        CreatureCombatRegistry registry,
        string command,
        Action<TacticalGrammarBattler> configure,
        List<string> errors)
    {
        var battler = new TacticalGrammarBattler(registry);
        configure?.Invoke(battler);
        TacticalBattleCommandResult summon = battler.SummonPlayer("RAT", new TacticalBattlePosition(0, 0));
        if (!summon.success)
        {
            errors.Add($"Preposition tactics: could not summon RAT for '{command}': {summon.message}");
            return;
        }

        TacticalBattleCommandResult result = battler.ExecutePlayerCommand(command);
        if (!result.success)
            errors.Add($"Preposition tactics: '{command}' failed: {result.message}");
    }

    static void ExpectAreaCompleted(
        GrammarWorldProgressService progress,
        string areaId,
        string label,
        List<string> errors)
    {
        GrammarMapAreaState area = FindArea(progress, areaId);
        if (area == null)
        {
            errors.Add($"{label}: area '{areaId}' was not registered.");
            return;
        }

        if (!area.objectiveCompleted)
            errors.Add($"{label}: area did not complete.");
    }

    static void ExpectGymRewards(
        GrammarWorldProgressService progress,
        NaturalGrammarRegion region,
        string gymId,
        List<string> errors)
    {
        if (!progress.IsGymCleared(gymId))
            errors.Add($"{region.displayName}: gym is not recorded as cleared.");

        foreach (GrammarPhrasePattern pattern in region.unlockedPhrasePatterns ?? Array.Empty<GrammarPhrasePattern>())
        {
            if (!progress.Data.unlockedGrammarPatterns.Contains(pattern.ToString()))
                errors.Add($"{region.displayName}: unlocked pattern '{pattern}' was not recorded after gym clear.");
        }

        foreach (string word in region.vocabularyPool ?? Array.Empty<string>())
        {
            string normalized = SpellRegistry.NormalizeWord(word);
            if (!string.IsNullOrEmpty(normalized) && !progress.Data.unlockedVocabulary.Contains(normalized))
                errors.Add($"{region.displayName}: vocabulary '{normalized}' was not recorded after gym clear.");
        }
    }

    static void ExpectVisible(
        GrammarWorldProgressService progress,
        string areaId,
        string label,
        List<string> errors)
    {
        GrammarMapAreaState area = FindArea(progress, areaId);
        if (area == null)
        {
            errors.Add($"{label}: area '{areaId}' was not registered.");
            return;
        }

        if (!area.visible)
            errors.Add($"{label}: area was not revealed.");
    }

    static bool RegionRequiresEncounter(NaturalGrammarRegion region, SemanticZoneKind kind)
    {
        return region != null &&
            region.encounterMode != GrammarEncounterMode.None &&
            (kind == SemanticZoneKind.Route || kind == SemanticZoneKind.Gym);
    }

    static bool RegionUnlocks(NaturalGrammarRegion region, GrammarPhrasePattern pattern)
    {
        if (region == null || region.unlockedPhrasePatterns == null)
            return false;

        foreach (GrammarPhrasePattern candidate in region.unlockedPhrasePatterns)
            if (candidate == pattern)
                return true;
        return false;
    }

    static GrammarMapAreaState FindArea(GrammarWorldProgressService progress, string areaId)
    {
        return progress.Data.areas.Find(candidate =>
            candidate != null &&
            string.Equals(candidate.areaId, areaId, StringComparison.OrdinalIgnoreCase));
    }

    static void CompleteEncounter(GrammarWorldProgressService progress)
    {
        MethodInfo handler = typeof(GrammarWorldProgressService).GetMethod(
            "HandleEncounterEnded",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (handler == null)
            throw new MissingMethodException(nameof(GrammarWorldProgressService), "HandleEncounterEnded");
        handler.Invoke(progress, new object[] { new WaveDescriptor(), EncounterOutcome.Completed });
    }

    static void LogResult(List<string> errors)
    {
        if (errors.Count == 0)
        {
            Debug.Log("[GrammarRpgCodeLevelPlaythroughCheck] PASS: all canonical regions completed Town -> Route -> Gym -> next Town at code level, with dialogue gates, encounter gates, gym rewards, and combat transcript checks.");
            return;
        }

        foreach (string error in errors)
            Debug.LogError($"[GrammarRpgCodeLevelPlaythroughCheck] {error}");
        Debug.LogError($"[GrammarRpgCodeLevelPlaythroughCheck] FAIL: {errors.Count} issue(s).");
    }
}

public sealed class GrammarRpgCodeLevelPlaythroughTests
{
    [Test]
    public void CanonicalWorldCompletesEndToEndAtCodeLevel()
    {
        List<string> errors = GrammarRpgCodeLevelPlaythroughCheck.Run();
        Assert.IsEmpty(errors, string.Join("\n", errors));
    }
}
#endif
