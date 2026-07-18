using System;
using System.Collections.Generic;
using UnityEngine;

public enum HandwritingAssessmentOutcome
{
    Accept,
    Retry,
    Reject,
}

public readonly struct HandwritingAssessmentDecision
{
    public readonly HandwritingAssessmentOutcome outcome;
    public readonly string reason;
    public readonly float confidence;
    public readonly float acceptanceThreshold;
    public readonly float rejectionThreshold;
    public readonly bool shapeSanityPassed;
    public readonly bool broadContradiction;
    public readonly bool neuralContradiction;

    public bool Accepted => outcome == HandwritingAssessmentOutcome.Accept;

    public HandwritingAssessmentDecision(
        HandwritingAssessmentOutcome outcome,
        string reason,
        float confidence,
        float acceptanceThreshold,
        float rejectionThreshold,
        bool shapeSanityPassed,
        bool broadContradiction,
        bool neuralContradiction)
    {
        this.outcome = outcome;
        this.reason = reason ?? "";
        this.confidence = Mathf.Clamp01(confidence);
        this.acceptanceThreshold = acceptanceThreshold;
        this.rejectionThreshold = rejectionThreshold;
        this.shapeSanityPassed = shapeSanityPassed;
        this.broadContradiction = broadContradiction;
        this.neuralContradiction = neuralContradiction;
    }
}

/// <summary>
/// A conservative fusion policy for expected-letter gameplay. P$ remains the
/// primary recognizer; unconstrained P$, CNN disagreement, and basic path sanity
/// can request a retry but cannot independently label a child's letter as wrong.
/// </summary>
public static class HandwritingAcceptancePolicy
{
    const float StrongNeuralConfidence = 0.72f;

    public static bool IsMaterialInputAssist(PDollarRecognizer.RecognitionResult recognition)
    {
        return (recognition.inputAssistFraction >= 0.15f && recognition.inputAssistMeanDistance >= 1.5f) ||
            recognition.inputAssistMaxDistance >= 10f;
    }

    public static HandwritingAssessmentDecision Evaluate(
        char expectedLetter,
        PDollarRecognizer.RecognitionResult recognition,
        List<List<Vector2>> rawStrokes,
        Rect inputBounds,
        HandwritingDiagnosticSummary diagnostic = null)
    {
        char expected = char.ToUpperInvariant(expectedLetter);
        float acceptThreshold = recognition.calibratedThreshold > 0f
            ? recognition.calibratedThreshold
            : Mathf.Min(PDollarRecognizer.SCORE_THRESHOLD, 420f);
        float rejectThreshold = recognition.calibrationReliable
            ? Mathf.Min(
                PDollarRecognizer.SCORE_THRESHOLD,
                acceptThreshold + Mathf.Max(70f, acceptThreshold * 0.22f))
            : PDollarRecognizer.SCORE_THRESHOLD;
        StrokeSanity sanity = MeasureSanity(rawStrokes, inputBounds);
        bool broadContradiction = IsStrongBroadContradiction(expected, recognition, acceptThreshold);
        bool neuralContradiction = IsStrongNeuralContradiction(expected, recognition);
        float confidence = ScoreConfidence(recognition.score, rejectThreshold);

        if (!sanity.passed)
            return Decision(HandwritingAssessmentOutcome.Reject, sanity.reason, confidence);

        if (recognition.score > rejectThreshold || float.IsInfinity(recognition.score) || float.IsNaN(recognition.score))
            return Decision(HandwritingAssessmentOutcome.Reject, "expected_shape_too_far", confidence);

        if (diagnostic != null &&
            diagnostic.HasTag(HandwritingDiagnosticTag.Mirror) &&
            diagnostic.mirrorConfidence >= 0.65f)
            return Decision(HandwritingAssessmentOutcome.Retry, "possible_mirror", confidence);

        bool excellentExpectedFit = recognition.score <= acceptThreshold * 0.48f;
        if (!excellentExpectedFit && broadContradiction)
            return Decision(HandwritingAssessmentOutcome.Retry, "broad_recognizer_disagrees", confidence);
        if (!excellentExpectedFit && neuralContradiction)
            return Decision(HandwritingAssessmentOutcome.Retry, "cnn_disagrees", confidence);
        if (!excellentExpectedFit && (recognition.isAmbiguous || recognition.broadIsAmbiguous))
            return Decision(HandwritingAssessmentOutcome.Retry, "recognizers_uncertain", confidence);
        if (sanity.outsideFraction > 0.35f)
            return Decision(HandwritingAssessmentOutcome.Retry, "mostly_outside_writing_zone", confidence);
        if (recognition.score <= acceptThreshold)
            return Decision(HandwritingAssessmentOutcome.Accept, "calibrated_expected_match", confidence);

        return Decision(HandwritingAssessmentOutcome.Retry, "near_expected_shape", confidence);

        HandwritingAssessmentDecision Decision(HandwritingAssessmentOutcome outcome, string reason, float scoreConfidence)
        {
            return new HandwritingAssessmentDecision(
                outcome,
                reason,
                scoreConfidence,
                acceptThreshold,
                rejectThreshold,
                sanity.passed,
                broadContradiction,
                neuralContradiction);
        }
    }

