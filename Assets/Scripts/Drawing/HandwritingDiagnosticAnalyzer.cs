using System;
using System.Collections.Generic;
using UnityEngine;

public static class HandwritingDiagnosticAnalyzer
{
    const float MirrorMargin = 18f;
    const float KinkTurnToleranceDegrees = 18f;
    const float KinkWindowThreshold = 0.30f;
    const float WobbleScoreThreshold = 0.30f;
    const float MinWobblePathLength = 55f;
    const int KinkWindowRadius = 2;
    const int MaxKinkSamples = 96;

    public static HandwritingDiagnosticSummary Analyze(
        char expectedLetter,
        string targetWord,
        int letterIndex,
        List<List<Vector2>> strokes,
        RectTransform panel,
        TemplateLibrary templateLibrary,
        LetterFormationCoach.FormationResult formationResult,
        bool accepted)
    {
        char letter = char.ToUpperInvariant(expectedLetter);
        List<Vector2> flattened = Flatten(strokes);
        Rect panelRect = panel != null ? panel.rect : new Rect(-200f, -200f, 400f, 400f);
        NotebookWritingGuide.NotebookSlot slot = NotebookWritingGuide.CalculateSlot(panelRect, targetWord, letterIndex);
        Rect templateFrame = NotebookWritingGuide.CalculateTemplateFrame(panelRect, targetWord, letterIndex);
        List<List<Vector2>> pageTemplate = BuildPageTemplate(letter, templateLibrary, templateFrame);
        Rect bounds = Bounds(flattened);

        var summary = new HandwritingDiagnosticSummary
        {
            letter = letter.ToString(),
            targetWord = SpellRegistry.NormalizeWord(targetWord),
            letterIndex = Mathf.Max(0, letterIndex),
            boundsX = bounds.x,
            boundsY = bounds.y,
            boundsWidth = bounds.width,
            boundsHeight = bounds.height,
            slotCenterOffsetX = bounds.width > 0f ? bounds.center.x - slot.slotRect.center.x : 0f,
            baselineOffset = bounds.height > 0f ? bounds.yMin - slot.baselineY : 0f,
            lineOverflowTop = Mathf.Max(0f, bounds.yMax - slot.topY),
            lineOverflowBottom = Mathf.Max(0f, slot.bottomY - bounds.yMin),
            pointCount = flattened.Count,
            strokeCount = CountNonEmpty(strokes),
            formationState = formationResult.state.ToString(),
            formationDiagnostic = formationResult.diagnostic.ToString(),
            formationConfidence = formationResult.confidence,
            accepted = accepted,
            severity = 0,
        };

        if (bounds.width <= 0f || bounds.height <= 0f)
            return summary;

        AddSpatialTags(summary, bounds, slot, strokes);
        AddTemplateDeviationTags(summary, strokes, pageTemplate);
        AddFormationTags(summary, formationResult);
        AddMirrorAndOrderTags(summary, letter, strokes, pageTemplate);
        AddWobbleTags(summary, strokes, pageTemplate, templateFrame);
        BuildDiagnosticEvidence(summary);
        summary.primaryHint = BuildHint(summary);
        summary.primaryHintConfidence = ResolvePrimaryHintConfidence(summary);
        summary.severity = ResolveSeverity(summary);
        return summary;
    }

    static void AddSpatialTags(HandwritingDiagnosticSummary summary, Rect bounds, NotebookWritingGuide.NotebookSlot slot, List<List<Vector2>> strokes)
    {
        float writingHeight = Mathf.Max(1f, slot.topY - slot.baselineY);
        float slotWidth = Mathf.Max(1f, slot.slotRect.width);

        if (bounds.height > writingHeight * 1.35f || summary.lineOverflowTop > 8f || summary.lineOverflowBottom > 8f)
            summary.AddTag(HandwritingDiagnosticTag.Oversized);
        if (bounds.height < writingHeight * 0.42f)
            summary.AddTag(HandwritingDiagnosticTag.Undersized);
        if (bounds.yMax < slot.midlineY + writingHeight * 0.18f)
            summary.AddTag(HandwritingDiagnosticTag.Floating);
        if (summary.baselineOffset > writingHeight * 0.22f)
            summary.AddTag(HandwritingDiagnosticTag.AboveBaseline);
        if (summary.lineOverflowBottom > 8f)
            summary.AddTag(HandwritingDiagnosticTag.BelowBaseline);
        if (Mathf.Abs(summary.slotCenterOffsetX) > slotWidth * 0.24f || bounds.xMin < slot.slotRect.xMin - 8f || bounds.xMax > slot.slotRect.xMax + 8f)
            summary.AddTag(HandwritingDiagnosticTag.SpacingDrift);

        Vector2 diagonal = LongestStrokeDirection(strokes);
        if (Mathf.Abs(diagonal.x) > 0.58f && Mathf.Abs(diagonal.y) > 0.58f && bounds.height > writingHeight * 0.62f)
            summary.AddTag(HandwritingDiagnosticTag.Slanted);
    }

