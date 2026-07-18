using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Data layer for custom letter templates.
///
/// Stores raw (un-normalised) stroke points per letter key so:
///   • The PDollar recogniser gets fresh data to normalise its own way.
///   • The preview renderer can replay exactly what was drawn.
///
/// File location:  The active player's save-slot directory.
/// Format:         JSON — human-readable and hand-editable if needed.
///
/// Letter keys are strings like "A", "a", "B", "b" etc.
/// Each key can hold many TemplateEntry objects (one per recorded sample).
/// </summary>
public class TemplateLibrary : MonoBehaviour
{
    // ── Save path ──────────────────────────────────────────────────────────
    public static string SavePath =>
        PlayerSaveSlots.GetSaveFilePath("templates.json");

    // ── Serialisable types ─────────────────────────────────────────────────

    [Serializable]
    public class RawPoint
    {
        public float x, y;
        public int   strokeId;

        public RawPoint() { }
        public RawPoint(float x, float y, int strokeId)
        { this.x = x; this.y = y; this.strokeId = strokeId; }
    }

    [Serializable]
    public class TemplateEntry
    {
        public string       id;          // GUID — stable across renames
        public string       letterKey;   // "A", "a", "B" …
        public List<RawPoint> points;    // raw canvas points (not normalised)
        public string       createdAt;
        public bool         isPrime;
        public bool         isAdaptive;
        public float        closenessScore;
        public float        recognitionScore;
        // Rich, versioned capture data for future ML. Legacy entries may be
        // null; recognition continues to consume `points` above.
        public HandwritingSampleRecord sample;

        public TemplateEntry() { }
        public TemplateEntry(string key,
                             List<RawPoint> pts,
                             bool prime = false,
                             bool adaptive = false,
                             float closeness = 1f,
                             float recognition = float.PositiveInfinity)
        {
            id             = Guid.NewGuid().ToString();
            letterKey      = key;
            points         = pts;
            createdAt      = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            isPrime        = prime;
            isAdaptive     = adaptive;
            closenessScore = closeness;
            recognitionScore = recognition;
        }
    }

    [Serializable]
    private class Database
    {
        public List<TemplateEntry> entries = new List<TemplateEntry>();
    }

    // ── Runtime data ───────────────────────────────────────────────────────
    private Database _db = new Database();
    /// <summary>Fired whenever templates change so dependents can reload.</summary>
    public event Action OnLibraryChanged;

    // ── Unity lifecycle ────────────────────────────────────────────────────

