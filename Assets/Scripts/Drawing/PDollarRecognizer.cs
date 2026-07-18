using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// P$ (PDollar) point-cloud gesture recogniser.
///
/// Changes vs original:
///   • RecognitionResult struct  — returns name AND raw score together
///   • SCORE_THRESHOLD           — rejects weak matches as "Unknown"
///   • RotateToZero              — rotation normalisation (your improvement from doc 4)
///   • Stroke-id is preserved through Resample when stroke boundaries matter
///   • LoadDefaultAlphabetTemplates() — geometric A-Z / a-z baseline
///   • All static helpers are pure functions — safe to call from any thread
/// </summary>
public class PDollarRecognizer
{
    // ── Types ──────────────────────────────────────────────────────────────

    public class Point
    {
        public float x, y;
        public int   id;   // stroke index

        public Point(float x, float y, int id)
        { this.x = x; this.y = y; this.id = id; }
    }

    public class Gesture
    {
        public string      name;
        public List<Point> points;
        public List<Point> rawPoints;
        public ShapeDescriptor descriptor;

        public Gesture(string name, List<Point> points)
        {
            this.name   = name;
            rawPoints = ClonePoints(points);
            this.points = Normalize(ClonePoints(points));
            descriptor  = BuildShapeDescriptor(this.points);
        }
    }

    public struct ShapeDescriptor
    {
        public float aspectRatio;
        public float openness;
        public float centerX;
        public float centerY;
        public float[] occupancy;
    }

    public struct RecognitionResult
    {
        public string name;
        public float  score;   // lower = better match
        public string bestCandidateName;
        public string runnerUpName;
        public float  runnerUpScore;
        public float  scoreMargin;
        public bool   isAmbiguous;
        public string pDollarName;
        public float  pDollarConfidence;
        public string neuralRecognizedName;
        public float  neuralConfidence;
        public float  combinedConfidence;
        public bool   recognizerAgreement;
        public string recognitionDecision;
        public bool   expectedLetterFiltered;
        public char   expectedLetter;
        // The unconstrained A-Z pass is retained when gameplay also asks for
        // an expected-letter match. It is an independent contradiction signal,
        // not an answer-key replacement.
        public string broadRecognizedName;
        public string broadBestCandidateName;
        public float  broadScore;
        public string broadRunnerUpName;
        public float  broadRunnerUpScore;
        public float  broadConfidence;
        public bool   broadIsAmbiguous;
        public float  calibratedThreshold;
        public float  calibrationSeparation;
        public bool   calibrationReliable;
        public bool   inputAssisted;
        public float  inputAssistFraction;
        public float  inputAssistMeanDistance;
        public float  inputAssistMaxDistance;
        public HandwritingSampleRecord captureSample;
    }

    // ── Constants ──────────────────────────────────────────────────────────

    /// <summary>
    /// Raw PDollar score ceiling for acceptance.
    /// Values above this → "Unknown".
    /// 120 works well with rotation normalisation; tune lower if false-positives appear.
    /// </summary>
    public const float SCORE_THRESHOLD     = 500f;
    public const float SCORE_MARGIN_THRESHOLD = 22f;

    const int   SAMPLING_RESOLUTION = 64;
    const float SQUARE_SIZE          = 250f;
    const int   SHAPE_GRID_SIZE      = 4;
    const float GRID_SHAPE_WEIGHT    = 120f;
    const float ASPECT_RATIO_WEIGHT  = 22f;
    const float OPENNESS_WEIGHT      = 45f;
    const float CENTER_BALANCE_WEIGHT = 28f;

    // ── Data ───────────────────────────────────────────────────────────────

    private List<Gesture> templates = new List<Gesture>();

    // ── Public API ─────────────────────────────────────────────────────────

    public void AddTemplate(string name, List<Point> points)
    {
        templates.Add(new Gesture(name, points));
    }

    public int TemplateCount => templates.Count;

    public List<(string name, List<Point> points)> CreateTemplateSnapshot()
    {
        var snapshot = new List<(string, List<Point>)>(templates.Count);
        foreach (Gesture template in templates)
            snapshot.Add((template.name, ClonePoints(template.points)));
        return snapshot;
    }

