using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

internal sealed class BuddyLearningDataRecorder
{
    const int RecentAttemptLimit = 20;

    readonly string dataDirectory;
    readonly string attemptHistoryPath;
    readonly string learnerStatePath;
    readonly string sessionsDirectory;
    readonly Dictionary<string, int> attemptCountsByGroup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    BuddyLearnerStateRecord state;
    BuddyLearningSessionRecord session;

    public string StudentId { get; }
    public string SessionId => session.sessionId;
    public BuddyLearnerStateRecord State => state;

    public BuddyLearningDataRecorder(
        string studentId,
        string schoolId,
        string classId,
        string missionId,
        string homeLanguage,
        string targetLanguage)
    {
        StudentId = string.IsNullOrWhiteSpace(studentId) ? "local-player" : studentId.Trim();
        string safeStudentId = SanitizePathSegment(StudentId);
        dataDirectory = Path.Combine(Application.persistentDataPath, "BuddyLearningData", safeStudentId);
        attemptHistoryPath = Path.Combine(dataDirectory, "attempts.jsonl");
        learnerStatePath = Path.Combine(dataDirectory, "learner_state.json");
        sessionsDirectory = Path.Combine(dataDirectory, "sessions");
        EnsureDirectories();

        state = LoadLearnerState();
        state.studentId = StudentId;
        state.homeLanguage = NormalizeLanguage(homeLanguage, "hi");
        state.targetLanguage = NormalizeLanguage(targetLanguage, "en");
        state.concepts ??= new List<BuddyConceptLearningStateRecord>();
        state.strengthConceptIds ??= new List<string>();
        state.needConceptIds ??= new List<string>();
        state.recurringErrorTags ??= new List<string>();
        state.recentAttempts ??= new List<BuddyLearningAttemptRecord>();

        string now = DateTime.UtcNow.ToString("o");
        session = new BuddyLearningSessionRecord
        {
            sessionId = Guid.NewGuid().ToString("N"),
            studentId = StudentId,
            schoolId = schoolId ?? "",
            classId = classId ?? "",
            missionId = missionId ?? "",
            homeLanguage = state.homeLanguage,
            targetLanguage = state.targetLanguage,
            supportBand = state.supportBand,
            recommendedEnglishRatio = state.recommendedEnglishRatio,
            startedAtUtc = now,
            lastActivityAtUtc = now,
        };
        PersistState();
        PersistSession();
    }

    public void UpdateLanguages(string homeLanguage, string targetLanguage)
    {
        state.homeLanguage = NormalizeLanguage(homeLanguage, "hi");
        state.targetLanguage = NormalizeLanguage(targetLanguage, "en");
        session.homeLanguage = state.homeLanguage;
        session.targetLanguage = state.targetLanguage;
        state.updatedAtUtc = DateTime.UtcNow.ToString("o");
        PersistState();
        PersistSession();
    }

    public void UpdateContext(string schoolId, string classId, string missionId)
    {
        bool changed = false;
        if (!string.IsNullOrWhiteSpace(schoolId) && session.schoolId != schoolId)
        {
            session.schoolId = schoolId;
            changed = true;
        }
        if (!string.IsNullOrWhiteSpace(classId) && session.classId != classId)
        {
            session.classId = classId;
            changed = true;
        }
        if (!string.IsNullOrWhiteSpace(missionId) && session.missionId != missionId)
        {
            session.missionId = missionId;
            changed = true;
        }
        if (changed)
            PersistSession();
    }

    public void MergeRemoteState(BuddyLearnerStateRecord remoteState)
    {
        state = LearnerStateMerger.Merge(state, remoteState, StudentId);
        RebuildLearnerSummary();
        session.supportBand = state.supportBand;
        session.recommendedEnglishRatio = state.recommendedEnglishRatio;
        session.lastActivityAtUtc = DateTime.UtcNow.ToString("o");
        PersistState();
        PersistSession();
    }

