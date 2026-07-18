using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class PauseMenuController : MonoBehaviour
{
    GameObject pausePanel;
    SettingsMenu settingsMenu;
    bool paused;

    static int modalPauseLocks;
    static bool pauseMenuOpen;

    public static bool IsPaused => pauseMenuOpen || modalPauseLocks > 0;
    public static bool IsPauseMenuOpen => pauseMenuOpen;
    public static bool IsModalPauseActive => modalPauseLocks > 0;
    public static bool CanOpenBlockingModal => !pauseMenuOpen && modalPauseLocks == 0 && !RunEndScreenController.IsOpen;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        GameSettings.ApplySavedDisplaySettings();
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "MainMenu" || FindAnyObjectByType<PauseMenuController>() != null)
            return;
        new GameObject("PauseMenuController").AddComponent<PauseMenuController>();
    }

    void Awake()
    {
        pauseMenuOpen = false;
        modalPauseLocks = 0;
        Time.timeScale = 1f;
        Canvas canvas = CreateCanvas();
        pausePanel = MakePanel(canvas.transform);
        Transform content = pausePanel.transform.GetChild(0);
        MakeLabel(content, "Paused", 34f);
        MakeButton(content, "Resume", Resume, GameUiTheme.ButtonRole.Primary);
        MakeButton(content, "Map", OpenMap, GameUiTheme.ButtonRole.Secondary);
        MakeButton(content, "Settings", OpenSettings, GameUiTheme.ButtonRole.Secondary);
        MakeButton(content, "Main Menu", GoToMainMenu, GameUiTheme.ButtonRole.Quiet);
        settingsMenu = gameObject.AddComponent<SettingsMenu>();
        settingsMenu.Build(canvas.transform, () => pausePanel.SetActive(true));
        pausePanel.SetActive(false);
    }

    void Update()
    {
        if (RunEndScreenController.IsOpen || modalPauseLocks > 0)
            return;

        if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame)
            return;
        if (!paused)
            Pause();
        else if (settingsMenu.IsOpen)
        {
            settingsMenu.Close();
            pausePanel.SetActive(true);
        }
        else
            Resume();
    }

    public void TogglePauseFromButton()
    {
        if (RunEndScreenController.IsOpen || modalPauseLocks > 0)
            return;

        if (!paused)
            Pause();
        else if (settingsMenu.IsOpen)
        {
            settingsMenu.Close();
            pausePanel.SetActive(true);
        }
        else
            Resume();
    }

    void Pause()
    {
        if (RunEndScreenController.IsOpen || modalPauseLocks > 0)
            return;

        paused = true;
        pauseMenuOpen = true;
        Time.timeScale = 0f;
        foreach (VoiceUnlockRecognizer recognizer in FindObjectsByType<VoiceUnlockRecognizer>(FindObjectsInactive.Exclude))
            recognizer.StopListening();
        pausePanel.SetActive(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void Resume()
    {
        settingsMenu.Close();
        pausePanel.SetActive(false);
        paused = false;
        pauseMenuOpen = false;
        if (modalPauseLocks == 0 && !RunEndScreenController.IsOpen)
            Time.timeScale = 1f;
        bool gameplayScene = FindAnyObjectByType<PlayerController>() != null;
        Cursor.lockState = gameplayScene ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !gameplayScene;
    }

    void OpenSettings()
    {
        pausePanel.SetActive(false);
        settingsMenu.Open();
    }

    void OpenMap()
    {
        GrammarMapUI.EnsureExists().Open();
    }

    void GoToMainMenu()
    {
        pauseMenuOpen = false;
        modalPauseLocks = 0;
        Time.timeScale = 1f;
        WorldSessionManager.EnsureExists().ReturnToMainMenu();
    }

    public static bool TryBeginModalPause()
    {
        if (!CanOpenBlockingModal)
            return false;

        modalPauseLocks++;
        Time.timeScale = 0f;
        foreach (VoiceUnlockRecognizer recognizer in FindObjectsByType<VoiceUnlockRecognizer>(FindObjectsInactive.Exclude))
            recognizer.StopListening();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        return true;
    }

    public static void EndModalPause()
    {
        if (modalPauseLocks > 0)
            modalPauseLocks--;

        if (!pauseMenuOpen && modalPauseLocks == 0 && !RunEndScreenController.IsOpen)
            Time.timeScale = 1f;
    }

    static Canvas CreateCanvas()
    {
        var go = new GameObject("PauseMenuCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        if (FindAnyObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        return canvas;
    }

    static GameObject MakePanel(Transform parent)
    {
        var shade = new GameObject("PauseShade", typeof(RectTransform), typeof(Image));
        shade.transform.SetParent(parent, false);
        var shadeRt = shade.GetComponent<RectTransform>();
        shadeRt.anchorMin = Vector2.zero;
        shadeRt.anchorMax = Vector2.one;
        shadeRt.offsetMin = shadeRt.offsetMax = Vector2.zero;
        shade.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);
        var panel = new GameObject("PausePanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        panel.transform.SetParent(shade.transform, false);
        var rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(420f, 360f);
        GameUiTheme.StylePanel(panel);
        var layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(34, 34, 30, 30);
        layout.spacing = 16f;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        return shade;
    }

    static Button MakeButton(Transform parent, string value, UnityEngine.Events.UnityAction action, GameUiTheme.ButtonRole role)
    {
        var go = new GameObject(value, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = 52f;
        var button = go.GetComponent<Button>();
        button.onClick.AddListener(action);
        GameUiTheme.StyleButton(button, role);
        MakeLabel(go.transform, value, 18f);
        return button;
    }

    static TextMeshProUGUI MakeLabel(Transform parent, string value, float size)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = size + 22f;
        var label = go.GetComponent<TextMeshProUGUI>();
        label.text = value;
        label.alignment = TextAlignmentOptions.Center;
        GameUiTheme.StyleText(label, size, parent.GetComponent<Button>() != null);
        return label;
    }

    void OnDestroy()
    {
        if (paused)
            Time.timeScale = 1f;
        pauseMenuOpen = false;
        modalPauseLocks = 0;
    }
}
