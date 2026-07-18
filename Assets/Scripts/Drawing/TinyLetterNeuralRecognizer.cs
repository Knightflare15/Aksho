using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lightweight image-style letter scorer that behaves like a tiny RBF neural recognizer.
/// It rasterizes strokes into a normalized black/white bitmap feature vector and compares
/// that image with per-letter prototype neurons loaded from Resources or generated from
/// the built-in alphabet templates.
/// </summary>
public sealed class TinyLetterNeuralRecognizer
{
    const int GridSize = 28;
    const int GridFeatureCount = GridSize * GridSize;
    const int ExtraFeatureCount = 10;
    const int FeatureCount = GridFeatureCount + ExtraFeatureCount;
    const float DefaultDistanceScale = 11f;
    const float BinaryThreshold = 0.32f;
    const string DefaultModelResourcePath = "TinyLetterNeuralRecognizer";

    readonly List<Prototype> prototypes = new List<Prototype>();
    float distanceScale = DefaultDistanceScale;

    public bool IsAvailable => prototypes.Count > 0;
    public string ModelName { get; private set; } = "Built-in prototype RBF letter model";

#pragma warning disable 0649
    [Serializable]
    sealed class ModelData
    {
        public string modelName;
        public float distanceScale = DefaultDistanceScale;
        public List<PrototypeData> prototypes = new List<PrototypeData>();
    }

    [Serializable]
    sealed class PrototypeData
    {
        public string label;
        public float[] features;
    }
#pragma warning restore 0649

    sealed class Prototype
    {
        public string label;
        public float[] features;
    }

    public struct NeuralResult
    {
        public string name;
        public float confidence;
        public string runnerUpName;
        public float runnerUpConfidence;
        public bool isAmbiguous;
    }

    public sealed class LetterImageDebugCapture
    {
        public Texture2D grayscaleTexture;
        public Texture2D thresholdTexture;
        public int width;
        public int height;
    }

    public static TinyLetterNeuralRecognizer CreateDefault()
    {
        var recognizer = new TinyLetterNeuralRecognizer();
        if (!recognizer.TryLoadFromResources(DefaultModelResourcePath))
            recognizer.LoadBuiltInPrototypeModel();
        return recognizer;
    }

    public bool TryLoadFromResources(string resourcePath)
    {
        TextAsset asset = Resources.Load<TextAsset>(resourcePath);
        if (asset == null || string.IsNullOrWhiteSpace(asset.text))
            return false;

        try
        {
            ModelData model = JsonUtility.FromJson<ModelData>(asset.text);
            if (model?.prototypes == null || model.prototypes.Count == 0)
                return false;

            prototypes.Clear();
            foreach (PrototypeData prototype in model.prototypes)
            {
                string label = NormalizeLabel(prototype.label);
                if (string.IsNullOrEmpty(label) || prototype.features == null || prototype.features.Length != FeatureCount)
                    continue;

                prototypes.Add(new Prototype
                {
                    label = label,
                    features = CopyFeatures(prototype.features),
                });
            }

            if (prototypes.Count == 0)
                return false;

            distanceScale = model.distanceScale > 0f ? model.distanceScale : DefaultDistanceScale;
            ModelName = string.IsNullOrWhiteSpace(model.modelName) ? "Resource RBF letter model" : model.modelName;
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[TinyLetterNeuralRecognizer] Could not load model '{resourcePath}': {ex.Message}");
            prototypes.Clear();
            return false;
        }
    }

    public void LoadBuiltInPrototypeModel()
    {
        prototypes.Clear();
        foreach ((string name, List<PDollarRecognizer.Point> points) in PDollarRecognizer.CreateDefaultAlphabetTemplateSnapshot())
        {
            string label = NormalizeLabel(name);
            if (string.IsNullOrEmpty(label))
                continue;

            prototypes.Add(new Prototype
            {
                label = label,
                features = ExtractFeatures(points),
            });
        }

        distanceScale = DefaultDistanceScale;
        ModelName = "Built-in prototype RBF letter model";
    }

    public NeuralResult Recognize(List<PDollarRecognizer.Point> points)
    {
        return RecognizeImage(GroupPointsByStroke(points));
    }

