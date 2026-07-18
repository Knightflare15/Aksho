using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;


public sealed partial class CurriculumSessionManager
{
    public void RecordBuddyConversationTurn(
        string learnerMessage,
        string buddyResponse,
        string sourceLanguage = "",
        string targetLanguage = "",
        float englishRatio = 0f,
        string conversationSkill = "",
        GrammarPhrasePattern grammarPattern = GrammarPhrasePattern.FullSentence,
        GrammarConceptId conceptId = GrammarConceptId.None,
        string wordChoiceIssue = "",
        string formationIssue = "",
        string errorCategory = "",
        string hintLevelShown = "",
        string remediationStep = "",
        string correctedResponse = "",
        IEnumerable<string> safeMemoryTags = null,
        IEnumerable<string> safetyFlags = null,
        string teacherNote = "",
        string buddyContractId = "response_coach",
        string promptTemplateId = "deterministic_buddy_feedback",
        string policyVersion = "buddy_policy_v1",
        string dialogueTaskId = "",
        bool reportable = true,
        float responseSeconds = 0f)
    {
        GrammarWorldProgressData buddyProgress = GrammarWorldProgressService.Instance != null
            ? GrammarWorldProgressService.Instance.Data
            : null;
        GrammarMapAreaState buddyCurrentArea = ResolveArea(buddyProgress?.currentAreaId ?? "");
        string buddyCurrentAreaId = buddyCurrentArea?.areaId ?? buddyProgress?.currentAreaId ?? "";
        SemanticZoneKind buddyZoneKind = buddyCurrentArea != null ? buddyCurrentArea.sceneKind : SemanticZoneKind.Town;
        string buddyConceptId = conceptId != GrammarConceptId.None
            ? conceptId.ToString()
            : ResolveConceptId(buddyCurrentAreaId);
        string buddyContentId = ResolveDialogueContentId(dialogueTaskId, "", learnerMessage);
        CaptureBuddyLearningAttempt(new BuddyLearningAttemptRecord
        {
            sourceRecordType = "buddy_conversation",
            areaId = buddyCurrentAreaId,
            zoneKind = buddyZoneKind.ToString(),
            activityType = string.IsNullOrWhiteSpace(dialogueTaskId) ? "buddy_free_conversation" : "buddy_learning_conversation",
            modality = BuddyLearningModality.Conversation.ToString(),
            inputSource = string.IsNullOrWhiteSpace(dialogueTaskId) ? "buddy_chat" : "buddy_dialogue_support",
            contentId = string.IsNullOrWhiteSpace(buddyContentId) ? "buddy:conversation" : buddyContentId,
            dialogueTaskId = dialogueTaskId ?? "",
            questionPrompt = buddyResponse ?? "",
            submittedResponse = learnerMessage ?? "",
            correctedResponse = correctedResponse ?? "",
            grammarPattern = grammarPattern.ToString(),
            conceptId = buddyConceptId,
            grimoireReference = BuildGrimoireReference(buddyConceptId),
            vocabularyTokens = ExtractVocabularyTokens(learnerMessage),
            masteryTags = ResolveMasteryTags(buddyCurrentAreaId),
            errorTags = BuildErrorTags(errorCategory, FirstNonEmpty(wordChoiceIssue, formationIssue), null, string.IsNullOrWhiteSpace(errorCategory)),
            countsTowardMastery = conceptId != GrammarConceptId.None || !string.IsNullOrWhiteSpace(errorCategory),
            correct = string.IsNullOrWhiteSpace(errorCategory),
            hintCount = string.IsNullOrWhiteSpace(hintLevelShown) ? 0 : 1,
            highestHintLevel = hintLevelShown ?? "",
            remediationStep = remediationStep ?? "",
            buddyAssistMode = ResolveBuddyAssistMode(buddyZoneKind, buddyCurrentAreaId),
            responseSeconds = responseSeconds,
        });

        if (!ShouldSubmitTeacherAnalytics)
            return;

        GrammarWorldProgressData progress = buddyProgress;
        GrammarMapAreaState currentArea = buddyCurrentArea;
        string currentAreaId = buddyCurrentAreaId;
        SemanticZoneKind zoneKind = buddyZoneKind;
        string evidenceText = !string.IsNullOrWhiteSpace(learnerMessage)
            ? learnerMessage
            : buddyResponse ?? "";
        List<string> vocabularyTokens = ExtractVocabularyTokens(evidenceText);
        List<string> masteryTags = ResolveMasteryTags(currentAreaId);
        TrackGrammarPattern(grammarPattern);
        TrackMasteryTags(masteryTags);
        TrackPracticedVocabulary(vocabularyTokens);
        if (!string.IsNullOrWhiteSpace(errorCategory))
            grammarErrorsThisRun++;

        string contentId = ResolveDialogueContentId(dialogueTaskId, "", evidenceText);
        string inputSource = string.IsNullOrWhiteSpace(dialogueTaskId) ? "buddy_chat" : "buddy_dialogue_support";
        string attemptGroupId = BuildAttemptGroupId(inputSource, string.IsNullOrWhiteSpace(contentId) ? "buddy" : contentId, currentAreaId);
        provider?.SubmitBuddyConversationTurn(new BuddyConversationTurnRecord
        {
            eventId = Guid.NewGuid().ToString("N"),
            studentId = CurrentMission.studentId,
            classId = CurrentMission.classId,
            schoolId = CurrentMission.schoolId,
            missionId = CurrentMission.missionId,
            goalId = CurrentWorldGoal?.goalId ?? "",
            dialogueTaskId = dialogueTaskId ?? "",
            contentId = contentId,
            inputSource = inputSource,
            attemptGroupId = attemptGroupId,
            attemptNumber = NextAttemptNumber(attemptGroupId),
            areaId = currentAreaId,
            zoneKind = zoneKind.ToString(),
            learnerMessage = learnerMessage ?? "",
            buddyResponse = buddyResponse ?? "",
            sourceLanguage = sourceLanguage ?? "",
            targetLanguage = targetLanguage ?? "",
            englishRatio = Mathf.Clamp01(englishRatio),
            conversationSkill = conversationSkill ?? "",
            grammarPattern = grammarPattern.ToString(),
            conceptId = conceptId != GrammarConceptId.None ? conceptId.ToString() : ResolveConceptId(currentAreaId),
            wordChoiceIssue = wordChoiceIssue ?? "",
            formationIssue = formationIssue ?? "",
            errorCategory = errorCategory ?? "",
            hintLevelShown = hintLevelShown ?? "",
            remediationStep = remediationStep ?? "",
            correctedResponse = correctedResponse ?? "",
            buddyAssistMode = ResolveBuddyAssistMode(zoneKind, currentAreaId),
            vocabularyTokens = vocabularyTokens,
            masteryTags = masteryTags,
            safeMemoryTags = CopyStrings(safeMemoryTags),
            safetyFlags = CopyStrings(safetyFlags),
            teacherNote = teacherNote ?? "",
            buddyContractId = string.IsNullOrWhiteSpace(buddyContractId) ? "response_coach" : buddyContractId,
            promptTemplateId = string.IsNullOrWhiteSpace(promptTemplateId) ? "deterministic_buddy_feedback" : promptTemplateId,
            policyVersion = string.IsNullOrWhiteSpace(policyVersion) ? "buddy_policy_v1" : policyVersion,
            reportable = reportable,
            responseSeconds = Mathf.Max(0f, responseSeconds),
            createdAtUtc = DateTime.UtcNow.ToString("o"),
        });
    }

