using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;


public sealed partial class CurriculumSessionManager
{
    public void ConfigureBuddyLearningPreferences(
        string homeLanguage = "hi",
        string targetLanguage = "en",
        bool allowTransliteration = false,
        bool learningMemoryEnabled = true,
        string explanationStyle = "short_then_expand")
    {
        buddyHomeLanguage = NormalizeLanguageCode(homeLanguage, "hi");
        buddyTargetLanguage = NormalizeLanguageCode(targetLanguage, "en");
        buddyAllowTransliteration = allowTransliteration;
        buddyLearningMemoryEnabled = learningMemoryEnabled;
        buddyExplanationStyle = string.IsNullOrWhiteSpace(explanationStyle)
            ? "short_then_expand"
            : explanationStyle.Trim();
        TranslatorBuddyService activeBuddy = FindAnyObjectByType<TranslatorBuddyService>();
        if (activeBuddy != null)
            activeBuddy.preferredLanguage = buddyHomeLanguage;
        SaveBuddyPreferencePrefs();
        PlayerPrefs.Save();

        EnsureBuddyLearningData();
        buddyLearningData.UpdateLanguages(buddyHomeLanguage, buddyTargetLanguage);
        SubmitBuddyLearnerProfile();
    }

    public BuddyContextSnapshotRecord BuildBuddyContextSnapshot(string conceptId = "", int recentAttemptLimit = 8)
    {
        EnsureBuddyLearningData();
        GrammarWorldProgressData progress = GrammarWorldProgressService.Instance != null
            ? GrammarWorldProgressService.Instance.Data
            : null;
        GrammarMapAreaState currentArea = ResolveArea(progress?.currentAreaId ?? "");
        string resolvedConceptId = !string.IsNullOrWhiteSpace(conceptId)
            ? conceptId.Trim()
            : currentArea != null && currentArea.conceptId != GrammarConceptId.None
                ? currentArea.conceptId.ToString()
                : ResolveConceptId(progress?.currentAreaId ?? "");

        BuddyContextSnapshotRecord snapshot = buddyLearningData.BuildContextSnapshot(
            BuildBuddyLearnerProfile(),
            progress?.currentAreaId ?? "",
            currentArea != null ? currentArea.sceneKind.ToString() : "",
            resolvedConceptId,
            recentAttemptLimit);
        snapshot.schedulerRecommendation = RefreshLearnerRecommendation(false);
        return snapshot;
    }

    public LearnerScheduleRecommendation RefreshLearnerRecommendation(bool notify = true)
    {
        EnsureBuddyLearningData();
        GrammarWorldProgressData progress = GrammarWorldProgressService.Instance != null
            ? GrammarWorldProgressService.Instance.Data
            : null;
        CurrentLearnerRecommendation = LearnerScheduler.Recommend(
            buddyLearningData.State,
            NaturalGrammarProgression.Regions,
            progress,
            DateTime.UtcNow);
        if (notify)
            OnLearnerRecommendationChanged?.Invoke(CurrentLearnerRecommendation);
        return CurrentLearnerRecommendation;
    }

    public void RecordBuddySupportEvent(
        string activityType,
        GrammarConceptId conceptId,
        string contentId,
        string dialogueTaskId = "",
        string hintLevel = "",
        int hintCount = 1,
        string grimoireReference = "",
        string questionPrompt = "")
    {
        GrammarWorldProgressData progress = GrammarWorldProgressService.Instance != null
            ? GrammarWorldProgressService.Instance.Data
            : null;
        GrammarMapAreaState area = ResolveArea(progress?.currentAreaId ?? "");
        SemanticZoneKind zoneKind = area != null ? area.sceneKind : SemanticZoneKind.Town;
        string resolvedConcept = conceptId != GrammarConceptId.None
            ? conceptId.ToString()
            : ResolveConceptId(progress?.currentAreaId ?? "");

        CaptureBuddyLearningAttempt(new BuddyLearningAttemptRecord
        {
            sourceRecordType = "buddy_support",
            areaId = progress?.currentAreaId ?? "",
            zoneKind = zoneKind.ToString(),
            activityType = string.IsNullOrWhiteSpace(activityType) ? "buddy_support" : activityType,
            modality = BuddyLearningModality.Reference.ToString(),
            inputSource = "buddy_support",
            contentId = string.IsNullOrWhiteSpace(contentId) ? $"support:{resolvedConcept}" : contentId,
            dialogueTaskId = dialogueTaskId ?? "",
            questionPrompt = questionPrompt ?? "",
            grammarPattern = GrammarPhrasePattern.FullSentence.ToString(),
            conceptId = resolvedConcept,
            grimoireReference = string.IsNullOrWhiteSpace(grimoireReference)
                ? BuildGrimoireReference(resolvedConcept)
                : grimoireReference,
            countsTowardMastery = false,
            correct = false,
            hintCount = Mathf.Max(0, hintCount),
            highestHintLevel = hintLevel ?? "",
            buddyAssistMode = ResolveBuddyAssistMode(zoneKind, progress?.currentAreaId ?? ""),
        });
    }

