using System.Globalization;

namespace TemplateRecorderStandalone;

public sealed class PortableRecognizer
{
    public sealed class Point
    {
        public float x;
        public float y;
        public int id;

        public Point(float x, float y, int id)
        {
            this.x = x;
            this.y = y;
            this.id = id;
        }
    }

    public sealed class Gesture
    {
        public string name;
        public List<Point> points;
        public ShapeDescriptor descriptor;

        public Gesture(string name, List<Point> points)
        {
            this.name = name;
            this.points = Normalize(ClonePoints(points));
            descriptor = BuildShapeDescriptor(this.points);
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
        public float score;
        public string bestCandidateName;
        public string runnerUpName;
        public float runnerUpScore;
        public float scoreMargin;
        public bool isAmbiguous;
    }

    public const float SCORE_THRESHOLD = 350f;
    public const float SCORE_MARGIN_THRESHOLD = 22f;

    const int SAMPLING_RESOLUTION = 64;
    const float SQUARE_SIZE = 250f;
    const int SHAPE_GRID_SIZE = 4;
    const float GRID_SHAPE_WEIGHT = 120f;
    const float ASPECT_RATIO_WEIGHT = 22f;
    const float OPENNESS_WEIGHT = 45f;
    const float CENTER_BALANCE_WEIGHT = 28f;

    readonly List<Gesture> _templates = new();

    public int TemplateCount => _templates.Count;

    public void ClearTemplates()
    {
        _templates.Clear();
    }

    public void AddTemplate(string name, List<Point> points)
    {
        if (string.IsNullOrWhiteSpace(name) || points == null || points.Count < 2)
            return;

        _templates.Add(new Gesture(name, points));
    }