    public BuddyLearningAttemptRecord RecordAttempt(BuddyLearningAttemptRecord attempt)
    {
        string now = DateTime.UtcNow.ToString("o");
        attempt.schemaVersion = 1;
        attempt.eventId = string.IsNullOrWhiteSpace(attempt.eventId) ? Guid.NewGuid().ToString("N") : attempt.eventId;
        attempt.sessionId = session.sessionId;
        attempt.studentId = StudentId;
        attempt.schoolId = string.IsNullOrWhiteSpace(attempt.schoolId) ? session.schoolId : attempt.schoolId;
        attempt.classId = string.IsNullOrWhiteSpace(attempt.classId) ? session.classId : attempt.classId;
        attempt.missionId = string.IsNullOrWhiteSpace(attempt.missionId) ? session.missionId : attempt.missionId;
        attempt.activityType = string.IsNullOrWhiteSpace(attempt.activityType) ? "learning_attempt" : attempt.activityType.Trim();
        attempt.modality = string.IsNullOrWhiteSpace(attempt.modality) ? BuddyLearningModality.Unknown.ToString() : attempt.modality.Trim();
        attempt.inputSource = string.IsNullOrWhiteSpace(attempt.inputSource) ? attempt.activityType : attempt.inputSource.Trim();
        attempt.contentId = string.IsNullOrWhiteSpace(attempt.contentId) ? $"unscoped:{attempt.activityType}" : attempt.contentId.Trim();
        attempt.conceptId = string.IsNullOrWhiteSpace(attempt.conceptId) ? "Unscoped" : attempt.conceptId.Trim();
        attempt.grimoireReference = string.IsNullOrWhiteSpace(attempt.grimoireReference)
            ? $"grimoire:{attempt.conceptId}"
            : attempt.grimoireReference.Trim();
        attempt.questionPrompt = CapText(attempt.questionPrompt);
        attempt.expectedResponse = CapText(attempt.expectedResponse);
        attempt.submittedResponse = CapText(attempt.submittedResponse);
        attempt.normalizedResponse = CapText(string.IsNullOrWhiteSpace(attempt.normalizedResponse)
            ? NormalizeResponse(attempt.submittedResponse)
            : attempt.normalizedResponse);
        attempt.correctedResponse = CapText(attempt.correctedResponse);
        attempt.errorTags = NormalizeTags(attempt.errorTags);
        attempt.vocabularyTokens = NormalizeTags(attempt.vocabularyTokens);
        attempt.masteryTags = NormalizeTags(attempt.masteryTags);
        attempt.attemptGroupId = string.IsNullOrWhiteSpace(attempt.attemptGroupId)
            ? BuildAttemptGroupId(attempt)
            : attempt.attemptGroupId.Trim();
        attempt.attemptNumber = NextAttemptNumber(attempt.attemptGroupId);
        attempt.hintCount = Mathf.Max(0, attempt.hintCount);
        if (!attempt.correct && attempt.hintCount == 0 && !string.IsNullOrWhiteSpace(attempt.highestHintLevel))
            attempt.hintCount = 1;
        attempt.completedIndependently = attempt.correct && attempt.attemptNumber == 1 && attempt.hintCount == 0;
        attempt.responseSeconds = Mathf.Max(0f, attempt.responseSeconds);
        attempt.confidenceScore = Mathf.Clamp01(attempt.confidenceScore);
        attempt.recognitionConfidence = Mathf.Clamp01(attempt.recognitionConfidence);
        attempt.pronunciationScore = Mathf.Clamp01(attempt.pronunciationScore);
        attempt.createdAtUtc = string.IsNullOrWhiteSpace(attempt.createdAtUtc) ? now : attempt.createdAtUtc;

        if (attempt.countsTowardMastery && !attempt.correct && attempt.errorTags.Count == 0)
            attempt.errorTags.Add("incorrect");

        state.sourceEventCount++;
        state.totalHints += attempt.hintCount;
        state.lastEventAtUtc = attempt.createdAtUtc;
        state.updatedAtUtc = now;
        AddRecentAttempt(attempt);

        if (attempt.countsTowardMastery)
        {
            UpdateConceptState(attempt);
            session.attemptCount++;
            if (attempt.correct)
            {
                state.correctAttemptCount++;
                session.correctAttemptCount++;
            }
            if (attempt.completedIndependently)
            {
                state.independentCorrectAttemptCount++;
                session.independentCorrectAttemptCount++;
            }
            session.totalHints += attempt.hintCount;
            AddUnique(session.conceptIds, attempt.conceptId);
            foreach (string errorTag in attempt.errorTags)
                AddUnique(session.recurringErrorTags, errorTag);
        }
        else if (attempt.errorTags.Count > 0)
        {
            UpdateConceptDiagnostics(attempt);
            foreach (string errorTag in attempt.errorTags)
                AddUnique(session.recurringErrorTags, errorTag);
        }

        RebuildLearnerSummary();
        session.supportBand = state.supportBand;
        session.recommendedEnglishRatio = state.recommendedEnglishRatio;
        session.lastActivityAtUtc = now;
        AppendAttempt(attempt);
        PersistState();
        PersistSession();
        return attempt;
    }