    void SubmitBattleSpokenPhraseEvidence(
        string phrase,
        GrammarPhrasePattern grammarPattern,
        bool accepted,
        string rejectionReason,
        GrammarMapAreaState area,
        List<string> masteryTags,
        List<string> vocabularyTokens,
        PronunciationInsightResult? pronunciationInsight,
        string battleContentId,
        string battleAttemptGroupId)
    {
        if (string.IsNullOrWhiteSpace(phrase))
            return;

        if (accepted)
            spokenPhraseEventsThisRun++;
        else
            pronunciationRetriesThisRun++;

        string contentId = string.IsNullOrWhiteSpace(battleContentId) ? $"battle:{grammarPattern}" : battleContentId;
        string attemptGroupId = string.IsNullOrWhiteSpace(battleAttemptGroupId)
            ? BuildAttemptGroupId("grammar_battle_speech", contentId, area?.areaId ?? "")
            : $"{battleAttemptGroupId}:speech";
        provider?.SubmitSpokenPhraseEvent(new SpokenPhraseEventRecord
        {
            eventId = Guid.NewGuid().ToString("N"),
            studentId = CurrentMission.studentId,
            classId = CurrentMission.classId,
            schoolId = CurrentMission.schoolId,
            missionId = CurrentMission.missionId,
            goalId = CurrentWorldGoal?.goalId ?? "",
            dialogueTaskId = "",
            contentId = contentId,
            inputSource = "grammar_battle_speech",
            attemptGroupId = attemptGroupId,
            attemptNumber = NextAttemptNumber(attemptGroupId),
            areaId = area?.areaId ?? "",
            zoneKind = area != null ? area.sceneKind.ToString() : "",
            phrase = phrase ?? "",
            grammarPattern = grammarPattern.ToString(),
            conceptId = area != null && area.conceptId != GrammarConceptId.None ? area.conceptId.ToString() : ResolveConceptId(area?.areaId ?? ""),
            errorCategory = rejectionReason ?? "",
            hintLevelShown = "",
            remediationStep = "",
            correctedResponse = "",
            buddyAssistMode = ResolveBuddyAssistMode(area != null ? area.sceneKind : SemanticZoneKind.Route, area?.areaId ?? ""),
            vocabularyTokens = vocabularyTokens ?? ExtractVocabularyTokens(phrase),
            masteryTags = masteryTags ?? new List<string>(),
            accepted = accepted,
            rejectionReason = rejectionReason ?? "",
            responseSeconds = 0f,
            pronunciationInsight = BuildPronunciationInsightRecord(pronunciationInsight),
            rawAudioCaptured = false,
            rawAudioUploaded = false,
            rawAudioRetentionPolicy = "no_raw_audio_captured",
            createdAtUtc = DateTime.UtcNow.ToString("o"),
        });
    }

