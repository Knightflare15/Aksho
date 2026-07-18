using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;


public sealed partial class CurriculumSessionManager
{
    public void NotifyServerPronunciationInsight(WordCastRecord record)
    {
        if (record?.serverPronunciationInsight == null)
            return;

        OnAzurePronunciationInsightReady?.Invoke(record.serverPronunciationInsight);

        GrammarWorldProgressData progress = GrammarWorldProgressService.Instance != null
            ? GrammarWorldProgressService.Instance.Data
            : null;
        GrammarMapAreaState currentArea = ResolveArea(progress?.currentAreaId ?? "");
        string conceptId = currentArea != null && currentArea.conceptId != GrammarConceptId.None
            ? currentArea.conceptId.ToString()
            : ResolveConceptId(progress?.currentAreaId ?? "");
        bool pronunciationAccepted = record.serverPronunciationInsight.score >= 0.65f;
        CaptureBuddyLearningAttempt(new BuddyLearningAttemptRecord
        {
            eventId = string.IsNullOrWhiteSpace(record.eventId)
                ? Guid.NewGuid().ToString("N")
                : $"{record.eventId}_server_pronunciation",
            sourceRecordType = "server_pronunciation_review",
            sourceRecordId = record.eventId ?? "",
            areaId = progress?.currentAreaId ?? "",
            zoneKind = currentArea != null ? currentArea.sceneKind.ToString() : "",
            activityType = "pronunciation_review",
            modality = BuddyLearningModality.Speaking.ToString(),
            inputSource = "server_pronunciation_review",
            contentId = string.IsNullOrWhiteSpace(record.contentId) ? $"word:{record.word}" : record.contentId,
            questionPrompt = $"Say {record.word}",
            expectedResponse = record.word ?? "",
            submittedResponse = record.serverPronunciationInsight.rawRecognizedText ?? "",
            grammarPattern = GrammarPhrasePattern.FullSentence.ToString(),
            conceptId = conceptId,
            grimoireReference = BuildGrimoireReference(conceptId),
            errorTags = BuildPronunciationErrorTags(record.serverPronunciationInsight, pronunciationAccepted),
            countsTowardMastery = false,
            correct = pronunciationAccepted,
            recognitionConfidence = record.serverPronunciationInsight.modelConfidence,
            pronunciationScore = record.serverPronunciationInsight.score,
            pronunciationInsight = record.serverPronunciationInsight,
            buddyAssistMode = ResolveBuddyAssistMode(currentArea != null ? currentArea.sceneKind : SemanticZoneKind.Route, progress?.currentAreaId ?? ""),
        });

        OnServerPronunciationInsightReady?.Invoke(record);
    }

    static float EstimateWavDurationSeconds(byte[] wavBytes)
    {
        if (wavBytes == null || wavBytes.Length <= 44)
            return 0f;

        int sampleRate = BitConverter.ToInt32(wavBytes, 24);
        short channels = BitConverter.ToInt16(wavBytes, 22);
        short bitsPerSample = BitConverter.ToInt16(wavBytes, 34);
        int bytesPerSampleFrame = Mathf.Max(1, channels * bitsPerSample / 8);
        int audioBytes = Mathf.Max(0, wavBytes.Length - 44);
        return sampleRate > 0 ? audioBytes / (sampleRate * (float)bytesPerSampleFrame) : 0f;
    }

    SemanticZoneKind CurrentPronunciationZone()
    {
        GrammarWorldProgressData progress = GrammarWorldProgressService.Instance != null
            ? GrammarWorldProgressService.Instance.Data
            : null;
        GrammarMapAreaState area = ResolveArea(progress?.currentAreaId ?? "");
        return area != null ? area.sceneKind : SemanticZoneKind.Route;
    }

    bool ShouldScheduleServerPronunciationReview(
        SemanticZoneKind zoneKind,
        PronunciationInsightResult? localInsight)
    {
        eligibleServerPronunciationReviewsThisRun++;
        bool localNeedsReview = localInsight.HasValue &&
            localInsight.Value.Score > 0f &&
            localInsight.Value.Score < 0.8f;
        bool selected = zoneKind == SemanticZoneKind.Gym ||
            localNeedsReview ||
            eligibleServerPronunciationReviewsThisRun == 1 ||
            eligibleServerPronunciationReviewsThisRun % ServerPronunciationReviewStride == 0;
        if (!selected || serverPronunciationReviewsThisRun >= MaxServerPronunciationReviewsPerRun)
            return false;

        serverPronunciationReviewsThisRun++;
        return true;
    }

