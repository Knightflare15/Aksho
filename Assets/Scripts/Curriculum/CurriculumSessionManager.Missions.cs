using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;


public sealed partial class CurriculumSessionManager
{
    public WorldGoalAssignment LoadOptionalWorldGoal()
    {
        if (!HasStudentSession)
        {
            CurrentWorldGoal = null;
            return null;
        }

        EnsureProvider();
        if (provider is IAsyncCurriculumAccessProvider asyncProvider)
        {
            EnsureWorldPlayMissionFallback();
            BeginAsyncCurriculumRefresh(asyncProvider);
            WorldGoalTracker.EnsureExists().Refresh();
            return CurrentWorldGoal;
        }

        if (CurrentMission == null)
        {
            CurrentMission = provider.GetTodayMission(activeStudentId) ?? new MissionAssignment
            {
                missionId = $"world_{DateTime.UtcNow:yyyy-MM-dd}",
                schoolId = activeSchoolId,
                classId = activeClassId,
                studentId = activeStudentId,
                date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                missionType = MissionType.Practice,
            };
            NormalizeMission(CurrentMission);
            RebuildAllowedSets();
        }
        ApplyWorldGoal(provider.GetCurrentWorldGoal(activeStudentId));
        WorldGoalTracker.EnsureExists().Refresh();
        return CurrentWorldGoal;
    }

    void EnsureWorldPlayMissionFallback()
    {
        if (CurrentMission != null)
            return;
        CurrentMission = new MissionAssignment
        {
            missionId = $"world_{DateTime.UtcNow:yyyy-MM-dd}",
            schoolId = activeSchoolId,
            classId = activeClassId,
            studentId = activeStudentId,
            date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            missionType = MissionType.Practice,
        };
        NormalizeMission(CurrentMission);
        RebuildAllowedSets();
    }

    void BeginAsyncCurriculumRefresh(IAsyncCurriculumAccessProvider asyncProvider)
    {
        int generation = studentSessionGeneration;
        string studentId = activeStudentId;
        if (!missionRequestPending)
        {
            missionRequestPending = true;
            asyncProvider.GetTodayMissionAsync(studentId, mission =>
            {
                if (generation != studentSessionGeneration || !string.Equals(studentId, activeStudentId, StringComparison.Ordinal))
                    return;
                missionRequestPending = false;
                if (mission == null)
                    return;
                CurrentMission = mission;
                NormalizeMission(CurrentMission);
                RebuildAllowedSets();
                OnMissionLoaded?.Invoke(CurrentMission);
            });
        }

        if (!worldGoalRequestPending)
        {
            worldGoalRequestPending = true;
            asyncProvider.GetCurrentWorldGoalAsync(studentId, goal =>
            {
                if (generation != studentSessionGeneration || !string.Equals(studentId, activeStudentId, StringComparison.Ordinal))
                    return;
                worldGoalRequestPending = false;
                ApplyWorldGoal(goal);
                WorldGoalTracker.EnsureExists().Refresh();
            });
        }
    }

    void ApplyWorldGoal(WorldGoalAssignment goal)
    {
        CurrentWorldGoal = goal;
        if (CurrentWorldGoal == null)
            return;
        CurrentWorldGoal.targetAreaId = GrammarWorldProgressService.CanonicalizeAreaId(CurrentWorldGoal.targetAreaId);
        CurrentWorldGoal.targetGymId = GrammarWorldProgressService.CanonicalizeAreaId(CurrentWorldGoal.targetGymId);
        CurrentWorldGoal.rewardCoins = Mathf.Max(0, CurrentWorldGoal.rewardCoins);
        if (string.IsNullOrWhiteSpace(CurrentWorldGoal.schoolTimeZone))
            CurrentWorldGoal.schoolTimeZone = "Asia/Kolkata";
    }

