using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Renders a lightweight ghost guide for the current target letter and
/// evaluates how far the player's live stroke has drifted from that guide.
/// This first slice only includes guides for R, A, and T.
/// </summary>
public class LetterGuideCoach
{
    public enum GuidanceState
    {
        Hidden,
        OnTrack,
        Drifting,
        OffTrack
    }

    struct GuideEvaluation
    {
        public float distance;
        public Vector2 target;
    }

    struct GuideSource
    {
        public List<TemplateLibrary.RawPoint> points;
        public bool flipVertically;
        public bool isPrimeTemplate;
    }

    private readonly FeedbackManager feedback;
    private readonly TemplateLibrary templateLibrary;
    private readonly Vector2 guideRenderSize;
    private readonly RectTransform guideRoot;
    private readonly StrokePreviewRenderer guideRenderer;
    private readonly RectTransform leadRoot;
    private readonly Image leadGlowImage;
    private readonly Image leadCoreImage;
    private readonly List<Image> leadTrailImages = new List<Image>();
    private readonly List<Vector2> leadTrailPositions = new List<Vector2>();
    private readonly Dictionary<char, List<TemplateLibrary.RawPoint>> rawGuides;

    private List<List<Vector2>> activeGuideStrokes = new List<List<Vector2>>();
    private GuidanceState state = GuidanceState.Hidden;
    private char activeLetter = '\0';
    private float lastFeedbackAt;
    private bool activeGuideUsesPrimeTemplate;

    private const float OnTrackDistance = 18f;
    private const float DriftDistance = 38f;
    private const float HardAssistDistance = 76f;
    private const float LeadAheadDistance = 66f;
    private const float InitialPreviewAlpha = 0.24f;
    private const float FailedAttemptAlpha = 0.38f;
    private const float TraceAcceptThreshold = 0.79f;
    private const float DriftFeedbackCooldown = 0.34f;
    private const float OffTrackFeedbackCooldown = 0.22f;
    private const int LeadTrailCount = 6;

    public LetterGuideCoach(
        RectTransform panel,
        FeedbackManager feedbackManager,
        Vector2 desiredGuideSize,
        TemplateLibrary library = null,
        float guideStrokeThickness = 9f)
    {
        feedback = feedbackManager;
        templateLibrary = library;
        guideRenderSize = desiredGuideSize;
        rawGuides = BuildGuides();

        var rootGo = new GameObject("LetterGuideRoot", typeof(RectTransform));
        guideRoot = rootGo.GetComponent<RectTransform>();
        guideRoot.SetParent(panel, false);
        guideRoot.anchorMin = new Vector2(0.5f, 0.5f);
        guideRoot.anchorMax = new Vector2(0.5f, 0.5f);
        guideRoot.pivot = new Vector2(0.5f, 0.5f);
        guideRoot.sizeDelta = desiredGuideSize;

        var previewGo = new GameObject("LetterGuidePreview", typeof(RectTransform));
        var previewRt = previewGo.GetComponent<RectTransform>();
        previewRt.SetParent(guideRoot, false);
        previewRt.anchorMin = Vector2.zero;
        previewRt.anchorMax = Vector2.one;
        previewRt.offsetMin = Vector2.zero;
        previewRt.offsetMax = Vector2.zero;

        guideRenderer = previewGo.AddComponent<StrokePreviewRenderer>();
        guideRenderer.strokeColour = new Color(1f, 1f, 1f, 0.25f);
        guideRenderer.strokeThickness = Mathf.Max(1f, guideStrokeThickness);

        var leadGo = new GameObject("LetterGuideLead", typeof(RectTransform));
        leadRoot = leadGo.GetComponent<RectTransform>();
        leadRoot.SetParent(panel, false);
        leadRoot.anchorMin = new Vector2(0.5f, 0.5f);
        leadRoot.anchorMax = new Vector2(0.5f, 0.5f);
        leadRoot.pivot = new Vector2(0.5f, 0.5f);

        var glowGo = new GameObject("Glow", typeof(RectTransform), typeof(Image));
        var glowRt = glowGo.GetComponent<RectTransform>();
        glowRt.SetParent(leadRoot, false);
        glowRt.anchorMin = new Vector2(0.5f, 0.5f);
        glowRt.anchorMax = new Vector2(0.5f, 0.5f);
        glowRt.pivot = new Vector2(0.5f, 0.5f);
        leadGlowImage = glowGo.GetComponent<Image>();
        leadGlowImage.sprite = GetCircleSprite();
        leadGlowImage.raycastTarget = false;

        var coreGo = new GameObject("Core", typeof(RectTransform), typeof(Image));
        var coreRt = coreGo.GetComponent<RectTransform>();
        coreRt.SetParent(leadRoot, false);
        coreRt.anchorMin = new Vector2(0.5f, 0.5f);
        coreRt.anchorMax = new Vector2(0.5f, 0.5f);
        coreRt.pivot = new Vector2(0.5f, 0.5f);
        leadCoreImage = coreGo.GetComponent<Image>();
        leadCoreImage.sprite = GetCircleSprite();
        leadCoreImage.raycastTarget = false;

        for (int i = 0; i < LeadTrailCount; i++)
        {
            var trailGo = new GameObject($"Trail{i}", typeof(RectTransform), typeof(Image));
            var trailRt = trailGo.GetComponent<RectTransform>();
            trailRt.SetParent(panel, false);
            trailRt.anchorMin = new Vector2(0.5f, 0.5f);
            trailRt.anchorMax = new Vector2(0.5f, 0.5f);
            trailRt.pivot = new Vector2(0.5f, 0.5f);
            trailRt.sizeDelta = Vector2.one * 10f;

            var trailImage = trailGo.GetComponent<Image>();
            trailImage.sprite = GetCircleSprite();
            trailImage.raycastTarget = false;
            trailImage.enabled = false;
            leadTrailImages.Add(trailImage);
            leadTrailPositions.Add(Vector2.zero);
        }

        Hide();
    }