    void Awake()
    {
        Load();
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>All entries for a specific letter key, e.g. "A".</summary>
    public List<TemplateEntry> GetEntries(string letterKey)
{
    return _db.entries.FindAll(e => 
        e != null &&
        e.letterKey == letterKey &&
        e.points != null
    );
}

    /// <summary>Every letter key that has at least one entry.</summary>
    public List<string> GetAllKeys()
    {
        var keys = new HashSet<string>();
        foreach (var e in _db.entries) keys.Add(e.letterKey);
        var list = new List<string>(keys);
        list.Sort();
        return list;
    }

    /// <summary>Total number of saved templates across all letters.</summary>
    public int TotalCount => _db.entries.Count;

    public TemplateEntry GetPrimeTemplate(string letterKey)
    {
        return GetPrimeEntry(letterKey);
    }

    /// <summary>Add a new sample and persist immediately.</summary>
    public TemplateEntry Add(string letterKey,
                             List<List<Vector2>> strokes)
    {
        return Add(letterKey, strokes, null);
    }

    public TemplateEntry Add(
        string letterKey,
        List<List<Vector2>> strokes,
        HandwritingSampleRecord sample)
    {
        var pts = StrokesToRaw(strokes);
        bool prime = GetPrimeEntry(letterKey) == null;
        var entry = new TemplateEntry(
            letterKey,
            pts,
            prime,
            adaptive: false,
            closeness: 1f,
            recognition: prime ? 0f : float.PositiveInfinity);
        entry.sample = sample;
        _db.entries.Add(entry);
        Save();
        OnLibraryChanged?.Invoke();
        return entry;
    }

    public bool SetPrimeTemplate(string entryId)
    {
        var chosen = _db.entries.Find(e => e != null && e.id == entryId);
        if (chosen == null)
            return false;

        foreach (var entry in _db.entries)
        {
            if (entry != null && entry.letterKey == chosen.letterKey)
                entry.isPrime = entry.id == entryId;
        }

        chosen.closenessScore = 1f;
        chosen.recognitionScore = 0f;
        Save();
        OnLibraryChanged?.Invoke();
        return true;
    }

    /// <summary>Delete one entry by its GUID and persist.</summary>
    public void Delete(string entryId)
    {
        int removed = _db.entries.RemoveAll(e => e.id == entryId);
        if (removed > 0)
        {
            EnsurePrimeTemplates();
            Save();
            OnLibraryChanged?.Invoke();
        }
    }

    /// <summary>Delete ALL entries for a letter key.</summary>
    public void DeleteAll(string letterKey)
    {
        int removed = _db.entries.RemoveAll(e => e.letterKey == letterKey);
        if (removed > 0)
        {
            Save();
            OnLibraryChanged?.Invoke();
        }
    }

    /// <summary>
    /// Convert all entries into PDollar Points grouped by letter.
    /// Returns a list of (name, points) tuples ready for AddTemplate().
    /// </summary>
    public List<(string name, List<PDollarRecognizer.Point> points)> ToRecognizerTemplates()
    {
        var result = new List<(string, List<PDollarRecognizer.Point>)>();

        foreach (var entry in _db.entries)
        {
            if (entry == null || entry.isAdaptive || entry.points == null)
                continue;

            var pts = new List<PDollarRecognizer.Point>();
            foreach (var rp in GetUnityOrientedRawPoints(entry.points))
                pts.Add(new PDollarRecognizer.Point(rp.x, rp.y, rp.strokeId));
            result.Add((entry.letterKey, pts));
        }
        return result;
    }

    // ── Persistence ────────────────────────────────────────────────────────

    public void Save()
    {
        try
        {
            PlayerSaveSlots.EnsureActiveSlotDirectory();
            string json = JsonUtility.ToJson(_db, prettyPrint: true);
            File.WriteAllText(SavePath, json);
            Debug.Log($"[TemplateLibrary] Saved {_db.entries.Count} templates → {SavePath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[TemplateLibrary] Save failed: {ex.Message}");
        }
    }

    public void Load()
    {
        if (!File.Exists(SavePath))
        {
            Debug.Log("[TemplateLibrary] No save file found - seeding default templates.");
            EnsureDefaultTemplates();
            return;
        }

        try
        {
            string json = File.ReadAllText(SavePath);
            _db = JsonUtility.FromJson<Database>(json) ?? new Database();
            foreach (var e in _db.entries)
            {
                if (e.points == null)
                    e.points = new List<RawPoint>();
                if (e.isPrime)
                    e.recognitionScore = 0f;
                else if (e.isAdaptive && e.recognitionScore <= 0f)
                    e.recognitionScore = Mathf.Lerp(60f, 12f, Mathf.Clamp01(e.closenessScore));
                else if (!e.isAdaptive && e.recognitionScore <= 0f)
                    e.recognitionScore = float.PositiveInfinity;
            }
            EnsurePrimeTemplates();
            EnsureDefaultTemplates();
            Debug.Log($"[TemplateLibrary] Loaded {_db.entries.Count} templates.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[TemplateLibrary] Load failed: {ex.Message}");
            _db = new Database();
            EnsureDefaultTemplates();
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    static List<RawPoint> StrokesToRaw(List<List<Vector2>> strokes)
    {
        var pts = new List<RawPoint>();
        for (int s = 0; s < strokes.Count; s++)
            foreach (var p in strokes[s])
                pts.Add(new RawPoint(p.x, p.y, s));
        return pts;
    }

    void EnsureDefaultTemplates()
    {
        _db ??= new Database();
        _db.entries ??= new List<TemplateEntry>();

        bool changed = TrySeedAuthoredDefaultTemplates();
        if (!changed)
            changed = SeedGeometricFallbackTemplates();

        if (!changed)
            return;

        EnsurePrimeTemplates();
        Save();
        OnLibraryChanged?.Invoke();
    }

    bool TrySeedAuthoredDefaultTemplates()
    {
        TextAsset defaults = Resources.Load<TextAsset>("DefaultTemplates");
        if (defaults == null || string.IsNullOrWhiteSpace(defaults.text))
            return false;

        try
        {
            Database defaultDb = JsonUtility.FromJson<Database>(defaults.text);
            if (defaultDb == null || defaultDb.entries == null || defaultDb.entries.Count == 0)
                return false;

            return SeedMissingLetters(defaultDb.entries);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[TemplateLibrary] Could not load authored default templates: {ex.Message}");
            return false;
        }
    }

    bool SeedMissingLetters(List<TemplateEntry> sourceEntries)
    {
        var existingKeys = GetExistingTemplateKeys();
        bool changed = false;

        foreach (TemplateEntry source in sourceEntries)
        {
            if (source == null || string.IsNullOrWhiteSpace(source.letterKey) ||
                source.points == null || source.points.Count < 2)
                continue;

            string key = source.letterKey.Trim().ToUpperInvariant();
            if (existingKeys.Contains(key))
                continue;

            _db.entries.Add(CloneTemplateEntry(source, key));
            changed = true;
        }

        return changed;
    }

    bool SeedGeometricFallbackTemplates()
    {
        var existingKeys = GetExistingTemplateKeys();
        bool changed = false;
        var defaultCounts = new Dictionary<string, int>();

        foreach (var template in PDollarRecognizer.CreateDefaultAlphabetTemplateSnapshot())
        {
            string key = string.IsNullOrWhiteSpace(template.name)
                ? ""
                : template.name.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(key) || existingKeys.Contains(key))
                continue;

            defaultCounts.TryGetValue(key, out int countForKey);
            _db.entries.Add(new TemplateEntry(
                key,
                RecognizerPointsToRaw(template.points),
                prime: countForKey == 0,
                adaptive: false,
                closeness: 1f,
                recognition: countForKey == 0 ? 0f : float.PositiveInfinity));

            defaultCounts[key] = countForKey + 1;
            changed = true;
        }

        return changed;
    }

    HashSet<string> GetExistingTemplateKeys()
    {
        var existingKeys = new HashSet<string>();
        foreach (TemplateEntry entry in _db.entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.letterKey))
                continue;

            existingKeys.Add(entry.letterKey.Trim().ToUpperInvariant());
        }

        return existingKeys;
    }

