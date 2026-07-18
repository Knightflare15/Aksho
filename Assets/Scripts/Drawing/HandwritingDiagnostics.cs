using System;
using System.Collections.Generic;
using UnityEngine;

public enum HandwritingDiagnosticTag
{
    None,
    Mirror,
    ReversedStroke,
    WrongStrokeOrder,
    Oversized,
    Undersized,
    AboveBaseline,
    BelowBaseline,
    Floating,
    SpacingDrift,
    ExtraStrokes,
    MissingStroke,
    Overdrawn,
    OpenLoop,
    ClosedTooSoon,
    WrongStart,
    WrongDirection,
    RepeatedCorrection,
    Slanted,
    Wobbly,
}

[Serializable]
public sealed class HandwritingDiagnosticEvidence
{
    public string tag = "";
    public string source = "";
    public float confidence;
    public bool actionable;
}

[Serializable]
public sealed class HandwritingDiagnosticSummary
{
    public string diagnosticSchemaVersion = "handwriting_diagnostics_v2";
    public string letter = "";
    public string targetWord = "";
    public int letterIndex;
    public int severity;
    public List<string> tags = new List<string>();
    public string primaryHint = "";
    public float primaryHintConfidence;
    public float diagnosticReliability;
    public List<HandwritingDiagnosticEvidence> evidence = new List<HandwritingDiagnosticEvidence>();
    public float boundsX;
    public float boundsY;
    public float boundsWidth;
    public float boundsHeight;
    public float slotCenterOffsetX;
    public float baselineOffset;
    public float lineOverflowTop;
    public float lineOverflowBottom;
    public float mirrorScore;
    public float normalScore;
    public float mirrorConfidence;
    public float wobbleScore;
    public float localRoughness;
    public float localKinkScore;
    public int localKinkCount;
    public float localKinkMax;
    public float perpendicularJitterScore;
    public float wobbleThresholdUsed;
    public float pointDensity;
    public float templateDeviationAverage;
    public float templateDeviationMax;
    public float templateNoiseScore;
    public float pathLength;
    public float directness;
    public int pointCount;
    public int strokeCount;
    public int expectedStrokeCount;
    public string formationState = "";
    public string formationDiagnostic = "";
    public float formationConfidence;
    public bool mirrorRecognized;
    public string mirroredRecognitionName = "";
    public float mirroredRecognitionScore;
    public string neuralRecognizedName = "";
    public float neuralConfidence;
    public float combinedConfidence;
    public bool recognizerAgreement;
    public string recognitionDecision = "";
    public bool accepted;
    public string assessmentOutcome = "";
    public string assessmentReason = "";
    public float assessmentConfidence;
    public string assessmentConfidenceKind = "relative_geometric_v1";
    public float calibratedAcceptanceThreshold;
    public float calibratedRejectionThreshold;
    public float calibrationSeparation;
    public bool calibrationReliable;
    public bool shapeSanityPassed;
    public bool broadRecognizerContradiction;
    public bool neuralRecognizerContradiction;
    public string broadRecognizedName = "";
    public float broadRecognitionScore;
    public bool inputAssisted;
    public bool materiallyInputAssisted;
    public float inputAssistFraction;
    public float inputAssistMeanDistance;
    public float inputAssistMaxDistance;

    public bool HasTag(HandwritingDiagnosticTag tag)
    {
        return tags != null && tags.Contains(ToTagString(tag));
    }

    public void AddTag(HandwritingDiagnosticTag tag)
    {
        string value = ToTagString(tag);
        if (string.IsNullOrEmpty(value))
            return;
        tags ??= new List<string>();
        if (!tags.Contains(value))
            tags.Add(value);
    }

    public static string ToTagString(HandwritingDiagnosticTag tag)
    {
        return tag switch
        {
            HandwritingDiagnosticTag.Mirror => "mirror",
            HandwritingDiagnosticTag.ReversedStroke => "reversedStroke",
            HandwritingDiagnosticTag.WrongStrokeOrder => "wrongStrokeOrder",
            HandwritingDiagnosticTag.Oversized => "oversized",
            HandwritingDiagnosticTag.Undersized => "undersized",
            HandwritingDiagnosticTag.AboveBaseline => "aboveBaseline",
            HandwritingDiagnosticTag.BelowBaseline => "belowBaseline",
            HandwritingDiagnosticTag.Floating => "floating",
            HandwritingDiagnosticTag.SpacingDrift => "spacingDrift",
            HandwritingDiagnosticTag.ExtraStrokes => "extraStrokes",
            HandwritingDiagnosticTag.MissingStroke => "missingStroke",
            HandwritingDiagnosticTag.Overdrawn => "overdrawn",
            HandwritingDiagnosticTag.OpenLoop => "openLoop",
            HandwritingDiagnosticTag.ClosedTooSoon => "closedTooSoon",
            HandwritingDiagnosticTag.WrongStart => "wrongStart",
            HandwritingDiagnosticTag.WrongDirection => "wrongDirection",
            HandwritingDiagnosticTag.RepeatedCorrection => "repeatedCorrection",
            HandwritingDiagnosticTag.Slanted => "slanted",
            HandwritingDiagnosticTag.Wobbly => "wobbly",
            _ => "",
        };
    }
}
