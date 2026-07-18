using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;


public sealed partial class CurriculumSessionManager
{
    public void RecordLetterAttempt(
        char letter,
        bool confident,
        int attempts,
        bool gifted,
        float confidenceScore = 0f,
        HandwritingDiagnosticSummary handwritingDiagnostics = null,
        List<List<Vector2>> rawStrokes = null,
        PDollarRecognizer.RecognitionResult recognition = default,
        string assessmentOutcome = "",
        string assessmentReason = "")
    {
        string key = char.ToUpperInvariant(letter).ToString();
        GrammarWorldProgressData buddyProgress = GrammarWorldProgressService.Instance != null
            ? GrammarWorldProgressService.Instance.Data
            : null;
        GrammarMapAreaState buddyArea = ResolveArea(buddyProgress?.currentAreaId ?? "");
        bool handwritingAccepted = handwritingDiagnostics != null ? handwritingDiagnostics.accepted : confident;
        CaptureBuddyLearningAttempt(new BuddyLearningAttemptRecord
        {
            sourceRecordType = "letter_attempt",
            areaId = buddyProgress?.currentAreaId ?? "",
            zoneKind = buddyArea != null ? buddyArea.sceneKind.ToString() : "",
            activityType = "handwriting_letter",
            modality = BuddyLearningModality.Writing.ToString(),
            inputSource = "handwriting",
            contentId = $"letter:{key.ToLowerInvariant()}",
            questionPrompt = $"Write the letter {key}",
            expectedResponse = key,
            submittedResponse = handwritingDiagnostics != null
                ? FirstNonEmpty(handwritingDiagnostics.neuralRecognizedName, handwritingDiagnostics.recognitionDecision)
                : "",
            grammarPattern = GrammarPhrasePattern.LetterOnly.ToString(),
            conceptId = GrammarConceptId.Alphabet.ToString(),
            grimoireReference = BuildGrimoireReference(GrammarConceptId.Alphabet.ToString()),
            errorTags = handwritingDiagnostics != null ? CopyStrings(handwritingDiagnostics.tags) : new List<string>(),
            correct = handwritingAccepted,
            hintCount = gifted || handwritingDiagnostics?.materiallyInputAssisted == true ? 1 : 0,
            highestHintLevel = gifted
                ? "GiftedLetter"
                : handwritingDiagnostics?.materiallyInputAssisted == true ? "SafeZoneAssist" : "",
            buddyAssistMode = ResolveBuddyAssistMode(buddyArea != null ? buddyArea.sceneKind : SemanticZoneKind.Town, buddyProgress?.currentAreaId ?? ""),
            confidenceScore = handwritingDiagnostics != null && handwritingDiagnostics.assessmentConfidence > 0f
                ? handwritingDiagnostics.assessmentConfidence
                : confidenceScore,
            recognitionConfidence = handwritingDiagnostics != null ? handwritingDiagnostics.neuralConfidence : 0f,
            handwritingDiagnostics = handwritingDiagnostics,
        });

        if (!ShouldSubmitTeacherAnalytics)
            return;

        if (!IsLetterAllowed(letter))
            return;

        lettersPracticed.Add(key);
        attemptsTotal += Mathf.Max(1, attempts);
        attemptsSampleCount++;
        if (confidenceScore > 0f)
        {
            confidenceTotal += confidenceScore;
            confidenceSampleCount++;
        }

        bool captureRejectedStrokes = collectRejectedHandwritingEvidence && !handwritingAccepted && rawStrokes != null;
        HandwritingSampleRecord captureSample = recognition.captureSample;
        if (captureSample != null)
        {
            captureSample.assessmentOutcome = string.IsNullOrWhiteSpace(assessmentOutcome)
                ? handwritingDiagnostics?.assessmentOutcome ?? ""
                : assessmentOutcome;
            captureSample.targetWord = handwritingDiagnostics?.targetWord ?? "";
            captureSample.letterIndex = handwritingDiagnostics?.letterIndex ?? -1;
            captureSample.attemptNumber = Mathf.Max(1, attempts);
        }
        List<HandwritingPointRecord> evidencePoints = captureRejectedStrokes
            ? captureSample != null
                ? BuildBoundedHandwritingPoints(captureSample, 256)
                : BuildBoundedHandwritingPoints(rawStrokes, 256)
            : new List<HandwritingPointRecord>();
        provider?.SubmitLetterAttempt(new LetterAttemptRecord
        {
            sampleSchemaVersion = captureSample?.schemaVersion ?? "",
            sampleId = captureSample?.sampleId ?? "",
            writerId = captureSample?.writerId ?? "",
            writerAgeBand = captureSample?.writerAgeBand ?? "",
            handedness = captureSample?.handedness ?? "unknown",
            collectionCohort = captureSample?.collectionCohort ?? "",
            consentReference = captureSample?.consentReference ?? "",
            captureSessionId = captureSample?.sessionId ?? "",
            captureSource = captureSample?.source ?? "",
            rawCoordinateSystem = captureSample?.rawCoordinateSystem ?? "",
            normalizedCoordinateSystem = captureSample?.normalizedCoordinateSystem ?? "",
            captureStartedAtUtc = captureSample?.captureStartedAtUtc ?? "",
            captureDurationMs = captureSample?.durationMs ?? 0L,
            captureDevice = captureSample?.device,
            attemptId = Guid.NewGuid().ToString("N"),
            studentId = CurrentMission.studentId,
            classId = CurrentMission.classId,
            schoolId = CurrentMission.schoolId,
            missionId = CurrentMission.missionId,
            letter = key,
            confident = confident,
            attempts = Mathf.Max(1, attempts),
            gifted = gifted,
            confidenceScore = confidenceScore,
            handwritingDiagnostics = handwritingDiagnostics,
            neuralRecognizedName = handwritingDiagnostics != null ? handwritingDiagnostics.neuralRecognizedName : "",
            neuralConfidence = handwritingDiagnostics != null ? handwritingDiagnostics.neuralConfidence : 0f,
            combinedConfidence = handwritingDiagnostics != null ? handwritingDiagnostics.combinedConfidence : confidenceScore,
            recognizerAgreement = handwritingDiagnostics == null || handwritingDiagnostics.recognizerAgreement,
            recognitionDecision = handwritingDiagnostics != null ? handwritingDiagnostics.recognitionDecision : "",
            assessmentOutcome = string.IsNullOrWhiteSpace(assessmentOutcome)
                ? handwritingDiagnostics?.assessmentOutcome ?? ""
                : assessmentOutcome,
            assessmentReason = string.IsNullOrWhiteSpace(assessmentReason)
                ? handwritingDiagnostics?.assessmentReason ?? ""
                : assessmentReason,
            pDollarScore = recognition.score,
            broadRecognizedName = handwritingDiagnostics?.broadRecognizedName ?? "",
            broadRecognitionScore = handwritingDiagnostics?.broadRecognitionScore ?? 0f,
            inputAssisted = handwritingDiagnostics?.inputAssisted == true,
            materiallyInputAssisted = handwritingDiagnostics?.materiallyInputAssisted == true,
            inputAssistFraction = handwritingDiagnostics?.inputAssistFraction ?? 0f,
            inputAssistMeanDistance = handwritingDiagnostics?.inputAssistMeanDistance ?? 0f,
            inputAssistMaxDistance = handwritingDiagnostics?.inputAssistMaxDistance ?? 0f,
            calibrationSeparation = recognition.calibrationSeparation,
            calibrationReliable = recognition.calibrationReliable,
            rawStrokeCaptured = evidencePoints.Count > 0,
            rawStrokePointCount = evidencePoints.Count,
            rawStrokeRetentionPolicy = evidencePoints.Count > 0
                ? "school_consented_rejected_sample_until_policy_deletion"
                : "not_collected",
            normalizedCoordinateSpace = evidencePoints.Count > 0 ? "attempt_bounds_v1" : "",
            points = evidencePoints,
            createdAtUtc = DateTime.UtcNow.ToString("o"),
        });
    }

