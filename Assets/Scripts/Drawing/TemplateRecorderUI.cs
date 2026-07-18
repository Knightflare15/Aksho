using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Template Recorder UI. Press I to open/close. Starts fully hidden.
///
/// Scroll fix:   Letter picker uses exactly 2 columns and fits button width
///               to the current viewport so inspector overrides cannot break it.
/// Preview fix:  StrokePreviewRenderer.SetFromRaw() receives explicit
///               renderSize so it works before Unity's layout pass.
/// </summary>
public class TemplateRecorderUI : MonoBehaviour
{
    private const int LetterPickerColumnCount = 2;
    private const int GalleryColumnCount = 1;

    public static bool IsOpen { get; private set; }

    // ── Inspector ──────────────────────────────────────────────────────────

    [Header("Panels")]
    public GameObject panelMainMenu;
    public GameObject panelLetterPicker;
    public GameObject panelRecorder;
    public GameObject panelGallery;

    [Header("Main Menu")]
    public Button btnAddTemplate;
    public Button btnTestRecognition;
    public Button btnTestVoiceInput;
    public Button btnViewEdit;

    [Header("Letter Picker")]
    [Tooltip("Drag the ScrollRect component (LetterScroll GameObject)")]
    public ScrollRect letterScrollRect;
    public Button     btnBackFromPicker;

    [Header("Recorder")]
    public RectTransform   recorderDrawArea;
    public TextMeshProUGUI labelCurrentLetter;
    public TextMeshProUGUI labelSampleCount;
    public TextMeshProUGUI labelInstruction;
    public Button          btnSaveSample;
    public Button          btnTestSample;
    public Button          btnClearRecorder;
    public Button          btnBackFromRecorder;

    [Header("Gallery")]
    [Tooltip("Drag the ScrollRect component (Scroll View GameObject in Gallery panel)")]
    public ScrollRect      galleryScrollRect;
    public Transform       letterTabBar;
    public Button          btnBackFromGallery;
    public TextMeshProUGUI labelGalleryEmpty;   // optional

    [Header("Layout")]
    [Tooltip("Size of each letter button in the picker grid")]
    public Vector2 letterButtonSize = new Vector2(70f, 70f);
    [Tooltip("Number of columns in the letter picker (keep small enough to fit your panel width)")]
    public int     letterColumns    = 2;
    [Tooltip("Size of each template card in the gallery")]
    public Vector2 cardSize         = new Vector2(130f, 160f);
    [Tooltip("Columns in the gallery grid")]
    public int     galleryColumns   = 1;
    public float   gridSpacing      = 6f;

    [Header("Stroke visuals")]
    public Color strokeColour    = Color.white;
    public float strokeThickness = 10f;

    [Header("Dependencies")]
    public TemplateLibrary templateLibrary;
    public RecognizerHost  recognizerHost;

    [Header("Dataset provenance")]
    [Tooltip("Pseudonymous writer ID stored with newly recorded samples. Do not enter a child's name.")]
    public string datasetWriterId = "designer";
    [Tooltip("Collection session ID. Leave blank to create one when the recorder starts.")]
    public string datasetSessionId = "";
    public string datasetWriterAgeBand = "";
    public string datasetHandedness = "unknown";
    public string datasetCollectionCohort = "";
    public string datasetConsentReference = "";

    // ── Runtime ────────────────────────────────────────────────────────────

    private Transform _letterContent;
    private Transform _galleryContent;

    private string              _currentLetter   = "A";
    private bool                _isDrawing       = false;
    private List<Vector2>       _activeStroke    = new List<Vector2>();
    private List<List<Vector2>> _recordedStrokes = new List<List<Vector2>>();
    private List<GameObject>    _liveSegments    = new List<GameObject>();
    private string              _galleryLetter   = "";
    private bool                _ready           = false;
    private Vector2             _drawAreaSize    = new Vector2(400f, 400f);
    private bool                _isTestMode;
    private VoiceInputTestUI    _voiceInputTest;
    private bool                _pauseLockHeld;
    private readonly HandwritingCaptureSession _captureSession = new HandwritingCaptureSession();

    // ── Lifecycle ──────────────────────────────────────────────────────────

