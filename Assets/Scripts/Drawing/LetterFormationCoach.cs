using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Online handwriting tutor for gameplay spell recording.
/// It watches the pencil path as ordered movement through canonical phases,
/// while P$ remains the final recognizer.
/// </summary>
public partial class LetterFormationCoach
{
    public enum FormationState
    {
        Hidden,
        OnTrack,
        NeedsNudge,
        NeedsHelp
    }

    public enum DiagnosticTag
    {
        None,
        MissingFoot,
        OpenLoop,
        ClosingTooSoon,
        TooStraight,
        Overdrawn,
        WrongStart,
        WrongDirection,
        StopHere,
        Hesitating,
        KeepGoing,
        TurnCorner,
        GenericShape
    }

    public enum PhaseKind
    {
        Line,
        Arc,
        Fallback
    }

    public struct FormationResult
    {
        public FormationState state;
        public DiagnosticTag diagnostic;
        public string hint;
        public Vector2 markerPosition;
        public bool hasMarker;
        public float confidence;
        public int phaseIndex;
    }

    public sealed class LetterFormationSpec
    {
        public char letter;
        public string displayName;
        public List<FormationPhase> phases = new List<FormationPhase>();
        public List<List<Vector2>> guide = new List<List<Vector2>>();
        public bool authored;
        public TemplateFormationSpec templateSpec;
    }

    public sealed class FormationPhase
    {
        public string name;
        public PhaseKind kind;
        public Vector2 expectedDirection;
        public float minProgress;
        public string activeHint;
        public string correctionHint;
        public DiagnosticTag correctionDiagnostic;
    }

    public sealed class FormationSession
    {
        public LetterFormationSpec spec;
        public int phaseIndex;
        public int mistakeCount;
        public DiagnosticTag lastDiagnostic = DiagnosticTag.None;
        public FormationResult lastResult;
        public float startedAt;
        public float lastMovementAt;
        public bool hasMoved;
    }

    struct StrokeFeatures
    {
        public List<Vector2> points;
        public Rect bounds;
        public float width;
        public float height;
        public float diagonal;
        public float pathLength;
        public float closure;
        public float angleSweep;
        public Vector2 firstDirection;
        public Vector2 lastDirection;
    }

    struct GuideEvaluation
    {
        public bool hasTarget;
        public float distance;
        public Vector2 target;
    }

    readonly FeedbackManager feedback;
    readonly TemplateLibrary templateLibrary;
    readonly RectTransform panel;
    Vector2 renderSize;
    readonly RectTransform overlayRoot;
    readonly StrokePreviewRenderer corridorRenderer;
    readonly StrokePreviewRenderer overlayRenderer;
    readonly RectTransform startMarkerRoot;
    readonly Image startMarkerGlow;
    readonly Image startMarkerCore;
    readonly RectTransform markerRoot;
    readonly Image markerGlow;
    readonly Image markerCore;
    readonly Dictionary<char, TemplateFormationSpec> compiledSpecs = new Dictionary<char, TemplateFormationSpec>();

    FormationSession session = new FormationSession();
    List<List<Vector2>> activeGuide = new List<List<Vector2>>();
    FormationState state = FormationState.Hidden;
    DiagnosticTag lastDiagnostic = DiagnosticTag.None;
    char activeLetter = '\0';
    float lastFeedbackAt;
    int lastPointCount;
    bool startMarkerEnabled;
    bool developerDiagnosticsVisible;

    const float CorridorInnerRadius = 28f;
    const float CorridorOuterRadius = 44f;
    const float CorridorHardRadius = 76f;
    const float FeedbackCooldown = 0.42f;
    const float TraceAlpha = 0.68f;
    const float InitialGuideAlpha = 0.30f;
    const float DemoAlpha = 0.48f;
    const float HesitationSeconds = 1.35f;

