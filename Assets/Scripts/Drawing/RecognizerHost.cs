using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Thin MonoBehaviour wrapper around PDollarRecognizer.
///
/// Keeps one shared recogniser instance alive across scenes.
/// Call ReloadFromLibrary() to merge custom templates on top of
/// the built-in geometric alphabet.
///
/// Attach this to the same GameObject as TemplateLibrary and DrawController.
/// Drag the reference into DrawController and TemplateRecorderUI.
/// </summary>
public class RecognizerHost : MonoBehaviour
{
    const float DefaultCalibratedThreshold = 420f;
    const float MinimumCalibratedThreshold = 180f;
    const float MaximumCalibratedThreshold = 460f;
    [Header("Settings")]
    [Tooltip("Load built-in geometric A-Z templates on startup as a fallback baseline")]
    public bool loadBuiltinTemplates = true;
    [Tooltip("Use the on-device image letter CNN as a review signal for P$ recognition.")]
    public bool enableTinyNeuralRecognizer = true;

    // ── Shared recogniser ──────────────────────────────────────────────────
    public PDollarRecognizer Recognizer { get; private set; }
    public TinyLetterNeuralRecognizer NeuralRecognizer { get; private set; }
    public PretrainedLetterCnnRecognizer LetterCnnRecognizer { get; private set; }
    readonly Dictionary<char, float> calibratedThresholds = new Dictionary<char, float>();
    readonly Dictionary<char, float> calibrationSeparations = new Dictionary<char, float>();
    readonly Dictionary<char, bool> calibrationReliability = new Dictionary<char, bool>();

    // ── Unity lifecycle ────────────────────────────────────────────────────