    public MissionAssignment LoadWorldGoalPractice()
    {
        if (devSandboxMissionConfigured)
            provider = null;
        devSandboxMissionConfigured = false;
        EnsureProvider();
        if (provider is IAsyncCurriculumAccessProvider)
        {
            CurrentMission = null;
            EnsureWorldPlayMissionFallback();
            LoadOptionalWorldGoal();
        }
        else
        {
            CurrentMission = provider.GetTodayMission(activeStudentId);
            LoadOptionalWorldGoal();
        }
        NormalizeMission(CurrentMission);
        RebuildAllowedSets();
        ResetRuntimeProgress();
        OnMissionLoaded?.Invoke(CurrentMission);
        return CurrentMission;
    }

    [Obsolete("Use LoadWorldGoalPractice for the weekly RPG goal flow.")]
    public MissionAssignment LoadTodayMission()
    {
        return LoadWorldGoalPractice();
    }

    public MissionAssignment ConfigureDevSandboxMission(
        IEnumerable<string> letters,
        IEnumerable<string> words,
        IEnumerable<string> sceneNames = null)
    {
        var sandboxLetters = NormalizeUniqueLetters(letters);
        var sandboxWords = NormalizeUniqueWords(words);

        foreach (string word in sandboxWords)
        {
            if (!string.IsNullOrEmpty(word))
                sandboxLetters.Add(word[0].ToString());
        }

        bool useLoggedInStudent = HasStudentSession;
        CurrentMission = new MissionAssignment
        {
            missionId = $"dev_sandbox_{DateTime.UtcNow:yyyyMMddHHmmss}",
            schoolId = useLoggedInStudent ? activeSchoolId : "dev-sandbox",
            classId = useLoggedInStudent ? activeClassId : "dev-sandbox",
            studentId = useLoggedInStudent ? activeStudentId : "dev-sandbox",
            date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            missionType = MissionType.Practice,
            missionDurationSeconds = 20 * 60,
            countingChestCount = 1,
            colorChestCount = 0,
            lettersForToday = sandboxLetters,
            wordsForToday = sandboxWords,
            revisionLetters = new List<string>(),
            subArenas = BuildSandboxSubArenas(sceneNames),
        };
        CurrentWorldGoal = new WorldGoalAssignment
        {
            goalId = $"dev_world_goal_{DateTime.UtcNow:yyyyMMddHHmmss}",
            schoolId = CurrentMission.schoolId,
            classId = CurrentMission.classId,
            studentId = CurrentMission.studentId,
            weekStart = DateTime.UtcNow.Date.AddDays(-(int)DateTime.UtcNow.DayOfWeek).ToString("yyyy-MM-dd"),
            targetAreaId = GrammarWorldProgressService.BuildAreaId(SemanticZoneKind.Town, "Basic Prepositions", 11),
            targetGymId = GrammarWorldProgressService.BuildAreaId(SemanticZoneKind.Gym, "Basic Prepositions", 11),
            focusGrammarPatterns = new List<string> { GrammarPhrasePattern.FullSentence.ToString() },
            focusVocabulary = new List<string> { "IN", "ON", "UNDER", "BEHIND", "RAT", "BOX", "ROOF" },
            dueDate = DateTime.UtcNow.Date.AddDays(7).ToString("yyyy-MM-dd"),
            rewardCoins = 25,
            schoolTimeZone = "Asia/Kolkata",
            assignedAtUtc = DateTime.UtcNow.ToString("o"),
            createdByTeacherId = "dev-sandbox",
        };

        schoolModeEnabled = true;
        provider = null;
        if (useLoggedInStudent)
            EnsureProvider();
        else
            provider = new LocalDemoCurriculumProvider();
        devSandboxMissionConfigured = true;
        NormalizeMission(CurrentMission);
        RebuildAllowedSets();
        ResetRuntimeProgress();
        OnMissionLoaded?.Invoke(CurrentMission);
        return CurrentMission;
    }

    public void ClearDevSandboxMission()
    {
        if (!devSandboxMissionConfigured)
            return;

        devSandboxMissionConfigured = false;
        provider = null;
        CurrentMission = null;
        RebuildAllowedSets();
        ResetRuntimeProgress();
    }
}
