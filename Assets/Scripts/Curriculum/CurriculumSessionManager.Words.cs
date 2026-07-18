using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;


public sealed partial class CurriculumSessionManager
{
    public void RecordWordCast(
        string word,
        bool success,
        bool special,
        float responseSeconds,
        PronunciationInsightResult? pronunciationInsight = null,
        byte[] pronunciationAudioWavBytes = null,
        bool requestPronunciationAnalysis = true,
        bool recordBuddyLearningAttempt = true)
    {
        string normalized = SpellRegistry.NormalizeWord(word);
        if (recordBuddyLearningAttempt && !string.IsNullOrWhiteSpace(normalized))
        {
            GrammarWorldProgressData buddyProgress = GrammarWorldProgressService.Instance != null
                ? GrammarWorldProgressService.Instance.Data
                : null;
            GrammarMapAreaState buddyArea = ResolveArea(buddyProgress?.currentAreaId ?? "");
            PronunciationInsightRecord buddyPronunciation = BuildPronunciationInsightRecord(pronunciationInsight);
            CaptureBuddyLearningAttempt(new BuddyLearningAttemptRecord
            {
                sourceRecordType = "word_cast",
                areaId = buddyProgress?.currentAreaId ?? "",
                zoneKind = buddyArea != null ? buddyArea.sceneKind.ToString() : "",
                activityType = "spoken_word",
                modality = BuddyLearningModality.Speaking.ToString(),
                inputSource = "word_cast",
                contentId = $"word:{normalized.ToLowerInvariant()}",
                questionPrompt = $"Say {normalized}",
                expectedResponse = normalized,
                submittedResponse = buddyPronunciation != null ? buddyPronunciation.rawRecognizedText : normalized,
                grammarPattern = GrammarPhrasePattern.FullSentence.ToString(),
                conceptId = buddyArea != null && buddyArea.conceptId != GrammarConceptId.None
                    ? buddyArea.conceptId.ToString()
                    : ResolveConceptId(buddyProgress?.currentAreaId ?? ""),
                errorTags = BuildPronunciationErrorTags(buddyPronunciation, success),
                correct = success,
                buddyAssistMode = ResolveBuddyAssistMode(buddyArea != null ? buddyArea.sceneKind : SemanticZoneKind.Route, buddyProgress?.currentAreaId ?? ""),
                responseSeconds = responseSeconds,
                confidenceScore = buddyPronunciation != null ? buddyPronunciation.modelConfidence : 0f,
                recognitionConfidence = buddyPronunciation != null ? buddyPronunciation.modelConfidence : 0f,
                pronunciationScore = buddyPronunciation != null ? buddyPronunciation.score : 0f,
                pronunciationInsight = buddyPronunciation,
            });
        }

        if (!ShouldSubmitTeacherAnalytics)
            return;

        if (!IsWordAllowed(normalized))
            return;

        wordsPracticed.Add(normalized);
        if (success && special)
            specialWordMatches++;

        string eventId = Guid.NewGuid().ToString("N");
        byte[] submittedPronunciationAudio = requestPronunciationAnalysis
            ? pronunciationAudioWavBytes ?? Array.Empty<byte>()
            : Array.Empty<byte>();
        bool hasPronunciationAudio = submittedPronunciationAudio.Length > 44;
        // Only a response accepted by the STT/keyword gate is eligible for
        // the authoritative Azure pronunciation review. Rejected attempts
        // remain useful analytics, but must not create pronunciation jobs.
        bool eligibleForPronunciationAnalysis = success && requestPronunciationAnalysis && ShouldRequestServerAnalysis && hasPronunciationAudio;
        bool shouldRequestPronunciationAnalysis = eligibleForPronunciationAnalysis &&
            ShouldScheduleServerPronunciationReview(CurrentPronunciationZone(), pronunciationInsight);
        if (requestPronunciationAnalysis && ShouldRequestServerAnalysis && !hasPronunciationAudio)
            Debug.LogWarning($"[CurriculumSessionManager] Azure pronunciation analysis skipped for '{normalized}': no captured WAV audio was available.");
        string serverJobId = shouldRequestPronunciationAnalysis ? $"pronunciation_{eventId}" : "";
        string onDeviceProvider = pronunciationInsight.HasValue && !string.IsNullOrWhiteSpace(pronunciationInsight.Value.ProviderName)
            ? pronunciationInsight.Value.ProviderName
            : "On-device speech gate";
        string contentId = $"word:{normalized.ToLowerInvariant()}";
        string attemptGroupId = BuildAttemptGroupId("word_cast", contentId);

        provider?.SubmitWordCast(new WordCastRecord
        {
            eventId = eventId,
            studentId = CurrentMission.studentId,
            classId = CurrentMission.classId,
            schoolId = CurrentMission.schoolId,
            missionId = CurrentMission.missionId,
            contentId = contentId,
            inputSource = "word_cast",
            attemptGroupId = attemptGroupId,
            attemptNumber = NextAttemptNumber(attemptGroupId),
            word = normalized,
            success = success,
            specialMatch = special,
            responseSeconds = responseSeconds,
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
            createdAtUtc = DateTime.UtcNow.ToString("o"),
        });
    }

