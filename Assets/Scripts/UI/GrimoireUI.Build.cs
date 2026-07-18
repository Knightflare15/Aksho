using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class GrimoireUI : MonoBehaviour
{
    void BuildUi()
    {
        if (root != null)
            return;

        root = new GameObject("GrimoireCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 450;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        GameUiTheme.ConfigureStandardGameplayCanvasScaler(scaler);

        GameObject backdrop = new GameObject("Backdrop", typeof(RectTransform), typeof(Image));
        backdrop.transform.SetParent(root.transform, false);
        RectTransform backdropRt = backdrop.GetComponent<RectTransform>();
        backdropRt.anchorMin = Vector2.zero;
        backdropRt.anchorMax = Vector2.one;
        backdropRt.offsetMin = Vector2.zero;
        backdropRt.offsetMax = Vector2.zero;
        backdrop.GetComponent<Image>().color = new Color(0.015f, 0.022f, 0.04f, 0.9f);

        GameObject panel = new GameObject("Book", typeof(RectTransform), typeof(Image), typeof(Outline));
        panel.transform.SetParent(backdrop.transform, false);
        RectTransform panelRt = panel.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(1120f, 820f);
        GameUiTheme.StylePanel(panel, false);

        TextMeshProUGUI title = MakeText(panel.transform, "Title", 42f, FontStyles.Bold, GameUiTheme.Gold);
        title.text = "GRIMOIRE";
        title.alignment = TextAlignmentOptions.Center;
        title.rectTransform.anchorMin = new Vector2(0f, 1f);
        title.rectTransform.anchorMax = new Vector2(1f, 1f);
        title.rectTransform.pivot = new Vector2(0.5f, 1f);
        title.rectTransform.offsetMin = new Vector2(28f, -92f);
        title.rectTransform.offsetMax = new Vector2(-28f, -24f);

        TextMeshProUGUI help = MakeText(panel.transform, "Help", 18f, FontStyles.Normal, GameUiTheme.TextMuted);
        help.text = "Unlocked word-spells by letter  |  ★ mastered  |  Press G or Escape to close";
        help.alignment = TextAlignmentOptions.Center;
        help.rectTransform.anchorMin = new Vector2(0f, 1f);
        help.rectTransform.anchorMax = new Vector2(1f, 1f);
        help.rectTransform.pivot = new Vector2(0.5f, 1f);
        help.rectTransform.offsetMin = new Vector2(28f, -130f);
        help.rectTransform.offsetMax = new Vector2(-28f, -94f);

        spellWordsTabButton = MakeTabButton(panel.transform, "SpellWordsTab", "Spell Words", new Vector2(-224f, -154f), out spellWordsTabLabel);
        grammarTabButton = MakeTabButton(panel.transform, "GrammarTab", "Grammar", new Vector2(0f, -154f), out grammarTabLabel);
        bestiaryTabButton = MakeTabButton(panel.transform, "BestiaryTab", "Bestiary", new Vector2(224f, -154f), out bestiaryTabLabel);
        spellWordsTabButton.onClick.AddListener(ShowSpellWordsList);
        grammarTabButton.onClick.AddListener(ShowGrammarList);
        bestiaryTabButton.onClick.AddListener(ShowBestiaryList);

        spellList = MakeText(panel.transform, "SpellList", 24f, FontStyles.Normal, GameUiTheme.Text);
        spellList.alignment = TextAlignmentOptions.TopLeft;
        spellList.textWrappingMode = TextWrappingModes.Normal;
        spellList.overflowMode = TextOverflowModes.Overflow;
        spellList.rectTransform.anchorMin = Vector2.zero;
        spellList.rectTransform.anchorMax = Vector2.one;
        spellList.rectTransform.offsetMin = new Vector2(70f, 48f);
        spellList.rectTransform.offsetMax = new Vector2(-70f, -190f);

        BuildSpellWordButtonRoot(panel.transform);
        BuildDetailImage(panel.transform);
        pronounceWordButton = MakePronounceButton(panel.transform, "PronounceWord", new Vector2(70f, 56f), new Vector2(250f, 48f), out pronounceWordLabel);
        pronounceLetterButton = MakePronounceButton(panel.transform, "PronounceLetter", new Vector2(338f, 56f), new Vector2(250f, 48f), out pronounceLetterLabel);
        openSpellEntryButton = MakePronounceButton(panel.transform, "OpenSpellEntry", new Vector2(606f, 56f), new Vector2(340f, 48f), out openSpellEntryLabel);
        backButton = MakePronounceButton(panel.transform, "BackToGrimoireList", new Vector2(70f, 736f), new Vector2(160f, 44f), out backButtonLabel);
        pronounceWordButton.onClick.AddListener(SpeakFocusedWeaknessWord);
        pronounceLetterButton.onClick.AddListener(SpeakFocusedStartingLetter);
        openSpellEntryButton.onClick.AddListener(OpenFocusedSpellEntry);
        backButton.onClick.AddListener(BackToSpellWordList);
        if (backButtonLabel != null)
            backButtonLabel.text = "Back";
    }

    void BuildDetailImage(Transform parent)
    {
        GameObject frameGo = new GameObject("DetailImageFrame", typeof(RectTransform), typeof(Image), typeof(Outline));
        frameGo.transform.SetParent(parent, false);
        RectTransform frameRt = frameGo.GetComponent<RectTransform>();
        frameRt.anchorMin = new Vector2(1f, 1f);
        frameRt.anchorMax = new Vector2(1f, 1f);
        frameRt.pivot = new Vector2(1f, 1f);
        frameRt.anchoredPosition = new Vector2(-70f, -210f);
        frameRt.sizeDelta = new Vector2(250f, 250f);
        GameUiTheme.StylePanel(frameGo, false);

        detailImageFrame = frameGo.GetComponent<Image>();

        GameObject imageGo = new GameObject("Image", typeof(RectTransform), typeof(Image));
        imageGo.transform.SetParent(frameGo.transform, false);
        detailImage = imageGo.GetComponent<Image>();
        detailImage.preserveAspect = true;
        detailImage.color = Color.white;
        detailImage.rectTransform.anchorMin = Vector2.zero;
        detailImage.rectTransform.anchorMax = Vector2.one;
        detailImage.rectTransform.offsetMin = new Vector2(12f, 12f);
        detailImage.rectTransform.offsetMax = new Vector2(-12f, -12f);

        SetDetailImage(null);
    }

    void SetDetailImage(Sprite sprite)
    {
        bool visible = sprite != null;
        if (detailImageFrame != null)
            detailImageFrame.gameObject.SetActive(visible);
        if (detailImage != null)
        {
            detailImage.sprite = sprite;
            detailImage.gameObject.SetActive(visible);
        }

        if (spellList != null)
            spellList.rectTransform.offsetMax = visible ? new Vector2(-360f, -190f) : new Vector2(-70f, -190f);
    }

    void BuildSpellWordButtonRoot(Transform parent)
    {
        GameObject go = new GameObject("SpellWordButtons", typeof(RectTransform), typeof(VerticalLayoutGroup));
        go.transform.SetParent(parent, false);
        spellWordButtonRoot = go.GetComponent<RectTransform>();
        spellWordButtonRoot.anchorMin = Vector2.zero;
        spellWordButtonRoot.anchorMax = Vector2.one;
        spellWordButtonRoot.offsetMin = new Vector2(70f, 48f);
        spellWordButtonRoot.offsetMax = new Vector2(-70f, -190f);

        VerticalLayoutGroup layout = go.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 14f;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        go.SetActive(false);
    }

    void ClearSpellWordButtons()
    {
        foreach (Button button in spellWordButtons)
        {
            if (button != null)
                Destroy(button.gameObject);
        }
        spellWordButtons.Clear();
    }

    Button MakeSpellWordButton(string label, string word)
    {
        GameObject go = new GameObject($"SpellWord_{word}", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(spellWordButtonRoot, false);

        LayoutElement layout = go.GetComponent<LayoutElement>();
        layout.preferredHeight = 54f;
        layout.minHeight = 54f;

        Image image = go.GetComponent<Image>();
        image.color = new Color(0.12f, 0.18f, 0.29f, 0.95f);

        Button button = go.GetComponent<Button>();
        button.onClick.AddListener(() => OpenSpellEntryFor(word));
        GameUiTheme.StyleButton(button, GameUiTheme.ButtonRole.Secondary);

        TextMeshProUGUI text = MakeText(go.transform, "Label", 21f, FontStyles.Bold, GameUiTheme.Text);
        text.text = label;
        text.alignment = TextAlignmentOptions.Left;
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = new Vector2(18f, 6f);
        text.rectTransform.offsetMax = new Vector2(-18f, -6f);

        spellWordButtons.Add(button);
        return button;
    }

    Button MakeBestiaryEntryButton(EnemyDefinition definition)
    {
        string enemyName = definition != null && !string.IsNullOrWhiteSpace(definition.displayName)
            ? definition.displayName.Trim()
            : "Unknown Enemy";
        string word = definition != null ? SpellRegistry.NormalizeWord(definition.weaknessSpell) : "";
        string label = string.IsNullOrEmpty(word)
            ? enemyName
            : $"{enemyName}    weakness: {word}";

        GameObject go = new GameObject($"BestiaryEntry_{enemyName}", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(spellWordButtonRoot, false);

        LayoutElement layout = go.GetComponent<LayoutElement>();
        layout.preferredHeight = 58f;
        layout.minHeight = 58f;

        Image image = go.GetComponent<Image>();
        image.color = new Color(0.12f, 0.18f, 0.29f, 0.95f);

        Button button = go.GetComponent<Button>();
        button.onClick.AddListener(() => OpenBestiaryEntry(definition));
        GameUiTheme.StyleButton(button, GameUiTheme.ButtonRole.Secondary);

        TextMeshProUGUI text = MakeText(go.transform, "Label", 21f, FontStyles.Bold, GameUiTheme.Text);
        text.text = label;
        text.alignment = TextAlignmentOptions.Left;
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = new Vector2(18f, 6f);
        text.rectTransform.offsetMax = new Vector2(-18f, -6f);

        spellWordButtons.Add(button);
        return button;
    }

    void SetSpellWordButtonsVisible(bool visible)
    {
        if (spellWordButtonRoot != null)
            spellWordButtonRoot.gameObject.SetActive(visible);
    }

    void SetOpen(bool open)
    {
        if (IsOpen == open)
        {
            if (root != null)
                root.SetActive(open);
            if (open)
                RefreshContents();
            return;
        }

        if (open)
        {
            if (!PauseMenuController.TryBeginModalPause())
                return;
            pauseLockHeld = true;
        }
        else if (pauseLockHeld)
        {
            PauseMenuController.EndModalPause();
            pauseLockHeld = false;
        }

        IsOpen = open;
        if (root != null)
            root.SetActive(open);

        if (open)
        {
            wordActionHandler?.CancelVoiceCast();
            RefreshContents();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (!TemplateRecorderUI.IsOpen && !ChestMiniGameState.IsOpen && !RunEndScreenController.IsOpen)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}