    void EnsureBuddyLearningData()
    {
        if (buddyLearningData != null &&
            string.Equals(buddyLearningData.StudentId, ResolveActiveStudentId(), StringComparison.OrdinalIgnoreCase))
        {
            buddyLearningData.UpdateContext(
                ResolveActiveSchoolId(),
                ResolveActiveClassId(),
                CurrentMission?.missionId ?? "");
            return;
        }

        buddyLearningData = new BuddyLearningDataRecorder(
            ResolveActiveStudentId(),
            ResolveActiveSchoolId(),
            ResolveActiveClassId(),
            CurrentMission?.missionId ?? "",
            buddyHomeLanguage,
            buddyTargetLanguage);
    }

    void CaptureBuddyLearningAttempt(BuddyLearningAttemptRecord attempt)
    {
        if (attempt == null)
            return;

        EnsureBuddyLearningData();
        attempt.studentId = ResolveActiveStudentId();
        attempt.schoolId = ResolveActiveSchoolId();
        attempt.classId = ResolveActiveClassId();
        attempt.missionId = CurrentMission?.missionId ?? "";
        attempt.goalId = CurrentWorldGoal?.goalId ?? "";
        BuddyLearningAttemptRecord recorded = buddyLearningData.RecordAttempt(attempt);
        RefreshLearnerRecommendation();

        if (ShouldSubmitBuddyLearningData())
        {
            EnsureProvider();
            provider?.SubmitBuddyLearningAttempt(recorded);
            if (buddyLearningData.State.sourceEventCount % 10 == 0)
                provider?.SubmitBuddyLearningSession(buddyLearningData.CheckpointSession(false));
        }
    }

    void CheckpointBuddyLearningSession(bool ended)
    {
        if (buddyLearningData == null)
            return;

        BuddyLearningSessionRecord session = buddyLearningData.CheckpointSession(ended);
        if (!ShouldSubmitBuddyLearningData())
            return;

        EnsureProvider();
        provider?.SubmitBuddyLearningSession(session);
    }

    void SubmitBuddyLearnerProfile()
    {
        if (!ShouldSubmitBuddyLearningData())
            return;

        EnsureProvider();
        provider?.SubmitBuddyLearnerProfile(BuildBuddyLearnerProfile());
    }

    BuddyLearnerProfileRecord BuildBuddyLearnerProfile()
    {
        return new BuddyLearnerProfileRecord
        {
            profileId = "current",
            studentId = ResolveActiveStudentId(),
            schoolId = ResolveActiveSchoolId(),
            classId = ResolveActiveClassId(),
            homeLanguage = NormalizeLanguageCode(buddyHomeLanguage, "hi"),
            targetLanguage = NormalizeLanguageCode(buddyTargetLanguage, "en"),
            allowTransliteration = buddyAllowTransliteration,
            learningMemoryEnabled = buddyLearningMemoryEnabled,
            explanationStyle = string.IsNullOrWhiteSpace(buddyExplanationStyle) ? "short_then_expand" : buddyExplanationStyle,
            updatedAtUtc = DateTime.UtcNow.ToString("o"),
        };
    }

    bool ShouldSubmitBuddyLearningData()
    {
        return providerMode == CurriculumProviderMode.LocalDemo || HasStudentSession;
    }

    string ResolveActiveStudentId()
    {
        return !string.IsNullOrWhiteSpace(CurrentMission?.studentId) ? CurrentMission.studentId : activeStudentId;
    }

    string ResolveActiveSchoolId()
    {
        return !string.IsNullOrWhiteSpace(CurrentMission?.schoolId) ? CurrentMission.schoolId : activeSchoolId;
    }

    string ResolveActiveClassId()
    {
        return !string.IsNullOrWhiteSpace(CurrentMission?.classId) ? CurrentMission.classId : activeClassId;
    }

    void SaveBuddyPreferencePrefs()
    {
        PlayerPrefs.SetString("TheScript.BuddyHomeLanguage", NormalizeLanguageCode(buddyHomeLanguage, "hi"));
        PlayerPrefs.SetString("TheScript.BuddyTargetLanguage", NormalizeLanguageCode(buddyTargetLanguage, "en"));
        PlayerPrefs.SetInt("TheScript.BuddyAllowTransliteration", buddyAllowTransliteration ? 1 : 0);
        PlayerPrefs.SetInt("TheScript.BuddyLearningMemoryEnabled", buddyLearningMemoryEnabled ? 1 : 0);
        PlayerPrefs.SetString("TheScript.BuddyExplanationStyle", buddyExplanationStyle ?? "short_then_expand");
    }

    static string NormalizeLanguageCode(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        string normalized = value.Trim().ToLowerInvariant();
        int separator = normalized.IndexOfAny(new[] { '-', '_' });
        return separator > 0 ? normalized.Substring(0, separator) : normalized;
    }

    static string BuildGrimoireReference(string conceptId)
    {
        return string.IsNullOrWhiteSpace(conceptId) ? "" : $"grimoire:{conceptId.Trim()}";
    }
}