    void Awake()
    {
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }
        var canvas = GetComponentInParent<Canvas>() ?? GetComponent<Canvas>();
        if (canvas && !canvas.GetComponent<GraphicRaycaster>())
            canvas.gameObject.AddComponent<GraphicRaycaster>();
    }

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
    void Start()
    {   
        if (!ValidateRefs()) return;
        if (string.IsNullOrWhiteSpace(datasetSessionId))
            datasetSessionId = System.Guid.NewGuid().ToString("N");
        letterColumns = LetterPickerColumnCount;
        if (cardSize == new Vector2(130f, 160f))
            cardSize = new Vector2(168f, 196f);
        galleryColumns = GalleryColumnCount;
        gridSpacing = Mathf.Max(gridSpacing, 10f);
        strokeThickness = Mathf.Max(strokeThickness, 10f);
        strokeColour = GameUiTheme.Stroke;
        _letterContent = SetupScrollRect(letterScrollRect, "LetterContent");
        _galleryContent = SetupScrollRect(galleryScrollRect, "GalleryContent");
        EnsureOptionalButtons();
        WireButtons();
        ApplyTheme();
        if (templateLibrary) templateLibrary.OnLibraryChanged += OnLibraryChanged;
        HideAll();
        _ready = true;
    }

    void OnValidate()
    {
        letterColumns = LetterPickerColumnCount;
        galleryColumns = GalleryColumnCount;
    }

    void OnDestroy()
    {
        if (templateLibrary) templateLibrary.OnLibraryChanged -= OnLibraryChanged;
        if (_pauseLockHeld)
        {
            PauseMenuController.EndModalPause();
            _pauseLockHeld = false;
        }
    }

    void Update()
    {
        if (!_ready) return;
        if (Input.GetKeyDown(KeyCode.I))
        {
            if (IsOpen) CloseRecorderUI(); else OpenRecorderUI();
        }
        if (panelRecorder && panelRecorder.activeInHierarchy)
            HandleRecorderInput();
    }

    // ── ScrollRect setup ───────────────────────────────────────────────────

    /// <summary>
    /// Configures a ScrollRect for vertical-only scrolling and returns
    /// its Content transform with correct anchors so GridLayoutGroup works.
    /// </summary>
    Transform SetupScrollRect(ScrollRect sr, string contentName)
    {
        if (sr == null) return null;

        sr.horizontal   = false;
        sr.vertical     = true;
        sr.movementType = ScrollRect.MovementType.Elastic;

        // Ensure Viewport has a Mask
        RectTransform viewport = sr.viewport;
        if (viewport == null)
        {
            // Some ScrollRects have no explicit viewport — use sr itself
            viewport = sr.GetComponent<RectTransform>();
        }
        if (!viewport.GetComponent<Mask>())
        {
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            if (!viewport.GetComponent<Image>())
                viewport.gameObject.AddComponent<Image>().color = new Color(1,1,1,0.01f);
        }

        // Find or create Content child
        RectTransform content = sr.content;
        if (content == null)
        {
            // Search viewport children
            for (int i = 0; i < viewport.childCount; i++)
            {
                var child = viewport.GetChild(i).GetComponent<RectTransform>();
                if (child != null && child.name == contentName)
                { content = child; break; }
            }
        }
        if (content == null)
        {
            var go = new GameObject(contentName, typeof(RectTransform));
            go.transform.SetParent(viewport, false);
            content = go.GetComponent<RectTransform>();
        }
        sr.content = content;

        // THE critical rect setup for vertical scrolling:
        // anchorMin/Max top-left stretch = content fills viewport width,
        // grows downward as children are added.
        content.anchorMin        = new Vector2(0f, 1f);
        content.anchorMax        = new Vector2(1f, 1f);
        content.pivot            = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta        = Vector2.zero;   // width tracks viewport

        return content;
    }

    // ── Validation ─────────────────────────────────────────────────────────

    bool ValidateRefs()
    {
        bool ok = true;
        void R(Object o, string n) { if (!o) { Debug.LogError($"[TemplateRecorderUI] '{n}' not assigned!"); ok = false; } }
        R(panelMainMenu,       "panelMainMenu");
        R(panelLetterPicker,   "panelLetterPicker");
        R(panelRecorder,       "panelRecorder");
        R(panelGallery,        "panelGallery");
        R(btnAddTemplate,      "btnAddTemplate");
        R(btnViewEdit,         "btnViewEdit");
        R(letterScrollRect,    "letterScrollRect");
        R(btnBackFromPicker,   "btnBackFromPicker");
        R(recorderDrawArea,    "recorderDrawArea");
        R(btnSaveSample,       "btnSaveSample");
        R(btnClearRecorder,    "btnClearRecorder");
        R(btnBackFromRecorder, "btnBackFromRecorder");
        R(galleryScrollRect,   "galleryScrollRect");
        R(letterTabBar,        "letterTabBar");
        R(btnBackFromGallery,  "btnBackFromGallery");
        R(templateLibrary,     "templateLibrary");
        if (!ok) Debug.LogError("[TemplateRecorderUI] Fix missing refs. UI stays hidden.");
        return ok;
    }

    void WireButtons()
    {
        btnAddTemplate      .onClick.AddListener(OpenLetterPicker);
        if (btnTestRecognition) btnTestRecognition.onClick.AddListener(OpenRecognitionTest);
        if (btnTestVoiceInput) btnTestVoiceInput.onClick.AddListener(OpenVoiceInputTest);
        btnViewEdit         .onClick.AddListener(OpenGallery);
        btnBackFromPicker   .onClick.AddListener(OpenMainMenu);
        btnSaveSample       .onClick.AddListener(SaveCurrentSample);
        if (btnTestSample) btnTestSample.onClick.AddListener(TestCurrentSample);
        btnClearRecorder    .onClick.AddListener(ClearRecorder);
        btnBackFromRecorder .onClick.AddListener(HandleBackFromRecorder);
        btnBackFromGallery  .onClick.AddListener(OpenMainMenu);
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public void OpenRecorderUI()
    {
        if (!_ready || IsOpen || GrimoireUI.IsOpen || !PauseMenuController.CanOpenBlockingModal)
            return;

        if (!PauseMenuController.TryBeginModalPause())
            return;

        _pauseLockHeld = true;
        IsOpen = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
        OpenMainMenu();
    }

    public void CloseRecorderUI()
    {
        if (!IsOpen && !_pauseLockHeld)
            return;

        IsOpen = false;
        if (_pauseLockHeld)
        {
            PauseMenuController.EndModalPause();
            _pauseLockHeld = false;
        }
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
        HideAll();
    }

    // ── Navigation ─────────────────────────────────────────────────────────

    void HideAll()
    {
        SetPanel(panelMainMenu,     false);
        SetPanel(panelLetterPicker, false);
        SetPanel(panelRecorder,     false);
        SetPanel(panelGallery,      false);
    }

    void OpenMainMenu()
    {
        _isTestMode = false;
        ApplyTheme();
        SetPanel(panelMainMenu,     true);
        SetPanel(panelLetterPicker, false);
        SetPanel(panelRecorder,     false);
        SetPanel(panelGallery,      false);
    }

    void OpenLetterPicker()
    {
        _isTestMode = false;
        ApplyTheme();
        SetPanel(panelMainMenu,     false);
        SetPanel(panelLetterPicker, true);
        SetPanel(panelRecorder,     false);
        SetPanel(panelGallery,      false);
        BuildLetterGrid();
        if (letterScrollRect) letterScrollRect.verticalNormalizedPosition = 1f;
    }

    void OpenRecorder(string letter)
    {
        _isTestMode = false;
        _currentLetter = letter;
        if (recorderDrawArea.rect.size.magnitude > 1f)
            _drawAreaSize = recorderDrawArea.rect.size;
        ClearRecorder();
        RefreshRecorderLabels();
        if (labelInstruction)
            labelInstruction.text =
                "Left-click to draw.\nRight-click to lift pen (multi-stroke letters like i, t, x).\nPress Save when happy with the shape.";
        ApplyTheme();
        SetPanel(panelMainMenu,     false);
        SetPanel(panelLetterPicker, false);
        SetPanel(panelRecorder,     true);
        SetPanel(panelGallery,      false);
        _voiceInputTest?.Close();
    }

    void OpenRecognitionTest()
    {
        _isTestMode = true;
        _currentLetter = "?";
        if (recorderDrawArea.rect.size.magnitude > 1f)
            _drawAreaSize = recorderDrawArea.rect.size;
        ClearRecorder();
        RefreshRecorderLabels();
        if (labelInstruction)
            labelInstruction.text =
                "Draw any letter.\nRight-click to lift pen for multi-stroke letters.\nPress Test to see what the recognizer thinks it is.";
        ApplyTheme();
        SetPanel(panelMainMenu,     false);
        SetPanel(panelLetterPicker, false);
        SetPanel(panelRecorder,     true);
        SetPanel(panelGallery,      false);
    }

    void OpenVoiceInputTest()
    {
        HideAll();
        _voiceInputTest?.Open();
    }

    void OpenGallery()
    {
        ApplyTheme();
        SetPanel(panelMainMenu,     false);
        SetPanel(panelLetterPicker, false);
        SetPanel(panelRecorder,     false);
        SetPanel(panelGallery,      true);

        var keys = templateLibrary.GetAllKeys();
        Debug.Log("Keys count: " + keys.Count);
        BuildGalleryTabs(keys);

        if (keys.Count == 0)
            ShowMsg("No templates saved yet.\nUse 'Add Template' to record some.");
        else
        {
            HideMsg();
            ShowGalleryFor(keys[0]);
        }
    }

    static void SetPanel(GameObject p, bool on) { if (p) p.SetActive(on); }

    void HandleBackFromRecorder()
    {
        if (_isTestMode) OpenMainMenu();
        else OpenLetterPicker();
    }

    // ── Letter grid ────────────────────────────────────────────────────────

    void BuildLetterGrid()
    {
        if (_letterContent == null) return;
        foreach (Transform c in _letterContent) Destroy(c.gameObject);

        letterColumns = LetterPickerColumnCount;
        Vector2 fittedButtonSize = ResolveLetterButtonSize(letterColumns);

        // GridLayoutGroup — always reconfigure so columns/size changes take effect
        var grid = _letterContent.GetComponent<GridLayoutGroup>()
                ?? _letterContent.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize        = fittedButtonSize;
        grid.spacing         = new Vector2(gridSpacing, gridSpacing);
        grid.padding         = new RectOffset(8, 8, 8, 8);
        grid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = letterColumns;
        grid.childAlignment  = TextAnchor.UpperLeft;
        grid.startCorner     = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis       = GridLayoutGroup.Axis.Horizontal;

        // ContentSizeFitter grows content height to fit all rows
        var csf = _letterContent.GetComponent<ContentSizeFitter>()
               ?? _letterContent.gameObject.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        foreach (char ch in chars)
        {
            string letter = ch.ToString();
            int    count  = templateLibrary.GetEntries(letter).Count;

            var btn = MakeButton(letter, _letterContent, fittedButtonSize);
            GameUiTheme.StyleButton(
                btn.GetComponent<Button>(),
                count > 0
                    ? GameUiTheme.ButtonRole.LetterSaved
                    : GameUiTheme.ButtonRole.LetterEmpty);

            if (count > 0)
            {
                var badge = MakeTMP("Badge", btn.transform,
                    new Vector2(0.5f, 0f), new Vector2(1f, 0.4f),
                    count.ToString(), 9f, TextAlignmentOptions.Center);
                badge.color = GameUiTheme.Gold;
            }

            btn.GetComponent<Button>().onClick.AddListener(() => OpenRecorder(letter));
        }

        if (_letterContent is RectTransform contentRect)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
        }
    }

    Vector2 ResolveLetterButtonSize(int columns)
    {
        Vector2 size = letterButtonSize;
        RectTransform viewport = letterScrollRect != null
            ? letterScrollRect.viewport != null
                ? letterScrollRect.viewport
                : letterScrollRect.GetComponent<RectTransform>()
            : null;

        if (viewport != null && viewport.rect.width > 1f)
        {
            float availableWidth = viewport.rect.width - 16f - gridSpacing * Mathf.Max(0, columns - 1);
            float fittedWidth = Mathf.Floor(availableWidth / Mathf.Max(1, columns));
            if (fittedWidth > 24f)
            {
                float squareSize = Mathf.Min(size.x, fittedWidth);
                size = new Vector2(squareSize, Mathf.Min(size.y, squareSize));
            }
        }

        return new Vector2(Mathf.Max(44f, size.x), Mathf.Max(44f, size.y));
    }

    // ── Recorder input ─────────────────────────────────────────────────────

    void HandleRecorderInput()
    {
        bool began = false, held = false, ended = false;
        Vector3 sp = Vector3.zero;

#if UNITY_ANDROID && !UNITY_EDITOR
        if (Input.touchCount > 0)
        {
            var t = Input.GetTouch(0);
            began = t.phase == TouchPhase.Began;
            held  = t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary;
            ended = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled;
            sp    = t.position;
        }
#else
        began = Input.GetMouseButtonDown(0);
        held  = Input.GetMouseButton(0);
        ended = Input.GetMouseButtonUp(0);
        sp    = Input.mousePosition;
#endif

        if ((began || held) &&
            !RectTransformUtility.RectangleContainsScreenPoint(recorderDrawArea, sp, null))
            return;

        if (began)
        {
            _isDrawing    = true;
            _activeStroke = new List<Vector2>();
            _recordedStrokes.Add(_activeStroke);
            _captureSession.BeginStroke();
            if (ToLocal(sp, out Vector2 lp)) { _activeStroke.Add(lp); _captureSession.AddPoint(lp); SpawnDot(lp); }
        }

        if (held && _isDrawing && ToLocal(sp, out Vector2 hp))
        {
            Vector2 last = _activeStroke.Count > 0 ? _activeStroke[^1] : hp;
            if (Vector2.Distance(last, hp) > 1.5f) { SpawnSegment(last, hp);/*Vector2 mid = (last + hp) * 0.5f*/;_activeStroke.Add(hp); _captureSession.AddPoint(hp); }
        }

        if (ended) _isDrawing = false;

        // Right-click = lift pen, start new stroke
        if (Input.GetMouseButtonDown(1))
        {
            _isDrawing    = false;
            _activeStroke = new List<Vector2>();
            _recordedStrokes.Add(_activeStroke);
        }
    }

    bool ToLocal(Vector3 screen, out Vector2 local)
        => RectTransformUtility.ScreenPointToLocalPointInRectangle(
               recorderDrawArea, screen, null, out local);

    void SpawnDot(Vector2 pos)
    {
        var go = MakeSegGO(recorderDrawArea);
        var rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta        = new Vector2(strokeThickness, strokeThickness);
        BrushStrokeStyle.ApplyDot(go.GetComponent<Image>(), strokeColour);
        _liveSegments.Add(go);
    }

    void SpawnSegment(Vector2 from, Vector2 to)
    {
        var dir = to - from;
        if (dir.magnitude < 0.5f) return;
        var go = MakeSegGO(recorderDrawArea);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta        = new Vector2(dir.magnitude, strokeThickness);
        rt.anchoredPosition = from + dir * 0.5f;
        rt.localRotation    = Quaternion.Euler(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        BrushStrokeStyle.ApplySegment(go.GetComponent<Image>(), strokeColour);
        _liveSegments.Add(go);
    }

    void ClearRecorder()
    {
        _recordedStrokes.Clear();
        _captureSession.Reset();
        _activeStroke = new List<Vector2>();
        _isDrawing    = false;
        foreach (var go in _liveSegments) if (go) Destroy(go);
        _liveSegments.Clear();
        RefreshRecorderLabels();
    }

    void RefreshRecorderLabels()
    {
        if (labelCurrentLetter)
            labelCurrentLetter.text = _isTestMode
                ? "Recognition Test"
                : $"Drawing:  {_currentLetter}";
        if (labelSampleCount && templateLibrary)
        {
            if (_isTestMode)
            {
                labelSampleCount.text = "Draw and press Test";
            }
            else
            {
                int n = templateLibrary.GetEntries(_currentLetter).Count;
                labelSampleCount.text = $"{n} sample{(n == 1 ? "" : "s")} saved";
            }
        }
    }

    // ── Save ───────────────────────────────────────────────────────────────

    void SaveCurrentSample()
    {
        _recordedStrokes.RemoveAll(s => s.Count < 2);
        if (_recordedStrokes.Count == 0)
        {
            Debug.LogWarning("[TemplateRecorderUI] Nothing to save — draw first.");
            return;
        }
        if (recorderDrawArea.rect.size.magnitude > 1f)
            _drawAreaSize = recorderDrawArea.rect.size;
        HandwritingSampleRecord sample = _captureSession.Build(
            _currentLetter,
            recorderDrawArea.rect,
            datasetWriterId,
            datasetSessionId,
            "unity_template_recorder");
        sample.labelStatus = "author_accepted_template";
        sample.writerAgeBand = datasetWriterAgeBand?.Trim() ?? "";
        sample.handedness = string.IsNullOrWhiteSpace(datasetHandedness) ? "unknown" : datasetHandedness.Trim();
        sample.collectionCohort = datasetCollectionCohort?.Trim() ?? "";
        sample.consentReference = datasetConsentReference?.Trim() ?? "";
        sample.guideVisible = false;
        sample.tracing = false;
        templateLibrary.Add(_currentLetter, _recordedStrokes, sample);
        List<Vector2> allPoints = new List<Vector2>();
        foreach (var stroke in _recordedStrokes)
            allPoints.AddRange(stroke);

        LogBounds(allPoints, "Recorder");

        Debug.Log($"[TemplateRecorderUI] Saved '{_currentLetter}' — " +
                  $"total: {templateLibrary.GetEntries(_currentLetter).Count}");
        ClearRecorder();
    }

    // ── Gallery tabs ───────────────────────────────────────────────────────

    void TestCurrentSample()
    {
        _recordedStrokes.RemoveAll(s => s.Count < 2);
        if (_recordedStrokes.Count == 0)
        {
            if (labelInstruction)
                labelInstruction.text = "Draw something first, then press Test.";
            return;
        }

        if (recognizerHost == null)
        {
            if (labelInstruction)
                labelInstruction.text = "Recognizer not assigned.";
            return;
        }

        var points = new List<PDollarRecognizer.Point>();
        for (int strokeIndex = 0; strokeIndex < _recordedStrokes.Count; strokeIndex++)
        {
            foreach (Vector2 point in _recordedStrokes[strokeIndex])
                points.Add(new PDollarRecognizer.Point(point.x, point.y, strokeIndex));
        }

        PDollarRecognizer.RecognitionResult result = recognizerHost.Recognize(points);
        string recognized = string.IsNullOrEmpty(result.name) ? "Unknown" : result.name;
        string bestCandidate = string.IsNullOrEmpty(result.bestCandidateName) ? recognized : result.bestCandidateName;
        string detail = result.runnerUpScore < float.MaxValue
            ? $"  runner-up {result.runnerUpName} ({result.runnerUpScore:F1})"
            : "";

        if (labelSampleCount)
            labelSampleCount.text = $"Recognized: {recognized}";
        if (labelInstruction)
            labelInstruction.text = result.isAmbiguous
                ? $"Recognizer result: ambiguous  best {bestCandidate} ({result.score:F1}){detail}"
                : $"Recognizer result: {recognized}  (score {result.score:F1}){detail}";
    }

    void BuildGalleryTabs(List<string> keys)
    {
        foreach (Transform c in letterTabBar) Destroy(c.gameObject);
        if (keys.Count == 0) return;

        var hlg = letterTabBar.GetComponent<HorizontalLayoutGroup>()
               ?? letterTabBar.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing              = 4f;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;
        hlg.childAlignment       = TextAnchor.MiddleLeft;
        hlg.padding              = new RectOffset(4, 4, 2, 100);

        var csf = letterTabBar.GetComponent<ContentSizeFitter>()
               ?? letterTabBar.gameObject.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        foreach (string key in keys)
        {
            string captured = key;
            int    count    = templateLibrary.GetEntries(key).Count;
            var btn = MakeButton($"{key} ({count})", letterTabBar, new Vector2(72f, 36f));
            btn.GetComponent<Button>().onClick.AddListener(() => ShowGalleryFor(captured));
        }
    }

    // ── Gallery cards ──────────────────────────────────────────────────────

    void ShowGalleryFor(string letter)
    {
        if (string.IsNullOrEmpty(letter) || _galleryContent == null) return;
        _galleryLetter = letter;

        foreach (Transform c in _galleryContent) Destroy(c.gameObject);

        var entries = templateLibrary.GetEntries(letter);
        if (entries.Count == 0) { ShowMsg($"No samples saved for '{letter}'."); return; }
        HideMsg();

        galleryColumns = GalleryColumnCount;
        int cols = GalleryColumnCount;

        var grid = _galleryContent.GetComponent<GridLayoutGroup>()
                ?? _galleryContent.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize        = cardSize;
        grid.spacing         = new Vector2(gridSpacing, gridSpacing);
        grid.padding         = new RectOffset(8, 8, 8, 8);
        grid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = cols;
        grid.childAlignment  = TextAnchor.UpperLeft;
        grid.startCorner     = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis       = GridLayoutGroup.Axis.Horizontal;

        var csf = _galleryContent.GetComponent<ContentSizeFitter>()
               ?? _galleryContent.gameObject.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        // The preview area is 62% of the card height, full width minus small padding
        Vector2 previewRenderSize = new Vector2(
            cardSize.x - 8f,
            cardSize.y * 0.62f - 4f);

        foreach (var entry in entries)
            BuildCard(_galleryContent, entry, previewRenderSize);

        if (galleryScrollRect) galleryScrollRect.verticalNormalizedPosition = 1f;
    }

    void BuildCard(Transform parent,
                   TemplateLibrary.TemplateEntry entry,
                   Vector2 previewRenderSize)
    {   
        if (entry == null || entry.points == null)
            {
                Debug.LogWarning("Skipping invalid template entry");
                return;
            }
        string eid = entry.id;

        // Card root — GridLayoutGroup sizes this, no sizeDelta needed
        var card = new GameObject("Card", typeof(RectTransform), typeof(Image));
        card.transform.SetParent(parent, false);
        card.GetComponent<Image>().color = GameUiTheme.PanelRaised;
        var cardOutline = card.AddComponent<Outline>();
        cardOutline.effectColor = new Color(1f, 1f, 1f, 0.08f);
        cardOutline.effectDistance = new Vector2(1f, -1f);

        // Preview background (top 62%)
        var prvGO = new GameObject("Preview", typeof(RectTransform), typeof(Image));
        prvGO.transform.SetParent(card.transform, false);
        prvGO.GetComponent<Image>().color        = new Color(0.96f, 0.94f, 0.86f, 1f);
        prvGO.GetComponent<Image>().raycastTarget = false;
        var prvRT = prvGO.GetComponent<RectTransform>();
        prvRT.anchorMin = new Vector2(0f, 0.38f);
        prvRT.anchorMax = Vector2.one;
        prvRT.offsetMin = new Vector2( 3f,  2f);
        prvRT.offsetMax = new Vector2(-3f, -2f);

        // StrokePreviewRenderer — pass previewRenderSize explicitly
        // so it doesn't need to wait for a layout pass
        var spr = prvGO.AddComponent<StrokePreviewRenderer>();
        spr.strokeColour    = GameUiTheme.Stroke;
        spr.strokeThickness = Mathf.Max(3f, strokeThickness * 0.6f);
        spr.SetFromRaw(entry.points, _drawAreaSize, previewRenderSize);

        string metaText = entry.isPrime
            ? "PRIME"
            : entry.isAdaptive
                ? $"Match {Mathf.RoundToInt(entry.closenessScore * 100f)}%"
                : entry.createdAt.Length > 15
                    ? entry.createdAt.Substring(11, 5)
                    : entry.createdAt;

        // Metadata label (middle strip 19-38%)
        MakeTMP("Date", card.transform,
            new Vector2(0f, 0.19f), new Vector2(1f, 0.37f),
            metaText,
            9f, TextAlignmentOptions.Center);

        // Prime button (bottom left)
        var primeGO = new GameObject("Prime", typeof(RectTransform), typeof(Image), typeof(Button));
        primeGO.transform.SetParent(card.transform, false);
        var primeRT = primeGO.GetComponent<RectTransform>();
        primeRT.anchorMin = new Vector2(0.04f, 0.02f);
        primeRT.anchorMax = new Vector2(0.48f, 0.18f);
        primeRT.offsetMin = primeRT.offsetMax = Vector2.zero;
        MakeTMP("L", primeGO.transform, Vector2.zero, Vector2.one,
            "Prime", 10f, TextAlignmentOptions.Center);
        GameUiTheme.StyleButton(
            primeGO.GetComponent<Button>(),
            entry.isPrime ? GameUiTheme.ButtonRole.Primary : GameUiTheme.ButtonRole.Prime);
        primeGO.GetComponent<Button>().interactable = !entry.isPrime;
        primeGO.GetComponent<Button>().onClick.AddListener(() =>
        {
            templateLibrary.SetPrimeTemplate(eid);
            ShowGalleryFor(_galleryLetter);
        });

        // Delete button (bottom right)
        var delGO = new GameObject("Del", typeof(RectTransform), typeof(Image), typeof(Button));
        delGO.transform.SetParent(card.transform, false);
        var delRT = delGO.GetComponent<RectTransform>();
        delRT.anchorMin = new Vector2(0.52f, 0.02f);
        delRT.anchorMax = new Vector2(0.96f, 0.18f);
        delRT.offsetMin = delRT.offsetMax = Vector2.zero;
        MakeTMP("L", delGO.transform, Vector2.zero, Vector2.one,
            "Delete", 10f, TextAlignmentOptions.Center);
        GameUiTheme.StyleButton(delGO.GetComponent<Button>(), GameUiTheme.ButtonRole.Danger);
        delGO.GetComponent<Button>().onClick.AddListener(() =>
        {
            templateLibrary.Delete(eid);
            ShowGalleryFor(_galleryLetter);
        });
    }

    // ── UI helpers ─────────────────────────────────────────────────────────

    GameObject MakeButton(string label, Transform parent, Vector2 size)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        go.GetComponent<RectTransform>().sizeDelta = size;
        var le = go.AddComponent<LayoutElement>();
        le.minWidth = le.preferredWidth   = size.x;
        le.minHeight = le.preferredHeight = size.y;
        MakeTMP("L", go.transform, Vector2.zero, Vector2.one,
            label, size.y * 0.33f, TextAlignmentOptions.Center);
        GameUiTheme.StyleButton(go.GetComponent<Button>(), GameUiTheme.ButtonRole.Secondary);
        return go;
    }

    void EnsureOptionalButtons()
    {
        if (!btnTestRecognition && btnAddTemplate)
            btnTestRecognition = CreateSiblingButton(btnAddTemplate, "Test Recognition", new Vector2(0f, -62f));

        if (!btnTestVoiceInput && btnAddTemplate)
            btnTestVoiceInput = CreateSiblingButton(btnAddTemplate, "Test Voice Input", new Vector2(0f, -124f));

        if (!btnTestSample && btnSaveSample)
            btnTestSample = CreateSiblingButton(btnSaveSample, "Test", new Vector2(0f, -56f));

        if (_voiceInputTest == null)
        {
            _voiceInputTest = GetComponent<VoiceInputTestUI>() ?? gameObject.AddComponent<VoiceInputTestUI>();
            Transform parent = panelMainMenu != null ? panelMainMenu.transform.parent : transform;
            _voiceInputTest.Build(parent, OpenMainMenu);
        }
    }

    Button CreateSiblingButton(Button source, string label, Vector2 anchoredOffset)
    {
        RectTransform sourceRect = source != null ? source.GetComponent<RectTransform>() : null;
        if (sourceRect == null || sourceRect.parent == null)
            return null;

        var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(sourceRect.parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = sourceRect.anchorMin;
        rt.anchorMax = sourceRect.anchorMax;
        rt.pivot = sourceRect.pivot;
        rt.sizeDelta = sourceRect.sizeDelta;
        rt.anchoredPosition = sourceRect.anchoredPosition + anchoredOffset;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;

        MakeTMP("L", go.transform, Vector2.zero, Vector2.one,
            label, Mathf.Max(18f, rt.sizeDelta.y * 0.32f), TextAlignmentOptions.Center);
        return go.GetComponent<Button>();
    }

    static TextMeshProUGUI MakeTMP(string name, Transform parent,
        Vector2 ancMin, Vector2 ancMax,
        string text, float size, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = ancMin; rt.anchorMax = ancMax;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = size; tmp.alignment = align;
        tmp.raycastTarget = false;
        return tmp;
    }

    static GameObject MakeSegGO(RectTransform parent)
    {
        var go = new GameObject("s", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().raycastTarget = false;
        return go;
    }

    void ShowMsg(string msg)
    {
        if (labelGalleryEmpty)
        { labelGalleryEmpty.text = msg; labelGalleryEmpty.gameObject.SetActive(true); }
        else Debug.Log($"[TemplateRecorderUI] {msg}");
    }

    void HideMsg()
    {
        if (labelGalleryEmpty) labelGalleryEmpty.gameObject.SetActive(false);
    }

    void OnLibraryChanged()
    {
        if (recognizerHost && templateLibrary)
            recognizerHost.ReloadFromLibrary(templateLibrary);
        if (panelRecorder && panelRecorder.activeInHierarchy)
            RefreshRecorderLabels();
        if (panelGallery && panelGallery.activeInHierarchy && !string.IsNullOrEmpty(_galleryLetter))
            ShowGalleryFor(_galleryLetter);
        if (panelLetterPicker && panelLetterPicker.activeInHierarchy)
            BuildLetterGrid();
    }

    void ApplyTheme()
    {
        GameUiTheme.StylePanel(panelMainMenu);
        GameUiTheme.StylePanel(panelLetterPicker);
        GameUiTheme.StylePanel(panelRecorder);
        GameUiTheme.StylePanel(panelGallery);
        GameUiTheme.StyleDrawingSurface(recorderDrawArea, recorder: true);
        GameUiTheme.StyleScrollRect(letterScrollRect);
        GameUiTheme.StyleScrollRect(galleryScrollRect);
        GameUiTheme.StyleButton(btnAddTemplate, GameUiTheme.ButtonRole.Primary);
        GameUiTheme.StyleButton(btnTestRecognition, GameUiTheme.ButtonRole.Secondary);
        GameUiTheme.StyleButton(btnTestVoiceInput, GameUiTheme.ButtonRole.Secondary);
        GameUiTheme.StyleButton(btnViewEdit, GameUiTheme.ButtonRole.Secondary);
        GameUiTheme.StyleButton(btnBackFromPicker, GameUiTheme.ButtonRole.Quiet);
        GameUiTheme.StyleButton(btnSaveSample, GameUiTheme.ButtonRole.Primary);
        GameUiTheme.StyleButton(btnTestSample, GameUiTheme.ButtonRole.Secondary);
        GameUiTheme.StyleButton(btnClearRecorder, GameUiTheme.ButtonRole.Danger);
        GameUiTheme.StyleButton(btnBackFromRecorder, GameUiTheme.ButtonRole.Quiet);
        GameUiTheme.StyleButton(btnBackFromGallery, GameUiTheme.ButtonRole.Quiet);
        GameUiTheme.StyleText(labelCurrentLetter, 30f);
        GameUiTheme.StyleText(labelSampleCount, 18f);
        GameUiTheme.StyleText(labelInstruction, 17f);
        GameUiTheme.StyleText(labelGalleryEmpty, 20f);
        if (labelCurrentLetter) labelCurrentLetter.color = GameUiTheme.Text;
        if (labelSampleCount) labelSampleCount.color = _isTestMode ? GameUiTheme.Accent : GameUiTheme.Gold;
        if (labelInstruction) labelInstruction.color = GameUiTheme.TextMuted;
        if (labelGalleryEmpty) labelGalleryEmpty.color = GameUiTheme.TextMuted;
        if (btnSaveSample) btnSaveSample.gameObject.SetActive(!_isTestMode);
        if (btnTestSample) btnTestSample.gameObject.SetActive(_isTestMode);
        GameUiTheme.StyleAllText(transform);
    }
}