    public NeuralResult RecognizeImage(List<List<Vector2>> strokes)
    {
        if (!IsAvailable || CountPoints(strokes) < 2)
            return EmptyResult();

        float[] candidate = ExtractFeatures(strokes);
        var bestDistanceByLabel = new Dictionary<string, float>();
        foreach (Prototype prototype in prototypes)
        {
            float distance = SquaredDistance(candidate, prototype.features);
            if (!bestDistanceByLabel.TryGetValue(prototype.label, out float existing) || distance < existing)
                bestDistanceByLabel[prototype.label] = distance;
        }

        string bestName = "Unknown";
        string runnerUpName = "Unknown";
        float bestDistance = float.MaxValue;
        float runnerUpDistance = float.MaxValue;
        foreach (KeyValuePair<string, float> pair in bestDistanceByLabel)
        {
            if (pair.Value < bestDistance)
            {
                runnerUpDistance = bestDistance;
                runnerUpName = bestName;
                bestDistance = pair.Value;
                bestName = pair.Key;
            }
            else if (pair.Value < runnerUpDistance)
            {
                runnerUpDistance = pair.Value;
                runnerUpName = pair.Key;
            }
        }

        float bestConfidence = ScoreImageConfidence(bestDistance, runnerUpDistance);
        float runnerUpConfidence = ScoreImageConfidence(runnerUpDistance, bestDistance);
        bool ambiguous = bestDistance < float.MaxValue &&
                         runnerUpDistance < float.MaxValue &&
                         runnerUpDistance - bestDistance < 0.018f;
        return new NeuralResult
        {
            name = bestName,
            confidence = bestConfidence,
            runnerUpName = runnerUpName,
            runnerUpConfidence = runnerUpConfidence,
            isAmbiguous = ambiguous,
        };
    }

    public LetterImageDebugCapture CapturePanelImagePreview(
        List<List<Vector2>> strokes,
        Rect panelRect,
        float strokeWidth,
        int previewSize = 128)
    {
        if (strokes == null || strokes.Count == 0)
            return null;

        int width = Mathf.Max(32, previewSize);
        int height = Mathf.Max(32, Mathf.RoundToInt(width * Mathf.Clamp(panelRect.height / Mathf.Max(1f, panelRect.width), 0.45f, 1.6f)));
        float[] ink = RenderInkToPanel(strokes, panelRect, strokeWidth, width, height);
        if (ink == null)
            return null;

        var grayscale = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var threshold = new Texture2D(width, height, TextureFormat.RGBA32, false);
        grayscale.wrapMode = TextureWrapMode.Clamp;
        threshold.wrapMode = TextureWrapMode.Clamp;
        grayscale.filterMode = FilterMode.Point;
        threshold.filterMode = FilterMode.Point;

        var grayPixels = new Color32[width * height];
        var thresholdPixels = new Color32[width * height];
        for (int i = 0; i < ink.Length; i++)
        {
            byte gray = (byte)Mathf.RoundToInt(255f * (1f - Mathf.Clamp01(ink[i])));
            grayPixels[i] = new Color32(gray, gray, gray, 255);
            thresholdPixels[i] = ink[i] >= BinaryThreshold
                ? new Color32(0, 0, 0, 255)
                : new Color32(255, 255, 255, 255);
        }

        grayscale.SetPixels32(grayPixels);
        grayscale.Apply(false, false);
        threshold.SetPixels32(thresholdPixels);
        threshold.Apply(false, false);

        return new LetterImageDebugCapture
        {
            grayscaleTexture = grayscale,
            thresholdTexture = threshold,
            width = width,
            height = height,
        };
    }

    static NeuralResult EmptyResult()
    {
        return new NeuralResult
        {
            name = "Unknown",
            confidence = 0f,
            runnerUpName = "Unknown",
            runnerUpConfidence = 0f,
            isAmbiguous = false,
        };
    }

    static float[] ExtractFeatures(List<PDollarRecognizer.Point> points)
    {
        return ExtractFeatures(GroupPointsByStroke(points));
    }