    public BuddyLearningSessionRecord CheckpointSession(bool ended)
    {
        session.lastActivityAtUtc = DateTime.UtcNow.ToString("o");
        if (ended)
            session.endedAtUtc = session.lastActivityAtUtc;
        PersistSession();
        return session;
    }

    public BuddyContextSnapshotRecord BuildContextSnapshot(
        BuddyLearnerProfileRecord profile,
        string currentAreaId,
        string currentZoneKind,
        string currentConceptId,
        int recentAttemptLimit)
    {
        var relevant = new List<BuddyLearningAttemptRecord>();
        int limit = Mathf.Clamp(recentAttemptLimit, 1, RecentAttemptLimit);
        for (int i = state.recentAttempts.Count - 1; i >= 0 && relevant.Count < limit; i--)
        {
            BuddyLearningAttemptRecord attempt = state.recentAttempts[i];
            if (attempt == null)
                continue;
            if (!string.IsNullOrWhiteSpace(currentConceptId) &&
                !string.Equals(attempt.conceptId, currentConceptId, StringComparison.OrdinalIgnoreCase))
                continue;
            relevant.Insert(0, attempt);
        }

        if (relevant.Count == 0)
        {
            for (int i = Mathf.Max(0, state.recentAttempts.Count - limit); i < state.recentAttempts.Count; i++)
                relevant.Add(state.recentAttempts[i]);
        }

        return new BuddyContextSnapshotRecord
        {
            generatedAtUtc = DateTime.UtcNow.ToString("o"),
            studentId = StudentId,
            sessionId = session.sessionId,
            currentAreaId = currentAreaId ?? "",
            currentZoneKind = currentZoneKind ?? "",
            currentConceptId = currentConceptId ?? "",
            profile = profile,
            learnerState = state,
            relevantRecentAttempts = relevant,
        };
    }

    void UpdateConceptState(BuddyLearningAttemptRecord attempt)
    {
        BuddyConceptLearningStateRecord concept = FindConcept(attempt.conceptId);
        concept.errorCounts ??= new List<BuddyLearningErrorCountRecord>();
        concept.modalityStates ??= new List<BuddyModalityLearningStateRecord>();
        concept.attempts++;
        concept.totalHints += attempt.hintCount;
        concept.totalResponseSeconds += attempt.responseSeconds;
        concept.lastPracticedAtUtc = attempt.createdAtUtc;
        if (attempt.correct)
        {
            concept.correctAttempts++;
            if (attempt.completedIndependently)
                concept.independentCorrectAttempts++;
            else
                concept.assistedCorrectAttempts++;
            if (attempt.attemptNumber == 1)
                concept.firstAttemptCorrectAttempts++;
        }

        foreach (string errorTag in attempt.errorTags)
        {
            BuddyLearningErrorCountRecord error = FindError(concept.errorCounts, errorTag);
            error.count++;
            error.lastSeenAtUtc = attempt.createdAtUtc;
        }

        BuddyModalityLearningStateRecord modality = FindModality(concept, attempt.modality);
        modality.attempts++;
        modality.totalResponseSeconds += attempt.responseSeconds;
        modality.lastPracticedAtUtc = attempt.createdAtUtc;
        if (attempt.correct)
        {
            modality.correctAttempts++;
            if (attempt.completedIndependently)
                modality.independentCorrectAttempts++;
        }

        modality.averageResponseSeconds = SafeDivide(modality.totalResponseSeconds, modality.attempts);
        modality.successRate = SafeDivide(modality.correctAttempts, modality.attempts);
        modality.independentSuccessRate = SafeDivide(modality.independentCorrectAttempts, modality.attempts);

        concept.averageResponseSeconds = SafeDivide(concept.totalResponseSeconds, concept.attempts);
        concept.successRate = SafeDivide(concept.correctAttempts, concept.attempts);
        concept.independentSuccessRate = SafeDivide(concept.independentCorrectAttempts, concept.attempts);
        concept.assistedSuccessRate = SafeDivide(concept.assistedCorrectAttempts, concept.attempts);
        concept.hintDependency = Mathf.Clamp01(SafeDivide(concept.totalHints, concept.attempts));
        float smoothedSuccess = SafeDivide(concept.correctAttempts + 1, concept.attempts + 2);
        float smoothedIndependent = SafeDivide(concept.independentCorrectAttempts + 1, concept.attempts + 2);
        float evidenceMastery = Mathf.Clamp01(
            0.55f * smoothedIndependent +
            0.35f * smoothedSuccess +
            0.10f * (1f - concept.hintDependency));
        float evidenceWeight = Mathf.Clamp01(concept.attempts / 8f);
        concept.masteryEstimate = Mathf.Lerp(0.25f, evidenceMastery, evidenceWeight);
        ResolveLanguageSupport(concept.masteryEstimate, out concept.supportBand, out concept.recommendedEnglishRatio);
        LearnerScheduler.UpdateReviewState(concept, attempt);
    }

