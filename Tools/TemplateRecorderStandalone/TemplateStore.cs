using System.Text.Json;
using System.Text.Json.Serialization;

namespace TemplateRecorderStandalone;

public sealed class TemplateStore
{
    const float ManualTemplateRecognitionScore = 1000000f;

    public sealed class RawPoint
    {
        public float x;
        public float y;
        public int strokeId;

        public RawPoint()
        {
        }

        public RawPoint(float x, float y, int strokeId)
        {
            this.x = x;
            this.y = y;
            this.strokeId = strokeId;
        }
    }

    public sealed class TemplateEntry
    {
        public string id = Guid.NewGuid().ToString();
        public string letterKey = "";
        public List<RawPoint> points = new();
        public string createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        public bool isPrime;
        public bool isAdaptive;
        public float closenessScore = 1f;
        public float recognitionScore = ManualTemplateRecognitionScore;
        public HandwritingSampleRecord? sample;
    }

    sealed class Database
    {
        public List<TemplateEntry> entries = new();
    }

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        IncludeFields = true,
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    readonly string _savePath;
    Database _db = new();

    public TemplateStore(string? savePath = null)
    {
        _savePath = string.IsNullOrWhiteSpace(savePath)
            ? Path.Combine(AppContext.BaseDirectory, "templates.json")
            : savePath;
        Load();
    }

    public string SavePath => _savePath;

    public int TotalCount => _db.entries.Count;

    public IReadOnlyList<string> GetAllKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (TemplateEntry entry in _db.entries)
        {
            if (entry != null && !string.IsNullOrWhiteSpace(entry.letterKey) && entry.points != null)
                keys.Add(entry.letterKey);
        }

        var list = keys.ToList();
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    public List<TemplateEntry> GetEntries(string letterKey)
    {
        return _db.entries
            .Where(entry => entry != null &&
                            entry.points != null &&
                            string.Equals(entry.letterKey, letterKey, StringComparison.Ordinal))
            .ToList();
    }

    public TemplateEntry? GetPrimeTemplate(string letterKey)
    {
        return GetEntries(letterKey).FirstOrDefault(entry => entry.isPrime);
    }

    public TemplateEntry Add(
        string letterKey,
        IReadOnlyList<IReadOnlyList<PointF>> strokes,
        HandwritingSampleRecord? sample = null)
    {
        List<RawPoint> points = StrokesToRaw(strokes);
        bool isPrime = GetPrimeTemplate(letterKey) == null;
        var entry = new TemplateEntry
        {
            id = Guid.NewGuid().ToString(),
            letterKey = letterKey,
            points = points,
            createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            isPrime = isPrime,
            isAdaptive = false,
            closenessScore = 1f,
            recognitionScore = isPrime ? 0f : ManualTemplateRecognitionScore
        };
        entry.sample = sample;

        _db.entries.Add(entry);
        Save();
        return entry;
    }

    public bool SetPrimeTemplate(string entryId)
    {
        TemplateEntry? chosen = _db.entries.FirstOrDefault(entry => entry != null && entry.id == entryId);
        if (chosen == null)
            return false;

        foreach (TemplateEntry entry in _db.entries)
        {
            if (entry != null && string.Equals(entry.letterKey, chosen.letterKey, StringComparison.Ordinal))
                entry.isPrime = entry.id == entryId;
        }

        chosen.closenessScore = 1f;
        chosen.recognitionScore = 0f;
        Save();
        return true;
    }

    public bool Delete(string entryId)
    {
        int removed = _db.entries.RemoveAll(entry => entry != null && entry.id == entryId);
        if (removed <= 0)
            return false;

        EnsurePrimeTemplates();
        Save();
        return true;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_savePath) ?? AppContext.BaseDirectory);
        string json = JsonSerializer.Serialize(_db, JsonOptions);
        File.WriteAllText(_savePath, json);
    }

    public void Load()
    {
        if (!File.Exists(_savePath))
        {
            _db = new Database();
            return;
        }

        string json = File.ReadAllText(_savePath);
        _db = JsonSerializer.Deserialize<Database>(json, JsonOptions) ?? new Database();

        foreach (TemplateEntry entry in _db.entries)
        {
            entry.points ??= new List<RawPoint>();
            if (entry.isPrime)
                entry.recognitionScore = 0f;
            else if (entry.isAdaptive && (!float.IsFinite(entry.recognitionScore) || entry.recognitionScore <= 0f))
                entry.recognitionScore = Lerp(60f, 12f, Clamp01(entry.closenessScore));
            else if (!entry.isAdaptive && (!float.IsFinite(entry.recognitionScore) || entry.recognitionScore <= 0f))
                entry.recognitionScore = ManualTemplateRecognitionScore;
        }

        EnsurePrimeTemplates();
    }

    public List<(string name, List<PortableRecognizer.Point> points)> ToRecognizerTemplates()
    {
        var result = new List<(string name, List<PortableRecognizer.Point> points)>();
        foreach (TemplateEntry entry in _db.entries)
        {
            var points = new List<PortableRecognizer.Point>(entry.points.Count);
            foreach (RawPoint rawPoint in entry.points)
                points.Add(new PortableRecognizer.Point(rawPoint.x, rawPoint.y, rawPoint.strokeId));
            result.Add((entry.letterKey, points));
        }

        return result;
    }

    static List<RawPoint> StrokesToRaw(IReadOnlyList<IReadOnlyList<PointF>> strokes)
    {
        var points = new List<RawPoint>();
        for (int strokeIndex = 0; strokeIndex < strokes.Count; strokeIndex++)
        {
            IReadOnlyList<PointF> stroke = strokes[strokeIndex];
            foreach (PointF point in stroke)
                points.Add(new RawPoint(point.X, point.Y, strokeIndex));
        }

        return points;
    }

    void EnsurePrimeTemplates()
    {
        foreach (string key in GetAllKeys())
        {
            if (GetPrimeTemplate(key) != null)
                continue;

            List<TemplateEntry> entries = GetEntries(key);
            if (entries.Count == 0)
                continue;

            entries[0].isPrime = true;
            entries[0].closenessScore = 1f;
            entries[0].recognitionScore = 0f;
        }
    }

    static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);

    static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
