using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public partial class LetterFormationCoach
{
    public static FormationResult AnalyzeForTests(char letter, List<List<Vector2>> strokes)
    {
        char normalized = char.ToUpperInvariant(letter);
        LetterFormationSpec spec = ToLetterFormationSpec(
            TemplateFormationCompiler.Compile(normalized, null, new Vector2(220f, 220f)));
        var testSession = new FormationSession { spec = spec };
        return AnalyzeSession(testSession, strokes);
    }

    static FormationResult AnalyzeSession(FormationSession activeSession, List<List<Vector2>> strokes)
    {
        StrokeFeatures features = BuildFeatures(strokes);
        if (features.points.Count < 2 || features.pathLength < 8f)
            return HiddenResult();

        if (activeSession.spec.templateSpec != null &&
            activeSession.spec.templateSpec.primitives != null &&
            activeSession.spec.templateSpec.primitives.Count > 0)
            return AnalyzeTemplate(activeSession, features, strokes);

        return AnalyzeGeneric(activeSession.spec.letter, features, activeSession.spec.guide);
    }

    static FormationResult AnalyzeTemplate(
        FormationSession activeSession,
        StrokeFeatures features,
        List<List<Vector2>> strokes)
    {
        TemplateFormationSpec target = activeSession.spec.templateSpec;
        TemplateFormationSpec candidate = TemplateFormationCompiler.CompileCandidate(target.letter, strokes);
        if (candidate.primitives == null || candidate.primitives.Count == 0)
            return HiddenResult();

        float targetLength = TotalPrimitiveLength(target.primitives);
        float candidateLength = TotalPrimitiveLength(candidate.primitives);
        int targetLoopCount = CountPrimitives(target.primitives, FormationPrimitiveKind.Loop);
        int candidateLoopCount = CountPrimitives(candidate.primitives, FormationPrimitiveKind.Loop);

        // Candidate primitives are normalized before compilation, which can
        // hide a repeated loop. Compare the original touch path to the guide
        // as well so scribbles and double tracing receive corrective feedback
        // rather than praise.
        float targetGuideLength = TotalGuideLength(target.guideStrokes);
        if (ContainsCurvedPrimitive(target.primitives) &&
            targetGuideLength > 0f && features.pathLength > targetGuideLength * 1.75f)
            return WithPhase(activeSession, NeedsHelp($"Too many marks for '{target.letter}'. Clear it and draw the shape once.", DiagnosticTag.Overdrawn, features.points[^1]), Mathf.Max(0, target.primitives.Count - 1));

        if (targetLoopCount == 0 && candidateLoopCount > 0)
            return WithPhase(activeSession, NeedsHelp($"Leave the opening for '{target.letter}'.", DiagnosticTag.ClosingTooSoon, features.points[^1]), 0);

        if (candidateLoopCount > Mathf.Max(1, targetLoopCount))
            return WithPhase(activeSession, NeedsHelp($"One loop is enough for '{target.letter}'.", DiagnosticTag.Overdrawn, features.points[^1]), Mathf.Max(0, target.primitives.Count - 1));

        if (targetLength > 0f && candidateLength > targetLength * 1.95f)
            return WithPhase(activeSession, NeedsHelp($"Too many marks for '{target.letter}'. Clear it and draw the shape once.", DiagnosticTag.Overdrawn, features.points[^1]), Mathf.Max(0, target.primitives.Count - 1));

        int compareCount = Mathf.Min(candidate.primitives.Count, target.primitives.Count);
        for (int i = 0; i < compareCount; i++)
        {
            FormationPrimitive expected = target.primitives[i];
            FormationPrimitive actual = candidate.primitives[i];
            if (PrimitiveMatches(expected, actual))
                continue;

            return WithPhase(activeSession, CorrectionForPrimitive(target.letter, expected, actual, features.points[^1], i), i);
        }

        int phaseIndex = Mathf.Clamp(compareCount - 1, 0, Mathf.Max(0, target.primitives.Count - 1));
        activeSession.phaseIndex = phaseIndex;

        if (candidate.primitives.Count < target.primitives.Count)
        {
            FormationPrimitive next = target.primitives[candidate.primitives.Count];
            FormationPrimitive current = candidate.primitives[^1];
            if (current.kind == FormationPrimitiveKind.Line &&
                next.kind == FormationPrimitiveKind.Line &&
                AxisMatches(current.axis, target.primitives[Mathf.Max(0, candidate.primitives.Count - 1)].axis))
                return WithPhase(activeSession, NeedsNudge(HintForMissingNext(target.letter, next), DiagnosticTag.MissingFoot, features.points[^1]), candidate.primitives.Count);

            return WithPhase(activeSession, OnTrack(HintForPrimitive(target.letter, next), DiagnosticTag.KeepGoing, features.points[^1]), candidate.primitives.Count);
        }

        if (candidate.primitives.Count > target.primitives.Count)
            return WithPhase(activeSession, NeedsHelp($"That adds extra shape to '{target.letter}'. Draw it once.", DiagnosticTag.Overdrawn, features.points[^1]), target.primitives.Count - 1);

        FormationPrimitive lastTarget = target.primitives[^1];
        FormationPrimitive lastCandidate = candidate.primitives[^1];
        if (lastTarget.kind == FormationPrimitiveKind.Loop && !lastCandidate.closed)
            return WithPhase(activeSession, NeedsNudge($"Close the loop for '{target.letter}'.", DiagnosticTag.OpenLoop, features.points[0]), target.primitives.Count - 1);

        if (lastTarget.kind == FormationPrimitiveKind.Curve && lastTarget.openness > 0.32f && lastCandidate.closed)
            return WithPhase(activeSession, NeedsHelp($"Leave the opening for '{target.letter}'.", DiagnosticTag.ClosingTooSoon, features.points[^1]), target.primitives.Count - 1);

        return WithPhase(activeSession, OnTrack($"Good {target.letter}.", DiagnosticTag.None, features.points[^1]), target.primitives.Count - 1);
    }

    static bool PrimitiveMatches(FormationPrimitive expected, FormationPrimitive actual)
    {
        if (expected.kind == FormationPrimitiveKind.Line)
            return actual.kind == FormationPrimitiveKind.Line && AxisMatches(expected.axis, actual.axis);

        if (expected.kind == FormationPrimitiveKind.Loop)
            return actual.kind == FormationPrimitiveKind.Loop ||
                   (actual.kind == FormationPrimitiveKind.Curve && actual.closed);

        if (expected.kind == FormationPrimitiveKind.Curve)
        {
            if (actual.kind == FormationPrimitiveKind.Loop && expected.openness > 0.32f)
                return false;
            if (actual.kind != FormationPrimitiveKind.Curve && actual.kind != FormationPrimitiveKind.Loop)
                return false;
            return actual.horizontalTurns <= expected.horizontalTurns &&
                   actual.verticalTurns <= expected.verticalTurns + 1;
        }

        return expected.kind == actual.kind;
    }

    static bool AxisMatches(FormationAxis expected, FormationAxis actual)
    {
        if (expected == actual)
            return true;
        if (expected == FormationAxis.None || actual == FormationAxis.None)
            return true;
        if ((expected == FormationAxis.DiagonalPositive || expected == FormationAxis.DiagonalNegative) &&
            (actual == FormationAxis.DiagonalPositive || actual == FormationAxis.DiagonalNegative))
            return true;
        if ((expected == FormationAxis.Vertical || expected == FormationAxis.Horizontal) &&
            (actual == FormationAxis.DiagonalPositive || actual == FormationAxis.DiagonalNegative))
            return true;
        return false;
    }

    static FormationResult CorrectionForPrimitive(
        char letter,
        FormationPrimitive expected,
        FormationPrimitive actual,
        Vector2 marker,
        int phaseIndex)
    {
        if (expected.kind == FormationPrimitiveKind.Line)
        {
            string line = expected.axis == FormationAxis.Vertical
                ? "vertical line"
                : expected.axis == FormationAxis.Horizontal
                    ? "line across"
                    : "diagonal line";
            DiagnosticTag diagnostic = phaseIndex == 0 ? DiagnosticTag.WrongStart : DiagnosticTag.WrongDirection;
            return NeedsHelp($"For '{letter}', make the {line}.", diagnostic, marker);
        }

        if (expected.kind == FormationPrimitiveKind.Loop)
        {
            if (actual.kind == FormationPrimitiveKind.Curve)
                return NeedsNudge($"Close the loop for '{letter}'.", DiagnosticTag.OpenLoop, marker);
            return NeedsHelp($"Draw one round loop for '{letter}'.", DiagnosticTag.WrongDirection, marker);
        }

        if (expected.kind == FormationPrimitiveKind.Curve)
        {
            if (actual.kind == FormationPrimitiveKind.Loop)
                return NeedsHelp($"Leave the opening for '{letter}'.", DiagnosticTag.ClosingTooSoon, marker);
            return NeedsHelp($"Keep one clean curve for '{letter}'.", DiagnosticTag.WrongDirection, marker);
        }

        return NeedsNudge($"Shape this into '{letter}'.", DiagnosticTag.GenericShape, marker);
    }

    static float TotalPrimitiveLength(List<FormationPrimitive> primitives)
    {
        float total = 0f;
        if (primitives == null)
            return total;

        foreach (FormationPrimitive primitive in primitives)
            if (primitive != null)
                total += primitive.pathLength;
        return total;
    }

    static float TotalGuideLength(List<List<Vector2>> strokes)
    {
        float total = 0f;
        if (strokes == null)
            return total;

        foreach (List<Vector2> stroke in strokes)
        {
            if (stroke == null)
                continue;

            for (int i = 1; i < stroke.Count; i++)
                total += Vector2.Distance(stroke[i - 1], stroke[i]);
        }

        return total;
    }

    static bool ContainsCurvedPrimitive(List<FormationPrimitive> primitives)
    {
        if (primitives == null)
            return false;

        foreach (FormationPrimitive primitive in primitives)
        {
            if (primitive != null &&
                (primitive.kind == FormationPrimitiveKind.Curve || primitive.kind == FormationPrimitiveKind.Loop))
                return true;
        }

        return false;
    }

    static int CountPrimitives(List<FormationPrimitive> primitives, FormationPrimitiveKind kind)
    {
        int count = 0;
        if (primitives == null)
            return count;

        foreach (FormationPrimitive primitive in primitives)
            if (primitive != null && primitive.kind == kind)
                count++;
        return count;
    }

    static FormationResult AnalyzeGeneric(char letter, StrokeFeatures features, List<List<Vector2>> guide)
    {
        if (guide == null || guide.Count == 0 || features.pathLength < 24f)
            return OnTrack($"Keep shaping '{letter}'.", DiagnosticTag.KeepGoing, features.points[^1]);

        List<Vector2> candidate = NormalizeForComparison(features.points, 32, 250f);
        List<Vector2> target = NormalizeForComparison(FlattenStrokes(guide), 32, 250f);
        if (candidate.Count == 0 || target.Count == 0)
            return OnTrack($"Keep shaping '{letter}'.", DiagnosticTag.KeepGoing, features.points[^1]);

        int compareCount = Mathf.Clamp(candidate.Count, 4, target.Count);
        float total = 0f;
        for (int i = 0; i < compareCount; i++)
            total += Vector2.Distance(candidate[i], target[i]);

        float average = total / compareCount;
        if (average > 92f)
            return NeedsNudge($"Shape this into '{letter}'.", DiagnosticTag.GenericShape, features.points[^1]);

        return OnTrack($"Good. Keep building '{letter}'.", DiagnosticTag.None, features.points[^1]);
    }

    static FormationResult ApplyQuietFirstAttempt(FormationResult result, int attemptsUsed, int helpLevel)
    {
        if (attemptsUsed > 0 || helpLevel > 0)
            return result;

        // The authored guide represents one valid formation, not the only
        // culturally or developmentally acceptable stroke order. Let the
        // learner finish a first attempt before coaching direction/order.
        if (result.state >= FormationState.NeedsNudge)
        {
            result.state = FormationState.OnTrack;
            result.hint = "Keep going. I will check the finished letter.";
            result.hasMarker = false;
        }

        return result;
    }
}