    public void BeginLetter(char letter)
    {
        activeLetter = char.ToUpperInvariant(letter);
        state = GuidanceState.Hidden;

        GuideSource guideSource = TryGetPrimeGuide(activeLetter);
        if (guideSource.points == null && rawGuides.TryGetValue(activeLetter, out var fallbackPoints))
        {
            guideSource = new GuideSource
            {
                points = fallbackPoints,
                flipVertically = true,
                isPrimeTemplate = false
            };
        }

        if (guideSource.points == null)
        {
            Hide();
            return;
        }

        var displayPoints = guideSource.flipVertically
            ? FlipRawPointsVertically(guideSource.points)
            : TemplateLibrary.GetUnityOrientedRawPoints(guideSource.points);
        activeGuideStrokes = TransformRawToGuideSpace(displayPoints, guideRenderSize);
        activeGuideUsesPrimeTemplate = guideSource.isPrimeTemplate;
        HideVisual();
    }

    private GuideSource TryGetPrimeGuide(char letter)
    {
        if (templateLibrary == null)
            return default;

        var prime = templateLibrary.GetPrimeTemplate(letter.ToString());
        if (prime != null && prime.points != null && prime.points.Count > 1)
        {
            return new GuideSource
            {
                points = prime.points,
                flipVertically = false,
                isPrimeTemplate = true
            };
        }

        prime = templateLibrary.GetPrimeTemplate(char.ToLowerInvariant(letter).ToString());
        if (prime != null && prime.points != null && prime.points.Count > 1)
        {
            return new GuideSource
            {
                points = prime.points,
                flipVertically = false,
                isPrimeTemplate = true
            };
        }

        return default;
    }

    public GuidanceState UpdateStroke(
        List<List<Vector2>> strokes,
        List<GameObject> strokeVisuals,
        bool showLeadAssist)
    {
        if (activeLetter == '\0' || activeGuideStrokes.Count == 0)
            return GuidanceState.Hidden;

        if (!TryGetLastPoint(strokes, out var lastPoint))
            return GuidanceState.Hidden;

        var evaluation = EvaluateAgainstGuide(lastPoint, LeadAheadDistance);
        GuidanceState nextState = evaluation.distance <= OnTrackDistance
            ? GuidanceState.OnTrack
            : evaluation.distance <= DriftDistance
                ? GuidanceState.Drifting
                : GuidanceState.OffTrack;

        bool escalated = nextState > state;
        float feedbackCooldown = nextState == GuidanceState.OffTrack
            ? OffTrackFeedbackCooldown
            : DriftFeedbackCooldown;
        bool sustainedGuidance = nextState != GuidanceState.OnTrack &&
                                 Time.unscaledTime - lastFeedbackAt >= feedbackCooldown;

        if (escalated || sustainedGuidance)
        {
            feedback?.PlayGuidanceFeedback(ToFeedbackState(nextState), strokeVisuals);
            lastFeedbackAt = Time.unscaledTime;
        }

        state = nextState;
        UpdateLiveGuideVisual(nextState, evaluation.distance);

        if (showLeadAssist || nextState != GuidanceState.OnTrack)
            ShowLeadAt(evaluation.target, evaluation.distance, nextState);
        else
            HideLeadMarker();

        return nextState;
    }