    public List<(string name, List<Point> points)> CreateRawTemplateSnapshot()
    {
        var snapshot = new List<(string, List<Point>)>(templates.Count);
        foreach (Gesture template in templates)
            snapshot.Add((template.name, ClonePoints(template.rawPoints)));
        return snapshot;
    }

    public static List<(string name, List<Point> points)> CreateDefaultAlphabetTemplateSnapshot()
    {
        var recognizer = new PDollarRecognizer();
        recognizer.LoadDefaultAlphabetTemplates();
        // Consumers of this public snapshot render and classify stroke order,
        // so expose authored points rather than P$'s resampled recognition
        // cloud (which intentionally collapses pen-lift boundaries).
        return recognizer.CreateRawTemplateSnapshot();
    }

    /// <summary>
    /// Recognise a gesture.
    /// Returns RecognitionResult.name == "Unknown" when score exceeds SCORE_THRESHOLD.
    /// </summary>
    public RecognitionResult Recognize(List<Point> points)
    {
        return RecognizeInternal(points, null, SCORE_THRESHOLD, rejectAmbiguous: true);
    }

    public RecognitionResult RecognizeMatching(
        List<Point> points,
        System.Predicate<string> templateNameFilter,
        float scoreThreshold)
    {
        return RecognizeInternal(points, templateNameFilter, scoreThreshold, rejectAmbiguous: false);
    }

    RecognitionResult RecognizeInternal(
        List<Point> points,
        System.Predicate<string> templateNameFilter,
        float scoreThreshold,
        bool rejectAmbiguous)
    {
        if (templates.Count == 0 || points == null || points.Count < 2)
            return new RecognitionResult
            {
                name = "Unknown",
                score = float.MaxValue,
                bestCandidateName = "Unknown",
                runnerUpName = "Unknown",
                runnerUpScore = float.MaxValue,
                scoreMargin = float.MaxValue,
                isAmbiguous = false
            };

        var candidate = Normalize(points);
        ShapeDescriptor candidateDescriptor = BuildShapeDescriptor(candidate);

        var bestScoreByName = new Dictionary<string, float>();

        foreach (var template in templates)
        {
            if (templateNameFilter != null && !templateNameFilter(template.name))
                continue;

            float d = GreedyCloudMatch(candidate, template.points);
            d += ComputeStructuralPenalty(candidateDescriptor, template);

            if (!bestScoreByName.TryGetValue(template.name, out float existing) || d < existing)
                bestScoreByName[template.name] = d;
        }

        if (bestScoreByName.Count == 0)
            return new RecognitionResult
            {
                name = "Unknown",
                score = float.MaxValue,
                bestCandidateName = "Unknown",
                runnerUpName = "Unknown",
                runnerUpScore = float.MaxValue,
                scoreMargin = float.MaxValue,
                isAmbiguous = false
            };

        float bestScore = float.MaxValue;
        float runnerUpScore = float.MaxValue;
        string bestMatch = "Unknown";
        string runnerUpMatch = "Unknown";

        foreach (var pair in bestScoreByName)
        {
            if (pair.Value < bestScore)
            {
                runnerUpScore = bestScore;
                runnerUpMatch = bestMatch;
                bestScore = pair.Value;
                bestMatch = pair.Key;
            }
            else if (pair.Value < runnerUpScore)
            {
                runnerUpScore = pair.Value;
                runnerUpMatch = pair.Key;
            }
        }

        float margin = runnerUpScore - bestScore;
        bool isAmbiguous = runnerUpScore < float.MaxValue && margin < SCORE_MARGIN_THRESHOLD;
        string topCandidateName = bestMatch;

        if (bestScore > scoreThreshold || (rejectAmbiguous && isAmbiguous))
            bestMatch = "Unknown";

        return new RecognitionResult
        {
            name = bestMatch,
            score = bestScore,
            bestCandidateName = topCandidateName,
            runnerUpName = runnerUpMatch,
            runnerUpScore = runnerUpScore,
            scoreMargin = margin,
            isAmbiguous = isAmbiguous
        };
    }

    // ── Core algorithm ─────────────────────────────────────────────────────

