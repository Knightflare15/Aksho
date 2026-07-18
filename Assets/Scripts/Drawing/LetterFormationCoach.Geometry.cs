using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public partial class LetterFormationCoach
{
    static StrokeFeatures BuildFeatures(List<List<Vector2>> strokes)
    {
        List<Vector2> points = FlattenStrokes(strokes);
        StrokeFeatures features = new StrokeFeatures
        {
            points = points,
            bounds = new Rect(),
            width = 0f,
            height = 0f,
            diagonal = 1f,
            pathLength = PathLength(points),
            closure = 1f,
            angleSweep = 0f,
            firstDirection = Vector2.zero,
            lastDirection = Vector2.zero
        };

        if (points.Count == 0)
            return features;

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

        features.width = Mathf.Max(1f, maxX - minX);
        features.height = Mathf.Max(1f, maxY - minY);
        features.diagonal = Mathf.Max(1f, Mathf.Sqrt(features.width * features.width + features.height * features.height));
        features.bounds = Rect.MinMaxRect(minX, minY, maxX, maxY);
        features.closure = points.Count > 1
            ? Vector2.Distance(points[0], points[^1]) / features.diagonal
            : 1f;
        features.angleSweep = ComputeAngleSweep(points, features.bounds.center);
        features.firstDirection = FindDirection(points, fromStart: true);
        features.lastDirection = FindDirection(points, fromStart: false);
        return features;
    }

    static Vector2 FindDirection(List<Vector2> points, bool fromStart)
    {
        if (points.Count < 2)
            return Vector2.zero;

        if (fromStart)
        {
            Vector2 origin = points[0];
            for (int i = 1; i < points.Count; i++)
            {
                Vector2 delta = points[i] - origin;
                if (delta.magnitude >= 12f)
                    return delta.normalized;
            }
        }
        else
        {
            Vector2 origin = points[^1];
            for (int i = points.Count - 2; i >= 0; i--)
            {
                Vector2 delta = origin - points[i];
                if (delta.magnitude >= 12f)
                    return delta.normalized;
            }
        }

        Vector2 fallback = points[^1] - points[0];
        return fallback.sqrMagnitude > 1f ? fallback.normalized : Vector2.zero;
    }

    static float ComputeAngleSweep(List<Vector2> points, Vector2 center)
    {
        if (points.Count < 3)
            return 0f;

        float sweep = 0f;
        float previous = Mathf.Atan2(points[0].y - center.y, points[0].x - center.x) * Mathf.Rad2Deg;
        for (int i = 1; i < points.Count; i++)
        {
            float current = Mathf.Atan2(points[i].y - center.y, points[i].x - center.x) * Mathf.Rad2Deg;
            float delta = Mathf.DeltaAngle(previous, current);
            sweep += Mathf.Abs(delta);
            previous = current;
        }

        return sweep;
    }

    static float[] CumulativeLengths(List<Vector2> points)
    {
        float[] lengths = new float[points.Count];
        for (int i = 1; i < points.Count; i++)
            lengths[i] = lengths[i - 1] + Vector2.Distance(points[i - 1], points[i]);
        return lengths;
    }

    static float PathLength(List<Vector2> points)
    {
        float total = 0f;
        for (int i = 1; i < points.Count; i++)
            total += Vector2.Distance(points[i - 1], points[i]);
        return total;
    }

    static int CountPoints(List<List<Vector2>> strokes)
    {
        int count = 0;
        if (strokes == null)
            return count;

        foreach (List<Vector2> stroke in strokes)
            if (stroke != null)
                count += stroke.Count;
        return count;
    }

    static FormationResult HiddenResult()
    {
        return new FormationResult
        {
            state = FormationState.Hidden,
            diagnostic = DiagnosticTag.None,
            hint = "",
            confidence = 0f,
            phaseIndex = 0
        };
    }

    static FormationResult OnTrack(string hint, DiagnosticTag diagnostic, Vector2 marker = default)
    {
        return new FormationResult
        {
            state = FormationState.OnTrack,
            diagnostic = diagnostic,
            hint = hint,
            markerPosition = marker,
            hasMarker = marker != default,
            confidence = 0.82f
        };
    }

    static FormationResult NeedsNudge(string hint, DiagnosticTag diagnostic, Vector2 marker)
    {
        return new FormationResult
        {
            state = FormationState.NeedsNudge,
            diagnostic = diagnostic,
            hint = hint,
            markerPosition = marker,
            hasMarker = true,
            confidence = 0.78f
        };
    }

    static FormationResult NeedsHelp(string hint, DiagnosticTag diagnostic, Vector2 marker)
    {
        return new FormationResult
        {
            state = FormationState.NeedsHelp,
            diagnostic = diagnostic,
            hint = hint,
            markerPosition = marker,
            hasMarker = true,
            confidence = 0.92f
        };
    }

    static FormationResult WithPhase(FormationSession activeSession, FormationResult result, int phaseIndex)
    {
        activeSession.phaseIndex = Mathf.Clamp(phaseIndex, 0, Mathf.Max(0, activeSession.spec.phases.Count - 1));
        result.phaseIndex = activeSession.phaseIndex;
        return result;
    }

    static FeedbackManager.GuidanceState ToFeedbackState(FormationState formationState)
    {
        return formationState == FormationState.NeedsHelp
            ? FeedbackManager.GuidanceState.OffTrack
            : formationState == FormationState.NeedsNudge
                ? FeedbackManager.GuidanceState.Drifting
                : FeedbackManager.GuidanceState.OnTrack;
    }

    static bool TryGetLastPoint(List<List<Vector2>> strokes, out Vector2 point)
    {
        for (int i = strokes.Count - 1; i >= 0; i--)
        {
            List<Vector2> stroke = strokes[i];
            if (stroke != null && stroke.Count > 0)
            {
                point = stroke[^1];
                return true;
            }
        }

        point = Vector2.zero;
        return false;
    }
}
