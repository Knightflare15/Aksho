using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class HandwritingAcceptancePolicyTests
{
    static readonly Rect WritingBounds = new Rect(-120f, -120f, 240f, 240f);

    [Test]
    public void TinyTapIsRejectedBeforeRecognitionCanAcceptIt()
    {
        PDollarRecognizer.RecognitionResult recognition = Match("I", 20f, 300f);
        var strokes = new List<List<Vector2>>
        {
            new List<Vector2> { Vector2.zero, new Vector2(1f, 1f) }
        };

        HandwritingAssessmentDecision decision = HandwritingAcceptancePolicy.Evaluate(
            'I', recognition, strokes, WritingBounds);

        Assert.AreEqual(HandwritingAssessmentOutcome.Reject, decision.outcome);
        Assert.IsFalse(decision.shapeSanityPassed);
    }

    [Test]
    public void GoodExpectedMatchIsAccepted()
    {
        PDollarRecognizer.RecognitionResult recognition = Match("A", 90f, 300f);
        recognition.broadBestCandidateName = "A";
        recognition.broadScore = 85f;
        recognition.broadConfidence = 0.8f;
        recognition.neuralRecognizedName = "A";
        recognition.neuralConfidence = 0.82f;

        HandwritingAssessmentDecision decision = HandwritingAcceptancePolicy.Evaluate(
            'A', recognition, ValidStroke(), WritingBounds);

        Assert.AreEqual(HandwritingAssessmentOutcome.Accept, decision.outcome);
    }

    [Test]
    public void StrongCnnDisagreementRequestsRetryInsteadOfCallingChildWrong()
    {
        PDollarRecognizer.RecognitionResult recognition = Match("A", 200f, 300f);
        recognition.broadBestCandidateName = "A";
        recognition.broadScore = 190f;
        recognition.broadConfidence = 0.55f;
        recognition.neuralRecognizedName = "H";
        recognition.neuralConfidence = 0.9f;

        HandwritingAssessmentDecision decision = HandwritingAcceptancePolicy.Evaluate(
            'A', recognition, ValidStroke(), WritingBounds);

        Assert.AreEqual(HandwritingAssessmentOutcome.Retry, decision.outcome);
        Assert.AreEqual("cnn_disagrees", decision.reason);
    }

    [Test]
    public void ExcellentGeometricMatchIsNotVetoedByOutOfDomainCnn()
    {
        PDollarRecognizer.RecognitionResult recognition = Match("A", 70f, 300f);
        recognition.neuralRecognizedName = "H";
        recognition.neuralConfidence = 0.95f;

        HandwritingAssessmentDecision decision = HandwritingAcceptancePolicy.Evaluate(
            'A', recognition, ValidStroke(), WritingBounds);

        Assert.AreEqual(HandwritingAssessmentOutcome.Accept, decision.outcome);
    }

    [Test]
    public void StrongUnconstrainedContradictionRequestsRetry()
    {
        PDollarRecognizer.RecognitionResult recognition = Match("A", 230f, 300f);
        recognition.broadBestCandidateName = "H";
        recognition.broadScore = 90f;
        recognition.broadConfidence = 0.82f;

        HandwritingAssessmentDecision decision = HandwritingAcceptancePolicy.Evaluate(
            'A', recognition, ValidStroke(), WritingBounds);

        Assert.AreEqual(HandwritingAssessmentOutcome.Retry, decision.outcome);
        Assert.AreEqual("broad_recognizer_disagrees", decision.reason);
    }

    [Test]
    public void MostlyOutsideWritingZoneRequestsRetry()
    {
        PDollarRecognizer.RecognitionResult recognition = Match("A", 80f, 300f);
        var outside = new List<List<Vector2>>
        {
            new List<Vector2>
            {
                new Vector2(180f, 180f), new Vector2(210f, 220f),
                new Vector2(230f, 170f), new Vector2(250f, 230f)
            }
        };

        HandwritingAssessmentDecision decision = HandwritingAcceptancePolicy.Evaluate(
            'A', recognition, outside, WritingBounds);

        Assert.AreEqual(HandwritingAssessmentOutcome.Retry, decision.outcome);
        Assert.AreEqual("mostly_outside_writing_zone", decision.reason);
    }

    [Test]
    public void WeakTemplateCalibrationCannotHardRejectByItself()
    {
        PDollarRecognizer.RecognitionResult recognition = Match("A", 420f, 250f);
        recognition.calibrationReliable = false;

        HandwritingAssessmentDecision decision = HandwritingAcceptancePolicy.Evaluate(
            'A', recognition, ValidStroke(), WritingBounds);

        Assert.AreEqual(HandwritingAssessmentOutcome.Retry, decision.outcome);
        Assert.AreEqual(PDollarRecognizer.SCORE_THRESHOLD, decision.rejectionThreshold);
    }

    [Test]
    public void WellSeparatedCalibrationCanRejectAClearlyDistantShape()
    {
        PDollarRecognizer.RecognitionResult recognition = Match("A", 420f, 250f);
        recognition.calibrationReliable = true;

        HandwritingAssessmentDecision decision = HandwritingAcceptancePolicy.Evaluate(
            'A', recognition, ValidStroke(), WritingBounds);

        Assert.AreEqual(HandwritingAssessmentOutcome.Reject, decision.outcome);
    }

    [Test]
    public void MinorMagneticMovementDoesNotCountAsMaterialHelp()
    {
        PDollarRecognizer.RecognitionResult recognition = Match("A", 80f, 300f);
        recognition.inputAssisted = true;
        recognition.inputAssistFraction = 0.05f;
        recognition.inputAssistMeanDistance = 1.2f;
        recognition.inputAssistMaxDistance = 4f;

        Assert.IsFalse(HandwritingAcceptancePolicy.IsMaterialInputAssist(recognition));
    }

    [Test]
    public void SustainedMagneticMovementCountsAsMaterialHelp()
    {
        PDollarRecognizer.RecognitionResult recognition = Match("A", 80f, 300f);
        recognition.inputAssisted = true;
        recognition.inputAssistFraction = 0.30f;
        recognition.inputAssistMeanDistance = 2f;
        recognition.inputAssistMaxDistance = 6f;

        Assert.IsTrue(HandwritingAcceptancePolicy.IsMaterialInputAssist(recognition));
    }

    [Test]
    public void DiagnosticEvidenceSeparatesActionableGeometryFromRawTags()
    {
        var strokes = new List<List<Vector2>>
        {
            new List<Vector2>
            {
                new Vector2(-190f, -190f), new Vector2(-120f, 190f),
                new Vector2(0f, -170f), new Vector2(120f, 190f),
                new Vector2(190f, -190f)
            }
        };

        HandwritingDiagnosticSummary diagnostic = HandwritingDiagnosticAnalyzer.Analyze(
            'A', "A", 0, strokes, null, null, default, accepted: false);

        Assert.AreEqual("handwriting_diagnostics_v2", diagnostic.diagnosticSchemaVersion);
        Assert.IsNotEmpty(diagnostic.evidence);
        Assert.That(diagnostic.diagnosticReliability, Is.InRange(0f, 1f));
        Assert.IsTrue(diagnostic.evidence.Exists(item => item.actionable));
        Assert.IsNotEmpty(diagnostic.primaryHint);
    }

    [Test]
    public void EvidenceDownsamplingPreservesShortStrokesAndNormalizedCoordinates()
    {
        var longStroke = new List<Vector2>();
        for (int i = 0; i < 100; i++)
            longStroke.Add(new Vector2(i, i * 0.5f));
        var strokes = new List<List<Vector2>>
        {
            longStroke,
            new List<Vector2> { new Vector2(15f, 70f), new Vector2(35f, 70f) }
        };

        List<HandwritingPointRecord> sampled = CurriculumSessionManager.BuildBoundedHandwritingPoints(strokes, 16);

        Assert.AreEqual(16, sampled.Count);
        Assert.AreEqual(2, sampled.FindAll(point => point.strokeId == 1).Count);
        Assert.IsTrue(sampled.TrueForAll(point => point.nx >= 0f && point.nx <= 1f && point.ny >= 0f && point.ny <= 1f));
        Assert.AreEqual(Vector2.zero.x, sampled[0].x, 0.001f);
        Assert.AreEqual(99f, sampled.FindLast(point => point.strokeId == 0).x, 0.001f);
    }

    [Test]
    public void RichCaptureSchemaKeepsRecognitionCompatibleCoordinates()
    {
        var capture = new HandwritingCaptureSession();
        capture.BeginStroke();
        capture.AddPoint(new Vector2(-30f, -40f));
        capture.AddPoint(new Vector2(0f, 50f));
        capture.AddPoint(new Vector2(30f, -40f));
        HandwritingSampleRecord sample = capture.Build(
            "A", new Rect(-100f, -100f, 200f, 200f), "writer-test", "session-test", "unit_test");

        Assert.AreEqual(HandwritingSampleRecord.CurrentSchemaVersion, sample.schemaVersion);
        Assert.AreEqual(3, sample.pointCount);
        Assert.AreEqual(1, sample.strokeCount);
        Assert.AreEqual("A", sample.expectedLetter);
        Assert.AreEqual("canvas_center_y_up", sample.rawCoordinateSystem);
        Assert.AreEqual("unit_square_bottom_left_y_up", sample.normalizedCoordinateSystem);
        Assert.AreEqual(0f, sample.points[0].ny, 0.001f);
        Assert.AreEqual(1f, sample.points[1].ny, 0.001f);
        Assert.IsTrue(sample.points.TrueForAll(point =>
            point.nx >= 0f && point.nx <= 1f &&
            point.ny >= 0f && point.ny <= 1f &&
            point.canvasX >= 0f && point.canvasX <= 1f &&
            point.canvasY >= 0f && point.canvasY <= 1f));

        var legacy = new List<PDollarRecognizer.Point>();
        foreach (HandwritingCapturePoint point in sample.points)
            legacy.Add(new PDollarRecognizer.Point(point.x, point.y, point.strokeId));
        Assert.AreEqual(sample.pointCount, legacy.Count);
        Assert.AreEqual(sample.points[1].x, legacy[1].x, 0.001f);
        Assert.AreEqual(sample.points[1].strokeId, legacy[1].id);
    }

    [Test]
    public void RichEvidenceDownsamplingPreservesTimingAndPointerMetadata()
    {
        var sample = new HandwritingSampleRecord
        {
            points = new List<HandwritingCapturePoint>
            {
                new HandwritingCapturePoint { x = 0f, y = 0f, strokeId = 0, order = 0, tMs = 0, inputType = "touch", pointerId = 3, pressure = 0.4f },
                new HandwritingCapturePoint { x = 10f, y = 20f, strokeId = 0, order = 1, tMs = 20, inputType = "touch", pointerId = 3, pressure = 0.5f },
                new HandwritingCapturePoint { x = 20f, y = 0f, strokeId = 0, order = 2, tMs = 40, inputType = "touch", pointerId = 3, pressure = 0.6f }
            }
        };

        List<HandwritingPointRecord> points = CurriculumSessionManager.BuildBoundedHandwritingPoints(sample, 16);

        Assert.AreEqual(3, points.Count);
        Assert.AreEqual("touch", points[1].inputType);
        Assert.AreEqual(3, points[1].pointerId);
        Assert.AreEqual(20L, points[1].tMs);
        Assert.AreEqual(20L, points[1].deltaMs);
        Assert.AreEqual(0.5f, points[1].pressure, 0.001f);
    }

    [Test]
    public void RichCaptureSchemaSurvivesUnityJsonRoundTrip()
    {
        var sample = new HandwritingSampleRecord
        {
            sampleId = "sample-json",
            writerId = "writer-json",
            sessionId = "session-json",
            expectedLetter = "Q",
            points = new List<HandwritingCapturePoint>
            {
                new HandwritingCapturePoint
                {
                    x = 12f, y = -4f, nx = 0.25f, ny = 0.75f,
                    canvasX = 0.4f, canvasY = 0.6f, tMs = 123L,
                    deltaMs = 23L, pressure = 0.7f, strokeId = 0, order = 0,
                    inputType = "stylus"
                }
            }
        };

        string json = JsonUtility.ToJson(sample);
        HandwritingSampleRecord restored = JsonUtility.FromJson<HandwritingSampleRecord>(json);

        Assert.AreEqual(HandwritingSampleRecord.CurrentSchemaVersion, restored.schemaVersion);
        Assert.AreEqual("sample-json", restored.sampleId);
        Assert.AreEqual(123L, restored.points[0].tMs);
        Assert.AreEqual("stylus", restored.points[0].inputType);
        Assert.AreEqual(0.7f, restored.points[0].pressure, 0.001f);
    }

    [Test]
    public void AuthoredAlphabetSurvivesDeterministicTouchscreenPerturbation()
    {
        var recognizer = new PDollarRecognizer();
        recognizer.LoadDefaultAlphabetTemplates();
        var seen = new HashSet<char>();
        foreach ((string name, List<PDollarRecognizer.Point> points) in PDollarRecognizer.CreateDefaultAlphabetTemplateSnapshot())
        {
            if (string.IsNullOrWhiteSpace(name) || points == null || points.Count < 2)
                continue;
            char expected = char.ToUpperInvariant(name[0]);
            if (!char.IsLetter(expected) || !seen.Add(expected))
                continue;

            List<PDollarRecognizer.Point> perturbed = Perturb(points);
            PDollarRecognizer.RecognitionResult result = recognizer.Recognize(perturbed);
            Assert.AreEqual(expected, char.ToUpperInvariant(result.bestCandidateName[0]),
                $"Perturbed authored {expected} was closest to {result.bestCandidateName} (score {result.score:F1}).");
        }
        Assert.AreEqual(26, seen.Count);
    }

    static PDollarRecognizer.RecognitionResult Match(string name, float score, float threshold)
    {
        return new PDollarRecognizer.RecognitionResult
        {
            name = name,
            bestCandidateName = name,
            score = score,
            calibratedThreshold = threshold,
            broadBestCandidateName = name,
            broadScore = score,
            broadConfidence = 0.6f,
            calibrationReliable = true
        };
    }

    static List<List<Vector2>> ValidStroke()
    {
        return new List<List<Vector2>>
        {
            new List<Vector2>
            {
                new Vector2(-55f, -70f), new Vector2(0f, 75f),
                new Vector2(55f, -70f), new Vector2(30f, -10f),
                new Vector2(-30f, -10f)
            }
        };
    }

    static List<PDollarRecognizer.Point> Perturb(List<PDollarRecognizer.Point> points)
    {
        var result = new List<PDollarRecognizer.Point>(points.Count);
        for (int i = 0; i < points.Count; i++)
        {
            PDollarRecognizer.Point point = points[i];
            float x = point.x * 1.06f + point.y * 0.045f + Mathf.Sin(i * 0.73f) * 1.2f + 8f;
            float y = point.y * 0.94f + Mathf.Cos(i * 0.51f) * 1.1f - 5f;
            result.Add(new PDollarRecognizer.Point(x, y, point.id));
        }
        return result;
    }
}