    public LetterFormationCoach(
        RectTransform drawingPanel,
        FeedbackManager feedbackManager,
        Vector2 desiredRenderSize,
        TemplateLibrary library = null,
        float guideStrokeThickness = 9f)
    {
        panel = drawingPanel;
        feedback = feedbackManager;
        templateLibrary = library;
        renderSize = desiredRenderSize;
        if (templateLibrary != null)
            templateLibrary.OnLibraryChanged += compiledSpecs.Clear;

        var overlayGo = new GameObject("LetterFormationOverlay", typeof(RectTransform));
        overlayRoot = overlayGo.GetComponent<RectTransform>();
        overlayRoot.SetParent(panel, false);
        overlayRoot.anchorMin = new Vector2(0.5f, 0.5f);
        overlayRoot.anchorMax = new Vector2(0.5f, 0.5f);
        overlayRoot.pivot = new Vector2(0.5f, 0.5f);
        overlayRoot.sizeDelta = renderSize;

        var corridorGo = new GameObject("Corridor", typeof(RectTransform));
        RectTransform corridorRt = corridorGo.GetComponent<RectTransform>();
        corridorRt.SetParent(overlayRoot, false);
        corridorRt.anchorMin = Vector2.zero;
        corridorRt.anchorMax = Vector2.one;
        corridorRt.offsetMin = Vector2.zero;
        corridorRt.offsetMax = Vector2.zero;
        corridorRenderer = corridorGo.AddComponent<StrokePreviewRenderer>();
        corridorRenderer.strokeColour = new Color(0.15f, 0.85f, 0.35f, 0.32f);
        corridorRenderer.strokeThickness = CorridorOuterRadius * 2f;
        corridorRenderer.useRoundBrushRendering = true;

        var rendererGo = new GameObject("Renderer", typeof(RectTransform));
        RectTransform rendererRt = rendererGo.GetComponent<RectTransform>();
        rendererRt.SetParent(overlayRoot, false);
        rendererRt.anchorMin = Vector2.zero;
        rendererRt.anchorMax = Vector2.one;
        rendererRt.offsetMin = Vector2.zero;
        rendererRt.offsetMax = Vector2.zero;
        overlayRenderer = rendererGo.AddComponent<StrokePreviewRenderer>();
        overlayRenderer.strokeColour = new Color(0.55f, 0.9f, 1f, DemoAlpha);
        overlayRenderer.strokeThickness = Mathf.Max(1f, guideStrokeThickness);

        var startGo = new GameObject("LetterFormationStartMarker", typeof(RectTransform));
        startMarkerRoot = startGo.GetComponent<RectTransform>();
        startMarkerRoot.SetParent(overlayRoot, false);
        startMarkerRoot.anchorMin = new Vector2(0.5f, 0.5f);
        startMarkerRoot.anchorMax = new Vector2(0.5f, 0.5f);
        startMarkerRoot.pivot = new Vector2(0.5f, 0.5f);

        startMarkerGlow = CreateMarkerImage(startMarkerRoot, "Glow", 46f);
        startMarkerCore = CreateMarkerImage(startMarkerRoot, "Core", 16f);

        var markerGo = new GameObject("LetterFormationMarker", typeof(RectTransform));
        markerRoot = markerGo.GetComponent<RectTransform>();
        markerRoot.SetParent(overlayRoot, false);
        markerRoot.anchorMin = new Vector2(0.5f, 0.5f);
        markerRoot.anchorMax = new Vector2(0.5f, 0.5f);
        markerRoot.pivot = new Vector2(0.5f, 0.5f);

        markerGlow = CreateMarkerImage(markerRoot, "Glow", 44f);
        markerCore = CreateMarkerImage(markerRoot, "Core", 14f);

        Hide();
    }

    public bool HasAuthoredSpec => session?.spec?.templateSpec != null && session.spec.templateSpec.primitives.Count > 0;
    public bool IsOverlayVisible => overlayRoot != null && overlayRoot.gameObject.activeSelf;
    public int CurrentPhaseIndex => session != null ? session.phaseIndex : 0;
    public FormationResult LastResult => session != null ? session.lastResult : HiddenResult();

    public void SetDeveloperDiagnosticsVisible(bool visible)
    {
        if (developerDiagnosticsVisible == visible)
            return;

        developerDiagnosticsVisible = visible;
        if (developerDiagnosticsVisible)
            ShowDeveloperOverlay(LastResult);
        else if (state == FormationState.Hidden)
            HideVisual(true);
        else
            UpdateVisuals(LastResult, 0);
    }

    public void SetGuideFrame(Vector2 center, Vector2 size)
    {
        Vector2 nextSize = new Vector2(Mathf.Max(24f, size.x), Mathf.Max(24f, size.y));
        if ((nextSize - renderSize).sqrMagnitude > 1f)
        {
            renderSize = nextSize;
            compiledSpecs.Clear();
        }

        if (overlayRoot != null)
        {
            overlayRoot.anchoredPosition = center;
            overlayRoot.sizeDelta = renderSize;
        }
    }