    public void RecordSpokenPhraseEvent(
        string phrase,
        GrammarPhrasePattern grammarPattern,
        bool accepted,
        string rejectionReason,
        SemanticZoneKind zoneKind,
        string areaId,
        float responseSeconds = 0f,
        PronunciationInsightResult? pronunciationInsight = null,
        GrammarConceptId conceptId = GrammarConceptId.None,
        string errorCategory = "",
        string hintLevelShown = "",
        string remediationStep = "",
        string correctedResponse = "",
        string submittedPhrase = "",
        string targetPhrase = "",
        string dialogueTaskId = "",
        string inputSource = "npc_dialogue",
        string activityType = "",
        string questionPrompt = "",
        string grimoireReference = "",
        int hintCount = 0,
        byte[] pronunciationAudioWavBytes = null,
        bool requestPronunciationAnalysis = true)
    {
        string buddyEvidencePhrase = ResolveEvidencePhrase(phrase, submittedPhrase, targetPhrase, accepted);
        string buddyContentId = ResolveDialogueContentId(dialogueTaskId, targetPhrase, buddyEvidencePhrase);
        string buddyConceptId = conceptId != GrammarConceptId.None ? conceptId.ToString() : ResolveConceptId(areaId);
        PronunciationInsightRecord buddyPronunciation = BuildPronunciationInsightRecord(pronunciationInsight);
        CaptureBuddyLearningAttempt(new BuddyLearningAttemptRecord
        {
            sourceRecordType = "spoken_phrase",
            areaId = areaId ?? "",
            zoneKind = zoneKind.ToString(),
            activityType = string.IsNullOrWhiteSpace(activityType) ? "spoken_phrase" : activityType,
            modality = BuddyLearningModality.Speaking.ToString(),
            inputSource = inputSource ?? "npc_dialogue",
            contentId = buddyContentId,
            dialogueTaskId = dialogueTaskId ?? "",
            questionPrompt = questionPrompt ?? "",
            expectedResponse = targetPhrase ?? "",
            submittedResponse = submittedPhrase ?? phrase ?? "",
            correctedResponse = correctedResponse ?? "",
            grammarPattern = grammarPattern.ToString(),
            conceptId = buddyConceptId,
            grimoireReference = string.IsNullOrWhiteSpace(grimoireReference)
                ? BuildGrimoireReference(buddyConceptId)
                : grimoireReference,
            vocabularyTokens = ExtractVocabularyTokens(buddyEvidencePhrase),
            masteryTags = ResolveMasteryTags(areaId),
            errorTags = BuildErrorTags(errorCategory, rejectionReason, buddyPronunciation, accepted),
            correct = accepted,
            hintCount = Mathf.Max(0, hintCount),
            highestHintLevel = hintLevelShown ?? "",
            remediationStep = remediationStep ?? "",
            buddyAssistMode = ResolveBuddyAssistMode(zoneKind, areaId),
            responseSeconds = responseSeconds,
            recognitionConfidence = buddyPronunciation != null ? buddyPronunciation.modelConfidence : 0f,
            pronunciationScore = buddyPronunciation != null ? buddyPronunciation.score : 0f,
            pronunciationInsight = buddyPronunciation,
        });

        if (!ShouldSubmitTeacherAnalytics)
            return;

        if (accepted)
            spokenPhraseEventsThisRun++;
        else
        {
            grammarErrorsThisRun++;
            pronunciationRetriesThisRun++;
        }

        string evidencePhrase = ResolveEvidencePhrase(phrase, submittedPhrase, targetPhrase, accepted);
        List<string> vocabularyTokens = ExtractVocabularyTokens(evidencePhrase);
        List<string> masteryTags = ResolveMasteryTags(areaId);
        TrackGrammarPattern(grammarPattern);
        TrackMasteryTags(masteryTags);
        TrackPracticedVocabulary(vocabularyTokens);
        if (accepted)
            TrackAcceptedVocabulary(acceptedSpokenVocabulary, vocabularyTokens);
        string contentId = ResolveDialogueContentId(dialogueTaskId, targetPhrase, evidencePhrase);
        string attemptGroupId = BuildAttemptGroupId(inputSource, contentId, areaId);
        byte[] submittedPronunciationAudio = requestPronunciationAnalysis
            ? pronunciationAudioWavBytes ?? Array.Empty<byte>()
            : Array.Empty<byte>();
        bool hasPronunciationAudio = submittedPronunciationAudio.Length > 44;
        // Only an STT-accepted response enters the Azure pronunciation queue.
        bool eligibleForPronunciationAnalysis = accepted && requestPronunciationAnalysis && ShouldRequestServerAnalysis && hasPronunciationAudio;
        bool shouldRequestPronunciationAnalysis = eligibleForPronunciationAnalysis &&
            ShouldScheduleServerPronunciationReview(zoneKind, pronunciationInsight);
        if (accepted && hasPronunciationAudio && !ShouldRequestServerAnalysis)
            Debug.LogWarning($"[CurriculumSessionManager] Azure pronunciation not queued: no authenticated school session or analysis mode is OnDeviceOnly. hasSession={HasStudentSession} analysisMode={analysisMode}.");
        string serverJobId = shouldRequestPronunciationAnalysis ? $"pronunciation_{Guid.NewGuid():N}" : "";
        if (shouldRequestPronunciationAnalysis)
            AzurePronunciationInsightWindow.EnsureExists().ShowPending(targetPhrase);
        provider?.SubmitSpokenPhraseEvent(new SpokenPhraseEventRecord
        {
            eventId = Guid.NewGuid().ToString("N"),
            studentId = CurrentMission.studentId,
            classId = CurrentMission.classId,
            schoolId = CurrentMission.schoolId,
            missionId = CurrentMission.missionId,
            goalId = CurrentWorldGoal?.goalId ?? "",
            dialogueTaskId = dialogueTaskId ?? "",
            contentId = contentId,
            inputSource = inputSource ?? "npc_dialogue",
            attemptGroupId = attemptGroupId,
            attemptNumber = NextAttemptNumber(attemptGroupId),
            areaId = areaId ?? "",
            zoneKind = zoneKind.ToString(),
            phrase = evidencePhrase,
            submittedPhrase = submittedPhrase ?? phrase ?? "",
            targetPhrase = targetPhrase ?? "",
            submittedResponse = submittedPhrase ?? phrase ?? "",
            targetText = targetPhrase ?? "",
            grammarPattern = grammarPattern.ToString(),
            conceptId = conceptId != GrammarConceptId.None ? conceptId.ToString() : ResolveConceptId(areaId),
            errorCategory = errorCategory ?? "",
            hintLevelShown = hintLevelShown ?? "",
            remediationStep = remediationStep ?? "",
            correctedResponse = correctedResponse ?? "",
            buddyAssistMode = ResolveBuddyAssistMode(zoneKind, areaId),
            vocabularyTokens = vocabularyTokens,
            masteryTags = masteryTags,
            accepted = accepted,
            rejectionReason = rejectionReason ?? "",
            responseSeconds = Mathf.Max(0f, responseSeconds),
            pronunciationInsight = BuildPronunciationInsightRecord(pronunciationInsight),
            analysisMode = analysisMode.ToString(),
            onDeviceAnalysisProvider = pronunciationInsight.HasValue ? pronunciationInsight.Value.ProviderName : "",
            serverAnalysisStatus = shouldRequestPronunciationAnalysis
                ? "pending"
                : eligibleForPronunciationAnalysis ? "sampled_out" : "not_requested",
            serverAnalysisJobId = serverJobId,
            audioContentType = hasPronunciationAudio ? "audio/wav" : "",
            audioDurationSeconds = EstimateWavDurationSeconds(submittedPronunciationAudio),
            rawAudioCaptured = hasPronunciationAudio,
            rawAudioUploaded = false,
            rawAudioRetentionPolicy = AudioRetentionPolicy(hasPronunciationAudio, shouldRequestPronunciationAnalysis),
            pronunciationAudioWavBytes = submittedPronunciationAudio,
            createdAtUtc = DateTime.UtcNow.ToString("o"),
        });
    }