    float GreedyCloudMatch(List<Point> points, List<Point> template)
    {
        int   n    = points.Count;
        int   step = Mathf.Max(1, Mathf.FloorToInt(Mathf.Pow(n, 0.5f)));
        float min  = float.MaxValue;

        for (int i = 0; i < n; i += step)
        {
            float d1 = CloudDistance(points,   template, i);
            float d2 = CloudDistance(template, points,   i);
            min = Mathf.Min(min, Mathf.Min(d1, d2));
        }
        return min;
    }

    float CloudDistance(List<Point> pts1, List<Point> pts2, int start)
    {
        int    n       = pts1.Count;
        bool[] matched = new bool[n];
        float  sum     = 0f;
        int    i       = start;

        do
        {
            int   index   = -1;
            float minDist = float.MaxValue;

            for (int j = 0; j < n; j++)
            {
                if (!matched[j])
                {
                    float d = Distance(pts1[i], pts2[j]);
                    if (d < minDist) { minDist = d; index = j; }
                }
            }

            if (index == -1) break;

            matched[index] = true;
            float weight   = 1f - ((i - start + n) % n) / (float)n;
            sum += weight * minDist;
            i    = (i + 1) % n;

        } while (i != start);

        return sum;
    }

    // ── Normalisation pipeline ─────────────────────────────────────────────

    static List<Point> Normalize(List<Point> points)
    {
        var pts = Resample(points, SAMPLING_RESOLUTION);
        pts = Scale(pts);
        pts = TranslateToOrigin(pts);
        return pts;
    }

    static float ComputeStructuralPenalty(ShapeDescriptor candidate, Gesture template)
    {
        float penalty = 0f;
        ShapeDescriptor templateDescriptor = template.descriptor;

        penalty += Mathf.Abs(candidate.aspectRatio - templateDescriptor.aspectRatio) * ASPECT_RATIO_WEIGHT;
        penalty += Mathf.Abs(candidate.openness - templateDescriptor.openness) * OPENNESS_WEIGHT;
        penalty += (Mathf.Abs(candidate.centerX - templateDescriptor.centerX) +
                    Mathf.Abs(candidate.centerY - templateDescriptor.centerY)) * CENTER_BALANCE_WEIGHT;
        penalty += ComputeGridDistance(candidate.occupancy, templateDescriptor.occupancy) * GRID_SHAPE_WEIGHT;
        return penalty;
    }

    // ── Rotation normalisation (your improvement) ──────────────────────────

    static List<Point> RotateToZero(List<Point> points)
    {
        Point c     = Centroid(points);
        float theta = Mathf.Atan2(points[0].y - c.y, points[0].x - c.x);
        return RotateBy(points, -theta);
    }

    static List<Point> RotateBy(List<Point> points, float angle)
    {
        Point  c   = Centroid(points);
        float cos  = Mathf.Cos(angle);
        float sin  = Mathf.Sin(angle);
        var result = new List<Point>(points.Count);

        foreach (var p in points)
        {
            float dx = p.x - c.x;
            float dy = p.y - c.y;
            result.Add(new Point(dx * cos - dy * sin + c.x,
                                 dx * sin + dy * cos + c.y,
                                 p.id));
        }
        return result;
    }

    static Point Centroid(List<Point> points)
    {
        float x = 0, y = 0;
        foreach (var p in points) { x += p.x; y += p.y; }
        return new Point(x / points.Count, y / points.Count, 0);
    }

    // ── Resample ───────────────────────────────────────────────────────────

    static List<Point> Resample(List<Point> points, int n)
    {
        if (points.Count == 0) return points;

        float I = PathLength(points) / (n - 1);
        if (I <= 0f) return points;

        float D = 0f;
        var newPoints = new List<Point> { points[0] };

        for (int i = 1; i < points.Count; i++)
        {
            // Respect stroke boundaries — don't interpolate across pen-lifts
            if (points[i].id != points[i - 1].id) continue;

            float d = Distance(points[i - 1], points[i]);

            if ((D + d) >= I)
            {
                float t  = (I - D) / d;
                float qx = points[i - 1].x + t * (points[i].x - points[i - 1].x);
                float qy = points[i - 1].y + t * (points[i].y - points[i - 1].y);
                Point q  = new Point(qx, qy, points[i].id);
                newPoints.Add(q);
                points.Insert(i, q);
                D = 0f;
            }
            else D += d;
        }

        // Ensure exactly n points
        while (newPoints.Count < n)
            newPoints.Add(points[points.Count - 1]);
        if (newPoints.Count > n)
            newPoints.RemoveRange(n, newPoints.Count - n);

        return newPoints;
    }

