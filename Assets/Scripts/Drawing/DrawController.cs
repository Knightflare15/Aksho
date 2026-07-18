using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System.Collections;
using UnityEngine.UI;

/// <summary>
/// Handles freehand drawing input, letter-by-letter recognition via PDollar,
/// word accumulation, and dispatching results through the active IDrawMode.
///
/// CONTROLS (in draw mode):
///   Left Mouse        — draw stroke
///   Right Mouse       — confirm current letter
///   Backspace / MMB   — delete last confirmed letter
///   Enter             — submit word
///   F                 — cancel / exit (via PlayerController)
///
/// SETUP:
///   1. Attach DrawController, FeedbackManager, SandboxMode, ChallengeMode,
///      TemplateLibrary, and RecognizerHost all to the same GameObject.
///   2. Assign recognizerHost in the Inspector (or it is found automatically).
///   3. Call SetMode() from your game UI before entering draw mode.
/// </summary>
public class DrawController : MonoBehaviour
{
    // ── Inspector ──────────────────────────────────────────────────────────
    [Header("References")]
    public RectTransform    drawingPanel;
    public GameObject       pointPrefab;
    public TextMeshProUGUI  wordDisplay;
    public TextMeshProUGUI  hintText;

    [Tooltip("Drag RecognizerHost here (auto-found on same GameObject if left empty)")]
    public RecognizerHost   recognizerHost;

    [Header("Stroke Settings")]
    public float minSegmentDistance =   1.5f;
    [Tooltip("Visual-only brush width for the live ink. Recognition still uses the captured point path.")]
    public float strokeThickness    = 18f;
    [Tooltip("Hard safety cap for one letter. Prevents accidental scribbles from allocating unbounded objects and diagnostic work.")]
    public int maxPointsPerLetter = 2048;
    [Tooltip("Hard safety cap for pen lifts in one letter.")]
    public int maxStrokesPerLetter = 32;

    // ── Mode plug-in ───────────────────────────────────────────────────────
    public IDrawMode ActiveMode { get; private set; }

    public void SetMode(IDrawMode mode) => ActiveMode = mode;

    // ── State ──────────────────────────────────────────────────────────────
    [HideInInspector] public bool canDraw = false;

    private bool              isDrawing            = false;
    private List<GameObject>  currentStrokeVisuals = new List<GameObject>();
    private List<List<Vector2>> currentLetterStrokes = new List<List<Vector2>>();
    private List<List<Vector2>> currentLetterRawStrokes = new List<List<Vector2>>();
    private List<Vector2>     activeStroke         = new List<Vector2>();
    private List<Vector2>     activeRawStroke      = new List<Vector2>();
    private List<char>        acceptedLetters      = new List<char>();
    private bool              sessionInitialized   = false;
    private bool              waitForPrimaryReleaseOnEnter = false;
    private bool              activeStrokeUsesAssist = false;
    private bool              currentLetterUsedAssist = false;
    private int               currentLetterCapturedPointCount;
    private int               currentLetterAssistedPointCount;
    private float             currentLetterAssistDistanceTotal;
    private float             currentLetterAssistDistanceMax;
    private float             pendingInputAssistDistance;
    private readonly HandwritingCaptureSession currentLetterCapture = new HandwritingCaptureSession();

    private FeedbackManager   feedback;
    private WordActionHandler wordActionHandler;
    private GameObject        neuralDebugRoot;
    private RawImage          neuralDebugGrayscaleImage;
    private RawImage          neuralDebugThresholdImage;
    private TextMeshProUGUI   neuralDebugLabel;
    private Texture2D         neuralDebugGrayscaleTexture;
    private Texture2D         neuralDebugThresholdTexture;

    public bool ActiveStrokeUsesAssist => activeStrokeUsesAssist;