    public bool TryAdjustStrokePoint(
        Vector2 rawPoint,
        Vector2 previousPoint,
        bool isStrokeStart,
        out Vector2 adjustedPoint)
    {
        adjustedPoint = rawPoint;
        if (activeLetter == '\0' || activeGuideStrokes.Count == 0)
            return false;

        var rawEval = EvaluateAgainstGuide(rawPoint, LeadAheadDistance);
        var previousEval = EvaluateAgainstGuide(previousPoint, LeadAheadDistance);

        float drift = Mathf.InverseLerp(OnTrackDistance, HardAssistDistance, rawEval.distance);
        float previousDistance = Vector2.Distance(previousPoint, previousEval.target);
        float nextDistance = Vector2.Distance(rawPoint, rawEval.target);
        bool movingAway = nextDistance > previousDistance + 1f;

        float slowFactor = movingAway
            ? Mathf.Lerp(1f, 0.22f, drift)
            : Mathf.Lerp(1f, 0.62f, drift * 0.55f);

        Vector2 candidate = previousPoint + (rawPoint - previousPoint) * slowFactor;
        float pullStrength = isStrokeStart
            ? Mathf.Lerp(0.24f, 0.58f, drift)
            : Mathf.Lerp(0.16f, 0.48f, drift);

        if (movingAway)
            pullStrength = Mathf.Min(0.72f, pullStrength + drift * 0.26f);

        adjustedPoint = Vector2.Lerp(candidate, rawEval.target, pullStrength);
        return true;
    }

    public bool IsStrongTraceMatch(List<List<Vector2>> strokes, out float closeness)
    {
        closeness = 0f;
        if (strokes == null || activeGuideStrokes.Count == 0)
            return false;

        var candidate = FlattenStrokes(strokes);
        var guide = FlattenStrokes(activeGuideStrokes);
        if (candidate.Count < 2 || guide.Count < 2)
            return false;

        var a = NormalizeForComparison(candidate, 64, 250f);
        var b = NormalizeForComparison(guide, 64, 250f);
        if (a.Count == 0 || b.Count == 0 || a.Count != b.Count)
            return false;

        float sum = 0f;
        for (int i = 0; i < a.Count; i++)
            sum += Vector2.Distance(a[i], b[i]);

        float average = sum / a.Count;
        closeness = Mathf.Clamp01(1f - average / 95f);
        return closeness >= TraceAcceptThreshold;
    }

    public void ShowAnimatedPreview(float progress)
    {
        if (activeLetter == '\0' || activeGuideStrokes.Count == 0)
            return;

        guideRoot.gameObject.SetActive(true);
        var partial = BuildPartialGuide(Mathf.Clamp01(progress));
        guideRenderer.SetFromLocalStrokes(partial);
        float previewAlpha = Mathf.Lerp(0.06f, activeGuideUsesPrimeTemplate ? 0.30f : InitialPreviewAlpha, Mathf.SmoothStep(0f, 1f, progress));
        guideRenderer.SetOpacity(previewAlpha);

        if (TryGetLastPoint(partial, out var tip))
            ShowLeadAt(tip, 0f, GuidanceState.OnTrack, true);
        else
            HideLeadMarker();
    }

    public void CompleteLetter()
    {
        state = GuidanceState.Hidden;
        HideLeadMarker();
        HideVisual();
    }

    public void Hide()
    {
        activeLetter = '\0';
        activeGuideStrokes.Clear();
        state = GuidanceState.Hidden;
        activeGuideUsesPrimeTemplate = false;
        HideLeadMarker();
        HideVisual();
    }

    public void SetOpacity(float alpha)
    {
        if (guideRenderer != null)
            guideRenderer.SetOpacity(Mathf.Clamp01(alpha));
    }