    public void BeginLetter(char letter)
    {
        activeLetter = char.ToUpperInvariant(letter);
        state = FormationState.Hidden;
        lastDiagnostic = DiagnosticTag.None;
        lastPointCount = 0;

        LetterFormationSpec spec = ResolveSpec(activeLetter);
        activeGuide = spec.guide != null
            ? FitGuideToFrame(CopyStrokes(spec.guide), renderSize)
            : new List<List<Vector2>>();
        startMarkerEnabled = false;
        session = new FormationSession
        {
            spec = spec,
            phaseIndex = 0,
            startedAt = Time.unscaledTime,
            lastMovementAt = Time.unscaledTime,
            lastResult = HiddenResult()
        };

        HideVisual(true);
    }

    public void SetStartMarkerEnabled(bool enabled)
    {
        startMarkerEnabled = enabled;
        if (!startMarkerEnabled)
        {
            HideStartMarker();
        }
    }

    public void ShowInitialGuide()
    {
        if (activeGuide.Count == 0)
            return;

        overlayRoot.gameObject.SetActive(true);
        overlayRenderer.strokeColour = new Color(0.55f, 0.9f, 1f, InitialGuideAlpha);
        overlayRenderer.SetFromLocalStrokes(activeGuide);
        overlayRenderer.SetOpacity(InitialGuideAlpha);
        ShowStartMarker();
        HideMarker();
    }

    public FormationResult UpdateStroke(
        List<List<Vector2>> strokes,
        List<GameObject> strokeVisuals,
        int attemptsUsed,
        int helpLevel)
    {
        if (activeLetter == '\0' || session == null || session.spec == null)
            return HiddenResult();

        int pointCount = CountPoints(strokes);
        if (pointCount > lastPointCount)
        {
            session.hasMoved = true;
            session.lastMovementAt = Time.unscaledTime;
            lastPointCount = pointCount;
        }

        List<List<Vector2>> guideSpaceStrokes = ToGuideSpaceStrokes(strokes);
        FormationResult result = AnalyzeSession(session, guideSpaceStrokes);
        result = ApplyQuietFirstAttempt(result, attemptsUsed, helpLevel);
        result = ApplyCorridorFeedback(result, guideSpaceStrokes, helpLevel);

        bool shouldCue = result.state > state ||
                         (result.state >= FormationState.NeedsNudge &&
                          result.diagnostic != lastDiagnostic &&
                          Time.unscaledTime - lastFeedbackAt >= FeedbackCooldown);

        if (shouldCue)
        {
            feedback?.PlayGuidanceFeedback(ToFeedbackState(result.state), strokeVisuals);
            lastFeedbackAt = Time.unscaledTime;
        }

        state = result.state;
        lastDiagnostic = result.diagnostic;
        session.lastDiagnostic = result.diagnostic;
        session.lastResult = result;
        UpdateVisuals(result, helpLevel);
        return result;
    }

    public FormationResult Tick(List<GameObject> strokeVisuals, int helpLevel)
    {
        if (activeLetter == '\0' || session == null || !session.hasMoved)
            return HiddenResult();

        if (Time.unscaledTime - session.lastMovementAt < HesitationSeconds)
            return session.lastResult;

        FormationResult result = NeedsNudge(
            NextStepHint(),
            DiagnosticTag.Hesitating,
            CurrentPhaseMarker());
        result.phaseIndex = session.phaseIndex;
        result.confidence = 0.7f;
        result = ApplyHelpOverlay(result, helpLevel);

        if (result.diagnostic != lastDiagnostic &&
            Time.unscaledTime - lastFeedbackAt >= FeedbackCooldown)
        {
            feedback?.PlayGuidanceFeedback(FeedbackManager.GuidanceState.Drifting, strokeVisuals);
            lastFeedbackAt = Time.unscaledTime;
        }

        state = result.state;
        lastDiagnostic = result.diagnostic;
        session.lastResult = result;
        return result;
    }

    public FormationResult ShowHelp(int helpLevel)
    {
        FormationResult result = new FormationResult
        {
            state = helpLevel >= 2 ? FormationState.NeedsHelp : FormationState.NeedsNudge,
            diagnostic = DiagnosticTag.KeepGoing,
            hint = helpLevel >= 2
                ? $"Trace the shape for '{activeLetter}', then try it in your own size."
                : $"Watch the shape for '{activeLetter}', then draw it your way.",
            confidence = 1f,
            phaseIndex = session != null ? session.phaseIndex : 0
        };

        if (helpLevel >= 2)
            ShowTraceOverlay();
        else
            HideMarker();

        return result;
    }