    // ── Unity lifecycle ────────────────────────────────────────────────────
    void LogBounds(List<Vector2> points, string label)
{
    if (points == null || points.Count == 0)
    {
        Debug.Log($"{label}: No points");
        return;
    }

    float minX = float.MaxValue;
    float maxX = float.MinValue;
    float minY = float.MaxValue;
    float maxY = float.MinValue;

    foreach (var p in points)
    {
        if (p.x < minX) minX = p.x;
        if (p.x > maxX) maxX = p.x;
        if (p.y < minY) minY = p.y;
        if (p.y > maxY) maxY = p.y;
    }

    Debug.Log($"{label} Bounds → X: [{minX}, {maxX}]  Y: [{minY}, {maxY}]");
}
    void Awake()
    {
        feedback = GetComponent<FeedbackManager>();
        wordActionHandler = GetComponent<WordActionHandler>();

        if (wordActionHandler == null)
            wordActionHandler = gameObject.AddComponent<WordActionHandler>();

        if (recognizerHost == null)
            recognizerHost = GetComponent<RecognizerHost>();

        if (recognizerHost == null)
            Debug.LogError("[DrawController] No RecognizerHost found! Attach one to this GameObject.");

        strokeThickness = Mathf.Max(strokeThickness, 18f);

        ConfigureDrawingCanvas();
        ApplyUITheme();
    }

    void Start()
    {
        if (ActiveMode != null)
            return;

        var challengeMode = GetComponent<ChallengeMode>();
        if (challengeMode != null)
            SetMode(challengeMode);
    }


    void Update()
    {
        if (!canDraw) return;
        if (PauseMenuController.IsPaused)
        {
            isDrawing = false;
            activeStrokeUsesAssist = false;
            return;
        }

        if (waitForPrimaryReleaseOnEnter)
        {
            if (Input.GetMouseButton(0))
                return;

            waitForPrimaryReleaseOnEnter = false;
        }

        if (Input.GetMouseButtonDown(0))
        {
            if (currentLetterRawStrokes.Count >= Mathf.Max(1, maxStrokesPerLetter) ||
                currentLetterCapturedPointCount >= Mathf.Max(8, maxPointsPerLetter))
            {
                SetHintText("That is enough ink for one letter. Check it or clear and try again.", forceVisible: true);
                isDrawing = false;
                activeStrokeUsesAssist = false;
                return;
            }
            if (TryGetPanelLocalPoint(Input.mousePosition, out Vector2 rawStart))
            {
                activeStrokeUsesAssist = ShouldUseInputAssist(rawStart);
                Vector2 pos = GetPanelInputPoint(rawStart, true);
                isDrawing = true;
                activeStroke = new List<Vector2>();
                activeRawStroke = new List<Vector2>();
                currentLetterStrokes.Add(activeStroke);
                currentLetterRawStrokes.Add(activeRawStroke);
                currentLetterCapture.BeginStroke();
                activeStroke.Add(pos);
                activeRawStroke.Add(rawStart);
                RecordCapturedPointAssist(rawStart);
                SpawnDot(pos);
                NotifyLiveStrokeUpdated();
            }
            else
            {
                isDrawing = false;
                activeStrokeUsesAssist = false;
            }
        }

        if (Input.GetMouseButton(0) && isDrawing)
        {
            if (!TryGetPanelLocalPoint(Input.mousePosition, out Vector2 rawPoint))
            {
                isDrawing = false;
                activeStrokeUsesAssist = false;
            }
            else
            {
                Vector2 pos = GetPanelInputPoint(rawPoint, false);
                if (activeStroke.Count == 0 || activeRawStroke.Count == 0)
                {
                    activeStroke.Add(pos);
                    activeRawStroke.Add(rawPoint);
                    SpawnDot(pos);
                }
                else
                {
                    Vector2 last = activeStroke[activeStroke.Count - 1];
                    Vector2 lastRaw = activeRawStroke[activeRawStroke.Count - 1];
                    if (Vector2.Distance(lastRaw, rawPoint) >= minSegmentDistance)
                    {
                        if (currentLetterCapturedPointCount >= Mathf.Max(8, maxPointsPerLetter))
                        {
                            SetHintText("That is enough ink for one letter. Check it or clear and try again.", forceVisible: true);
                            isDrawing = false;
                            activeStrokeUsesAssist = false;
                            return;
                        }
                        SpawnSegment(last, pos);
                        activeStroke.Add(pos);
                        activeRawStroke.Add(rawPoint);
                        RecordCapturedPointAssist(rawPoint);
                        NotifyLiveStrokeUpdated();
                    }
                }
            }
        }

        if (Input.GetMouseButtonUp(0))
        {
            isDrawing = false;
            activeStrokeUsesAssist = false;
        }

        if (Input.GetMouseButtonDown(1))
            TryConfirmLetter();

        if (Input.GetKeyDown(KeyCode.Backspace) || Input.GetMouseButtonDown(2))
            DeleteLastLetter();

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            HandleEnterPressed();

        ApplyUITheme();
        RefreshHintText();
    }