    void Awake()
    {
        Recognizer = new PDollarRecognizer();

        if (loadBuiltinTemplates)
            Recognizer.LoadDefaultAlphabetTemplates();

        if (enableTinyNeuralRecognizer)
        {
            LetterCnnRecognizer = PretrainedLetterCnnRecognizer.CreateDefault();
            NeuralRecognizer = TinyLetterNeuralRecognizer.CreateDefault();
        }

        // Load custom templates from disk immediately
        var lib = GetComponent<TemplateLibrary>();
        if (lib != null)
            ReloadFromLibrary(lib);
        else
            RebuildLetterCalibration();
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Rebuild the recogniser's template list:
    ///   1. Start fresh (or re-seed with builtins if enabled)
    ///   2. Add every entry from the library on top
    ///
    /// Custom samples take precedence in matching because PDollar picks the
    /// single best match — more diverse custom samples = better coverage.
    /// </summary>
    public void ReloadFromLibrary(TemplateLibrary library)
    {
        Recognizer = new PDollarRecognizer();
        if (enableTinyNeuralRecognizer && LetterCnnRecognizer == null)
            LetterCnnRecognizer = PretrainedLetterCnnRecognizer.CreateDefault();
        if (enableTinyNeuralRecognizer && NeuralRecognizer == null)
            NeuralRecognizer = TinyLetterNeuralRecognizer.CreateDefault();

        var templates = library.ToRecognizerTemplates();

        if (loadBuiltinTemplates)
            Recognizer.LoadDefaultAlphabetTemplates();

        foreach (var (name, pts) in templates)
            Recognizer.AddTemplate(name, pts);

        RebuildLetterCalibration();

        Debug.Log($"[RecognizerHost] Loaded {templates.Count} custom templates");
    }

    /// <summary>Convenience passthrough for DrawController.</summary>
    public PDollarRecognizer.RecognitionResult Recognize(List<PDollarRecognizer.Point> points)
        => FuseRecognition(points, Recognizer.Recognize(points), PDollarRecognizer.SCORE_THRESHOLD);

    public PDollarRecognizer.RecognitionResult Recognize(
        List<PDollarRecognizer.Point> points,
        List<List<Vector2>> imageStrokes,
        Rect panelRect,
        float strokeWidth)
    {
        return FuseRecognition(
            points,
            Recognizer.Recognize(points),
            PDollarRecognizer.SCORE_THRESHOLD,
            '\0',
            imageStrokes);
    }

    public PDollarRecognizer.RecognitionResult RecognizeAsLetter(
        List<PDollarRecognizer.Point> points,
        char expectedLetter,
        float scoreThreshold)
    {
        return RecognizeAsLetter(points, expectedLetter, scoreThreshold, null, default, 0f);
    }

    public PDollarRecognizer.RecognitionResult RecognizeAsLetter(
        List<PDollarRecognizer.Point> points,
        char expectedLetter,
        float scoreThreshold,
        List<List<Vector2>> imageStrokes,
        Rect panelRect,
        float strokeWidth)
    {
        char expected = char.ToUpperInvariant(expectedLetter);
        float calibratedThreshold = ResolveExpectedLetterThreshold(expected, scoreThreshold);
        PDollarRecognizer.RecognitionResult broad = Recognizer.Recognize(points);
        PDollarRecognizer.RecognitionResult result = Recognizer.RecognizeMatching(
            points,
            name => CandidateStartsWith(name, expected),
            calibratedThreshold);
        PDollarRecognizer.RecognitionResult fused = FuseRecognition(points, result, calibratedThreshold, expected, imageStrokes);
        fused.expectedLetterFiltered = true;
        fused.expectedLetter = expected;
        fused.broadRecognizedName = broad.name;
        fused.broadBestCandidateName = broad.bestCandidateName;
        fused.broadScore = broad.score;
        fused.broadRunnerUpName = broad.runnerUpName;
        fused.broadRunnerUpScore = broad.runnerUpScore;
        fused.broadConfidence = ScoreToConfidence(broad.score, PDollarRecognizer.SCORE_THRESHOLD);
        fused.broadIsAmbiguous = broad.isAmbiguous;
        fused.calibratedThreshold = calibratedThreshold;
        fused.calibrationSeparation = calibrationSeparations.TryGetValue(expected, out float separation)
            ? separation
            : 0f;
        fused.calibrationReliable = calibrationReliability.TryGetValue(expected, out bool reliable) && reliable;
        return fused;
    }

    public float ResolveExpectedLetterThreshold(char expectedLetter, float configuredCeiling)
    {
        char expected = char.ToUpperInvariant(expectedLetter);
        float ceiling = Mathf.Clamp(configuredCeiling, MinimumCalibratedThreshold, PDollarRecognizer.SCORE_THRESHOLD);
        return calibratedThresholds.TryGetValue(expected, out float calibrated)
            ? Mathf.Min(ceiling, calibrated)
            : Mathf.Min(ceiling, DefaultCalibratedThreshold);
    }

    void RebuildLetterCalibration()
    {
        calibratedThresholds.Clear();
        calibrationSeparations.Clear();
        calibrationReliability.Clear();
        if (Recognizer == null)
            return;

        var grouped = new Dictionary<char, List<List<PDollarRecognizer.Point>>>();
        foreach ((string name, List<PDollarRecognizer.Point> points) in Recognizer.CreateRawTemplateSnapshot())
        {
            if (string.IsNullOrWhiteSpace(name) || points == null || points.Count < 2)
                continue;
            char letter = char.ToUpperInvariant(name[0]);
            if (!char.IsLetter(letter))
                continue;
            if (!grouped.TryGetValue(letter, out List<List<PDollarRecognizer.Point>> samples))
            {
                samples = new List<List<PDollarRecognizer.Point>>();
                grouped[letter] = samples;
            }
            samples.Add(points);
        }

        foreach (KeyValuePair<char, List<List<PDollarRecognizer.Point>>> pair in grouped)
        {
            List<List<PDollarRecognizer.Point>> samples = pair.Value;
            if (samples.Count < 3)
            {
                calibratedThresholds[pair.Key] = DefaultCalibratedThreshold;
                calibrationSeparations[pair.Key] = 0f;
                calibrationReliability[pair.Key] = false;
                continue;
            }

            var leaveOneOutScores = new List<float>(samples.Count);
            for (int heldOut = 0; heldOut < samples.Count; heldOut++)
            {
                var calibrationRecognizer = new PDollarRecognizer();
                for (int training = 0; training < samples.Count; training++)
                {
                    if (training != heldOut)
                        calibrationRecognizer.AddTemplate(pair.Key.ToString(), samples[training]);
                }
                PDollarRecognizer.RecognitionResult result = calibrationRecognizer.RecognizeMatching(
                    samples[heldOut],
                    _ => true,
                    float.MaxValue);
                if (!float.IsNaN(result.score) && !float.IsInfinity(result.score) && result.score < float.MaxValue * 0.5f)
                    leaveOneOutScores.Add(result.score);
            }

            if (leaveOneOutScores.Count == 0)
            {
                calibratedThresholds[pair.Key] = DefaultCalibratedThreshold;
                calibrationSeparations[pair.Key] = 0f;
                calibrationReliability[pair.Key] = false;
                continue;
            }

            leaveOneOutScores.Sort();
            int percentileIndex = Mathf.Clamp(Mathf.CeilToInt(leaveOneOutScores.Count * 0.90f) - 1, 0, leaveOneOutScores.Count - 1);
            float positiveP90 = leaveOneOutScores[percentileIndex];
            float robustPositiveCeiling = positiveP90 * 1.22f + 28f;

            // Score authored samples from every other letter against this
            // letter's templates. This prevents a permissive positive-only
            // threshold from swallowing a nearby letter class.
            var targetRecognizer = new PDollarRecognizer();
            foreach (List<PDollarRecognizer.Point> sample in samples)
                targetRecognizer.AddTemplate(pair.Key.ToString(), sample);
            var impostorScores = new List<float>();
            foreach (KeyValuePair<char, List<List<PDollarRecognizer.Point>>> other in grouped)
            {
                if (other.Key == pair.Key)
                    continue;
                foreach (List<PDollarRecognizer.Point> impostor in other.Value)
                {
                    PDollarRecognizer.RecognitionResult score = targetRecognizer.RecognizeMatching(
                        impostor,
                        _ => true,
                        float.MaxValue);
                    if (!float.IsNaN(score.score) && !float.IsInfinity(score.score) && score.score < float.MaxValue * 0.5f)
                        impostorScores.Add(score.score);
                }
            }

            float separation = 0f;
            bool reliable = false;
            float negativeSafetyCeiling = MaximumCalibratedThreshold;
            if (impostorScores.Count > 0)
            {
                impostorScores.Sort();
                int negativeIndex = Mathf.Clamp(Mathf.FloorToInt(impostorScores.Count * 0.10f), 0, impostorScores.Count - 1);
                float impostorP10 = impostorScores[negativeIndex];
                separation = impostorP10 - positiveP90;
                reliable = separation >= 45f;
                negativeSafetyCeiling = impostorP10 - Mathf.Max(24f, separation * 0.20f);
            }

            calibratedThresholds[pair.Key] = Mathf.Clamp(
                Mathf.Min(robustPositiveCeiling, negativeSafetyCeiling),
                MinimumCalibratedThreshold,
                MaximumCalibratedThreshold);
            calibrationSeparations[pair.Key] = separation;
            calibrationReliability[pair.Key] = reliable;
        }

        int reliableCount = 0;
        foreach (bool reliable in calibrationReliability.Values)
            if (reliable) reliableCount++;
        Debug.Log($"[RecognizerHost] Calibrated {calibratedThresholds.Count} expected-letter thresholds with positive and cross-letter evidence ({reliableCount} well-separated).");
    }

    public PDollarRecognizer.RecognitionResult RecognizeMirroredAsLetter(
        List<PDollarRecognizer.Point> points,
        char expectedLetter,
        float scoreThreshold)
    {
        return RecognizeAsLetter(MirrorHorizontally(points), expectedLetter, scoreThreshold);
    }

    public TinyLetterNeuralRecognizer.LetterImageDebugCapture CaptureLetterImagePreview(
        List<List<Vector2>> strokes,
        Rect panelRect,
        float strokeWidth,
        int previewSize = 128)
    {
        return enableTinyNeuralRecognizer && NeuralRecognizer != null
            ? NeuralRecognizer.CapturePanelImagePreview(strokes, panelRect, strokeWidth, previewSize)
            : null;
    }

    static bool CandidateStartsWith(string candidate, char expected)
    {
        return !string.IsNullOrEmpty(candidate) &&
               candidate != "Unknown" &&
               char.ToUpperInvariant(candidate[0]) == expected;
    }

    PDollarRecognizer.RecognitionResult FuseRecognition(
        List<PDollarRecognizer.Point> points,
        PDollarRecognizer.RecognitionResult result,
        float pDollarScoreThreshold,
        char expectedLetter = '\0',
        List<List<Vector2>> imageStrokes = null)
    {
        string pDollarName = BestPlausiblePDollarName(result);
        float pDollarConfidence = ScoreToConfidence(result.score, pDollarScoreThreshold);
        TinyLetterNeuralRecognizer.NeuralResult neural = RecognizeWithNeuralBackend(points, imageStrokes);

        if (string.IsNullOrEmpty(neural.name))
        {
            result.pDollarName = pDollarName;
            result.pDollarConfidence = pDollarConfidence;
            result.neuralRecognizedName = "Unknown";
            result.neuralConfidence = 0f;
            result.combinedConfidence = pDollarConfidence;
            result.recognizerAgreement = true;
            result.recognitionDecision = "p_dollar_only";
            return result;
        }

        string neuralName = neural.name;
        float neuralConfidence = neural.confidence;
        bool pDollarAccepted = result.score <= pDollarScoreThreshold && IsKnown(pDollarName);
        bool neuralAccepted = neuralConfidence >= 0.48f && IsKnown(neuralName);
        bool agreement = pDollarAccepted && neuralAccepted && SameLetter(pDollarName, neuralName);
        float combinedConfidence = Mathf.Clamp01((0.65f * pDollarConfidence) + (0.35f * neuralConfidence));
        bool closeDisagreement = pDollarAccepted && neuralAccepted && !agreement &&
            Mathf.Abs(pDollarConfidence - neuralConfidence) < 0.22f;

        string finalName = result.name;
        string decision = "p_dollar_only";
        bool ambiguous = result.isAmbiguous || neural.isAmbiguous || closeDisagreement;

        if (pDollarAccepted)
        {
            finalName = pDollarName;
            if (agreement)
            {
                decision = "p_dollar_primary_neural_agree";
                ambiguous = false;
            }
            else if (!neuralAccepted || neuralConfidence < 0.62f)
            {
                decision = "p_dollar_primary_neural_weak";
            }
            else
            {
                decision = "p_dollar_primary_neural_review";
                ambiguous = true;
            }
        }
        else
        {
            finalName = "Unknown";
            decision = neuralAccepted ? "neural_only_review" : "low_confidence";
        }

        result.name = finalName;
        result.pDollarName = pDollarName;
        result.pDollarConfidence = pDollarConfidence;
        result.neuralRecognizedName = neuralName;
        result.neuralConfidence = neuralConfidence;
        result.combinedConfidence = combinedConfidence;
        result.recognizerAgreement = agreement;
        result.recognitionDecision = decision;
        result.isAmbiguous = ambiguous;
        return result;
    }

    TinyLetterNeuralRecognizer.NeuralResult RecognizeWithNeuralBackend(
        List<PDollarRecognizer.Point> points,
        List<List<Vector2>> imageStrokes)
    {
        if (!enableTinyNeuralRecognizer)
            return default;

        if (HasImageStrokes(imageStrokes) && LetterCnnRecognizer != null && LetterCnnRecognizer.IsAvailable)
            return LetterCnnRecognizer.RecognizeImage(imageStrokes);

        if (NeuralRecognizer == null)
            return default;

        return HasImageStrokes(imageStrokes)
            ? NeuralRecognizer.RecognizeImage(imageStrokes)
            : NeuralRecognizer.Recognize(points);
    }

    static bool HasImageStrokes(List<List<Vector2>> strokes)
    {
        if (strokes == null)
            return false;

        foreach (List<Vector2> stroke in strokes)
            if (stroke != null && stroke.Count > 0)
                return true;
        return false;
    }

    static string BestPlausiblePDollarName(PDollarRecognizer.RecognitionResult result)
    {
        if (IsKnown(result.name))
            return result.name;
        return IsKnown(result.bestCandidateName) ? result.bestCandidateName : "Unknown";
    }

    static float ScoreToConfidence(float score, float threshold)
    {
        if (float.IsNaN(score) || float.IsInfinity(score) || score >= float.MaxValue * 0.5f)
            return 0f;

        float safeThreshold = Mathf.Max(1f, threshold);
        return Mathf.Clamp01(1f - Mathf.InverseLerp(0f, safeThreshold, Mathf.Max(0f, score)));
    }

    static bool IsKnown(string name)
    {
        return !string.IsNullOrWhiteSpace(name) && name != "Unknown";
    }

    static bool SameLetter(string left, string right)
    {
        return IsKnown(left) && IsKnown(right) &&
               char.ToUpperInvariant(left[0]) == char.ToUpperInvariant(right[0]);
    }

    static List<PDollarRecognizer.Point> MirrorHorizontally(List<PDollarRecognizer.Point> points)
    {
        var mirrored = new List<PDollarRecognizer.Point>();
        if (points == null || points.Count == 0)
            return mirrored;

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        foreach (PDollarRecognizer.Point point in points)
        {
            if (point.x < minX) minX = point.x;
            if (point.x > maxX) maxX = point.x;
        }

        float centerX = (minX + maxX) * 0.5f;
        foreach (PDollarRecognizer.Point point in points)
            mirrored.Add(new PDollarRecognizer.Point(centerX - (point.x - centerX), point.y, point.id));
        return mirrored;
    }
}