    public void ShowAnimatedDemo(float progress)
    {
        if (activeGuide.Count == 0)
            return;

        List<List<Vector2>> partial = BuildPartialGuide(Mathf.Clamp01(progress));
        overlayRoot.gameObject.SetActive(true);
        ShowCorridor(FormationState.OnTrack);
        overlayRenderer.strokeColour = new Color(0.55f, 0.9f, 1f, DemoAlpha);
        overlayRenderer.SetFromLocalStrokes(partial);
        overlayRenderer.SetOpacity(DemoAlpha);

        if (TryGetLastPoint(partial, out Vector2 tip))
            ShowMarker(tip, FormationState.OnTrack);

        ShowStartMarker();
    }

    public void ShowTraceOverlay()
    {
        if (activeGuide.Count == 0)
            return;

        overlayRoot.gameObject.SetActive(true);
        ShowCorridor(FormationState.OnTrack);
        overlayRenderer.strokeColour = new Color(0.55f, 0.9f, 1f, TraceAlpha);
        overlayRenderer.SetFromLocalStrokes(activeGuide);
        overlayRenderer.SetOpacity(TraceAlpha);
        ShowStartMarker();
    }

    public void ShowCorridorOverlay()
    {
        if (activeGuide.Count == 0)
            return;

        overlayRoot.gameObject.SetActive(true);
        ShowCorridor(FormationState.OnTrack);
        ShowStartMarker();
    }

    public bool TryAdjustStrokePoint(
        Vector2 rawPoint,
        Vector2 previousPoint,
        bool isStrokeStart,
        out Vector2 adjustedPoint)
    {
        adjustedPoint = rawPoint;
        if (activeGuide.Count == 0)
            return false;

        Vector2 rawGuidePoint = PanelToGuideSpace(rawPoint);
        Vector2 previousGuidePoint = PanelToGuideSpace(previousPoint);
        GuideEvaluation rawEval = EvaluateAgainstCorridor(rawGuidePoint);
        GuideEvaluation previousEval = EvaluateAgainstCorridor(previousGuidePoint);
        if (!rawEval.hasTarget)
            return false;

        float drift = Mathf.InverseLerp(CorridorInnerRadius, CorridorHardRadius, rawEval.distance);
        if (drift <= 0f)
            return false;

        bool movingAway = rawEval.distance > previousEval.distance + 1f;
        float slowFactor = movingAway
            ? Mathf.Lerp(1f, 0.55f, drift)
            : Mathf.Lerp(1f, 0.82f, drift * 0.75f);

        Vector2 candidate = previousGuidePoint + (rawGuidePoint - previousGuidePoint) * slowFactor;
        float pullStrength = isStrokeStart
            ? Mathf.Lerp(0.04f, 0.26f, drift)
            : Mathf.Lerp(0.03f, 0.20f, drift);

        if (movingAway)
            pullStrength = Mathf.Min(0.32f, pullStrength + drift * 0.08f);

        adjustedPoint = GuideToPanelSpace(Vector2.Lerp(candidate, rawEval.target, pullStrength));
        return true;
    }

    public void CompleteLetter()
    {
        state = FormationState.Hidden;
        lastDiagnostic = DiagnosticTag.None;
        HideVisual(true);
    }

    public void Hide()
    {
        activeLetter = '\0';
        activeGuide.Clear();
        state = FormationState.Hidden;
        lastDiagnostic = DiagnosticTag.None;
        lastPointCount = 0;
        session = new FormationSession();
        HideVisual(true);
    }

    public void HideVisual()
    {
        HideVisual(false);
    }

    void HideVisual(bool force)
    {
        if (!force && developerDiagnosticsVisible && activeLetter != '\0' && activeGuide.Count > 0)
        {
            ShowDeveloperOverlay(LastResult);
            return;
        }

        if (overlayRenderer != null)
            overlayRenderer.Clear();
        HideCorridor();
        if (overlayRoot != null)
            overlayRoot.gameObject.SetActive(false);
        HideStartMarker();
        HideMarker();
    }