    public void RecordGymAttempt(string areaId, string gymId, bool passed)
    {
        string gymConceptId = ResolveConceptId(areaId);
        CaptureBuddyLearningAttempt(new BuddyLearningAttemptRecord
        {
            sourceRecordType = "gym_attempt",
            areaId = areaId ?? "",
            zoneKind = SemanticZoneKind.Gym.ToString(),
            activityType = "gym_assessment",
            modality = BuddyLearningModality.Assessment.ToString(),
            inputSource = "gym",
            contentId = string.IsNullOrWhiteSpace(gymId) ? areaId ?? "gym" : gymId,
            questionPrompt = "Complete the gym assessment without Buddy help.",
            grammarPattern = GrammarPhrasePattern.FullSentence.ToString(),
            conceptId = gymConceptId,
            grimoireReference = BuildGrimoireReference(gymConceptId),
            masteryTags = ResolveMasteryTags(areaId),
            countsTowardMastery = false,
            correct = passed,
            buddyAssistMode = TranslatorAssistMode.Off.ToString(),
            outcome = passed ? "passed" : "not_passed",
        });

        if (!ShouldSubmitTeacherAnalytics)
            return;

        float elapsedSeconds = ElapsedSeconds;
        TrackMasteryTags(ResolveMasteryTags(areaId));
        provider?.SubmitGymAttempt(new GymAttemptRecord
        {
            attemptId = Guid.NewGuid().ToString("N"),
            studentId = CurrentMission.studentId,
            classId = CurrentMission.classId,
            schoolId = CurrentMission.schoolId,
            missionId = CurrentMission.missionId,
            goalId = CurrentWorldGoal?.goalId ?? "",
            areaId = areaId ?? "",
            gymId = string.IsNullOrWhiteSpace(gymId) ? areaId ?? "" : gymId,
            zoneKind = SemanticZoneKind.Gym.ToString(),
            buddyAssistMode = ResolveBuddyAssistMode(SemanticZoneKind.Gym, areaId),
            masteryTags = ResolveMasteryTags(areaId),
            passed = passed,
            spokenPhraseCount = Mathf.Max(0, spokenPhraseEventsThisRun),
            writtenPhraseCount = Mathf.Max(0, writtenPhraseEventsThisRun),
            grammarErrors = Mathf.Max(0, grammarErrorsThisRun),
            pronunciationRetries = Mathf.Max(0, pronunciationRetriesThisRun),
            startedAtUtc = DateTime.UtcNow.AddSeconds(-Mathf.Max(0f, elapsedSeconds)).ToString("o"),
            endedAtUtc = DateTime.UtcNow.ToString("o"),
        });
    }

    static List<string> ResolveMasteryTags(string areaId)
    {
        GrammarMapAreaState area = ResolveArea(areaId);

        if (area == null)
            return new List<string>();

        NaturalGrammarRegion region = NaturalGrammarProgression.ResolveByTopicOrTier(area.grammarTopic, area.grammarTopicTier);
        return new List<string>(region.masteryTags ?? Array.Empty<string>());
    }

