using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class MobileControlsOverlay : MonoBehaviour
{
    const float ButtonAlpha = 0.84f;

    Canvas canvas;
    GameObject gameplayRoot;
    MobileJoystick moveJoystick;
    MobileLookPad lookPad;
    PlayerController player;
    PauseMenuController pauseMenu;

    public static bool GameplayControlsVisible { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindAnyObjectByType<MobileControlsOverlay>() != null)
            return;

        var go = new GameObject("MobileControlsOverlay");
        DontDestroyOnLoad(go);
        go.AddComponent<MobileControlsOverlay>();
    }

    void Awake()
    {
        EnsureEventSystem();
        BuildUi();
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    void OnDestroy()
    {
        GameplayControlsVisible = false;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ResolveReferences();
    }

    void Update()
    {
        ResolveReferences();
        bool controlsAllowed = GameSettings.TouchControlsEnabled && HasTouchScreen();
        bool gameplayAvailable = player != null;

        if (gameplayRoot != null)
            gameplayRoot.SetActive(controlsAllowed && gameplayAvailable && !PauseMenuController.IsPaused && !RunEndScreenController.IsOpen);

        GameplayControlsVisible = gameplayRoot != null && gameplayRoot.activeSelf;

        if (!controlsAllowed && player != null)
            player.SetMobileMoveInput(Vector2.zero);
    }

    void ResolveReferences()
    {
        player = FindAnyObjectByType<PlayerController>();
        pauseMenu = FindAnyObjectByType<PauseMenuController>();

        if (moveJoystick != null)
            moveJoystick.Player = player;
        if (lookPad != null)
            lookPad.Player = player;
    }

    void BuildUi()
    {
        var canvasGo = new GameObject("MobileControlsCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        GameUiTheme.ConfigureStandardGameplayCanvasScaler(scaler);

        gameplayRoot = new GameObject("GameplayControls", typeof(RectTransform));
        gameplayRoot.transform.SetParent(canvasGo.transform, false);
        Stretch(gameplayRoot.GetComponent<RectTransform>());

        lookPad = MakeLookSurface(gameplayRoot.transform, "LookSurface");
        moveJoystick = MakeJoystick(gameplayRoot.transform, "MoveStick", new Vector2(190f, 185f), 210f);

        MakeButton(gameplayRoot.transform, "Jump", "Jump", new Vector2(-150f, 200f), new Vector2(150f, 84f), () => player?.MobileJump());
        MakeHoldButton(gameplayRoot.transform, "SpeakCast", "Speak", new Vector2(-150f, 96f), new Vector2(170f, 92f),
            () => player?.MobileBeginVoiceCast(),
            () => player?.MobileEndVoiceCast());
        MakeButton(gameplayRoot.transform, "Pause", "Pause", new Vector2(-112f, -72f), new Vector2(150f, 70f), () => pauseMenu?.TogglePauseFromButton(), topRight: true);

        gameplayRoot.SetActive(false);
    }

    static void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null)
            return;

        var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        DontDestroyOnLoad(go);
    }

    static MobileJoystick MakeJoystick(Transform parent, string name, Vector2 anchoredPosition, float size)
    {
        var root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(MobileJoystick));
        root.transform.SetParent(parent, false);
        var rect = root.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = new Vector2(size, size);
        root.GetComponent<Image>().color = new Color(0.05f, 0.08f, 0.1f, 0.42f);

        var label = MakeLabel(root.transform, "Move", 28f);
        label.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        label.rectTransform.anchoredPosition = Vector2.zero;

        var knob = new GameObject("Knob", typeof(RectTransform), typeof(Image));
        knob.transform.SetParent(root.transform, false);
        var knobRect = knob.GetComponent<RectTransform>();
        knobRect.anchorMin = new Vector2(0.5f, 0.5f);
        knobRect.anchorMax = new Vector2(0.5f, 0.5f);
        knobRect.pivot = new Vector2(0.5f, 0.5f);
        knobRect.sizeDelta = new Vector2(size * 0.36f, size * 0.36f);
        knob.GetComponent<Image>().color = new Color(0.92f, 0.98f, 1f, 0.72f);

        var joystick = root.GetComponent<MobileJoystick>();
        joystick.Knob = knobRect;
        joystick.Radius = size * 0.38f;
        return joystick;
    }

    static MobileLookPad MakeLookSurface(Transform parent, string name)
    {
        var root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(MobileLookPad));
        root.transform.SetParent(parent, false);
        var rect = root.GetComponent<RectTransform>();
        Stretch(rect);
        root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
        return root.GetComponent<MobileLookPad>();
    }

    static Button MakeButton(Transform parent, string name, string text, Vector2 anchoredPosition, Vector2 size, UnityEngine.Events.UnityAction action, bool topRight = false, Vector2? anchorOverride = null)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        Vector2 anchor = anchorOverride ?? (topRight ? Vector2.one : new Vector2(1f, 0f));
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        var image = go.GetComponent<Image>();
        image.color = new Color(0.07f, 0.09f, 0.12f, ButtonAlpha);

        Button button = go.GetComponent<Button>();
        button.onClick.AddListener(action);
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(0.14f, 0.2f, 0.25f, ButtonAlpha);
        colors.pressedColor = new Color(0.03f, 0.52f, 0.72f, ButtonAlpha);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;

        var label = MakeLabel(go.transform, text, 27f);
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = Vector2.zero;
        label.rectTransform.offsetMax = Vector2.zero;
        return button;
    }

    static void MakeHoldButton(Transform parent, string name, string text, Vector2 anchoredPosition, Vector2 size, UnityEngine.Events.UnityAction down, UnityEngine.Events.UnityAction up)
    {
        Button button = MakeButton(parent, name, text, anchoredPosition, size, () => { });
        var hold = button.gameObject.AddComponent<MobileHoldButton>();
        hold.PointerDown = down;
        hold.PointerUp = up;
    }

    static TextMeshProUGUI MakeLabel(Transform parent, string text, float size)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var label = go.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = size;
        label.enableAutoSizing = true;
        label.fontSizeMin = 14f;
        label.fontSizeMax = size;
        label.color = new Color(0.96f, 0.98f, 1f, 0.96f);
        label.raycastTarget = false;
        return label;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    static bool HasTouchScreen()
    {
        if (Touchscreen.current != null && Touchscreen.current.enabled)
            return true;

        return Input.touchSupported;
    }

    sealed class MobileHoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public UnityEngine.Events.UnityAction PointerDown;
        public UnityEngine.Events.UnityAction PointerUp;
        bool held;

        public void OnPointerDown(PointerEventData eventData)
        {
            held = true;
            PointerDown?.Invoke();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            Release();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            Release();
        }

        void Release()
        {
            if (!held)
                return;
            held = false;
            PointerUp?.Invoke();
        }
    }

    sealed class MobileJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public RectTransform Knob;
        public PlayerController Player;
        public float Radius = 80f;
        RectTransform rect;

        void Awake()
        {
            rect = GetComponent<RectTransform>();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            UpdateInput(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            UpdateInput(eventData);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (Knob != null)
                Knob.anchoredPosition = Vector2.zero;
            Player?.SetMobileMoveInput(Vector2.zero);
        }

        void UpdateInput(PointerEventData eventData)
        {
            if (rect == null)
                return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, eventData.position, eventData.pressEventCamera, out Vector2 local);
            Vector2 clamped = Vector2.ClampMagnitude(local, Radius);
            if (Knob != null)
                Knob.anchoredPosition = clamped;
            Player?.SetMobileMoveInput(clamped / Mathf.Max(1f, Radius));
        }
    }

    sealed class MobileLookPad : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        public PlayerController Player;
        public float Sensitivity = 0.055f;

        public void OnBeginDrag(PointerEventData eventData)
        {
            eventData.Use();
        }

        public void OnDrag(PointerEventData eventData)
        {
            Player?.AddMobileLookInput(eventData.delta * Sensitivity);
            eventData.Use();
        }
    }
}
