using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed partial class GrammarWorldProgressService : MonoBehaviour
{
    void SeedDefaultMapIfNeeded()
    {
        if (data.areas.Count > 0)
            return;

        IReadOnlyList<NaturalGrammarRegion> regions = NaturalGrammarProgression.Regions;

        for (int i = 0; i < regions.Count; i++)
        {
            NaturalGrammarRegion region = regions[i];
            string topic = region.grammarTopic;
            int tier = region.tier;
            string townId = BuildAreaId(SemanticZoneKind.Town, topic, tier);
            string gymId = BuildAreaId(SemanticZoneKind.Gym, topic, tier);
            string routeId = BuildAreaId(SemanticZoneKind.Route, topic, tier);
            string nextTownId = i < regions.Count - 1
                ? BuildAreaId(SemanticZoneKind.Town, regions[i + 1].grammarTopic, regions[i + 1].tier)
                : "";

            var town = new GrammarMapAreaState
            {
                areaId = townId,
                displayName = region.displayName,
                sceneKind = SemanticZoneKind.Town,
                conceptId = region.conceptId,
                grammarTopic = topic,
                grammarTopicTier = tier,
                mapPosition = new Vector2(90f + i * 150f, 160f + (i % 2) * 105f),
                visible = i == 0,
                explored = false,
                connectedAreaIds = new List<string>(),
            };
            town.connectedAreaIds.Add(routeId);
            data.areas.Add(town);

            var gymConnections = new List<string> { routeId };
            if (!string.IsNullOrWhiteSpace(nextTownId))
                gymConnections.Add(nextTownId);
            var gym = new GrammarMapAreaState
            {
                areaId = gymId,
                displayName = $"{region.displayName} Gym",
                sceneKind = SemanticZoneKind.Gym,
                conceptId = region.conceptId,
                grammarTopic = topic,
                grammarTopicTier = tier,
                mapPosition = town.mapPosition + new Vector2(0f, -58f),
                visible = false,
                explored = false,
                connectedAreaIds = gymConnections,
            };
            data.areas.Add(gym);

            data.areas.Add(new GrammarMapAreaState
            {
                areaId = routeId,
                displayName = $"{region.displayName} Route",
                sceneKind = SemanticZoneKind.Route,
                conceptId = region.conceptId,
                grammarTopic = topic,
                grammarTopicTier = tier,
                mapPosition = town.mapPosition + new Vector2(60f, 48f),
                visible = false,
                explored = false,
                connectedAreaIds = new List<string> { townId, gymId },
            });
        }
    }

    void EnsureCanonicalMapCoverage()
    {
        IReadOnlyList<NaturalGrammarRegion> regions = NaturalGrammarProgression.Regions;
        for (int i = 0; i < regions.Count; i++)
        {
            NaturalGrammarRegion region = regions[i];
            string townId = BuildAreaId(SemanticZoneKind.Town, region.grammarTopic, region.tier);
            string routeId = BuildAreaId(SemanticZoneKind.Route, region.grammarTopic, region.tier);
            string gymId = BuildAreaId(SemanticZoneKind.Gym, region.grammarTopic, region.tier);
            string previousGymId = i > 0
                ? BuildAreaId(SemanticZoneKind.Gym, regions[i - 1].grammarTopic, regions[i - 1].tier)
                : "";
            string nextTownId = i + 1 < regions.Count
                ? BuildAreaId(SemanticZoneKind.Town, regions[i + 1].grammarTopic, regions[i + 1].tier)
                : "";

            bool townExisted = data.areas.Exists(area => area != null && string.Equals(area.areaId, townId, StringComparison.OrdinalIgnoreCase));
            bool routeExisted = data.areas.Exists(area => area != null && string.Equals(area.areaId, routeId, StringComparison.OrdinalIgnoreCase));
            bool gymExisted = data.areas.Exists(area => area != null && string.Equals(area.areaId, gymId, StringComparison.OrdinalIgnoreCase));
            GrammarMapAreaState town = EnsureArea(townId);
            ConfigureCanonicalArea(town, region, SemanticZoneKind.Town, region.displayName,
                new Vector2(90f + i * 150f, 160f + (i % 2) * 105f),
                string.IsNullOrWhiteSpace(previousGymId) ? new[] { routeId } : new[] { routeId, previousGymId });
            if (i == 0)
                town.visible = true;
            else if (!townExisted)
                town.visible = IsAreaObjectiveCompleted(previousGymId);

            GrammarMapAreaState route = EnsureArea(routeId);
            ConfigureCanonicalArea(route, region, SemanticZoneKind.Route, $"{region.displayName} Route",
                town.mapPosition + new Vector2(60f, 48f), new[] { townId, gymId });
            if (!routeExisted)
                route.visible = town.objectiveCompleted;

            GrammarMapAreaState gym = EnsureArea(gymId);
            ConfigureCanonicalArea(gym, region, SemanticZoneKind.Gym, $"{region.displayName} Gym",
                town.mapPosition + new Vector2(0f, -58f),
                string.IsNullOrWhiteSpace(nextTownId) ? new[] { routeId } : new[] { routeId, nextTownId });
            if (!gymExisted)
                gym.visible = route.objectiveCompleted;
        }
    }

    static void ConfigureCanonicalArea(
        GrammarMapAreaState area,
        NaturalGrammarRegion region,
        SemanticZoneKind kind,
        string displayName,
        Vector2 mapPosition,
        IEnumerable<string> connections)
    {
        area.displayName = displayName;
        area.sceneKind = kind;
        area.conceptId = region.conceptId;
        area.grammarTopic = region.grammarTopic;
        area.grammarTopicTier = region.tier;
        area.mapPosition = mapPosition;
        area.connectedAreaIds = new List<string>();
        foreach (string connection in connections)
            AddUnique(area.connectedAreaIds, connection);
    }

    static void AddUnique(List<string> values, string value)
    {
        if (values == null || string.IsNullOrWhiteSpace(value))
            return;
        if (!values.Contains(value))
            values.Add(value);
    }

    static bool ContainsPattern(List<string> values, GrammarPhrasePattern pattern)
    {
        if (values == null)
            return false;

        string expected = pattern.ToString();
        foreach (string value in values)
            if (string.Equals(value, expected, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    static bool ContainsVocabulary(IEnumerable<string> values, string word)
    {
        if (values == null || string.IsNullOrWhiteSpace(word))
            return false;

        string expected = CreaturePhraseUtility.NormalizeToken(word);
        foreach (string value in values)
            if (CreaturePhraseUtility.NormalizeToken(value) == expected)
                return true;
        return false;
    }

    static bool RegionUnlocksPattern(NaturalGrammarRegion region, GrammarPhrasePattern pattern)
    {
        if (region == null || region.unlockedPhrasePatterns == null)
            return false;

        foreach (GrammarPhrasePattern candidate in region.unlockedPhrasePatterns)
            if (candidate == pattern)
                return true;
        return false;
    }

    static bool RegionUnlocksVocabulary(NaturalGrammarRegion region, string word)
    {
        return region != null && ContainsVocabulary(region.vocabularyPool, word);
    }

    static GrammarConceptId ResolveConceptId(string grammarTopic, int grammarTopicTier)
    {
        NaturalGrammarRegion region = NaturalGrammarProgression.ResolveByTopicOrTier(grammarTopic, grammarTopicTier);
        return region != null ? region.conceptId : GrammarConceptId.None;
    }
}