    // ── Scale ──────────────────────────────────────────────────────────────

    static List<Point> Scale(List<Point> points)
    {
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;

        foreach (var p in points)
        {
            if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
        }

        float size = Mathf.Max(maxX - minX, maxY - minY);
        if (size < 1e-6f) return points;

        var scaled = new List<Point>(points.Count);
        foreach (var p in points)
            scaled.Add(new Point(
                (p.x - minX) / size * SQUARE_SIZE,
                (p.y - minY) / size * SQUARE_SIZE,
                p.id));
        return scaled;
    }

    // ── Translate to origin ────────────────────────────────────────────────

    static List<Point> TranslateToOrigin(List<Point> points)
    {
        Point c = Centroid(points);
        var   t = new List<Point>(points.Count);
        foreach (var p in points)
            t.Add(new Point(p.x - c.x, p.y - c.y, p.id));
        return t;
    }

    // ── Path helpers ───────────────────────────────────────────────────────

    static float PathLength(List<Point> points)
    {
        float d = 0;
        for (int i = 1; i < points.Count; i++)
            if (points[i].id == points[i - 1].id)
                d += Distance(points[i - 1], points[i]);
        return d;
    }

    static float Distance(Point a, Point b)
    {
        float dx = a.x - b.x, dy = a.y - b.y;
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    static ShapeDescriptor BuildShapeDescriptor(List<Point> points)
    {
        var descriptor = new ShapeDescriptor
        {
            aspectRatio = 1f,
            openness = 0f,
            centerX = 0.5f,
            centerY = 0.5f,
            occupancy = new float[SHAPE_GRID_SIZE * SHAPE_GRID_SIZE]
        };

        if (points == null || points.Count == 0)
            return descriptor;

        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        float sumX = 0f, sumY = 0f;

        foreach (var p in points)
        {
            if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
            sumX += p.x;
            sumY += p.y;
        }

        float width = Mathf.Max(1f, maxX - minX);
        float height = Mathf.Max(1f, maxY - minY);
        float diagonal = Mathf.Sqrt(width * width + height * height);

        descriptor.aspectRatio = width / height;
        descriptor.openness = points.Count > 1
            ? Distance(points[0], points[points.Count - 1]) / Mathf.Max(1f, diagonal)
            : 0f;
        descriptor.centerX = Mathf.Clamp01((sumX / points.Count - minX) / width);
        descriptor.centerY = Mathf.Clamp01((sumY / points.Count - minY) / height);

        for (int i = 0; i < points.Count; i++)
            AccumulateGridOccupancy(descriptor.occupancy, points[i], minX, minY, width, height);

        for (int i = 1; i < points.Count; i++)
        {
            if (points[i].id != points[i - 1].id)
                continue;

            float segmentLength = Distance(points[i - 1], points[i]);
            int steps = Mathf.Max(2, Mathf.CeilToInt(segmentLength / 12f));
            for (int step = 1; step <= steps; step++)
            {
                float t = step / (float)steps;
                var sample = new Point(
                    Mathf.Lerp(points[i - 1].x, points[i].x, t),
                    Mathf.Lerp(points[i - 1].y, points[i].y, t),
                    points[i].id);
                AccumulateGridOccupancy(descriptor.occupancy, sample, minX, minY, width, height);
            }
        }

        float maxValue = 0f;
        for (int i = 0; i < descriptor.occupancy.Length; i++)
            if (descriptor.occupancy[i] > maxValue)
                maxValue = descriptor.occupancy[i];

        if (maxValue > 0f)
        {
            for (int i = 0; i < descriptor.occupancy.Length; i++)
                descriptor.occupancy[i] /= maxValue;
        }

        return descriptor;
    }

    static void AccumulateGridOccupancy(float[] occupancy, Point point, float minX, float minY, float width, float height)
    {
        if (occupancy == null || occupancy.Length == 0)
            return;

        float nx = Mathf.Clamp01((point.x - minX) / width);
        float ny = Mathf.Clamp01((point.y - minY) / height);
        int gx = Mathf.Clamp(Mathf.FloorToInt(nx * SHAPE_GRID_SIZE), 0, SHAPE_GRID_SIZE - 1);
        int gy = Mathf.Clamp(Mathf.FloorToInt(ny * SHAPE_GRID_SIZE), 0, SHAPE_GRID_SIZE - 1);
        occupancy[gy * SHAPE_GRID_SIZE + gx] += 1f;
    }

    static float ComputeGridDistance(float[] a, float[] b)
    {
        if (a == null || b == null || a.Length != b.Length)
            return 1f;

        float total = 0f;
        for (int i = 0; i < a.Length; i++)
            total += Mathf.Abs(a[i] - b[i]);

        return total / a.Length;
    }

    // ── Template builder helpers ───────────────────────────────────────────

    static List<Point> Pts(int strokeId, params (float x, float y)[] coords)
    {
        var list = new List<Point>(coords.Length);
        foreach (var c in coords)
            list.Add(new Point(c.x, c.y, strokeId));
        return list;
    }

    static List<Point> PtsMulti(params (int id, (float x, float y)[] coords)[] strokes)
    {
        var list = new List<Point>();
        foreach (var stroke in strokes)
            foreach (var c in stroke.coords)
                list.Add(new Point(c.x, c.y, stroke.id));
        return list;
    }

    // ── Default geometric alphabet ─────────────────────────────────────────
    // Three geometric variants per letter for baseline coverage.
    // Replace / supplement with recorded samples from TemplateLibrary
    // for handwriting that actually matches your style.

    static List<Point> ClonePoints(List<Point> points)
    {
        var clone = new List<Point>(points != null ? points.Count : 0);
        if (points == null)
            return clone;

        foreach (Point point in points)
            if (point != null)
                clone.Add(new Point(point.x, point.y, point.id));

        return clone;
    }

    public void LoadDefaultAlphabetTemplates()
    {
        // A
        AddTemplate("A", PtsMulti((0,new[]{(50f,0f),(125f,250f)}),(1,new[]{(125f,250f),(200f,0f)}),(2,new[]{(80f,120f),(170f,120f)})));
        AddTemplate("A", PtsMulti((0,new[]{(60f,0f),(125f,250f)}),(1,new[]{(125f,250f),(190f,0f)}),(2,new[]{(90f,140f),(160f,140f)})));
        AddTemplate("A", PtsMulti((0,new[]{(50f,0f),(120f,250f)}),(1,new[]{(120f,250f),(210f,0f)}),(2,new[]{(70f,110f),(180f,110f)})));

        // B
        AddTemplate("B", PtsMulti((0,new[]{(50f,0f),(50f,250f)}),(1,new[]{(50f,250f),(180f,200f),(50f,125f)}),(2,new[]{(50f,125f),(180f,50f),(50f,0f)})));
        AddTemplate("B", PtsMulti((0,new[]{(55f,0f),(55f,250f)}),(1,new[]{(55f,250f),(170f,190f),(55f,130f)}),(2,new[]{(55f,130f),(170f,60f),(55f,0f)})));
        AddTemplate("B", PtsMulti((0,new[]{(60f,0f),(60f,250f)}),(1,new[]{(60f,250f),(190f,210f),(60f,140f)}),(2,new[]{(60f,140f),(190f,70f),(60f,0f)})));

        // C
        AddTemplate("C", Pts(0,(200f,50f),(150f,10f),(80f,10f),(30f,80f),(30f,170f),(80f,240f),(150f,240f),(200f,200f)));
        AddTemplate("C", Pts(0,(210f,60f),(160f,20f),(90f,20f),(40f,90f),(40f,160f),(90f,230f),(160f,230f),(210f,190f)));
        AddTemplate("C", Pts(0,(190f,40f),(140f,5f),(70f,5f),(25f,85f),(25f,165f),(70f,245f),(140f,245f),(190f,210f)));
        AddTemplate("C", Pts(0,(200f,200f),(150f,240f),(80f,240f),(30f,170f),(30f,80f),(80f,10f),(150f,10f),(200f,50f)));
        AddTemplate("C", Pts(0,(210f,190f),(160f,230f),(90f,230f),(40f,160f),(40f,90f),(90f,20f),(160f,20f),(210f,60f)));
        AddTemplate("C", Pts(0,(190f,210f),(140f,245f),(70f,245f),(25f,165f),(25f,85f),(70f,5f),(140f,5f),(190f,40f)));

        // D
        AddTemplate("D", PtsMulti((0,new[]{(50f,0f),(50f,250f)}),(1,new[]{(50f,250f),(180f,180f),(180f,70f),(50f,0f)})));
        AddTemplate("D", PtsMulti((0,new[]{(55f,0f),(55f,250f)}),(1,new[]{(55f,250f),(170f,170f),(170f,80f),(55f,0f)})));
        AddTemplate("D", PtsMulti((0,new[]{(60f,0f),(60f,250f)}),(1,new[]{(60f,250f),(190f,190f),(190f,60f),(60f,0f)})));

        // E
        AddTemplate("E", PtsMulti((0,new[]{(200f,0f),(50f,0f),(50f,250f),(200f,250f)}),(1,new[]{(50f,125f),(160f,125f)})));
        AddTemplate("E", PtsMulti((0,new[]{(190f,0f),(55f,0f),(55f,250f),(190f,250f)}),(1,new[]{(55f,130f),(155f,130f)})));
        AddTemplate("E", PtsMulti((0,new[]{(205f,0f),(45f,0f),(45f,250f),(205f,250f)}),(1,new[]{(45f,120f),(170f,120f)})));

        // F
        AddTemplate("F", PtsMulti((0,new[]{(200f,0f),(50f,0f),(50f,250f)}),(1,new[]{(50f,125f),(160f,125f)})));
        AddTemplate("F", PtsMulti((0,new[]{(190f,0f),(55f,0f),(55f,250f)}),(1,new[]{(55f,130f),(155f,130f)})));
        AddTemplate("F", PtsMulti((0,new[]{(205f,0f),(45f,0f),(45f,250f)}),(1,new[]{(45f,115f),(170f,115f)})));

        // G
        AddTemplate("G", Pts(0,(220f,50f),(180f,10f),(80f,0f),(30f,50f),(10f,125f),(30f,200f),(80f,250f),(180f,240f),(220f,190f),(220f,125f),(130f,125f)));
        AddTemplate("G", Pts(0,(210f,55f),(175f,15f),(85f,5f),(35f,55f),(15f,125f),(35f,195f),(85f,245f),(175f,235f),(210f,185f),(210f,120f),(125f,120f)));
        AddTemplate("G", Pts(0,(225f,45f),(185f,8f),(75f,8f),(25f,60f),(5f,130f),(25f,205f),(75f,248f),(185f,242f),(225f,195f),(225f,130f),(135f,130f)));

        // H
        AddTemplate("H", PtsMulti((0,new[]{(50f,0f),(50f,250f)}),(1,new[]{(200f,0f),(200f,250f)}),(2,new[]{(50f,125f),(200f,125f)})));
        AddTemplate("H", PtsMulti((0,new[]{(55f,0f),(55f,250f)}),(1,new[]{(195f,0f),(195f,250f)}),(2,new[]{(55f,130f),(195f,130f)})));
        AddTemplate("H", PtsMulti((0,new[]{(45f,0f),(45f,250f)}),(1,new[]{(205f,0f),(205f,250f)}),(2,new[]{(45f,120f),(205f,120f)})));

        // I
        AddTemplate("I", Pts(0,(125f,0f),(125f,250f)));
        AddTemplate("I", Pts(0,(120f,0f),(120f,250f)));
        AddTemplate("I", Pts(0,(130f,0f),(130f,250f)));

        // J
        AddTemplate("J", Pts(0,(180f,0f),(180f,200f),(150f,240f),(100f,250f),(60f,235f),(45f,200f),(50f,170f)));
        AddTemplate("J", Pts(0,(175f,0f),(175f,195f),(145f,238f),(95f,248f),(55f,232f),(40f,195f),(48f,165f)));
        AddTemplate("J", Pts(0,(185f,0f),(185f,205f),(155f,242f),(105f,252f),(65f,238f),(50f,205f),(55f,175f)));

        // K
        AddTemplate("K", PtsMulti((0,new[]{(50f,0f),(50f,250f)}),(1,new[]{(200f,0f),(50f,125f),(200f,250f)})));
        AddTemplate("K", PtsMulti((0,new[]{(55f,0f),(55f,250f)}),(1,new[]{(195f,0f),(55f,130f),(195f,250f)})));
        AddTemplate("K", PtsMulti((0,new[]{(45f,0f),(45f,250f)}),(1,new[]{(205f,0f),(45f,120f),(205f,250f)})));

        // L
        AddTemplate("L", Pts(0,(50f,0f),(50f,250f),(200f,250f)));
        AddTemplate("L", Pts(0,(55f,0f),(55f,250f),(195f,250f)));
        AddTemplate("L", Pts(0,(45f,0f),(45f,250f),(205f,250f)));

        // M
        AddTemplate("M", Pts(0,(30f,250f),(30f,0f),(125f,125f),(220f,0f),(220f,250f)));
        AddTemplate("M", Pts(0,(35f,250f),(35f,0f),(120f,120f),(215f,0f),(215f,250f)));
        AddTemplate("M", Pts(0,(25f,250f),(25f,0f),(130f,130f),(225f,0f),(225f,250f)));

        // N
        AddTemplate("N", Pts(0,(30f,250f),(30f,0f),(220f,250f),(220f,0f)));
        AddTemplate("N", Pts(0,(35f,250f),(35f,0f),(215f,250f),(215f,0f)));
        AddTemplate("N", Pts(0,(25f,250f),(25f,0f),(225f,250f),(225f,0f)));

        // O
        AddTemplate("O", Pts(0,(125f,0f),(50f,30f),(10f,100f),(10f,150f),(50f,220f),(125f,250f),(200f,220f),(240f,150f),(240f,100f),(200f,30f),(125f,0f)));
        AddTemplate("O", Pts(0,(125f,5f),(55f,35f),(15f,105f),(15f,145f),(55f,215f),(125f,245f),(195f,215f),(235f,145f),(235f,105f),(195f,35f),(125f,5f)));
        AddTemplate("O", Pts(0,(125f,8f),(48f,28f),(8f,98f),(8f,155f),(48f,225f),(125f,252f),(202f,225f),(242f,155f),(242f,98f),(202f,28f),(125f,8f)));

        // P
        AddTemplate("P", PtsMulti((0,new[]{(50f,250f),(50f,0f)}),(1,new[]{(50f,0f),(175f,30f),(190f,80f),(175f,130f),(50f,125f)})));
        AddTemplate("P", PtsMulti((0,new[]{(55f,250f),(55f,0f)}),(1,new[]{(55f,0f),(170f,35f),(185f,85f),(170f,135f),(55f,130f)})));
        AddTemplate("P", PtsMulti((0,new[]{(45f,250f),(45f,0f)}),(1,new[]{(45f,0f),(180f,25f),(195f,75f),(180f,125f),(45f,120f)})));

        // Q
        AddTemplate("Q", Pts(0,(125f,0f),(50f,30f),(10f,100f),(10f,150f),(50f,220f),(125f,250f),(200f,220f),(240f,150f),(240f,100f),(200f,30f),(125f,0f),(170f,200f),(220f,250f)));
        AddTemplate("Q", Pts(0,(125f,5f),(55f,35f),(15f,105f),(15f,145f),(55f,215f),(125f,245f),(195f,215f),(235f,145f),(235f,105f),(195f,35f),(125f,5f),(165f,205f),(215f,255f)));
        AddTemplate("Q", Pts(0,(125f,2f),(52f,32f),(12f,102f),(12f,148f),(52f,218f),(125f,248f),(198f,218f),(238f,148f),(238f,102f),(198f,32f),(125f,2f),(168f,202f),(218f,252f)));

        // R
        AddTemplate("R", PtsMulti((0,new[]{(50f,250f),(50f,0f)}),(1,new[]{(50f,0f),(175f,30f),(190f,80f),(175f,130f),(50f,125f),(200f,250f)})));
        AddTemplate("R", PtsMulti((0,new[]{(55f,250f),(55f,0f)}),(1,new[]{(55f,0f),(170f,35f),(185f,85f),(170f,135f),(55f,130f),(195f,250f)})));
        AddTemplate("R", PtsMulti((0,new[]{(45f,250f),(45f,0f)}),(1,new[]{(45f,0f),(180f,25f),(195f,75f),(180f,125f),(45f,120f),(205f,250f)})));

        // S
        AddTemplate("S", Pts(0,(210f,30f),(180f,5f),(120f,0f),(60f,15f),(35f,55f),(55f,100f),(125f,125f),(195f,150f),(215f,195f),(190f,235f),(130f,250f),(70f,245f),(40f,220f)));
        AddTemplate("S", Pts(0,(205f,35f),(175f,8f),(115f,3f),(55f,18f),(30f,58f),(50f,103f),(120f,128f),(190f,153f),(210f,198f),(185f,238f),(125f,248f),(65f,243f),(35f,215f)));
        AddTemplate("S", Pts(0,(215f,25f),(185f,2f),(125f,0f),(65f,12f),(40f,52f),(60f,97f),(130f,122f),(200f,147f),(220f,192f),(195f,232f),(135f,252f),(75f,247f),(45f,225f)));

        // T
        AddTemplate("T", PtsMulti((0,new[]{(30f,0f),(220f,0f)}),(1,new[]{(125f,0f),(125f,250f)})));
        AddTemplate("T", PtsMulti((0,new[]{(35f,0f),(215f,0f)}),(1,new[]{(125f,0f),(125f,250f)})));
        AddTemplate("T", PtsMulti((0,new[]{(25f,0f),(225f,0f)}),(1,new[]{(125f,0f),(125f,250f)})));

        // U
        AddTemplate("U", Pts(0,(50f,0f),(50f,200f),(80f,240f),(125f,250f),(170f,240f),(200f,200f),(200f,0f)));
        AddTemplate("U", Pts(0,(55f,0f),(55f,195f),(85f,238f),(125f,248f),(165f,238f),(195f,195f),(195f,0f)));
        AddTemplate("U", Pts(0,(45f,0f),(45f,205f),(75f,242f),(125f,252f),(175f,242f),(205f,205f),(205f,0f)));

        // V
        AddTemplate("V", Pts(0,(30f,0f),(125f,250f),(220f,0f)));
        AddTemplate("V", Pts(0,(35f,0f),(125f,245f),(215f,0f)));
        AddTemplate("V", Pts(0,(25f,0f),(125f,255f),(225f,0f)));

        // W
        AddTemplate("W", Pts(0,(20f,0f),(70f,250f),(125f,150f),(180f,250f),(230f,0f)));
        AddTemplate("W", Pts(0,(25f,0f),(75f,245f),(125f,145f),(175f,245f),(225f,0f)));
        AddTemplate("W", Pts(0,(15f,0f),(65f,255f),(125f,155f),(185f,255f),(235f,0f)));

        // X
        AddTemplate("X", PtsMulti((0,new[]{(30f,0f),(220f,250f)}),(1,new[]{(220f,0f),(30f,250f)})));
        AddTemplate("X", PtsMulti((0,new[]{(35f,0f),(215f,250f)}),(1,new[]{(215f,0f),(35f,250f)})));
        AddTemplate("X", PtsMulti((0,new[]{(25f,0f),(225f,250f)}),(1,new[]{(225f,0f),(25f,250f)})));

        // Y
        AddTemplate("Y", PtsMulti((0,new[]{(30f,0f),(125f,130f),(220f,0f)}),(1,new[]{(125f,130f),(125f,250f)})));
        AddTemplate("Y", PtsMulti((0,new[]{(35f,0f),(125f,125f),(215f,0f)}),(1,new[]{(125f,125f),(125f,250f)})));
        AddTemplate("Y", PtsMulti((0,new[]{(25f,0f),(125f,135f),(225f,0f)}),(1,new[]{(125f,135f),(125f,250f)})));

        // Z
        AddTemplate("Z", Pts(0,(30f,0f),(220f,0f),(30f,250f),(220f,250f)));
        AddTemplate("Z", Pts(0,(35f,0f),(215f,0f),(35f,250f),(215f,250f)));
        AddTemplate("Z", Pts(0,(25f,0f),(225f,0f),(25f,250f),(225f,250f)));

        Debug.Log($"[PDollarRecognizer] Loaded {templates.Count} built-in templates (A-Z, 3 variants each).");
    }
}
