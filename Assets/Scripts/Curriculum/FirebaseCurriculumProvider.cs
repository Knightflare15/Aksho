using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// REST-backed curriculum provider for production school mode.
/// Uses Firestore REST for mission loading and callable Cloud Functions for writes.
/// Falls back to LocalDemoCurriculumProvider whenever configuration or network access is unavailable.
/// </summary>
public sealed partial class FirebaseCurriculumProvider : ICurriculumAccessProvider, IAsyncCurriculumAccessProvider, IRemoteLearnerStateProvider
{
    const string QueueKey = "TheScript.PendingCurriculumSubmissions";
    const string FirestoreQueueKey = "TheScript.PendingCurriculumFirestoreWrites";
    const string ServerPronunciationPollQueueKey = "TheScript.PendingServerPronunciationPolls";
    const string DirectWavLmQueueDirectory = "PendingWavLmWordCasts";
    const int ServerPronunciationPollLogEveryAttempts = 12;
    const float ServerPronunciationPollIntervalSeconds = 5f;
    const int ServerPronunciationPollMaxAttempts = 30;
    const int CallableRequestAttempts = 3;
    const int CallableRequestTimeoutSeconds = 30;
    const int MaxQueuedSubmissions = 500;
    const int MaxQueuedSubmissionBytes = 2 * 1024 * 1024;
    const float QueuedRetryBaseSeconds = 15f;
    const float QueuedRetryMaxSeconds = 300f;

    readonly string projectId;
    readonly string storageBucket;
    readonly string functionsBaseUrl;
    readonly string wavLmApiBaseUrl;
    readonly string schoolId;
    readonly string classId;
    readonly string idToken;
    readonly LocalDemoCurriculumProvider fallback = new LocalDemoCurriculumProvider();
    bool queuedRetryScheduled;
    int queuedRetryFailureStreak;

    public FirebaseCurriculumProvider(
        string projectId,
        string storageBucket,
        string functionsBaseUrl,
        string wavLmApiBaseUrl,
        string schoolId,
        string classId,
        string idToken)
    {
        this.projectId = projectId;
        this.storageBucket = string.IsNullOrWhiteSpace(storageBucket) ? DefaultStorageBucket(projectId) : storageBucket.Trim();
        this.functionsBaseUrl = TrimSlash(functionsBaseUrl);
        this.wavLmApiBaseUrl = TrimSlash(wavLmApiBaseUrl);
        this.schoolId = schoolId;
        this.classId = classId;
        this.idToken = idToken;
        FlushQueuedSubmissions();
    }

    public MissionAssignment GetTodayMission(string studentId)
    {
        Debug.LogWarning("[FirebaseCurriculumProvider] Synchronous mission reads are disabled; use GetTodayMissionAsync.");
        return null;
    }

    public WorldGoalAssignment GetCurrentWorldGoal(string studentId)
    {
        Debug.LogWarning("[FirebaseCurriculumProvider] Synchronous goal reads are disabled; use GetCurrentWorldGoalAsync.");
        return null;
    }

    public void GetTodayMissionAsync(string studentId, Action<MissionAssignment> completed)
    {
        if (!IsConfigured() || CurriculumSessionManager.Instance == null)
        {
            completed?.Invoke(null);
            return;
        }
        CurriculumSessionManager.Instance.StartCoroutine(FetchTodayMission(studentId, completed));
    }

    public void GetCurrentWorldGoalAsync(string studentId, Action<WorldGoalAssignment> completed)
    {
        if (!IsConfigured() || CurriculumSessionManager.Instance == null)
        {
            completed?.Invoke(null);
            return;
        }
        CurriculumSessionManager.Instance.StartCoroutine(FetchCurrentWorldGoal(studentId, completed));
    }