    static void AddFormationTags(HandwritingDiagnosticSummary summary, LetterFormationCoach.FormationResult result)
    {
        switch (result.diagnostic)
        {
            case LetterFormationCoach.DiagnosticTag.MissingFoot:
                summary.AddTag(HandwritingDiagnosticTag.MissingStroke);
                break;
            case LetterFormationCoach.DiagnosticTag.OpenLoop:
                summary.AddTag(HandwritingDiagnosticTag.OpenLoop);
                break;
            case LetterFormationCoach.DiagnosticTag.ClosingTooSoon:
                summary.AddTag(HandwritingDiagnosticTag.ClosedTooSoon);
                break;
            case LetterFormationCoach.DiagnosticTag.Overdrawn:
                summary.AddTag(HandwritingDiagnosticTag.Overdrawn);
                break;
            case LetterFormationCoach.DiagnosticTag.WrongStart:
                summary.AddTag(HandwritingDiagnosticTag.WrongStart);
                break;
            case LetterFormationCoach.DiagnosticTag.WrongDirection:
                summary.AddTag(HandwritingDiagnosticTag.WrongDirection);
                break;
        }
    }

    static void AddMirrorAndOrderTags(
        HandwritingDiagnosticSummary summary,
        char letter,
        List<List<Vector2>> strokes,
        List<List<Vector2>> pageTemplate)
    {
        if (pageTemplate == null || pageTemplate.Count == 0)
            return;

        List<Vector2> candidate = Normalize(Flatten(strokes), 48, 250f);
        List<Vector2> guide = Normalize(Flatten(pageTemplate), 48, 250f);
        if (candidate.Count == 0 || guide.Count != candidate.Count)
            return;

        List<Vector2> mirroredGuide = MirrorX(guide);
        summary.normalScore = AverageDistance(candidate, guide);
        summary.mirrorScore = AverageDistance(candidate, mirroredGuide);
        float mirrorAdvantage = summary.normalScore - summary.mirrorScore;
        summary.mirrorConfidence = Mathf.InverseLerp(MirrorMargin, MirrorMargin + 40f, mirrorAdvantage);
        if (IsMirrorSensitive(letter) && mirrorAdvantage > MirrorMargin)
            summary.AddTag(HandwritingDiagnosticTag.Mirror);

        if (strokes != null && pageTemplate.Count > 0)
        {
            int actualStrokeCount = CountNonEmpty(strokes);
            int expectedStrokeCount = CountNonEmpty(pageTemplate);
            summary.strokeCount = actualStrokeCount;
            summary.expectedStrokeCount = expectedStrokeCount;
            if (actualStrokeCount > expectedStrokeCount)
                summary.AddTag(HandwritingDiagnosticTag.RepeatedCorrection);
            if (actualStrokeCount > expectedStrokeCount + 1)
                summary.AddTag(HandwritingDiagnosticTag.ExtraStrokes);
            if (actualStrokeCount < Mathf.Max(1, expectedStrokeCount - 1))
                summary.AddTag(HandwritingDiagnosticTag.MissingStroke);

            if (actualStrokeCount > 0)
            {
                List<Vector2> firstActual = FirstNonEmpty(strokes);
                List<Vector2> firstExpected = FirstNonEmpty(pageTemplate);
                if (firstActual != null && firstExpected != null && firstExpected.Count > 1)
                {
                    Vector2 actualStart = firstActual[0];
                    Vector2 expectedStart = firstExpected[0];
                    Vector2 expectedEnd = firstExpected[firstExpected.Count - 1];
                    if (Vector2.Distance(actualStart, expectedEnd) + 16f < Vector2.Distance(actualStart, expectedStart))
                        summary.AddTag(HandwritingDiagnosticTag.ReversedStroke);
                }
            }

            if (actualStrokeCount >= 2 && expectedStrokeCount >= 2)
            {
                List<Vector2> firstActual = FirstNonEmpty(strokes);
                List<Vector2> secondExpected = SecondNonEmpty(pageTemplate);
                List<Vector2> firstExpected = FirstNonEmpty(pageTemplate);
                if (firstActual != null && secondExpected != null && firstExpected != null &&
                    Vector2.Distance(firstActual[0], secondExpected[0]) + 18f < Vector2.Distance(firstActual[0], firstExpected[0]))
                    summary.AddTag(HandwritingDiagnosticTag.WrongStrokeOrder);
            }
        }
    }

