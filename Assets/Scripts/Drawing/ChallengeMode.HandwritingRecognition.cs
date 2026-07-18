using System.Collections.Generic;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public partial class ChallengeMode
{
    string PickWrongMessage(string wrong, char expected, FeedbackManager.Severity severity)
    {
        string[] pool = severity == FeedbackManager.Severity.Warm
            ? WrongMessagesWarm
            : severity == FeedbackManager.Severity.Wrong
                ? WrongMessagesMid
                : WrongMessagesVeryWrong;

        string template = pool[Random.Range(0, pool.Length)];
        return template
            .Replace("{wrong}", wrong)
            .Replace("{expected}", expected.ToString());
    }

    bool TryGetExpectedCandidateScore(
        PDollarRecognizer.RecognitionResult result,
        char expected,
        out float score,
        out bool matchedRunnerUp)
    {
        score = thresholdWrong;
        matchedRunnerUp = false;

        if (CandidateStartsWith(result.name, expected))
        {
            score = result.score;
            return true;
        }

        if (CandidateStartsWith(result.bestCandidateName, expected))
        {
            score = result.score;
            return true;
        }

        if (CandidateStartsWith(result.runnerUpName, expected))
        {
            score = result.runnerUpScore;
            matchedRunnerUp = true;
            return true;
        }

        return false;
    }

    static bool CanReuseExpectedRecognition(PDollarRecognizer.RecognitionResult result, char expected)
    {
        return result.expectedLetterFiltered &&
               char.ToUpperInvariant(result.expectedLetter) == char.ToUpperInvariant(expected);
    }

    PDollarRecognizer.RecognitionResult RecognizeExpectedLetter(List<List<Vector2>> rawStrokes, char expected)
    {
        List<PDollarRecognizer.Point> points = ToPDollarPoints(rawStrokes);
        if (drawController != null && drawController.recognizerHost != null)
        {
            Rect panelRect = drawController.drawingPanel != null
                ? drawController.drawingPanel.rect
                : new Rect(-125f, -125f, 250f, 250f);
            return drawController.recognizerHost.RecognizeAsLetter(
                points,
                expected,
                EffectiveDesiredLetterMatchThreshold,
                rawStrokes,
                panelRect,
                drawController.strokeThickness);
        }

        return new PDollarRecognizer.RecognitionResult
        {
            name = "Unknown",
            score = float.MaxValue,
            bestCandidateName = "Unknown",
            runnerUpName = "Unknown",
            runnerUpScore = float.MaxValue,
            scoreMargin = float.MaxValue,
            isAmbiguous = false
        };
    }

    PDollarRecognizer.RecognitionResult RecognizeMirroredExpectedLetter(List<List<Vector2>> rawStrokes, char expected)
    {
        List<PDollarRecognizer.Point> points = ToPDollarPoints(rawStrokes);
        if (drawController != null && drawController.recognizerHost != null)
            return drawController.recognizerHost.RecognizeMirroredAsLetter(points, expected, EffectiveDesiredLetterMatchThreshold);

        return new PDollarRecognizer.RecognitionResult
        {
            name = "Unknown",
            score = float.MaxValue,
            bestCandidateName = "Unknown",
            runnerUpName = "Unknown",
            runnerUpScore = float.MaxValue,
            scoreMargin = float.MaxValue,
            isAmbiguous = false
        };
    }

    bool IsExpectedLetterAccepted(PDollarRecognizer.RecognitionResult expectedMatch, char expected)
    {
        return expectedMatch.score <= EffectiveDesiredLetterMatchThreshold &&
               CandidateStartsWith(expectedMatch.bestCandidateName, expected);
    }

    void ApplyMirrorRecognitionDiagnostic(
        HandwritingDiagnosticSummary diagnostic,
        PDollarRecognizer.RecognitionResult mirroredExpectedMatch,
        bool alreadyAccepted)
    {
        if (diagnostic == null || alreadyAccepted)
            return;

        char expected = string.IsNullOrEmpty(diagnostic.letter) ? '?' : diagnostic.letter[0];
        float mirrorThreshold = mirroredExpectedMatch.calibratedThreshold > 0f
            ? mirroredExpectedMatch.calibratedThreshold
            : EffectiveDesiredLetterMatchThreshold;
        bool mirrorRecognized = mirroredExpectedMatch.score <= mirrorThreshold &&
                                CandidateStartsWith(mirroredExpectedMatch.bestCandidateName, expected);
        diagnostic.mirrorRecognized = mirrorRecognized;
        diagnostic.mirroredRecognitionName = mirroredExpectedMatch.bestCandidateName;
        diagnostic.mirroredRecognitionScore = mirroredExpectedMatch.score;
        if (mirrorRecognized)
        {
            diagnostic.AddTag(HandwritingDiagnosticTag.Mirror);
            diagnostic.primaryHint = $"That looks flipped. Start {diagnostic.letter} from the normal side.";
            diagnostic.severity = Mathf.Max(diagnostic.severity, 3);
        }
    }

    static void ApplyRecognitionFusionDiagnostic(
        HandwritingDiagnosticSummary diagnostic,
        PDollarRecognizer.RecognitionResult recognition)
    {
        if (diagnostic == null)
            return;

        diagnostic.neuralRecognizedName = recognition.neuralRecognizedName ?? "";
        diagnostic.neuralConfidence = recognition.neuralConfidence;
        diagnostic.combinedConfidence = recognition.combinedConfidence;
        diagnostic.recognizerAgreement = recognition.recognizerAgreement;
        diagnostic.recognitionDecision = recognition.recognitionDecision ?? "";
    }

    static void ApplyAssessmentDecision(
        HandwritingDiagnosticSummary diagnostic,
        PDollarRecognizer.RecognitionResult recognition,
        HandwritingAssessmentDecision decision)
    {
        if (diagnostic == null)
            return;
        diagnostic.accepted = decision.Accepted;
        diagnostic.assessmentOutcome = decision.outcome.ToString();
        diagnostic.assessmentReason = decision.reason;
        diagnostic.assessmentConfidence = decision.confidence;
        diagnostic.calibratedAcceptanceThreshold = decision.acceptanceThreshold;
        diagnostic.calibratedRejectionThreshold = decision.rejectionThreshold;
        diagnostic.calibrationSeparation = recognition.calibrationSeparation;
        diagnostic.calibrationReliable = recognition.calibrationReliable;
        diagnostic.shapeSanityPassed = decision.shapeSanityPassed;
        diagnostic.broadRecognizerContradiction = decision.broadContradiction;
        diagnostic.neuralRecognizerContradiction = decision.neuralContradiction;
        diagnostic.broadRecognizedName = string.IsNullOrWhiteSpace(recognition.broadBestCandidateName)
            ? recognition.broadRecognizedName ?? ""
            : recognition.broadBestCandidateName;
        diagnostic.broadRecognitionScore = recognition.broadScore;
        diagnostic.inputAssisted = recognition.inputAssisted;
        diagnostic.inputAssistFraction = recognition.inputAssistFraction;
        diagnostic.inputAssistMeanDistance = recognition.inputAssistMeanDistance;
        diagnostic.inputAssistMaxDistance = recognition.inputAssistMaxDistance;
        diagnostic.materiallyInputAssisted = HandwritingAcceptancePolicy.IsMaterialInputAssist(recognition);
    }

    static string BuildUncertainHandwritingHint(
        char expected,
        HandwritingAssessmentDecision decision,
        HandwritingDiagnosticSummary diagnostic)
    {
        if (decision.reason == "mostly_outside_writing_zone")
            return $"Keep '{expected}' inside its writing box and try once more.";
        if (decision.reason == "possible_mirror")
            return $"That may be flipped. Follow the start marker for '{expected}' once more.";
        if (diagnostic != null && !string.IsNullOrWhiteSpace(diagnostic.primaryHint))
            return diagnostic.primaryHint;
        return $"That is close to '{expected}'. Draw it once more slowly so I can check it fairly.";
    }

    static List<PDollarRecognizer.Point> ToPDollarPoints(List<List<Vector2>> strokes)
    {
        var points = new List<PDollarRecognizer.Point>();
        if (strokes == null)
            return points;

        for (int strokeIndex = 0; strokeIndex < strokes.Count; strokeIndex++)
        {
            List<Vector2> stroke = strokes[strokeIndex];
            if (stroke == null)
                continue;

            foreach (Vector2 point in stroke)
                points.Add(new PDollarRecognizer.Point(point.x, point.y, strokeIndex));
        }

        return points;
    }

    static bool CandidateStartsWith(string candidate, char expected)
    {
        return !string.IsNullOrEmpty(candidate) &&
               candidate != "Unknown" &&
               char.ToUpperInvariant(candidate[0]) == char.ToUpperInvariant(expected);
    }

    LetterFormationCoach.FormationResult CaptureFormationResult(
        List<List<Vector2>> rawStrokes,
        List<GameObject> strokeVisuals)
    {
        EnsureFormationCoach();
        if (formationCoach == null ||
            CountRawPoints(rawStrokes) < 2 ||
            !StrokeStartedInInputBounds(rawStrokes))
            return default;

        LetterFormationCoach.FormationResult result = formationCoach.LastResult;
        if (result.state == LetterFormationCoach.FormationState.Hidden ||
            result.diagnostic == LetterFormationCoach.DiagnosticTag.None)
        {
            result = formationCoach.UpdateStroke(rawStrokes, strokeVisuals, attemptsUsed, helpLevel);
        }

        return result;
    }

    HandwritingDiagnosticSummary BuildDiagnostic(
        char expected,
        List<List<Vector2>> rawStrokes,
        LetterFormationCoach.FormationResult formationResult,
        bool accepted)
    {
        return HandwritingDiagnosticAnalyzer.Analyze(
            expected,
            targetWord,
            letterIndex,
            rawStrokes,
            drawController != null ? drawController.drawingPanel : null,
            templateLibrary,
            formationResult,
            accepted);
    }

    void RecordLetterDiagnostic(
        char expected,
        bool accepted,
        int attempts,
        float confidenceScore,
        HandwritingDiagnosticSummary diagnostic,
        List<List<Vector2>> rawStrokes,
        PDollarRecognizer.RecognitionResult recognition,
        HandwritingAssessmentDecision assessment)
    {
        CurriculumSessionManager curriculum = CurriculumSessionManager.EnsureExists();

        bool confident = accepted && (diagnostic == null || diagnostic.severity < 3);
        curriculum.RecordLetterAttempt(
            expected,
            confident,
            attempts,
            giftedLetters > 0,
            confidenceScore,
            diagnostic,
            rawStrokes,
            recognition,
            assessment.outcome.ToString(),
            assessment.reason);
    }

    void LogHandwritingAttempt(
        PDollarRecognizer.RecognitionResult broadRecognition,
        PDollarRecognizer.RecognitionResult expectedRecognition,
        PDollarRecognizer.RecognitionResult mirroredExpectedRecognition,
        char expected,
        bool accepted,
        int attemptNumber,
        float matchedScore,
        LetterFormationCoach.FormationResult formationResult,
        HandwritingDiagnosticSummary diagnostic)
    {
        if (diagnostic == null)
        {
            Debug.Log($"[HandwritingAttempt] word={targetWord} letter={expected} index={letterIndex} attempt={attemptNumber} accepted={accepted} no diagnostic summary.");
            return;
        }

        bool correctStrokePath = !HasAnyDiagnostic(
            diagnostic,
            HandwritingDiagnosticTag.WrongStart,
            HandwritingDiagnosticTag.WrongDirection,
            HandwritingDiagnosticTag.ReversedStroke,
            HandwritingDiagnosticTag.WrongStrokeOrder,
            HandwritingDiagnosticTag.MissingStroke,
            HandwritingDiagnosticTag.ExtraStrokes,
            HandwritingDiagnosticTag.Overdrawn);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[HandwritingAttempt] word={targetWord} letter={expected} index={letterIndex} attempt={attemptNumber} accepted={accepted}");
        sb.AppendLine($"  Broad P$: name={SafeCandidate(broadRecognition.name)} best={SafeCandidate(broadRecognition.bestCandidateName)} runnerUp={SafeCandidate(broadRecognition.runnerUpName)} score={broadRecognition.score:F1} margin={broadRecognition.scoreMargin:F1} ambiguous={broadRecognition.isAmbiguous}");
        sb.AppendLine($"  Expected-letter P$: best={SafeCandidate(expectedRecognition.bestCandidateName)} score={expectedRecognition.score:F1} threshold={EffectiveDesiredLetterMatchThreshold:F1} acceptedByGate={accepted}");
        sb.AppendLine($"  Tiny NN: best={SafeCandidate(expectedRecognition.neuralRecognizedName)} confidence={expectedRecognition.neuralConfidence:F2} combined={expectedRecognition.combinedConfidence:F2} agreement={YesNo(expectedRecognition.recognizerAgreement)} decision={expectedRecognition.recognitionDecision}");
        sb.AppendLine($"  Assessment: outcome={diagnostic.assessmentOutcome} reason={diagnostic.assessmentReason} confidence={diagnostic.assessmentConfidence:F2} acceptThreshold={diagnostic.calibratedAcceptanceThreshold:F1} rejectThreshold={diagnostic.calibratedRejectionThreshold:F1} rawInput={YesNo(!diagnostic.inputAssisted)}");
        sb.AppendLine($"  Broad P$: best={SafeCandidate(diagnostic.broadRecognizedName)} score={diagnostic.broadRecognitionScore:F1} contradiction={YesNo(diagnostic.broadRecognizerContradiction)} CNN contradiction={YesNo(diagnostic.neuralRecognizerContradiction)}");
        sb.AppendLine($"  Mirrored expected P$: recognized={YesNo(diagnostic.mirrorRecognized)} best={SafeCandidate(mirroredExpectedRecognition.bestCandidateName)} score={mirroredExpectedRecognition.score:F1}");
        sb.AppendLine($"  Tags: {FormatDiagnosticTags(diagnostic)} severity={diagnostic.severity} hint=\"{diagnostic.primaryHint}\"");
        sb.AppendLine($"  Stroke made correct way: {YesNo(correctStrokePath)} formationState={formationResult.state} formationDiagnostic={formationResult.diagnostic} formationConfidence={formationResult.confidence:F2}");
        sb.AppendLine($"  Flipped/mirror: {YesNo(diagnostic.HasTag(HandwritingDiagnosticTag.Mirror))} normalScore={diagnostic.normalScore:F1} mirrorScore={diagnostic.mirrorScore:F1}");
        sb.AppendLine($"  Ruled-line fit: bounds=({diagnostic.boundsX:F1},{diagnostic.boundsY:F1},{diagnostic.boundsWidth:F1},{diagnostic.boundsHeight:F1}) topOverflow={diagnostic.lineOverflowTop:F1} bottomOverflow={diagnostic.lineOverflowBottom:F1} baselineOffset={diagnostic.baselineOffset:F1}");
        sb.AppendLine($"  Spacing: centerOffsetX={diagnostic.slotCenterOffsetX:F1} spacingDrift={YesNo(diagnostic.HasTag(HandwritingDiagnosticTag.SpacingDrift))} slanted={YesNo(diagnostic.HasTag(HandwritingDiagnosticTag.Slanted))}");
        sb.AppendLine($"  Strokes: actual={diagnostic.strokeCount} expected={diagnostic.expectedStrokeCount} reversed={YesNo(diagnostic.HasTag(HandwritingDiagnosticTag.ReversedStroke))} wrongOrder={YesNo(diagnostic.HasTag(HandwritingDiagnosticTag.WrongStrokeOrder))}");
        sb.AppendLine($"  Page-template deviation: avg={diagnostic.templateDeviationAverage:F1} max={diagnostic.templateDeviationMax:F1} templateNoise={diagnostic.templateNoiseScore:F1}");
        sb.AppendLine($"  Wobbliness: wobbly={YesNo(diagnostic.HasTag(HandwritingDiagnosticTag.Wobbly))} wobbleScore={diagnostic.wobbleScore:F2} threshold={diagnostic.wobbleThresholdUsed:F2} localKinkScore={diagnostic.localKinkScore:F2} localKinkCount={diagnostic.localKinkCount} localKinkMax={diagnostic.localKinkMax:F2} perpJitter={diagnostic.perpendicularJitterScore:F2} legacyLocalRoughness={diagnostic.localRoughness:F2} pointDensity={diagnostic.pointDensity:F1}/100px directness={diagnostic.directness:F2} pathLength={diagnostic.pathLength:F1} points={diagnostic.pointCount}");
        Debug.Log(sb.ToString());
    }

    static bool HasAnyDiagnostic(HandwritingDiagnosticSummary diagnostic, params HandwritingDiagnosticTag[] tags)
    {
        if (diagnostic == null || tags == null)
            return false;

        foreach (HandwritingDiagnosticTag tag in tags)
            if (diagnostic.HasTag(tag))
                return true;
        return false;
    }

    static string FormatDiagnosticTags(HandwritingDiagnosticSummary diagnostic)
    {
        if (diagnostic?.tags == null || diagnostic.tags.Count == 0)
            return "none";
        return string.Join(", ", diagnostic.tags);
    }

    static string SafeCandidate(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "Unknown" : value;
    }

    static string YesNo(bool value)
    {
        return value ? "yes" : "no";
    }

    int CountRawPoints(List<List<Vector2>> strokes)
    {
        int count = 0;
        if (strokes == null)
            return count;
        foreach (List<Vector2> stroke in strokes)
            if (stroke != null)
                count += stroke.Count;
        return count;
    }

    bool StrokeStartedInInputBounds(List<List<Vector2>> strokes)
    {
        if (strokes == null || !TryGetInputBounds(out Rect bounds))
            return false;

        foreach (List<Vector2> stroke in strokes)
        {
            if (stroke == null || stroke.Count == 0)
                continue;

            return bounds.Contains(stroke[0]);
        }

        return false;
    }

    string BuildAcceptedFormationNote(char expected, HandwritingDiagnosticSummary diagnostic)
    {
        if (diagnostic != null && !string.IsNullOrWhiteSpace(diagnostic.primaryHint))
            return diagnostic.primaryHint;

        return "";
    }

    float EstimateAcceptedScore(float matchedScore)
    {
        return SanitizeRecognitionScore(matchedScore);
    }
}
