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
    void EnsureLoaded()
    {
        if (data != null)
            return;

        string path = PlayerSaveSlots.GetSaveFilePath(FileName);
        if (File.Exists(path))
        {
            try
            {
                data = JsonUtility.FromJson<GrammarWorldProgressData>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GrammarWorldProgress] Could not read progress: {ex.Message}");
            }
        }

        if (data == null)
            data = new GrammarWorldProgressData();
        if (data.areas == null)
            data.areas = new List<GrammarMapAreaState>();
        data.completedAreaIds ??= new List<string>();
        data.clearedGymAreaIds ??= new List<string>();
        data.unlockedGrammarPatterns ??= new List<string>();
        data.unlockedVocabulary ??= new List<string>();
        data.unlockedConceptIds ??= new List<string>();
        data.masteredConceptIds ??= new List<string>();
        data.completedDialogueTaskKeys ??= new List<string>();

        if (LooksLikeLegacyDefaultMap(data))
        {
            data.areas.Clear();
            data.currentAreaId = "";
            data.currentSceneName = "";
        }

        MigrateAreaIdsToCanonicalForm();
        SeedDefaultMapIfNeeded();
        EnsureCanonicalMapCoverage();
        RebuildLookup();
        BackfillUnlockedRewardsFromCompletedAreas();
    }

    static bool LooksLikeLegacyDefaultMap(GrammarWorldProgressData progress)
    {
        if (progress == null || progress.areas == null || progress.areas.Count == 0)
            return false;

        bool hasLegacyStart = false;
        bool hasNaturalStart = false;
        foreach (GrammarMapAreaState area in progress.areas)
        {
            if (area == null)
                continue;
            if (area.sceneKind == SemanticZoneKind.Town &&
                area.grammarTopicTier == 1 &&
                string.Equals(area.grammarTopic, "Articles", StringComparison.OrdinalIgnoreCase))
                hasLegacyStart = true;
            if (area.sceneKind == SemanticZoneKind.Town &&
                area.grammarTopicTier == 1 &&
                string.Equals(area.grammarTopic, "Greetings and Survival English", StringComparison.OrdinalIgnoreCase))
                hasNaturalStart = true;
        }

        return hasLegacyStart && !hasNaturalStart;
    }

    void RebuildLookup()
    {
        areaLookup.Clear();
        foreach (GrammarMapAreaState area in data.areas)
        {
            if (area == null || string.IsNullOrWhiteSpace(area.areaId))
                continue;
            if (area.connectedAreaIds == null)
                area.connectedAreaIds = new List<string>();
            if (data.completedAreaIds.Contains(area.areaId))
                area.objectiveCompleted = true;
            areaLookup[area.areaId] = area;
        }
    }

    void MigrateAreaIdsToCanonicalForm()
    {
        if (data == null)
            return;

        data.currentAreaId = CanonicalizeAreaId(data.currentAreaId);
        RewriteAreaIdList(data.completedAreaIds);
        RewriteAreaIdList(data.clearedGymAreaIds);
        RewriteDialogueTaskKeys();

        if (data.areas == null || data.areas.Count == 0)
            return;

        var merged = new Dictionary<string, GrammarMapAreaState>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<GrammarMapAreaState>();
        foreach (GrammarMapAreaState area in data.areas)
        {
            if (area == null)
                continue;

            area.areaId = CanonicalizeAreaId(area.areaId);
            area.grammarTopicTier = ResolveCanonicalTier(BuildAreaTopicKey(area.grammarTopic), area.grammarTopicTier);
            if (area.connectedAreaIds == null)
                area.connectedAreaIds = new List<string>();

            if (string.IsNullOrWhiteSpace(area.areaId) || !merged.TryGetValue(area.areaId, out GrammarMapAreaState existing))
            {
                merged[area.areaId] = area;
                ordered.Add(area);
                continue;
            }

            MergeAreaState(existing, area);
        }

        data.areas = ordered;
        foreach (GrammarMapAreaState area in data.areas)
            RewriteAreaIdList(area.connectedAreaIds);
    }

    public static string CanonicalizeAreaId(string areaId)
    {
        if (string.IsNullOrWhiteSpace(areaId))
            return "";

        string trimmed = areaId.Trim();
        string[] parts = trimmed.Split(':');
        if (parts.Length != 3 || !int.TryParse(parts[2], out int tier))
            return trimmed;

        string kind = SpellRegistry.NormalizeWord(parts[0]);
        string topic = BuildAreaTopicKey(parts[1]);
        if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(topic))
            return trimmed;

        tier = ResolveCanonicalTier(topic, tier);
        return $"{kind}:{topic}:{Mathf.Max(1, tier)}";
    }

    static int ResolveCanonicalTier(string topicKey, int fallback)
    {
        return topicKey switch
        {
            "GREETINGSANDSURVIVALENGLISH" => 1,
            "ALPHABET" => 2,
            "VOWELSANDCONSONANTS" => 3,
            "SENTENCESTARTANDFULLSTOP" => 4,
            "NOUNS" => 5,
            "VERBS" => 6,
            "ARTICLES" => 7,
            "PRONOUNS" => 8,
            "PLURALS" => 9,
            "ADJECTIVES" => 10,
            "BASICPREPOSITIONS" => 11,
            _ => fallback,
        };
    }

    static void RewriteAreaIdList(List<string> values)
    {
        if (values == null)
            return;

        var rewritten = new List<string>();
        foreach (string value in values)
            AddUnique(rewritten, CanonicalizeAreaId(value));

        values.Clear();
        values.AddRange(rewritten);
    }

    void RewriteDialogueTaskKeys()
    {
        if (data.completedDialogueTaskKeys == null)
            return;

        var rewritten = new List<string>();
        foreach (string key in data.completedDialogueTaskKeys)
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;

            int separator = key.IndexOf('|');
            if (separator < 0)
            {
                AddUnique(rewritten, key.Trim());
                continue;
            }

            string areaId = CanonicalizeAreaId(key.Substring(0, separator));
            string taskId = key.Substring(separator + 1).Trim();
            AddUnique(rewritten, BuildDialogueTaskKey(areaId, taskId));
        }

        data.completedDialogueTaskKeys.Clear();
        data.completedDialogueTaskKeys.AddRange(rewritten);
    }

    static void MergeAreaState(GrammarMapAreaState target, GrammarMapAreaState source)
    {
        if (target == null || source == null)
            return;

        if (string.IsNullOrWhiteSpace(target.displayName))
            target.displayName = source.displayName;
        if (string.IsNullOrWhiteSpace(target.sceneName))
            target.sceneName = source.sceneName;
        if (string.IsNullOrWhiteSpace(target.grammarTopic))
            target.grammarTopic = source.grammarTopic;
        if (target.grammarTopicTier <= 0)
            target.grammarTopicTier = source.grammarTopicTier;
        if (target.conceptId == GrammarConceptId.None)
            target.conceptId = source.conceptId;
        if (target.mapPosition == Vector2.zero)
            target.mapPosition = source.mapPosition;

        target.visible |= source.visible;
        target.explored |= source.explored;
        target.objectiveCompleted |= source.objectiveCompleted;
        target.encounterCompleted |= source.encounterCompleted;
        if (target.connectedAreaIds == null)
            target.connectedAreaIds = new List<string>();
        if (source.connectedAreaIds != null)
        {
            foreach (string connectedId in source.connectedAreaIds)
                AddUnique(target.connectedAreaIds, connectedId);
        }
    }
}