    public void RecordWrittenPhraseEvent(
        string phrase,
        GrammarPhrasePattern grammarPattern,
        bool accepted,
        string rejectionReason,
        SemanticZoneKind zoneKind,
        string areaId,
        float responseSeconds = 0f,
        GrammarConceptId conceptId = GrammarConceptId.None,
        string errorCategory = "",
        string hintLevelShown = "",
        string remediationStep = "",
        string correctedResponse = "",
        string submittedPhrase = "",
        string targetPhrase = "",
        string dialogueTaskId = "",
        string inputSource = "npc_dialogue",
        string activityType = "",
        string questionPrompt = "",
        string grimoireReference = "",
        int hintCount = 0)
    {
        string buddyEvidencePhrase = ResolveEvidencePhrase(phrase, submittedPhrase, targetPhrase, accepted);
        string buddyContentId = ResolveDialogueContentId(dialogueTaskId, targetPhrase, buddyEvidencePhrase);
        string buddyConceptId = conceptId != GrammarConceptId.None ? conceptId.ToString() : ResolveConceptId(areaId);
        CaptureBuddyLearningAttempt(new BuddyLearningAttemptRecord
        {
            sourceRecordType = "written_phrase",
            areaId = areaId ?? "",
            zoneKind = zoneKind.ToString(),
            activityType = string.IsNullOrWhiteSpace(activityType) ? "written_phrase" : activityType,
            modality = string.Equals(activityType, "subtitle_unjumble", StringComparison.OrdinalIgnoreCase)
                ? BuddyLearningModality.Arrangement.ToString()
                : BuddyLearningModality.Writing.ToString(),
            inputSource = inputSource ?? "npc_dialogue",
            contentId = buddyContentId,
            dialogueTaskId = dialogueTaskId ?? "",
            questionPrompt = questionPrompt ?? "",
            expectedResponse = targetPhrase ?? "",
            submittedResponse = submittedPhrase ?? phrase ?? "",
            correctedResponse = correctedResponse ?? "",
            grammarPattern = grammarPattern.ToString(),
            conceptId = buddyConceptId,
            grimoireReference = string.IsNullOrWhiteSpace(grimoireReference)
                ? BuildGrimoireReference(buddyConceptId)
                : grimoireReference,
            vocabularyTokens = ExtractVocabularyTokens(buddyEvidencePhrase),
            masteryTags = ResolveMasteryTags(areaId),
            errorTags = BuildErrorTags(errorCategory, rejectionReason, null, accepted),
            correct = accepted,
            hintCount = Mathf.Max(0, hintCount),
            highestHintLevel = hintLevelShown ?? "",
            remediationStep = remediationStep ?? "",
            buddyAssistMode = ResolveBuddyAssistMode(zoneKind, areaId),
            responseSeconds = responseSeconds,
        });

        if (!ShouldSubmitTeacherAnalytics)
            return;

        if (accepted)
            writtenPhraseEventsThisRun++;
        else
            grammarErrorsThisRun++;

        string evidencePhrase = ResolveEvidencePhrase(phrase, submittedPhrase, targetPhrase, accepted);
        List<string> vocabularyTokens = ExtractVocabularyTokens(evidencePhrase);
        List<string> masteryTags = ResolveMasteryTags(areaId);
        TrackGrammarPattern(grammarPattern);
        TrackMasteryTags(masteryTags);
        TrackPracticedVocabulary(vocabularyTokens);
        if (accepted)
            TrackAcceptedVocabulary(acceptedWrittenVocabulary, vocabularyTokens);
        string contentId = ResolveDialogueContentId(dialogueTaskId, targetPhrase, evidencePhrase);
        string attemptGroupId = BuildAttemptGroupId(inputSource, contentId, areaId);
        provider?.SubmitWrittenPhraseEvent(new WrittenPhraseEventRecord
        {
            eventId = Guid.NewGuid().ToString("N"),
            studentId = CurrentMission.studentId,
            classId = CurrentMission.classId,
            schoolId = CurrentMission.schoolId,
            missionId = CurrentMission.missionId,
            goalId = CurrentWorldGoal?.goalId ?? "",
            dialogueTaskId = dialogueTaskId ?? "",
            contentId = contentId,
            inputSource = inputSource ?? "npc_dialogue",
            attemptGroupId = attemptGroupId,
            attemptNumber = NextAttemptNumber(attemptGroupId),
            areaId = areaId ?? "",
            zoneKind = zoneKind.ToString(),
            phrase = evidencePhrase,
            submittedPhrase = submittedPhrase ?? phrase ?? "",
            targetPhrase = targetPhrase ?? "",
            grammarPattern = grammarPattern.ToString(),
            conceptId = conceptId != GrammarConceptId.None ? conceptId.ToString() : ResolveConceptId(areaId),
            errorCategory = errorCategory ?? "",
            hintLevelShown = hintLevelShown ?? "",
            remediationStep = remediationStep ?? "",
            correctedResponse = correctedResponse ?? "",
            buddyAssistMode = ResolveBuddyAssistMode(zoneKind, areaId),
            vocabularyTokens = vocabularyTokens,
            masteryTags = masteryTags,
            accepted = accepted,
            rejectionReason = rejectionReason ?? "",
            responseSeconds = Mathf.Max(0f, responseSeconds),
            createdAtUtc = DateTime.UtcNow.ToString("o"),
        });
    }