    // ── Called by PlayerController ─────────────────────────────────────────

    public void EnterDrawing()
    {
        waitForPrimaryReleaseOnEnter = Input.GetMouseButton(0);
        ClearCurrentLetter();
        canDraw = true;
        if (sessionInitialized)
        {
            UpdateWordDisplay();
            RefreshChallengeChrome();
            RefreshHintText();
            return;
        }

        acceptedLetters.Clear();
        UpdateWordDisplay();
        Debug.Log("ActiveMode: " + ActiveMode);
        ActiveMode?.OnEnter();
        sessionInitialized = true;
    }

    public void PauseDrawing()
    {
        isDrawing = false;
        ClearCurrentLetter();
        canDraw = false;
        RefreshChallengeChrome();
    }

    public void CancelAndReset()
    {
        if (sessionInitialized)
            ActiveMode?.OnExit();
        ResetAll();
        canDraw = false;
        sessionInitialized = false;
        RefreshChallengeChrome();
    }

    public bool HasActiveSession()
    {
        return sessionInitialized;
    }

    // ── Drawing helpers ────────────────────────────────────────────────────

    bool TryGetPanelLocalPoint(Vector3 screenPos, out Vector2 local)
    {
        local = Vector2.zero;
        if (drawingPanel == null)
            return false;

        Camera eventCamera = GetPanelEventCamera();
        if (!RectTransformUtility.RectangleContainsScreenPoint(drawingPanel, screenPos, eventCamera))
            return false;

        return RectTransformUtility.ScreenPointToLocalPointInRectangle(
            drawingPanel, screenPos, eventCamera, out local);
    }

    Camera GetPanelEventCamera()
    {
        Canvas canvas = drawingPanel != null ? drawingPanel.GetComponentInParent<Canvas>() : null;
        if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            return null;

        return canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
    }

    bool ShouldUseInputAssist(Vector2 raw)
    {
        if (ActiveMode is not IDrawInputAssist assist ||
            !assist.TryGetInputBounds(out Rect inputBounds))
            return false;

        return inputBounds.Contains(raw);
    }

    Vector2 GetPanelInputPoint(Vector2 raw, bool isStrokeStart)
    {
        pendingInputAssistDistance = 0f;
        if (!activeStrokeUsesAssist ||
            ActiveMode is not IDrawInputAssist assist)
            return raw;

        Vector2 originalRaw = raw;

        bool hasBounds = assist.TryGetInputBounds(out Rect inputBounds);
        if (hasBounds)
            raw = ClampInputPoint(raw, inputBounds);

        Vector2 previous = activeStroke != null && activeStroke.Count > 0
            ? activeStroke[activeStroke.Count - 1]
            : raw;

        Vector2 point = assist.TryAdjustStrokePoint(raw, previous, isStrokeStart, out Vector2 adjusted)
            ? adjusted
            : raw;
        Vector2 finalPoint = hasBounds ? ClampInputPoint(point, inputBounds) : point;
        pendingInputAssistDistance = Vector2.Distance(finalPoint, originalRaw);
        if (pendingInputAssistDistance > 0.75f)
            currentLetterUsedAssist = true;
        return finalPoint;
    }