    static float[] ExtractFeatures(List<List<Vector2>> strokes)
    {
        var features = new float[FeatureCount];
        if (CountPoints(strokes) == 0)
            return features;

        Bounds2D bounds = MeasureBounds(strokes);
        float[] ink = RenderInkToNormalizedSquare(strokes, bounds, GridSize, 2.25f);
        for (int i = 0; i < GridFeatureCount; i++)
            features[i] = ink[i] >= BinaryThreshold ? 1f : ink[i];

        float pathLength = 0f;
        float directDistance = 0f;
        float sumX = 0f;
        float sumY = 0f;
        int pointCount = 0;
        int strokeCount = 0;
        Vector2 firstPoint = Vector2.zero;
        Vector2 lastPoint = Vector2.zero;
        bool hasFirst = false;

        foreach (List<Vector2> stroke in strokes)
        {
            if (stroke == null || stroke.Count == 0)
                continue;

            strokeCount++;
            for (int i = 0; i < stroke.Count; i++)
            {
                Vector2 point = stroke[i];
                if (!hasFirst)
                {
                    firstPoint = point;
                    hasFirst = true;
                }

                lastPoint = point;
                sumX += Mathf.InverseLerp(bounds.minX, bounds.maxX, point.x);
                sumY += Mathf.InverseLerp(bounds.minY, bounds.maxY, point.y);
                pointCount++;
                if (i > 0)
                    pathLength += Vector2.Distance(point, stroke[i - 1]);
            }
        }

        float width = Mathf.Max(1f, bounds.maxX - bounds.minX);
        float height = Mathf.Max(1f, bounds.maxY - bounds.minY);
        float maxSide = Mathf.Max(width, height);
        if (hasFirst)
            directDistance = Vector2.Distance(firstPoint, lastPoint);

        int offset = GridFeatureCount;
        features[offset++] = Mathf.Clamp01(strokeCount / 4f);
        features[offset++] = Mathf.Clamp01(width / height / 3f);
        features[offset++] = Mathf.Clamp01(height / width / 3f);
        features[offset++] = Mathf.Clamp01(pathLength / Mathf.Max(1f, maxSide * 5f));
        features[offset++] = pathLength > 0f ? Mathf.Clamp01(directDistance / pathLength) : 0f;
        features[offset++] = Mathf.Clamp01(pointCount / 128f);
        features[offset++] = Mathf.Clamp01(sumX / Mathf.Max(1, pointCount));
        features[offset++] = Mathf.Clamp01(sumY / Mathf.Max(1, pointCount));
        features[offset++] = hasFirst ? Mathf.Clamp01((firstPoint.x - bounds.minX) / width) : 0f;
        features[offset] = hasFirst ? Mathf.Clamp01((firstPoint.y - bounds.minY) / height) : 0f;

        return features;
    }

    static List<List<Vector2>> GroupPointsByStroke(List<PDollarRecognizer.Point> points)
    {
        var strokes = new List<List<Vector2>>();
        if (points == null)
            return strokes;

        var byId = new Dictionary<int, List<Vector2>>();
        foreach (PDollarRecognizer.Point point in points)
        {
            if (!byId.TryGetValue(point.id, out List<Vector2> stroke))
            {
                stroke = new List<Vector2>();
                byId[point.id] = stroke;
            }

            stroke.Add(new Vector2(point.x, point.y));
        }

        var ids = new List<int>(byId.Keys);
        ids.Sort();
        foreach (int id in ids)
            strokes.Add(byId[id]);
        return strokes;
    }

    struct Bounds2D
    {
        public float minX;
        public float maxX;
        public float minY;
        public float maxY;
    }

    static Bounds2D MeasureBounds(List<List<Vector2>> strokes)
    {
        var bounds = new Bounds2D
        {
            minX = float.MaxValue,
            maxX = float.MinValue,
            minY = float.MaxValue,
            maxY = float.MinValue,
        };

        foreach (List<Vector2> stroke in strokes)
        {
            if (stroke == null)
                continue;

            foreach (Vector2 point in stroke)
            {
                if (point.x < bounds.minX) bounds.minX = point.x;
                if (point.x > bounds.maxX) bounds.maxX = point.x;
                if (point.y < bounds.minY) bounds.minY = point.y;
                if (point.y > bounds.maxY) bounds.maxY = point.y;
            }
        }

        if (bounds.minX == float.MaxValue)
            return new Bounds2D { minX = 0f, maxX = 1f, minY = 0f, maxY = 1f };

        if (Mathf.Abs(bounds.maxX - bounds.minX) < 1f)
            bounds.maxX = bounds.minX + 1f;
        if (Mathf.Abs(bounds.maxY - bounds.minY) < 1f)
            bounds.maxY = bounds.minY + 1f;
        return bounds;
    }

    static float[] RenderInkToNormalizedSquare(
        List<List<Vector2>> strokes,
        Bounds2D bounds,
        int size,
        float brushRadius)
    {
        float[] ink = new float[size * size];
        float width = Mathf.Max(1f, bounds.maxX - bounds.minX);
        float height = Mathf.Max(1f, bounds.maxY - bounds.minY);
        float maxSide = Mathf.Max(width, height);
        float padding = 3.25f;
        float usable = Mathf.Max(1f, size - (padding * 2f) - 1f);

        foreach (List<Vector2> stroke in strokes)
        {
            if (stroke == null || stroke.Count == 0)
                continue;

            Vector2 previous = NormalizePoint(stroke[0]);
            StampInk(ink, size, size, previous.x, previous.y, brushRadius, 1f);
            for (int i = 1; i < stroke.Count; i++)
            {
                Vector2 current = NormalizePoint(stroke[i]);
                DrawInkLine(ink, size, size, previous, current, brushRadius);
                previous = current;
            }
        }

        return ink;

        Vector2 NormalizePoint(Vector2 point)
        {
            float nx = ((point.x - bounds.minX) / maxSide) + ((maxSide - width) / maxSide) * 0.5f;
            float ny = ((point.y - bounds.minY) / maxSide) + ((maxSide - height) / maxSide) * 0.5f;
            return new Vector2(padding + nx * usable, padding + ny * usable);
        }
    }

