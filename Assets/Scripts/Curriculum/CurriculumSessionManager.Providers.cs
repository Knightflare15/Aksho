using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;


public sealed partial class CurriculumSessionManager
{
    public void SubmitRunSummary(RunSummary summary)
    {
        if (!ShouldSubmitTeacherAnalytics || summary == null)
            return;

        provider?.SubmitRunSession(new CurriculumRunSessionRecord
        {
            sessionId = Guid.NewGuid().ToString("N"),
            studentId = CurrentMission.studentId,
            classId = CurrentMission.classId,
            schoolId = CurrentMission.schoolId,
            missionId = CurrentMission.missionId,
            configuredDurationSeconds = MissionDurationSeconds,
            actualDurationSeconds = Mathf.RoundToInt(summary.elapsedSeconds),
            subarenasCleared = summary.subarenasCleared,
            fullLoopsCleared = summary.fullLoopsCleared,
            lettersPracticed = new List<string>(lettersPracticed),
            wordsPracticed = new List<string>(wordsPracticed),
            grammarPatternsPracticed = new List<string>(grammarPatternsPracticed),
            masteryTagsPracticed = new List<string>(masteryTagsPracticed),
            vocabularyTokens = new List<string>(vocabularyTokensPracticed),
            acceptedSpokenVocabulary = new List<string>(acceptedSpokenVocabulary),
            acceptedWrittenVocabulary = new List<string>(acceptedWrittenVocabulary),
            acceptedBattleVocabulary = new List<string>(acceptedBattleVocabulary),
            spokenPhraseCount = Mathf.Max(0, spokenPhraseEventsThisRun),
            writtenPhraseCount = Mathf.Max(0, writtenPhraseEventsThisRun),
            grammarBattleCount = Mathf.Max(0, grammarBattleEventsThisRun),
            grammarErrors = Mathf.Max(0, grammarErrorsThisRun),
            pronunciationRetries = Mathf.Max(0, pronunciationRetriesThisRun),
            averageConfidence = AverageConfidence,
            averageAttemptsPerLetter = AverageAttemptsPerLetter,
            creaturesCleared = summary.enemiesDefeated,
            specialWordMatches = SpecialWordMatches,
            completed = summary.reason == RunEndReason.TimeUp || summary.reason == RunEndReason.Victory,
            startedAtUtc = DateTime.UtcNow.AddSeconds(-Mathf.Max(0f, summary.elapsedSeconds)).ToString("o"),
            endedAtUtc = DateTime.UtcNow.ToString("o"),
        });
    }

    void EnsureProvider()
    {
        if (provider != null)
            return;

        providerFactory ??= new DefaultCurriculumProviderFactory();
        provider = providerFactory.Create(new CurriculumProviderContext(
            providerMode,
            firebaseProjectId,
            firebaseStorageBucket,
            firebaseFunctionsBaseUrl,
            EffectiveWavLmApiBaseUrl,
            activeSchoolId,
            activeClassId,
            studentIdToken));

        if (provider == null)
        {
            Debug.LogWarning("[CurriculumSessionManager] The configured provider factory returned null; local demo persistence will be used.");
            provider = new LocalDemoCurriculumProvider();
        }
    }

    public void ConfigureProviderFactory(ICurriculumProviderFactory factory)
    {
        providerFactory = factory ?? new DefaultCurriculumProviderFactory();
        provider = null;
    }

    public void ConfigurePronunciationEndpointResolver(IPronunciationAnalysisEndpointResolver resolver)
    {
        pronunciationEndpointResolver = resolver ?? new PronunciationAnalysisEndpointResolver();
        provider = null;
    }

    void BeginLearnerStateHydration()
    {
        if (!HasStudentSession || learnerStateHydrationPending)
            return;

        EnsureProvider();
        if (!(provider is IRemoteLearnerStateProvider remoteProvider))
            return;

        int generation = studentSessionGeneration;
        string studentId = activeStudentId;
        learnerStateHydrationPending = true;
        remoteProvider.GetBuddyLearnerStateAsync(studentId, remoteState =>
        {
            if (generation != studentSessionGeneration ||
                !string.Equals(studentId, activeStudentId, StringComparison.Ordinal))
                return;

            learnerStateHydrationPending = false;
            if (remoteState == null)
                return;

            EnsureBuddyLearningData();
            buddyLearningData.MergeRemoteState(remoteState);
            RefreshLearnerRecommendation();
            OnLearnerStateHydrated?.Invoke(buddyLearningData.State);
            Debug.Log($"[CurriculumSessionManager] Hydrated learner state for '{studentId}' with {buddyLearningData.State.concepts.Count} concept records.");
        });
    }

    public string ResolveWavLmApiBaseUrl()
    {
        pronunciationEndpointResolver ??= new PronunciationAnalysisEndpointResolver();
        return pronunciationEndpointResolver.Resolve(new PronunciationAnalysisEndpointOptions(
            wavLmEndpointMode,
            string.IsNullOrWhiteSpace(localWavLmApiBaseUrl)
                ? DefaultLocalWavLmApiBaseUrl
                : localWavLmApiBaseUrl,
            cloudWavLmApiBaseUrl,
            wavLmApiBaseUrl,
            ShouldPreferLocalWavLmEndpoint()));
    }

    static bool ShouldPreferLocalWavLmEndpoint()
    {
#if UNITY_EDITOR
        return true;
#else
        return Debug.isDebugBuild;
#endif
    }

    static List<string> ExtractVocabularyTokens(string phrase)
    {
        var tokens = new List<string>();
        foreach (string token in CreaturePhraseUtility.Tokenize(phrase))
            AddUniqueToken(tokens, token);
        return tokens;
    }

