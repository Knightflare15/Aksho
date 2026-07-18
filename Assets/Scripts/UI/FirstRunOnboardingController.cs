using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>First-run, replayable explanation of the grammar-world controls and rules.</summary>
[DisallowMultipleComponent]
public sealed class FirstRunOnboardingController : MonoBehaviour
{
    const string CompletedKey = "TheScript.Onboarding.Version";
    const int CurrentVersion = 2;
    readonly Step[] steps =
    {
        new Step("Welcome to the grammar world", "The world uses a hidden hex grid so movement and attacks stay fair. You do not select a grid in normal 3D play—look where you want to act."),
        new Step("Look, then move", "Turn the camera toward a path and say or choose a movement command. “Walk forward” follows your facing; left, right, and backward are relative to where you are looking. Longer moves preview a straight route."),
        new Step("Summon a word creature", "Start with an adjective + noun, such as “swift fox.” In battle, summons must be the enemy noun or a permitted synonym from that noun family—not an unrelated creature."),
        new Step("Build an attack", "Choose an attack verb, then an optional adverb. The verb defines the attack shape; the adverb can change reach, timing, or force. Look toward the target to aim the cone before committing."),
        new Step("Read curses", "Enemies can curse grammar during the fight. A pronoun curse expires after its timer. A tense curse lasts for the whole battle, so every later command must use that tense."),
        new Step("Call Buddy wisely", "Town Buddy can teach and translate. Route Buddy gives clues without leaking the answer. Gym checks are independent: Buddy cannot answer there, and the Gym includes real battles."),
        new Step("You are ready", "Watch enemy telegraphs and the speed gauge, look before moving, and use the Grimoire when a rule feels unclear. Microphone features are optional; text controls remain available.")
    };

    Canvas canvas;
    Text titleText;
    Text bodyText;
    Text progressText;
    Button backButton;
    Button nextButton;
    int index;

    public static void EnsureExists(GameObject host)
    {
        if (host != null && host.GetComponent<FirstRunOnboardingController>() == null)
            host.AddComponent<FirstRunOnboardingController>();
    }

    void Start()
    {
        BuildUi();
        if (PlayerPrefs.GetInt(CompletedKey, 0) < CurrentVersion)
            Open();
        else
            canvas.gameObject.SetActive(false);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1)) Open();
        if (canvas != null && canvas.gameObject.activeSelf && Input.GetKeyDown(KeyCode.Escape)) Close(false);
    }

    void Open()
    {
        if (canvas == null) BuildUi();
        index = 0;
        canvas.gameObject.SetActive(true);
        Render();
    }

    void BuildUi()
    {
        if (canvas != null) return;
        if (FindAnyObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        GameObject canvasObject = new GameObject("FirstRunOnboardingCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);

        Image shade = CreateImage("Shade", canvasObject.transform, new Color(0.02f, 0.04f, 0.09f, 0.72f));
        Stretch(shade.rectTransform);
        Image panel = CreateImage("GuideCard", shade.transform, new Color(0.075f, 0.11f, 0.19f, 0.98f));
        RectTransform card = panel.rectTransform;
        card.anchorMin = new Vector2(0.5f, 0f);
        card.anchorMax = new Vector2(0.5f, 0f);
        card.pivot = new Vector2(0.5f, 0f);
        card.anchoredPosition = new Vector2(0f, 42f);
        card.sizeDelta = new Vector2(820f, 270f);

        titleText = CreateText("Title", card, 30, FontStyle.Bold, TextAnchor.UpperLeft);
        SetRect(titleText.rectTransform, new Vector2(32f, -26f), new Vector2(756f, 46f));
        bodyText = CreateText("Body", card, 19, FontStyle.Normal, TextAnchor.UpperLeft);
        bodyText.color = new Color(0.86f, 0.91f, 1f);
        SetRect(bodyText.rectTransform, new Vector2(32f, -82f), new Vector2(756f, 104f));
        progressText = CreateText("Progress", card, 15, FontStyle.Bold, TextAnchor.MiddleLeft);
        progressText.color = new Color(0.37f, 0.79f, 1f);
        SetRect(progressText.rectTransform, new Vector2(32f, -210f), new Vector2(220f, 36f));

        backButton = CreateButton("Back", card, new Vector2(492f, -210f), new Vector2(110f, 40f), Previous);
        CreateButton("Skip", card, new Vector2(610f, -210f), new Vector2(90f, 40f), () => Close(true));
        nextButton = CreateButton("Next", card, new Vector2(708f, -210f), new Vector2(90f, 40f), Next);
    }

    void Render()
    {
        Step step = steps[Mathf.Clamp(index, 0, steps.Length - 1)];
        titleText.text = step.title;
        bodyText.text = step.body;
        progressText.text = $"{index + 1} of {steps.Length}  •  F1 replays this guide";
        backButton.interactable = index > 0;
        nextButton.GetComponentInChildren<Text>().text = index == steps.Length - 1 ? "Finish" : "Next";
    }

    void Previous() { if (index > 0) { index--; Render(); } }
    void Next()
    {
        if (index < steps.Length - 1) { index++; Render(); return; }
        Close(true);
    }

    void Close(bool completed)
    {
        if (completed)
        {
            PlayerPrefs.SetInt(CompletedKey, CurrentVersion);
            PlayerPrefs.Save();
        }
        if (canvas != null) canvas.gameObject.SetActive(false);
    }

    static Image CreateImage(string name, Transform parent, Color color)
    {
        GameObject value = new GameObject(name, typeof(RectTransform), typeof(Image));
        value.transform.SetParent(parent, false);
        Image image = value.GetComponent<Image>();
        image.color = color;
        return image;
    }

    static Text CreateText(string name, Transform parent, int size, FontStyle style, TextAnchor alignment)
    {
        GameObject value = new GameObject(name, typeof(RectTransform), typeof(Text));
        value.transform.SetParent(parent, false);
        Text text = value.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    static Button CreateButton(string label, Transform parent, Vector2 anchoredPosition, Vector2 size, UnityEngine.Events.UnityAction action)
    {
        Image image = CreateImage(label + "Button", parent, new Color(0.12f, 0.38f, 0.62f, 1f));
        SetRect(image.rectTransform, anchoredPosition, size);
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);
        Text text = CreateText("Label", image.transform, 16, FontStyle.Bold, TextAnchor.MiddleCenter);
        Stretch(text.rectTransform);
        text.text = label;
        return button;
    }

    static void SetRect(RectTransform rect, Vector2 anchoredPosition, Vector2 size)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    readonly struct Step
    {
        public readonly string title;
        public readonly string body;
        public Step(string title, string body) { this.title = title; this.body = body; }
    }
}