    void RecordCapturedPointAssist(Vector2 rawPoint)
    {
        currentLetterCapturedPointCount++;
        currentLetterCapture.AddPoint(rawPoint);
        if (pendingInputAssistDistance <= 0.75f)
            return;

        currentLetterAssistedPointCount++;
        currentLetterAssistDistanceTotal += pendingInputAssistDistance;
        currentLetterAssistDistanceMax = Mathf.Max(currentLetterAssistDistanceMax, pendingInputAssistDistance);
    }

    Vector2 ClampInputPoint(Vector2 point, Rect bounds)
    {
        float inset = Mathf.Max(2f, strokeThickness * 0.5f);
        float minX = bounds.xMin + inset;
        float maxX = bounds.xMax - inset;
        float minY = bounds.yMin + inset;
        float maxY = bounds.yMax - inset;

        if (minX > maxX)
        {
            float center = bounds.center.x;
            minX = center;
            maxX = center;
        }

        if (minY > maxY)
        {
            float center = bounds.center.y;
            minY = center;
            maxY = center;
        }

        return new Vector2(
            Mathf.Clamp(point.x, minX, maxX),
            Mathf.Clamp(point.y, minY, maxY));
    }

    void SpawnDot(Vector2 pos)
    {
        var go = Instantiate(pointPrefab, drawingPanel);
        go.SetActive(true);
        var rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta        = new Vector2(strokeThickness, strokeThickness);
        BrushStrokeStyle.ApplyDot(go.GetComponent<Image>(), Color.white);
        currentStrokeVisuals.Add(go);
    }