    void TrackPracticedVocabulary(IEnumerable<string> tokens)
    {
        if (tokens == null)
            return;

        foreach (string token in tokens)
        {
            AddUniqueToken(wordsPracticed, token);
            AddUniqueToken(vocabularyTokensPracticed, token);
        }
    }

    static void TrackAcceptedVocabulary(ICollection<string> target, IEnumerable<string> tokens)
    {
        if (target == null || tokens == null)
            return;

        foreach (string token in tokens)
            AddUniqueToken(target, token);
    }

    void TrackGrammarPattern(GrammarPhrasePattern pattern)
    {
        string value = pattern.ToString();
        if (!string.IsNullOrWhiteSpace(value))
            grammarPatternsPracticed.Add(value);
    }

    void TrackMasteryTags(IEnumerable<string> tags)
    {
        if (tags == null)
            return;

        foreach (string tag in tags)
        {
            string normalized = string.IsNullOrWhiteSpace(tag) ? "" : tag.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(normalized))
                masteryTagsPracticed.Add(normalized);
        }
    }

    static void AddUniqueToken(ICollection<string> target, string value)
    {
        if (target == null)
            return;

        string normalized = CreaturePhraseUtility.NormalizeToken(value);
        if (!string.IsNullOrWhiteSpace(normalized) && !target.Contains(normalized))
            target.Add(normalized);
    }

    void ResetRuntimeProgress()
    {
        missionStartedAt = 0f;
        CurrentSubArenaIndex = 1;
        SubArenasCleared = 0;
        FullLoopsCleared = 0;
        spokenPhraseEventsThisRun = 0;
        writtenPhraseEventsThisRun = 0;
        grammarBattleEventsThisRun = 0;
        grammarErrorsThisRun = 0;
        pronunciationRetriesThisRun = 0;
        eligibleServerPronunciationReviewsThisRun = 0;
        serverPronunciationReviewsThisRun = 0;
        lettersPracticed.Clear();
        wordsPracticed.Clear();
        grammarPatternsPracticed.Clear();
        masteryTagsPracticed.Clear();
        vocabularyTokensPracticed.Clear();
        acceptedSpokenVocabulary.Clear();
        acceptedWrittenVocabulary.Clear();
        acceptedBattleVocabulary.Clear();
    }

    void RebuildAllowedSets()
    {
        allowedLetters.Clear();
        allowedWords.Clear();

        if (CurrentMission == null)
            return;

        AddLetters(CurrentMission.lettersForToday);
        AddLetters(CurrentMission.revisionLetters);

        AddWords(CurrentMission.wordsForToday);

        foreach (string word in new List<string>(allowedWords))
        {
            if (string.IsNullOrEmpty(word))
                continue;
            allowedLetters.Add(word[0].ToString());
        }
    }

    void AddLetters(IEnumerable<string> letters)
    {
        if (letters == null)
            return;

        foreach (string letter in letters)
        {
            string normalized = SpellRegistry.NormalizeWord(letter);
            if (!string.IsNullOrEmpty(normalized))
                allowedLetters.Add(normalized[0].ToString());
        }
    }

    void AddWords(IEnumerable<string> words)
    {
        if (words == null)
            return;

        foreach (string word in words)
        {
            string normalized = SpellRegistry.NormalizeWord(word);
            if (!string.IsNullOrEmpty(normalized))
                allowedWords.Add(normalized);
        }
    }

    static void NormalizeMission(MissionAssignment mission)
    {
        if (mission == null)
            return;

        mission.missionDurationSeconds = Mathf.Clamp(mission.missionDurationSeconds, 180, 20 * 60);
        mission.countingChestCount = Mathf.Clamp(mission.countingChestCount, 0, 2);
        mission.colorChestCount = Mathf.Clamp(mission.colorChestCount, 0, 2);
        if (mission.subArenas == null || mission.subArenas.Count == 0)
            mission.subArenas = LocalDemoCurriculumProvider.BuildDefaultSubArenas();
    }

    static List<string> NormalizeUniqueLetters(IEnumerable<string> letters)
    {
        var result = new List<string>();
        if (letters == null)
            return result;

        foreach (string letter in letters)
        {
            string normalized = SpellRegistry.NormalizeWord(letter);
            if (string.IsNullOrEmpty(normalized))
                continue;

            string value = normalized[0].ToString();
            if (!result.Contains(value))
                result.Add(value);
        }

        return result;
    }

    static List<string> NormalizeUniqueWords(IEnumerable<string> words)
    {
        var result = new List<string>();
        if (words == null)
            return result;

        foreach (string word in words)
        {
            string normalized = SpellRegistry.NormalizeWord(word);
            if (!string.IsNullOrEmpty(normalized) && !result.Contains(normalized))
                result.Add(normalized);
        }

        return result;
    }

    static List<SubArenaDefinition> BuildSandboxSubArenas(IEnumerable<string> sceneNames)
    {
        var result = new List<SubArenaDefinition>();
        if (sceneNames != null)
        {
            foreach (string sceneName in sceneNames)
            {
                if (string.IsNullOrWhiteSpace(sceneName))
                    continue;

                result.Add(new SubArenaDefinition
                {
                    subArenaIndex = Mathf.Clamp(result.Count + 1, 1, 3),
                    displayName = $"Sandbox {result.Count + 1}",
                    sceneName = sceneName.Trim(),
                    focus = "dev sandbox",
                });

                if (result.Count >= 3)
                    break;
            }
        }

        return result.Count > 0 ? result : LocalDemoCurriculumProvider.BuildDefaultSubArenas();
    }
}