    public void ShowForAttempt(int attemptsUsed)
    {
        if (activeLetter == '\0' || activeGuideStrokes.Count == 0)
            return;

        float alpha = attemptsUsed >= 2
            ? FailedAttemptAlpha
            : activeGuideUsesPrimeTemplate ? 0.16f : InitialPreviewAlpha;
        guideRoot.gameObject.SetActive(true);
        guideRenderer.SetFromLocalStrokes(activeGuideStrokes);
        SetOpacity(alpha);
    }

    public void HideVisual()
    {
        if (guideRenderer != null)
            guideRenderer.Clear();
        if (guideRoot != null)
            guideRoot.gameObject.SetActive(false);
        HideLeadMarker();
    }

    public void HideLeadMarker()
    {
        if (leadRoot != null)
            leadRoot.gameObject.SetActive(false);

        for (int i = 0; i < leadTrailImages.Count; i++)
        {
            if (leadTrailImages[i] != null)
                leadTrailImages[i].enabled = false;
        }
    }

    public void ShowAfterTwoFails(int attemptsUsed)
    {
        if (attemptsUsed >= 2)
            ShowForAttempt(attemptsUsed);
        else
            HideVisual();
    }

    public static string BuildHint(char letter, GuidanceState guidanceState)
    {
        switch (guidanceState)
        {
            case GuidanceState.OnTrack:
                return $"Nice. Stay on the glow for '{letter}'.";
            case GuidanceState.Drifting:
                return $"Drifting. Pull back toward the glow on '{letter}'.";
            case GuidanceState.OffTrack:
                return $"Off track. Slow down and trace back into '{letter}'.";
            default:
                return $"Write the letter \"{letter}\".";
        }
    }

    private static bool TryGetLastPoint(List<List<Vector2>> strokes, out Vector2 point)
    {
        for (int i = strokes.Count - 1; i >= 0; i--)
        {
            var stroke = strokes[i];
            if (stroke != null && stroke.Count > 0)
            {
                point = stroke[stroke.Count - 1];
                return true;
            }
        }

        point = Vector2.zero;
        return false;
    }

    private GuideEvaluation EvaluateAgainstGuide(Vector2 point, float lookAheadDistance)
    {
        float best = float.MaxValue;
        Vector2 bestTarget = point;

        foreach (var stroke in activeGuideStrokes)
        {
            if (stroke.Count == 1)
            {
                float d = Vector2.Distance(point, stroke[0]);
                if (d < best)
                {
                    best = d;
                    bestTarget = stroke[0];
                }
                continue;
            }

            for (int i = 1; i < stroke.Count; i++)
            {
                var a = stroke[i - 1];
                var b = stroke[i];
                var sample = ClosestPointOnSegment(point, a, b, out float t);
                float d = Vector2.Distance(point, sample);

                if (d >= best)
                    continue;

                best = d;
                bestTarget = d > OnTrackDistance
                    ? sample
                    : LookAheadOnStroke(stroke, i - 1, t, lookAheadDistance);
            }
        }

        return new GuideEvaluation
        {
            distance = best,
            target = bestTarget
        };
    }