    void SpawnSegment(Vector2 from, Vector2 to)
    {
        var go  = Instantiate(pointPrefab, drawingPanel);
        go.SetActive(true);
        var rt  = go.GetComponent<RectTransform>();
        var dir = to - from;
        rt.sizeDelta        = new Vector2(dir.magnitude, strokeThickness);
        rt.anchoredPosition = from + dir * 0.5f;
        rt.rotation         = Quaternion.Euler(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        BrushStrokeStyle.ApplySegment(go.GetComponent<Image>(), Color.white);
        currentStrokeVisuals.Add(go);
    }

    // ── Letter / word logic ────────────────────────────────────────────────

    bool TryConfirmLetter()
    {
        if (TotalPointCount() == 0)
        {
            SetHintText("Draw a letter first!", forceVisible: true);
            return false;
        }

        var pts = new List<PDollarRecognizer.Point>();
        for (int s = 0; s < currentLetterRawStrokes.Count; s++)
            foreach (var p in currentLetterRawStrokes[s])
                pts.Add(new PDollarRecognizer.Point(p.x, p.y, s));

        List<List<Vector2>> rawStrokes = CopyCurrentLetterStrokes();
        Rect panelRect = drawingPanel != null ? drawingPanel.rect : new Rect(-125f, -125f, 250f, 250f);

        PDollarRecognizer.RecognitionResult result = RecognizeCurrentLetter(pts, rawStrokes, panelRect);
        result.inputAssisted = currentLetterUsedAssist;
        result.inputAssistFraction = currentLetterCapturedPointCount > 0
            ? currentLetterAssistedPointCount / (float)currentLetterCapturedPointCount
            : 0f;
        result.inputAssistMeanDistance = currentLetterAssistedPointCount > 0
            ? currentLetterAssistDistanceTotal / currentLetterAssistedPointCount
            : 0f;
        result.inputAssistMaxDistance = currentLetterAssistDistanceMax;
        char expectedLetter = '\0';
        if (ActiveMode is IExpectedLetterRecognitionContext captureContext)
            captureContext.TryGetExpectedLetterRecognition(out expectedLetter, out _);
        CurriculumSessionManager curriculum = CurriculumSessionManager.Instance;
        result.captureSample = currentLetterCapture.Build(
            expectedLetter == '\0' ? "" : expectedLetter.ToString(),
            panelRect,
            curriculum != null ? curriculum.activeStudentId : "anonymous",
            curriculum?.CurrentMission?.missionId ?? "",
            "unity_gameplay");
        result.captureSample.inputAssisted = result.inputAssisted;
        result.captureSample.inputAssistFraction = result.inputAssistFraction;
        result.captureSample.inputAssistMeanDistance = result.inputAssistMeanDistance;
        result.captureSample.inputAssistMaxDistance = result.inputAssistMaxDistance;
        if (curriculum != null)
        {
            result.captureSample.writerAgeBand = curriculum.handwritingWriterAgeBand?.Trim() ?? "";
            result.captureSample.handedness = string.IsNullOrWhiteSpace(curriculum.handwritingHandedness)
                ? "unknown"
                : curriculum.handwritingHandedness.Trim();
            result.captureSample.collectionCohort = curriculum.handwritingCollectionCohort?.Trim() ?? "";
            result.captureSample.consentReference = curriculum.handwritingConsentReference?.Trim() ?? "";
        }

        ShowNeuralDebugPreview(rawStrokes, result);

        char accepted = ActiveMode is IRawLetterEvaluator rawEvaluator
            ? rawEvaluator.OnLetterConfirmed(result, currentStrokeVisuals, rawStrokes)
            : ActiveMode != null
                ? ActiveMode.OnLetterConfirmed(result, currentStrokeVisuals)
                : FallbackConfirm(result);

        RefreshHintText();

        if (accepted == '\0')
        {
            ClearCurrentLetter();
            UpdateWordDisplay();
            return false;
        }
        
        acceptedLetters.Add(accepted);
        Debug.Log($"[DrawController] Accepted: '{accepted}'  score={result.score:F1}");
        NotifyLetterAccepted(accepted, result);
        ClearCurrentLetter();
        UpdateWordDisplay();
        return true;
    }

    public void ConfirmCurrentLetterFromButton()
    {
        TryConfirmLetter();
    }

    PDollarRecognizer.RecognitionResult RecognizeCurrentLetter(
        List<PDollarRecognizer.Point> points,
        List<List<Vector2>> rawStrokes,
        Rect panelRect)
    {
        if (recognizerHost == null)
            return new PDollarRecognizer.RecognitionResult { name = "Unknown", score = float.MaxValue };

        if (ActiveMode is IExpectedLetterRecognitionContext expectedContext &&
            expectedContext.TryGetExpectedLetterRecognition(out char expectedLetter, out float scoreThreshold))
        {
            return recognizerHost.RecognizeAsLetter(
                points,
                expectedLetter,
                scoreThreshold,
                rawStrokes,
                panelRect,
                strokeThickness);
        }

        return recognizerHost.Recognize(points, rawStrokes, panelRect, strokeThickness);
    }

    char FallbackConfirm(PDollarRecognizer.RecognitionResult result)
    {
        if (result.score > PDollarRecognizer.SCORE_THRESHOLD) return '?';
        return result.name.Length > 0 ? result.name[0] : '?';
    }

    void DeleteLastLetter()
    {
        if (acceptedLetters.Count > 0)
        {
            acceptedLetters.RemoveAt(acceptedLetters.Count - 1);
            UpdateWordDisplay();
        }
        else ClearCurrentLetter();
    }

    public void DeleteLastLetterFromButton()
    {
        DeleteLastLetter();
    }

    void HandleEnterPressed()
    {
        if (ActiveMode is ChallengeMode challengeMode && !challengeMode.IsWordComplete)
        {
            if (HasAttemptProgress(challengeMode))
            {
                challengeMode.DiscardPartialAttempt();
                ResetAll();
            }
            else
                RefreshHintText();

            return;
        }

        SubmitWord();
    }

    public void SubmitWordFromButton()
    {
        HandleEnterPressed();
    }

    public void CloseDrawingFromButton()
    {
        PlayerController player = GetComponentInParent<PlayerController>() ?? FindAnyObjectByType<PlayerController>();
        if (player != null)
            player.ExitDrawMode(true);
    }

    bool HasAttemptProgress(ChallengeMode challengeMode)
    {
        return TotalPointCount() > 0 ||
               acceptedLetters.Count > 0 ||
               (challengeMode != null && challengeMode.HasAttemptProgress);
    }

    void SubmitWord()
    {
        if (TotalPointCount() > 0 && !TryConfirmLetter())
            return;

        if (acceptedLetters.Count == 0)
        {
            SetHintText("Nothing to submit!", forceVisible: true);
            return;
        }

        if (ActiveMode is ChallengeMode challengeMode && !challengeMode.IsWordComplete)
        {
            challengeMode.DiscardPartialAttempt();
            ResetAll();
            return;
        }

        string word = new string(acceptedLetters.ToArray());
        Debug.Log($"[DrawController] Submitting: '{word}'");

        ActiveMode?.OnWordSubmitted(word);

        if (ActiveMode is IDrawSubmissionBehavior submissionBehavior &&
            submissionBehavior.KeepDrawingSessionAfterSubmit)
        {
            ResetAll();
            ActiveMode?.OnEnter();
            return;
        }

        if (ActiveMode is ChallengeMode completedChallengeMode && !completedChallengeMode.LastSubmissionWasCorrect)
        {
            completedChallengeMode.ResetForRetry();
            ResetAll();
            return;
        }

        string wordActionPhrase = ActiveMode is IDrawWordActionPhraseProvider phraseProvider
            ? phraseProvider.ResolveWordActionPhrase(word)
            : word;

        if (wordActionHandler != null &&
            (ActiveMode is not IDrawSubmissionBehavior consumingMode || !consumingMode.ConsumeWordAction))
            wordActionHandler.HandleWord(wordActionPhrase);

        ResetAll();
        ActiveMode?.OnExit();
        canDraw = false;
        sessionInitialized = false;
        
        StartCoroutine(ExitAfterDelay(1f));
    }
    IEnumerator ExitAfterDelay(float delay)
{
    yield return new WaitForSeconds(delay);

    var pc = GetComponentInParent<PlayerController>();
    if (pc) pc.ExitDrawMode(false);
}
    // ── Helpers ────────────────────────────────────────────────────────────

    void ClearCurrentLetter()
    {
        foreach (var go in currentStrokeVisuals) Destroy(go);
        currentStrokeVisuals.Clear();
        currentLetterStrokes.Clear();
        currentLetterRawStrokes.Clear();
        activeStroke = new List<Vector2>();
        activeRawStroke = new List<Vector2>();
        isDrawing    = false;
        activeStrokeUsesAssist = false;
        currentLetterUsedAssist = false;
        currentLetterCapturedPointCount = 0;
        currentLetterAssistedPointCount = 0;
        currentLetterAssistDistanceTotal = 0f;
        currentLetterAssistDistanceMax = 0f;
        pendingInputAssistDistance = 0f;
        currentLetterCapture.Reset();
    }

    void ResetAll()
    {
        ClearCurrentLetter();
        HideNeuralDebugPreview();
        waitForPrimaryReleaseOnEnter = false;
        acceptedLetters.Clear();
        UpdateWordDisplay();
    }

    void UpdateWordDisplay()
    {
        if (wordDisplay == null) return;
        wordDisplay.text = acceptedLetters.Count == 0
            ? ""
            : string.Join("  ", acceptedLetters) + "  _";
    }

    int TotalPointCount()
    {
        int n = 0;
        foreach (var s in currentLetterRawStrokes) n += s.Count;
        return n;
    }

    void NotifyLiveStrokeUpdated()
    {
        if (ActiveMode is ILiveLetterFeedback liveFeedback)
            liveFeedback.OnLiveStrokeUpdated(currentLetterRawStrokes, currentStrokeVisuals);
    }

    void NotifyLetterAccepted(char letter, PDollarRecognizer.RecognitionResult result)
    {
        if (ActiveMode is ILetterAcceptedObserver observer)
            observer.OnLetterAccepted(letter, currentLetterRawStrokes, result);
    }

    void ApplyUITheme()
    {
        ConfigureDrawingCanvas();
        GameUiTheme.StyleDrawingSurface(drawingPanel, recorder: false);
        GameUiTheme.StyleHudLabel(hintText, GameUiTheme.HudLabelRole.Hint);
        GameUiTheme.StyleHudLabel(wordDisplay, GameUiTheme.HudLabelRole.Word);
    }

    void RefreshHintText()
    {
        if (ActiveMode == null)
            return;

        SetHintText(ActiveMode.CurrentHint);
    }

    void RefreshChallengeChrome()
    {
        if (ActiveMode is ChallengeMode challengeMode)
            challengeMode.RefreshDrawingChromeVisibility();
    }

    void SetHintText(string value, bool forceVisible = false)
    {
        if (hintText == null)
            return;

        bool visible = forceVisible || !ShouldSuppressPanelHint();
        hintText.gameObject.SetActive(visible);
        SetHintBackdropVisible(visible);
        if (visible)
            hintText.text = value;
    }

    bool ShouldSuppressPanelHint()
    {
        return ActiveMode is ChallengeMode challengeMode && challengeMode.ShouldSuppressPanelHint;
    }

    void SetHintBackdropVisible(bool visible)
    {
        Transform parent = hintText != null ? hintText.transform.parent : null;
        if (parent == null)
            return;

        Transform backdrop = parent.Find(hintText.name + "_ThemeBackdrop");
        if (backdrop != null)
            backdrop.gameObject.SetActive(visible);
    }

    void ConfigureDrawingCanvas()
    {
        CanvasScaler scaler = drawingPanel != null
            ? drawingPanel.GetComponentInParent<CanvasScaler>()
            : null;
        GameUiTheme.ConfigureReadableCanvasScaler(scaler);
        ConfigureDrawingPanelLayout();
    }

    void ConfigureDrawingPanelLayout()
    {
        if (drawingPanel == null || drawingPanel.parent is not RectTransform parent)
            return;

        Vector2 parentSize = parent.rect.size;
        if (parentSize.x <= 1f || parentSize.y <= 1f)
            return;

        float width = Mathf.Clamp(parentSize.x * 0.94f, 860f, parentSize.x - 24f);
        float height = Mathf.Clamp(parentSize.y * 0.94f, 600f, parentSize.y - 24f);
        if (parentSize.x < 900f)
        {
            width = Mathf.Clamp(parentSize.x * 0.985f, 620f, parentSize.x - 10f);
            height = Mathf.Clamp(parentSize.y * 0.93f, 430f, parentSize.y - 18f);
        }

        drawingPanel.anchorMin = new Vector2(0.5f, 0.5f);
        drawingPanel.anchorMax = new Vector2(0.5f, 0.5f);
        drawingPanel.pivot = new Vector2(0.5f, 0.5f);
        drawingPanel.localScale = Vector3.one;
        drawingPanel.anchoredPosition = new Vector2(0f, -2f);
        drawingPanel.sizeDelta = new Vector2(width, height);
    }

    public List<List<Vector2>> CopyCurrentLetterStrokes()
    {
        var copy = new List<List<Vector2>>(currentLetterRawStrokes.Count);
        foreach (var stroke in currentLetterRawStrokes)
            copy.Add(stroke != null ? new List<Vector2>(stroke) : new List<Vector2>());
        return copy;
    }

    void ShowNeuralDebugPreview(List<List<Vector2>> rawStrokes, PDollarRecognizer.RecognitionResult result)
    {
        if (recognizerHost == null || drawingPanel == null)
            return;

        TinyLetterNeuralRecognizer.LetterImageDebugCapture capture =
            recognizerHost.CaptureLetterImagePreview(rawStrokes, drawingPanel.rect, strokeThickness, 150);
        if (capture == null)
            return;

        EnsureNeuralDebugPreview();
        ReplaceDebugTextures(capture.grayscaleTexture, capture.thresholdTexture);
        neuralDebugRoot.SetActive(true);
        if (neuralDebugLabel != null)
        {
            string nn = string.IsNullOrWhiteSpace(result.neuralRecognizedName)
                ? "Unknown"
                : result.neuralRecognizedName;
            string pDollarLabel = result.expectedLetterFiltered
                ? $"{char.ToUpperInvariant(result.expectedLetter)} only"
                : result.bestCandidateName;
            neuralDebugLabel.text =
                $"Panel capture -> grayscale -> threshold\nCNN {nn} {(result.neuralConfidence * 100f):0}% | P$ {pDollarLabel} {result.score:0.0}";
        }
    }

    void EnsureNeuralDebugPreview()
    {
        if (neuralDebugRoot != null)
            return;

        Transform parent = drawingPanel != null && drawingPanel.parent != null
            ? drawingPanel.parent
            : transform;

        neuralDebugRoot = new GameObject("NeuralLetterDebugPreview", typeof(RectTransform), typeof(Image));
        neuralDebugRoot.transform.SetParent(parent, false);
        var rootRect = neuralDebugRoot.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0f, 1f);
        rootRect.anchorMax = new Vector2(0f, 1f);
        rootRect.pivot = new Vector2(0f, 1f);
        rootRect.anchoredPosition = new Vector2(28f, -28f);
        rootRect.sizeDelta = new Vector2(372f, 232f);
        var background = neuralDebugRoot.GetComponent<Image>();
        background.color = new Color(0.04f, 0.05f, 0.06f, 0.88f);

        neuralDebugLabel = CreateDebugLabel(neuralDebugRoot.transform, "Title", new Vector2(16f, -14f), new Vector2(340f, 46f), 14);
        neuralDebugGrayscaleImage = CreateDebugImage(neuralDebugRoot.transform, "Grayscale", new Vector2(16f, -72f));
        neuralDebugThresholdImage = CreateDebugImage(neuralDebugRoot.transform, "Threshold", new Vector2(190f, -72f));
        CreateDebugLabel(neuralDebugRoot.transform, "GrayLabel", new Vector2(16f, -190f), new Vector2(150f, 24f), 12).text = "grayscale";
        CreateDebugLabel(neuralDebugRoot.transform, "ThresholdLabel", new Vector2(190f, -190f), new Vector2(150f, 24f), 12).text = "black/white";
    }

