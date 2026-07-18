using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
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
    public string inputType = "unknown";
}

[Serializable]
public sealed class HandwritingCaptureDevice
{
    public string platform = "";
    public string operatingSystem = "";
    public string deviceModel = "";
    public string deviceType = "";
    public int screenWidth;
    public int screenHeight;
    public float screenDpi;
    public float canvasWidth;
    public float canvasHeight;
    public bool pressureObserved;
    public bool tiltObserved;
}

[Serializable]
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
    public List<string> qualityLabels = new List<string>();
    public string source = "";
    public string rawCoordinateSystem = "canvas_center_y_up";
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
    public HandwritingCaptureDevice device = new HandwritingCaptureDevice();
    public List<HandwritingCapturePoint> points = new List<HandwritingCapturePoint>();
}

public sealed class HandwritingCaptureSession
{
    struct Seed
    {
        public Vector2 position;
        public long tMs;
        public float pressure;
        public float altitudeAngle;
        public float azimuthAngle;
        public int pointerId;
        public string inputType;
    }

    readonly List<List<Seed>> strokes = new List<List<Seed>>();
    DateTime startedAtUtc;
    double startedAtRealtime;
    int activeStrokeIndex = -1;

    public HandwritingCaptureSession()
    {
        Reset();
    }

    public void Reset()
    {
        strokes.Clear();
        startedAtUtc = DateTime.UtcNow;
        startedAtRealtime = Time.realtimeSinceStartupAsDouble;
        activeStrokeIndex = -1;
    }

    public void BeginStroke()
    {
        strokes.Add(new List<Seed>());
        activeStrokeIndex = strokes.Count - 1;
    }

    public void AddPoint(Vector2 position)
    {
        if (activeStrokeIndex < 0)
            BeginStroke();

        CapturePointer(out string inputType, out int pointerId, out float pressure, out float altitude, out float azimuth);
        long elapsed = Math.Max(0L, (long)Math.Round((Time.realtimeSinceStartupAsDouble - startedAtRealtime) * 1000d));
        strokes[activeStrokeIndex].Add(new Seed
        {
            position = position,
            tMs = elapsed,
            pressure = pressure,
            altitudeAngle = altitude,
            azimuthAngle = azimuth,
            pointerId = pointerId,
            inputType = inputType
        });
    }

    public HandwritingSampleRecord Build(
        string expectedLetter,
        Rect canvasRect,
        string writerId,
        string sessionId,
        string source)
    {
        var sample = new HandwritingSampleRecord
        {
            sampleId = Guid.NewGuid().ToString("N"),
            writerId = string.IsNullOrWhiteSpace(writerId) ? "anonymous" : writerId.Trim(),
            sessionId = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString("N") : sessionId.Trim(),
            expectedLetter = string.IsNullOrWhiteSpace(expectedLetter) ? "" : expectedLetter.Trim(),
            source = source ?? "",
            captureStartedAtUtc = startedAtUtc.ToString("o"),
            capturedAtUtc = DateTime.UtcNow.ToString("o"),
            appVersion = Application.version ?? "",
            device = BuildDevice(canvasRect)
        };

        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (List<Seed> stroke in strokes)
            foreach (Seed seed in stroke)
            {
                minX = Mathf.Min(minX, seed.position.x);
                maxX = Mathf.Max(maxX, seed.position.x);
                minY = Mathf.Min(minY, seed.position.y);
                maxY = Mathf.Max(maxY, seed.position.y);
            }

        float width = Mathf.Max(0.0001f, maxX - minX);
        float height = Mathf.Max(0.0001f, maxY - minY);
        int order = 0;
        long previousMs = 0L;
        for (int strokeId = 0; strokeId < strokes.Count; strokeId++)
        {
            List<Seed> stroke = strokes[strokeId];
            if (stroke.Count == 0)
                continue;
            sample.strokeCount++;
            foreach (Seed seed in stroke)
            {
                sample.points.Add(new HandwritingCapturePoint
                {
                    x = seed.position.x,
                    y = seed.position.y,
                    nx = Mathf.Clamp01((seed.position.x - minX) / width),
                    ny = Mathf.Clamp01((seed.position.y - minY) / height),
                    canvasX = Mathf.InverseLerp(canvasRect.xMin, canvasRect.xMax, seed.position.x),
                    canvasY = Mathf.InverseLerp(canvasRect.yMin, canvasRect.yMax, seed.position.y),
                    tMs = seed.tMs,
                    deltaMs = Math.Max(0L, seed.tMs - previousMs),
                    pressure = seed.pressure,
                    altitudeAngle = seed.altitudeAngle,
                    azimuthAngle = seed.azimuthAngle,
                    pointerId = seed.pointerId,
                    strokeId = strokeId,
                    order = order++,
                    inputType = seed.inputType
                });
                previousMs = seed.tMs;
                if (seed.pressure >= 0f) sample.device.pressureObserved = true;
                if (seed.altitudeAngle >= 0f || seed.azimuthAngle >= 0f) sample.device.tiltObserved = true;
            }
        }
        sample.pointCount = sample.points.Count;
        sample.durationMs = sample.points.Count > 0 ? sample.points[^1].tMs : 0L;
        return sample;
    }

    static HandwritingCaptureDevice BuildDevice(Rect canvasRect)
    {
        return new HandwritingCaptureDevice
        {
            platform = Application.platform.ToString(),
            operatingSystem = SystemInfo.operatingSystem ?? "",
            deviceModel = SystemInfo.deviceModel ?? "",
            deviceType = SystemInfo.deviceType.ToString(),
            screenWidth = Screen.width,
            screenHeight = Screen.height,
            screenDpi = Screen.dpi,
            canvasWidth = canvasRect.width,
            canvasHeight = canvasRect.height
        };
    }

    static void CapturePointer(
        out string inputType,
        out int pointerId,
        out float pressure,
        out float altitude,
        out float azimuth)
    {
        inputType = "mouse";
        pointerId = -1;
        pressure = -1f;
        altitude = -1f;
        azimuth = -1f;
        if (Input.touchCount <= 0)
            return;

        Touch touch = Input.GetTouch(0);
        inputType = touch.type == TouchType.Stylus ? "stylus" : "touch";
        pointerId = touch.fingerId;
        if (touch.maximumPossiblePressure > 0f)
            pressure = Mathf.Clamp01(touch.pressure / touch.maximumPossiblePressure);
        if (touch.type == TouchType.Stylus)
        {
            altitude = touch.altitudeAngle;
            azimuth = touch.azimuthAngle;
        }
    }
}
