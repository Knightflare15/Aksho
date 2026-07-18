using System.Collections.Generic;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public partial class ChallengeMode
{
    void HandleHandwritingDevDiagnosticsChanged(bool visible)
    {
        ApplyHandwritingDevDiagnosticsVisibility();
    }

    void ApplyHandwritingDevDiagnosticsVisibility()
    {
        bool visible = GameSettings.HandwritingDevDiagnosticsVisible;
        formationCoach?.SetDeveloperDiagnosticsVisible(visible);
        notebookGuide?.SetDeveloperDiagnosticsVisible(visible);
    }

    void ApplyUITheme()
    {
        LayoutDrawingHeaderUi();
        GameUiTheme.StyleHudLabel(promptLabel, GameUiTheme.HudLabelRole.Prompt);
        GameUiTheme.StyleHudLabel(tierLabel, GameUiTheme.HudLabelRole.Status);
        GameUiTheme.StyleHudLabel(attemptsLabel, GameUiTheme.HudLabelRole.Status);
        if (voiceBadgePanel != null)
            GameUiTheme.StyleVoiceBadgePanel(voiceBadgePanel.gameObject);
        if (voiceBadgeStateLabel != null)
            GameUiTheme.StyleDrawingVoiceBadgeText(voiceBadgeStateLabel, emphasized: true);
        if (voiceBadgeDetailLabel != null)
            GameUiTheme.StyleDrawingVoiceBadgeText(voiceBadgeDetailLabel, emphasized: false);
    }

    void LayoutDrawingHeaderUi()
    {
        if (drawController == null || drawController.drawingPanel == null)
            return;

        RectTransform panel = drawController.drawingPanel;
        RectTransform parent = VoiceControlParent();
        if (parent == null)
            return;

        NotebookWritingGuide.NotebookSlot slot = NotebookWritingGuide.CalculateSlot(
            panel.rect,
            string.IsNullOrEmpty(targetWord) ? "C" : targetWord,
            Mathf.Clamp(letterIndex, 0, Mathf.Max(0, targetWord.Length - 1)));

        float pageTop = panel.anchoredPosition.y + panel.rect.yMax;
        float pageLeft = panel.anchoredPosition.x + panel.rect.xMin;
        float pageRight = panel.anchoredPosition.x + panel.rect.xMax;
        float upperRule = panel.anchoredPosition.y + slot.topY;
        float headerHeight = Mathf.Max(58f, pageTop - upperRule);
        float headerCenterY = upperRule + headerHeight * 0.5f;
        float inset = Mathf.Clamp(panel.rect.width * 0.035f, 24f, 58f);

        PlaceHeaderLabel(promptLabel, parent, new Vector2(pageLeft + inset, headerCenterY), new Vector2(310f, 70f), new Vector2(0f, 0.5f), TextAlignmentOptions.Left);
        PlaceHeaderLabel(tierLabel, parent, new Vector2(panel.anchoredPosition.x - 94f, headerCenterY), new Vector2(150f, 62f), new Vector2(0.5f, 0.5f), TextAlignmentOptions.Center);
        PlaceHeaderLabel(attemptsLabel, parent, new Vector2(panel.anchoredPosition.x + 94f, headerCenterY), new Vector2(170f, 62f), new Vector2(0.5f, 0.5f), TextAlignmentOptions.Center);

        if (hintButton != null)
            PlaceHintButtonInDrawingHeader(hintButton.GetComponent<RectTransform>());
    }

    void PlaceHeaderLabel(
        TextMeshProUGUI label,
        RectTransform parent,
        Vector2 anchoredPosition,
        Vector2 size,
        Vector2 pivot,
        TextAlignmentOptions alignment)
    {
        if (label == null)
            return;

        RectTransform rect = label.rectTransform;
        if (rect.parent != parent)
            rect.SetParent(parent, false);

        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
        label.alignment = alignment;
        label.verticalAlignment = VerticalAlignmentOptions.Middle;
    }

    enum FeedbackPopupTone
    {
        Guidance,
        Success,
        Warning,
        Error
    }

    void ShowFeedbackPopup(string title, string message, FeedbackPopupTone tone)
    {
        if (!showLargeFeedbackPopups || feedback == null)
        {
            Debug.Log($"[LearningFeedback] Popup skipped title='{title}' enabled={showLargeFeedbackPopups} feedbackAssigned={feedback != null} message='{message}'");
            return;
        }

        Debug.Log($"[LearningFeedback] Popup title='{title}' tone={tone} message='{message}'");
        feedback.ShowLargeFeedbackPopup(title, message, FeedbackPopupColor(tone));
    }

    Color FeedbackPopupColor(FeedbackPopupTone tone)
    {
        if (feedback == null)
            return GameUiTheme.Accent;

        return tone switch
        {
            FeedbackPopupTone.Success => feedback.colourCorrect,
            FeedbackPopupTone.Warning => feedback.colourWarm,
            FeedbackPopupTone.Error => feedback.colourWrong,
            _ => feedback.colourGuide,
        };
    }

    string BuildLetterScorePopup(
        char expected,
        float score,
        int tries,
        HandwritingDiagnosticSummary diagnostic)
    {
        string hintText = diagnostic != null && !string.IsNullOrWhiteSpace(diagnostic.primaryHint)
            ? diagnostic.primaryHint
            : "Shape accepted.";
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        return $"Expected {expected}\nScore {score:F1}   Try {tries}\nTags: {FormatDiagnosticTags(diagnostic)}\n{hintText}";
#else
        return tries <= 1
            ? $"Nice {expected}!\n{hintText}"
            : $"You formed {expected}.\n{hintText}";
#endif
    }

    string BuildLetterRetryPopup(
        char expected,
        string wrongName,
        float expectedScore,
        HandwritingDiagnosticSummary diagnostic)
    {
        string hintText = diagnostic != null && !string.IsNullOrWhiteSpace(diagnostic.primaryHint)
            ? diagnostic.primaryHint
            : hint;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        return $"Expected {expected}   Saw {wrongName}\nExpected-match score {expectedScore:F1}\nTags: {FormatDiagnosticTags(diagnostic)}\n{hintText}";
#else
        return $"Draw {expected} once more.\n{hintText}";
#endif
    }

    void BeginGuideForCurrentLetter()
    {
        if (letterIndex >= targetWord.Length)
        {
            HideGuide();
            return;
        }

        EnsureFormationCoach();
        if (formationCoach == null)
            return;

        Rect frame = NotebookWritingGuide.CalculateTemplateFrame(
            drawController.drawingPanel.rect,
            targetWord,
            letterIndex);
        formationCoach.SetGuideFrame(frame.center, frame.size);
        formationCoach.BeginLetter(targetWord[letterIndex]);
        formationCoach.SetDeveloperDiagnosticsVisible(GameSettings.HandwritingDevDiagnosticsVisible);
        formationCoach.SetStartMarkerEnabled(false);
        notebookGuide?.SetTarget(targetWord, letterIndex);
        notebookGuide?.SetDeveloperDiagnosticsVisible(GameSettings.HandwritingDevDiagnosticsVisible);
        lastFormationState = LetterFormationCoach.FormationState.Hidden;
        helpLevel = 0;
        StopGuidePulse();
        guidePulseRoutine = StartCoroutine(ShowGuideBriefly());
    }

    void HideGuide()
    {
        StopGuidePulse();
        if (formationCoach != null)
            formationCoach.Hide();
        if (notebookGuide != null)
            notebookGuide.Hide();
    }

    void ShowGuideForCurrentAttempt(int attemptCount)
    {
        EnsureFormationCoach();
        if (formationCoach == null)
            return;

        StopGuidePulse();
        helpLevel = Mathf.Max(helpLevel, attemptCount >= 2 ? 2 : 1);
        formationCoach.SetStartMarkerEnabled(true);
        if (helpLevel >= 2)
        {
            formationCoach.ShowTraceOverlay();
        }
        else
        {
            guidePulseRoutine = StartCoroutine(ShowGuideBriefly());
        }
    }

    IEnumerator ShowGuideBriefly()
    {
        if (formationCoach == null)
            yield break;

        const float previewDuration = 1.05f;
        const int steps = 18;

        for (int i = 0; i <= steps; i++)
        {
            float progress = i / (float)steps;
            formationCoach.ShowAnimatedDemo(progress);
            yield return new WaitForSeconds(previewDuration / steps);
        }

        yield return new WaitForSeconds(0.12f);

        if (formationCoach != null && helpLevel < 2)
            formationCoach.HideVisual();

        guidePulseRoutine = null;
    }

    void StopGuidePulse()
    {
        if (guidePulseRoutine == null)
            return;

        StopCoroutine(guidePulseRoutine);
        guidePulseRoutine = null;
    }

    string BuildPromptString()
    {
        var sb = new System.Text.StringBuilder("Write:  ");
        for (int i = 0; i < targetWord.Length; i++)
        {
            if (i < letterIndex)
                sb.Append($"<color=#55FF55>{targetWord[i]}</color>  ");
            else if (i == letterIndex)
                sb.Append("<color=#FFEE55>_</color>  ");
            else
                sb.Append("_  ");
        }

        return sb.ToString().TrimEnd();
    }
}
