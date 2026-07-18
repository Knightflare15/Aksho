using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class GrammarSliceAssetCoverageValidatorTests
{
    [Test]
    public void GrammarSliceAssetCoverageValidator_FlagsMissingTownPrefabCoverage()
    {
        var go = new GameObject("TownCoverageAuditTest");
        try
        {
            var controller = go.AddComponent<GrammarSceneController>();
            controller.sceneKind = SemanticZoneKind.Town;
            controller.grammarTopic = "Greetings and Survival English";
            controller.grammarTopicTier = 1;
            controller.mapDisplayName = "Welcome Village";

            var generator = go.AddComponent<ProceduralGrammarSceneGenerator>();
            generator.sceneController = controller;
            generator.prefabSet = new GrammarScenePrefabSet();

            var issues = new List<ContentValidationIssue>();
            GrammarSliceAssetCoverageValidator.ValidateSceneGenerator(generator, issues);

            Assert.IsTrue(ContainsMessage(issues, "uses fallback generated ground"));
            Assert.IsTrue(ContainsMessage(issues, "missing town building prefabs"));
            Assert.IsTrue(ContainsMessage(issues, "missing town road prefabs"));
            Assert.IsTrue(ContainsMessage(issues, "missing town NPC prefabs"));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void GrammarSliceAssetCoverageValidator_RuntimeDefaultsExposeMissingSliceCombatAssets()
    {
        CreatureCombatCatalog catalog = CreatureCombatCatalog.CreateRuntimeDefault();
        try
        {
            var issues = new List<ContentValidationIssue>();
            GrammarSliceAssetCoverageValidator.ValidateCreatureCombatCatalog(catalog, "Runtime defaults", issues);

            Assert.IsTrue(ContainsMessage(issues, "noun 'RAT' has no summon prefab"));
            Assert.IsFalse(ContainsMessage(issues, "noun 'ROOF' has no summon prefab"));
            Assert.IsFalse(ContainsMessage(issues, "noun 'BRIDGE' has no summon prefab"));
            Assert.IsTrue(ContainsMessage(issues, "offense verb 'BITE' has no effect prefab"));
            Assert.IsFalse(ContainsMessage(issues, "missing the Class 1-2 adjective modifier 'BIG'"));
            Assert.IsFalse(ContainsMessage(issues, "missing the Class 1-2 adjective modifier 'SMALL'"));
        }
        finally
        {
            Object.DestroyImmediate(catalog);
        }
    }

    static bool ContainsMessage(List<ContentValidationIssue> issues, string fragment)
    {
        if (issues == null || string.IsNullOrWhiteSpace(fragment))
            return false;

        foreach (ContentValidationIssue issue in issues)
        {
            if (issue != null &&
                !string.IsNullOrWhiteSpace(issue.message) &&
                issue.message.IndexOf(fragment, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }
}