    public RecognitionResult Recognize(List<Point> points)
    {
        if (_templates.Count == 0 || points == null || points.Count < 2)
            return UnknownResult();

        List<Point> candidate = Normalize(ClonePoints(points));
        ShapeDescriptor candidateDescriptor = BuildShapeDescriptor(candidate);
        var bestScoreByName = new Dictionary<string, float>(StringComparer.Ordinal);

        foreach (Gesture template in _templates)
        {
            float distance = GreedyCloudMatch(candidate, template.points);
            distance += ComputeStructuralPenalty(candidateDescriptor, template);

            if (!bestScoreByName.TryGetValue(template.name, out float existing) || distance < existing)
                bestScoreByName[template.name] = distance;
        }

        float bestScore = float.MaxValue;
        float runnerUpScore = float.MaxValue;
        string bestMatch = "Unknown";
        string runnerUpMatch = "Unknown";

        foreach ((string name, float value) in bestScoreByName)
        {
            if (value < bestScore)
            {
                runnerUpScore = bestScore;
                runnerUpMatch = bestMatch;
                bestScore = value;
                bestMatch = name;
            }
            else if (value < runnerUpScore)
            {
                runnerUpScore = value;
                runnerUpMatch = name;
            }
        }

        float margin = runnerUpScore - bestScore;
        bool isAmbiguous = runnerUpScore < float.MaxValue && margin < SCORE_MARGIN_THRESHOLD;
        string topCandidateName = bestMatch;

        if (bestScore > SCORE_THRESHOLD || isAmbiguous)
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

    public void LoadDefaultAlphabetTemplates()
    {
        ClearTemplates();

        AddTemplate("A", PtsMulti((0, new[] { (50f, 0f), (125f, 250f), (200f, 0f) }), (1, new[] { (80f, 120f), (170f, 120f) })));
        AddTemplate("A", PtsMulti((0, new[] { (60f, 0f), (125f, 250f), (190f, 0f) }), (1, new[] { (90f, 140f), (160f, 140f) })));
        AddTemplate("A", PtsMulti((0, new[] { (50f, 0f), (120f, 250f), (210f, 0f) }), (1, new[] { (70f, 110f), (180f, 110f) })));

        AddTemplate("B", PtsMulti((0, new[] { (50f, 0f), (50f, 250f) }), (1, new[] { (50f, 250f), (180f, 200f), (50f, 125f) }), (2, new[] { (50f, 125f), (180f, 50f), (50f, 0f) })));
        AddTemplate("B", PtsMulti((0, new[] { (55f, 0f), (55f, 250f) }), (1, new[] { (55f, 250f), (170f, 190f), (55f, 130f) }), (2, new[] { (55f, 130f), (170f, 60f), (55f, 0f) })));
        AddTemplate("B", PtsMulti((0, new[] { (60f, 0f), (60f, 250f) }), (1, new[] { (60f, 250f), (190f, 210f), (60f, 140f) }), (2, new[] { (60f, 140f), (190f, 70f), (60f, 0f) })));

        AddTemplate("C", Pts(0, (200f, 50f), (150f, 10f), (80f, 10f), (30f, 80f), (30f, 170f), (80f, 240f), (150f, 240f), (200f, 200f)));
        AddTemplate("C", Pts(0, (210f, 60f), (160f, 20f), (90f, 20f), (40f, 90f), (40f, 160f), (90f, 230f), (160f, 230f), (210f, 190f)));
        AddTemplate("C", Pts(0, (190f, 40f), (140f, 5f), (70f, 5f), (25f, 85f), (25f, 165f), (70f, 245f), (140f, 245f), (190f, 210f)));

        AddTemplate("D", PtsMulti((0, new[] { (50f, 0f), (50f, 250f) }), (1, new[] { (50f, 250f), (180f, 180f), (180f, 70f), (50f, 0f) })));
        AddTemplate("D", PtsMulti((0, new[] { (55f, 0f), (55f, 250f) }), (1, new[] { (55f, 250f), (170f, 170f), (170f, 80f), (55f, 0f) })));
        AddTemplate("D", PtsMulti((0, new[] { (60f, 0f), (60f, 250f) }), (1, new[] { (60f, 250f), (190f, 190f), (190f, 60f), (60f, 0f) })));

        AddTemplate("E", PtsMulti((0, new[] { (200f, 0f), (50f, 0f), (50f, 250f), (200f, 250f) }), (1, new[] { (50f, 125f), (160f, 125f) })));
        AddTemplate("E", PtsMulti((0, new[] { (190f, 0f), (55f, 0f), (55f, 250f), (190f, 250f) }), (1, new[] { (55f, 130f), (155f, 130f) })));
        AddTemplate("E", PtsMulti((0, new[] { (205f, 0f), (45f, 0f), (45f, 250f), (205f, 250f) }), (1, new[] { (45f, 120f), (170f, 120f) })));

        AddTemplate("F", PtsMulti((0, new[] { (200f, 0f), (50f, 0f), (50f, 250f) }), (1, new[] { (50f, 125f), (160f, 125f) })));
        AddTemplate("F", PtsMulti((0, new[] { (190f, 0f), (55f, 0f), (55f, 250f) }), (1, new[] { (55f, 130f), (155f, 130f) })));
        AddTemplate("F", PtsMulti((0, new[] { (205f, 0f), (45f, 0f), (45f, 250f) }), (1, new[] { (45f, 115f), (170f, 115f) })));

        AddTemplate("G", Pts(0, (220f, 50f), (180f, 10f), (80f, 0f), (30f, 50f), (10f, 125f), (30f, 200f), (80f, 250f), (180f, 240f), (220f, 190f), (220f, 125f), (130f, 125f)));
        AddTemplate("G", Pts(0, (210f, 55f), (175f, 15f), (85f, 5f), (35f, 55f), (15f, 125f), (35f, 195f), (85f, 245f), (175f, 235f), (210f, 185f), (210f, 120f), (125f, 120f)));
        AddTemplate("G", Pts(0, (225f, 45f), (185f, 8f), (75f, 8f), (25f, 60f), (5f, 130f), (25f, 205f), (75f, 248f), (185f, 242f), (225f, 195f), (225f, 130f), (135f, 130f)));

        AddTemplate("H", PtsMulti((0, new[] { (50f, 0f), (50f, 250f) }), (1, new[] { (200f, 0f), (200f, 250f) }), (2, new[] { (50f, 125f), (200f, 125f) })));
        AddTemplate("H", PtsMulti((0, new[] { (55f, 0f), (55f, 250f) }), (1, new[] { (195f, 0f), (195f, 250f) }), (2, new[] { (55f, 130f), (195f, 130f) })));
        AddTemplate("H", PtsMulti((0, new[] { (45f, 0f), (45f, 250f) }), (1, new[] { (205f, 0f), (205f, 250f) }), (2, new[] { (45f, 120f), (205f, 120f) })));

        AddTemplate("I", Pts(0, (125f, 0f), (125f, 250f)));
        AddTemplate("I", Pts(0, (120f, 0f), (120f, 250f)));
        AddTemplate("I", Pts(0, (130f, 0f), (130f, 250f)));

        AddTemplate("J", Pts(0, (180f, 0f), (180f, 200f), (150f, 240f), (100f, 250f), (60f, 235f), (45f, 200f), (50f, 170f)));
        AddTemplate("J", Pts(0, (175f, 0f), (175f, 195f), (145f, 238f), (95f, 248f), (55f, 232f), (40f, 195f), (48f, 165f)));
        AddTemplate("J", Pts(0, (185f, 0f), (185f, 205f), (155f, 242f), (105f, 252f), (65f, 238f), (50f, 205f), (55f, 175f)));

        AddTemplate("K", PtsMulti((0, new[] { (50f, 0f), (50f, 250f) }), (1, new[] { (200f, 0f), (50f, 125f), (200f, 250f) })));
        AddTemplate("K", PtsMulti((0, new[] { (55f, 0f), (55f, 250f) }), (1, new[] { (195f, 0f), (55f, 130f), (195f, 250f) })));
        AddTemplate("K", PtsMulti((0, new[] { (45f, 0f), (45f, 250f) }), (1, new[] { (205f, 0f), (45f, 120f), (205f, 250f) })));

        AddTemplate("L", Pts(0, (50f, 0f), (50f, 250f), (200f, 250f)));
        AddTemplate("L", Pts(0, (55f, 0f), (55f, 250f), (195f, 250f)));
        AddTemplate("L", Pts(0, (45f, 0f), (45f, 250f), (205f, 250f)));

        AddTemplate("M", Pts(0, (30f, 250f), (30f, 0f), (125f, 125f), (220f, 0f), (220f, 250f)));
        AddTemplate("M", Pts(0, (35f, 250f), (35f, 0f), (120f, 120f), (215f, 0f), (215f, 250f)));
        AddTemplate("M", Pts(0, (25f, 250f), (25f, 0f), (130f, 130f), (225f, 0f), (225f, 250f)));

        AddTemplate("N", Pts(0, (30f, 250f), (30f, 0f), (220f, 250f), (220f, 0f)));
        AddTemplate("N", Pts(0, (35f, 250f), (35f, 0f), (215f, 250f), (215f, 0f)));
        AddTemplate("N", Pts(0, (25f, 250f), (25f, 0f), (225f, 250f), (225f, 0f)));

        AddTemplate("O", Pts(0, (125f, 0f), (50f, 30f), (10f, 100f), (10f, 150f), (50f, 220f), (125f, 250f), (200f, 220f), (240f, 150f), (240f, 100f), (200f, 30f), (125f, 0f)));
        AddTemplate("O", Pts(0, (125f, 5f), (55f, 35f), (15f, 105f), (15f, 145f), (55f, 215f), (125f, 245f), (195f, 215f), (235f, 145f), (235f, 105f), (195f, 35f), (125f, 5f)));
        AddTemplate("O", Pts(0, (125f, 8f), (48f, 28f), (8f, 98f), (8f, 155f), (48f, 225f), (125f, 252f), (202f, 225f), (242f, 155f), (242f, 98f), (202f, 28f), (125f, 8f)));

        AddTemplate("P", PtsMulti((0, new[] { (50f, 250f), (50f, 0f) }), (1, new[] { (50f, 0f), (175f, 30f), (190f, 80f), (175f, 130f), (50f, 125f) })));
        AddTemplate("P", PtsMulti((0, new[] { (55f, 250f), (55f, 0f) }), (1, new[] { (55f, 0f), (170f, 35f), (185f, 85f), (170f, 135f), (55f, 130f) })));
        AddTemplate("P", PtsMulti((0, new[] { (45f, 250f), (45f, 0f) }), (1, new[] { (45f, 0f), (180f, 25f), (195f, 75f), (180f, 125f), (45f, 120f) })));

        AddTemplate("Q", Pts(0, (125f, 0f), (50f, 30f), (10f, 100f), (10f, 150f), (50f, 220f), (125f, 250f), (200f, 220f), (240f, 150f), (240f, 100f), (200f, 30f), (125f, 0f), (170f, 200f), (220f, 250f)));
        AddTemplate("Q", Pts(0, (125f, 5f), (55f, 35f), (15f, 105f), (15f, 145f), (55f, 215f), (125f, 245f), (195f, 215f), (235f, 145f), (235f, 105f), (195f, 35f), (125f, 5f), (165f, 205f), (215f, 255f)));
        AddTemplate("Q", Pts(0, (125f, 2f), (52f, 32f), (12f, 102f), (12f, 148f), (52f, 218f), (125f, 248f), (198f, 218f), (238f, 148f), (238f, 102f), (198f, 32f), (125f, 2f), (168f, 202f), (218f, 252f)));

        AddTemplate("R", PtsMulti((0, new[] { (50f, 250f), (50f, 0f) }), (1, new[] { (50f, 0f), (175f, 30f), (190f, 80f), (175f, 130f), (50f, 125f), (200f, 250f) })));
        AddTemplate("R", PtsMulti((0, new[] { (55f, 250f), (55f, 0f) }), (1, new[] { (55f, 0f), (170f, 35f), (185f, 85f), (170f, 135f), (55f, 130f), (195f, 250f) })));
        AddTemplate("R", PtsMulti((0, new[] { (45f, 250f), (45f, 0f) }), (1, new[] { (45f, 0f), (180f, 25f), (195f, 75f), (180f, 125f), (45f, 120f), (205f, 250f) })));

        AddTemplate("S", Pts(0, (210f, 30f), (180f, 5f), (120f, 0f), (60f, 15f), (35f, 55f), (55f, 100f), (125f, 125f), (195f, 150f), (215f, 195f), (190f, 235f), (130f, 250f), (70f, 245f), (40f, 220f)));
        AddTemplate("S", Pts(0, (205f, 35f), (175f, 8f), (115f, 3f), (55f, 18f), (30f, 58f), (50f, 103f), (120f, 128f), (190f, 153f), (210f, 198f), (185f, 238f), (125f, 248f), (65f, 243f), (35f, 215f)));
        AddTemplate("S", Pts(0, (215f, 25f), (185f, 2f), (125f, 0f), (65f, 12f), (40f, 52f), (60f, 97f), (130f, 122f), (200f, 147f), (220f, 192f), (195f, 232f), (135f, 252f), (75f, 247f), (45f, 225f)));

        AddTemplate("T", PtsMulti((0, new[] { (30f, 0f), (220f, 0f) }), (1, new[] { (125f, 0f), (125f, 250f) })));
        AddTemplate("T", PtsMulti((0, new[] { (35f, 0f), (215f, 0f) }), (1, new[] { (125f, 0f), (125f, 250f) })));
        AddTemplate("T", PtsMulti((0, new[] { (25f, 0f), (225f, 0f) }), (1, new[] { (125f, 0f), (125f, 250f) })));

        AddTemplate("U", Pts(0, (50f, 0f), (50f, 200f), (80f, 240f), (125f, 250f), (170f, 240f), (200f, 200f), (200f, 0f)));
        AddTemplate("U", Pts(0, (55f, 0f), (55f, 195f), (85f, 238f), (125f, 248f), (165f, 238f), (195f, 195f), (195f, 0f)));
        AddTemplate("U", Pts(0, (45f, 0f), (45f, 205f), (75f, 242f), (125f, 252f), (175f, 242f), (205f, 205f), (205f, 0f)));

        AddTemplate("V", Pts(0, (30f, 0f), (125f, 250f), (220f, 0f)));
        AddTemplate("V", Pts(0, (35f, 0f), (125f, 245f), (215f, 0f)));
        AddTemplate("V", Pts(0, (25f, 0f), (125f, 255f), (225f, 0f)));

        AddTemplate("W", Pts(0, (20f, 0f), (70f, 250f), (125f, 150f), (180f, 250f), (230f, 0f)));
        AddTemplate("W", Pts(0, (25f, 0f), (75f, 245f), (125f, 145f), (175f, 245f), (225f, 0f)));
        AddTemplate("W", Pts(0, (15f, 0f), (65f, 255f), (125f, 155f), (185f, 255f), (235f, 0f)));

        AddTemplate("X", PtsMulti((0, new[] { (30f, 0f), (220f, 250f) }), (1, new[] { (220f, 0f), (30f, 250f) })));
        AddTemplate("X", PtsMulti((0, new[] { (35f, 0f), (215f, 250f) }), (1, new[] { (215f, 0f), (35f, 250f) })));
        AddTemplate("X", PtsMulti((0, new[] { (25f, 0f), (225f, 250f) }), (1, new[] { (225f, 0f), (25f, 250f) })));

        AddTemplate("Y", PtsMulti((0, new[] { (30f, 0f), (125f, 130f), (220f, 0f) }), (1, new[] { (125f, 130f), (125f, 250f) })));
        AddTemplate("Y", PtsMulti((0, new[] { (35f, 0f), (125f, 125f), (215f, 0f) }), (1, new[] { (125f, 125f), (125f, 250f) })));
        AddTemplate("Y", PtsMulti((0, new[] { (25f, 0f), (125f, 135f), (225f, 0f) }), (1, new[] { (125f, 135f), (125f, 250f) })));

        AddTemplate("Z", Pts(0, (30f, 0f), (220f, 0f), (30f, 250f), (220f, 250f)));
        AddTemplate("Z", Pts(0, (35f, 0f), (215f, 0f), (35f, 250f), (215f, 250f)));
        AddTemplate("Z", Pts(0, (25f, 0f), (225f, 0f), (25f, 250f), (225f, 250f)));
    }

    static RecognitionResult UnknownResult()
    {
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
    }

    static float GreedyCloudMatch(List<Point> points, List<Point> template)
    {
        int n = points.Count;
        int step = Math.Max(1, (int)MathF.Floor(MathF.Pow(n, 0.5f)));
        float min = float.MaxValue;

        for (int i = 0; i < n; i += step)
        {
            float d1 = CloudDistance(points, template, i);
            float d2 = CloudDistance(template, points, i);
            min = Math.Min(min, Math.Min(d1, d2));
        }

        return min;
    }

    static float CloudDistance(List<Point> pts1, List<Point> pts2, int start)
    {
        int n = pts1.Count;
        var matched = new bool[n];
        float sum = 0f;
        int i = start;

        do
        {
            int index = -1;
            float minDist = float.MaxValue;

            for (int j = 0; j < n; j++)
            {
                if (matched[j])
                    continue;

                float distance = Distance(pts1[i], pts2[j]);
                if (distance < minDist)
                {
                    minDist = distance;
                    index = j;
                }
            }

            if (index == -1)
                break;

            matched[index] = true;
            float weight = 1f - ((i - start + n) % n) / (float)n;
            sum += weight * minDist;
            i = (i + 1) % n;
        } while (i != start);

        return sum;
    }

    static List<Point> Normalize(List<Point> points)
    {
        List<Point> pts = Resample(points, SAMPLING_RESOLUTION);
        pts = Scale(pts);
        pts = TranslateToOrigin(pts);
        return pts;
    }

    static float ComputeStructuralPenalty(ShapeDescriptor candidate, Gesture template)
    {
        ShapeDescriptor templateDescriptor = template.descriptor;
        float penalty = 0f;
        penalty += MathF.Abs(candidate.aspectRatio - templateDescriptor.aspectRatio) * ASPECT_RATIO_WEIGHT;
        penalty += MathF.Abs(candidate.openness - templateDescriptor.openness) * OPENNESS_WEIGHT;
        penalty += (MathF.Abs(candidate.centerX - templateDescriptor.centerX) +
                    MathF.Abs(candidate.centerY - templateDescriptor.centerY)) * CENTER_BALANCE_WEIGHT;
        penalty += ComputeGridDistance(candidate.occupancy, templateDescriptor.occupancy) * GRID_SHAPE_WEIGHT;
        return penalty;
    }

    static List<Point> Resample(List<Point> points, int n)
    {
        if (points.Count == 0)
            return points;

        float interval = PathLength(points) / (n - 1);
        if (interval <= 0f)
            return points;

        float distanceSoFar = 0f;
        var newPoints = new List<Point> { points[0] };

        for (int i = 1; i < points.Count; i++)
        {
            if (points[i].id != points[i - 1].id)
                continue;

            float segmentLength = Distance(points[i - 1], points[i]);
            if (segmentLength <= 0f)
                continue;

            if ((distanceSoFar + segmentLength) >= interval)
            {
                float t = (interval - distanceSoFar) / segmentLength;
                float qx = points[i - 1].x + t * (points[i].x - points[i - 1].x);
                float qy = points[i - 1].y + t * (points[i].y - points[i - 1].y);
                var sample = new Point(qx, qy, points[i].id);
                newPoints.Add(sample);
                points.Insert(i, sample);
                distanceSoFar = 0f;
            }
            else
            {
                distanceSoFar += segmentLength;
            }
        }

        while (newPoints.Count < n)
            newPoints.Add(points[^1]);

        if (newPoints.Count > n)
            newPoints.RemoveRange(n, newPoints.Count - n);

        return newPoints;
    }

    static List<Point> Scale(List<Point> points)
    {
        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minY = float.MaxValue;
        float maxY = float.MinValue;

        foreach (Point point in points)
        {
            minX = Math.Min(minX, point.x);
            maxX = Math.Max(maxX, point.x);
            minY = Math.Min(minY, point.y);
            maxY = Math.Max(maxY, point.y);
        }

        float size = Math.Max(maxX - minX, maxY - minY);
        if (size < 1e-6f)
            return points;

        var scaled = new List<Point>(points.Count);
        foreach (Point point in points)
        {
            scaled.Add(new Point(
                (point.x - minX) / size * SQUARE_SIZE,
                (point.y - minY) / size * SQUARE_SIZE,
                point.id));
        }

        return scaled;
    }

    static List<Point> TranslateToOrigin(List<Point> points)
    {
        Point centroid = Centroid(points);
        var translated = new List<Point>(points.Count);

        foreach (Point point in points)
            translated.Add(new Point(point.x - centroid.x, point.y - centroid.y, point.id));

        return translated;
    }

    static float PathLength(List<Point> points)
    {
        float total = 0f;

        for (int i = 1; i < points.Count; i++)
        {
            if (points[i].id == points[i - 1].id)
                total += Distance(points[i - 1], points[i]);
        }

        return total;
    }

    static float Distance(Point a, Point b)
    {
        float dx = a.x - b.x;
        float dy = a.y - b.y;
        return MathF.Sqrt((dx * dx) + (dy * dy));
    }

    static Point Centroid(List<Point> points)
    {
        float x = 0f;
        float y = 0f;
        foreach (Point point in points)
        {
            x += point.x;
            y += point.y;
        }

        return new Point(x / points.Count, y / points.Count, 0);
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

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minY = float.MaxValue;
        float maxY = float.MinValue;
        float sumX = 0f;
        float sumY = 0f;

        foreach (Point point in points)
        {
            minX = Math.Min(minX, point.x);
            maxX = Math.Max(maxX, point.x);
            minY = Math.Min(minY, point.y);
            maxY = Math.Max(maxY, point.y);
            sumX += point.x;
            sumY += point.y;
        }

        float width = Math.Max(1f, maxX - minX);
        float height = Math.Max(1f, maxY - minY);
        float diagonal = MathF.Sqrt((width * width) + (height * height));

        descriptor.aspectRatio = width / height;
        descriptor.openness = points.Count > 1
            ? Distance(points[0], points[^1]) / Math.Max(1f, diagonal)
            : 0f;
        descriptor.centerX = Clamp01(((sumX / points.Count) - minX) / width);
        descriptor.centerY = Clamp01(((sumY / points.Count) - minY) / height);

        for (int i = 0; i < points.Count; i++)
            AccumulateGridOccupancy(descriptor.occupancy, points[i], minX, minY, width, height);

        for (int i = 1; i < points.Count; i++)
        {
            if (points[i].id != points[i - 1].id)
                continue;

            float segmentLength = Distance(points[i - 1], points[i]);
            int steps = Math.Max(2, (int)MathF.Ceiling(segmentLength / 12f));
            for (int step = 1; step <= steps; step++)
            {
                float t = step / (float)steps;
                var sample = new Point(
                    Lerp(points[i - 1].x, points[i].x, t),
                    Lerp(points[i - 1].y, points[i].y, t),
                    points[i].id);
                AccumulateGridOccupancy(descriptor.occupancy, sample, minX, minY, width, height);
            }
        }

        float maxValue = 0f;
        foreach (float cell in descriptor.occupancy)
            maxValue = Math.Max(maxValue, cell);

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

        float nx = Clamp01((point.x - minX) / width);
        float ny = Clamp01((point.y - minY) / height);
        int gx = Math.Clamp((int)MathF.Floor(nx * SHAPE_GRID_SIZE), 0, SHAPE_GRID_SIZE - 1);
        int gy = Math.Clamp((int)MathF.Floor(ny * SHAPE_GRID_SIZE), 0, SHAPE_GRID_SIZE - 1);
        occupancy[(gy * SHAPE_GRID_SIZE) + gx] += 1f;
    }

    static float ComputeGridDistance(float[] a, float[] b)
    {
        if (a == null || b == null || a.Length != b.Length)
            return 1f;

        float total = 0f;
        for (int i = 0; i < a.Length; i++)
            total += MathF.Abs(a[i] - b[i]);

        return total / a.Length;
    }

    static List<Point> Pts(int strokeId, params (float x, float y)[] coords)
    {
        var points = new List<Point>(coords.Length);
        foreach ((float x, float y) in coords)
            points.Add(new Point(x, y, strokeId));
        return points;
    }

    static List<Point> PtsMulti(params (int id, (float x, float y)[] coords)[] strokes)
    {
        var points = new List<Point>();
        foreach ((int id, (float x, float y)[] coords) stroke in strokes)
        {
            foreach ((float x, float y) in stroke.coords)
                points.Add(new Point(x, y, stroke.id));
        }

        return points;
    }

    static List<Point> ClonePoints(List<Point> points)
    {
        var clone = new List<Point>(points.Count);
        foreach (Point point in points)
            clone.Add(new Point(point.x, point.y, point.id));
        return clone;
    }

    static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);

    static float Lerp(float a, float b, float t) => a + ((b - a) * t);
}