    static string BuildHint(HandwritingDiagnosticSummary summary)
    {
        if (HasActionableTag(summary, HandwritingDiagnosticTag.Mirror))
            return $"That looks flipped. Start {summary.letter} from the normal side.";
        if (HasActionableTag(summary, HandwritingDiagnosticTag.Oversized))
            return "Keep it between the notebook lines.";
        if (HasActionableTag(summary, HandwritingDiagnosticTag.BelowBaseline))
            return "Lift it back onto the baseline.";
        if (HasActionableTag(summary, HandwritingDiagnosticTag.AboveBaseline) || HasActionableTag(summary, HandwritingDiagnosticTag.Floating))
            return "Bring the letter down to sit on the baseline.";
        if (HasActionableTag(summary, HandwritingDiagnosticTag.Wobbly))
            return "Try one smoother, slower stroke.";
        if (HasActionableTag(summary, HandwritingDiagnosticTag.RepeatedCorrection))
            return $"Draw {summary.letter} in one clean try.";
        if (HasActionableTag(summary, HandwritingDiagnosticTag.SpacingDrift))
            return "Keep this letter inside its word space.";
        if (HasActionableTag(summary, HandwritingDiagnosticTag.ExtraStrokes) || HasActionableTag(summary, HandwritingDiagnosticTag.Overdrawn))
            return $"Draw {summary.letter} once, then stop.";
        if (HasActionableTag(summary, HandwritingDiagnosticTag.OpenLoop))
            return $"Close the round part of {summary.letter}.";
        if (HasActionableTag(summary, HandwritingDiagnosticTag.ClosedTooSoon))
            return $"Leave the opening in {summary.letter}.";
        if (HasActionableTag(summary, HandwritingDiagnosticTag.Undersized))
            return "Make it a little taller between the lines.";
        return "";
    }

    static int ResolveSeverity(HandwritingDiagnosticSummary summary)
    {
        if (HasActionableTag(summary, HandwritingDiagnosticTag.Mirror) && summary.mirrorConfidence >= 0.85f)
            return 3;
        int actionableCount = 0;
        if (summary.evidence != null)
            foreach (HandwritingDiagnosticEvidence item in summary.evidence)
                if (item != null && item.actionable) actionableCount++;
        if (HasActionableTag(summary, HandwritingDiagnosticTag.Wobbly) && actionableCount >= 2)
            return 2;
        if (actionableCount >= 2)
            return 2;
        return actionableCount > 0 || (summary.tags != null && summary.tags.Count > 0) ? 1 : 0;
    }