    void UpdateConceptDiagnostics(BuddyLearningAttemptRecord attempt)
    {
        BuddyConceptLearningStateRecord concept = FindConcept(attempt.conceptId);
        concept.errorCounts ??= new List<BuddyLearningErrorCountRecord>();
        foreach (string errorTag in attempt.errorTags)
        {
            BuddyLearningErrorCountRecord error = FindError(concept.errorCounts, errorTag);
            error.count++;
            error.lastSeenAtUtc = attempt.createdAtUtc;
        }
        concept.lastPracticedAtUtc = attempt.createdAtUtc;
    }

    void RebuildLearnerSummary()
    {
        state.strengthConceptIds.Clear();
        state.needConceptIds.Clear();
        state.recurringErrorTags.Clear();

        var practiced = new List<BuddyConceptLearningStateRecord>();
        var combinedErrors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (BuddyConceptLearningStateRecord concept in state.concepts)
        {
            if (concept == null || concept.attempts <= 0)
                continue;
            practiced.Add(concept);
            foreach (BuddyLearningErrorCountRecord error in concept.errorCounts)
            {
                if (error == null || string.IsNullOrWhiteSpace(error.errorTag))
                    continue;
                combinedErrors[error.errorTag] = combinedErrors.TryGetValue(error.errorTag, out int count)
                    ? count + error.count
                    : error.count;
            }
        }

        practiced.Sort((a, b) => b.masteryEstimate.CompareTo(a.masteryEstimate));
        for (int i = 0; i < practiced.Count && state.strengthConceptIds.Count < 3; i++)
        {
            if (practiced[i].attempts >= 2)
                state.strengthConceptIds.Add(practiced[i].conceptId);
        }
        for (int i = practiced.Count - 1; i >= 0 && state.needConceptIds.Count < 3; i--)
            state.needConceptIds.Add(practiced[i].conceptId);

        var errorPairs = new List<KeyValuePair<string, int>>(combinedErrors);
        errorPairs.Sort((a, b) => b.Value.CompareTo(a.Value));
        for (int i = 0; i < errorPairs.Count && i < 5; i++)
            state.recurringErrorTags.Add(errorPairs[i].Key);

        float weightedMastery = 0f;
        int totalAttempts = 0;
        foreach (BuddyConceptLearningStateRecord concept in practiced)
        {
            weightedMastery += concept.masteryEstimate * concept.attempts;
            totalAttempts += concept.attempts;
        }
        float overallMastery = totalAttempts > 0 ? weightedMastery / totalAttempts : 0f;
        ResolveLanguageSupport(overallMastery, out state.supportBand, out state.recommendedEnglishRatio);
    }

    BuddyConceptLearningStateRecord FindConcept(string conceptId)
    {
        foreach (BuddyConceptLearningStateRecord concept in state.concepts)
        {
            if (concept != null && string.Equals(concept.conceptId, conceptId, StringComparison.OrdinalIgnoreCase))
                return concept;
        }

        var created = new BuddyConceptLearningStateRecord { conceptId = conceptId };
        state.concepts.Add(created);
        return created;
    }

    static BuddyLearningErrorCountRecord FindError(List<BuddyLearningErrorCountRecord> errors, string errorTag)
    {
        foreach (BuddyLearningErrorCountRecord error in errors)
        {
            if (error != null && string.Equals(error.errorTag, errorTag, StringComparison.OrdinalIgnoreCase))
                return error;
        }

        var created = new BuddyLearningErrorCountRecord { errorTag = errorTag };
        errors.Add(created);
        return created;
    }

