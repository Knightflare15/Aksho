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
    void HandleEncounterEnded(WaveDescriptor descriptor, EncounterOutcome outcome)
    {
        EnsureLoaded();
        GrammarMapAreaState current = !string.IsNullOrWhiteSpace(data.currentAreaId)
            ? EnsureArea(data.currentAreaId)
            : null;
        if (outcome != EncounterOutcome.Completed)
        {
            if (outcome == EncounterOutcome.Failed && current != null && current.sceneKind == SemanticZoneKind.Gym && !current.objectiveCompleted)
                CurriculumSessionManager.Instance?.RecordGymAttempt(current.areaId, current.areaId, passed: false);
            CapturePlayerPosition();
            Save();
            return;
        }

        data.encountersCompleted++;
        if (current != null && (current.sceneKind == SemanticZoneKind.Route || current.sceneKind == SemanticZoneKind.Gym))
        {
            current.encounterCompleted = true;
            if (CanCompleteAreaFromCurrentProgress(current))
                CompleteArea(current, saveAfter: false);
        }
        CapturePlayerPosition();
        Save();
    }

    void CompleteArea(GrammarMapAreaState area, bool saveAfter = true)
    {
        if (area == null)
            return;

        bool wasCompleted = area.objectiveCompleted ||
            (data.completedAreaIds != null && data.completedAreaIds.Contains(area.areaId));
        area.visible = true;
        area.explored = true;
        area.objectiveCompleted = true;
        SyncCompletionLists(area);
        if (area.sceneKind == SemanticZoneKind.Gym)
            UnlockRegionRewards(area);
        RevealConnected(area);
        if (!wasCompleted && area.sceneKind == SemanticZoneKind.Gym)
            CurriculumSessionManager.Instance?.RecordGymAttempt(area.areaId, area.areaId, passed: true);
        if (!wasCompleted)
            OnAreaCompleted?.Invoke(area);
        MarkCampaignCompleteIfFinalGym(area);
        if (saveAfter)
        {
            CapturePlayerPosition();
            Save();
        }
    }

    void MarkCampaignCompleteIfFinalGym(GrammarMapAreaState area)
    {
        if (area == null || area.sceneKind != SemanticZoneKind.Gym || data.campaignCompleted)
            return;

        IReadOnlyList<NaturalGrammarRegion> regions = NaturalGrammarProgression.Regions;
        if (regions == null || regions.Count == 0)
            return;

        NaturalGrammarRegion current = NaturalGrammarProgression.Resolve(area.grammarTopic, area.grammarTopicTier);
        NaturalGrammarRegion finalRegion = regions[regions.Count - 1];
        if (current == null || finalRegion == null ||
            !string.Equals(current.id, finalRegion.id, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        data.campaignCompleted = true;
        data.campaignCompletedAtUtc = DateTime.UtcNow.ToString("o");
        FeedbackManager feedback = FindAnyObjectByType<FeedbackManager>();
        if (feedback != null)
        {
            feedback.PlaySuccessFeedback();
            feedback.ShowLargeFeedbackPopup(
                "English World Complete!",
                "All 11 Gyms are clear. Revisit any region to turn practice into mastery.",
                feedback.colourSuccess,
                6f);
        }
        OnCampaignCompleted?.Invoke();
    }

    void SyncCompletionLists(GrammarMapAreaState area)
    {
        if (area == null)
            return;

        data.completedAreaIds ??= new List<string>();
        data.clearedGymAreaIds ??= new List<string>();
        if (data.completedAreaIds.Contains(area.areaId))
            area.objectiveCompleted = true;
        if (area.objectiveCompleted && !data.completedAreaIds.Contains(area.areaId))
            data.completedAreaIds.Add(area.areaId);
        if (area.sceneKind == SemanticZoneKind.Gym && area.objectiveCompleted && !data.clearedGymAreaIds.Contains(area.areaId))
            data.clearedGymAreaIds.Add(area.areaId);
    }

    bool AreRequiredDialogueTasksComplete(GrammarMapAreaState area)
    {
        string[] required = ResolveRequiredDialogueTaskIds(area);
        if (required == null || required.Length == 0)
            return true;

        data.completedDialogueTaskKeys ??= new List<string>();
        foreach (string taskId in required)
        {
            if (string.IsNullOrWhiteSpace(taskId))
                continue;
            if (!data.completedDialogueTaskKeys.Contains(BuildDialogueTaskKey(area.areaId, taskId)))
                return false;
        }

        return true;
    }

    bool CanCompleteAreaFromCurrentProgress(GrammarMapAreaState area)
    {
        if (area == null)
            return false;
        if (!AreRequiredDialogueTasksComplete(area))
            return false;

        NaturalGrammarRegion region = NaturalGrammarProgression.ResolveByTopicOrTier(area.grammarTopic, area.grammarTopicTier);
        bool requiresEncounter = region != null &&
            region.encounterMode != GrammarEncounterMode.None &&
            (area.sceneKind == SemanticZoneKind.Route || area.sceneKind == SemanticZoneKind.Gym);
        if (!requiresEncounter)
            return true;

        return area.encounterCompleted;
    }

    static string[] ResolveRequiredDialogueTaskIds(GrammarMapAreaState area)
    {
        if (area == null)
            return Array.Empty<string>();

        NaturalGrammarRegion region = NaturalGrammarProgression.Resolve(area.grammarTopic, area.grammarTopicTier);
        if (region == null)
            return Array.Empty<string>();

        return area.sceneKind switch
        {
            SemanticZoneKind.Town => region.npcLessonIds ?? Array.Empty<string>(),
            SemanticZoneKind.Route => region.routePracticeIds ?? Array.Empty<string>(),
            SemanticZoneKind.Gym => region.gymCheckIds ?? Array.Empty<string>(),
            _ => Array.Empty<string>(),
        };
    }

    static string BuildDialogueTaskKey(string areaId, string taskId)
    {
        return $"{(areaId ?? "").Trim()}|{(taskId ?? "").Trim()}";
    }

    void UnlockRegionRewards(GrammarMapAreaState area)
    {
        if (area == null)
            return;

        data.unlockedGrammarPatterns ??= new List<string>();
        data.unlockedVocabulary ??= new List<string>();
        data.unlockedConceptIds ??= new List<string>();
        data.masteredConceptIds ??= new List<string>();
        NaturalGrammarRegion region = NaturalGrammarProgression.Resolve(area.grammarTopic, area.grammarTopicTier);
        if (region == null)
            return;

        if (region.conceptId != GrammarConceptId.None)
        {
            AddUnique(data.unlockedConceptIds, region.conceptId.ToString());
            // A Gym clear unlocks progression, but “mastered” is a learning
            // claim and therefore requires sufficient independent evidence.
            // Learners may revisit a cleared Gym; a later clear can award the
            // badge after the evidence threshold is reached.
            if (area.sceneKind == SemanticZoneKind.Gym &&
                CurriculumSessionManager.Instance != null &&
                CurriculumSessionManager.Instance.HasDemonstratedConceptMastery(region.conceptId.ToString()))
            {
                AddUnique(data.masteredConceptIds, region.conceptId.ToString());
            }
        }

        if (region.unlockedPhrasePatterns != null)
        {
            foreach (GrammarPhrasePattern pattern in region.unlockedPhrasePatterns)
                AddUnique(data.unlockedGrammarPatterns, pattern.ToString());
        }

        if (region.vocabularyPool != null)
        {
            foreach (string word in region.vocabularyPool)
                AddUnique(data.unlockedVocabulary, SpellRegistry.NormalizeWord(word));
        }
    }
}