    public void GetBuddyLearnerStateAsync(string studentId, Action<BuddyLearnerStateRecord> completed)
    {
        if (!IsConfigured() || string.IsNullOrWhiteSpace(functionsBaseUrl) || CurriculumSessionManager.Instance == null)
        {
            completed?.Invoke(null);
            return;
        }
        CurriculumSessionManager.Instance.StartCoroutine(FetchBuddyLearnerState(studentId, completed));
    }

    IEnumerator FetchBuddyLearnerState(string studentId, Action<BuddyLearnerStateRecord> completed)
    {
        string safeStudentId = studentId?.Trim() ?? "";
        string body = "{\"data\":{\"schoolId\":\"" + EscapeJson(schoolId) +
            "\",\"studentId\":\"" + EscapeJson(safeStudentId) +
            "\",\"recentAttemptLimit\":20}}";
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        string url = $"{functionsBaseUrl}/getBuddyLearnerContext";
        string lastFailure = "";
        for (int attempt = 1; attempt <= CallableRequestAttempts; attempt++)
        {
            using UnityWebRequest request = new UnityWebRequest(url, "POST");
            request.uploadHandler = new UploadHandlerRaw(bytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {idToken}");
            request.timeout = CallableRequestTimeoutSeconds;
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                BuddyLearnerContextEnvelope envelope = JsonUtility.FromJson<BuddyLearnerContextEnvelope>(request.downloadHandler.text);
                completed?.Invoke(envelope?.result?.clientLearnerState);
                yield break;
            }

            lastFailure = DescribeRequestFailure(request);
            if (attempt < CallableRequestAttempts && IsTransientCallableFailure(request))
                yield return new WaitForSecondsRealtime(attempt);
            else
                break;
        }

        Debug.LogWarning($"[FirebaseCurriculumProvider] Learner-state hydration failed: {lastFailure}");
        completed?.Invoke(null);
    }

    public void SubmitRunSession(CurriculumRunSessionRecord session)
    {
        SubmitCallable("submitRunSession", session, () => fallback.SubmitRunSession(session));
    }

    public void SubmitLetterAttempt(LetterAttemptRecord attempt)
    {
        SubmitCallable("submitLetterAttempt", attempt, () => fallback.SubmitLetterAttempt(attempt));
    }

    public void SubmitWordCast(WordCastRecord castEvent)
    {
        SubmitCallable("submitWordCast", castEvent, () => fallback.SubmitWordCast(castEvent));
    }

    public void SubmitSpokenPhraseEvent(SpokenPhraseEventRecord phraseEvent)
    {
        SubmitCallable("submitSpokenPhraseEvent", phraseEvent, () => fallback.SubmitSpokenPhraseEvent(phraseEvent));
    }

    public void SubmitWrittenPhraseEvent(WrittenPhraseEventRecord phraseEvent)
    {
        SubmitCallable("submitWrittenPhraseEvent", phraseEvent, () => fallback.SubmitWrittenPhraseEvent(phraseEvent));
    }

    public void SubmitGrammarBattleEvent(GrammarBattleEventRecord battleEvent)
    {
        SubmitCallable("submitGrammarBattleEvent", battleEvent, () => fallback.SubmitGrammarBattleEvent(battleEvent));
    }

    public void SubmitBuddyConversationTurn(BuddyConversationTurnRecord turn)
    {
        SubmitCallable("submitBuddyConversationTurn", turn, () => fallback.SubmitBuddyConversationTurn(turn));
    }

    public void SubmitBuddyLearningAttempt(BuddyLearningAttemptRecord attempt)
    {
        SubmitCallable("submitBuddyLearningAttempt", attempt, () => fallback.SubmitBuddyLearningAttempt(attempt));
    }

    public void SubmitBuddyLearningSession(BuddyLearningSessionRecord session)
    {
        SubmitCallable("submitBuddyLearningSession", session, () => fallback.SubmitBuddyLearningSession(session));
    }

    public void SubmitBuddyLearnerProfile(BuddyLearnerProfileRecord profile)
    {
        SubmitCallable("submitBuddyLearnerProfile", profile, () => fallback.SubmitBuddyLearnerProfile(profile));
    }

