using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public partial class TranslatorBuddyService : MonoBehaviour
{
    public TutorFeedbackPlan BuildTutorFeedback(
        LocalizedDialogueLine dialogueLine,
        string submittedOrHeard,
        string rejectionReason,
        PronunciationInsightResult? pronunciationInsight = null,
        HandwritingDiagnosticSummary handwritingDiagnostics = null)
    {
        string observed = string.IsNullOrWhiteSpace(submittedOrHeard)
            ? "nothing clear"
            : VoiceUnlockRecognizer.NormalizeKeyword(submittedOrHeard);
        if (dialogueLine == null)
        {
            return new TutorFeedbackPlan
            {
                observedResponse = observed,
                errorCategory = rejectionReason ?? "response_mismatch",
                whatWasWrong = "The answer did not match yet.",
                correctedResponse = "",
                hintLevelShown = TutorHintLevel.DirectCorrection,
                remediationStep = TutorRemediationStep.Retry,
            };
        }

        string conceptKey = BuildConceptCounterKey(dialogueLine.conceptId);
        string subskillKey = BuildSubskillCounterKey(dialogueLine);
        int conceptMissCount = RegisterMiss(conceptMissCounts, conceptKey);
        int subskillMissCount = RegisterMiss(subskillMissCounts, subskillKey);
        int missCount = Mathf.Max(conceptMissCount, subskillMissCount);

        string corrected = dialogueLine.expectedEnglishResponse ?? "";
        string errorCategory = ResolveErrorCategory(rejectionReason, pronunciationInsight, handwritingDiagnostics);
        string what = BuildWhatWasWrong(dialogueLine, rejectionReason);
        string why = BuildWhy(dialogueLine, rejectionReason);
        string microLesson = missCount >= 2
            ? BuildMicroLesson(dialogueLine.conceptId)
            : "";
        if (!string.IsNullOrWhiteSpace(microLesson) && missCount >= 3)
            microLesson = $"{microLesson} Practice again with: {BuildPracticeExamples(dialogueLine.conceptId)}.";

        if (pronunciationInsight.HasValue && IsActionablePronunciationInsight(pronunciationInsight.Value))
        {
            string soundNote = BuildPronunciationNote(pronunciationInsight.Value);
            if (!string.IsNullOrWhiteSpace(soundNote))
                microLesson = string.IsNullOrWhiteSpace(microLesson) ? soundNote : $"{microLesson} {soundNote}";
        }

        if (handwritingDiagnostics != null && !string.IsNullOrWhiteSpace(handwritingDiagnostics.primaryHint))
        {
            string writingNote = $"Writing note: {handwritingDiagnostics.primaryHint}";
            microLesson = string.IsNullOrWhiteSpace(microLesson) ? writingNote : $"{microLesson} {writingNote}";
        }

        return new TutorFeedbackPlan
        {
            conceptId = dialogueLine.conceptId,
            errorCategory = errorCategory,
            subskillId = dialogueLine.subskillId ?? "",
            observedResponse = observed,
            whatWasWrong = what,
            why = why,
            correctedResponse = corrected,
            microLesson = microLesson,
            hintLevelShown = missCount >= 2 ? TutorHintLevel.MicroLesson : TutorHintLevel.RuleHint,
            remediationStep = missCount >= 3 ? TutorRemediationStep.ExampleDrill : TutorRemediationStep.GuidedRetry,
            missCount = missCount,
        };
    }

    public string BuildTutorFeedbackStatus(LocalizedDialogueLine dialogueLine, TutorFeedbackPlan feedback)
    {
        string observed = feedback != null && !string.IsNullOrWhiteSpace(feedback.observedResponse)
            ? feedback.observedResponse
            : "nothing clear";
        string prefix = $"Heard {observed}.";
        if (dialogueLine == null || feedback == null)
            return $"{prefix} Try again.";

        TranslatorAssistMode effectiveMode = dialogueLine.overrideAssistMode ? dialogueLine.assistMode : currentAssistMode;
        if (effectiveMode == TranslatorAssistMode.Off)
            return $"{prefix} Try again. No Buddy help in this check.";

        if (effectiveMode == TranslatorAssistMode.Partial)
        {
            // Route practice intentionally gives a clue, never the exact answer.
            string fix = "Use the grammar clue and make one small change.";
            string why = string.IsNullOrWhiteSpace(feedback.why)
                ? ""
                : $" {BuildShortWhy(feedback.why)}";
            return $"{prefix} {fix}{why}";
        }

        string status = $"{prefix} What went wrong: {feedback.whatWasWrong}";
        if (!string.IsNullOrWhiteSpace(feedback.why))
            status += $" Why: {feedback.why}";
        if (!string.IsNullOrWhiteSpace(feedback.correctedResponse))
            status += $" Try: {feedback.correctedResponse}.";
        if (!string.IsNullOrWhiteSpace(feedback.microLesson))
            status += $" {feedback.microLesson}";
        return status;
    }

    public void RecordConversationTurn(
        string learnerMessage,
        string buddyResponse,
        float englishRatio,
        string conversationSkill = "",
        GrammarPhrasePattern grammarPattern = GrammarPhrasePattern.FullSentence,
        GrammarConceptId conceptId = GrammarConceptId.None,
        string wordChoiceIssue = "",
        string formationIssue = "",
        string errorCategory = "",
        string correctedResponse = "",
        IEnumerable<string> safeMemoryTags = null,
        IEnumerable<string> safetyFlags = null,
        string teacherNote = "",
        bool reportable = true,
        float responseSeconds = 0f)
    {
        CurriculumSessionManager.EnsureExists().RecordBuddyConversationTurn(
            learnerMessage,
            buddyResponse,
            sourceLanguage: preferredLanguage,
            targetLanguage: "en",
            englishRatio: englishRatio,
            conversationSkill: conversationSkill,
            grammarPattern: grammarPattern,
            conceptId: conceptId,
            wordChoiceIssue: wordChoiceIssue,
            formationIssue: formationIssue,
            errorCategory: errorCategory,
            hintLevelShown: string.IsNullOrWhiteSpace(errorCategory) ? TutorHintLevel.None.ToString() : TutorHintLevel.RuleHint.ToString(),
            remediationStep: string.IsNullOrWhiteSpace(errorCategory) ? TutorRemediationStep.None.ToString() : TutorRemediationStep.GuidedRetry.ToString(),
            correctedResponse: correctedResponse,
            safeMemoryTags: safeMemoryTags,
            safetyFlags: safetyFlags,
            teacherNote: teacherNote,
            buddyContractId: string.IsNullOrWhiteSpace(errorCategory) ? "free_buddy_chat" : "response_coach",
            promptTemplateId: "translator_buddy_conversation_v1",
            policyVersion: "buddy_policy_v1",
            reportable: reportable,
            responseSeconds: responseSeconds);
    }
}