    static bool IsStrongBroadContradiction(
        char expected,
        PDollarRecognizer.RecognitionResult recognition,
        float threshold)
    {
        string broad = Known(recognition.broadBestCandidateName)
            ? recognition.broadBestCandidateName
            : recognition.broadRecognizedName;
        if (!Known(broad) || char.ToUpperInvariant(broad[0]) == expected)
            return false;
        float advantage = recognition.score - recognition.broadScore;
        return recognition.broadConfidence >= 0.45f &&
            advantage >= Mathf.Max(28f, threshold * 0.10f);
    }

    static bool IsStrongNeuralContradiction(char expected, PDollarRecognizer.RecognitionResult recognition)
    {
        string neural = recognition.neuralRecognizedName;
        return Known(neural) &&
            char.ToUpperInvariant(neural[0]) != expected &&
            recognition.neuralConfidence >= StrongNeuralConfidence;
    }

    static float ScoreConfidence(float score, float rejectionThreshold)
    {
        if (float.IsNaN(score) || float.IsInfinity(score))
            return 0f;
        return Mathf.Clamp01(1f - Mathf.Max(0f, score) / Mathf.Max(1f, rejectionThreshold));
    }

    static bool Known(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && value != "Unknown";
    }

    static StrokeSanity MeasureSanity(List<List<Vector2>> strokes, Rect inputBounds)
    {
        int points = 0;
        float pathLength = 0f;
        int outside = 0;
        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minY = float.MaxValue;
        float maxY = float.MinValue;
        if (strokes != null)
        {
            foreach (List<Vector2> stroke in strokes)
            {
                if (stroke == null)
                    continue;
                for (int i = 0; i < stroke.Count; i++)
                {
                    Vector2 point = stroke[i];
                    points++;
                    if (inputBounds.width > 1f && inputBounds.height > 1f && !inputBounds.Contains(point))
                        outside++;
                    minX = Mathf.Min(minX, point.x);
                    maxX = Mathf.Max(maxX, point.x);
                    minY = Mathf.Min(minY, point.y);
                    maxY = Mathf.Max(maxY, point.y);
                    if (i > 0)
                        pathLength += Vector2.Distance(point, stroke[i - 1]);
                }
            }
        }

        float referenceSize = inputBounds.width > 1f && inputBounds.height > 1f
            ? Mathf.Max(inputBounds.width, inputBounds.height)
            : 220f;
        float diagonal = points > 0
            ? new Vector2(Mathf.Max(0f, maxX - minX), Mathf.Max(0f, maxY - minY)).magnitude
            : 0f;
        if (points < 3)
            return new StrokeSanity(false, "too_few_points", 0f);
        if (pathLength < Mathf.Max(10f, referenceSize * 0.055f))
            return new StrokeSanity(false, "stroke_too_short", outside / (float)Mathf.Max(1, points));
        if (diagonal < Mathf.Max(8f, referenceSize * 0.075f))
            return new StrokeSanity(false, "drawing_too_small", outside / (float)Mathf.Max(1, points));
        return new StrokeSanity(true, "", outside / (float)Mathf.Max(1, points));
    }

    readonly struct StrokeSanity
    {
        public readonly bool passed;
        public readonly string reason;
        public readonly float outsideFraction;

        public StrokeSanity(bool passed, string reason, float outsideFraction)
        {
            this.passed = passed;
            this.reason = reason;
            this.outsideFraction = Mathf.Clamp01(outsideFraction);
        }
    }
}
