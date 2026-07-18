using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class LocalDemoCurriculumProvider : ICurriculumAccessProvider
{
    private readonly List<CurriculumRunSessionRecord> runSessions = new List<CurriculumRunSessionRecord>();
    private readonly List<LetterAttemptRecord> letterAttempts = new List<LetterAttemptRecord>();
    private readonly List<WordCastRecord> wordCasts = new List<WordCastRecord>();
    private readonly List<SpokenPhraseEventRecord> spokenPhraseEvents = new List<SpokenPhraseEventRecord>();
    private readonly List<WrittenPhraseEventRecord> writtenPhraseEvents = new List<WrittenPhraseEventRecord>();
    private readonly List<GrammarBattleEventRecord> grammarBattleEvents = new List<GrammarBattleEventRecord>();
    private readonly List<BuddyConversationTurnRecord> buddyConversationTurns = new List<BuddyConversationTurnRecord>();
    private readonly List<BuddyLearningAttemptRecord> buddyLearningAttempts = new List<BuddyLearningAttemptRecord>();
    private readonly List<BuddyLearningSessionRecord> buddyLearningSessions = new List<BuddyLearningSessionRecord>();
    private readonly List<BuddyLearnerProfileRecord> buddyLearnerProfiles = new List<BuddyLearnerProfileRecord>();
    private readonly List<GymAttemptRecord> gymAttempts = new List<GymAttemptRecord>();
    private readonly List<AcceptedHandwritingTemplateRecord> acceptedTemplates = new List<AcceptedHandwritingTemplateRecord>();
    private readonly List<CountingMiniGameAttemptRecord> countingAttempts = new List<CountingMiniGameAttemptRecord>();
    private readonly List<ColorMiniGameAttemptRecord> colorAttempts = new List<ColorMiniGameAttemptRecord>();

    public IReadOnlyList<CurriculumRunSessionRecord> RunSessions => runSessions;
    public IReadOnlyList<LetterAttemptRecord> LetterAttempts => letterAttempts;
    public IReadOnlyList<WordCastRecord> WordCasts => wordCasts;
    public IReadOnlyList<SpokenPhraseEventRecord> SpokenPhraseEvents => spokenPhraseEvents;
    public IReadOnlyList<WrittenPhraseEventRecord> WrittenPhraseEvents => writtenPhraseEvents;
    public IReadOnlyList<GrammarBattleEventRecord> GrammarBattleEvents => grammarBattleEvents;
    public IReadOnlyList<BuddyConversationTurnRecord> BuddyConversationTurns => buddyConversationTurns;
    public IReadOnlyList<BuddyLearningAttemptRecord> BuddyLearningAttempts => buddyLearningAttempts;
    public IReadOnlyList<BuddyLearningSessionRecord> BuddyLearningSessions => buddyLearningSessions;
    public IReadOnlyList<BuddyLearnerProfileRecord> BuddyLearnerProfiles => buddyLearnerProfiles;
    public IReadOnlyList<GymAttemptRecord> GymAttempts => gymAttempts;
    public IReadOnlyList<AcceptedHandwritingTemplateRecord> AcceptedTemplates => acceptedTemplates;
    public IReadOnlyList<CountingMiniGameAttemptRecord> CountingAttempts => countingAttempts;
    public IReadOnlyList<ColorMiniGameAttemptRecord> ColorAttempts => colorAttempts;

    public MissionAssignment GetTodayMission(string studentId)
    {
        var mission = new MissionAssignment
        {
            missionId = $"demo-class_{DateTime.UtcNow:yyyy-MM-dd}",
            schoolId = "demo-school",
            classId = "demo-class-lkg-a",
            studentId = string.IsNullOrWhiteSpace(studentId) ? "demo-student-1" : studentId,
            date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            missionType = MissionType.Practice,
            missionDurationSeconds = 8 * 60,
            countingChestCount = 1,
            colorChestCount = 0,
            lettersForToday = new List<string> { "A", "C", "T" },
            wordsForToday = new List<string> { "ANT", "CAT", "CAN", "TAP" },
            revisionLetters = new List<string> { "A" },
            subArenas = BuildDefaultSubArenas(),
        };

        return mission;
    }

    public WorldGoalAssignment GetCurrentWorldGoal(string studentId)
    {
        return new WorldGoalAssignment
        {
            goalId = $"demo-world-goal_{DateTime.UtcNow:yyyy-MM-dd}",
            schoolId = "demo-school",
            classId = "demo-class-lkg-a",
            studentId = string.IsNullOrWhiteSpace(studentId) ? "demo-student-1" : studentId,
            weekStart = DateTime.UtcNow.Date.AddDays(-(int)DateTime.UtcNow.DayOfWeek).ToString("yyyy-MM-dd"),
            targetAreaId = "TOWN:BASICPREPOSITIONS:11",
            targetGymId = "GYM:BASICPREPOSITIONS:11",
            focusGrammarPatterns = new List<string> { GrammarPhrasePattern.FullSentence.ToString() },
            focusVocabulary = new List<string> { "IN", "ON", "UNDER", "BEHIND", "RAT", "BOX", "ROOF" },
            dueDate = DateTime.UtcNow.Date.AddDays(7).ToString("yyyy-MM-dd"),
            rewardCoins = 25,
            schoolTimeZone = "Asia/Kolkata",
            assignedAtUtc = DateTime.UtcNow.ToString("o"),
            createdByTeacherId = "demo-teacher",
        };
    }

    public void SubmitRunSession(CurriculumRunSessionRecord session)
    {
        if (session == null)
            return;

        runSessions.Add(session);
        Debug.Log($"[LocalDemoCurriculumProvider] Run session saved locally: {session.sessionId}");
    }

    public void SubmitLetterAttempt(LetterAttemptRecord attempt)
    {
        if (attempt == null)
            return;

        letterAttempts.Add(attempt);
    }

    public void SubmitWordCast(WordCastRecord castEvent)
    {
        if (castEvent == null)
            return;

        wordCasts.Add(castEvent);
    }

    public void SubmitSpokenPhraseEvent(SpokenPhraseEventRecord phraseEvent)
    {
        if (phraseEvent != null)
            spokenPhraseEvents.Add(phraseEvent);
    }

    public void SubmitWrittenPhraseEvent(WrittenPhraseEventRecord phraseEvent)
    {
        if (phraseEvent != null)
            writtenPhraseEvents.Add(phraseEvent);
    }

    public void SubmitGrammarBattleEvent(GrammarBattleEventRecord battleEvent)
    {
        if (battleEvent != null)
            grammarBattleEvents.Add(battleEvent);
    }

    public void SubmitBuddyConversationTurn(BuddyConversationTurnRecord turn)
    {
        if (turn != null)
            buddyConversationTurns.Add(turn);
    }

    public void SubmitBuddyLearningAttempt(BuddyLearningAttemptRecord attempt)
    {
        if (attempt != null)
            buddyLearningAttempts.Add(attempt);
    }

    public void SubmitBuddyLearningSession(BuddyLearningSessionRecord session)
    {
        if (session == null)
            return;

        int existingIndex = buddyLearningSessions.FindIndex(candidate => candidate != null && candidate.sessionId == session.sessionId);
        if (existingIndex >= 0)
            buddyLearningSessions[existingIndex] = session;
        else
            buddyLearningSessions.Add(session);
    }

    public void SubmitBuddyLearnerProfile(BuddyLearnerProfileRecord profile)
    {
        if (profile == null)
            return;

        int existingIndex = buddyLearnerProfiles.FindIndex(candidate => candidate != null && candidate.profileId == profile.profileId);
        if (existingIndex >= 0)
            buddyLearnerProfiles[existingIndex] = profile;
        else
            buddyLearnerProfiles.Add(profile);
    }

    public void SubmitGymAttempt(GymAttemptRecord gymAttempt)
    {
        if (gymAttempt != null)
            gymAttempts.Add(gymAttempt);
    }

    public void SubmitAcceptedTemplate(AcceptedHandwritingTemplateRecord template)
    {
        if (template == null)
            return;

        acceptedTemplates.Add(template);
        Debug.Log($"[LocalDemoCurriculumProvider] Accepted handwriting template saved locally: {template.letter} ({template.points?.Count ?? 0} points).");
    }

    public void SubmitCountingMiniGameAttempt(CountingMiniGameAttemptRecord attempt)
    {
        if (attempt == null)
            return;

        countingAttempts.Add(attempt);
        Debug.Log($"[LocalDemoCurriculumProvider] Counting attempt saved locally: {attempt.selectedCount}/{attempt.targetCount}, speech={attempt.speechProofSucceeded}.");
    }

    public void SubmitColorMiniGameAttempt(ColorMiniGameAttemptRecord attempt)
    {
        if (attempt == null)
            return;

        colorAttempts.Add(attempt);
        Debug.Log($"[LocalDemoCurriculumProvider] Color attempt saved locally: {attempt.selectedColor}/{attempt.targetColor}, speech={attempt.speechProofSucceeded}.");
    }

    public static List<SubArenaDefinition> BuildDefaultSubArenas()
    {
        return new List<SubArenaDefinition>
        {
            new SubArenaDefinition { subArenaIndex = 1, displayName = "Meadow Gate", sceneName = "Level_1_Bat", focus = "practice" },
            new SubArenaDefinition { subArenaIndex = 2, displayName = "Bubble Bridge", sceneName = "Level_1_Bat", focus = "revision" },
            new SubArenaDefinition { subArenaIndex = 3, displayName = "Star Garden", sceneName = "Level_1_Bat", focus = "mini-test" },
        };
    }

}
