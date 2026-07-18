using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class RunEndScreenController : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    RunProgressionManager runProgression;
    GameObject root;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "MainMenu" || FindAnyObjectByType<RunEndScreenController>() != null)
            return;

        new GameObject("RunEndScreenController").AddComponent<RunEndScreenController>();
    }

    void Awake()
    {
        runProgression = RunProgressionManager.EnsureExists();
        runProgression.OnRunEnded += Show;
    }

    void OnDestroy()
    {
        if (runProgression != null)
            runProgression.OnRunEnded -= Show;
        Close();
    }

    void Show(RunSummary summary)
    {
        if (summary == null || IsOpen)
            return;

        IsOpen = true;
        Time.timeScale = 0f;
        UnlockCursorForRunEnd();
        StopActiveSpeech();

        Canvas canvas = CreateCanvas();
        root = MakePanel(canvas.transform);
        Transform content = root.transform.GetChild(0);
        bool victory = summary.reason == RunEndReason.Victory;
        bool schoolMode = summary.configuredDurationSeconds > 0;
        MakeLabel(
            content,
            schoolMode ? "World Goal Practice Complete" : victory ? "Run Complete" : "Run Over",
            38f,
            summary.reason == RunEndReason.Defeat ? GameUiTheme.Danger : GameUiTheme.Gold);
        MakeLabel(content, schoolMode ? BuildSchoolSummary(summary) : BuildLegacySummary(summary), 21f, GameUiTheme.Text);
        if (runProgression.SandboxModeActive)
            MakeButton(content, "Restart Sandbox", StartNewRun, GameUiTheme.ButtonRole.Primary);
        else if (!schoolMode)
            MakeButton(content, "Start New Run", StartNewRun, GameUiTheme.ButtonRole.Primary);
        MakeButton(content, schoolMode && !runProgression.SandboxModeActive ? "Continue to Menu" : "Main Menu", ReturnToMainMenu, GameUiTheme.ButtonRole.Quiet);
    }

    static string BuildSchoolSummary(RunSummary summary)
    {
        int practicedSeconds = Mathf.RoundToInt(summary.elapsedSeconds);
        return
            $"Time practiced: {FormatSeconds(practicedSeconds)} / {FormatSeconds(summary.configuredDurationSeconds)}\n" +
            $"World areas practiced: {summary.subarenasCleared}\n" +
            $"Practice loops completed: {summary.fullLoopsCleared}\n" +
            $"Grammar creatures cleared: {summary.enemiesDefeated}\n" +
            $"Rewards earned: {summary.coinsCollected}";
    }

    static string BuildLegacySummary(RunSummary summary)
    {
        return
            $"Stages cleared: {summary.stagesCompleted}\n" +
            $"Enemies defeated: {summary.enemiesDefeated}\n" +
            $"Coins collected: {summary.coinsCollected}\n" +
            $"Upgrades purchased: {summary.upgradesPurchased}";
    }

    static string FormatSeconds(int seconds)
    {
        seconds = Mathf.Max(0, seconds);
        return $"{seconds / 60:0}:{seconds % 60:00}";
    }

    void Update()
    {
        if (IsOpen)
            UnlockCursorForRunEnd();
    }

    void StartNewRun()
    {
        Close();
        if (runProgression.SandboxModeActive)
            runProgression.StartSandboxRun();
        else
            runProgression.StartWorldGoalPractice();
    }

    void ReturnToMainMenu()
    {
        Close();
        runProgression.ReturnToMainMenu();
    }

    void Close()
    {
        if (root != null)
            Destroy(root.transform.parent.gameObject);
        root = null;
        IsOpen = false;
        Time.timeScale = 1f;
    }

    static void StopActiveSpeech()
    {
        foreach (VoiceUnlockRecognizer recognizer in FindObjectsByType<VoiceUnlockRecognizer>(FindObjectsInactive.Exclude))
            recognizer.StopListening();
    }

    static void UnlockCursorForRunEnd()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    static Canvas CreateCanvas()
    {
        var go = new GameObject("RunEndCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1100;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        EnsureEventSystem();
        return canvas;
    }

    static void EnsureEventSystem()
    {
        EventSystem eventSystem = FindAnyObjectByType<EventSystem>();
        if (eventSystem == null)
        {
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            DontDestroyOnLoad(go);
            return;
        }

        if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
        {
            StandaloneInputModule standalone = eventSystem.GetComponent<StandaloneInputModule>();
            if (standalone != null)
                Destroy(standalone);

            eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
        }
    }

    static GameObject MakePanel(Transform parent)
    {
        var shade = new GameObject("RunEndShade", typeof(RectTransform), typeof(Image));
        shade.transform.SetParent(parent, false);
        var shadeRt = shade.GetComponent<RectTransform>();
        shadeRt.anchorMin = Vector2.zero;
        shadeRt.anchorMax = Vector2.one;
        shadeRt.offsetMin = shadeRt.offsetMax = Vector2.zero;
        shade.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.82f);

        var panel = new GameObject("RunEndPanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        panel.transform.SetParent(shade.transform, false);
        var rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(520f, 500f);
        GameUiTheme.StylePanel(panel);
        var layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(42, 42, 36, 36);
        layout.spacing = 18f;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        return shade;
    }

    static Button MakeButton(Transform parent, string value, UnityEngine.Events.UnityAction action, GameUiTheme.ButtonRole role)
    {
        var go = new GameObject(value, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = 56f;
        var button = go.GetComponent<Button>();
        button.onClick.AddListener(action);
        GameUiTheme.StyleButton(button, role);
        MakeLabel(go.transform, value, 19f, GameUiTheme.Text);
        return button;
    }

    static TextMeshProUGUI MakeLabel(Transform parent, string value, float size, Color color)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = value.Contains("\n") ? 170f : size + 24f;
        var label = go.GetComponent<TextMeshProUGUI>();
        label.text = value;
        label.alignment = TextAlignmentOptions.Center;
        label.color = color;
        label.raycastTarget = false;
        GameUiTheme.StyleText(label, size, parent.GetComponent<Button>() != null);
        return label;
    }
}
