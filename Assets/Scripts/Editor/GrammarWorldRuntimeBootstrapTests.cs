using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class GrammarWorldRuntimeBootstrapTests
{
    [Test]
    public void GrammarWorldProgressService_PrepareAreaTransition_UpdatesAreaAndClearsSavedPosition()
    {
        GrammarWorldProgressService service = GrammarWorldProgressService.Instance;
        GrammarWorldProgressData data = service.Data;
        string areaId = $"TOWN:TRANSITIONTEST:{Guid.NewGuid():N}";

        data.hasLastPlayerPosition = true;
        data.lastPlayerPosition = new Vector3(12f, 3f, -8f);

        service.PrepareAreaTransition(areaId, GrammarWorldRuntimeBootstrap.DefaultGrammarWorldSceneName);

        Assert.AreEqual(areaId, data.currentAreaId);
        Assert.AreEqual(GrammarWorldRuntimeBootstrap.DefaultGrammarWorldSceneName, data.currentSceneName);
        Assert.IsFalse(data.hasLastPlayerPosition);
        Assert.IsTrue(data.areas.Exists(area => area != null && area.areaId == areaId && area.visible));
    }

    [Test]
    public void GrammarWorldRuntimeBootstrap_ResolvesRouteBlueprintWithPartialBuddyAndTownGymConnections()
    {
        string routeAreaId = GrammarWorldProgressService.BuildAreaId(SemanticZoneKind.Route, "Basic Prepositions", 11);
        string townAreaId = GrammarWorldProgressService.BuildAreaId(SemanticZoneKind.Town, "Basic Prepositions", 11);
        string gymAreaId = GrammarWorldProgressService.BuildAreaId(SemanticZoneKind.Gym, "Basic Prepositions", 11);

        bool resolved = GrammarWorldRuntimeBootstrap.TryResolveAreaBlueprint(
            routeAreaId,
            out string displayName,
            out SemanticZoneKind sceneKind,
            out string grammarTopic,
            out int grammarTopicTier,
            out TranslatorAssistMode assistMode,
            out List<string> connectedAreaIds);

        Assert.IsTrue(resolved);
        Assert.AreEqual(SemanticZoneKind.Route, sceneKind);
        Assert.AreEqual("Basic Prepositions", grammarTopic);
        Assert.AreEqual(11, grammarTopicTier);
        Assert.AreEqual(TranslatorAssistMode.Partial, assistMode);
        StringAssert.Contains("Route", displayName);
        CollectionAssert.Contains(connectedAreaIds, townAreaId);
        CollectionAssert.Contains(connectedAreaIds, gymAreaId);
    }

    [Test]
    public void GrammarWorldRuntimeBootstrap_ResolvesTemplateSceneNamesByAreaKind()
    {
        string townAreaId = GrammarWorldProgressService.BuildAreaId(SemanticZoneKind.Town, "Greetings and Survival English", 1);
        string routeAreaId = GrammarWorldProgressService.BuildAreaId(SemanticZoneKind.Route, "Greetings and Survival English", 1);
        string gymAreaId = GrammarWorldProgressService.BuildAreaId(SemanticZoneKind.Gym, "Greetings and Survival English", 1);

        Assert.AreEqual(GrammarWorldRuntimeBootstrap.TownSceneName, GrammarWorldRuntimeBootstrap.ResolveSceneNameForAreaId(townAreaId));
        Assert.AreEqual(GrammarWorldRuntimeBootstrap.RouteSceneName, GrammarWorldRuntimeBootstrap.ResolveSceneNameForAreaId(routeAreaId));
        Assert.AreEqual(GrammarWorldRuntimeBootstrap.GymSceneName, GrammarWorldRuntimeBootstrap.ResolveSceneNameForAreaId(gymAreaId));
        Assert.AreEqual(GrammarWorldRuntimeBootstrap.TownSceneName, GrammarWorldRuntimeBootstrap.DefaultGrammarWorldSceneName);
        Assert.IsTrue(GrammarWorldRuntimeBootstrap.IsGrammarWorldTemplateSceneName(GrammarWorldRuntimeBootstrap.TownSceneName));
        Assert.IsTrue(GrammarWorldRuntimeBootstrap.IsGrammarWorldTemplateSceneName(GrammarWorldRuntimeBootstrap.RouteSceneName));
        Assert.IsTrue(GrammarWorldRuntimeBootstrap.IsGrammarWorldTemplateSceneName(GrammarWorldRuntimeBootstrap.GymSceneName));
        Assert.IsFalse(GrammarWorldRuntimeBootstrap.IsGrammarWorldTemplateSceneName(GrammarWorldRuntimeBootstrap.BattleSceneName));
    }

    [Test]
    public void TacticalGrammarBattleController_AndConjunctionCarriesSubjectIntoSecondVerb()
    {
        var go = new GameObject("TacticalConjunctionAndTest");
        try
        {
            var registry = go.AddComponent<CreatureCombatRegistry>();
            registry.catalog = CreatureCombatCatalog.CreateRuntimeDefault();

            bool resolved = TacticalGrammarBattleController.TryBuildConjunctionClauses(
                registry,
                "the rat jumps over the rock and bites",
                out string conjunction,
                out List<string> clauses,
                out string rejectionMessage);

            Assert.IsTrue(resolved);
            Assert.AreEqual("AND", conjunction);
            Assert.IsEmpty(rejectionMessage);
            CollectionAssert.AreEqual(
                new[] { "THE RAT JUMPS OVER THE ROCK", "THE RAT BITES" },
                clauses);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void TacticalGrammarBattleController_RejectsAndWithoutSecondVerbClause()
    {
        var go = new GameObject("TacticalConjunctionInvalidAndTest");
        try
        {
            var registry = go.AddComponent<CreatureCombatRegistry>();
            registry.catalog = CreatureCombatCatalog.CreateRuntimeDefault();

            bool resolved = TacticalGrammarBattleController.TryBuildConjunctionClauses(
                registry,
                "rat and bite",
                out string conjunction,
                out List<string> clauses,
                out string rejectionMessage);

            Assert.IsTrue(resolved);
            Assert.AreEqual("AND", conjunction);
            Assert.IsNotEmpty(rejectionMessage);
            Assert.IsEmpty(clauses);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void TacticalGrammarBattleController_OrAndBecauseConjunctionsResolvePracticeClauses()
    {
        var go = new GameObject("TacticalConjunctionOrBecauseTest");
        try
        {
            var registry = go.AddComponent<CreatureCombatRegistry>();
            registry.catalog = CreatureCombatCatalog.CreateRuntimeDefault();

            bool orResolved = TacticalGrammarBattleController.TryBuildConjunctionClauses(
                registry,
                "the rat jumps over the rock or bites",
                out string orConjunction,
                out List<string> orClauses,
                out string orRejection);

            Assert.IsTrue(orResolved);
            Assert.AreEqual("OR", orConjunction);
            Assert.IsEmpty(orRejection);
            CollectionAssert.AreEqual(
                new[] { "THE RAT JUMPS OVER THE ROCK", "THE RAT BITES" },
                orClauses);

            bool becauseResolved = TacticalGrammarBattleController.TryBuildConjunctionClauses(
                registry,
                "the rat hides because the rat bites",
                out string becauseConjunction,
                out List<string> becauseClauses,
                out string becauseRejection);

            Assert.IsTrue(becauseResolved);
            Assert.AreEqual("BECAUSE", becauseConjunction);
            Assert.IsEmpty(becauseRejection);
            CollectionAssert.AreEqual(
                new[] { "THE RAT HIDES", "THE RAT BITES" },
                becauseClauses);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void TacticalGrammarBattleController_BecauseReasonMustStayInSameWordFamily()
    {
        var go = new GameObject("TacticalConjunctionBecauseFamilyTest");
        try
        {
            var registry = go.AddComponent<CreatureCombatRegistry>();
            registry.catalog = CreatureCombatCatalog.CreateRuntimeDefault();

            bool resolved = TacticalGrammarBattleController.TryBuildConjunctionClauses(
                registry,
                "the rat hides because the cat bites",
                out string conjunction,
                out List<string> clauses,
                out string rejectionMessage);

            Assert.IsTrue(resolved);
            Assert.AreEqual("BECAUSE", conjunction);
            Assert.IsNotEmpty(rejectionMessage);
            Assert.IsEmpty(clauses);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
