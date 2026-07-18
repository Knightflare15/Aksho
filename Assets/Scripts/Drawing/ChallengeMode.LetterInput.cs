using System.Collections.Generic;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public partial class ChallengeMode
{
    public char OnLetterConfirmed(
        PDollarRecognizer.RecognitionResult result,
        List<GameObject> strokes)
    {
        return OnLetterConfirmed(
            result,
            strokes,
            drawController != null ? drawController.CopyCurrentLetterStrokes() : null);
    }

    public char OnLetterConfirmed(
        PDollarRecognizer.RecognitionResult result,
        List<GameObject> strokeVisuals,
        List<List<Vector2>> rawStrokes)
    {
        if (PauseMenuController.IsPaused)
            return '\0';

        if (!speechUnlocked)
        {
            if (feedback != null)
                feedback.PlayGuidanceFeedback(FeedbackManager.GuidanceState.Drifting, strokeVisuals);

            hint = BuildSpeechHint();
            ShowFeedbackPopup("Speech First", hint, FeedbackPopupTone.Guidance);
            return '\0';
        }

        if (letterIndex >= targetWord.Length)
            return '\0';

        char expected = targetWord[letterIndex];
        PDollarRecognizer.RecognitionResult expectedMatch = CanReuseExpectedRecognition(result, expected)
            ? result
            : RecognizeExpectedLetter(rawStrokes, expected);
        LetterFormationCoach.FormationResult formationResult = CaptureFormationResult(rawStrokes, strokeVisuals);
        lastLetterDiagnostic = BuildDiagnostic(expected, rawStrokes, formationResult, false);
        ApplyRecognitionFusionDiagnostic(lastLetterDiagnostic, expectedMatch);
        Rect inputBounds = TryGetInputBounds(out Rect resolvedBounds) ? resolvedBounds : default;
        HandwritingAssessmentDecision assessment = HandwritingAcceptancePolicy.Evaluate(
            expected,
            expectedMatch,
            rawStrokes,
            inputBounds,
            lastLetterDiagnostic);
        PDollarRecognizer.RecognitionResult mirroredExpectedMatch = default;
        if (!assessment.Accepted)
        {
            mirroredExpectedMatch = RecognizeMirroredExpectedLetter(rawStrokes, expected);
            ApplyMirrorRecognitionDiagnostic(lastLetterDiagnostic, mirroredExpectedMatch, false);
            assessment = HandwritingAcceptancePolicy.Evaluate(
                expected,
                expectedMatch,
                rawStrokes,
                inputBounds,
                lastLetterDiagnostic);
        }
        ApplyAssessmentDecision(lastLetterDiagnostic, expectedMatch, assessment);
        bool acceptedByRecognition = assessment.Accepted;

        if (acceptedByRecognition)
        {
            if (result.isAmbiguous || !CandidateStartsWith(result.name, expected))
                hint = $"Nice. I found '{expected}' as a close match.";

            LogHandwritingAttempt(result, expectedMatch, mirroredExpectedMatch, expected, true, attemptsUsed + 1, expectedMatch.score, formationResult, lastLetterDiagnostic);
            RecordLetterDiagnostic(
                expected,
                true,
                attemptsUsed + 1,
                assessment.confidence,
                lastLetterDiagnostic,
                rawStrokes,
                expectedMatch,
                assessment);
            ShowFeedbackPopup(
                $"Letter {expected} Accepted",
                BuildLetterScorePopup(expected, expectedMatch.score, attemptsUsed + 1, lastLetterDiagnostic),
                FeedbackPopupTone.Success);
            return AcceptLetter(
                expected,
                strokeVisuals,
                EstimateAcceptedScore(expectedMatch.score),
                attemptsUsed + 1,
                BuildAcceptedFormationNote(expected, lastLetterDiagnostic));
        }

        bool uncertainRetry = assessment.outcome == HandwritingAssessmentOutcome.Retry;
        if (!uncertainRetry)
            wrongLetterAttempts++;
        attemptsUsed++;
        LogHandwritingAttempt(result, expectedMatch, mirroredExpectedMatch, expected, false, attemptsUsed, expectedMatch.score, formationResult, lastLetterDiagnostic);
        RecordLetterDiagnostic(
            expected,
            false,
            attemptsUsed,
            assessment.confidence,
            lastLetterDiagnostic,
            rawStrokes,
            expectedMatch,
            assessment);

        var severity = uncertainRetry
            ? FeedbackManager.Severity.Warm
            : expectedMatch.score <= thresholdWarm
            ? FeedbackManager.Severity.Warm
            : expectedMatch.score <= thresholdWrong
                ? FeedbackManager.Severity.Wrong
                : FeedbackManager.Severity.VeryWrong;

        if (feedback != null)
        {
            var guidanceState = severity == FeedbackManager.Severity.Warm
                ? FeedbackManager.GuidanceState.Drifting
                : FeedbackManager.GuidanceState.OffTrack;
            feedback.PlayGuidanceFeedback(guidanceState, strokeVisuals);
        }

        formationCoach?.HideVisual();

        string wrongName = result.name != "Unknown" && result.name.Length > 0
            ? result.name
            : "something else";
        hint = uncertainRetry
            ? BuildUncertainHandwritingHint(expected, assessment, lastLetterDiagnostic)
            : lastLetterDiagnostic != null && !string.IsNullOrWhiteSpace(lastLetterDiagnostic.primaryHint)
            ? lastLetterDiagnostic.primaryHint
            : PickWrongMessage(wrongName, expected, severity);
        ShowFeedbackPopup(
            uncertainRetry ? $"Check {expected} Once More" : $"Try {expected} Again",
            BuildLetterRetryPopup(expected, wrongName, expectedMatch.score, lastLetterDiagnostic),
            severity == FeedbackManager.Severity.Warm ? FeedbackPopupTone.Warning : FeedbackPopupTone.Error);

        if (attemptsLabel != null)
            attemptsLabel.text = $"Tries left: {maxAttempts - attemptsUsed}";

        if (attemptsUsed >= maxAttempts)
        {
            giftedLetters++;
            hint = $"This one is '{expected}'. Trace the shape once, then try it your way.";
            attemptsUsed = 0;
            ShowFeedbackPopup("Trace Help", hint, FeedbackPopupTone.Guidance);
            ShowGuideForCurrentAttempt(maxAttempts);
            return '\0';
        }

        ShowGuideForCurrentAttempt(attemptsUsed);
        return '\0';
    }

    public bool TryGetExpectedLetterRecognition(out char expectedLetter, out float scoreThreshold)
    {
        expectedLetter = '\0';
        scoreThreshold = EffectiveDesiredLetterMatchThreshold;

        if (!speechUnlocked || letterIndex < 0 || string.IsNullOrEmpty(targetWord) || letterIndex >= targetWord.Length)
            return false;

        expectedLetter = targetWord[letterIndex];
        if (drawController != null && drawController.recognizerHost != null)
            scoreThreshold = drawController.recognizerHost.ResolveExpectedLetterThreshold(
                expectedLetter,
                EffectiveDesiredLetterMatchThreshold);
        return char.IsLetter(expectedLetter);
    }

    public void OnWordSubmitted(string word)
    {
        if (PauseMenuController.IsPaused)
            return;

        bool correct = word.ToUpperInvariant() == targetWord;
        float duration = Time.unscaledTime - sessionStartedAt;
        float speechUnlockSeconds = speechUnlockedAt >= 0f
            ? speechUnlockedAt - sessionStartedAt
            : -1f;

        progressStore.Record(new SpellLessonProgressStore.RunRecord
        {
            word = targetWord,
            success = correct,
            durationSeconds = duration,
            speechUnlockSeconds = speechUnlockSeconds,
            wrongLetters = wrongLetterAttempts,
            guideCorrections = guideCorrections,
            giftedLetters = giftedLetters,
        });

        spellPerformanceTracker?.RecordAttempt(
            targetWord,
            correct,
            AverageAcceptedScore(),
            AverageAcceptedTriesPerLetter(),
            wrongLetterAttempts,
            giftedLetters,
            duration);

        if (correct)
        {
            LastSubmissionWasCorrect = true;
            LastAwardedShots = CalculateShotAward();
            if (feedback != null)
                feedback.PlaySuccessFeedback();

            bool grammarBattleWord = IsGrammarBattleForgeMode();
            hint = grammarBattleWord
                ? $"You wrote \"{targetWord}\". Sending it to battle."
                : $"You spelled \"{targetWord}\". {LastAwardedShots} shot{(LastAwardedShots == 1 ? "" : "s")} charged.";
            ShowFeedbackPopup(
                grammarBattleWord ? "Battle Word Ready" : "Spell Charged",
                grammarBattleWord
                    ? $"{targetWord}\nSpeech and writing accepted\nAverage score {LastAverageLetterScore:F1}"
                    : $"{targetWord}\n{LastAwardedShots} shot{(LastAwardedShots == 1 ? "" : "s")} charged\nAverage score {LastAverageLetterScore:F1}",
                FeedbackPopupTone.Success);
            OnWordCompleted?.Invoke(ResolveWordActionPhrase(targetWord));
            if (learningProfile == null)
                learningProfile = FindAnyObjectByType<PlayerLearningProfile>();
            if (requestedForgeMode == ForgePageMode.LetterPage && targetWord.Length == 1)
            {
                CurriculumSessionManager curriculum = CurriculumSessionManager.Instance;
                if (curriculum == null || !curriculum.IsSchoolModeActive)
                    learningProfile?.RecordLetterFormation(targetWord[0], AverageAcceptedTriesPerLetter() <= 2f, Mathf.RoundToInt(AverageAcceptedTriesPerLetter()), giftedLetters > 0);
            }
            else
                learningProfile?.RecordSpecialForge(targetWord);
        }
        else
        {
            LastSubmissionWasCorrect = false;
            LastAwardedShots = 0;
            hint = $"Good try. The word for this encounter is \"{targetWord}\".";
            ShowFeedbackPopup("Word Check", hint, FeedbackPopupTone.Warning);
        }
    }

    public void OnLiveStrokeUpdated(List<List<Vector2>> strokes, List<GameObject> strokeVisuals)
    {
        if (!speechUnlocked || letterIndex >= targetWord.Length)
            return;

        if (!StrokeStartedInInputBounds(strokes))
            return;

        if (guidePulseRoutine != null)
        {
            StopGuidePulse();
            if (helpLevel >= 2)
                formationCoach?.ShowTraceOverlay();
            else if (helpLevel > 0)
                formationCoach?.ShowCorridorOverlay();
            else
                formationCoach?.HideVisual();
        }

        EnsureFormationCoach();
        if (formationCoach == null)
            return;

        var result = formationCoach.UpdateStroke(strokes, strokeVisuals, attemptsUsed, helpLevel);
        var guidanceState = result.state;

        if (guidanceState != lastFormationState &&
            (guidanceState == LetterFormationCoach.FormationState.NeedsNudge ||
             guidanceState == LetterFormationCoach.FormationState.NeedsHelp))
        {
            guideCorrections++;
        }

        lastFormationState = guidanceState;
    }

    public bool TryAdjustStrokePoint(
        Vector2 rawPoint,
        Vector2 previousPoint,
        bool isStrokeStart,
        out Vector2 adjustedPoint)
    {
        adjustedPoint = rawPoint;
        if (!speechUnlocked || letterIndex >= targetWord.Length)
            return false;

        EnsureFormationCoach();
        return formationCoach != null &&
               formationCoach.TryAdjustStrokePoint(rawPoint, previousPoint, isStrokeStart, out adjustedPoint);
    }

    public bool TryGetInputBounds(out Rect bounds)
    {
        bounds = default;
        if (!speechUnlocked ||
            string.IsNullOrEmpty(targetWord) ||
            letterIndex >= targetWord.Length ||
            drawController == null ||
            drawController.drawingPanel == null)
            return false;

        NotebookWritingGuide.NotebookSlot slot = NotebookWritingGuide.CalculateSlot(
            drawController.drawingPanel.rect,
            targetWord,
            letterIndex);
        bounds = slot.slotRect;
        return bounds.width > 1f && bounds.height > 1f;
    }

    public void OnLetterAccepted(
        char letter,
        List<List<Vector2>> strokes,
        PDollarRecognizer.RecognitionResult result)
    {
        CurriculumSessionManager curriculum = CurriculumSessionManager.Instance;
        if (curriculum != null && curriculum.IsSchoolModeActive)
        {
            curriculum.RecordAcceptedTemplate(
                letter,
                strokes,
                result,
                targetWord,
                Mathf.Max(0, letterIndex - 1),
                acceptedLetterTries.Count > 0 ? acceptedLetterTries[acceptedLetterTries.Count - 1] : attemptsUsed + 1,
                giftedLetters > 0,
                lastLetterDiagnostic);
        }
    }
}
