using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public partial class MainMenuController : MonoBehaviour
{
    [Header("Scene Flow")]
    [SerializeField] private string loginSceneName = "";
    [SerializeField] private string shopSceneName = "Shop";

    [Header("Primary UI")]
    [SerializeField] private Button loginButton;
    [SerializeField] private Button continueButton;
    [SerializeField] private Button newGameButton;
    [SerializeField] private Button shopButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private TextMeshProUGUI statusText;

    [Header("Confirmation UI")]
    [SerializeField] private GameObject confirmPanel;
    [SerializeField] private TextMeshProUGUI confirmMessageText;
    [SerializeField] private Button confirmYesButton;
    [SerializeField] private Button confirmNoButton;

    [Header("Firebase Login")]
    [SerializeField] private string firebaseApiKey = "";
    [SerializeField] private string firebaseProjectId = "the-script-dea4f";
    [SerializeField] private string firebaseFunctionsBaseUrl = "https://us-central1-the-script-dea4f.cloudfunctions.net";
    [SerializeField] private string firebaseBuddyVoiceFunctionsBaseUrl = "https://asia-south1-the-script-dea4f.cloudfunctions.net";
    [SerializeField] private WavLmEndpointMode wavLmEndpointMode = WavLmEndpointMode.Auto;
    [SerializeField] private string localWavLmApiBaseUrl = CurriculumSessionManager.DefaultLocalWavLmApiBaseUrl;
    [SerializeField] private string cloudWavLmApiBaseUrl = "";
    [SerializeField] private string wavLmApiBaseUrl = "";
    [SerializeField] private StudentAccessCodeLoginController.AccessCodeLoginMode loginMode =
        StudentAccessCodeLoginController.AccessCodeLoginMode.StudentCode;

    private bool hasSave;
    private GameObject accessibilityPanel;
    private GameObject loginPanel;
    private InputField loginEmailInput;
    private InputField loginPasswordInput;
    private InputField loginCodeInput;
    private Text loginStatusLabel;
    private Button loginSubmitButton;
    private Button loginCancelButton;
    private StudentAccessCodeLoginController runtimeLoginController;
    private Toggle vibrationToggle;
    private Slider shakeSlider;
    private SettingsMenu settingsMenu;
    private Image logoImage;
    private Sprite runtimeLogoSprite;

    void Awake()
    {
        ForceMenuCursor();
        ForceCloseStartupOverlays();
        AutoWireIfNeeded();
        ConfigureCanvasScaling();
        EnsureEventSystem();
        EnsureLogoUi();
        EnsureLoginButton();
        EnsureShopButton();
        EnsureLoginUi();
        EnsureAccessibilitySettingsUi();
        HideRetiredPrimaryButtons();
        WireButtons();
        ApplyTheme();
        ConfigureLoginController();
        SetConfirmPanelVisible(false);
        SetLoginPanelVisible(false);
        ForceCloseStartupOverlays();
        RefreshSaveState();
    }

    void OnEnable()
    {
        SubscribeToCurriculumSession();
        ForceMenuCursor();
        ConfigureLoginController();
        SetConfirmPanelVisible(false);
        SetLoginPanelVisible(false);
        RefreshSaveState();
    }

    void OnDisable()
    {
        if (CurriculumSessionManager.Instance != null)
            CurriculumSessionManager.Instance.OnStudentSessionChanged -= HandleStudentSessionChanged;
    }

    void Start()
    {
        SetConfirmPanelVisible(false);
        SetLoginPanelVisible(false);
        ForceCloseStartupOverlays();
        StartCoroutine(CloseStartupOverlaysNextFrame());
    }

    void Update()
    {
        ForceMenuCursor();
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
        {
            ForceMenuCursor();
            ForceCloseStartupOverlays();
        }
    }

    void OnValidate()
    {
        if (!Application.isPlaying)
            AutoWireIfNeeded();
    }

    void AutoWireIfNeeded()
    {
        if (continueButton == null)
            continueButton = FindButton("ContinueButton");
        if (loginButton == null)
            loginButton = FindButton("LoginButton");
        if (newGameButton == null)
            newGameButton = FindButton("NewGameButton");
        if (shopButton == null)
            shopButton = FindButton("ShopButton");
        if (settingsButton == null)
            settingsButton = FindButton("SettingsButton");
        if (statusText == null)
            statusText = FindText("StatusText");
        if (confirmPanel == null)
            confirmPanel = FindChild("ConfirmPanel")?.gameObject;
        if (confirmMessageText == null)
            confirmMessageText = FindText("ConfirmMessageText");
        if (confirmYesButton == null)
            confirmYesButton = FindButton("ConfirmYesButton");
        if (confirmNoButton == null)
            confirmNoButton = FindButton("ConfirmNoButton");
        if (loginPanel == null)
            loginPanel = FindChild("LoginPanel")?.gameObject;
        if (loginEmailInput == null)
            loginEmailInput = FindChild("LoginEmailInput")?.GetComponent<InputField>();
        if (loginPasswordInput == null)
            loginPasswordInput = FindChild("LoginPasswordInput")?.GetComponent<InputField>();
        if (loginCodeInput == null)
            loginCodeInput = FindChild("LoginCodeInput")?.GetComponent<InputField>();
        if (loginStatusLabel == null)
            loginStatusLabel = FindChild("LoginStatusLabel")?.GetComponent<Text>();
        if (loginSubmitButton == null)
            loginSubmitButton = FindButton("LoginSubmitButton");
        if (loginCancelButton == null)
            loginCancelButton = FindButton("LoginCancelButton");
    }

    void WireButtons()
    {
        if (loginButton != null)
        {
            loginButton.onClick.RemoveListener(HandleLoginPressed);
            loginButton.onClick.AddListener(HandleLoginPressed);
        }

        if (continueButton != null)
        {
            continueButton.onClick.RemoveListener(HandleContinuePressed);
            continueButton.onClick.AddListener(HandleContinuePressed);
        }

        if (newGameButton != null)
        {
            newGameButton.onClick.RemoveListener(HandleNewGamePressed);
            newGameButton.onClick.AddListener(HandleNewGamePressed);
        }

        if (shopButton != null)
        {
            shopButton.onClick.RemoveListener(HandleShopPressed);
            shopButton.onClick.AddListener(HandleShopPressed);
        }

        if (loginSubmitButton != null)
        {
            ConfigureLoginController();
            loginSubmitButton.onClick.RemoveAllListeners();
            loginSubmitButton.onClick.AddListener(() =>
            {
                ConfigureLoginController();
                runtimeLoginController?.StartLogin();
            });
        }

        if (loginCancelButton != null)
        {
            loginCancelButton.onClick.RemoveListener(HandleCancelLogin);
            loginCancelButton.onClick.AddListener(HandleCancelLogin);
        }

        if (confirmYesButton != null)
        {
            confirmYesButton.onClick.RemoveListener(HandleConfirmNewGame);
            confirmYesButton.onClick.AddListener(HandleConfirmNewGame);
        }

        if (confirmNoButton != null)
        {
            confirmNoButton.onClick.RemoveListener(HandleCancelNewGame);
            confirmNoButton.onClick.AddListener(HandleCancelNewGame);
        }
    }

    void SubscribeToCurriculumSession()
    {
        CurriculumSessionManager curriculum = CurriculumSessionManager.EnsureExists();
        if (curriculum == null)
            return;

        curriculum.OnStudentSessionChanged -= HandleStudentSessionChanged;
        curriculum.OnStudentSessionChanged += HandleStudentSessionChanged;
    }

    void HandleStudentSessionChanged()
    {
        SetLoginPanelVisible(false);
        RefreshSaveState();
    }

    static string HumanizeWorldGoalTarget(string areaId)
    {
        if (string.IsNullOrWhiteSpace(areaId))
            return "next gym";

        string[] parts = areaId.Split(':');
        if (parts.Length < 2)
            return areaId;

        string topic = parts[1].Trim().ToUpperInvariant() switch
        {
            "GREETINGSANDSURVIVALENGLISH" => "Greetings",
            "NOUNS" => "Nouns",
            "ALPHABET" => "Alphabet",
            "VOWELSANDCONSONANTS" => "Vowels and Consonants",
            "SENTENCESTARTANDFULLSTOP" => "Sentence Start and Full Stop",
            "VERBS" => "Verbs",
            "ARTICLES" => "Articles",
            "PRONOUNS" => "Pronouns",
            "PLURALS" => "Plurals",
            "ADJECTIVES" => "Adjectives",
            "BASICPREPOSITIONS" => "Basic Prepositions",
            _ => parts[1].Trim(),
        };
        string kind = parts[0].Trim().ToUpperInvariant() == "GYM" ? "Gym" : "Area";
        return $"{topic} {kind}";
    }

    static bool IsWorldGoalTargetCleared(WorldGoalAssignment goal)
    {
        if (goal == null || string.IsNullOrWhiteSpace(goal.targetGymId))
            return false;

        GrammarWorldProgressService progress = GrammarWorldProgressService.Instance;
        return progress != null && progress.IsGymCleared(goal.targetGymId);
    }

    static bool TryReadSlotSummary(int slot, out int level)
    {
        return PlayerLearningProfile.TryReadSummary(slot, out _, out level);
    }

    void SetConfirmPanelVisible(bool visible)
    {
        if (confirmPanel != null)
        {
            confirmPanel.SetActive(visible);
            if (visible)
                confirmPanel.transform.SetAsLastSibling();
        }
    }

    System.Collections.IEnumerator CloseStartupOverlaysNextFrame()
    {
        yield return null;
        SetConfirmPanelVisible(false);
        ForceCloseStartupOverlays();
    }

    void ForceCloseStartupOverlays()
    {
        if (confirmPanel != null)
            confirmPanel.SetActive(false);
    }

    WorldSessionManager ConfigureWorldSession()
    {
        WorldSessionManager manager = WorldSessionManager.EnsureExists();
        manager.mainMenuSceneName = SceneManager.GetActiveScene().name;
        manager.shopSceneName = string.IsNullOrWhiteSpace(shopSceneName) ? "Shop" : shopSceneName.Trim();
        return manager;
    }

    Button FindButton(string objectName)
    {
        return FindChild(objectName)?.GetComponent<Button>();
    }

    TextMeshProUGUI FindText(string objectName)
    {
        return FindChild(objectName)?.GetComponent<TextMeshProUGUI>();
    }

    Transform FindChild(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
            return null;

        return FindDeepChild(transform, objectName);
    }

    static Transform FindDeepChild(Transform root, string targetName)
    {
        if (root == null)
            return null;

        if (root.name == targetName)
            return root;

        foreach (Transform child in root)
        {
            Transform match = FindDeepChild(child, targetName);
            if (match != null)
                return match;
        }

        return null;
    }
}
