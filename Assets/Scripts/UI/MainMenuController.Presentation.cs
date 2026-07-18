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
    void ApplyTheme()
    {
        SetButtonLabel(newGameButton, "New Game");
        SetButtonLabel(settingsButton, "Settings");

        GameUiTheme.StyleButton(continueButton, GameUiTheme.ButtonRole.Primary);
        GameUiTheme.StyleButton(loginButton, GameUiTheme.ButtonRole.Secondary);
        GameUiTheme.StyleButton(newGameButton, GameUiTheme.ButtonRole.Secondary);
        GameUiTheme.StyleButton(shopButton, GameUiTheme.ButtonRole.Secondary);
        GameUiTheme.StyleButton(confirmYesButton, GameUiTheme.ButtonRole.Danger);
        GameUiTheme.StyleButton(confirmNoButton, GameUiTheme.ButtonRole.Quiet);
        GameUiTheme.StyleButton(loginSubmitButton, GameUiTheme.ButtonRole.Primary);
        GameUiTheme.StyleButton(loginCancelButton, GameUiTheme.ButtonRole.Quiet);

        SetButtonSize(continueButton, new Vector2(300f, 62f), 21f);
        SetButtonSize(loginButton, new Vector2(300f, 62f), 21f);
        SetButtonSize(newGameButton, new Vector2(300f, 62f), 21f);
        SetButtonSize(shopButton, new Vector2(300f, 62f), 21f);
        SetButtonSize(confirmYesButton, new Vector2(220f, 56f), 18f);
        SetButtonSize(confirmNoButton, new Vector2(220f, 56f), 18f);
        SetButtonSize(loginSubmitButton, new Vector2(190f, 52f), 17f);
        SetButtonSize(loginCancelButton, new Vector2(150f, 52f), 17f);
        SetButtonSize(settingsButton, new Vector2(260f, 60f), 20f);
        ApplyMainActionLayout();

        if (statusText != null)
        {
            GameUiTheme.StyleText(statusText, 20f);
            statusText.color = GameUiTheme.TextMuted;
        }

        if (confirmMessageText != null)
        {
            GameUiTheme.StyleText(confirmMessageText, 22f);
            confirmMessageText.color = GameUiTheme.Text;
        }

        if (confirmPanel != null)
            GameUiTheme.StylePanel(confirmPanel, true);

        if (loginPanel != null)
            GameUiTheme.StylePanel(loginPanel, true);
        if (loginStatusLabel != null)
        {
            loginStatusLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            loginStatusLabel.fontSize = 18;
            loginStatusLabel.color = GameUiTheme.TextMuted;
            loginStatusLabel.alignment = TextAnchor.MiddleCenter;
        }

        var menuRoot = transform as RectTransform;
        if (menuRoot != null)
            GameUiTheme.StyleAllText(menuRoot);
    }

    void ConfigureCanvasScaling()
    {
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
            return;

        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null)
            return;

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600f, 900f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
    }

    void EnsureLogoUi()
    {
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
            return;

        if (logoImage == null)
            logoImage = GameObject.Find("MainMenuLogo")?.GetComponent<Image>();

        Sprite logoSprite = LoadLogoSprite();
        if (logoSprite == null)
            return;

        if (logoImage == null)
        {
            var go = new GameObject("MainMenuLogo", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(canvas.transform, false);
            logoImage = go.GetComponent<Image>();
        }

        logoImage.sprite = logoSprite;
        logoImage.preserveAspect = true;
        logoImage.raycastTarget = false;

        RectTransform rt = logoImage.rectTransform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(48f, -28f);
        rt.sizeDelta = new Vector2(660f, 250f);
        rt.SetSiblingIndex(0);

        HideLegacyTitleText(canvas.transform);
    }

    Sprite LoadLogoSprite()
    {
        if (runtimeLogoSprite != null)
            return runtimeLogoSprite;

        Sprite resourceSprite = Resources.Load<Sprite>("UI/Brand/Logo");
        if (resourceSprite != null)
        {
            runtimeLogoSprite = resourceSprite;
            return runtimeLogoSprite;
        }

        string logoPath = Path.Combine(Application.dataPath, "UI", "Brand", "Logo.png");
        if (!File.Exists(logoPath))
            return null;

        byte[] bytes = File.ReadAllBytes(logoPath);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(bytes))
            return null;

        texture.name = "MainMenuLogoTexture";
        runtimeLogoSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            100f);
        runtimeLogoSprite.name = "MainMenuLogoSprite";
        return runtimeLogoSprite;
    }

    static void HideLegacyTitleText(Transform root)
    {
        foreach (TextMeshProUGUI text in root.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (!string.IsNullOrEmpty(text.text) && text.text.Contains("MAIN MENU"))
                text.gameObject.SetActive(false);
        }
    }

    static void SetButtonSize(Button button, Vector2 size, float textSize)
    {
        if (button == null)
            return;

        RectTransform rt = button.GetComponent<RectTransform>();
        if (rt != null)
            rt.sizeDelta = size;

        LayoutElement layout = button.GetComponent<LayoutElement>();
        if (layout != null)
        {
            layout.preferredWidth = size.x;
            layout.preferredHeight = size.y;
        }

        foreach (TextMeshProUGUI text in button.GetComponentsInChildren<TextMeshProUGUI>(true))
            GameUiTheme.StyleText(text, textSize, true);
    }

    static void SetButtonLabel(Button button, string label)
    {
        if (button == null || string.IsNullOrWhiteSpace(label))
            return;

        foreach (TextMeshProUGUI text in button.GetComponentsInChildren<TextMeshProUGUI>(true))
            text.text = label;
    }

    void ApplyMainActionLayout()
    {
        SetButtonPosition(loginButton, new Vector2(0f, 176f));
        SetButtonPosition(continueButton, new Vector2(0f, 88f));
        SetButtonPosition(newGameButton, Vector2.zero);
        SetButtonPosition(shopButton, new Vector2(0f, -88f));
        SetButtonPosition(settingsButton, new Vector2(0f, -176f));

        SetButtonVisible(shopButton, true);

        if (statusText == null)
            return;

        RectTransform statusRt = statusText.rectTransform;
        statusRt.anchorMin = new Vector2(0.5f, 0.5f);
        statusRt.anchorMax = new Vector2(0.5f, 0.5f);
        statusRt.pivot = new Vector2(0.5f, 0.5f);
        statusRt.anchoredPosition = new Vector2(0f, -296f);
        statusRt.sizeDelta = new Vector2(520f, 80f);
        statusText.alignment = TextAlignmentOptions.Center;
    }

    static void SetButtonPosition(Button button, Vector2 anchoredPosition)
    {
        if (button == null)
            return;

        RectTransform rt = button.GetComponent<RectTransform>();
        if (rt == null)
            return;

        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPosition;
    }

    static void SetButtonVisible(Button button, bool visible)
    {
        if (button != null)
            button.gameObject.SetActive(visible);
    }

    static void ForceMenuCursor()
    {
        if (Cursor.lockState != CursorLockMode.None)
            Cursor.lockState = CursorLockMode.None;

        if (!Cursor.visible)
            Cursor.visible = true;
    }

    static void EnsureEventSystem()
    {
        EventSystem eventSystem = FindAnyObjectByType<EventSystem>();
        if (eventSystem == null)
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            return;
        }

        if (eventSystem.GetComponent<InputSystemUIInputModule>() == null &&
            eventSystem.GetComponent<StandaloneInputModule>() == null)
        {
            eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
        }
    }
}