    static void BuildDiagnosticEvidence(HandwritingDiagnosticSummary summary)
    {
        summary.evidence ??= new List<HandwritingDiagnosticEvidence>();
        summary.evidence.Clear();
        float geometryConfidence = Mathf.Lerp(0.55f, 0.92f, Mathf.InverseLerp(4f, 24f, summary.pointCount));
        if (summary.tags == null)
            return;

        foreach (string tag in summary.tags)
        {
            HandwritingDiagnosticTag parsed = ParseTag(tag);
            float confidence = geometryConfidence;
            string source = "geometry";
            bool actionable = true;
            switch (parsed)
            {
                case HandwritingDiagnosticTag.Mirror:
                    confidence = summary.mirrorConfidence;
                    source = "template_mirror_comparison";
                    actionable = confidence >= 0.65f;
                    break;
                case HandwritingDiagnosticTag.ReversedStroke:
                case HandwritingDiagnosticTag.WrongStrokeOrder:
                    confidence = 0.45f;
                    source = "canonical_template_order";
                    actionable = false;
                    break;
                case HandwritingDiagnosticTag.WrongStart:
                case HandwritingDiagnosticTag.WrongDirection:
                case HandwritingDiagnosticTag.OpenLoop:
                case HandwritingDiagnosticTag.ClosedTooSoon:
                case HandwritingDiagnosticTag.Overdrawn:
                case HandwritingDiagnosticTag.MissingStroke:
                    confidence = Mathf.Clamp01(summary.formationConfidence);
                    source = "formation_heuristic";
                    actionable = confidence >= 0.68f;
                    break;
                case HandwritingDiagnosticTag.Wobbly:
                    confidence = Mathf.InverseLerp(summary.wobbleThresholdUsed, 0.68f, summary.wobbleScore);
                    source = "local_path_roughness";
                    actionable = confidence >= 0.60f;
                    break;
                case HandwritingDiagnosticTag.Slanted:
                    confidence = Mathf.Min(0.58f, geometryConfidence);
                    source = "bounding_geometry";
                    actionable = false;
                    break;
                case HandwritingDiagnosticTag.RepeatedCorrection:
                case HandwritingDiagnosticTag.ExtraStrokes:
                    confidence = summary.expectedStrokeCount > 0 ? 0.82f : 0.55f;
                    source = "stroke_count";
                    actionable = confidence >= 0.65f;
                    break;
            }

            summary.evidence.Add(new HandwritingDiagnosticEvidence
            {
                tag = tag,
                source = source,
                confidence = Mathf.Clamp01(confidence),
                actionable = actionable
            });
        }

        if (summary.evidence.Count == 0)
        {
            summary.diagnosticReliability = 1f;
            return;
        }
        float total = 0f;
        foreach (HandwritingDiagnosticEvidence item in summary.evidence)
            total += item.confidence;
        summary.diagnosticReliability = total / summary.evidence.Count;
    }

    static HandwritingDiagnosticTag ParseTag(string value)
    {
        foreach (HandwritingDiagnosticTag tag in Enum.GetValues(typeof(HandwritingDiagnosticTag)))
            if (string.Equals(HandwritingDiagnosticSummary.ToTagString(tag), value, StringComparison.Ordinal))
                return tag;
        return HandwritingDiagnosticTag.None;
    }

    static bool HasActionableTag(HandwritingDiagnosticSummary summary, HandwritingDiagnosticTag tag)
    {
        string value = HandwritingDiagnosticSummary.ToTagString(tag);
        if (summary?.evidence == null)
            return false;
        foreach (HandwritingDiagnosticEvidence item in summary.evidence)
            if (item != null && item.actionable && string.Equals(item.tag, value, StringComparison.Ordinal))
                return true;
        return false;
    }

    static float ResolvePrimaryHintConfidence(HandwritingDiagnosticSummary summary)
    {
        if (string.IsNullOrEmpty(summary?.primaryHint) || summary.evidence == null)
            return 0f;
        float best = 0f;
        foreach (HandwritingDiagnosticEvidence item in summary.evidence)
            if (item != null && item.actionable)
                best = Mathf.Max(best, item.confidence);
        return best;
    }

    static List<List<Vector2>> BuildPageTemplate(char letter, TemplateLibrary templateLibrary, Rect frame)
    {
        TemplateFormationSpec spec = TemplateFormationCompiler.Compile(letter, templateLibrary, frame.size);
        var result = new List<List<Vector2>>();
        if (spec?.guideStrokes == null)
            return result;

        foreach (List<Vector2> stroke in spec.guideStrokes)
        {
            if (stroke == null)
                continue;

            var translated = new List<Vector2>(stroke.Count);
            foreach (Vector2 point in stroke)
                translated.Add(point + frame.center);
            result.Add(translated);
        }

        return result;
    }

    static void AddTemplateDeviationTags(
        HandwritingDiagnosticSummary summary,
        List<List<Vector2>> strokes,
        List<List<Vector2>> pageTemplate)
    {
        List<Vector2> attempt = Resample(Flatten(strokes), 48);
        List<Vector2> guide = Resample(Flatten(pageTemplate), 48);
        int count = Mathf.Min(attempt.Count, guide.Count);
        if (count == 0)
            return;

        float total = 0f;
        float max = 0f;
        for (int i = 0; i < count; i++)
        {
            float distance = Vector2.Distance(attempt[i], guide[i]);
            total += distance;
            if (distance > max)
                max = distance;
        }

        summary.templateDeviationAverage = total / count;
        summary.templateDeviationMax = max;
    }