    static string ResolveEvidencePhrase(string phrase, string submittedPhrase, string targetPhrase, bool accepted)
    {
        if (accepted && !string.IsNullOrWhiteSpace(targetPhrase))
            return targetPhrase;
        if (!string.IsNullOrWhiteSpace(phrase))
            return phrase;
        if (!string.IsNullOrWhiteSpace(submittedPhrase))
            return submittedPhrase;
        return targetPhrase ?? "";
    }

    int NextAttemptNumber(string attemptGroupId)
    {
        string key = string.IsNullOrWhiteSpace(attemptGroupId)
            ? Guid.NewGuid().ToString("N")
            : attemptGroupId.Trim();
        int next = attemptCountsByGroup.TryGetValue(key, out int current) ? current + 1 : 1;
        attemptCountsByGroup[key] = next;
        return next;
    }

    string BuildAttemptGroupId(string inputSource, string contentId, string areaId = "")
    {
        string source = string.IsNullOrWhiteSpace(inputSource) ? "learning_event" : inputSource.Trim();
        string content = string.IsNullOrWhiteSpace(contentId) ? "unscoped" : contentId.Trim();
        string area = string.IsNullOrWhiteSpace(areaId) ? "" : $":{areaId.Trim()}";
        string missionId = string.IsNullOrWhiteSpace(CurrentMission?.missionId)
            ? "unassigned"
            : CurrentMission.missionId.Trim();
        return $"{missionId}:{source}:{content}{area}";
    }

