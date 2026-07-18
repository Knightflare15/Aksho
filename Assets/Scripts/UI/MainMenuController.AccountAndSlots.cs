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
    void RefreshSaveState()
    {
        bool hasWorldSave = GrammarWorldProgressService.HasSavedWorldProgress();
        hasSave = hasWorldSave;
        RefreshPlayerSlotUi();


        CurriculumSessionManager curriculum = CurriculumSessionManager.EnsureExists();
        bool loggedIn = curriculum != null && curriculum.HasStudentSession;
        if (loggedIn)
            curriculum.LoadOptionalWorldGoal();
        WorldGoalAssignment goal = curriculum != null ? curriculum.CurrentWorldGoal : null;
        bool targetGymCleared = IsWorldGoalTargetCleared(goal);
        SetButtonLabel(loginButton, loggedIn ? "Log Out" : "Login");
        SetButtonLabel(newGameButton, "New Game");
        SetButtonLabel(continueButton, hasWorldSave ? "Continue" : "Play");
        SetButtonVisible(continueButton, true);
        if (continueButton != null)
            continueButton.interactable = true;
        if (newGameButton != null)
            newGameButton.interactable = true;

        if (statusText == null)
            return;

        if (!loggedIn)
            statusText.text = "Play the full world locally. Log in for Buddy AI, pronunciation assessment, teacher goals, and cloud progress.";
        else if (hasWorldSave && goal == null)
            statusText.text = "Continue your world journey. No weekly teacher goal is assigned.";
        else if (targetGymCleared)
            statusText.text = $"Weekly goal cleared: {HumanizeWorldGoalTarget(goal.targetGymId)}.";
        else if (goal != null && !string.IsNullOrWhiteSpace(goal.targetGymId))
            statusText.text = $"Weekly goal: clear {HumanizeWorldGoalTarget(goal.targetGymId)} by {goal.dueDate} for +{goal.rewardCoins} coins.";
        else
            statusText.text = hasWorldSave ? "Continue your world journey." : "Begin your journey in Welcome Village.";
    }

    void HandleLoginPressed()
    {
        CurriculumSessionManager curriculum = CurriculumSessionManager.EnsureExists();
        if (curriculum != null && curriculum.HasStudentSession)
        {
            SetLoginPanelVisible(false);
            curriculum.ClearStudentSession();
            if (loginEmailInput != null)
                loginEmailInput.text = "";
            if (loginPasswordInput != null)
                loginPasswordInput.text = "";
            if (loginCodeInput != null)
                loginCodeInput.text = "";
            if (statusText != null)
                statusText.text = "Logged out. Log in to load your class focus.";
            RefreshSaveState();
            return;
        }

        if (EnsureLoginUi())
        {
            ConfigureLoginController();
            SetConfirmPanelVisible(false);
            SetLoginPanelVisible(true);
            if (accessibilityPanel != null)
                accessibilityPanel.SetActive(false);
            return;
        }

        if (!string.IsNullOrWhiteSpace(loginSceneName))
        {
            SceneManager.LoadScene(loginSceneName.Trim());
            return;
        }

        if (statusText != null)
            statusText.text = "Login UI is not configured in this scene.";
    }

    void HandleCancelLogin()
    {
        SetLoginPanelVisible(false);
    }

    void HandleContinuePressed()
    {
        ConfigureWorldSession().Play();
    }

    void HandleNewGamePressed()
    {
        if (!hasSave)
        {
            ConfigureWorldSession().StartNewGame();
            return;
        }

        if (confirmMessageText != null)
            confirmMessageText.text = "Start a new game? This resets world progress for this player.";
        SetConfirmPanelVisible(true);
    }

    void HandleShopPressed()
    {
        ConfigureWorldSession().OpenShop();
    }

    void HandleConfirmNewGame()
    {
        SetConfirmPanelVisible(false);
        ConfigureWorldSession().StartNewGame();
    }

    void HandleCancelNewGame()
    {
        SetConfirmPanelVisible(false);
    }

    void EnsureLoginButton()
    {
        if (loginButton != null)
            return;

        Transform parent = continueButton != null ? continueButton.transform.parent : transform;
        loginButton = MakeRuntimeButton("LoginButton", parent, "Login", Vector2.zero);

        if (continueButton != null)
            loginButton.transform.SetSiblingIndex(continueButton.transform.GetSiblingIndex());
    }

    void EnsureShopButton()
    {
        if (shopButton != null)
            return;

        Transform parent = continueButton != null ? continueButton.transform.parent : transform;
        shopButton = MakeRuntimeButton("ShopButton", parent, "Shop", Vector2.zero);

        if (newGameButton != null)
            shopButton.transform.SetSiblingIndex(newGameButton.transform.GetSiblingIndex() + 1);
    }

    bool EnsureLoginUi()
    {
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
            return false;

        if (loginPanel == null)
        {
            loginPanel = new GameObject("LoginPanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            loginPanel.transform.SetParent(canvas.transform, false);
            RectTransform panelRt = loginPanel.GetComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(560f, 520f);
            GameUiTheme.StylePanel(loginPanel, true);

            VerticalLayoutGroup layout = loginPanel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(34, 34, 30, 30);
            layout.spacing = 12f;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            MakeRuntimeLabel(loginPanel.transform, "Student Login", 28f);
            MakeRuntimeLabel(loginPanel.transform, "Email", 17f).alignment = TextAlignmentOptions.Left;
            loginEmailInput = MakeRuntimeInputField("LoginEmailInput", loginPanel.transform, "student email");
            MakeRuntimeLabel(loginPanel.transform, "Password", 17f).alignment = TextAlignmentOptions.Left;
            loginPasswordInput = MakeRuntimeInputField("LoginPasswordInput", loginPanel.transform, "password", InputField.ContentType.Password);
            MakeRuntimeLabel(loginPanel.transform, "Access code (optional)", 17f).alignment = TextAlignmentOptions.Left;
            loginCodeInput = MakeRuntimeInputField("LoginCodeInput", loginPanel.transform, "student code");
            loginStatusLabel = MakeRuntimeLegacyLabel(loginPanel.transform, "Use the email and password from your teacher.", 18f);
            loginStatusLabel.name = "LoginStatusLabel";

            GameObject row = new GameObject("LoginButtonRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(loginPanel.transform, false);
            row.GetComponent<LayoutElement>().preferredHeight = 58f;
            HorizontalLayoutGroup rowLayout = row.GetComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 12f;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childControlWidth = false;
            rowLayout.childForceExpandWidth = false;

            loginSubmitButton = MakeRuntimeButton("LoginSubmitButton", row.transform, "Sign In", Vector2.zero);
            loginCancelButton = MakeRuntimeButton("LoginCancelButton", row.transform, "Cancel", Vector2.zero);
            loginPanel.SetActive(false);
        }

        if (runtimeLoginController == null)
            runtimeLoginController = loginPanel.GetComponent<StudentAccessCodeLoginController>() ??
                loginPanel.AddComponent<StudentAccessCodeLoginController>();

        return true;
    }

    void HideRetiredPrimaryButtons()
    {
        SetButtonVisible(shopButton, true);
        HideNamedMenuObject("SandboxButton");
        HideNamedMenuObject("SandboxPanel");
        HideNamedMenuObject("EraseSaveButton");
    }

    void HideNamedMenuObject(string objectName)
    {
        Transform target = FindChild(objectName);
        if (target != null)
            target.gameObject.SetActive(false);
    }

    void EnsureAccessibilitySettingsUi()
    {
        if (accessibilityPanel != null)
            return;

        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
            return;

        settingsButton = MakeRuntimeButton("SettingsButton", canvas.transform, "Settings", new Vector2(-28f, 28f));
        var buttonRt = settingsButton.GetComponent<RectTransform>();
        buttonRt.anchorMin = new Vector2(1f, 0f);
        buttonRt.anchorMax = new Vector2(1f, 0f);
        buttonRt.pivot = new Vector2(1f, 0f);
        SetButtonSize(settingsButton, new Vector2(260f, 60f), 20f);
        settingsMenu = gameObject.GetComponent<SettingsMenu>() ?? gameObject.AddComponent<SettingsMenu>();
        settingsMenu.Build(canvas.transform, null);
        settingsButton.onClick.AddListener(() => settingsMenu.Open());

        accessibilityPanel = new GameObject("AccessibilityPanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        accessibilityPanel.transform.SetParent(canvas.transform, false);
        var panelRt = accessibilityPanel.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(430f, 350f);
        GameUiTheme.StylePanel(accessibilityPanel);

        var layout = accessibilityPanel.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(28, 28, 24, 24);
        layout.spacing = 14f;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;

        MakeRuntimeLabel(accessibilityPanel.transform, "Accessibility", 28f);
        vibrationToggle = MakeRuntimeToggle(accessibilityPanel.transform, "Vibration", AccessibilitySettings.VibrationEnabled);
        vibrationToggle.onValueChanged.AddListener(value => AccessibilitySettings.VibrationEnabled = value);

        MakeRuntimeLabel(accessibilityPanel.transform, "Screen shake intensity", 18f);
        shakeSlider = MakeRuntimeSlider(accessibilityPanel.transform, AccessibilitySettings.ShakeIntensity);
        shakeSlider.onValueChanged.AddListener(value => AccessibilitySettings.ShakeIntensity = value);

        var closeButton = MakeRuntimeButton("CloseButton", accessibilityPanel.transform, "Close", Vector2.zero);
        closeButton.onClick.AddListener(() => accessibilityPanel.SetActive(false));
        accessibilityPanel.SetActive(false);
    }

    void RefreshPlayerSlotUi()
    {
    }

    void ConfigureLoginController()
    {
        if (runtimeLoginController == null)
            runtimeLoginController = FindAnyObjectByType<StudentAccessCodeLoginController>();
        if (runtimeLoginController == null)
            return;

        CurriculumSessionManager curriculum = CurriculumSessionManager.EnsureExists();
        string savedApiKey = PlayerPrefs.GetString("TheScript.FirebaseApiKey", firebaseApiKey);
        string savedProjectId = PlayerPrefs.GetString("TheScript.FirebaseProjectId", firebaseProjectId);
        string savedFunctionsBaseUrl = PlayerPrefs.GetString("TheScript.FirebaseFunctionsBaseUrl", firebaseFunctionsBaseUrl);
        string savedBuddyVoiceFunctionsBaseUrl = PlayerPrefs.GetString("TheScript.FirebaseBuddyVoiceFunctionsBaseUrl", firebaseBuddyVoiceFunctionsBaseUrl);
        var savedWavLmEndpointMode = (WavLmEndpointMode)PlayerPrefs.GetInt("TheScript.WavLmEndpointMode", (int)wavLmEndpointMode);
        string savedLocalWavLmApiBaseUrl = PlayerPrefs.GetString("TheScript.LocalWavLmApiBaseUrl", localWavLmApiBaseUrl);
        string savedCloudWavLmApiBaseUrl = PlayerPrefs.GetString("TheScript.CloudWavLmApiBaseUrl", cloudWavLmApiBaseUrl);
        string savedWavLmApiBaseUrl = PlayerPrefs.GetString("TheScript.WavLmApiBaseUrl", wavLmApiBaseUrl);

        runtimeLoginController.firebaseApiKey = string.IsNullOrWhiteSpace(savedApiKey) ? firebaseApiKey : savedApiKey;
        runtimeLoginController.firebaseProjectId = string.IsNullOrWhiteSpace(savedProjectId) ? firebaseProjectId : savedProjectId;
        runtimeLoginController.functionsBaseUrl = string.IsNullOrWhiteSpace(savedFunctionsBaseUrl)
            ? firebaseFunctionsBaseUrl
            : savedFunctionsBaseUrl;
        runtimeLoginController.buddyVoiceFunctionsBaseUrl = string.IsNullOrWhiteSpace(savedBuddyVoiceFunctionsBaseUrl)
            ? firebaseBuddyVoiceFunctionsBaseUrl
            : savedBuddyVoiceFunctionsBaseUrl;
        runtimeLoginController.wavLmEndpointMode = savedWavLmEndpointMode;
        runtimeLoginController.localWavLmApiBaseUrl = string.IsNullOrWhiteSpace(savedLocalWavLmApiBaseUrl)
            ? localWavLmApiBaseUrl
            : savedLocalWavLmApiBaseUrl;
        runtimeLoginController.cloudWavLmApiBaseUrl = string.IsNullOrWhiteSpace(savedCloudWavLmApiBaseUrl)
            ? cloudWavLmApiBaseUrl
            : savedCloudWavLmApiBaseUrl;
        runtimeLoginController.wavLmApiBaseUrl = string.IsNullOrWhiteSpace(savedWavLmApiBaseUrl)
            ? wavLmApiBaseUrl
            : savedWavLmApiBaseUrl;
        runtimeLoginController.loginMode = loginMode;
        runtimeLoginController.curriculumSession = curriculum;
        runtimeLoginController.loadMissionAfterLogin = true;
        runtimeLoginController.emailInput = loginEmailInput;
        runtimeLoginController.passwordInput = loginPasswordInput;
        runtimeLoginController.codeInput = loginCodeInput;
        runtimeLoginController.loginButton = loginSubmitButton;
        runtimeLoginController.statusLabel = loginStatusLabel;

        if (curriculum == null)
            return;

        curriculum.providerMode = CurriculumProviderMode.FirebaseRest;
        if (!string.IsNullOrWhiteSpace(runtimeLoginController.firebaseProjectId))
            curriculum.firebaseProjectId = runtimeLoginController.firebaseProjectId;
        curriculum.firebaseFunctionsBaseUrl = runtimeLoginController.functionsBaseUrl ?? "";
        curriculum.firebaseBuddyVoiceFunctionsBaseUrl = runtimeLoginController.buddyVoiceFunctionsBaseUrl ?? "";
        curriculum.wavLmEndpointMode = runtimeLoginController.wavLmEndpointMode;
        curriculum.localWavLmApiBaseUrl = runtimeLoginController.localWavLmApiBaseUrl ?? "";
        curriculum.cloudWavLmApiBaseUrl = runtimeLoginController.cloudWavLmApiBaseUrl ?? "";
        curriculum.wavLmApiBaseUrl = runtimeLoginController.wavLmApiBaseUrl ?? "";
    }

    void SetLoginPanelVisible(bool visible)
    {
        if (loginPanel == null)
            return;

        loginPanel.SetActive(visible);
        if (visible)
        {
            EnsureEventSystem();
            loginPanel.transform.SetAsLastSibling();
            StartCoroutine(FocusLoginInputNextFrame());
        }
    }

    System.Collections.IEnumerator FocusLoginInputNextFrame()
    {
        yield return null;

        InputField field = loginEmailInput != null && string.IsNullOrWhiteSpace(loginEmailInput.text)
            ? loginEmailInput
            : loginPasswordInput != null && string.IsNullOrWhiteSpace(loginPasswordInput.text)
                ? loginPasswordInput
                : loginCodeInput;

        if (field == null || !field.gameObject.activeInHierarchy)
            yield break;

        EventSystem eventSystem = EventSystem.current;
        if (eventSystem != null)
        {
            eventSystem.SetSelectedGameObject(null);
            eventSystem.SetSelectedGameObject(field.gameObject);
        }

        field.ActivateInputField();
    }
}