    static void AddWobbleTags(
        HandwritingDiagnosticSummary summary,
        List<List<Vector2>> strokes,
        List<List<Vector2>> pageTemplate,
        Rect templateFrame)
    {
        if (strokes == null)
            return;

        float totalPath = 0f;
        float totalChord = 0f;

        foreach (List<Vector2> stroke in strokes)
        {
            if (stroke == null || stroke.Count < 4)
                continue;

            float path = PathLength(stroke);
            if (path < 20f)
                continue;

            float chord = Vector2.Distance(stroke[0], stroke[^1]);

            totalPath += path;
            totalChord += Mathf.Max(1f, chord);
        }

        if (totalPath <= 1f)
            return;

        summary.pathLength = totalPath;
        summary.directness = Mathf.Clamp01(totalChord / Mathf.Max(1f, totalPath));
        summary.templateNoiseScore = TemplateNoise(strokes, pageTemplate);
        LocalKinkMetrics kink = CalculateLocalKinkMetrics(strokes, pageTemplate, templateFrame);
        summary.localKinkScore = kink.localKinkScore;
        summary.localKinkCount = kink.localKinkCount;
        summary.localKinkMax = kink.localKinkMax;
        summary.perpendicularJitterScore = kink.perpendicularJitterScore;
        summary.localRoughness = kink.localKinkScore;
        summary.pointDensity = summary.pointCount / Mathf.Max(1f, totalPath / 100f);
        summary.wobbleScore = kink.wobbleScore;
        summary.wobbleThresholdUsed = WobbleScoreThreshold;

        if (kink.hasSamples &&
            totalPath > MinWobblePathLength &&
            (summary.localKinkCount >= 2 || summary.wobbleScore >= WobbleScoreThreshold))
            summary.AddTag(HandwritingDiagnosticTag.Wobbly);
    }

    struct LocalKinkMetrics
    {
        public bool hasSamples;
        public float localKinkScore;
        public int localKinkCount;
        public float localKinkMax;
        public float perpendicularJitterScore;
        public float wobbleScore;
    }

    static LocalKinkMetrics CalculateLocalKinkMetrics(
        List<List<Vector2>> strokes,
        List<List<Vector2>> pageTemplate,
        Rect templateFrame)
    {
        var metrics = new LocalKinkMetrics();
        if (strokes == null)
            return metrics;

        float guideHeight = templateFrame.height > 0f ? templateFrame.height : Bounds(Flatten(pageTemplate)).height;
        float sampleSpacing = Mathf.Clamp(Mathf.Max(1f, guideHeight) * 0.035f, 5f, 9f);
        List<List<Vector2>> actualStrokes = NonEmptyStrokes(strokes);
        List<List<Vector2>> templateStrokes = NonEmptyStrokes(pageTemplate);
        bool useStrokeIndex = templateStrokes.Count > 0 && actualStrokes.Count == templateStrokes.Count;

        var turnScores = new List<float>();
        var jitterScores = new List<float>();

        for (int i = 0; i < actualStrokes.Count; i++)
        {
            List<Vector2> stroke = actualStrokes[i];
            List<Vector2> templateStroke = null;
            if (useStrokeIndex)
                templateStroke = templateStrokes[i];
            else if (templateStrokes.Count > 0)
                templateStroke = BestMatchingTemplateStroke(stroke, templateStrokes);

            CollectStrokeKinks(
                stroke,
                templateStroke,
                sampleSpacing,
                turnScores,
                jitterScores,
                ref metrics.localKinkCount,
                ref metrics.localKinkMax);
        }

        metrics.hasSamples = turnScores.Count > 0 || jitterScores.Count > 0;
        metrics.localKinkScore = Percentile(turnScores, 0.90f);
        metrics.perpendicularJitterScore = Percentile(jitterScores, 0.90f);
        metrics.wobbleScore = metrics.localKinkScore * 0.65f + metrics.perpendicularJitterScore * 0.35f;
        return metrics;
    }

