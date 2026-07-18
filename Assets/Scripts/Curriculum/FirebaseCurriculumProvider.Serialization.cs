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
    static string CollectionNameFor(string functionName)
    {
        switch (functionName)
        {
            case "submitRunSession": return "runSessions";
            case "submitLetterAttempt": return "letterAttempts";
            case "submitWordCast": return "wordCastEvents";
            case "submitSpokenPhraseEvent": return "spokenPhraseEvents";
            case "submitWrittenPhraseEvent": return "writtenPhraseEvents";
            case "submitGrammarBattleEvent": return "grammarBattleEvents";
            case "submitBuddyConversationTurn": return "buddyConversationTurns";
            case "submitBuddyLearningAttempt": return "buddyLearningAttempts";
            case "submitBuddyLearningSession": return "buddyLearningSessions";
            case "submitBuddyLearnerProfile": return "buddyLearnerProfiles";
            case "submitGymAttempt": return "gymAttempts";
            case "submitAcceptedTemplate": return "acceptedHandwritingTemplates";
            case "submitServerAnalysisJob": return "analysisJobs";
            case "submitCountingMiniGameAttempt": return "countingMiniGameAttempts";
            case "submitColorMiniGameAttempt": return "colorMiniGameAttempts";
            default: return "";
        }
    }

    static string DocumentIdFor(string functionName, object record)
    {
        switch (functionName)
        {
            case "submitWordCast": return StringField(record, "eventId");
            case "submitSpokenPhraseEvent": return StringField(record, "eventId");
            case "submitWrittenPhraseEvent": return StringField(record, "eventId");
            case "submitGrammarBattleEvent": return StringField(record, "eventId");
            case "submitBuddyConversationTurn": return StringField(record, "eventId");
            case "submitBuddyLearningAttempt": return StringField(record, "eventId");
            case "submitBuddyLearningSession": return StringField(record, "sessionId");
            case "submitBuddyLearnerProfile": return StringField(record, "profileId");
            case "submitGymAttempt": return StringField(record, "attemptId");
            case "submitRunSession": return StringField(record, "sessionId");
            case "submitAcceptedTemplate": return StringField(record, "templateId");
            case "submitServerAnalysisJob": return StringField(record, "jobId");
            case "submitCountingMiniGameAttempt": return StringField(record, "attemptId");
            case "submitColorMiniGameAttempt": return StringField(record, "attemptId");
            default: return Guid.NewGuid().ToString("N");
        }
    }

    static string StudentIdFor(object record)
    {
        return StringField(record, "studentId");
    }

    static ServerAnalysisJobRecord BuildServerAnalysisJob(
        string functionName,
        object record,
        string sourceCollection,
        string sourceRecordId)
    {
        if (record == null)
            return null;

        string jobId = StringField(record, "serverAnalysisJobId");
        if (string.IsNullOrWhiteSpace(jobId))
            return null;

        string status = StringField(record, "serverAnalysisStatus");
        if (!string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase))
            return null;

        string kind;
        string targetText;
        switch (functionName)
        {
            case "submitWordCast":
                kind = "pronunciation";
                targetText = StringField(record, "word");
                break;
            case "submitCountingMiniGameAttempt":
                kind = "pronunciation";
                targetText = CountingNumberUtility.ToWord(IntField(record, "selectedCount"));
                break;
            case "submitColorMiniGameAttempt":
                kind = "pronunciation";
                targetText = StringField(record, "selectedColor");
                break;
            case "submitAcceptedTemplate":
                kind = "handwriting";
                targetText = StringField(record, "letter");
                break;
            default:
                return null;
        }

        return new ServerAnalysisJobRecord
        {
            jobId = jobId,
            studentId = StringField(record, "studentId"),
            classId = StringField(record, "classId"),
            schoolId = StringField(record, "schoolId"),
            missionId = StringField(record, "missionId"),
            analysisKind = kind,
            status = "pending",
            sourceCollection = sourceCollection,
            sourceRecordId = sourceRecordId,
            targetText = targetText,
            audioStoragePath = StringField(record, "audioStoragePath"),
            onDeviceAnalysisProvider = StringField(record, "onDeviceAnalysisProvider"),
            analysisMode = StringField(record, "analysisMode"),
            createdAtUtc = StringField(record, "createdAtUtc"),
        };
    }

    static string StringField(object record, string fieldName)
    {
        FieldInfo field = record?.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
        return field?.GetValue(record) as string ?? "";
    }

    static void SetStringField(object record, string fieldName, string value)
    {
        FieldInfo field = record?.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
        if (field != null && field.FieldType == typeof(string))
            field.SetValue(record, value ?? "");
    }

    static void SetBoolField(object record, string fieldName, bool value)
    {
        FieldInfo field = record?.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
        if (field != null && field.FieldType == typeof(bool))
            field.SetValue(record, value);
    }

    static void MarkServerAnalysisFailed(object record)
    {
        SetStringField(record, "serverAnalysisStatus", "failed");
    }

    static bool HasPendingServerAnalysis(object record)
    {
        return string.Equals(StringField(record, "serverAnalysisStatus"), "pending", StringComparison.OrdinalIgnoreCase);
    }

    static byte[] BytesField(object record, string fieldName)
    {
        FieldInfo field = record?.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
        return field?.GetValue(record) as byte[];
    }

    static int IntField(object record, string fieldName)
    {
        FieldInfo field = record?.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
        return field != null && field.FieldType == typeof(int) ? (int)field.GetValue(record) : 0;
    }

    static string FirestoreFieldsJson(object value)
    {
        if (value == null)
            return "{}";

        var parts = new List<string>();
        FieldInfo[] fields = value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public);
        foreach (FieldInfo field in fields)
        {
            if (field.IsNotSerialized)
                continue;

            object fieldValue = field.GetValue(value);
            if (fieldValue == null)
                continue;

            string encoded = FirestoreValueJson(fieldValue);
            if (!string.IsNullOrEmpty(encoded))
                parts.Add($"\"{EscapeJson(field.Name)}\":{encoded}");
        }
        return "{" + string.Join(",", parts) + "}";
    }

    static string FirestoreValueJson(object value)
    {
        if (value == null)
            return "";

        Type type = value.GetType();
        if (value is string text)
            return $"{{\"stringValue\":\"{EscapeJson(text)}\"}}";
        if (value is bool boolean)
            return $"{{\"booleanValue\":{boolean.ToString().ToLowerInvariant()}}}";
        if (value is int || value is long || value is short || value is byte)
            return $"{{\"integerValue\":\"{Convert.ToInt64(value, CultureInfo.InvariantCulture)}\"}}";
        if (value is float || value is double || value is decimal)
            return $"{{\"doubleValue\":{Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)}}}";
        if (value is IList list)
        {
            var values = new List<string>();
            foreach (object item in list)
            {
                string encoded = FirestoreValueJson(item);
                if (!string.IsNullOrEmpty(encoded))
                    values.Add(encoded);
            }
            return "{\"arrayValue\":{\"values\":[" + string.Join(",", values) + "]}}";
        }

        if (type.IsClass)
            return "{\"mapValue\":{\"fields\":" + FirestoreFieldsJson(value) + "}}";

        return "";
    }

    static string EscapeJson(string value)
    {
        return string.IsNullOrEmpty(value)
            ? ""
            : value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }

    static string EscapeUrl(string value)
    {
        return UnityWebRequest.EscapeURL(value ?? "").Replace("+", "%20");
    }

    static string DescribeRequestFailure(UnityWebRequest request)
    {
        if (request == null)
            return "request unavailable";

        string error = request.error ?? "request failed";
        string body = request.downloadHandler?.text;
        if (string.IsNullOrWhiteSpace(body))
            return error;

        body = body.Replace("\r", " ").Replace("\n", " ").Trim();
        if (body.Length > 300)
            body = body.Substring(0, 300) + "...";
        return $"{error}; body={body}";
    }

    static bool IsTransientCallableFailure(UnityWebRequest request)
    {
        if (request == null)
            return true;

        long status = request.responseCode;
        return status == 0 || status == 408 || status == 429 || status >= 500 ||
               request.result == UnityWebRequest.Result.ConnectionError ||
               request.result == UnityWebRequest.Result.DataProcessingError;
    }

    static string ClipLogBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "";

        string flattened = body.Replace("\r", " ").Replace("\n", " ").Trim();
        return flattened.Length <= 500 ? flattened : flattened.Substring(0, 500) + "...";
    }

    static string DefaultStorageBucket(string projectId)
    {
        return string.IsNullOrWhiteSpace(projectId) ? "" : $"{projectId.Trim()}.firebasestorage.app";
    }

    static IEnumerable<string> CandidateStorageBuckets(string configuredBucket, string projectId)
    {
        var buckets = new List<string>();
        AddBucketCandidate(buckets, configuredBucket);

        string trimmedProjectId = projectId?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmedProjectId))
        {
            AddBucketCandidate(buckets, $"{trimmedProjectId}.firebasestorage.app");
            AddBucketCandidate(buckets, $"{trimmedProjectId}.appspot.com");
        }

        return buckets;
    }

    static void AddBucketCandidate(List<string> buckets, string bucket)
    {
        string normalized = bucket?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        foreach (string existing in buckets)
        {
            if (string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase))
                return;
        }

        buckets.Add(normalized);
    }

    static PronunciationInsightRecord BuildServerPronunciationInsight(WavLmDirectResponse response, string fallbackWord)
    {
        string target = string.IsNullOrWhiteSpace(response.targetText) ? fallbackWord : response.targetText;
        return new PronunciationInsightRecord
        {
            providerName = "Server Pronunciation Review",
            targetWord = SpellRegistry.NormalizeWord(target),
            confirmedWord = "",
            rawRecognizedText = response.phonemeText ?? "",
            voskConfirmedWord = false,
            attemptedTarget = true,
            score = Mathf.Clamp01(response.score),
            modelConfidence = Mathf.Clamp01(response.modelConfidence),
            hintKey = response.score >= 0.65f ? PronunciationHintKey.GreatTry.ToString() : PronunciationHintKey.TryAgain.ToString(),
            message = string.IsNullOrWhiteSpace(response.message)
                ? "Server pronunciation review completed."
                : response.message,
            expectedPhonemes = response.expectedPhonemes ?? new List<string>(),
            observedPhonemes = response.observedPhonemes ?? new List<string>(),
            phonemeIssues = ConvertAlignment(response.phonemeIssues),
            phonemeAlignment = ConvertAlignment(response.alignment),
            segments = ConvertAlignmentToSegments(response.alignment),
            focusSegment = FirstPracticeSegment(response.alignment),
        };
    }

    static List<PhonemeAlignmentRecord> ConvertAlignment(List<WavLmAlignmentItem> source)
    {
        var records = new List<PhonemeAlignmentRecord>();
        if (source == null)
            return records;

        foreach (WavLmAlignmentItem item in source)
        {
            if (item == null)
                continue;
            records.Add(new PhonemeAlignmentRecord
            {
                expected = item.expected ?? "",
                observed = item.observed ?? "",
                status = item.status ?? "",
                confidence = Mathf.Clamp01(item.confidence),
            });
        }

        return records;
    }

    static List<PhoneticSegmentRecord> ConvertAlignmentToSegments(List<WavLmAlignmentItem> alignment)
    {
        var segments = new List<PhoneticSegmentRecord>();
        if (alignment == null)
            return segments;

        foreach (WavLmAlignmentItem item in alignment)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.expected))
                continue;
            segments.Add(ToPhoneticSegment(item));
        }

        return segments;
    }

    static PhoneticSegmentRecord FirstPracticeSegment(List<WavLmAlignmentItem> alignment)
    {
        if (alignment == null)
            return null;

        foreach (WavLmAlignmentItem item in alignment)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.expected))
                continue;
            if (!string.Equals(item.status, "matched", StringComparison.OrdinalIgnoreCase))
                return ToPhoneticSegment(item);
        }

        return null;
    }

    static PhoneticSegmentRecord ToPhoneticSegment(WavLmAlignmentItem item)
    {
        string status = item.status ?? "";
        string segmentStatus = string.Equals(status, "matched", StringComparison.OrdinalIgnoreCase)
            ? PhoneticSegmentStatus.Matched.ToString()
            : string.Equals(status, "missing", StringComparison.OrdinalIgnoreCase)
                ? PhoneticSegmentStatus.Missing.ToString()
                : PhoneticSegmentStatus.NeedsPractice.ToString();

        return new PhoneticSegmentRecord
        {
            spelling = item.expected ?? "",
            friendlySound = item.expected ?? "",
            heardSound = item.observed ?? "",
            beatIndex = 0,
            status = segmentStatus,
            confidence = Mathf.Clamp01(item.confidence),
        };
    }
}
