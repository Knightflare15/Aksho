using System.Collections.Generic;
using UnityEngine;

public enum FormationPrimitiveKind
{
    Line,
    Curve,
    Loop,
    Corner,
    OptionalLift
}

public enum FormationAxis
{
    None,
    Horizontal,
    Vertical,
    DiagonalPositive,
    DiagonalNegative
}

public sealed class FormationPrimitive
{
    public FormationPrimitiveKind kind;
    public FormationAxis axis;
    public List<Vector2> path = new List<Vector2>();
    public Rect bounds;
    public Vector2 start;
    public Vector2 end;
    public Vector2 center;
    public bool reversible;
    public bool closed;
    public float openness;
    public float curvatureDegrees;
    public float pathLength;
    public int horizontalTurns;
    public int verticalTurns;
}

public sealed class TemplateFormationSpec
{
    public char letter;
    public string sourceTemplateId;
    public List<List<Vector2>> guideStrokes = new List<List<Vector2>>();
    public List<FormationPrimitive> primitives = new List<FormationPrimitive>();
    public bool usedFallbackTemplate;
}

public struct FormationMatchResult
{
    public LetterFormationCoach.FormationState state;
    public LetterFormationCoach.DiagnosticTag diagnostic;
    public string hint;
    public int activePrimitiveIndex;
    public Vector2 markerPosition;
    public bool hasMarker;
    public float confidence;
}

public static class TemplateFormationCompiler
{
    const float NormalizedSize = 250f;
    const float GuideScale = 0.82f;

    public static TemplateFormationSpec Compile(
        char letter,
        TemplateLibrary library,
        Vector2 renderSize)
    {
        char normalized = char.ToUpperInvariant(letter);
        List<TemplateLibrary.RawPoint> raw = ResolvePrimeRaw(normalized, library, out string sourceId, out bool fallback);
        List<List<Vector2>> localStrokes = RawToLocalStrokes(raw, renderSize);
        TemplateFormationSpec spec = CompileFromLocalStrokes(normalized, localStrokes, sourceId, fallback);
        spec.guideStrokes = localStrokes;
        return spec;
    }

    public static TemplateFormationSpec CompileCandidate(char letter, List<List<Vector2>> localStrokes)
    {
        return CompileFromLocalStrokes(char.ToUpperInvariant(letter), localStrokes, "candidate", false);
    }

    static TemplateFormationSpec CompileFromLocalStrokes(
        char letter,
        List<List<Vector2>> localStrokes,
        string sourceId,
        bool fallback)
    {
        var spec = new TemplateFormationSpec
        {
            letter = letter,
            sourceTemplateId = sourceId,
            usedFallbackTemplate = fallback,
            guideStrokes = CopyStrokes(localStrokes)
        };

        List<List<Vector2>> normalized = NormalizeStrokes(localStrokes, NormalizedSize);
        foreach (List<Vector2> stroke in normalized)
            AddPrimitivesForStroke(spec.primitives, stroke);

        MergeLineRuns(spec.primitives);
        return spec;
    }

    static List<TemplateLibrary.RawPoint> ResolvePrimeRaw(
        char letter,
        TemplateLibrary library,
        out string sourceId,
        out bool fallback)
    {
        fallback = false;
        sourceId = "";

        if (library != null)
        {
            TemplateLibrary.TemplateEntry prime = library.GetPrimeTemplate(letter.ToString());
            if (prime == null)
                prime = library.GetPrimeTemplate(char.ToLowerInvariant(letter).ToString());

            if (prime != null && prime.points != null && prime.points.Count > 1)
            {
                sourceId = prime.id;
                return TemplateLibrary.GetUnityOrientedRawPoints(prime.points);
            }
        }

        fallback = true;
        foreach ((string name, List<PDollarRecognizer.Point> points) in PDollarRecognizer.CreateDefaultAlphabetTemplateSnapshot())
        {
            if (string.IsNullOrEmpty(name) || char.ToUpperInvariant(name[0]) != letter)
                continue;

            var raw = new List<TemplateLibrary.RawPoint>();
            foreach (PDollarRecognizer.Point point in points)
                raw.Add(new TemplateLibrary.RawPoint(point.x, point.y, point.id));
            sourceId = $"builtin:{letter}";
            return raw;
        }

        sourceId = $"empty:{letter}";
        return new List<TemplateLibrary.RawPoint>();
    }

