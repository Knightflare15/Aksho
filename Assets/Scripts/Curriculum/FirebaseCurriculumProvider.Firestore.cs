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
    IEnumerator PostFirestoreDocument(string functionName, object record)
    {
        string collectionName = CollectionNameFor(functionName);
        string documentId = DocumentIdFor(functionName, record);
        string studentId = StudentIdFor(record);
        if (string.IsNullOrWhiteSpace(collectionName) || string.IsNullOrWhiteSpace(documentId) || string.IsNullOrWhiteSpace(studentId))
        {
            Debug.LogWarning($"[FirebaseCurriculumProvider] Could not map {functionName} to a Firestore document.");
            yield break;
        }

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

        string url = $"https://firestore.googleapis.com/v1/projects/{projectId}/databases/(default)/documents/schools/{schoolId}/students/{studentId}/{collectionName}/{documentId}";
        string body = "{\"fields\":" + FirestoreFieldsJson(record) + "}";
        using UnityWebRequest request = new UnityWebRequest(url, "PATCH");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", $"Bearer {idToken}");
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[FirebaseCurriculumProvider] Firestore write {collectionName} failed: {DescribeRequestFailure(request)}");
            QueueFirestoreWrite(url, body);
            yield break;
        }

        ServerAnalysisJobRecord analysisJob = audioReady
            ? BuildServerAnalysisJob(functionName, record, collectionName, documentId)
            : null;
        if (analysisJob != null)
        {
            bool analysisJobSubmitted = false;
            yield return PostFirestoreDocument("submitServerAnalysisJob", analysisJob, value => analysisJobSubmitted = value);
            if (!analysisJobSubmitted)
                Debug.LogWarning($"[FirebaseCurriculumProvider] Azure pronunciation job queued for retry: {analysisJob.jobId}");
        }

        if (audioReady && record is WordCastRecord submittedCast)
            yield return PollServerPronunciationInsight(submittedCast, documentId);
        else if (audioReady && record is SpokenPhraseEventRecord submittedPhrase && HasPendingServerAnalysis(submittedPhrase))
            yield return PollSpokenPhrasePronunciationInsight(submittedPhrase, documentId);
        else if (audioReady && record is CountingMiniGameAttemptRecord countingAttempt && HasPendingServerAnalysis(countingAttempt))
            yield return PollGenericPronunciationInsight("countingMiniGameAttempts", documentId, countingAttempt.studentId, CountingNumberUtility.ToWord(countingAttempt.selectedCount));
        else if (audioReady && record is ColorMiniGameAttemptRecord colorAttempt && HasPendingServerAnalysis(colorAttempt))
            yield return PollGenericPronunciationInsight("colorMiniGameAttempts", documentId, colorAttempt.studentId, colorAttempt.selectedColor);
    }

    IEnumerator PostFirestoreDocument(string functionName, object record, Action<bool> completed)
    {
        bool succeeded = false;
        yield return PostFirestoreDocumentWithResult(functionName, record, value => succeeded = value);
        completed?.Invoke(succeeded);
    }

    IEnumerator PostFirestoreDocumentWithResult(string functionName, object record, Action<bool> completed)
    {
        string collectionName = CollectionNameFor(functionName);
        string documentId = DocumentIdFor(functionName, record);
        string studentId = StudentIdFor(record);
        if (string.IsNullOrWhiteSpace(collectionName) || string.IsNullOrWhiteSpace(documentId) || string.IsNullOrWhiteSpace(studentId))
        {
            Debug.LogWarning($"[FirebaseCurriculumProvider] Could not map {functionName} to a Firestore document.");
            completed?.Invoke(false);
            yield break;
        }

        string url = $"https://firestore.googleapis.com/v1/projects/{projectId}/databases/(default)/documents/schools/{schoolId}/students/{studentId}/{collectionName}/{documentId}";
        string body = "{\"fields\":" + FirestoreFieldsJson(record) + "}";
        using UnityWebRequest request = new UnityWebRequest(url, "PATCH");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", $"Bearer {idToken}");
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[FirebaseCurriculumProvider] Firestore write {collectionName} failed: {DescribeRequestFailure(request)}");
            QueueFirestoreWrite(url, body);
            completed?.Invoke(false);
            yield break;
        }

        completed?.Invoke(true);
    }

    IEnumerator PollServerPronunciationInsight(WordCastRecord submittedCast, string documentId)
    {
        if (submittedCast == null ||
            string.IsNullOrWhiteSpace(documentId) ||
            string.IsNullOrWhiteSpace(submittedCast.studentId) ||
            !string.Equals(submittedCast.serverAnalysisStatus, "pending", StringComparison.OrdinalIgnoreCase))
            yield break;

        TrackPendingServerPronunciationPoll(submittedCast, documentId);
        string url = $"https://firestore.googleapis.com/v1/projects/{projectId}/databases/(default)/documents/schools/{schoolId}/students/{submittedCast.studentId}/wordCastEvents/{documentId}";
        int attempt = 0;
        while (IsConfigured() && attempt < ServerPronunciationPollMaxAttempts)
        {
            if (attempt > 0)
                yield return new WaitForSecondsRealtime(ServerPronunciationPollIntervalSeconds);

            using UnityWebRequest request = UnityWebRequest.Get(url);
            request.SetRequestHeader("Authorization", $"Bearer {idToken}");
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                if (attempt == 0 || attempt % ServerPronunciationPollLogEveryAttempts == 0)
                    Debug.LogWarning($"[FirebaseCurriculumProvider] Server pronunciation poll still waiting for '{submittedCast.word}' attempt={attempt + 1}: {DescribeRequestFailure(request)}");
                attempt++;
                continue;
            }

            FirestoreWordCastDocument document = JsonUtility.FromJson<FirestoreWordCastDocument>(request.downloadHandler.text);
            WordCastRecord updated = document?.ToRecord(submittedCast);
            string status = updated?.serverAnalysisStatus ?? "";
            if (string.Equals(status, "complete", StringComparison.OrdinalIgnoreCase) &&
                updated.serverPronunciationInsight != null)
            {
                Debug.Log($"[FirebaseCurriculumProvider] Server pronunciation insight ready for '{updated.word}' provider='{updated.serverPronunciationInsight.providerName}' score={updated.serverPronunciationInsight.score:0.00}");
                RemovePendingServerPronunciationPoll(documentId, submittedCast.studentId);
                CurriculumSessionManager.Instance?.NotifyServerPronunciationInsight(updated);
                yield break;
            }

            if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"[FirebaseCurriculumProvider] Server pronunciation analysis failed for '{submittedCast.word}'.");
                RemovePendingServerPronunciationPoll(documentId, submittedCast.studentId);
                yield break;
            }

            if (attempt == 0 || attempt % ServerPronunciationPollLogEveryAttempts == 0)
                Debug.Log($"[FirebaseCurriculumProvider] Waiting for Azure pronunciation insight for '{submittedCast.word}' status='{status}' attempt={attempt + 1}.");
            attempt++;
        }

        Debug.LogWarning(IsConfigured()
            ? $"[FirebaseCurriculumProvider] Server pronunciation polling paused for '{submittedCast.word}' after {ServerPronunciationPollMaxAttempts} attempts; it remains queued for a later session."
            : $"[FirebaseCurriculumProvider] Server pronunciation polling stopped for '{submittedCast.word}' because Firebase configuration is no longer available.");
    }

    void TrackPendingServerPronunciationPoll(WordCastRecord castEvent, string documentId)
    {
        if (castEvent == null || string.IsNullOrWhiteSpace(documentId) || string.IsNullOrWhiteSpace(castEvent.studentId))
            return;

        string entry = EncodeServerPronunciationPollEntry(documentId, castEvent.studentId, castEvent.word);
        string raw = PlayerPrefs.GetString(ServerPronunciationPollQueueKey, "");
        foreach (string existing in SplitPollEntries(raw))
        {
            if (string.Equals(existing, entry, StringComparison.Ordinal))
                return;
        }

        string next = string.IsNullOrEmpty(raw) ? entry : $"{raw}\n{entry}";
        PlayerPrefs.SetString(ServerPronunciationPollQueueKey, next);
        PlayerPrefs.Save();
    }

    void RemovePendingServerPronunciationPoll(string documentId, string studentId)
    {
        string raw = PlayerPrefs.GetString(ServerPronunciationPollQueueKey, "");
        if (string.IsNullOrWhiteSpace(raw))
            return;

        var remaining = new List<string>();
        foreach (string entry in SplitPollEntries(raw))
        {
            if (!TryDecodeServerPronunciationPollEntry(entry, out string queuedDocumentId, out string queuedStudentId, out _))
                continue;
            if (string.Equals(queuedDocumentId, documentId, StringComparison.Ordinal) &&
                string.Equals(queuedStudentId, studentId, StringComparison.Ordinal))
                continue;
            remaining.Add(entry);
        }

        if (remaining.Count == 0)
            PlayerPrefs.DeleteKey(ServerPronunciationPollQueueKey);
        else
            PlayerPrefs.SetString(ServerPronunciationPollQueueKey, string.Join("\n", remaining));
        PlayerPrefs.Save();
    }

    void FlushPendingServerPronunciationPolls()
    {
        if (!IsConfigured() || CurriculumSessionManager.Instance == null)
            return;

        string raw = PlayerPrefs.GetString(ServerPronunciationPollQueueKey, "");
        var retained = new List<string>();
        bool removedStaleEntries = false;
        foreach (string entry in SplitPollEntries(raw))
        {
            if (!TryDecodeServerPronunciationPollEntry(entry, out string documentId, out string studentId, out string word))
                continue;

            if (!string.Equals(studentId, CurriculumSessionManager.Instance.activeStudentId, StringComparison.OrdinalIgnoreCase))
            {
                removedStaleEntries = true;
                Debug.Log($"[FirebaseCurriculumProvider] Dropping stale Azure pronunciation poll for student '{studentId}' while signed in as '{CurriculumSessionManager.Instance.activeStudentId}'.");
                continue;
            }

            retained.Add(entry);
            var castEvent = new WordCastRecord
            {
                eventId = documentId,
                studentId = studentId,
                word = word,
                serverAnalysisStatus = "pending",
            };
            CurriculumSessionManager.Instance.StartCoroutine(PollServerPronunciationInsight(castEvent, documentId));
        }

        if (removedStaleEntries)
        {
            if (retained.Count == 0)
                PlayerPrefs.DeleteKey(ServerPronunciationPollQueueKey);
            else
                PlayerPrefs.SetString(ServerPronunciationPollQueueKey, string.Join("\n", retained));
            PlayerPrefs.Save();
        }
    }

    static string EncodeServerPronunciationPollEntry(string documentId, string studentId, string word)
    {
        return $"{SanitizePollPart(documentId)}\t{SanitizePollPart(studentId)}\t{SanitizePollPart(SpellRegistry.NormalizeWord(word))}";
    }

    static bool TryDecodeServerPronunciationPollEntry(string entry, out string documentId, out string studentId, out string word)
    {
        documentId = "";
        studentId = "";
        word = "";
        if (string.IsNullOrWhiteSpace(entry))
            return false;

        string[] parts = entry.Split('\t');
        if (parts.Length < 3)
            return false;

        documentId = parts[0];
        studentId = parts[1];
        word = parts[2];
        return !string.IsNullOrWhiteSpace(documentId) && !string.IsNullOrWhiteSpace(studentId);
    }

    static IEnumerable<string> SplitPollEntries(string raw)
    {
        return string.IsNullOrWhiteSpace(raw)
            ? Array.Empty<string>()
            : raw.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
    }

    static string SanitizePollPart(string value)
    {
        return (value ?? "").Replace("\t", "").Replace("\n", "").Replace("\r", "").Trim();
    }
}