    public void SubmitGymAttempt(GymAttemptRecord gymAttempt)
    {
        SubmitCallable("submitGymAttempt", gymAttempt, () => fallback.SubmitGymAttempt(gymAttempt));
    }

    public void SubmitAcceptedTemplate(AcceptedHandwritingTemplateRecord template)
    {
        SubmitCallable("submitAcceptedTemplate", template, () => fallback.SubmitAcceptedTemplate(template));
    }

    public void SubmitCountingMiniGameAttempt(CountingMiniGameAttemptRecord attempt)
    {
        SubmitCallable("submitCountingMiniGameAttempt", attempt, () => fallback.SubmitCountingMiniGameAttempt(attempt));
    }

    public void SubmitColorMiniGameAttempt(ColorMiniGameAttemptRecord attempt)
    {
        SubmitCallable("submitColorMiniGameAttempt", attempt, () => fallback.SubmitColorMiniGameAttempt(attempt));
    }

    void FlushQueuedSubmissions()
    {
        if (!IsConfigured() || CurriculumSessionManager.Instance == null)
            return;

        FlushQueuedFirestoreWrites();
        FlushQueuedDirectWavLmRecords();
        FlushPendingServerPronunciationPolls();
        if (string.IsNullOrWhiteSpace(functionsBaseUrl))
            return;

        string raw = PlayerPrefs.GetString(QueueKey, "");
        if (string.IsNullOrEmpty(raw))
            return;

        PlayerPrefs.DeleteKey(QueueKey);
        PlayerPrefs.Save();
        foreach (string entry in raw.Split(new[] { "\n---\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = entry.IndexOf('\n');
            if (separator <= 0)
                continue;

            string functionName = entry.Substring(0, separator);
            string body = entry.Substring(separator + 1);
            CurriculumSessionManager.Instance.StartCoroutine(PostQueuedCallable(functionName, body));
        }
    }

    IEnumerator PostQueuedCallable(string functionName, string body)
    {
        string url = $"{functionsBaseUrl}/{functionName}";
        using UnityWebRequest request = new UnityWebRequest(url, "POST");
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        request.uploadHandler = new UploadHandlerRaw(bytes);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", $"Bearer {idToken}");
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[FirebaseCurriculumProvider] queued {functionName} failed: {DescribeRequestFailure(request)}");
            QueueSubmission(functionName, body);
        }
        else
        {
            queuedRetryFailureStreak = 0;
        }
    }

    void QueueSubmission(string functionName, string body)
    {
        if (string.IsNullOrWhiteSpace(functionName) || string.IsNullOrWhiteSpace(body))
            return;

        string existing = PlayerPrefs.GetString(QueueKey, "");
        string entry = $"{functionName}\n{body}";
        var entries = new List<string>(existing.Split(new[] { "\n---\n" }, StringSplitOptions.RemoveEmptyEntries));
        if (!entries.Contains(entry))
            entries.Add(entry);

        string next = string.Join("\n---\n", entries);
        int dropped = 0;
        while (entries.Count > MaxQueuedSubmissions || Encoding.UTF8.GetByteCount(next) > MaxQueuedSubmissionBytes)
        {
            entries.RemoveAt(0);
            dropped++;
            next = string.Join("\n---\n", entries);
        }
        if (dropped > 0)
            Debug.LogError($"[FirebaseCurriculumProvider] Evidence outbox reached its safety cap; dropped {dropped} oldest submission(s). Investigate prolonged connectivity failure.");
        PlayerPrefs.SetString(QueueKey, next);
        PlayerPrefs.Save();
        ScheduleQueuedRetry();
    }

    void ScheduleQueuedRetry()
    {
        if (queuedRetryScheduled || CurriculumSessionManager.Instance == null || !IsConfigured())
            return;
        queuedRetryScheduled = true;
        CurriculumSessionManager.Instance.StartCoroutine(RetryQueuedSubmissionsAfterBackoff());
    }

    IEnumerator RetryQueuedSubmissionsAfterBackoff()
    {
        int exponent = Mathf.Clamp(queuedRetryFailureStreak, 0, 5);
        float delay = Mathf.Min(QueuedRetryMaxSeconds, QueuedRetryBaseSeconds * Mathf.Pow(2f, exponent));
        yield return new WaitForSecondsRealtime(delay);
        queuedRetryScheduled = false;
        queuedRetryFailureStreak = Mathf.Min(queuedRetryFailureStreak + 1, 6);
        FlushQueuedSubmissions();
    }

    bool IsConfigured()
    {
        return !string.IsNullOrWhiteSpace(projectId)
            && !string.IsNullOrWhiteSpace(schoolId)
            && !string.IsNullOrWhiteSpace(classId)
            && !string.IsNullOrWhiteSpace(idToken);
    }

    void FlushQueuedFirestoreWrites()
    {
        string raw = PlayerPrefs.GetString(FirestoreQueueKey, "");
        if (string.IsNullOrEmpty(raw))
            return;

        PlayerPrefs.DeleteKey(FirestoreQueueKey);
        PlayerPrefs.Save();
        foreach (string entry in raw.Split(new[] { "\n---\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = entry.IndexOf('\n');
            if (separator <= 0)
                continue;

            string url = entry.Substring(0, separator);
            string body = entry.Substring(separator + 1);
            if (!IsQueuedFirestoreWriteForCurrentStudent(url))
            {
                Debug.Log($"[FirebaseCurriculumProvider] Dropping stale queued Firestore write for another student/session: {DescribeQueuedFirestoreWrite(url)}");
                continue;
            }

            CurriculumSessionManager.Instance.StartCoroutine(PostQueuedFirestoreDocument(url, body));
        }
    }

    IEnumerator PostQueuedFirestoreDocument(string url, string body)
    {
        using UnityWebRequest request = new UnityWebRequest(url, "PATCH");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", $"Bearer {idToken}");
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[FirebaseCurriculumProvider] Queued Firestore write failed: {DescribeRequestFailure(request)}");
            QueueFirestoreWrite(url, body);
        }
    }

    void QueueFirestoreWrite(string url, string body)
    {
        string existing = PlayerPrefs.GetString(FirestoreQueueKey, "");
        string next = string.IsNullOrEmpty(existing)
            ? $"{url}\n{body}"
            : $"{existing}\n---\n{url}\n{body}";
        PlayerPrefs.SetString(FirestoreQueueKey, next);
        PlayerPrefs.Save();
    }

    bool IsQueuedFirestoreWriteForCurrentStudent(string url)
    {
        CurriculumSessionManager session = CurriculumSessionManager.Instance;
        if (session == null || !session.HasStudentSession)
            return false;

        string marker = $"/documents/schools/{EscapePathPart(session.activeSchoolId)}/students/{EscapePathPart(session.activeStudentId)}/";
        return !string.IsNullOrWhiteSpace(url) &&
            url.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static string DescribeQueuedFirestoreWrite(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "empty-url";

        const string marker = "/documents/";
        int index = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return index >= 0 ? url.Substring(index + marker.Length) : url;
    }

    static string EscapePathPart(string value)
    {
        return Uri.EscapeDataString(value ?? "");
    }

    static string TrimSlash(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "" : value.TrimEnd('/');
    }

#pragma warning disable 0649
    [Serializable]
    sealed class BuddyLearnerContextEnvelope
    {
        public BuddyLearnerContextResult result;
    }

    [Serializable]
    sealed class BuddyLearnerContextResult
    {
        public BuddyLearnerStateRecord clientLearnerState;
    }

    [Serializable]
    sealed class DirectWavLmQueueEnvelope
    {
        public WordCastRecord record;
        public string wavFileName;
    }

    [Serializable]
    sealed class WavLmDirectResponse
    {
        public string targetText;
        public string phonemeText;
        public List<string> expectedPhonemes;
        public List<string> observedPhonemes;
        public float score;
        public float modelConfidence;
        public List<WavLmAlignmentItem> alignment;
        public List<WavLmAlignmentItem> phonemeIssues;
        public string message;
    }

    [Serializable]
    sealed class WavLmAlignmentItem
    {
        public string expected;
        public string observed;
        public string status;
        public float confidence;
    }

    [Serializable]
    sealed class FirestoreWordCastDocument
    {
        public string name;
        public FirestoreWordCastFields fields;

        public WordCastRecord ToRecord(WordCastRecord fallback)
        {
            if (fields == null)
                return fallback;

            return new WordCastRecord
            {
                eventId = fields.eventId?.AsString() ?? fallback?.eventId,
                studentId = fields.studentId?.AsString() ?? fallback?.studentId,
                classId = fields.classId?.AsString() ?? fallback?.classId,
                schoolId = fields.schoolId?.AsString() ?? fallback?.schoolId,
                missionId = fields.missionId?.AsString() ?? fallback?.missionId,
                word = fields.word?.AsString() ?? fallback?.word,
                success = fields.success?.booleanValue ?? fallback?.success ?? false,
                specialMatch = fields.specialMatch?.booleanValue ?? fallback?.specialMatch ?? false,
                responseSeconds = fields.responseSeconds?.AsFloat() ?? fallback?.responseSeconds ?? 0f,
                serverAnalysisStatus = fields.serverAnalysisStatus?.AsString() ?? fallback?.serverAnalysisStatus,
                serverAnalysisJobId = fields.serverAnalysisJobId?.AsString() ?? fallback?.serverAnalysisJobId,
                audioStoragePath = fields.audioStoragePath?.AsString() ?? fallback?.audioStoragePath,
                audioContentType = fields.audioContentType?.AsString() ?? fallback?.audioContentType,
                audioDurationSeconds = fields.audioDurationSeconds?.AsFloat() ?? fallback?.audioDurationSeconds ?? 0f,
                createdAtUtc = fields.createdAtUtc?.AsString() ?? fallback?.createdAtUtc,
                pronunciationInsight = fallback?.pronunciationInsight,
                serverPronunciationInsight = fields.serverPronunciationInsight?.ToRecord(),
            };
        }
    }

    [Serializable]
    sealed class FirestoreSpokenPhraseDocument
    {
        public FirestoreSpokenPhraseFields fields;
    }

    [Serializable]
    sealed class FirestoreSpokenPhraseFields
    {
        public FirestoreValue serverAnalysisStatus;
        public FirestorePronunciationInsightValue serverPronunciationInsight;
    }

    [Serializable]
    sealed class FirestoreWordCastFields
    {
        public FirestoreValue eventId;
        public FirestoreValue studentId;
        public FirestoreValue classId;
        public FirestoreValue schoolId;
        public FirestoreValue missionId;
        public FirestoreValue word;
        public FirestoreValue success;
        public FirestoreValue specialMatch;
        public FirestoreValue responseSeconds;
        public FirestoreValue serverAnalysisStatus;
        public FirestoreValue serverAnalysisJobId;
        public FirestoreValue audioStoragePath;
        public FirestoreValue audioContentType;
        public FirestoreValue audioDurationSeconds;
        public FirestoreValue createdAtUtc;
        public FirestorePronunciationInsightValue serverPronunciationInsight;
    }

    [Serializable]
    sealed class FirestorePronunciationInsightValue
    {
        public FirestorePronunciationInsightMap mapValue;

        public PronunciationInsightRecord ToRecord()
        {
            return mapValue?.fields?.ToRecord();
        }
    }

    [Serializable]
    sealed class FirestorePronunciationInsightMap
    {
        public FirestorePronunciationInsightFields fields;
    }

    [Serializable]
    sealed class FirestorePronunciationInsightFields
    {
        public FirestoreValue providerName;
        public FirestoreValue targetWord;
        public FirestoreValue confirmedWord;
        public FirestoreValue rawRecognizedText;
        public FirestoreValue voskConfirmedWord;
        public FirestoreValue attemptedTarget;
        public FirestoreValue score;
        public FirestoreValue modelConfidence;
        public FirestoreValue hintKey;
        public FirestoreValue message;
        public FirestorePhoneticSegmentValue focusSegment;
        public FirestorePhoneticSegmentArrayValue segments;
        public FirestoreArrayValue syllableBeats;
        public FirestoreArrayValue expectedPhonemes;
        public FirestoreArrayValue observedPhonemes;
        public FirestorePhonemeAlignmentArrayValue phonemeIssues;
        public FirestorePhonemeAlignmentArrayValue phonemeAlignment;

        public PronunciationInsightRecord ToRecord()
        {
            return new PronunciationInsightRecord
            {
                providerName = providerName?.AsString() ?? "",
                targetWord = targetWord?.AsString() ?? "",
                confirmedWord = confirmedWord?.AsString() ?? "",
                rawRecognizedText = rawRecognizedText?.AsString() ?? "",
                voskConfirmedWord = voskConfirmedWord?.booleanValue ?? false,
                attemptedTarget = attemptedTarget?.booleanValue ?? false,
                score = score?.AsFloat() ?? 0f,
                modelConfidence = modelConfidence?.AsFloat() ?? 0f,
                hintKey = hintKey?.AsString() ?? "",
                message = message?.AsString() ?? "",
                focusSegment = focusSegment?.ToRecord(),
                segments = segments?.ToRecords() ?? new List<PhoneticSegmentRecord>(),
                syllableBeats = syllableBeats?.ToStringList() ?? new List<string>(),
                expectedPhonemes = expectedPhonemes?.ToStringList() ?? new List<string>(),
                observedPhonemes = observedPhonemes?.ToStringList() ?? new List<string>(),
                phonemeIssues = phonemeIssues?.ToRecords() ?? new List<PhonemeAlignmentRecord>(),
                phonemeAlignment = phonemeAlignment?.ToRecords() ?? new List<PhonemeAlignmentRecord>(),
            };
        }
    }

    [Serializable]
    sealed class FirestorePhoneticSegmentArrayValue
    {
        public FirestorePhoneticSegmentArray arrayValue;

        public List<PhoneticSegmentRecord> ToRecords()
        {
            var records = new List<PhoneticSegmentRecord>();
            if (arrayValue?.values == null)
                return records;

            foreach (FirestorePhoneticSegmentValue value in arrayValue.values)
            {
                PhoneticSegmentRecord record = value?.ToRecord();
                if (record != null)
                    records.Add(record);
            }

            return records;
        }
    }

    [Serializable]
    sealed class FirestorePhoneticSegmentArray
    {
        public FirestorePhoneticSegmentValue[] values;
    }

    [Serializable]
    sealed class FirestorePhoneticSegmentValue
    {
        public FirestorePhoneticSegmentMap mapValue;

        public PhoneticSegmentRecord ToRecord()
        {
            return mapValue?.fields?.ToRecord();
        }
    }

    [Serializable]
    sealed class FirestorePhoneticSegmentMap
    {
        public FirestorePhoneticSegmentFields fields;
    }

    [Serializable]
    sealed class FirestorePhoneticSegmentFields
    {
        public FirestoreValue spelling;
        public FirestoreValue friendlySound;
        public FirestoreValue heardSound;
        public FirestoreValue beatIndex;
        public FirestoreValue status;
        public FirestoreValue confidence;

        public PhoneticSegmentRecord ToRecord()
        {
            return new PhoneticSegmentRecord
            {
                spelling = spelling?.AsString() ?? "",
                friendlySound = friendlySound?.AsString() ?? "",
                heardSound = heardSound?.AsString() ?? "",
                beatIndex = beatIndex?.AsInt() ?? 0,
                status = status?.AsString() ?? "",
                confidence = confidence?.AsFloat() ?? 0f,
            };
        }
    }

    [Serializable]
    sealed class FirestorePhonemeAlignmentArrayValue
    {
        public FirestorePhonemeAlignmentArray arrayValue;

        public List<PhonemeAlignmentRecord> ToRecords()
        {
            var records = new List<PhonemeAlignmentRecord>();
            if (arrayValue?.values == null)
                return records;

            foreach (FirestorePhonemeAlignmentValue value in arrayValue.values)
            {
                PhonemeAlignmentRecord record = value?.ToRecord();
                if (record != null)
                    records.Add(record);
            }

            return records;
        }
    }

    [Serializable]
    sealed class FirestorePhonemeAlignmentArray
    {
        public FirestorePhonemeAlignmentValue[] values;
    }

    [Serializable]
    sealed class FirestorePhonemeAlignmentValue
    {
        public FirestorePhonemeAlignmentMap mapValue;

        public PhonemeAlignmentRecord ToRecord()
        {
            return mapValue?.fields?.ToRecord();
        }
    }

    [Serializable]
    sealed class FirestorePhonemeAlignmentMap
    {
        public FirestorePhonemeAlignmentFields fields;
    }

    [Serializable]
    sealed class FirestorePhonemeAlignmentFields
    {
        public FirestoreValue expected;
        public FirestoreValue observed;
        public FirestoreValue status;
        public FirestoreValue confidence;

        public PhonemeAlignmentRecord ToRecord()
        {
            return new PhonemeAlignmentRecord
            {
                expected = expected?.AsString() ?? "",
                observed = observed?.AsString() ?? "",
                status = status?.AsString() ?? "",
                confidence = confidence?.AsFloat() ?? 0f,
            };
        }
    }

    [Serializable]
    sealed class FirestoreMissionDocument
    {
        public string name;
        public FirestoreMissionFields fields;

        public MissionAssignment ToMission(string studentId)
        {
            if (fields == null)
                return null;

            return new MissionAssignment
            {
                missionId = LastPathSegment(name),
                studentId = studentId,
                schoolId = fields.schoolId?.stringValue,
                classId = fields.classId?.stringValue,
                date = fields.date?.stringValue,
                missionType = ParseMissionType(fields.missionType?.stringValue),
                missionDurationSeconds = fields.missionDurationSeconds?.AsInt() ?? 480,
                countingChestCount = fields.countingChestCount?.AsInt() ?? 1,
                colorChestCount = fields.colorChestCount?.AsInt() ?? 0,
                lettersForToday = fields.lettersForToday?.ToStringList() ?? new List<string>(),
                wordsForToday = fields.wordsForToday?.ToStringList() ?? new List<string>(),
                revisionLetters = fields.revisionLetters?.ToStringList() ?? new List<string>(),
                subArenas = LocalDemoCurriculumProvider.BuildDefaultSubArenas(),
            };
        }
    }

    [Serializable]
    sealed class FirestoreMissionFields
    {
        public FirestoreValue schoolId;
        public FirestoreValue classId;
        public FirestoreValue date;
        public FirestoreValue missionType;
        public FirestoreValue missionDurationSeconds;
        public FirestoreValue countingChestCount;
        public FirestoreValue colorChestCount;
        public FirestoreArrayValue lettersForToday;
        public FirestoreArrayValue wordsForToday;
        public FirestoreArrayValue revisionLetters;
    }

    [Serializable]
    sealed class FirestoreWorldGoalDocument
    {
        public string name;
        public FirestoreWorldGoalFields fields;

        public WorldGoalAssignment ToGoal(string studentId)
        {
            if (fields == null)
                return null;

            return new WorldGoalAssignment
            {
                goalId = LastPathSegment(name),
                studentId = string.IsNullOrWhiteSpace(fields.studentId?.stringValue) ? studentId : fields.studentId.stringValue,
                schoolId = fields.schoolId?.stringValue,
                classId = fields.classId?.stringValue,
                weekStart = fields.weekStart?.stringValue,
                targetAreaId = fields.targetAreaId?.stringValue,
                targetGymId = fields.targetGymId?.stringValue,
                focusGrammarPatterns = fields.focusGrammarPatterns?.ToStringList() ?? new List<string>(),
                focusVocabulary = fields.focusVocabulary?.ToStringList() ?? new List<string>(),
                dueDate = fields.dueDate?.stringValue,
                rewardCoins = fields.rewardCoins?.AsInt() ?? 25,
                schoolTimeZone = string.IsNullOrWhiteSpace(fields.schoolTimeZone?.stringValue) ? "Asia/Kolkata" : fields.schoolTimeZone.stringValue,
                assignedAtUtc = fields.assignedAtUtc?.stringValue,
                createdByTeacherId = fields.createdByTeacherId?.stringValue,
            };
        }
    }

    [Serializable]
    sealed class FirestoreWorldGoalFields
    {
        public FirestoreValue schoolId;
        public FirestoreValue classId;
        public FirestoreValue studentId;
        public FirestoreValue weekStart;
        public FirestoreValue targetAreaId;
        public FirestoreValue targetGymId;
        public FirestoreArrayValue focusGrammarPatterns;
        public FirestoreArrayValue focusVocabulary;
        public FirestoreValue dueDate;
        public FirestoreValue rewardCoins;
        public FirestoreValue schoolTimeZone;
        public FirestoreValue assignedAtUtc;
        public FirestoreValue createdByTeacherId;
    }

    [Serializable]
    sealed class FirestoreValue
    {
        public string stringValue;
        public string integerValue;
        public double doubleValue;
        public bool booleanValue;

        public string AsString()
        {
            if (!string.IsNullOrEmpty(stringValue))
                return stringValue;
            if (!string.IsNullOrEmpty(integerValue))
                return integerValue;
            if (Math.Abs(doubleValue) > double.Epsilon)
                return doubleValue.ToString(CultureInfo.InvariantCulture);
            return "";
        }

        public int? AsInt()
        {
            if (!string.IsNullOrEmpty(integerValue) && int.TryParse(integerValue, out int parsed))
                return parsed;
            if (doubleValue > 0)
                return Mathf.RoundToInt((float)doubleValue);
            return null;
        }

        public float? AsFloat()
        {
            if (Math.Abs(doubleValue) > double.Epsilon)
                return (float)doubleValue;
            if (!string.IsNullOrEmpty(integerValue) &&
                long.TryParse(integerValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integer))
                return integer;
            return null;
        }
    }

    [Serializable]
    sealed class FirestoreArrayValue
    {
        public FirestoreArray arrayValue;

        public List<string> ToStringList()
        {
            var result = new List<string>();
            if (arrayValue?.values == null)
                return result;

            foreach (FirestoreValue value in arrayValue.values)
            {
                if (!string.IsNullOrWhiteSpace(value?.stringValue))
                    result.Add(value.stringValue.ToUpperInvariant());
            }

            return result;
        }
    }

    [Serializable]
    sealed class FirestoreArray
    {
        public FirestoreValue[] values;
    }
#pragma warning restore 0649

    static MissionType ParseMissionType(string value)
    {
        if (string.Equals(value, "revision", StringComparison.OrdinalIgnoreCase))
            return MissionType.Revision;
        if (string.Equals(value, "test", StringComparison.OrdinalIgnoreCase))
            return MissionType.Test;
        return MissionType.Practice;
    }

    static string LastPathSegment(string value)
    {
        if (string.IsNullOrEmpty(value))
            return DateTime.UtcNow.ToString("yyyy-MM-dd");
        int slash = value.LastIndexOf('/');
        return slash >= 0 ? value.Substring(slash + 1) : value;
    }
}
