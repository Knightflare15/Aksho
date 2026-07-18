using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public sealed partial class FirebaseCurriculumProvider : ICurriculumAccessProvider, IAsyncCurriculumAccessProvider, IRemoteLearnerStateProvider
{
    IEnumerator FetchTodayMission(string studentId, Action<MissionAssignment> completed)
    {
        string date = DateTime.UtcNow.ToString("yyyy-MM-dd");
        string overridePath = $"schools/{schoolId}/students/{studentId}/studentMissionOverrides/{date}";
        MissionAssignment mission = null;
        yield return FetchMissionDocument(overridePath, studentId, true, value => mission = value);
        if (mission == null)
        {
            string classPath = $"schools/{schoolId}/classes/{classId}/dailyMissions/{date}";
            yield return FetchMissionDocument(classPath, studentId, false, value => mission = value);
        }

        if (mission != null)
        {
            mission.schoolId = schoolId;
            mission.classId = classId;
            mission.studentId = studentId;
        }
        completed?.Invoke(mission);
    }

    IEnumerator FetchCurrentWorldGoal(string studentId, Action<WorldGoalAssignment> completed)
    {
        string weekStart = DateTime.UtcNow.Date.AddDays(-(int)DateTime.UtcNow.DayOfWeek).ToString("yyyy-MM-dd");
        string canonicalGoalId = $"{classId}_{weekStart}_world";
        WorldGoalAssignment goal = null;
        foreach (string goalDocumentId in new[] { weekStart, canonicalGoalId })
        {
            string path = $"schools/{schoolId}/students/{studentId}/worldGoals/{goalDocumentId}";
            yield return FetchWorldGoalDocument(path, studentId, true, value => goal = value);
            if (goal != null)
                break;
        }

        if (goal == null)
        {
            foreach (string goalDocumentId in new[] { weekStart, canonicalGoalId })
            {
                string path = $"schools/{schoolId}/classes/{classId}/worldGoals/{goalDocumentId}";
                yield return FetchWorldGoalDocument(path, studentId, true, value => goal = value);
                if (goal != null)
                    break;
            }
        }
        completed?.Invoke(goal);
    }

    IEnumerator FetchWorldGoalDocument(string path, string studentId, bool quietNotFound, Action<WorldGoalAssignment> completed)
    {
        string url = $"https://firestore.googleapis.com/v1/projects/{projectId}/databases/(default)/documents/{path}";
        using UnityWebRequest request = UnityWebRequest.Get(url);
        request.SetRequestHeader("Authorization", $"Bearer {idToken}");
        request.timeout = 8;
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            if (!quietNotFound && request.responseCode != 404)
                Debug.LogWarning($"[FirebaseCurriculumProvider] Firestore world-goal request failed: {DescribeRequestFailure(request)}");
            completed?.Invoke(null);
            yield break;
        }

        FirestoreWorldGoalDocument document = JsonUtility.FromJson<FirestoreWorldGoalDocument>(request.downloadHandler.text);
        WorldGoalAssignment goal = document?.ToGoal(studentId);
        if (goal != null)
        {
            goal.schoolId = schoolId;
            goal.classId = classId;
        }
        completed?.Invoke(goal);
    }

    IEnumerator FetchMissionDocument(string path, string studentId, bool quietNotFound, Action<MissionAssignment> completed)
    {
        string url = $"https://firestore.googleapis.com/v1/projects/{projectId}/databases/(default)/documents/{path}";
        using UnityWebRequest request = UnityWebRequest.Get(url);
        request.SetRequestHeader("Authorization", $"Bearer {idToken}");
        request.timeout = 8;
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            if (!quietNotFound && request.responseCode != 404)
                Debug.LogWarning($"[FirebaseCurriculumProvider] Firestore mission request failed: {DescribeRequestFailure(request)}");
            completed?.Invoke(null);
            yield break;
        }

        FirestoreMissionDocument document = JsonUtility.FromJson<FirestoreMissionDocument>(request.downloadHandler.text);
        completed?.Invoke(document?.ToMission(studentId));
    }

    void SubmitCallable<T>(string functionName, T record, Action fallbackWrite)
    {
        if (!IsConfigured())
        {
            fallbackWrite?.Invoke();
            return;
        }

        if (CurriculumSessionManager.Instance != null)
        {
            if (string.IsNullOrWhiteSpace(functionsBaseUrl))
                CurriculumSessionManager.Instance.StartCoroutine(PostFirestoreDocument(functionName, record));
            else
                CurriculumSessionManager.Instance.StartCoroutine(PostCallable(functionName, record));
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(functionsBaseUrl))
            {
                string body = "{\"data\":" + JsonUtility.ToJson(record) + "}";
                QueueSubmission(functionName, body);
            }
        }
    }

    IEnumerator PostCallable(string functionName, object record)
    {
        if (record is WordCastRecord castEvent && ShouldUseDirectWavLm(castEvent))
        {
            bool analyzed = false;
            yield return TryAnalyzeWithDirectWavLm(castEvent, value => analyzed = value);
            if (!analyzed)
            {
                QueueDirectWavLmRecord(castEvent);
                yield break;
            }
        }

        bool audioReady = true;
        yield return UploadAudioEvidenceIfNeeded(record, value => audioReady = value);
        if (!audioReady)
            MarkServerAnalysisFailed(record);

        string body = "{\"data\":" + JsonUtility.ToJson(record) + "}";
        string url = $"{functionsBaseUrl}/{functionName}";
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        bool submitted = false;
        string lastFailure = "";
        for (int attempt = 1; attempt <= CallableRequestAttempts; attempt++)
        {
            float startedAt = Time.realtimeSinceStartup;
            using UnityWebRequest request = new UnityWebRequest(url, "POST");
            request.uploadHandler = new UploadHandlerRaw(bytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {idToken}");
            request.timeout = CallableRequestTimeoutSeconds;

            Debug.Log($"[FirebaseCurriculumProvider] callable start name='{functionName}' attempt={attempt}/{CallableRequestAttempts} url='{url}' payloadBytes={bytes.Length} tokenPresent={!string.IsNullOrWhiteSpace(idToken)}.");
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log($"[FirebaseCurriculumProvider] callable complete name='{functionName}' attempt={attempt}/{CallableRequestAttempts} http={request.responseCode} elapsed={(Time.realtimeSinceStartup - startedAt):0.00}s response='{ClipLogBody(request.downloadHandler?.text)}'.");
                submitted = true;
                break;
            }

            lastFailure = DescribeRequestFailure(request);
            Debug.LogWarning($"[FirebaseCurriculumProvider] callable failed name='{functionName}' attempt={attempt}/{CallableRequestAttempts} http={request.responseCode} elapsed={(Time.realtimeSinceStartup - startedAt):0.00}s failure={lastFailure}");
            if (attempt < CallableRequestAttempts && IsTransientCallableFailure(request))
                yield return new WaitForSecondsRealtime(attempt);
            else
                break;
        }

        if (!submitted)
        {
            Debug.LogWarning($"[FirebaseCurriculumProvider] {functionName} was queued after callable failure: {lastFailure}");
            QueueSubmission(functionName, body);
            if (audioReady && HasPendingServerAnalysis(record) && record is WordCastRecord queuedCast)
                TrackPendingServerPronunciationPoll(queuedCast, queuedCast.eventId);
            yield break;
        }

        if (audioReady && record is WordCastRecord submittedCast)
            yield return PollServerPronunciationInsight(submittedCast, submittedCast.eventId);
        else if (audioReady && record is SpokenPhraseEventRecord submittedPhrase && HasPendingServerAnalysis(submittedPhrase))
            yield return PollSpokenPhrasePronunciationInsight(submittedPhrase, submittedPhrase.eventId);
        else if (audioReady && record is CountingMiniGameAttemptRecord countingAttempt && HasPendingServerAnalysis(countingAttempt))
            yield return PollGenericPronunciationInsight("countingMiniGameAttempts", countingAttempt.attemptId, countingAttempt.studentId, CountingNumberUtility.ToWord(countingAttempt.selectedCount));
        else if (audioReady && record is ColorMiniGameAttemptRecord colorAttempt && HasPendingServerAnalysis(colorAttempt))
            yield return PollGenericPronunciationInsight("colorMiniGameAttempts", colorAttempt.attemptId, colorAttempt.studentId, colorAttempt.selectedColor);
    }

    IEnumerator PollSpokenPhrasePronunciationInsight(SpokenPhraseEventRecord phrase, string documentId)
    {
        if (phrase == null || string.IsNullOrWhiteSpace(documentId) || string.IsNullOrWhiteSpace(phrase.studentId))
            yield break;

        string url = $"https://firestore.googleapis.com/v1/projects/{projectId}/databases/(default)/documents/schools/{schoolId}/students/{phrase.studentId}/spokenPhraseEvents/{documentId}";
        for (int attempt = 0; attempt < 30 && IsConfigured(); attempt++)
        {
            if (attempt > 0)
                yield return new WaitForSecondsRealtime(ServerPronunciationPollIntervalSeconds);

            using UnityWebRequest request = UnityWebRequest.Get(url);
            request.SetRequestHeader("Authorization", $"Bearer {idToken}");
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
                continue;

            FirestoreSpokenPhraseDocument document = JsonUtility.FromJson<FirestoreSpokenPhraseDocument>(request.downloadHandler.text);
            string status = document?.fields?.serverAnalysisStatus?.AsString() ?? "";
            PronunciationInsightRecord insight = document?.fields?.serverPronunciationInsight?.ToRecord();
            if (string.Equals(status, "complete", StringComparison.OrdinalIgnoreCase) && insight != null)
            {
                Debug.Log($"[FirebaseCurriculumProvider] Azure pronunciation insight ready for phrase '{phrase.targetPhrase}' provider='{insight.providerName}' score={insight.score:0.00}");
                CurriculumSessionManager.Instance?.NotifyServerPronunciationInsight(new WordCastRecord
                {
                    eventId = documentId,
                    studentId = phrase.studentId,
                    word = phrase.targetPhrase,
                    serverPronunciationInsight = insight,
                });
                yield break;
            }
            if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
                yield break;
        }
    }

    IEnumerator PollGenericPronunciationInsight(string collectionName, string documentId, string studentId, string targetPhrase)
    {
        if (string.IsNullOrWhiteSpace(collectionName) || string.IsNullOrWhiteSpace(documentId) || string.IsNullOrWhiteSpace(studentId))
            yield break;

        string url = $"https://firestore.googleapis.com/v1/projects/{projectId}/databases/(default)/documents/schools/{schoolId}/students/{studentId}/{collectionName}/{documentId}";
        for (int attempt = 0; attempt < 30 && IsConfigured(); attempt++)
        {
            if (attempt > 0)
                yield return new WaitForSecondsRealtime(ServerPronunciationPollIntervalSeconds);

            using UnityWebRequest request = UnityWebRequest.Get(url);
            request.SetRequestHeader("Authorization", $"Bearer {idToken}");
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
                continue;

            FirestoreSpokenPhraseDocument document = JsonUtility.FromJson<FirestoreSpokenPhraseDocument>(request.downloadHandler.text);
            string status = document?.fields?.serverAnalysisStatus?.AsString() ?? "";
            PronunciationInsightRecord insight = document?.fields?.serverPronunciationInsight?.ToRecord();
            if (string.Equals(status, "complete", StringComparison.OrdinalIgnoreCase) && insight != null)
            {
                Debug.Log($"[FirebaseCurriculumProvider] Azure pronunciation insight ready for '{targetPhrase}' collection='{collectionName}' score={insight.score:0.00}");
                CurriculumSessionManager.Instance?.NotifyServerPronunciationInsight(new WordCastRecord
                {
                    eventId = documentId,
                    studentId = studentId,
                    word = targetPhrase ?? "",
                    serverPronunciationInsight = insight,
                });
                yield break;
            }
            if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
                yield break;
        }
    }

    IEnumerator UploadAudioEvidenceIfNeeded(object record, Action<bool> completed)
    {
        completed?.Invoke(true);

        if (record is WordCastRecord castEvent && ShouldUseDirectWavLm(castEvent))
            yield break;

        byte[] audioBytes = BytesField(record, "pronunciationAudioWavBytes");
        if (audioBytes == null || audioBytes.Length <= 44)
            yield break;

        string serverAnalysisJobId = StringField(record, "serverAnalysisJobId");
        string serverAnalysisStatus = StringField(record, "serverAnalysisStatus");
        if (string.IsNullOrWhiteSpace(serverAnalysisJobId) ||
            !string.Equals(serverAnalysisStatus, "pending", StringComparison.OrdinalIgnoreCase))
            yield break;

        if (!string.IsNullOrWhiteSpace(StringField(record, "audioStoragePath")))
            yield break;

        if (string.IsNullOrWhiteSpace(storageBucket))
        {
            Debug.LogWarning("[FirebaseCurriculumProvider] Cannot upload pronunciation audio: Firebase Storage bucket is not configured.");
            completed?.Invoke(false);
            yield break;
        }

        string objectName = $"schools/{schoolId}/students/{StudentIdFor(record)}/pronunciationAudio/{serverAnalysisJobId}.wav";
        string uploadedBucket = "";
        string lastError = "";
        foreach (string candidateBucket in CandidateStorageBuckets(storageBucket, projectId))
        {
            string url = $"https://firebasestorage.googleapis.com/v0/b/{candidateBucket}/o?uploadType=media&name={EscapeUrl(objectName)}";
            using UnityWebRequest request = new UnityWebRequest(url, "POST");
            request.uploadHandler = new UploadHandlerRaw(audioBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "audio/wav");
            request.SetRequestHeader("Authorization", $"Bearer {idToken}");
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                uploadedBucket = candidateBucket;
                break;
            }

            lastError = DescribeRequestFailure(request);
            if (request.responseCode != 404)
                break;
        }

        if (string.IsNullOrWhiteSpace(uploadedBucket))
        {
            Debug.LogWarning($"[FirebaseCurriculumProvider] Pronunciation audio upload failed: {lastError}");
            completed?.Invoke(false);
            yield break;
        }

        string audioStoragePath = $"gs://{uploadedBucket}/{objectName}";
        SetStringField(record, "audioStoragePath", audioStoragePath);
        SetStringField(record, "audioContentType", "audio/wav");
        SetBoolField(record, "rawAudioUploaded", true);
        Debug.Log($"[FirebaseCurriculumProvider] Uploaded pronunciation audio evidence: {audioStoragePath}");
    }
}