    static string ResolveDialogueContentId(string dialogueTaskId, string targetPhrase, string phrase)
    {
        if (!string.IsNullOrWhiteSpace(dialogueTaskId))
            return dialogueTaskId.Trim();
        if (!string.IsNullOrWhiteSpace(targetPhrase))
            return $"phrase:{SpellRegistry.NormalizeWord(targetPhrase).ToLowerInvariant()}";
        if (!string.IsNullOrWhiteSpace(phrase))
            return $"phrase:{SpellRegistry.NormalizeWord(phrase).ToLowerInvariant()}";
        return "";
    }

    static string AudioRetentionPolicy(bool hasAudio, bool serverAnalysisRequested)
    {
        if (!hasAudio)
            return "no_raw_audio_captured";
        return serverAnalysisRequested
            ? "upload_for_server_analysis_then_delete"
            : "captured_for_local_submission_only";
    }

    static string ResolveConceptId(string areaId)
    {
        if (string.IsNullOrWhiteSpace(areaId))
            return "";

        GrammarWorldProgressData progress = GrammarWorldProgressService.Instance != null ? GrammarWorldProgressService.Instance.Data : null;
        GrammarMapAreaState area = progress != null && progress.areas != null
            ? progress.areas.Find(candidate => candidate != null && candidate.areaId == areaId)
            : null;
        if (area != null && area.conceptId != GrammarConceptId.None)
            return area.conceptId.ToString();

        string[] parts = areaId.Split(':');
        int tier = parts.Length == 3 && int.TryParse(parts[2], out int parsedTier) ? parsedTier : 1;
        NaturalGrammarRegion region = NaturalGrammarProgression.ResolveByTopicOrTier(parts.Length > 1 ? parts[1] : areaId, tier);
        return region != null && region.conceptId != GrammarConceptId.None ? region.conceptId.ToString() : "";
    }

