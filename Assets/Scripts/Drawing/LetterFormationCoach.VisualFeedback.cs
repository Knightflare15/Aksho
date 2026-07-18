using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public partial class LetterFormationCoach
{
    FormationResult ApplyCorridorFeedback(FormationResult result, List<List<Vector2>> strokes, int helpLevel)
    {
        if (!TryGetLastPoint(strokes, out Vector2 livePoint))
            return result;

        GuideEvaluation evaluation = EvaluateAgainstCorridor(livePoint);
        if (!evaluation.hasTarget)
            return result;

        FormationState corridorState = CorridorStateForDistance(evaluation.distance);
        HideCorridor();

        if (corridorState == FormationState.OnTrack &&
            (result.state == FormationState.Hidden || result.state > FormationState.OnTrack))
        {
            FormationResult onTrack = OnTrack($"Keep tracing '{activeLetter}'.", DiagnosticTag.KeepGoing, evaluation.target);
            onTrack.phaseIndex = result.phaseIndex;
            onTrack.confidence = Mathf.Max(result.confidence, 0.86f);
            return onTrack;
        }

        if (corridorState == FormationState.NeedsNudge && result.state == FormationState.NeedsHelp)
        {
            FormationResult nudge = NeedsNudge($"Stay close to the shape for '{activeLetter}'.", DiagnosticTag.GenericShape, evaluation.target);
            nudge.phaseIndex = result.phaseIndex;
            nudge.confidence = Mathf.Max(result.confidence, 0.78f);
            return nudge;
        }

        if (corridorState <= result.state)
            return result;

        if (corridorState == FormationState.NeedsNudge)
            return NeedsNudge($"Stay close to the shape for '{activeLetter}'.", DiagnosticTag.GenericShape, evaluation.target);

        return NeedsHelp($"Bring it back toward '{activeLetter}'.", DiagnosticTag.GenericShape, evaluation.target);
    }

    FormationResult ApplyHelpOverlay(FormationResult result, int helpLevel)
    {
        UpdateVisuals(result, helpLevel);
        return result;
    }

    void UpdateVisuals(FormationResult result, int helpLevel)
    {
        if (developerDiagnosticsVisible)
        {
            ShowDeveloperOverlay(result);
            return;
        }

        if (helpLevel >= 2)
            ShowTraceOverlay();
        else if (helpLevel == 0 && result.state < FormationState.NeedsHelp)
            HideVisual();

        if (result.hasMarker && (helpLevel > 0 || result.state >= FormationState.NeedsHelp))
            ShowMarker(result.markerPosition, result.state);
        else if (helpLevel < 2)
            HideMarker();
    }

    LetterFormationSpec ResolveSpec(char letter)
    {
        if (!compiledSpecs.TryGetValue(letter, out TemplateFormationSpec compiled))
        {
            compiled = TemplateFormationCompiler.Compile(letter, templateLibrary, renderSize);
            compiledSpecs[letter] = compiled;
        }

        return ToLetterFormationSpec(compiled);
    }

    string NextStepHint()
    {
        if (session?.spec == null || session.spec.phases == null || session.spec.phases.Count == 0)
            return $"Keep shaping '{activeLetter}'.";

        int index = Mathf.Clamp(session.phaseIndex, 0, session.spec.phases.Count - 1);
        FormationPhase phase = session.spec.phases[index];
        return string.IsNullOrEmpty(phase.activeHint)
            ? $"Keep shaping '{activeLetter}'."
            : phase.activeHint;
    }

    Vector2 CurrentPhaseMarker()
    {
        if (session?.spec?.templateSpec?.primitives != null &&
            session.spec.templateSpec.primitives.Count > 0)
        {
            int primitiveIndex = Mathf.Clamp(session.phaseIndex, 0, session.spec.templateSpec.primitives.Count - 1);
            return session.spec.templateSpec.primitives[primitiveIndex].end;
        }

        if (activeGuide.Count == 0)
            return Vector2.zero;

        float progress = session != null && session.spec != null && session.spec.phases.Count > 1
            ? (session.phaseIndex + 0.55f) / session.spec.phases.Count
            : 0.65f;
        List<List<Vector2>> partial = BuildPartialGuide(Mathf.Clamp01(progress));
        return TryGetLastPoint(partial, out Vector2 point) ? point : Vector2.zero;
    }

    static LetterFormationSpec ToLetterFormationSpec(TemplateFormationSpec compiled)
    {
        var spec = new LetterFormationSpec
        {
            letter = compiled != null ? compiled.letter : '?',
            displayName = compiled != null ? compiled.letter.ToString() : "?",
            authored = compiled != null && compiled.primitives != null && compiled.primitives.Count > 0,
            guide = compiled != null ? CopyStrokes(compiled.guideStrokes) : new List<List<Vector2>>(),
            templateSpec = compiled
        };

        if (compiled?.primitives != null)
        {
            foreach (FormationPrimitive primitive in compiled.primitives)
            {
                spec.phases.Add(new FormationPhase
                {
                    name = primitive.kind.ToString(),
                    kind = primitive.kind == FormationPrimitiveKind.Line
                        ? PhaseKind.Line
                        : primitive.kind == FormationPrimitiveKind.Curve || primitive.kind == FormationPrimitiveKind.Loop
                            ? PhaseKind.Arc
                            : PhaseKind.Fallback,
                    expectedDirection = primitive.end - primitive.start,
                    minProgress = 0.75f,
                    activeHint = HintForPrimitive(compiled.letter, primitive),
                    correctionHint = HintForMissingNext(compiled.letter, primitive),
                    correctionDiagnostic = primitive.kind == FormationPrimitiveKind.Loop
                        ? DiagnosticTag.OpenLoop
                        : primitive.kind == FormationPrimitiveKind.Line
                            ? DiagnosticTag.WrongDirection
                            : DiagnosticTag.KeepGoing
                });
            }
        }

        if (spec.phases.Count == 0)
        {
            spec.phases.Add(new FormationPhase
            {
                name = "shape",
                kind = PhaseKind.Fallback,
                activeHint = $"Shape '{spec.letter}'.",
                correctionHint = $"Shape this into '{spec.letter}'.",
                correctionDiagnostic = DiagnosticTag.GenericShape
            });
        }

        return spec;
    }

    static string HintForPrimitive(char letter, FormationPrimitive primitive)
    {
        if (primitive == null)
            return $"Keep shaping '{letter}'.";

        if (primitive.kind == FormationPrimitiveKind.Line)
        {
            if (primitive.axis == FormationAxis.Vertical)
                return $"Make the vertical line for '{letter}'.";
            if (primitive.axis == FormationAxis.Horizontal)
                return $"Draw across for '{letter}'.";
            return $"Make the diagonal line for '{letter}'.";
        }

        if (primitive.kind == FormationPrimitiveKind.Loop)
            return $"Round it back and close '{letter}'.";

        if (primitive.kind == FormationPrimitiveKind.Curve)
            return $"Keep curving for '{letter}'.";

        return $"Keep shaping '{letter}'.";
    }

    static string HintForMissingNext(char letter, FormationPrimitive primitive)
    {
        if (primitive == null)
            return $"Finish '{letter}'.";

        if (primitive.kind == FormationPrimitiveKind.Line)
        {
            if (primitive.axis == FormationAxis.Horizontal)
                return $"Add the line across for '{letter}'.";
            if (primitive.axis == FormationAxis.Vertical)
                return $"Add the vertical line for '{letter}'.";
            return $"Add the diagonal line for '{letter}'.";
        }

        if (primitive.kind == FormationPrimitiveKind.Loop)
            return $"Close the loop for '{letter}'.";

        if (primitive.kind == FormationPrimitiveKind.Curve)
            return $"Finish the curve for '{letter}'.";

        return $"Finish '{letter}'.";
    }

    void ShowCorridor(FormationState corridorState)
    {
        if (!developerDiagnosticsVisible || activeGuide.Count == 0)
        {
            HideCorridor();
            return;
        }

        if (overlayRoot != null)
            overlayRoot.gameObject.SetActive(true);

        if (corridorRenderer == null)
            return;

        Color color = corridorState == FormationState.NeedsHelp
            ? new Color(1f, 0.45f, 0.25f, 0.30f)
            : corridorState == FormationState.NeedsNudge
                ? new Color(1f, 0.86f, 0.20f, 0.26f)
                : new Color(0.15f, 0.85f, 0.35f, 0.22f);
        corridorRenderer.strokeColour = color;
        corridorRenderer.strokeThickness = CorridorOuterRadius * 2f;
        corridorRenderer.SetFromLocalStrokes(activeGuide);
        corridorRenderer.SetOpacity(color.a);
    }

    void HideCorridor()
    {
        if (corridorRenderer != null)
            corridorRenderer.Clear();
    }

    void ShowDeveloperOverlay(FormationResult result)
    {
        if (overlayRoot == null || activeGuide.Count == 0)
            return;

        FormationState overlayState = result.state == FormationState.Hidden
            ? FormationState.OnTrack
            : result.state;

        overlayRoot.gameObject.SetActive(true);
        ShowCorridor(overlayState);

        if (overlayRenderer != null)
        {
            const float alpha = 0.42f;
            overlayRenderer.strokeColour = new Color(0.55f, 0.90f, 1f, alpha);
            overlayRenderer.SetFromLocalStrokes(activeGuide);
            overlayRenderer.SetOpacity(alpha);
        }

        ShowStartMarker();
        if (result.hasMarker)
            ShowMarker(result.markerPosition, overlayState);
        else
            HideMarker();
    }

    FormationState CorridorStateForDistance(float distance)
    {
        if (distance <= CorridorInnerRadius)
            return FormationState.OnTrack;
        return distance <= CorridorOuterRadius
            ? FormationState.NeedsNudge
            : FormationState.NeedsHelp;
    }

    Vector2 PanelToGuideSpace(Vector2 panelPoint)
    {
        return overlayRoot != null
            ? panelPoint - overlayRoot.anchoredPosition
            : panelPoint;
    }

    Vector2 GuideToPanelSpace(Vector2 guidePoint)
    {
        return overlayRoot != null
            ? guidePoint + overlayRoot.anchoredPosition
            : guidePoint;
    }

    List<List<Vector2>> ToGuideSpaceStrokes(List<List<Vector2>> strokes)
    {
        if (strokes == null || strokes.Count == 0)
            return new List<List<Vector2>>();

        Vector2 offset = overlayRoot != null ? overlayRoot.anchoredPosition : Vector2.zero;
        var transformed = new List<List<Vector2>>(strokes.Count);
        foreach (List<Vector2> stroke in strokes)
        {
            if (stroke == null)
            {
                transformed.Add(new List<Vector2>());
                continue;
            }

            var transformedStroke = new List<Vector2>(stroke.Count);
            foreach (Vector2 point in stroke)
                transformedStroke.Add(point - offset);
            transformed.Add(transformedStroke);
        }

        return transformed;
    }

    GuideEvaluation EvaluateAgainstCorridor(Vector2 point)
    {
        GuideEvaluation evaluation = new GuideEvaluation
        {
            hasTarget = false,
            distance = float.MaxValue,
            target = point
        };

        foreach (List<Vector2> stroke in activeGuide)
        {
            if (stroke == null || stroke.Count == 0)
                continue;

            if (stroke.Count == 1)
            {
                float distance = Vector2.Distance(point, stroke[0]);
                if (distance < evaluation.distance)
                {
                    evaluation.hasTarget = true;
                    evaluation.distance = distance;
                    evaluation.target = stroke[0];
                }
                continue;
            }

            for (int i = 1; i < stroke.Count; i++)
            {
                Vector2 sample = ClosestPointOnSegment(point, stroke[i - 1], stroke[i]);
                float distance = Vector2.Distance(point, sample);
                if (distance >= evaluation.distance)
                    continue;

                evaluation.hasTarget = true;
                evaluation.distance = distance;
                evaluation.target = sample;
            }
        }

        return evaluation;
    }

    static Vector2 ClosestPointOnSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float denom = ab.sqrMagnitude;
        if (denom <= 0.001f)
            return a;

        float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / denom);
        return a + ab * t;
    }

    void ShowMarker(Vector2 position, FormationState markerState)
    {
        if (markerRoot == null)
            return;

        markerRoot.gameObject.SetActive(true);
        markerRoot.anchoredPosition = position;
        Color color = markerState == FormationState.OnTrack
            ? new Color(GameUiTheme.Accent.r, GameUiTheme.Accent.g, GameUiTheme.Accent.b, 0.82f)
            : markerState == FormationState.NeedsNudge
                ? new Color(GameUiTheme.Gold.r, GameUiTheme.Gold.g, GameUiTheme.Gold.b, 0.88f)
                : new Color(1f, 0.5f, 0.38f, 0.92f);

        markerGlow.color = new Color(color.r, color.g, color.b, 0.32f);
        markerCore.color = color;
    }

    void ShowStartMarker()
    {
        if (!startMarkerEnabled || startMarkerRoot == null || !TryGetFirstGuidePoint(out Vector2 position))
            return;

        startMarkerRoot.gameObject.SetActive(true);
        startMarkerRoot.anchoredPosition = position;

        float pulse = 1f + Mathf.Sin(Time.unscaledTime * 7.5f) * 0.08f;
        startMarkerGlow.rectTransform.sizeDelta = Vector2.one * 46f * pulse;
        startMarkerCore.rectTransform.sizeDelta = Vector2.one * 16f * pulse;
        startMarkerGlow.color = new Color(GameUiTheme.Accent.r, GameUiTheme.Accent.g, GameUiTheme.Accent.b, 0.38f);
        startMarkerCore.color = new Color(0.92f, 1f, 0.96f, 0.96f);
    }

    void HideStartMarker()
    {
        if (startMarkerRoot != null)
            startMarkerRoot.gameObject.SetActive(false);
    }

    void HideMarker()
    {
        if (markerRoot != null)
            markerRoot.gameObject.SetActive(false);
    }

    bool TryGetFirstGuidePoint(out Vector2 point)
    {
        foreach (List<Vector2> stroke in activeGuide)
        {
            if (stroke != null && stroke.Count > 0)
            {
                point = stroke[0];
                return true;
            }
        }

        point = Vector2.zero;
        return false;
    }

    static Image CreateMarkerImage(RectTransform parent, string name, float size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = Vector2.one * size;
        Image image = go.GetComponent<Image>();
        image.sprite = CircleSprite();
        image.raycastTarget = false;
        return image;
    }

    static Sprite circleSprite;

    static Sprite CircleSprite()
    {
        if (circleSprite != null)
            return circleSprite;

        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
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
}