    static void AddPrimitivesForStroke(List<FormationPrimitive> primitives, List<Vector2> stroke)
    {
        // Preserve a deliberately drawn elbow before resampling/smoothing can
        // round it into a curve. This is the common one-stroke construction
        // for L and the two legs of a handwritten A.
        if (stroke != null && stroke.Count == 3)
        {
            FormationAxis firstAxis = ClassifyAxis(stroke[0], stroke[1]);
            FormationAxis secondAxis = ClassifyAxis(stroke[1], stroke[2]);
            if (firstAxis != FormationAxis.None &&
                secondAxis != FormationAxis.None &&
                firstAxis != secondAxis)
            {
                AddLineSegments(primitives, stroke);
                return;
            }
        }

        List<Vector2> cleaned = CleanStroke(stroke);
        if (cleaned.Count < 2)
            return;

        float length = PathLength(cleaned);
        if (length < 8f)
            return;

        Rect bounds = Bounds(cleaned);
        float diagonal = Mathf.Max(1f, Mathf.Sqrt(bounds.width * bounds.width + bounds.height * bounds.height));
        float closure = Vector2.Distance(cleaned[0], cleaned[^1]) / diagonal;
        float sweep = ComputeAngleSweep(cleaned, bounds.center);
        float chord = Vector2.Distance(cleaned[0], cleaned[^1]);
        float pathToChord = length / Mathf.Max(1f, chord);
        List<Vector2> simplified = Simplify(cleaned, 8f);

        // A three-point polyline is a deliberate corner (L, V and the first
        // stroke of A), not a curve. Detect it before the broad curve sweep
        // rule below, otherwise the guide loses the exact next stroke that a
        // learner needs to see.
        if (simplified.Count == 3)
        {
            AddLineSegments(primitives, simplified);
            return;
        }

        if (closure < 0.28f && sweep > 230f)
        {
            primitives.Add(BuildPrimitive(FormationPrimitiveKind.Loop, cleaned));
            return;
        }

        if (sweep > 120f && pathToChord > 1.25f)
        {
            primitives.Add(BuildPrimitive(FormationPrimitiveKind.Curve, cleaned));
            return;
        }

        if (simplified.Count <= 2)
        {
            primitives.Add(BuildPrimitive(FormationPrimitiveKind.Line, cleaned));
            return;
        }

        AddLineSegments(primitives, simplified);
    }

    static void AddLineSegments(List<FormationPrimitive> primitives, List<Vector2> points)
    {
        for (int i = 1; i < points.Count; i++)
        {
            var segment = new List<Vector2> { points[i - 1], points[i] };
            if (Vector2.Distance(segment[0], segment[1]) >= 10f)
                primitives.Add(BuildPrimitive(FormationPrimitiveKind.Line, segment));
        }
    }

    static FormationPrimitive BuildPrimitive(FormationPrimitiveKind kind, List<Vector2> path)
    {
        var primitive = new FormationPrimitive
        {
            kind = kind,
            path = new List<Vector2>(path),
            bounds = Bounds(path),
            start = path[0],
            end = path[^1],
            reversible = kind == FormationPrimitiveKind.Line || kind == FormationPrimitiveKind.Curve
        };

        primitive.center = primitive.bounds.center;
        primitive.pathLength = PathLength(path);
        float diagonal = Mathf.Max(1f, Mathf.Sqrt(primitive.bounds.width * primitive.bounds.width + primitive.bounds.height * primitive.bounds.height));
        primitive.openness = Vector2.Distance(primitive.start, primitive.end) / diagonal;
        primitive.closed = primitive.openness < 0.28f;
        primitive.curvatureDegrees = ComputeAngleSweep(path, primitive.center);
        primitive.axis = ClassifyAxis(primitive.start, primitive.end);
        primitive.horizontalTurns = CountDirectionTurns(path, horizontal: true);
        primitive.verticalTurns = CountDirectionTurns(path, horizontal: false);
        return primitive;
    }