    public void RecordGrammarBattleEvent(
        string phrase,
        GrammarPhrasePattern grammarPattern,
        GrammarBattleCurse activeCurse,
        string actionVerb,
        string actionRole,
        bool accepted,
        string outcome,
        int damageDealt = 0,
        int ppSpent = 0,
        GrammarConceptId conceptId = GrammarConceptId.None,
        string errorCategory = "",
        string hintLevelShown = "",
        string remediationStep = "",
        string correctedResponse = "",
        string enemyNounFamily = "",
        string enemyActionVerb = "",
        string enemyGrammarCommand = "",
        string enemyGrammarPattern = "",
        string commandPreposition = "",
        string commandConjunction = "",
        List<string> encounterMasteryTags = null,
        PronunciationInsightResult? pronunciationInsight = null)
    {
        GrammarWorldProgressData buddyProgress = GrammarWorldProgressService.Instance != null
            ? GrammarWorldProgressService.Instance.Data
            : null;
        GrammarMapAreaState buddyCurrentArea = ResolveArea(buddyProgress?.currentAreaId ?? "");
        string buddyCurrentAreaId = buddyCurrentArea?.areaId ?? buddyProgress?.currentAreaId ?? "";
        List<string> buddyVocabularyTokens = ExtractVocabularyTokens(phrase);
        List<string> buddyMasteryTags = MergeMasteryTags(ResolveMasteryTags(buddyCurrentAreaId), encounterMasteryTags);
        AddMasteryTags(buddyMasteryTags, MasteryTagsForPattern(grammarPattern));
        string buddyConceptId = conceptId != GrammarConceptId.None
            ? conceptId.ToString()
            : ResolveConceptId(buddyCurrentAreaId);
        PronunciationInsightRecord buddyPronunciation = BuildPronunciationInsightRecord(pronunciationInsight);
        CaptureBuddyLearningAttempt(new BuddyLearningAttemptRecord
        {
            sourceRecordType = "grammar_battle",
            areaId = buddyCurrentAreaId,
            zoneKind = buddyCurrentArea != null ? buddyCurrentArea.sceneKind.ToString() : "",
            activityType = "grammar_battle_command",
            modality = BuddyLearningModality.Combat.ToString(),
            inputSource = "grammar_battle",
            contentId = $"battle:{grammarPattern}",
            questionPrompt = enemyGrammarCommand ?? "",
            expectedResponse = correctedResponse ?? "",
            submittedResponse = phrase ?? "",
            correctedResponse = correctedResponse ?? "",
            grammarPattern = grammarPattern.ToString(),
            conceptId = buddyConceptId,
            grimoireReference = BuildGrimoireReference(buddyConceptId),
            vocabularyTokens = buddyVocabularyTokens,
            masteryTags = buddyMasteryTags,
            errorTags = BuildErrorTags(errorCategory, accepted ? "" : outcome, buddyPronunciation, accepted),
            correct = accepted,
            hintCount = string.IsNullOrWhiteSpace(hintLevelShown) ? 0 : 1,
            highestHintLevel = hintLevelShown ?? "",
            remediationStep = remediationStep ?? "",
            buddyAssistMode = ResolveBuddyAssistMode(buddyCurrentArea != null ? buddyCurrentArea.sceneKind : SemanticZoneKind.Route, buddyCurrentAreaId),
            pronunciationScore = buddyPronunciation != null ? buddyPronunciation.score : 0f,
            recognitionConfidence = buddyPronunciation != null ? buddyPronunciation.modelConfidence : 0f,
            pronunciationInsight = buddyPronunciation,
            activeCurse = activeCurse.ToString(),
            actionVerb = actionVerb ?? "",
            actionRole = actionRole ?? "",
            outcome = outcome ?? "",
        });

        if (!ShouldSubmitTeacherAnalytics)
            return;

        if (!accepted)
            grammarErrorsThisRun++;

        List<string> vocabularyTokens = buddyVocabularyTokens;
        grammarBattleEventsThisRun++;
        TrackGrammarPattern(grammarPattern);
        TrackPracticedVocabulary(vocabularyTokens);
        if (accepted)
            TrackAcceptedVocabulary(acceptedBattleVocabulary, vocabularyTokens);
        GrammarWorldProgressData progress = buddyProgress;
        GrammarMapAreaState currentArea = buddyCurrentArea;
        string currentAreaId = buddyCurrentAreaId;
        List<string> masteryTags = buddyMasteryTags;
        TrackMasteryTags(masteryTags);
        string contentId = $"battle:{grammarPattern}";
        string attemptGroupId = BuildAttemptGroupId("grammar_battle", contentId, currentAreaId);
        SubmitBattleSpokenPhraseEvidence(
            phrase,
            grammarPattern,
            accepted,
            accepted ? "" : outcome,
            currentArea,
            masteryTags,
            vocabularyTokens,
            pronunciationInsight,
            contentId,
            attemptGroupId);
        provider?.SubmitGrammarBattleEvent(new GrammarBattleEventRecord
        {
            eventId = Guid.NewGuid().ToString("N"),
            studentId = CurrentMission.studentId,
            classId = CurrentMission.classId,
            schoolId = CurrentMission.schoolId,
            missionId = CurrentMission.missionId,
            goalId = CurrentWorldGoal?.goalId ?? "",
            contentId = contentId,
            inputSource = "grammar_battle",
            attemptGroupId = attemptGroupId,
            attemptNumber = NextAttemptNumber(attemptGroupId),
            areaId = currentAreaId,
            zoneKind = currentArea != null ? currentArea.sceneKind.ToString() : "",
            encounterType = "grammar_battle",
            playerPhrase = phrase ?? "",
            grammarPattern = grammarPattern.ToString(),
            conceptId = conceptId != GrammarConceptId.None ? conceptId.ToString() : ResolveConceptId(currentAreaId),
            errorCategory = errorCategory ?? "",
            hintLevelShown = hintLevelShown ?? "",
            remediationStep = remediationStep ?? "",
            correctedResponse = correctedResponse ?? "",
            buddyAssistMode = ResolveBuddyAssistMode(currentArea != null ? currentArea.sceneKind : SemanticZoneKind.Route, currentAreaId),
            commandPreposition = commandPreposition ?? "",
            commandConjunction = commandConjunction ?? "",
            vocabularyTokens = vocabularyTokens,
            masteryTags = masteryTags,
            activeCurse = activeCurse.ToString(),
            enemyNounFamily = enemyNounFamily ?? "",
            enemyActionVerb = enemyActionVerb ?? "",
            enemyGrammarCommand = enemyGrammarCommand ?? "",
            enemyGrammarPattern = enemyGrammarPattern ?? "",
            actionVerb = actionVerb ?? "",
            actionRole = string.IsNullOrWhiteSpace(actionRole) ? "Command" : actionRole,
            accepted = accepted,
            outcome = outcome ?? "",
            damageDealt = Mathf.Max(0, damageDealt),
            ppSpent = Mathf.Max(0, ppSpent),
            pronunciationInsight = BuildPronunciationInsightRecord(pronunciationInsight),
            rawAudioCaptured = false,
            rawAudioUploaded = false,
            rawAudioRetentionPolicy = "no_raw_audio_captured",
            createdAtUtc = DateTime.UtcNow.ToString("o"),
        });
    }
}