    static TemplateEntry CloneTemplateEntry(TemplateEntry source, string key)
    {
        var points = new List<RawPoint>(source.points.Count);
        foreach (RawPoint point in source.points)
            if (point != null)
                points.Add(new RawPoint(point.x, point.y, point.strokeId));

        return new TemplateEntry(
            key,
            points,
            prime: source.isPrime,
            adaptive: false,
            closeness: source.closenessScore <= 0f ? 1f : source.closenessScore,
            recognition: source.isPrime ? 0f : source.recognitionScore);
    }

    static List<RawPoint> RecognizerPointsToRaw(List<PDollarRecognizer.Point> points)
    {
        var raw = new List<RawPoint>(points != null ? points.Count : 0);
        if (points == null)
            return raw;

        foreach (PDollarRecognizer.Point point in points)
            if (point != null)
                raw.Add(new RawPoint(point.x, point.y, point.id));

        return raw;
    }

    public static List<RawPoint> GetUnityOrientedRawPoints(List<RawPoint> rawPoints)
    {
        if (rawPoints == null)
            return new List<RawPoint>();

        return LooksLikeStandaloneRecorderSpace(rawPoints)
            ? FlipRawPointsVertically(rawPoints)
            : CloneRawPoints(rawPoints);
    }

    static bool LooksLikeStandaloneRecorderSpace(List<RawPoint> rawPoints)
    {
        if (rawPoints == null || rawPoints.Count == 0)
            return false;

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minY = float.MaxValue;
        float maxY = float.MinValue;

        foreach (var raw in rawPoints)
        {
            if (raw == null)
                continue;

            if (raw.x < minX) minX = raw.x;
            if (raw.x > maxX) maxX = raw.x;
            if (raw.y < minY) minY = raw.y;
            if (raw.y > maxY) maxY = raw.y;
        }

        if (minX == float.MaxValue || minY == float.MaxValue)
            return false;

        return minX >= 0f &&
               minY >= 0f &&
               maxX > 500f &&
               maxY < 600f;
    }

    static List<RawPoint> FlipRawPointsVertically(List<RawPoint> rawPoints)
    {
        if (rawPoints == null || rawPoints.Count == 0)
            return new List<RawPoint>();

        float minY = float.MaxValue;
        float maxY = float.MinValue;

        foreach (var raw in rawPoints)
        {
            if (raw == null)
                continue;

            if (raw.y < minY) minY = raw.y;
            if (raw.y > maxY) maxY = raw.y;
        }

        var flipped = new List<RawPoint>(rawPoints.Count);
        foreach (var raw in rawPoints)
        {
            if (raw == null)
                continue;

            float flippedY = maxY - (raw.y - minY);
            flipped.Add(new RawPoint(raw.x, flippedY, raw.strokeId));
        }

        return flipped;
    }

    static List<RawPoint> CloneRawPoints(List<RawPoint> rawPoints)
    {
        var cloned = new List<RawPoint>(rawPoints.Count);
        foreach (var raw in rawPoints)
        {
            if (raw != null)
                cloned.Add(new RawPoint(raw.x, raw.y, raw.strokeId));
        }

        return cloned;
    }

    TemplateEntry GetPrimeEntry(string letterKey)
    {
        return _db.entries.Find(e =>
            e != null &&
            e.letterKey == letterKey &&
            e.isPrime &&
            e.points != null);
    }

    void EnsurePrimeTemplates()
    {
        var keys = GetAllKeys();
        foreach (string key in keys)
        {
            if (GetPrimeEntry(key) != null)
                continue;

            var entries = GetEntries(key);
            if (entries.Count == 0)
                continue;

            entries[0].isPrime = true;
            entries[0].closenessScore = 1f;
            entries[0].recognitionScore = 0f;
        }
    }

}
