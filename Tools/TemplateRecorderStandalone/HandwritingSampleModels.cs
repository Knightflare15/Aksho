using System.Diagnostics;

namespace TemplateRecorderStandalone;

public sealed class HandwritingCapturePoint
{
    public float x;
    public float y;
    public float nx;
    public float ny;
    public float canvasX;
    public float canvasY;
    public long tMs;
    public long deltaMs;
    public float pressure = -1f;
    public float altitudeAngle = -1f;
    public float azimuthAngle = -1f;
    public int pointerId = -1;
    public int strokeId;
    public int order;
    public string inputType = "mouse";
}

public sealed class HandwritingCaptureDevice
{
    public string platform = "Windows";
    public string operatingSystem = Environment.OSVersion.VersionString;
    public string deviceModel = "";
    public string deviceType = "Desktop";
    public int screenWidth;
    public int screenHeight;
    public float screenDpi;
    public float canvasWidth;
    public float canvasHeight;
    public bool pressureObserved;
    public bool tiltObserved;
}

public sealed class HandwritingSampleRecord
{
    public const string CurrentSchemaVersion = "handwriting_sample_v1";

    public string schemaVersion = CurrentSchemaVersion;
    public string sampleId = "";
    public string writerId = "";
    public string writerAgeBand = "";
    public string handedness = "unknown";
    public string collectionCohort = "";
    public string consentReference = "";
    public string sessionId = "";
    public string expectedLetter = "";
    public string labelStatus = "unreviewed";
    public string humanReviewStatus = "unreviewed";
    public string reviewedLetter = "";
    public List<string> qualityLabels = new();
    public string source = "standalone_template_recorder";
    public string rawCoordinateSystem = "canvas_top_left_y_down";
    public string normalizedCoordinateSystem = "unit_square_bottom_left_y_up";
    public string captureStartedAtUtc = "";
    public string capturedAtUtc = "";
    public long durationMs;
    public int strokeCount;
    public int pointCount;
    public string targetWord = "";
    public int letterIndex = -1;
    public int attemptNumber = 1;
    public bool guideVisible;
    public bool tracing;
    public bool inputAssisted;
    public float inputAssistFraction;
    public float inputAssistMeanDistance;
    public float inputAssistMaxDistance;
    public string assessmentOutcome = "";
    public string appVersion = "";
    public HandwritingCaptureDevice device = new();
    public List<HandwritingCapturePoint> points = new();
}

public sealed class HandwritingCapturedSeed
{
    public PointF position;
    public long tMs;
}

public sealed class HandwritingCaptureClock
{
    readonly long startedTimestamp = Stopwatch.GetTimestamp();
    public DateTime StartedAtUtc { get; } = DateTime.UtcNow;

    public long ElapsedMilliseconds =>
        Math.Max(0L, (long)Math.Round((Stopwatch.GetTimestamp() - startedTimestamp) * 1000d / Stopwatch.Frequency));
}

public static class HandwritingSampleFactory
{
    public static HandwritingSampleRecord Build(
        string expectedLetter,
        string writerId,
        string sessionId,
        IReadOnlyList<IReadOnlyList<HandwritingCapturedSeed>> strokes,
        Size canvasSize,
        DateTime startedAtUtc)
    {
        var sample = new HandwritingSampleRecord
        {
            sampleId = Guid.NewGuid().ToString("N"),
            writerId = string.IsNullOrWhiteSpace(writerId) ? "anonymous" : writerId.Trim(),
            sessionId = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString("N") : sessionId.Trim(),
            expectedLetter = expectedLetter?.Trim() ?? "",
            labelStatus = "author_accepted_template",
            captureStartedAtUtc = startedAtUtc.ToString("o"),
            capturedAtUtc = DateTime.UtcNow.ToString("o"),
            appVersion = typeof(HandwritingSampleFactory).Assembly.GetName().Version?.ToString() ?? "",
            device = new HandwritingCaptureDevice
            {
                screenWidth = Screen.PrimaryScreen?.Bounds.Width ?? 0,
                screenHeight = Screen.PrimaryScreen?.Bounds.Height ?? 0,
                canvasWidth = canvasSize.Width,
                canvasHeight = canvasSize.Height
            }
        };

        List<HandwritingCapturedSeed> all = strokes.SelectMany(stroke => stroke).ToList();
        if (all.Count == 0)
            return sample;
        float minX = all.Min(point => point.position.X);
        float maxX = all.Max(point => point.position.X);
        float minY = all.Min(point => point.position.Y);
        float maxY = all.Max(point => point.position.Y);
        float width = Math.Max(0.0001f, maxX - minX);
        float height = Math.Max(0.0001f, maxY - minY);
        int order = 0;
        long previousMs = 0;
        for (int strokeId = 0; strokeId < strokes.Count; strokeId++)
        {
            IReadOnlyList<HandwritingCapturedSeed> stroke = strokes[strokeId];
            if (stroke.Count == 0)
                continue;
            sample.strokeCount++;
            foreach (HandwritingCapturedSeed seed in stroke)
            {
                sample.points.Add(new HandwritingCapturePoint
                {
                    x = seed.position.X,
                    y = seed.position.Y,
                    nx = Math.Clamp((seed.position.X - minX) / width, 0f, 1f),
                    ny = 1f - Math.Clamp((seed.position.Y - minY) / height, 0f, 1f),
                    canvasX = Math.Clamp(seed.position.X / Math.Max(1f, canvasSize.Width), 0f, 1f),
                    canvasY = 1f - Math.Clamp(seed.position.Y / Math.Max(1f, canvasSize.Height), 0f, 1f),
                    tMs = seed.tMs,
                    deltaMs = Math.Max(0L, seed.tMs - previousMs),
                    strokeId = strokeId,
                    order = order++
                });
                previousMs = seed.tMs;
            }
        }
        sample.pointCount = sample.points.Count;
        sample.durationMs = sample.points[^1].tMs;
        return sample;
    }
}
