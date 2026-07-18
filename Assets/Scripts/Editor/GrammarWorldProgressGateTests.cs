using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class GrammarWorldProgressGateTests
{
    [Test]
    public void ManualCompletionApis_CannotBypassDialogueOrEncounterRequirements()
    {
        string previousProfile = PlayerSaveSlots.ActiveProfileId;
        string testProfile = $"progress-gates-{Guid.NewGuid():N}";
        PlayerSaveSlots.SelectProfile(testProfile);
        PlayerSaveSlots.DeleteActiveSlot();

        var serviceObject = new GameObject("ProgressGateService");
        var sceneObject = new GameObject("ProgressGateRoute");
        try
        {
            var service = serviceObject.AddComponent<GrammarWorldProgressService>();
            var controller = sceneObject.AddComponent<GrammarSceneController>();
            controller.mapAreaId = GrammarWorldProgressService.BuildAreaId(
                SemanticZoneKind.Route,
                "Nouns",
                5);
            controller.sceneKind = SemanticZoneKind.Route;
            controller.grammarTopic = "Nouns";
            controller.grammarTopicTier = 5;
            service.RegisterCurrentScene(controller);

            GrammarMapAreaState area = FindArea(service, controller.mapAreaId);
            service.MarkCurrentAreaObjectiveCompleted();
            Assert.IsFalse(area.objectiveCompleted, "Manual current-area completion must still require dialogue.");

            foreach (LocalizedDialogueLine line in NaturalGrammarProgression.BuildGeneratedDialogueSet(
                         SemanticZoneKind.Route,
                         "Nouns",
                         5,
                         trainerBattle: true))
            {
                service.RegisterCurrentAreaDialogueTaskCompleted(
                    line,
                    SemanticZoneKind.Route,
                    "Nouns",
                    5);
            }

            service.MarkAreaObjectiveCompleted(SemanticZoneKind.Route, "Nouns", 5);
            Assert.IsFalse(area.objectiveCompleted, "Manual area completion must still require its encounter.");

            MethodInfo encounterEnded = typeof(GrammarWorldProgressService).GetMethod(
                "HandleEncounterEnded",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(encounterEnded);
            encounterEnded.Invoke(service, new object[] { new WaveDescriptor(), EncounterOutcome.Completed });

            Assert.IsTrue(area.encounterCompleted);
            Assert.IsTrue(area.objectiveCompleted);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(sceneObject);
            UnityEngine.Object.DestroyImmediate(serviceObject);
            PlayerSaveSlots.DeleteActiveSlot();
            PlayerSaveSlots.SelectProfile(previousProfile);
        }
    }

    static GrammarMapAreaState FindArea(GrammarWorldProgressService service, string areaId)
    {
        GrammarMapAreaState area = service.Data.areas.Find(candidate =>
            candidate != null &&
            string.Equals(candidate.areaId, areaId, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(area);
        return area;
    }
}