    static void CollectStrokeKinks(
        List<Vector2> stroke,
        List<Vector2> templateStroke,
        float sampleSpacing,
        List<float> turnScores,
        List<float> jitterScores,
        ref int kinkCount,
        ref float maxKink)
    {
        if (stroke == null || stroke.Count < 2)
            return;

        float strokePath = PathLength(stroke);
        if (strokePath < 20f)
            return;

        bool hasTemplate = templateStroke != null && templateStroke.Count >= 2 && PathLength(templateStroke) > 1f;
        float comparisonPath = hasTemplate ? Mathf.Max(strokePath, PathLength(templateStroke)) : strokePath;
        int sampleCount = Mathf.Clamp(Mathf.RoundToInt(comparisonPath / Mathf.Max(1f, sampleSpacing)) + 1, KinkWindowRadius * 2 + 1, MaxKinkSamples);
        List<Vector2> attempt = Resample(stroke, sampleCount);
        List<Vector2> guide = hasTemplate ? Resample(templateStroke, sampleCount) : null;
        int count = attempt.Count;
        if (count < KinkWindowRadius * 2 + 1)
            return;

        var highTurnWindows = new List<bool>();
        for (int i = KinkWindowRadius; i < count - KinkWindowRadius; i++)
        {
            if (!TrySignedTurn(attempt, i, KinkWindowRadius, out float attemptTurn))
                continue;

            float templateTurn = 0f;
            if (guide != null)
                TrySignedTurn(guide, i, KinkWindowRadius, out templateTurn);

            float excessTurn = Mathf.Max(0f, Mathf.Abs(Mathf.DeltaAngle(templateTurn, attemptTurn)) - KinkTurnToleranceDegrees);
            float score = Mathf.Clamp01(excessTurn / 90f);
            turnScores.Add(score);
            highTurnWindows.Add(score > KinkWindowThreshold);
            if (score > maxKink)
                maxKink = score;
        }

        kinkCount += CountClusters(highTurnWindows);
        if (guide == null || count < 3)
            return;

        var perpendicularDeviations = new List<float>(count);
        for (int i = 0; i < count; i++)
        {
            Vector2 tangent = TemplateTangent(guide, i);
            if (tangent.sqrMagnitude < 0.001f)
                tangent = TemplateTangent(attempt, i);
            if (tangent.sqrMagnitude < 0.001f)
            {
                perpendicularDeviations.Add(0f);
                continue;
            }

            tangent.Normalize();
            Vector2 normal = new Vector2(-tangent.y, tangent.x);
            perpendicularDeviations.Add(Vector2.Dot(attempt[i] - guide[i], normal));
        }

        float jitterNormalizer = Mathf.Max(1f, sampleSpacing * 1.5f);
        for (int i = 1; i < perpendicularDeviations.Count - 1; i++)
        {
            float jitter = perpendicularDeviations[i + 1] - 2f * perpendicularDeviations[i] + perpendicularDeviations[i - 1];
            jitterScores.Add(Mathf.Clamp01(Mathf.Abs(jitter) / jitterNormalizer));
        }
    }

    static bool TrySignedTurn(List<Vector2> points, int index, int radius, out float turn)
    {
        turn = 0f;
        if (points == null || index - radius < 0 || index + radius >= points.Count)
            return false;

        Vector2 before = points[index] - points[index - radius];
        Vector2 after = points[index + radius] - points[index];
        if (before.sqrMagnitude < 4f || after.sqrMagnitude < 4f)
            return false;

        turn = Vector2.SignedAngle(before, after);
        return true;
    }

    static Vector2 TemplateTangent(List<Vector2> points, int index)
    {
        if (points == null || points.Count < 2)
            return Vector2.zero;

        int before = Mathf.Max(0, index - 1);
        int after = Mathf.Min(points.Count - 1, index + 1);
        return points[after] - points[before];
    }

    static int CountClusters(List<bool> values)
    {
        if (values == null || values.Count == 0)
            return 0;

        int count = 0;
        bool inCluster = false;
        foreach (bool value in values)
        {
            if (value)
            {
                if (!inCluster)
                {
                    count++;
                    inCluster = true;
                }
            }
            else
            {
                inCluster = false;
            }
        }

        return count;
    }

    static float Percentile(List<float> values, float percentile)
    {
        if (values == null || values.Count == 0)
            return 0f;

        var sorted = new List<float>(values);
        sorted.Sort();
        int index = Mathf.Clamp(Mathf.CeilToInt(sorted.Count * percentile) - 1, 0, sorted.Count - 1);
        return sorted[index];
    }