    private static Vector2 ClosestPointOnSegment(Vector2 point, Vector2 a, Vector2 b, out float t)
    {
        var ab = b - a;
        float denom = ab.sqrMagnitude;
        if (denom <= 0.001f)
        {
            t = 0f;
            return a;
        }

        t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / denom);
        return a + ab * t;
    }

    private static Vector2 LookAheadOnStroke(List<Vector2> stroke, int segmentIndex, float segmentT, float distance)
    {
        Vector2 current = Vector2.Lerp(stroke[segmentIndex], stroke[segmentIndex + 1], segmentT);
        float remaining = distance;

        for (int i = segmentIndex; i < stroke.Count - 1; i++)
        {
            Vector2 from = i == segmentIndex ? current : stroke[i];
            Vector2 to = stroke[i + 1];
            float segmentLength = Vector2.Distance(from, to);

            if (segmentLength >= remaining)
                return Vector2.Lerp(from, to, remaining / Mathf.Max(segmentLength, 0.001f));

            remaining -= segmentLength;
        }

        return stroke[stroke.Count - 1];
    }

    private void ShowLeadAt(Vector2 position, float distance, GuidanceState guidanceState, bool preview = false)
    {
        if (leadRoot == null)
            return;

        float pulse = 1f + Mathf.Sin(Time.unscaledTime * 9.5f) * 0.12f;
        float drift = Mathf.InverseLerp(0f, HardAssistDistance, distance);
        float glowSize = preview ? 36f : Mathf.Lerp(24f, 50f, drift);
        float coreSize = preview ? 12f : Mathf.Lerp(10f, 18f, drift);

        leadRoot.gameObject.SetActive(true);
        leadRoot.anchoredPosition = position;
        UpdateTrail(position, guidanceState, preview);

        var glowRt = leadGlowImage.rectTransform;
        glowRt.sizeDelta = Vector2.one * glowSize * pulse;
        leadGlowImage.color = preview
            ? new Color(0.55f, 0.9f, 1f, 0.35f)
            : guidanceState == GuidanceState.OnTrack
                ? new Color(GameUiTheme.Accent.r, GameUiTheme.Accent.g, GameUiTheme.Accent.b, 0.34f)
                : guidanceState == GuidanceState.Drifting
                    ? new Color(GameUiTheme.Gold.r, GameUiTheme.Gold.g, GameUiTheme.Gold.b, 0.40f)
                    : new Color(1f, 0.45f, 0.35f, 0.48f);

        var coreRt = leadCoreImage.rectTransform;
        coreRt.sizeDelta = Vector2.one * coreSize * pulse;
        leadCoreImage.color = preview
            ? new Color(0.85f, 1f, 1f, 0.9f)
            : guidanceState == GuidanceState.OnTrack
                ? new Color(0.92f, 1f, 0.96f, 0.98f)
                : guidanceState == GuidanceState.Drifting
                    ? new Color(1f, 0.95f, 0.72f, 0.98f)
                    : new Color(1f, 0.82f, 0.78f, 0.98f);
    }

    private void UpdateLiveGuideVisual(GuidanceState guidanceState, float distance)
    {
        if (guideRenderer == null || guideRoot == null || activeGuideStrokes.Count == 0)
            return;

        float opacity = guidanceState == GuidanceState.OnTrack
            ? Mathf.Lerp(activeGuideUsesPrimeTemplate ? 0.04f : 0.08f, activeGuideUsesPrimeTemplate ? 0.12f : 0.18f, Mathf.InverseLerp(0f, OnTrackDistance, distance))
            : guidanceState == GuidanceState.Drifting
                ? Mathf.Lerp(activeGuideUsesPrimeTemplate ? 0.22f : 0.30f, activeGuideUsesPrimeTemplate ? 0.46f : 0.56f, Mathf.InverseLerp(OnTrackDistance, DriftDistance, distance))
                : Mathf.Lerp(0.58f, 0.86f, Mathf.InverseLerp(DriftDistance, HardAssistDistance, distance));

        guideRoot.gameObject.SetActive(true);
        guideRenderer.SetFromLocalStrokes(activeGuideStrokes);
        guideRenderer.SetOpacity(opacity);
    }

    private void UpdateTrail(Vector2 position, GuidanceState guidanceState, bool preview)
    {
        if (leadTrailImages.Count == 0)
            return;

        for (int i = leadTrailPositions.Count - 1; i > 0; i--)
            leadTrailPositions[i] = leadTrailPositions[i - 1];
        leadTrailPositions[0] = position;

        Color baseColor = preview
            ? new Color(0.75f, 0.95f, 1f, 0.24f)
            : guidanceState == GuidanceState.OnTrack
                ? new Color(GameUiTheme.Accent.r, GameUiTheme.Accent.g, GameUiTheme.Accent.b, 0.30f)
                : guidanceState == GuidanceState.Drifting
                    ? new Color(GameUiTheme.Gold.r, GameUiTheme.Gold.g, GameUiTheme.Gold.b, 0.34f)
                    : new Color(1f, 0.52f, 0.40f, 0.36f);

        for (int i = 0; i < leadTrailImages.Count; i++)
        {
            Image image = leadTrailImages[i];
            if (image == null)
                continue;

            float fade = 1f - (i + 1f) / (leadTrailImages.Count + 1f);
            image.enabled = true;
            image.rectTransform.anchoredPosition = leadTrailPositions[i];
            float size = Mathf.Lerp(8f, 18f, fade);
            image.rectTransform.sizeDelta = Vector2.one * size;
            image.color = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * fade);
        }
    }

    private List<List<Vector2>> BuildPartialGuide(float progress)
    {
        if (activeGuideStrokes.Count == 0)
            return new List<List<Vector2>>();

        if (progress <= 0f)
            return new List<List<Vector2>> { new List<Vector2> { activeGuideStrokes[0][0] } };

        float totalLength = TotalLength(activeGuideStrokes);
        if (totalLength <= 0.001f || progress >= 1f)
            return CopyStrokes(activeGuideStrokes);

        float targetLength = totalLength * progress;
        float covered = 0f;
        var partial = new List<List<Vector2>>();

        foreach (var stroke in activeGuideStrokes)
        {
            if (stroke == null || stroke.Count == 0)
                continue;

            var partialStroke = new List<Vector2> { stroke[0] };

            for (int i = 1; i < stroke.Count; i++)
            {
                float segmentLength = Vector2.Distance(stroke[i - 1], stroke[i]);
                if (covered + segmentLength < targetLength)
                {
                    partialStroke.Add(stroke[i]);
                    covered += segmentLength;
                    continue;
                }

                float remaining = Mathf.Max(0f, targetLength - covered);
                if (segmentLength > 0.001f && remaining > 0.001f)
                    partialStroke.Add(Vector2.Lerp(stroke[i - 1], stroke[i], remaining / segmentLength));

                partial.Add(partialStroke);
                return partial;
            }

            partial.Add(partialStroke);
        }

        return partial;
    }

    private static float TotalLength(List<List<Vector2>> strokes)
    {
        float total = 0f;
        foreach (var stroke in strokes)
        {
            for (int i = 1; i < stroke.Count; i++)
                total += Vector2.Distance(stroke[i - 1], stroke[i]);
        }

        return total;
    }

    private static List<List<Vector2>> CopyStrokes(List<List<Vector2>> strokes)
    {
        var copy = new List<List<Vector2>>(strokes.Count);
        foreach (var stroke in strokes)
            copy.Add(new List<Vector2>(stroke));
        return copy;
    }

    private static List<Vector2> FlattenStrokes(List<List<Vector2>> strokes)
    {
        var flat = new List<Vector2>();
        foreach (var stroke in strokes)
        {
            if (stroke == null)
                continue;

            for (int i = 0; i < stroke.Count; i++)
                flat.Add(stroke[i]);
        }
        return flat;
    }

    private static List<Vector2> NormalizeForComparison(List<Vector2> points, int targetCount, float squareSize)
    {
        points = Resample(points, targetCount);
        if (points.Count == 0)
            return points;

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minY = float.MaxValue;
        float maxY = float.MinValue;

        foreach (var point in points)
        {
            if (point.x < minX) minX = point.x;
            if (point.x > maxX) maxX = point.x;
            if (point.y < minY) minY = point.y;
            if (point.y > maxY) maxY = point.y;
        }

        float size = Mathf.Max(1f, Mathf.Max(maxX - minX, maxY - minY));
        var normalized = new List<Vector2>(points.Count);
        foreach (var point in points)
        {
            normalized.Add(new Vector2(
                (point.x - minX) / size * squareSize,
                (point.y - minY) / size * squareSize));
        }

        return normalized;
    }

    private static List<Vector2> Resample(List<Vector2> points, int targetCount)
    {
        if (points == null || points.Count == 0)
            return new List<Vector2>();

        if (points.Count == 1)
            return new List<Vector2> { points[0] };

        float pathLength = 0f;
        for (int i = 1; i < points.Count; i++)
            pathLength += Vector2.Distance(points[i - 1], points[i]);

        if (pathLength <= 0.001f)
            return new List<Vector2>(points);

        float interval = pathLength / (targetCount - 1);
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
            result.Add(source[source.Count - 1]);

        if (result.Count > targetCount)
            result.RemoveRange(targetCount, result.Count - targetCount);

        return result;
    }

    private static List<List<Vector2>> TransformRawToGuideSpace(
        List<TemplateLibrary.RawPoint> rawPoints,
        Vector2 renderSize)
    {
        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minY = float.MaxValue;
        float maxY = float.MinValue;

        foreach (var raw in rawPoints)
        {
            if (raw.x < minX) minX = raw.x;
            if (raw.x > maxX) maxX = raw.x;
            if (raw.y < minY) minY = raw.y;
            if (raw.y > maxY) maxY = raw.y;
        }

        float rangeX = Mathf.Max(1f, maxX - minX);
        float rangeY = Mathf.Max(1f, maxY - minY);
        float scale = Mathf.Min(renderSize.x / rangeX, renderSize.y / rangeY) * 0.85f;
        float centerX = (minX + maxX) * 0.5f;
        float centerY = (minY + maxY) * 0.5f;

        var groups = new SortedDictionary<int, List<Vector2>>();
        foreach (var raw in rawPoints)
        {
            if (!groups.TryGetValue(raw.strokeId, out var stroke))
            {
                stroke = new List<Vector2>();
                groups[raw.strokeId] = stroke;
            }

            stroke.Add(new Vector2(
                (raw.x - centerX) * scale,
                (raw.y - centerY) * scale));
        }

        return new List<List<Vector2>>(groups.Values);
    }

    private static Dictionary<char, List<TemplateLibrary.RawPoint>> BuildGuides()
    {
        return new Dictionary<char, List<TemplateLibrary.RawPoint>>
        {
            {
                'R',
                RawMulti(
                    (0, new [] { P(50f, 250f), P(50f, 0f) }),
                    (1, new [] { P(50f, 0f), P(175f, 30f), P(190f, 80f), P(175f, 130f), P(50f, 125f), P(200f, 250f) })
                )
            },
            {
                'A',
                RawMulti(
                    (0, new [] { P(50f, 0f), P(125f, 250f), P(200f, 0f) }),
                    (1, new [] { P(80f, 120f), P(170f, 120f) })
                )
            },
            {
                'T',
                RawMulti(
                    (0, new [] { P(30f, 0f), P(220f, 0f) }),
                    (1, new [] { P(125f, 0f), P(125f, 250f) })
                )
            }
        };
    }

    private static List<TemplateLibrary.RawPoint> FlipRawPointsVertically(
        List<TemplateLibrary.RawPoint> rawPoints)
    {
        if (rawPoints == null || rawPoints.Count == 0)
            return new List<TemplateLibrary.RawPoint>();

        float minY = float.MaxValue;
        float maxY = float.MinValue;

        foreach (var raw in rawPoints)
        {
            if (raw.y < minY) minY = raw.y;
            if (raw.y > maxY) maxY = raw.y;
        }

        var flipped = new List<TemplateLibrary.RawPoint>(rawPoints.Count);
        foreach (var raw in rawPoints)
        {
            float flippedY = maxY - (raw.y - minY);
            flipped.Add(new TemplateLibrary.RawPoint(raw.x, flippedY, raw.strokeId));
        }

        return flipped;
    }

    private static List<TemplateLibrary.RawPoint> CloneRawPoints(
        List<TemplateLibrary.RawPoint> rawPoints)
    {
        var cloned = new List<TemplateLibrary.RawPoint>(rawPoints.Count);
        foreach (var raw in rawPoints)
            cloned.Add(new TemplateLibrary.RawPoint(raw.x, raw.y, raw.strokeId));

        return cloned;
    }

    private static FeedbackManager.GuidanceState ToFeedbackState(GuidanceState guidanceState)
    {
        switch (guidanceState)
        {
            case GuidanceState.OnTrack:
                return FeedbackManager.GuidanceState.OnTrack;
            case GuidanceState.Drifting:
                return FeedbackManager.GuidanceState.Drifting;
            case GuidanceState.OffTrack:
                return FeedbackManager.GuidanceState.OffTrack;
            default:
                return FeedbackManager.GuidanceState.Hidden;
        }
    }

    private static Sprite circleSprite;

    private static Sprite GetCircleSprite()
    {
        if (circleSprite != null)
            return circleSprite;

        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float radius = size * 0.48f;
        float feather = size * 0.14f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                float alpha = Mathf.Clamp01(1f - Mathf.InverseLerp(radius - feather, radius, distance));
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply();
        circleSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        return circleSprite;
    }

    private static Vector2 P(float x, float y)
    {
        return new Vector2(x, y);
    }

    private static List<TemplateLibrary.RawPoint> RawMulti(
        params (int strokeId, Vector2[] points)[] strokes)
    {
        var list = new List<TemplateLibrary.RawPoint>();
        foreach (var stroke in strokes)
        {
            foreach (var point in stroke.points)
                list.Add(new TemplateLibrary.RawPoint(point.x, point.y, stroke.strokeId));
        }

        return list;
    }
}