    static void MergeLineRuns(List<FormationPrimitive> primitives)
    {
        for (int i = primitives.Count - 2; i >= 0; i--)
        {
            FormationPrimitive current = primitives[i];
            FormationPrimitive next = primitives[i + 1];
            if (current.kind != FormationPrimitiveKind.Line ||
                next.kind != FormationPrimitiveKind.Line ||
                current.axis != next.axis)
                continue;

            // Same-axis strokes on separate parts of a letter (the two posts
            // of H) are not a single line run. Only join segments that meet.
            float joinTolerance = Mathf.Max(4f, Mathf.Min(current.pathLength, next.pathLength) * 0.12f);
            if (Vector2.Distance(current.end, next.start) > joinTolerance)
                continue;

            float angle = Vector2.Angle(current.end - current.start, next.end - next.start);
            if (angle > 24f && angle < 156f)
                continue;

            var merged = new List<Vector2>(current.path);
            merged.AddRange(next.path);
            primitives[i] = BuildPrimitive(FormationPrimitiveKind.Line, merged);
            primitives.RemoveAt(i + 1);
        }
    }

    static List<Vector2> CleanStroke(List<Vector2> stroke)
    {
        List<Vector2> resampled = Resample(stroke, Mathf.Clamp(stroke.Count, 12, 48));
        if (resampled.Count < 3)
            return resampled;

        var smoothed = new List<Vector2>(resampled.Count);
        smoothed.Add(resampled[0]);
        for (int i = 1; i < resampled.Count - 1; i++)
            smoothed.Add((resampled[i - 1] + resampled[i] * 2f + resampled[i + 1]) * 0.25f);
        smoothed.Add(resampled[^1]);
        return smoothed;
    }

    static FormationAxis ClassifyAxis(Vector2 start, Vector2 end)
    {
        Vector2 delta = end - start;
        if (delta.sqrMagnitude < 1f)
            return FormationAxis.None;

        float ax = Mathf.Abs(delta.x);
        float ay = Mathf.Abs(delta.y);
        if (ax > ay * 1.25f)
            return FormationAxis.Horizontal;
        if (ay > ax * 1.25f)
            return FormationAxis.Vertical;
        return Mathf.Sign(delta.x) == Mathf.Sign(delta.y)
            ? FormationAxis.DiagonalPositive
            : FormationAxis.DiagonalNegative;
    }

    static List<List<Vector2>> RawToLocalStrokes(List<TemplateLibrary.RawPoint> rawPoints, Vector2 targetSize)
    {
        var strokes = new SortedDictionary<int, List<Vector2>>();
        if (rawPoints == null || rawPoints.Count == 0)
            return new List<List<Vector2>>();

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minY = float.MaxValue;
        float maxY = float.MinValue;
        foreach (TemplateLibrary.RawPoint raw in rawPoints)
        {
            if (raw == null)
                continue;
            if (raw.x < minX) minX = raw.x;
            if (raw.x > maxX) maxX = raw.x;
            if (raw.y < minY) minY = raw.y;
            if (raw.y > maxY) maxY = raw.y;
        }

        if (minX == float.MaxValue)
            return new List<List<Vector2>>();

        float rangeX = Mathf.Max(1f, maxX - minX);
        float rangeY = Mathf.Max(1f, maxY - minY);
        float scale = Mathf.Min(targetSize.x / rangeX, targetSize.y / rangeY) * GuideScale;
        Vector2 center = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);

        foreach (TemplateLibrary.RawPoint raw in rawPoints)
        {
            if (raw == null)
                continue;

            if (!strokes.TryGetValue(raw.strokeId, out List<Vector2> stroke))
            {
                stroke = new List<Vector2>();
                strokes[raw.strokeId] = stroke;
            }

            stroke.Add((new Vector2(raw.x, raw.y) - center) * scale);
        }