    static List<List<Vector2>> NonEmptyStrokes(List<List<Vector2>> strokes)
    {
        var result = new List<List<Vector2>>();
        if (strokes == null)
            return result;

        foreach (List<Vector2> stroke in strokes)
            if (stroke != null && stroke.Count > 0)
                result.Add(stroke);
        return result;
    }

    static List<Vector2> BestMatchingTemplateStroke(List<Vector2> stroke, List<List<Vector2>> templateStrokes)
    {
        if (templateStrokes == null || templateStrokes.Count == 0)
            return null;
        if (templateStrokes.Count == 1)
            return templateStrokes[0];

        float bestScore = float.MaxValue;
        List<Vector2> best = templateStrokes[0];
        foreach (List<Vector2> templateStroke in templateStrokes)
        {
            float score = AverageNormalizedDistance(stroke, templateStroke);
            if (score >= bestScore)
                continue;

            bestScore = score;
            best = templateStroke;
        }

        return best;
    }

    static float AverageNormalizedDistance(List<Vector2> a, List<Vector2> b)
    {
        List<Vector2> normalizedA = Normalize(a, 24, 250f);
        List<Vector2> normalizedB = Normalize(b, 24, 250f);
        return AverageDistance(normalizedA, normalizedB);
    }

    static List<Vector2> ResampleBySpacing(List<Vector2> points, float spacing)
    {
        var result = new List<Vector2>();
        if (points == null || points.Count == 0)
            return result;
        if (points.Count == 1)
        {
            result.Add(points[0]);
            return result;
        }

        result.Add(points[0]);
        float carried = 0f;
        Vector2 previous = points[0];
        for (int i = 1; i < points.Count; i++)
        {
            Vector2 current = points[i];
            float distance = Vector2.Distance(previous, current);
            if (distance <= 0.001f)
                continue;

            while (carried + distance >= spacing)
            {
                float t = (spacing - carried) / distance;
                Vector2 sample = Vector2.Lerp(previous, current, t);
                result.Add(sample);
                previous = sample;
                distance = Vector2.Distance(previous, current);
                carried = 0f;
                if (distance <= 0.001f)
                    break;
            }

            carried += distance;
            previous = current;
        }

        if (result[^1] != points[^1])
            result.Add(points[^1]);
        return result;
    }

    static float TemplateNoise(List<List<Vector2>> strokes, List<List<Vector2>> pageTemplate)
    {
        List<Vector2> attempt = Resample(Flatten(strokes), 48);
        List<Vector2> guide = Resample(Flatten(pageTemplate), 48);
        int count = Mathf.Min(attempt.Count, guide.Count);
        if (count < 3)
            return 0f;

        float total = 0f;
        for (int i = 1; i < count; i++)
        {
            Vector2 previousDeviation = attempt[i - 1] - guide[i - 1];
            Vector2 currentDeviation = attempt[i] - guide[i];
            total += Vector2.Distance(previousDeviation, currentDeviation);
        }

        return total / (count - 1);
    }

    static bool IsMirrorSensitive(char letter)
    {
        return "BDPQSZRF".IndexOf(char.ToUpperInvariant(letter)) >= 0;
    }

    static Vector2 LongestStrokeDirection(List<List<Vector2>> strokes)
    {
        if (strokes == null)
            return Vector2.zero;

        float bestLength = 0f;
        Vector2 bestDirection = Vector2.zero;
        foreach (List<Vector2> stroke in strokes)
        {
            if (stroke == null || stroke.Count < 2)
                continue;

            float length = 0f;
            for (int i = 1; i < stroke.Count; i++)
                length += Vector2.Distance(stroke[i - 1], stroke[i]);

            if (length <= bestLength)
                continue;

            bestLength = length;
            Vector2 delta = stroke[^1] - stroke[0];
            bestDirection = delta.sqrMagnitude > 0.001f ? delta.normalized : Vector2.zero;
        }

        return bestDirection;
    }