    static PronunciationInsightRecord BuildPronunciationInsightRecord(PronunciationInsightResult? maybeInsight)
    {
        if (!maybeInsight.HasValue)
            return null;

        PronunciationInsightResult insight = maybeInsight.Value;
        if (string.IsNullOrWhiteSpace(insight.TargetWord) &&
            string.IsNullOrWhiteSpace(insight.RawRecognizedText) &&
            (insight.Segments == null || insight.Segments.Count == 0))
            return null;

        var record = new PronunciationInsightRecord
        {
            providerName = insight.ProviderName,
            targetWord = insight.TargetWord,
            confirmedWord = insight.ConfirmedWord,
            rawRecognizedText = insight.RawRecognizedText,
            voskConfirmedWord = insight.VoskConfirmedWord,
            attemptedTarget = insight.AttemptedTarget,
            score = insight.Score,
            modelConfidence = insight.Score,
            hintKey = insight.HintKey.ToString(),
            message = insight.Message,
            focusSegment = BuildPhoneticSegmentRecord(insight.FocusSegment),
        };

        if (insight.Segments != null)
        {
            foreach (PhoneticSoundSegment segment in insight.Segments)
                record.segments.Add(BuildPhoneticSegmentRecord(segment));
        }

        if (insight.SyllableBeats != null)
            record.syllableBeats.AddRange(insight.SyllableBeats);

        return record;
    }

    static PhoneticSegmentRecord BuildPhoneticSegmentRecord(PhoneticSoundSegment segment)
    {
        return new PhoneticSegmentRecord
        {
            spelling = segment.Spelling,
            friendlySound = segment.FriendlySound,
            heardSound = segment.HeardSound,
            beatIndex = segment.BeatIndex,
            status = segment.Status.ToString(),
            confidence = segment.Confidence,
        };
    }

