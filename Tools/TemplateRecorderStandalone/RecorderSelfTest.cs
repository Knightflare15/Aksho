namespace TemplateRecorderStandalone;

static class RecorderSelfTest
{
    public static int Run()
    {
        string path = Path.Combine(Path.GetTempPath(), $"handwriting-recorder-{Guid.NewGuid():N}.json");
        try
        {
            var strokes = new List<IReadOnlyList<PointF>>
            {
                new List<PointF> { new(10f, 90f), new(50f, 10f), new(90f, 90f) },
                new List<PointF> { new(30f, 60f), new(70f, 60f) }
            };
            var captured = new List<IReadOnlyList<HandwritingCapturedSeed>>
            {
                new List<HandwritingCapturedSeed>
                {
                    new() { position = new PointF(10f, 90f), tMs = 0 },
                    new() { position = new PointF(50f, 10f), tMs = 30 },
                    new() { position = new PointF(90f, 90f), tMs = 60 }
                },
                new List<HandwritingCapturedSeed>
                {
                    new() { position = new PointF(30f, 60f), tMs = 90 },
                    new() { position = new PointF(70f, 60f), tMs = 120 }
                }
            };
            HandwritingSampleRecord sample = HandwritingSampleFactory.Build(
                "A", "writer-test", "session-test", captured, new Size(100, 100), DateTime.UtcNow);
            Require(sample.schemaVersion == HandwritingSampleRecord.CurrentSchemaVersion, "schema version");
            Require(sample.pointCount == 5 && sample.strokeCount == 2, "point/stroke counts");
            Require(sample.rawCoordinateSystem == "canvas_top_left_y_down", "raw coordinate system");
            Require(sample.normalizedCoordinateSystem == "unit_square_bottom_left_y_up", "normalized coordinate system");
            Require(Math.Abs(sample.points[0].ny - 0f) < 0.001f && Math.Abs(sample.points[1].ny - 1f) < 0.001f, "canonical y-up normalization");
            Require(sample.points.All(point => point.nx is >= 0f and <= 1f && point.canvasX is >= 0f and <= 1f), "normalization");

            var store = new TemplateStore(path);
            store.Add("A", strokes, sample);
            var reloaded = new TemplateStore(path);
            TemplateStore.TemplateEntry entry = reloaded.GetEntries("A").Single();
            Require(entry.sample?.sampleId == sample.sampleId, "rich sample persistence");
            (string name, List<PortableRecognizer.Point> points) = reloaded.ToRecognizerTemplates().Single();
            Require(name == "A" && points.Count == 5, "legacy recognizer adapter");
            Require(Math.Abs(points[1].x - 50f) < 0.001f, "recognizer coordinates");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    static void Require(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException($"Recorder self-test failed: {name}.");
    }
}