    static Rect Bounds(List<Vector2> points)
    {
        if (points == null || points.Count == 0)
            return new Rect();

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minY = float.MaxValue;
        float maxY = float.MinValue;
        foreach (Vector2 point in points)
        {
            if (point.x < minX) minX = point.x;
            if (point.x > maxX) maxX = point.x;
            if (point.y < minY) minY = point.y;
            if (point.y > maxY) maxY = point.y;
        }

        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    static List<Vector2> Flatten(List<List<Vector2>> strokes)
    {
        var result = new List<Vector2>();
        if (strokes == null)
            return result;
        foreach (List<Vector2> stroke in strokes)
            if (stroke != null)
                result.AddRange(stroke);
        return result;
    }

    static int CountNonEmpty(List<List<Vector2>> strokes)
    {
        int count = 0;
        if (strokes == null)
            return count;
        foreach (List<Vector2> stroke in strokes)
            if (stroke != null && stroke.Count > 0)
                count++;
        return count;
    }

    static List<Vector2> FirstNonEmpty(List<List<Vector2>> strokes)
    {
        if (strokes == null)
            return null;
        foreach (List<Vector2> stroke in strokes)
            if (stroke != null && stroke.Count > 0)
                return stroke;
        return null;
    }

    static List<Vector2> SecondNonEmpty(List<List<Vector2>> strokes)
    {
        bool foundFirst = false;
        if (strokes == null)
            return null;
        foreach (List<Vector2> stroke in strokes)
        {
            if (stroke == null || stroke.Count == 0)
                continue;
            if (!foundFirst)
            {
                foundFirst = true;
                continue;
            }
            return stroke;
        }
        return null;
    }

    static List<Vector2> Normalize(List<Vector2> points, int targetCount, float squareSize)
    {
        points = Resample(points, targetCount);
        if (points.Count == 0)
            return points;

        Rect bounds = Bounds(points);
        float size = Mathf.Max(1f, Mathf.Max(bounds.width, bounds.height));
        var result = new List<Vector2>(points.Count);
        foreach (Vector2 point in points)
        {
            result.Add(new Vector2(
                (point.x - bounds.xMin) / size * squareSize,
                (point.y - bounds.yMin) / size * squareSize));
        }
        return result;
    }

    static List<Vector2> MirrorX(List<Vector2> points)
    {
        var result = new List<Vector2>(points.Count);
        foreach (Vector2 point in points)
            result.Add(new Vector2(250f - point.x, point.y));
        return result;
    }

    static float AverageDistance(List<Vector2> a, List<Vector2> b)
    {
        int count = Mathf.Min(a.Count, b.Count);
        if (count == 0)
            return float.MaxValue;
        float total = 0f;
        for (int i = 0; i < count; i++)
            total += Vector2.Distance(a[i], b[i]);
        return total / count;
    }

    static float PathLength(List<Vector2> points)
    {
        float total = 0f;
        if (points == null)
            return total;
        for (int i = 1; i < points.Count; i++)
            total += Vector2.Distance(points[i - 1], points[i]);
        return total;
    }

    static List<Vector2> Resample(List<Vector2> points, int targetCount)
    {
        if (points == null || points.Count == 0)
            return new List<Vector2>();
        if (points.Count == 1)
            return new List<Vector2> { points[0] };

        float pathLength = 0f;
        for (int i = 1; i < points.Count; i++)
            pathLength += Vector2.Distance(points[i - 1], points[i]);
        if (pathLength <= 0.001f)
            return new List<Vector2>(points);

        float interval = pathLength / (targetCount - 1);
        float carried = 0f;
        var source = new List<Vector2>(points);
        var result = new List<Vector2> { source[0] };
        for (int i = 1; i < source.Count; i++)
        {
            float distance = Vector2.Distance(source[i - 1], source[i]);
            if (distance <= 0.001f)
                continue;

            if (carried + distance >= interval)
            {
                float t = (interval - carried) / distance;
                Vector2 q = Vector2.Lerp(source[i - 1], source[i], t);
                result.Add(q);
                source.Insert(i, q);
                carried = 0f;
            }
            else
            {
                carried += distance;
            }
        }

        while (result.Count < targetCount)
            result.Add(source[^1]);
        if (result.Count > targetCount)
            result.RemoveRange(targetCount, result.Count - targetCount);
        return result;
    }

    static List<Vector2> Resample(List<Vector2> points, int targetCount, float squareSize)
    {
        points = Resample(points, targetCount);
        if (points.Count == 0)
            return points;

        Rect bounds = Bounds(points);
        float size = Mathf.Max(1f, Mathf.Max(bounds.width, bounds.height));
        var result = new List<Vector2>(points.Count);
        foreach (Vector2 point in points)
        {
            result.Add(new Vector2(
                (point.x - bounds.xMin) / size * squareSize,
                (point.y - bounds.yMin) / size * squareSize));
        }
        return result;
    }
}