    RawImage CreateDebugImage(Transform parent, string name, Vector2 anchoredPosition)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = new Vector2(150f, 110f);
        var image = go.GetComponent<RawImage>();
        image.color = Color.white;
        return image;
    }

    TextMeshProUGUI CreateDebugLabel(Transform parent, string name, Vector2 anchoredPosition, Vector2 size, float fontSize)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
        var label = go.GetComponent<TextMeshProUGUI>();
        label.fontSize = fontSize;
        label.color = Color.white;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.alignment = TextAlignmentOptions.TopLeft;
        return label;
    }

    void ReplaceDebugTextures(Texture2D grayscale, Texture2D threshold)
    {
        if (neuralDebugGrayscaleTexture != null)
            Destroy(neuralDebugGrayscaleTexture);
        if (neuralDebugThresholdTexture != null)
            Destroy(neuralDebugThresholdTexture);

        neuralDebugGrayscaleTexture = grayscale;
        neuralDebugThresholdTexture = threshold;
        if (neuralDebugGrayscaleImage != null)
            neuralDebugGrayscaleImage.texture = neuralDebugGrayscaleTexture;
        if (neuralDebugThresholdImage != null)
            neuralDebugThresholdImage.texture = neuralDebugThresholdTexture;
    }

    void HideNeuralDebugPreview()
    {
        if (neuralDebugRoot != null)
            neuralDebugRoot.SetActive(false);

        ReplaceDebugTextures(null, null);
    }
}
