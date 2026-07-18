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
    public void MarkAreaExplored(string areaId)
    {
        EnsureLoaded();
        GrammarMapAreaState area = EnsureArea(areaId);
        area.visible = true;
        area.explored = true;
        data.currentAreaId = area.areaId;
        SyncCompletionLists(area);
        if (area.objectiveCompleted)
            RevealConnected(area);
        CapturePlayerPosition();
        Save();
    }

    public void MarkCurrentAreaObjectiveCompleted()
    {
        EnsureLoaded();
        GrammarMapAreaState area = !string.IsNullOrWhiteSpace(data.currentAreaId)
            ? EnsureArea(data.currentAreaId)
            : null;
        if (area == null)
            return;

        if (CanCompleteAreaFromCurrentProgress(area))
            CompleteArea(area);
        else
            Save();
    }

    public bool RegisterCurrentAreaDialogueTaskCompleted(
        LocalizedDialogueLine line,
        SemanticZoneKind kind,
        string grammarTopic,
        int grammarTopicTier)
    {
        EnsureLoaded();
        GrammarMapAreaState area = !string.IsNullOrWhiteSpace(data.currentAreaId)
            ? EnsureArea(data.currentAreaId)
            : EnsureArea(BuildAreaId(kind, grammarTopic, grammarTopicTier));
        if (area == null)
            return false;

        string taskId = line != null ? line.dialogueTaskId : "";
        if (string.IsNullOrWhiteSpace(taskId))
        {
            // Some authored lines are flavour or guidance rather than a task.
            // They must never bypass the area's required-task/encounter gate.
            bool tasksCompleteWithoutThisLine = AreRequiredDialogueTasksComplete(area);
            if (CanCompleteAreaFromCurrentProgress(area))
                CompleteArea(area);
            else
            {
                CapturePlayerPosition();
                Save();
            }
            return tasksCompleteWithoutThisLine;
        }

        data.completedDialogueTaskKeys ??= new List<string>();
        AddUnique(data.completedDialogueTaskKeys, BuildDialogueTaskKey(area.areaId, taskId));
        bool tasksComplete = AreRequiredDialogueTasksComplete(area);

        if (CanCompleteAreaFromCurrentProgress(area))
            CompleteArea(area);
        else
        {
            CapturePlayerPosition();
            Save();
        }
        return tasksComplete;
    }

    public void MarkAreaObjectiveCompleted(SemanticZoneKind kind, string grammarTopic, int grammarTopicTier)
    {
        EnsureLoaded();
        GrammarMapAreaState area = EnsureArea(BuildAreaId(kind, grammarTopic, grammarTopicTier));
        if (CanCompleteAreaFromCurrentProgress(area))
            CompleteArea(area);
        else
            Save();
    }

    public bool IsGrammarPhrasePatternUnlocked(GrammarPhrasePattern pattern)
    {
        EnsureLoaded();
        if (pattern == GrammarPhrasePattern.LetterOnly || pattern == GrammarPhrasePattern.FullSentence)
            return true;

        data.unlockedGrammarPatterns ??= new List<string>();
        if (ContainsPattern(data.unlockedGrammarPatterns, pattern))
            return true;

        GrammarMapAreaState current = !string.IsNullOrWhiteSpace(data.currentAreaId)
            ? EnsureArea(data.currentAreaId)
            : null;
        if (current == null)
            return data.unlockedGrammarPatterns.Count == 0;

        if (current.sceneKind == SemanticZoneKind.Town)
            return false;

        NaturalGrammarRegion region = NaturalGrammarProgression.ResolveByTopicOrTier(current.grammarTopic, current.grammarTopicTier);
        return RegionUnlocksPattern(region, pattern);
    }

    public bool IsVocabularyUnlocked(string word)
    {
        EnsureLoaded();
        string normalized = CreaturePhraseUtility.NormalizeToken(word);
        if (string.IsNullOrEmpty(normalized))
            return true;

        data.unlockedVocabulary ??= new List<string>();
        if (ContainsVocabulary(data.unlockedVocabulary, normalized))
            return true;

        GrammarMapAreaState current = !string.IsNullOrWhiteSpace(data.currentAreaId)
            ? EnsureArea(data.currentAreaId)
            : null;
        if (current == null)
            return data.unlockedVocabulary.Count == 0;

        if (current.sceneKind == SemanticZoneKind.Town)
            return false;

        NaturalGrammarRegion region = NaturalGrammarProgression.ResolveByTopicOrTier(current.grammarTopic, current.grammarTopicTier);
        return RegionUnlocksVocabulary(region, normalized);
    }
}
