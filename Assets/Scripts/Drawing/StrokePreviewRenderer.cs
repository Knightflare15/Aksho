using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Draws stroke paths inside a UI RectTransform using Image-based line segments.
///
/// Key fix: SetFromRaw() takes an explicit renderSize parameter instead of
/// reading _rt.rect.size, because rect.size is (0,0) until Unity does a
/// layout pass — which hasn't happened when cards are first built.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class StrokePreviewRenderer : MonoBehaviour
{
    public Color strokeColour    = Color.white;
    public float strokeThickness = 4f;
    public bool useRoundBrushRendering;

    private RectTransform      _rt;
    private List<GameObject>   _segments = new List<GameObject>();

    void Awake() => _rt = GetComponent<RectTransform>();

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Render saved template points scaled to fit renderSize.
    /// renderSize = the pixel dimensions of this preview rect (pass cardPreviewSize,
    /// not recorderDrawArea.rect.size — that may be zero).
    /// originalSize = the coordinate space the points were recorded in.
    /// </summary>
    public void SetFromRaw(List<TemplateLibrary.RawPoint> rawPts,
                           Vector2 originalSize,
                           Vector2 renderSize)
    {
        Clear();
        if (rawPts == null || rawPts.Count == 0) return;
        rawPts = TemplateLibrary.GetUnityOrientedRawPoints(rawPts);

        // Guard against zero sizes
        if (originalSize.x < 1f) originalSize.x = 400f;
        if (originalSize.y < 1f) originalSize.y = 400f;
        if (renderSize.x   < 1f) renderSize.x   = 100f;
        if (renderSize.y   < 1f) renderSize.y   = 100f;

        // Find bounding box of raw points so we can centre and fit them
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var rp in rawPts)
        {
            if (rp.x < minX) minX = rp.x; if (rp.x > maxX) maxX = rp.x;
            if (rp.y < minY) minY = rp.y; if (rp.y > maxY) maxY = rp.y;
        }

        float rangeX = Mathf.Max(1f, maxX - minX);
        float rangeY = Mathf.Max(1f, maxY - minY);

        // Uniform scale to fit inside renderSize with 10% padding
        float padding = 0.85f;
        float scale   = Mathf.Min(renderSize.x / rangeX, renderSize.y / rangeY) * padding;

        float cx = (minX + maxX) * 0.5f;
        float cy = (minY + maxY) * 0.5f;

        // Group by stroke id
        var groups = new SortedDictionary<int, List<Vector2>>();
        foreach (var rp in rawPts)
        {
            if (!groups.ContainsKey(rp.strokeId))
                groups[rp.strokeId] = new List<Vector2>();

            // Centre on bounding box, scale to preview size
            float x = (rp.x - cx) * scale;
            float y = (rp.y - cy) * scale;
            groups[rp.strokeId].Add(new Vector2(x, y));
        }

        foreach (var kv in groups)
            DrawStroke(kv.Value);
    }

    public void SetFromLocalStrokes(List<List<Vector2>> strokes)
    {
        Clear();
        if (strokes == null) return;

        foreach (var stroke in strokes)
            DrawStroke(stroke);
    }

    public void Clear()
    {
        foreach (var go in _segments)
        {
            if (!go)
                continue;

            if (Application.isPlaying)
                Destroy(go);
            else
                DestroyImmediate(go);
        }
        _segments.Clear();
    }

    public void SetOpacity(float alpha)
    {
        strokeColour = new Color(strokeColour.r, strokeColour.g, strokeColour.b, alpha);

        foreach (var segment in _segments)
        {
            if (segment == null) continue;

            var image = segment.GetComponent<Image>();
            if (image == null) continue;

            var c = image.color;
            image.color = new Color(c.r, c.g, c.b, alpha);
        }
    }

    // ── Drawing ────────────────────────────────────────────────────────────

    void DrawStroke(List<Vector2> pts)
    {
        if (pts == null || pts.Count == 0) return;
        if (useRoundBrushRendering)
        {
            DrawSmoothStroke(pts);
            return;
        }

        SpawnDot(pts[0]);
        for (int i = 1; i < pts.Count; i++)
            SpawnSegment(pts[i - 1], pts[i]);
    }

    void DrawSmoothStroke(List<Vector2> pts)
    {
        float spacing = Mathf.Clamp(strokeThickness * 0.28f, 4f, 18f);
        SpawnDot(pts[0]);
        for (int i = 1; i < pts.Count; i++)
        {
            Vector2 from = pts[i - 1];
            Vector2 to = pts[i];
            float distance = Vector2.Distance(from, to);
            if (distance < 0.5f)
                continue;

            int steps = Mathf.Max(1, Mathf.CeilToInt(distance / spacing));
            for (int step = 1; step <= steps; step++)
                SpawnDot(Vector2.Lerp(from, to, step / (float)steps));
        }
    }

    void SpawnDot(Vector2 pos)
    {
        var go = Make("d");
        var rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta        = new Vector2(strokeThickness, strokeThickness);
        BrushStrokeStyle.ApplyDot(go.GetComponent<Image>(), strokeColour);
    }

    void SpawnSegment(Vector2 from, Vector2 to)
    {
        var dir = to - from;
        if (dir.magnitude < 0.5f) return;
        var go = Make("s");
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta        = new Vector2(dir.magnitude, strokeThickness);
        rt.anchoredPosition = from + dir * 0.5f;
        rt.localRotation    = Quaternion.Euler(0, 0,
            Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        BrushStrokeStyle.ApplySegment(go.GetComponent<Image>(), strokeColour);
    }

    GameObject Make(string n)
    {
        var go = new GameObject(n, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(_rt, false);
        go.GetComponent<Image>().raycastTarget = false;
        _segments.Add(go);
        return go;
    }
}