    public void RecordAcceptedTemplate(
        char letter,
        List<List<Vector2>> strokes,
        PDollarRecognizer.RecognitionResult result,
        string targetWord,
        int letterIndex,
        int attemptsForLetter,
        bool gifted,
        HandwritingDiagnosticSummary handwritingDiagnostics = null)
    {
        if (!ShouldSubmitTeacherAnalytics || !collectAcceptedHandwritingEvidence || strokes == null || strokes.Count == 0)
            return;

        if (!IsLetterAllowed(letter))
            return;

        HandwritingSampleRecord captureSample = result.captureSample;
        if (captureSample != null)
        {
            captureSample.expectedLetter = char.ToUpperInvariant(letter).ToString();
            captureSample.targetWord = SpellRegistry.NormalizeWord(targetWord);
            captureSample.letterIndex = Mathf.Max(0, letterIndex);
            captureSample.attemptNumber = Mathf.Max(1, attemptsForLetter);
            captureSample.labelStatus = "gameplay_accepted";
            captureSample.assessmentOutcome = handwritingDiagnostics?.assessmentOutcome ?? "Accept";
        }
        List<HandwritingPointRecord> points = captureSample != null
            ? BuildBoundedHandwritingPoints(captureSample, 512)
            : BuildBoundedHandwritingPoints(strokes, 512);

        if (points.Count == 0)
            return;

        string templateId = Guid.NewGuid().ToString("N");

        provider?.SubmitAcceptedTemplate(new AcceptedHandwritingTemplateRecord
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
            templateId = templateId,
            studentId = CurrentMission.studentId,
            classId = CurrentMission.classId,
            schoolId = CurrentMission.schoolId,
            missionId = CurrentMission.missionId,
            letter = char.ToUpperInvariant(letter).ToString(),
            targetWord = SpellRegistry.NormalizeWord(targetWord),
            letterIndex = Mathf.Max(0, letterIndex),
            attemptsForLetter = Mathf.Max(1, attemptsForLetter),
            gifted = gifted,
            recognitionScore = result.score,
            recognizedName = result.name,
            bestCandidateName = result.bestCandidateName,
            runnerUpName = result.runnerUpName,
            runnerUpScore = result.runnerUpScore,
            scoreMargin = result.scoreMargin,
            isAmbiguous = result.isAmbiguous,
            handwritingDiagnostics = handwritingDiagnostics,
            points = points,
            rawStrokeCaptured = points.Count > 0,
            rawStrokePointCount = points.Count,
            rawStrokeRetentionPolicy = "school_evidence_180_day_default",
            normalizedCoordinateSpace = "attempt_bounds_v1",
            neuralRecognizedName = result.neuralRecognizedName,
            neuralConfidence = result.neuralConfidence,
            combinedConfidence = result.combinedConfidence,
            recognizerAgreement = result.recognizerAgreement,
            recognitionDecision = result.recognitionDecision,
            analysisMode = analysisMode.ToString(),
            onDeviceAnalysisProvider = "P$ template recognizer",
            // No server handwriting analyzer is deployed. Mark this honestly
            // instead of creating a permanent pending job that no worker can consume.
            serverAnalysisStatus = "not_requested",
            serverAnalysisJobId = "",
            createdAtUtc = DateTime.UtcNow.ToString("o"),
        });
    }

    public void RecordCountingMiniGameAttempt(
        string chestCategory,
        int targetCount,
        int selectedCount,
        string spokenNumber,
        bool countCorrect,
        bool speechProofSucceeded,
        bool hintUsed,
        int rewardAwarded,
        float responseSeconds,
        string outcomeStatus = null,
        PronunciationInsightResult? pronunciationInsight = null,
        byte[] pronunciationAudioWavBytes = null)
    {
        PronunciationInsightRecord buddyPronunciation = BuildPronunciationInsightRecord(pronunciationInsight);
        bool countingCorrect = countCorrect && speechProofSucceeded;
        CaptureBuddyLearningAttempt(new BuddyLearningAttemptRecord
        {
            sourceRecordType = "counting_minigame",
            activityType = "counting_spoken_answer",
            modality = BuddyLearningModality.Speaking.ToString(),
            inputSource = "counting_chest",
            contentId = $"counting_chest:{Mathf.Clamp(targetCount, 1, 10)}",
            questionPrompt = $"Count and say {Mathf.Clamp(targetCount, 1, 10)}",
            expectedResponse = Mathf.Clamp(targetCount, 1, 10).ToString(),
            submittedResponse = spokenNumber ?? selectedCount.ToString(),
            grammarPattern = GrammarPhrasePattern.FullSentence.ToString(),
            conceptId = "CountingVocabulary",
            grimoireReference = "grimoire:CountingVocabulary",
            errorTags = BuildErrorTags(
                countCorrect ? (speechProofSucceeded ? "" : "pronunciation_mismatch") : "count_mismatch",
                "",
                buddyPronunciation,
                countingCorrect),
            correct = countingCorrect,
            hintCount = hintUsed ? 1 : 0,
            highestHintLevel = hintUsed ? "ActivityHint" : "",
            responseSeconds = responseSeconds,
            pronunciationScore = buddyPronunciation != null ? buddyPronunciation.score : 0f,
            recognitionConfidence = buddyPronunciation != null ? buddyPronunciation.modelConfidence : 0f,
            pronunciationInsight = buddyPronunciation,
            outcome = outcomeStatus ?? "",
        });

        if (!ShouldSubmitTeacherAnalytics)
            return;

        string attemptId = Guid.NewGuid().ToString("N");
        byte[] submittedPronunciationAudio = pronunciationAudioWavBytes ?? Array.Empty<byte>();
        bool hasPronunciationAudio = submittedPronunciationAudio.Length > 44;
        bool eligibleForPronunciationAnalysis = speechProofSucceeded && ShouldRequestServerAnalysis && hasPronunciationAudio;
        bool shouldRequestPronunciationAnalysis = eligibleForPronunciationAnalysis &&
            ShouldScheduleServerPronunciationReview(CurrentPronunciationZone(), pronunciationInsight);
        if (ShouldRequestServerAnalysis && !hasPronunciationAudio)
            Debug.LogWarning("[CurriculumSessionManager] Azure pronunciation analysis skipped for counting mini-game: no captured WAV audio was available.");
        string serverJobId = shouldRequestPronunciationAnalysis ? $"pronunciation_{attemptId}" : "";
        string onDeviceProvider = pronunciationInsight.HasValue && !string.IsNullOrWhiteSpace(pronunciationInsight.Value.ProviderName)
            ? pronunciationInsight.Value.ProviderName
            : "On-device speech gate";
        string contentId = $"counting_chest:{Mathf.Clamp(targetCount, 1, 10)}";
        string attemptGroupId = BuildAttemptGroupId("counting_chest", contentId);

        provider?.SubmitCountingMiniGameAttempt(new CountingMiniGameAttemptRecord
        {
            attemptId = attemptId,
            studentId = CurrentMission.studentId,
            classId = CurrentMission.classId,
            schoolId = CurrentMission.schoolId,
            missionId = CurrentMission.missionId,
            contentId = contentId,
            inputSource = "counting_chest",
            attemptGroupId = attemptGroupId,
            attemptNumber = NextAttemptNumber(attemptGroupId),
            chestCategory = string.IsNullOrWhiteSpace(chestCategory) ? "TreasureChest" : chestCategory,
            targetCount = Mathf.Clamp(targetCount, 1, 10),
            selectedCount = Mathf.Clamp(selectedCount, 0, 10),
            spokenNumber = spokenNumber ?? "",
            countCorrect = countCorrect,
            speechProofSucceeded = speechProofSucceeded,
            hintUsed = hintUsed,
            pronunciationInsight = BuildPronunciationInsightRecord(pronunciationInsight),
            analysisMode = analysisMode.ToString(),
            onDeviceAnalysisProvider = onDeviceProvider,
            serverAnalysisStatus = shouldRequestPronunciationAnalysis
                ? "pending"
                : eligibleForPronunciationAnalysis ? "sampled_out" : "not_requested",
            serverAnalysisJobId = serverJobId,
            audioContentType = submittedPronunciationAudio.Length > 44 ? "audio/wav" : "",
            audioDurationSeconds = EstimateWavDurationSeconds(submittedPronunciationAudio),
            rawAudioCaptured = hasPronunciationAudio,
            rawAudioUploaded = false,
            rawAudioRetentionPolicy = AudioRetentionPolicy(hasPronunciationAudio, shouldRequestPronunciationAnalysis),
            pronunciationAudioWavBytes = submittedPronunciationAudio,
            outcomeStatus = string.IsNullOrWhiteSpace(outcomeStatus)
                ? BuildCountingOutcomeStatus(selectedCount, countCorrect, speechProofSucceeded)
                : outcomeStatus,
            rewardAwarded = Mathf.Max(0, rewardAwarded),
            responseSeconds = Mathf.Max(0f, responseSeconds),
            createdAtUtc = DateTime.UtcNow.ToString("o"),
        });
    }

    public void RecordColorMiniGameAttempt(
        string chestCategory,
        string targetColor,
        string selectedColor,
        string spokenColor,
        bool colorCorrect,
        bool speechProofSucceeded,
        bool hintUsed,
        int rewardAwarded,
        float responseSeconds,
        string outcomeStatus = null,
        PronunciationInsightResult? pronunciationInsight = null,
        byte[] pronunciationAudioWavBytes = null)
    {
        PronunciationInsightRecord buddyPronunciation = BuildPronunciationInsightRecord(pronunciationInsight);
        bool colorAttemptCorrect = colorCorrect && speechProofSucceeded;
        string buddyTargetColor = ColorWordUtility.Normalize(targetColor);
        CaptureBuddyLearningAttempt(new BuddyLearningAttemptRecord
        {
            sourceRecordType = "color_minigame",
            activityType = "color_spoken_answer",
            modality = BuddyLearningModality.Speaking.ToString(),
            inputSource = "color_chest",
            contentId = $"color_chest:{buddyTargetColor.ToLowerInvariant()}",
            questionPrompt = $"Choose and say {buddyTargetColor}",
            expectedResponse = buddyTargetColor,
            submittedResponse = spokenColor ?? selectedColor ?? "",
            grammarPattern = GrammarPhrasePattern.FullSentence.ToString(),
            conceptId = "ColorVocabulary",
            grimoireReference = "grimoire:ColorVocabulary",
            errorTags = BuildErrorTags(
                colorCorrect ? (speechProofSucceeded ? "" : "pronunciation_mismatch") : "color_mismatch",
                "",
                buddyPronunciation,
                colorAttemptCorrect),
            correct = colorAttemptCorrect,
            hintCount = hintUsed ? 1 : 0,
            highestHintLevel = hintUsed ? "ActivityHint" : "",
            responseSeconds = responseSeconds,
            pronunciationScore = buddyPronunciation != null ? buddyPronunciation.score : 0f,
            recognitionConfidence = buddyPronunciation != null ? buddyPronunciation.modelConfidence : 0f,
            pronunciationInsight = buddyPronunciation,
            outcome = outcomeStatus ?? "",
        });

        if (!ShouldSubmitTeacherAnalytics)
            return;

        string attemptId = Guid.NewGuid().ToString("N");
        byte[] submittedPronunciationAudio = pronunciationAudioWavBytes ?? Array.Empty<byte>();
        bool hasPronunciationAudio = submittedPronunciationAudio.Length > 44;
        bool eligibleForPronunciationAnalysis = speechProofSucceeded && ShouldRequestServerAnalysis && hasPronunciationAudio;
        bool shouldRequestPronunciationAnalysis = eligibleForPronunciationAnalysis &&
            ShouldScheduleServerPronunciationReview(CurrentPronunciationZone(), pronunciationInsight);
        if (ShouldRequestServerAnalysis && !hasPronunciationAudio)
            Debug.LogWarning("[CurriculumSessionManager] Azure pronunciation analysis skipped for color mini-game: no captured WAV audio was available.");
        string serverJobId = shouldRequestPronunciationAnalysis ? $"pronunciation_{attemptId}" : "";
        string onDeviceProvider = pronunciationInsight.HasValue && !string.IsNullOrWhiteSpace(pronunciationInsight.Value.ProviderName)
            ? pronunciationInsight.Value.ProviderName
            : "On-device speech gate";
        string normalizedTargetColor = ColorWordUtility.Normalize(targetColor);
        string normalizedSelectedColor = ColorWordUtility.Normalize(selectedColor);
        string contentId = $"color_chest:{normalizedTargetColor.ToLowerInvariant()}";
        string attemptGroupId = BuildAttemptGroupId("color_chest", contentId);

        provider?.SubmitColorMiniGameAttempt(new ColorMiniGameAttemptRecord
        {
            attemptId = attemptId,
            studentId = CurrentMission.studentId,
            classId = CurrentMission.classId,
            schoolId = CurrentMission.schoolId,
            missionId = CurrentMission.missionId,
            contentId = contentId,
            inputSource = "color_chest",
            attemptGroupId = attemptGroupId,
            attemptNumber = NextAttemptNumber(attemptGroupId),
            chestCategory = string.IsNullOrWhiteSpace(chestCategory) ? "TreasureChest" : chestCategory,
            targetColor = normalizedTargetColor,
            selectedColor = normalizedSelectedColor,
            spokenColor = spokenColor ?? "",
            colorCorrect = colorCorrect,
            speechProofSucceeded = speechProofSucceeded,
            hintUsed = hintUsed,
            pronunciationInsight = BuildPronunciationInsightRecord(pronunciationInsight),
            analysisMode = analysisMode.ToString(),
            onDeviceAnalysisProvider = onDeviceProvider,
            serverAnalysisStatus = shouldRequestPronunciationAnalysis
                ? "pending"
                : eligibleForPronunciationAnalysis ? "sampled_out" : "not_requested",
            serverAnalysisJobId = serverJobId,
            audioContentType = submittedPronunciationAudio.Length > 44 ? "audio/wav" : "",
            audioDurationSeconds = EstimateWavDurationSeconds(submittedPronunciationAudio),
            rawAudioCaptured = hasPronunciationAudio,
            rawAudioUploaded = false,
            rawAudioRetentionPolicy = AudioRetentionPolicy(hasPronunciationAudio, shouldRequestPronunciationAnalysis),
            pronunciationAudioWavBytes = submittedPronunciationAudio,
            outcomeStatus = string.IsNullOrWhiteSpace(outcomeStatus)
                ? BuildColorOutcomeStatus(selectedColor, colorCorrect, speechProofSucceeded)
                : outcomeStatus,
            rewardAwarded = Mathf.Max(0, rewardAwarded),
            responseSeconds = Mathf.Max(0f, responseSeconds),
            createdAtUtc = DateTime.UtcNow.ToString("o"),
        });
    }

    static string BuildCountingOutcomeStatus(int selectedCount, bool countCorrect, bool speechProofSucceeded)
    {
        if (selectedCount <= 0)
            return "seen_ignored";
        if (!countCorrect)
            return "opened_wrong_answer";
        if (!speechProofSucceeded)
            return "opened_correct_pronunciation_failed";
        return "opened_correct";
    }

    static string BuildColorOutcomeStatus(string selectedColor, bool colorCorrect, bool speechProofSucceeded)
    {
        if (string.IsNullOrWhiteSpace(selectedColor))
            return "seen_ignored";
        if (!colorCorrect)
            return "opened_wrong_answer";
        if (!speechProofSucceeded)
            return "opened_correct_pronunciation_failed";
        return "opened_correct";
    }
}