    public static List<HandwritingPointRecord> BuildBoundedHandwritingPoints(
        List<List<Vector2>> strokes,
        int maximumPoints)
    {
        var flattened = new List<HandwritingPointRecord>();
        if (strokes == null)
            return flattened;

        int cap = Mathf.Max(8, maximumPoints);
        var validStrokes = new List<(int strokeId, List<Vector2> points)>();
        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minY = float.MaxValue;
        float maxY = float.MinValue;
        int totalPoints = 0;
        for (int strokeIndex = 0; strokeIndex < strokes.Count; strokeIndex++)
        {
            if (validStrokes.Count >= cap)
                break;
            List<Vector2> stroke = strokes[strokeIndex];
            if (stroke == null)
                continue;
            var valid = new List<Vector2>(stroke.Count);
            foreach (Vector2 point in stroke)
            {
                if (float.IsNaN(point.x) || float.IsInfinity(point.x) ||
                    float.IsNaN(point.y) || float.IsInfinity(point.y))
                    continue;
                valid.Add(point);
                minX = Mathf.Min(minX, point.x);
                maxX = Mathf.Max(maxX, point.x);
                minY = Mathf.Min(minY, point.y);
                maxY = Mathf.Max(maxY, point.y);
            }
            if (valid.Count == 0)
                continue;
            validStrokes.Add((strokeIndex, valid));
            totalPoints += valid.Count;
        }

        if (validStrokes.Count == 0)
            return flattened;

        int[] allocations = new int[validStrokes.Count];
        int allocated = 0;
        for (int i = 0; i < validStrokes.Count; i++)
        {
            int count = validStrokes[i].points.Count;
            allocations[i] = totalPoints <= cap
                ? count
                : Mathf.Min(count, Mathf.Max(1, Mathf.FloorToInt(cap * (count / (float)totalPoints))));
            if (count > 1 && cap >= validStrokes.Count * 2)
                allocations[i] = Mathf.Max(2, allocations[i]);
            allocated += allocations[i];
        }

        while (allocated > cap)
        {
            int candidate = -1;
            int largest = 0;
            for (int i = 0; i < allocations.Length; i++)
            {
                int floor = validStrokes[i].points.Count > 1 && cap >= validStrokes.Count * 2 ? 2 : 1;
                if (allocations[i] > floor && allocations[i] > largest)
                {
                    largest = allocations[i];
                    candidate = i;
                }
            }
            if (candidate < 0)
                break;
            allocations[candidate]--;
            allocated--;
        }

        while (allocated < Mathf.Min(cap, totalPoints))
        {
            int candidate = -1;
            int largestRemainder = 0;
            for (int i = 0; i < allocations.Length; i++)
            {
                int remainder = validStrokes[i].points.Count - allocations[i];
                if (remainder > largestRemainder)
                {
                    largestRemainder = remainder;
                    candidate = i;
                }
            }
            if (candidate < 0)
                break;
            allocations[candidate]++;
            allocated++;
        }

        float width = Mathf.Max(0.0001f, maxX - minX);
        float height = Mathf.Max(0.0001f, maxY - minY);
        int order = 0;
        for (int strokeIndex = 0; strokeIndex < validStrokes.Count; strokeIndex++)
        {
            List<Vector2> source = validStrokes[strokeIndex].points;
            int targetCount = allocations[strokeIndex];
            for (int i = 0; i < targetCount; i++)
            {
                int sourceIndex = targetCount == 1
                    ? 0
                    : Mathf.RoundToInt(i * (source.Count - 1f) / (targetCount - 1f));
                Vector2 point = source[sourceIndex];
                flattened.Add(new HandwritingPointRecord
                {
                    x = point.x,
                    y = point.y,
                    nx = Mathf.Clamp01((point.x - minX) / width),
                    ny = Mathf.Clamp01((point.y - minY) / height),
                    strokeId = validStrokes[strokeIndex].strokeId,
                    order = order++
                });
            }
        }
        return flattened;
    }