    List<List<Vector2>> BuildPartialGuide(float progress)
    {
        if (activeGuide.Count == 0)
            return new List<List<Vector2>>();

        if (progress <= 0f)
            return new List<List<Vector2>> { new List<Vector2> { activeGuide[0][0] } };

        float totalLength = TotalLength(activeGuide);
        if (totalLength <= 0.001f || progress >= 1f)
            return CopyStrokes(activeGuide);

        float targetLength = totalLength * progress;
        float covered = 0f;
        var partial = new List<List<Vector2>>();
        foreach (List<Vector2> stroke in activeGuide)
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

    static float TotalLength(List<List<Vector2>> strokes)
    {
        float total = 0f;
        foreach (List<Vector2> stroke in strokes)
            for (int i = 1; i < stroke.Count; i++)
                total += Vector2.Distance(stroke[i - 1], stroke[i]);
        return total;
    }

    static List<Vector2> FlattenStrokes(List<List<Vector2>> strokes)
    {
        var flat = new List<Vector2>();
        if (strokes == null)
            return flat;

        foreach (List<Vector2> stroke in strokes)
        {
            if (stroke == null)
                continue;
            flat.AddRange(stroke);
        }

        return flat;
    }

    static List<List<Vector2>> CopyStrokes(List<List<Vector2>> strokes)
    {
        var copy = new List<List<Vector2>>(strokes.Count);
        foreach (List<Vector2> stroke in strokes)
            copy.Add(stroke != null ? new List<Vector2>(stroke) : new List<Vector2>());
        return copy;
    }

    static List<List<Vector2>> FitGuideToFrame(List<List<Vector2>> strokes, Vector2 frameSize)
    {
        Rect bounds = CalculateBounds(strokes);
        if (bounds.width <= 0.001f || bounds.height <= 0.001f)
            return strokes;

        float targetHeight = Mathf.Max(1f, frameSize.y * 0.96f);
        float maxWidth = Mathf.Max(1f, frameSize.x * 0.94f);
        float scale = targetHeight / bounds.height;
        if (bounds.width * scale > maxWidth)
            scale = maxWidth / bounds.width;

        Vector2 center = bounds.center;
        var fitted = new List<List<Vector2>>(strokes.Count);
        foreach (List<Vector2> stroke in strokes)
        {
            if (stroke == null || stroke.Count == 0)
                continue;

            var fittedStroke = new List<Vector2>(stroke.Count);
            foreach (Vector2 point in stroke)
                fittedStroke.Add((point - center) * scale);
            fitted.Add(fittedStroke);
        }

        return fitted;
    }

    static Rect CalculateBounds(List<List<Vector2>> strokes)
    {
        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minY = float.MaxValue;
        float maxY = float.MinValue;
        bool hasPoint = false;

        if (strokes != null)
        {
            foreach (List<Vector2> stroke in strokes)
            {
                if (stroke == null)
                    continue;

                foreach (Vector2 point in stroke)
                {
                    hasPoint = true;
                    if (point.x < minX) minX = point.x;
                    if (point.x > maxX) maxX = point.x;
                    if (point.y < minY) minY = point.y;
                    if (point.y > maxY) maxY = point.y;
                }
            }
        }

        return hasPoint ? Rect.MinMaxRect(minX, minY, maxX, maxY) : new Rect();
    }

    static LetterFormationSpec CloneSpec(LetterFormationSpec source)
    {
        var clone = new LetterFormationSpec
        {
            letter = source.letter,
            displayName = source.displayName,
            authored = source.authored,
            guide = CopyStrokes(source.guide),
            phases = new List<FormationPhase>()
        };

        foreach (FormationPhase phase in source.phases)
        {
            clone.phases.Add(new FormationPhase
            {
                name = phase.name,
                kind = phase.kind,
                expectedDirection = phase.expectedDirection,
                minProgress = phase.minProgress,
                activeHint = phase.activeHint,
                correctionHint = phase.correctionHint,
                correctionDiagnostic = phase.correctionDiagnostic
            });
        }

        return clone;
    }

    static List<Vector2> NormalizeForComparison(List<Vector2> points, int targetCount, float squareSize)
    {
        points = Resample(points, targetCount);
        if (points.Count == 0)
            return points;

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minY = float.MaxValue;
        float maxY = float.MinValue;
        foreach (Vector2 point in points)
        {
            if (point.x < minX) minX = point.x;
            if (point.x > maxX) maxX = point.x;
            if (point.y < minY) minY = point.y;
            if (point.y > maxY) maxY = point.y;
        }

        float size = Mathf.Max(1f, Mathf.Max(maxX - minX, maxY - minY));
        var normalized = new List<Vector2>(points.Count);
        foreach (Vector2 point in points)
            normalized.Add(new Vector2((point.x - minX) / size * squareSize,
                                       (point.y - minY) / size * squareSize));
        return normalized;
    }

    static List<Vector2> Resample(List<Vector2> points, int targetCount)
    {
        if (points == null || points.Count == 0)
            return new List<Vector2>();
        if (points.Count == 1)
            return new List<Vector2> { points[0] };

        float pathLength = PathLength(points);
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
            result.Add(source[^1]);
        if (result.Count > targetCount)
            result.RemoveRange(targetCount, result.Count - targetCount);
        return result;
    }

}
