using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public partial class NpcDialogueUI
{
static TextMeshProUGUI MakeLabel(RectTransform parent, string name, Vector2 offsetMax, Vector2 offsetMin, float size, bool bold, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = new Vector2(offsetMax.x, -Mathf.Abs(offsetMin.y));
        text.rectTransform.offsetMax = new Vector2(offsetMin.x, offsetMax.y);
        text.alignment = TextAlignmentOptions.TopLeft;
        text.textWrappingMode = TextWrappingModes.Normal;
        GameUiTheme.StyleText(text, size, bold);
        text.color = color;
        return text;
    }

    static Button MakeButton(RectTransform parent, string name, string text, Vector2 anchoredPosition, Vector2 size, out TextMeshProUGUI label)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = size;

        Button button = go.GetComponent<Button>();
        GameUiTheme.StyleButton(button, GameUiTheme.ButtonRole.Secondary);

        GameObject labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(go.transform, false);
        label = labelGo.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.alignment = TextAlignmentOptions.Center;
        label.verticalAlignment = VerticalAlignmentOptions.Middle;
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = Vector2.zero;
        label.rectTransform.offsetMax = Vector2.zero;
        GameUiTheme.StyleText(label, 19f, true);
        return button;
    }

    static TMP_InputField MakeInput(RectTransform parent, string name, Vector2 anchoredPosition, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = size;
        Image image = go.GetComponent<Image>();
        image.color = new Color(0.04f, 0.11f, 0.16f, 0.98f);

        GameObject textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(go.transform, false);
        TextMeshProUGUI text = textGo.GetComponent<TextMeshProUGUI>();
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = new Vector2(16f, 8f);
        text.rectTransform.offsetMax = new Vector2(-16f, -8f);
        text.alignment = TextAlignmentOptions.MidlineLeft;
        GameUiTheme.StyleText(text, 20f, false);

        GameObject placeholderGo = new GameObject("Placeholder", typeof(RectTransform), typeof(TextMeshProUGUI));
        placeholderGo.transform.SetParent(go.transform, false);
        TextMeshProUGUI placeholder = placeholderGo.GetComponent<TextMeshProUGUI>();
        placeholder.rectTransform.anchorMin = Vector2.zero;
        placeholder.rectTransform.anchorMax = Vector2.one;
        placeholder.rectTransform.offsetMin = new Vector2(16f, 8f);
        placeholder.rectTransform.offsetMax = new Vector2(-16f, -8f);
        placeholder.text = "Draw your English answer";
        placeholder.alignment = TextAlignmentOptions.MidlineLeft;
        GameUiTheme.StyleText(placeholder, 20f, false);
        placeholder.color = new Color(0.72f, 0.78f, 0.82f, 0.62f);

        TMP_InputField input = go.GetComponent<TMP_InputField>();
        input.textViewport = rt;
        input.textComponent = text;
        input.placeholder = placeholder;
        input.lineType = TMP_InputField.LineType.SingleLine;
        return input;
    }

    static bool IsAcceptedResponse(LocalizedDialogueLine line, string response)
    {
        string normalized = NormalizeResponse(response);
        if (string.IsNullOrWhiteSpace(normalized) || line == null)
            return false;

        foreach (string accepted in BuildAcceptedResponses(line))
        {
            if (NormalizeResponse(accepted) == normalized)
                return true;
        }

        return false;
    }

    static bool IsAcceptedTypedResponse(LocalizedDialogueLine line, string response)
    {
        if (line == null)
            return false;

        // Only the sentence-start/end concept assesses the learner's exact
        // capitalization and terminal punctuation. Typed FITB/transcript
        // repairs should use the same case-insensitive accepted-answer set as
        // their spoken equivalents, including authored alternatives and the
        // derived missing-word answer.
        if (line.conceptId != GrammarConceptId.SentenceStartEnd)
            return IsAcceptedResponse(line, response);

        string normalized = NormalizeTypedSentence(response);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (string.Equals(
                NormalizeTypedSentence(line.expectedEnglishResponse),
                normalized,
                StringComparison.Ordinal))
        {
            return true;
        }

        if (line.acceptedEnglishResponses == null)
            return false;

        foreach (string accepted in line.acceptedEnglishResponses)
        {
            if (string.Equals(
                    NormalizeTypedSentence(accepted),
                    normalized,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    static string NormalizeResponse(string response)
    {
        return VoiceUnlockRecognizer.NormalizeKeyword(response);
    }

    static bool UsesTypedSentenceInput(LocalizedDialogueLine line)
    {
        if (line == null ||
            line.inputMode == GrammarDialogueInputMode.SpeakOnly ||
            line.inputMode == GrammarDialogueInputMode.None ||
            line.malfunctionType == GrammarDialogueMalfunctionType.ScrambledSentence)
        {
            return false;
        }

        return line.conceptId == GrammarConceptId.SentenceStartEnd ||
               line.malfunctionType == GrammarDialogueMalfunctionType.MissingWord ||
               line.malfunctionType == GrammarDialogueMalfunctionType.HeardWrong ||
               line.malfunctionType == GrammarDialogueMalfunctionType.PartialTranscript;
    }

    internal static string NormalizeTypedSentence(string response)
    {
        string value = (response ?? "")
            .Trim()
            .Replace('’', '\'')
            .Replace('。', '.')
            .Replace('！', '!')
            .Replace('？', '?');
        value = System.Text.RegularExpressions.Regex.Replace(value, @"\s+", " ");
        value = System.Text.RegularExpressions.Regex.Replace(value, @"\s+([.!?])", "$1");
        return value;
    }

    static string BuildRejectionReason(LocalizedDialogueLine line, bool spoken)
    {
        if (line == null)
            return "response_mismatch";

        if (spoken && line.inputMode == GrammarDialogueInputMode.WriteOnly)
            return "input_mode_write_required";
        if (!spoken && line.inputMode == GrammarDialogueInputMode.SpeakOnly)
            return "input_mode_speak_required";
        if (!spoken && line.conceptId == GrammarConceptId.SentenceStartEnd)
            return "sentence_case_or_punctuation_mismatch";

        TranslatorAssistMode assistMode = line.overrideAssistMode ? line.assistMode : TranslatorAssistMode.Full;
        if (assistMode == TranslatorAssistMode.Off)
            return "gym_answer_mismatch";

        return line.malfunctionType switch
        {
            GrammarDialogueMalfunctionType.MissingWord => "missing_word_mismatch",
            GrammarDialogueMalfunctionType.ScrambledSentence => "scrambled_sentence_mismatch",
            GrammarDialogueMalfunctionType.HeardWrong => "heard_wrong_correction_mismatch",
            GrammarDialogueMalfunctionType.PartialTranscript => "partial_transcript_mismatch",
            _ => "response_mismatch",
        };
    }

    string BuildCorrectionStatus(LocalizedDialogueLine line, string submittedOrHeard, string rejectionReason, TutorFeedbackPlan feedback = null)
    {
        string observed = string.IsNullOrWhiteSpace(submittedOrHeard)
            ? "nothing clear"
            : rejectionReason == "sentence_case_or_punctuation_mismatch"
                ? NormalizeTypedSentence(submittedOrHeard)
                : NormalizeResponse(submittedOrHeard);
        string prefix = $"Heard {observed}.";
        if (line == null)
            return $"{prefix} Try again.";
        if (rejectionReason == "sentence_case_or_punctuation_mismatch")
            return $"{prefix} Check the capital at the start and the full stop at the end.";

        if (currentBuddy != null && feedback != null)
            return currentBuddy.BuildTutorFeedbackStatus(line, feedback);

        TranslatorAssistMode assistMode = line.overrideAssistMode ? line.assistMode : TranslatorAssistMode.Full;
        if (assistMode == TranslatorAssistMode.Off)
            return $"{prefix} Not quite. Try again without Buddy help in this check.";
        if (assistMode == TranslatorAssistMode.Partial)
            return $"{prefix} Use the grammar clue and make one small change before trying again.";

        string expected = line.expectedEnglishResponse ?? "";
        return rejectionReason switch
        {
            "missing_word_mismatch" => $"{prefix} Fill the blank with the clean answer: {expected}.",
            "scrambled_sentence_mismatch" => $"{prefix} Unscramble into: {expected}.",
            "heard_wrong_correction_mismatch" => $"{prefix} Correct what the transcript got wrong: {expected}.",
            "partial_transcript_mismatch" => $"{prefix} Complete the transcript as: {expected}.",
            "input_mode_write_required" => $"{prefix} This task needs writing.",
            "input_mode_speak_required" => $"{prefix} This task needs speech.",
            _ => $"{prefix} Expected: {expected}.",
        };
    }

    static string ResolveNpcLine(LocalizedDialogueLine line)
    {
        if (line == null)
            return "";
        return string.IsNullOrWhiteSpace(line.npcLine) ? line.sourceText : line.npcLine;
    }

    PronunciationInsightResult? ResolveLatestPronunciationInsight()
    {
        if (lastResponsePronunciationInsight.HasValue)
            return lastResponsePronunciationInsight;
        if (responseRecognizer == null)
            return null;

        PronunciationInsightResult insight = responseRecognizer.LastPronunciationInsight;
        if (string.IsNullOrWhiteSpace(insight.ProviderName) &&
            string.IsNullOrWhiteSpace(insight.TargetWord) &&
            string.IsNullOrWhiteSpace(insight.RawRecognizedText) &&
            string.IsNullOrWhiteSpace(insight.Message))
            return null;
        return insight;
    }

    void RecordSpokenPhrase(string phrase, bool accepted, string rejectionReason, PronunciationInsightResult? pronunciationInsight = null, TutorFeedbackPlan feedback = null)
    {
        CurriculumSessionManager curriculum = CurriculumSessionManager.EnsureExists();
        if (currentLine == null)
            return;

        curriculum.RecordSpokenPhraseEvent(
            phrase,
            currentLine.grammarPattern,
            accepted,
            rejectionReason,
            currentNpc != null ? currentNpc.sceneKind : SemanticZoneKind.Town,
            GrammarWorldProgressService.Instance.Data?.currentAreaId ?? "",
            responseSeconds: ConsumeResponseSeconds(),
            conceptId: currentLine.conceptId,
            errorCategory: feedback != null ? feedback.errorCategory : rejectionReason,
            hintLevelShown: feedback != null ? feedback.hintLevelShown.ToString() : "",
            remediationStep: feedback != null ? feedback.remediationStep.ToString() : "",
            correctedResponse: feedback != null ? feedback.correctedResponse : currentLine.expectedEnglishResponse ?? "",
            pronunciationInsight: pronunciationInsight,
            submittedPhrase: phrase,
            targetPhrase: currentLine.expectedEnglishResponse ?? "",
            dialogueTaskId: string.IsNullOrWhiteSpace(currentLine.dialogueTaskId) ? currentLine.lineId : currentLine.dialogueTaskId,
            inputSource: currentNpc != null && currentNpc.sceneKind == SemanticZoneKind.Gym ? "gym_dialogue" : "npc_dialogue",
            activityType: ResolveBuddyActivityType(currentLine),
            questionPrompt: ResolveNpcLine(currentLine),
            grimoireReference: $"grimoire:{currentLine.conceptId}",
            hintCount: feedback != null ? 1 : 0,
            pronunciationAudioWavBytes: responseRecognizer != null
                ? responseRecognizer.GetLastCapturedPronunciationWav()
                : Array.Empty<byte>(),
            requestPronunciationAnalysis: accepted);
    }

    void RecordWrittenPhrase(string phrase, bool accepted, string rejectionReason, TutorFeedbackPlan feedback = null)
    {
        CurriculumSessionManager curriculum = CurriculumSessionManager.EnsureExists();
        if (currentLine == null)
            return;

        curriculum.RecordWrittenPhraseEvent(
            phrase,
            currentLine.grammarPattern,
            accepted,
            rejectionReason,
            currentNpc != null ? currentNpc.sceneKind : SemanticZoneKind.Town,
            GrammarWorldProgressService.Instance.Data?.currentAreaId ?? "",
            responseSeconds: ConsumeResponseSeconds(),
            conceptId: currentLine.conceptId,
            errorCategory: feedback != null ? feedback.errorCategory : rejectionReason,
            hintLevelShown: feedback != null ? feedback.hintLevelShown.ToString() : "",
            remediationStep: feedback != null ? feedback.remediationStep.ToString() : "",
            correctedResponse: feedback != null ? feedback.correctedResponse : currentLine.expectedEnglishResponse ?? "",
            submittedPhrase: phrase,
            targetPhrase: currentLine.expectedEnglishResponse ?? "",
            dialogueTaskId: string.IsNullOrWhiteSpace(currentLine.dialogueTaskId) ? currentLine.lineId : currentLine.dialogueTaskId,
            inputSource: currentNpc != null && currentNpc.sceneKind == SemanticZoneKind.Gym ? "gym_dialogue" : "npc_dialogue",
            activityType: ResolveBuddyActivityType(currentLine),
            questionPrompt: ResolveNpcLine(currentLine),
            grimoireReference: $"grimoire:{currentLine.conceptId}",
            hintCount: feedback != null ? 1 : 0);
    }

    float ConsumeResponseSeconds()
    {
        float startedAt = responseAttemptStartedAt > 0f ? responseAttemptStartedAt : Time.unscaledTime;
        float elapsed = Mathf.Max(0f, Time.unscaledTime - startedAt);
        responseAttemptStartedAt = Time.unscaledTime;
        return elapsed;
    }

    static string ResolveBuddyActivityType(LocalizedDialogueLine line)
    {
        if (line == null)
            return "npc_question";
        return line.malfunctionType switch
        {
            GrammarDialogueMalfunctionType.MissingWord => "subtitle_fill_blank",
            GrammarDialogueMalfunctionType.ScrambledSentence => "subtitle_unjumble",
            GrammarDialogueMalfunctionType.HeardWrong => "subtitle_correction",
            GrammarDialogueMalfunctionType.PartialTranscript => "subtitle_completion",
            _ => "npc_question",
        };
    }
}