    public static List<HandwritingPointRecord> BuildBoundedHandwritingPoints(
        HandwritingSampleRecord sample,
        int maximumPoints)
    {
        if (sample?.points == null || sample.points.Count == 0)
            return new List<HandwritingPointRecord>();

        int maximumStrokeId = 0;
        foreach (HandwritingCapturePoint point in sample.points)
            if (point != null)
                maximumStrokeId = Mathf.Max(maximumStrokeId, point.strokeId);
        var strokes = new List<List<Vector2>>(maximumStrokeId + 1);
        for (int i = 0; i <= maximumStrokeId; i++)
            strokes.Add(new List<Vector2>());
        foreach (HandwritingCapturePoint point in sample.points)
            if (point != null && point.strokeId >= 0 && point.strokeId < strokes.Count)
                strokes[point.strokeId].Add(new Vector2(point.x, point.y));

        List<HandwritingPointRecord> result = BuildBoundedHandwritingPoints(strokes, maximumPoints);
        foreach (HandwritingPointRecord target in result)
        {
            HandwritingCapturePoint nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (HandwritingCapturePoint source in sample.points)
            {
                if (source == null || source.strokeId != target.strokeId)
                    continue;
                float dx = source.x - target.x;
                float dy = source.y - target.y;
                float distance = dx * dx + dy * dy;
                if (distance >= nearestDistance)
                    continue;
                nearestDistance = distance;
                nearest = source;
            }
            if (nearest == null)
                continue;
            target.canvasX = nearest.canvasX;
            target.canvasY = nearest.canvasY;
            target.tMs = nearest.tMs;
            target.deltaMs = nearest.deltaMs;
            target.pressure = nearest.pressure;
            target.altitudeAngle = nearest.altitudeAngle;
            target.azimuthAngle = nearest.azimuthAngle;
            target.pointerId = nearest.pointerId;
            target.inputType = nearest.inputType;
        }
        long previousMs = 0L;
        result.Sort((left, right) => left.order.CompareTo(right.order));
        foreach (HandwritingPointRecord point in result)
        {
            point.deltaMs = Math.Max(0L, point.tMs - previousMs);
            previousMs = point.tMs;
        }
        return result;
    }
}