    static float[] RenderInkToPanel(
        List<List<Vector2>> strokes,
        Rect panelRect,
        float strokeWidth,
        int width,
        int height)
    {
        if (panelRect.width <= 1f || panelRect.height <= 1f)
            return null;

        float[] ink = new float[width * height];
        float sx = (width - 1f) / panelRect.width;
        float sy = (height - 1f) / panelRect.height;
        float radius = Mathf.Clamp(strokeWidth * Mathf.Min(sx, sy) * 0.5f, 1.4f, 10f);

        foreach (List<Vector2> stroke in strokes)
        {
            if (stroke == null || stroke.Count == 0)
                continue;

            Vector2 previous = ToPixel(stroke[0]);
            StampInk(ink, width, height, previous.x, previous.y, radius, 1f);
            for (int i = 1; i < stroke.Count; i++)
            {
                Vector2 current = ToPixel(stroke[i]);
                DrawInkLine(ink, width, height, previous, current, radius);
                previous = current;
            }
        }

        return ink;

        Vector2 ToPixel(Vector2 point)
        {
            float x = (point.x - panelRect.xMin) * sx;
            float y = (point.y - panelRect.yMin) * sy;
            return new Vector2(x, y);
        }
    }

    static void DrawInkLine(float[] ink, int width, int height, Vector2 from, Vector2 to, float radius)
    {
        float distance = Vector2.Distance(from, to);
        int steps = Mathf.Max(1, Mathf.CeilToInt(distance * 1.5f));
        for (int i = 0; i <= steps; i++)
        {
            Vector2 point = Vector2.Lerp(from, to, i / (float)steps);
            StampInk(ink, width, height, point.x, point.y, radius, 1f);
        }
    }

    static void StampInk(float[] ink, int width, int height, float cx, float cy, float radius, float strength)
    {
        int minX = Mathf.Clamp(Mathf.FloorToInt(cx - radius - 1f), 0, width - 1);
        int maxX = Mathf.Clamp(Mathf.CeilToInt(cx + radius + 1f), 0, width - 1);
        int minY = Mathf.Clamp(Mathf.FloorToInt(cy - radius - 1f), 0, height - 1);
        int maxY = Mathf.Clamp(Mathf.CeilToInt(cy + radius + 1f), 0, height - 1);
        float safeRadius = Mathf.Max(0.001f, radius);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                float coverage = Mathf.Clamp01(1f - ((distance - safeRadius + 0.75f) / 1.5f)) * strength;
                int index = y * width + x;
                if (coverage > ink[index])
                    ink[index] = coverage;
            }
        }
    }

    static int CountPoints(List<List<Vector2>> strokes)
    {
        int count = 0;
        if (strokes == null)
            return 0;

        foreach (List<Vector2> stroke in strokes)
            if (stroke != null)
                count += stroke.Count;
        return count;
    }

    static float SquaredDistance(float[] a, float[] b)
    {
        if (a == null || b == null)
            return 1f;

        int count = Mathf.Min(a.Length, b.Length);
        float sum = 0f;
        for (int i = 0; i < count; i++)
        {
            float delta = a[i] - b[i];
            sum += delta * delta;
        }
        return sum / Mathf.Max(1, count);
    }

    float ScoreImageConfidence(float distance, float comparisonDistance)
    {
        if (float.IsNaN(distance) || float.IsInfinity(distance) || distance >= float.MaxValue * 0.5f)
            return 0f;

        float absoluteFit = Mathf.Exp(-distance * distanceScale);
        if (float.IsNaN(comparisonDistance) ||
            float.IsInfinity(comparisonDistance) ||
            comparisonDistance >= float.MaxValue * 0.5f)
            return Mathf.Clamp01(absoluteFit);

        float margin = comparisonDistance - distance;
        float marginFit = Mathf.Clamp01(0.45f + margin * 18f);
        return Mathf.Clamp01(absoluteFit * 0.35f + marginFit * 0.65f);
    }

    static float Distance(PDollarRecognizer.Point a, PDollarRecognizer.Point b)
    {
        float dx = a.x - b.x;
        float dy = a.y - b.y;
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    static string NormalizeLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return "";

        foreach (char c in label)
            if (char.IsLetter(c))
                return char.ToUpperInvariant(c).ToString();
        return "";
    }

    static float[] CopyFeatures(float[] source)
    {
        var copy = new float[source.Length];
        Array.Copy(source, copy, source.Length);
        return copy;
    }
}