    static string ResolveBuddyAssistMode(SemanticZoneKind zoneKind, string areaId)
    {
        GrammarSceneController controller = UnityEngine.Object.FindAnyObjectByType<GrammarSceneController>();
        if (controller != null)
            return controller.translatorAssist.ToString();

        TranslatorBuddyService buddy = UnityEngine.Object.FindAnyObjectByType<TranslatorBuddyService>();
        if (buddy != null)
            return buddy.CurrentAssistMode.ToString();

        GrammarMapAreaState area = ResolveArea(areaId);
        SemanticZoneKind resolvedKind = area != null ? area.sceneKind : zoneKind;
        return resolvedKind switch
        {
            SemanticZoneKind.Route => TranslatorAssistMode.Partial.ToString(),
            SemanticZoneKind.Gym => TranslatorAssistMode.Off.ToString(),
            _ => TranslatorAssistMode.Full.ToString(),
        };
    }

    static List<string> MergeMasteryTags(List<string> baseTags, IEnumerable<string> extraTags)
    {
        var result = new List<string>();
        AddMasteryTags(result, baseTags);
        AddMasteryTags(result, extraTags);
        return result;
    }

    static IEnumerable<string> MasteryTagsForPattern(GrammarPhrasePattern pattern)
    {
        return pattern switch
        {
            GrammarPhrasePattern.NounVerbPresent or GrammarPhrasePattern.PronounVerbPresent => new[] { "simple-present" },
            GrammarPhrasePattern.PastTense => new[] { "past-tense" },
            GrammarPhrasePattern.ProgressiveTense => new[] { "progressive-tense" },
            GrammarPhrasePattern.DeterminerNoun => new[] { "article-noun" },
            GrammarPhrasePattern.AdjectiveNoun or GrammarPhrasePattern.DeterminerAdjectiveNoun => new[] { "adjective-noun" },
            _ => Array.Empty<string>(),
        };
    }

    static void AddMasteryTags(List<string> target, IEnumerable<string> tags)
    {
        if (target == null || tags == null)
            return;

        foreach (string tag in tags)
        {
            string normalized = string.IsNullOrWhiteSpace(tag) ? "" : tag.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(normalized) && !target.Contains(normalized))
                target.Add(normalized);
        }
    }

    static List<string> CopyStrings(IEnumerable<string> values)
    {
        var result = new List<string>();
        if (values == null)
            return result;

        foreach (string value in values)
        {
            if (!string.IsNullOrWhiteSpace(value) && !result.Contains(value))
                result.Add(value.Trim());
        }

        return result;
    }

    static List<string> BuildErrorTags(
        string primary,
        string secondary,
        PronunciationInsightRecord pronunciation,
        bool accepted)
    {
        var result = new List<string>();
        AddErrorTag(result, primary);
        AddErrorTag(result, secondary);
        if (pronunciation != null && (!accepted || pronunciation.score < 0.65f))
        {
            AddErrorTag(result, string.IsNullOrWhiteSpace(pronunciation.hintKey)
                ? "pronunciation_needs_practice"
                : $"pronunciation_{pronunciation.hintKey}");
            if (pronunciation.phonemeIssues != null)
            {
                foreach (PhonemeAlignmentRecord issue in pronunciation.phonemeIssues)
                {
                    if (issue == null || string.Equals(issue.status, "matched", StringComparison.OrdinalIgnoreCase))
                        continue;
                    AddErrorTag(result, $"phoneme_{issue.expected}_{issue.status}");
                }
            }
        }
        return result;
    }

    static List<string> BuildPronunciationErrorTags(PronunciationInsightRecord pronunciation, bool accepted)
    {
        return BuildErrorTags(accepted ? "" : "spoken_response_mismatch", "", pronunciation, accepted);
    }

    static void AddErrorTag(List<string> target, string value)
    {
        if (target == null || string.IsNullOrWhiteSpace(value))
            return;
        string normalized = value.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
        if (!target.Contains(normalized))
            target.Add(normalized);
    }

    static string FirstNonEmpty(string first, string second)
    {
        if (!string.IsNullOrWhiteSpace(first))
            return first;
        return second ?? "";
    }

    static GrammarMapAreaState ResolveArea(string areaId)
    {
        GrammarWorldProgressData progress = GrammarWorldProgressService.Instance != null
            ? GrammarWorldProgressService.Instance.Data
            : null;
        if (progress == null || progress.areas == null)
            return null;

        GrammarMapAreaState area = null;
        if (!string.IsNullOrWhiteSpace(areaId))
            area = progress.areas.Find(candidate => candidate != null && candidate.areaId == areaId);
        if (area == null && !string.IsNullOrWhiteSpace(progress.currentAreaId))
            area = progress.areas.Find(candidate => candidate != null && candidate.areaId == progress.currentAreaId);
        return area;
    }
}