    static BuddyModalityLearningStateRecord FindModality(BuddyConceptLearningStateRecord concept, string modality)
    {
        concept.modalityStates ??= new List<BuddyModalityLearningStateRecord>();
        foreach (BuddyModalityLearningStateRecord state in concept.modalityStates)
        {
            if (state != null && string.Equals(state.modality, modality, StringComparison.OrdinalIgnoreCase))
                return state;
        }

        var created = new BuddyModalityLearningStateRecord { modality = modality };
        concept.modalityStates.Add(created);
        return created;
    }

    void AddRecentAttempt(BuddyLearningAttemptRecord attempt)
    {
        state.recentAttempts.Add(attempt);
        while (state.recentAttempts.Count > RecentAttemptLimit)
            state.recentAttempts.RemoveAt(0);
    }

    int NextAttemptNumber(string groupId)
    {
        int next = attemptCountsByGroup.TryGetValue(groupId, out int current) ? current + 1 : 1;
        attemptCountsByGroup[groupId] = next;
        return next;
    }

    string BuildAttemptGroupId(BuddyLearningAttemptRecord attempt)
    {
        return $"{session.sessionId}:{attempt.inputSource}:{attempt.contentId}:{attempt.areaId}";
    }

    void AppendAttempt(BuddyLearningAttemptRecord attempt)
    {
        try
        {
            File.AppendAllText(attemptHistoryPath, JsonUtility.ToJson(attempt, false) + Environment.NewLine);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[BuddyLearningData] Could not append attempt history: {ex.Message}");
        }
    }

    void PersistState()
    {
        try
        {
            File.WriteAllText(learnerStatePath, JsonUtility.ToJson(state, true));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[BuddyLearningData] Could not persist learner state: {ex.Message}");
        }
    }

    void PersistSession()
    {
        try
        {
            File.WriteAllText(Path.Combine(sessionsDirectory, $"{session.sessionId}.json"), JsonUtility.ToJson(session, true));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[BuddyLearningData] Could not persist session: {ex.Message}");
        }
    }

    BuddyLearnerStateRecord LoadLearnerState()
    {
        try
        {
            if (File.Exists(learnerStatePath))
            {
                BuddyLearnerStateRecord loaded = JsonUtility.FromJson<BuddyLearnerStateRecord>(File.ReadAllText(learnerStatePath));
                if (loaded != null && string.Equals(loaded.studentId, StudentId, StringComparison.OrdinalIgnoreCase))
                    return loaded;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[BuddyLearningData] Could not load learner state: {ex.Message}");
        }

        return new BuddyLearnerStateRecord { studentId = StudentId };
    }

    void EnsureDirectories()
    {
        try
        {
            Directory.CreateDirectory(dataDirectory);
            Directory.CreateDirectory(sessionsDirectory);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[BuddyLearningData] Could not create data directories: {ex.Message}");
        }
    }

    static void ResolveLanguageSupport(float mastery, out string band, out float englishRatio)
    {
        if (mastery < 0.35f)
        {
            band = BuddyLearningSupportBand.Foundation.ToString();
            englishRatio = 0.3f;
        }
        else if (mastery < 0.55f)
        {
            band = BuddyLearningSupportBand.Guided.ToString();
            englishRatio = 0.45f;
        }
        else if (mastery < 0.75f)
        {
            band = BuddyLearningSupportBand.Growing.ToString();
            englishRatio = 0.65f;
        }
        else
        {
            band = BuddyLearningSupportBand.Independent.ToString();
            englishRatio = 0.85f;
        }
    }

    static List<string> NormalizeTags(IEnumerable<string> values)
    {
        var result = new List<string>();
        if (values == null)
            return result;
        foreach (string value in values)
        {
            string normalized = string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
            if (!string.IsNullOrWhiteSpace(normalized) && !result.Contains(normalized))
                result.Add(normalized);
        }
        return result;
    }

    static void AddUnique(List<string> values, string value)
    {
        if (values == null || string.IsNullOrWhiteSpace(value) || values.Contains(value))
            return;
        values.Add(value);
    }

    static string NormalizeResponse(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "" : VoiceUnlockRecognizer.NormalizeKeyword(value);
    }

    static string NormalizeLanguage(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        string normalized = value.Trim().ToLowerInvariant();
        int separator = normalized.IndexOfAny(new[] { '-', '_' });
        return separator > 0 ? normalized.Substring(0, separator) : normalized;
    }

    static string CapText(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        const int maxLength = 4000;
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }

    static float SafeDivide(float numerator, float denominator)
    {
        return denominator <= 0f ? 0f : numerator / denominator;
    }

    static string SanitizePathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "local-player";
        foreach (char invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');
        return value.Trim();
    }
}