        return new List<List<Vector2>>(strokes.Values);
    }

    static List<List<Vector2>> NormalizeStrokes(List<List<Vector2>> strokes, float squareSize)
    {
        List<Vector2> all = Flatten(strokes);
        Rect bounds = Bounds(all);
        float size = Mathf.Max(1f, Mathf.Max(bounds.width, bounds.height));
        Vector2 center = bounds.center;

        var normalized = new List<List<Vector2>>();
        if (all.Count == 0)
            return normalized;

        foreach (List<Vector2> stroke in strokes)
        {
            if (stroke == null || stroke.Count == 0)
                continue;

            var converted = new List<Vector2>(stroke.Count);
            foreach (Vector2 point in stroke)
                converted.Add((point - center) / size * squareSize);
            normalized.Add(converted);
        }

        return normalized;
    }

    static List<Vector2> Simplify(List<Vector2> points, float epsilon)
    {
        if (points == null || points.Count <= 2)
            return points != null ? new List<Vector2>(points) : new List<Vector2>();

        var keep = new bool[points.Count];
        keep[0] = true;
        keep[^1] = true;
        SimplifyRange(points, 0, points.Count - 1, epsilon, keep);

        var simplified = new List<Vector2>();
        for (int i = 0; i < points.Count; i++)
            if (keep[i])
                simplified.Add(points[i]);
        return simplified;
    }

    static void SimplifyRange(List<Vector2> points, int start, int end, float epsilon, bool[] keep)
    {
        if (end <= start + 1)
            return;

        float maxDistance = 0f;
        int index = -1;
        for (int i = start + 1; i < end; i++)
        {
            float distance = DistanceToSegment(points[i], points[start], points[end]);
            if (distance > maxDistance)
            {
                maxDistance = distance;
                index = i;
            }
        }

        if (maxDistance <= epsilon || index < 0)
            return;

        keep[index] = true;
        SimplifyRange(points, start, index, epsilon, keep);
        SimplifyRange(points, index, end, epsilon, keep);
    }

    static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float denominator = ab.sqrMagnitude;
        if (denominator <= 0.001f)
            return Vector2.Distance(point, a);

        float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / denominator);
        return Vector2.Distance(point, a + ab * t);
    }

    static List<Vector2> Resample(List<Vector2> points, int targetCount)
    {
        if (points == null || points.Count == 0)
            return new List<Vector2>();
        if (points.Count == 1 || targetCount <= 1)
            return new List<Vector2>(points);

        float length = PathLength(points);
        if (length <= 0.001f)
            return new List<Vector2>(points);

        float interval = length / (targetCount - 1);
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

    static float ComputeAngleSweep(List<Vector2> points, Vector2 center)
    {
        if (points == null || points.Count < 3)
            return 0f;

        float sweep = 0f;
        float previous = Mathf.Atan2(points[0].y - center.y, points[0].x - center.x) * Mathf.Rad2Deg;
        for (int i = 1; i < points.Count; i++)
        {
            float current = Mathf.Atan2(points[i].y - center.y, points[i].x - center.x) * Mathf.Rad2Deg;
            sweep += Mathf.Abs(Mathf.DeltaAngle(previous, current));
            previous = current;
        }

        return sweep;
    }

    static int CountDirectionTurns(List<Vector2> points, bool horizontal)
    {
        if (points == null || points.Count < 3)
            return 0;

        int turns = 0;
        int previousSign = 0;
        for (int i = 1; i < points.Count; i++)
        {
            float delta = horizontal
                ? points[i].x - points[i - 1].x
                : points[i].y - points[i - 1].y;
            if (Mathf.Abs(delta) < 6f)
                continue;

            int sign = delta > 0f ? 1 : -1;
            if (previousSign != 0 && sign != previousSign)
                turns++;
            previousSign = sign;
        }

        return turns;
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

    static float PathLength(List<Vector2> points)
    {
        float total = 0f;
        if (points == null)
            return total;

        for (int i = 1; i < points.Count; i++)
            total += Vector2.Distance(points[i - 1], points[i]);
        return total;
    }

    static List<Vector2> Flatten(List<List<Vector2>> strokes)
    {
        var flat = new List<Vector2>();
        if (strokes == null)
            return flat;

        foreach (List<Vector2> stroke in strokes)
        {
            if (stroke == null)
                continue;
            flat.AddRange(stroke);
        }

        return flat;
    }

    static List<List<Vector2>> CopyStrokes(List<List<Vector2>> strokes)
    {
        var copy = new List<List<Vector2>>();
        if (strokes == null)
            return copy;

        foreach (List<Vector2> stroke in strokes)
            copy.Add(stroke != null ? new List<Vector2>(stroke) : new List<Vector2>());
        return copy;
    }
}
