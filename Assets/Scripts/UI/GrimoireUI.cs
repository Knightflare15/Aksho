using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(SpellRegistry))]
public partial class GrimoireUI : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    private SpellRegistry spellRegistry;
    private PlayerLearningProfile learningProfile;
    private PlayerController playerController;
    private WordActionHandler wordActionHandler;
    private EnemyCatalog enemyCatalog;
    private PronunciationSpeaker pronunciationSpeaker;
    private GameObject root;
    private TextMeshProUGUI spellList;
    private Button spellWordsTabButton;
    private TextMeshProUGUI spellWordsTabLabel;
    private Button grammarTabButton;
    private TextMeshProUGUI grammarTabLabel;
    private Button bestiaryTabButton;
    private TextMeshProUGUI bestiaryTabLabel;
    private Button pronounceWordButton;
    private TextMeshProUGUI pronounceWordLabel;
    private Button pronounceLetterButton;
    private TextMeshProUGUI pronounceLetterLabel;
    private Button openSpellEntryButton;
    private TextMeshProUGUI openSpellEntryLabel;
    private Button backButton;
    private TextMeshProUGUI backButtonLabel;
    private Image detailImageFrame;
    private Image detailImage;
    private RectTransform spellWordButtonRoot;
    private readonly List<Button> spellWordButtons = new List<Button>();
    private EnemyBestiaryIdentity focusedEnemy;
    private EnemyDefinition focusedEnemyDefinition;
    private string focusedSpellWord = "";
    private GrammarConceptId focusedConceptId = GrammarConceptId.None;
    private string focusedGrammarHighlightKey = "";
    private bool showingGrammar;
    private bool showingBestiary;
    private bool pauseLockHeld;

    void Awake()
    {
        spellRegistry = GetComponent<SpellRegistry>();
        learningProfile = GetComponent<PlayerLearningProfile>() ?? FindAnyObjectByType<PlayerLearningProfile>();
        playerController = GetComponent<PlayerController>() ?? FindAnyObjectByType<PlayerController>();
        wordActionHandler = GetComponent<WordActionHandler>();
        enemyCatalog = Resources.Load<EnemyCatalog>("EnemyCatalog_Main");
        pronunciationSpeaker = PronunciationSpeaker.EnsureExists();
        BuildUi();
        SetOpen(false);
    }

    void Update()
    {
        if (IsOpen)
        {
            if (Input.GetKeyDown(KeyCode.G) || Input.GetKeyDown(KeyCode.Escape))
                SetOpen(false);
            return;
        }

        if (TemplateRecorderUI.IsOpen || ChestMiniGameState.IsOpen || PauseMenuController.IsPaused)
            return;

        if (playerController != null && playerController.IsDrawingMode)
            return;

        if (Input.GetKeyDown(KeyCode.G))
        {
            SpellCombatHud combatHud = GetComponent<SpellCombatHud>() ?? FindAnyObjectByType<SpellCombatHud>();
            if (combatHud != null && combatHud.TryOpenVisibleBestiaryEntry())
                return;

            ClearFocusedEntry();
            SetOpen(true);
        }
    }

    void OnDisable()
    {
        if (IsOpen)
            SetOpen(false);
    }

    void OnDestroy()
    {
        if (pauseLockHeld)
        {
            PauseMenuController.EndModalPause();
            pauseLockHeld = false;
        }
    }

    public void OpenBestiaryFor(EnemyBestiaryIdentity identity)
    {
        if (!IsOpen && !PauseMenuController.CanOpenBlockingModal)
            return;

        focusedEnemy = identity;
        focusedEnemyDefinition = identity != null ? identity.definition : null;
        focusedSpellWord = "";
        showingBestiary = true;
        SetOpen(true);
    }

    public void OpenSpellEntryFor(string word)
    {
        if (!IsOpen && !PauseMenuController.CanOpenBlockingModal)
            return;

        focusedSpellWord = SpellRegistry.NormalizeWord(word);
        focusedConceptId = GrammarConceptId.None;
        focusedGrammarHighlightKey = "";
        focusedEnemy = null;
        focusedEnemyDefinition = null;
        showingGrammar = false;
        showingBestiary = false;
        SetOpen(true);
    }

    public void OpenConceptFor(GrammarConceptId conceptId)
    {
        OpenConceptFor(conceptId, "");
    }

    public void OpenConceptFor(GrammarConceptId conceptId, string highlightKey)
    {
        if (conceptId == GrammarConceptId.None)
            return;
        if (!IsOpen && !PauseMenuController.CanOpenBlockingModal)
            return;

        focusedConceptId = conceptId;
        focusedGrammarHighlightKey = GrammarGrimoireCatalog.TryGetPage(conceptId, out GrammarGrimoirePage page) &&
                                     GrammarGrimoireCatalog.IsValidHighlightKey(page, highlightKey)
            ? highlightKey.Trim()
            : "";
        focusedSpellWord = "";
        focusedEnemy = null;
        focusedEnemyDefinition = null;
        showingGrammar = true;
        showingBestiary = false;
        SetOpen(true);
    }

    public void ToggleOpenFromButton()
    {
        if (IsOpen)
        {
            SetOpen(false);
            return;
        }

        SpellCombatHud combatHud = GetComponent<SpellCombatHud>() ?? FindAnyObjectByType<SpellCombatHud>();
        if (combatHud != null && combatHud.TryOpenVisibleBestiaryEntry())
            return;

        ClearFocusedEntry();
        SetOpen(true);
    }

    void OpenBestiaryEntry(EnemyDefinition definition)
    {
        if (definition == null)
            return;

        focusedEnemy = null;
        focusedEnemyDefinition = definition;
        focusedSpellWord = "";
        showingBestiary = true;
        RefreshContents();
    }

    void SetPronounceButtonsVisible(bool visible)
    {
        if (pronounceWordButton != null)
            pronounceWordButton.gameObject.SetActive(visible);
        if (pronounceLetterButton != null)
            pronounceLetterButton.gameObject.SetActive(visible);
    }

    void SetOpenSpellEntryButtonVisible(bool visible)
    {
        if (openSpellEntryButton != null)
            openSpellEntryButton.gameObject.SetActive(visible);
    }

    void SetBackButtonVisible(bool visible)
    {
        if (backButton != null)
            backButton.gameObject.SetActive(visible);
    }

    void BackToSpellWordList()
    {
        if (showingGrammar && focusedConceptId != GrammarConceptId.None)
        {
            focusedConceptId = GrammarConceptId.None;
            focusedGrammarHighlightKey = "";
            RefreshContents();
            return;
        }

        if (showingBestiary && GetFocusedEnemyDefinition() != null)
        {
            ClearFocusedEntry();
            RefreshContents();
            return;
        }

        ClearFocusedEntry();
        showingBestiary = false;
        RefreshContents();
    }

    void ShowSpellWordsList()
    {
        ClearFocusedEntry();
        showingGrammar = false;
        showingBestiary = false;
        RefreshContents();
    }

    void ShowGrammarList()
    {
        ClearFocusedEntry();
        showingGrammar = true;
        showingBestiary = false;
        RefreshContents();
    }

    void ShowBestiaryList()
    {
        ClearFocusedEntry();
        showingGrammar = false;
        showingBestiary = true;
        RefreshContents();
    }

    void ClearFocusedEntry()
    {
        focusedEnemy = null;
        focusedEnemyDefinition = null;
        focusedSpellWord = "";
        focusedConceptId = GrammarConceptId.None;
        focusedGrammarHighlightKey = "";
    }

    enum GrimoireTab
    {
        SpellWords,
        Grammar,
        Bestiary,
    }

    void RefreshTabButtons(GrimoireTab activeTab)
    {
        SetTabButtonState(spellWordsTabButton, spellWordsTabLabel, "Spell Words", activeTab == GrimoireTab.SpellWords);
        SetTabButtonState(grammarTabButton, grammarTabLabel, "Grammar", activeTab == GrimoireTab.Grammar);
        SetTabButtonState(bestiaryTabButton, bestiaryTabLabel, "Bestiary", activeTab == GrimoireTab.Bestiary);
    }

    void SetTabButtonState(Button button, TextMeshProUGUI label, string text, bool active)
    {
        if (button == null)
            return;

        Image image = button.GetComponent<Image>();
        Color color = active
            ? new Color(GameUiTheme.Gold.r, GameUiTheme.Gold.g, GameUiTheme.Gold.b, 0.92f)
            : new Color(0.12f, 0.18f, 0.29f, 0.95f);
        if (image != null)
            image.color = color;

        ColorBlock colors = button.colors;
        colors.normalColor = color;
        colors.highlightedColor = Color.Lerp(color, Color.white, 0.14f);
        colors.pressedColor = Color.Lerp(color, Color.black, 0.18f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;

        if (label != null)
        {
            label.text = active ? $"[{text}]" : text;
            label.color = active ? GameUiTheme.Ink : GameUiTheme.Text;
        }
    }

    void OpenFocusedSpellEntry()
    {
        EnemyDefinition definition = GetFocusedEnemyDefinition();
        string word = definition != null ? SpellRegistry.NormalizeWord(definition.weaknessSpell) : "";
        if (string.IsNullOrEmpty(word))
            return;

        OpenSpellEntryFor(word);
    }

    void SpeakFocusedWeaknessWord()
    {
        string word = GetFocusedPronunciationWord();
        if (string.IsNullOrEmpty(word))
        {
            Debug.LogWarning("[Pronunciation] Weakness word speak requested, but no focused spell or enemy word is available.");
            return;
        }

        AudioClip clip = null;
        if (!string.IsNullOrEmpty(focusedSpellWord) && spellRegistry.TryGetSpell(word, out SpellDefinition spellDefinition))
        {
            clip = spellDefinition.pronunciationClip;
            Debug.Log($"[Pronunciation] Speak weakness word from spell entry word='{word}' clip={DescribePronunciationClip(clip)} focusedSpellWord='{focusedSpellWord}'");
        }
        else
        {
            EnemyDefinition enemyDefinition = GetFocusedEnemyDefinition();
            if (enemyDefinition != null)
            {
                clip = enemyDefinition.weaknessPronunciationClip;
                Debug.Log($"[Pronunciation] Speak weakness word from enemy entry word='{word}' enemy='{enemyDefinition.displayName}' clip={DescribePronunciationClip(clip)}");
            }
            else
            {
                Debug.LogWarning($"[Pronunciation] Speak weakness word requested for '{word}', but no focused enemy definition was found.");
            }
        }

        pronunciationSpeaker.Speak(word, clip);
    }

    void SpeakFocusedStartingLetter()
    {
        string word = GetFocusedPronunciationWord();
        if (string.IsNullOrEmpty(word))
        {
            Debug.LogWarning("[Pronunciation] Starting-letter speak requested, but no focused spell or enemy word is available.");
            return;
        }

        char letter = word[0];
        AudioClip clip = spellRegistry != null ? spellRegistry.GetLetterPronunciationClip(letter) : null;
        Debug.Log($"[Pronunciation] Speak starting letter word='{word}' letter='{letter}' registry={spellRegistry != null} clip={DescribePronunciationClip(clip)}");
        pronunciationSpeaker.Speak(letter.ToString(), clip);
    }

    static string DescribePronunciationClip(AudioClip clip)
    {
        if (clip == null)
            return "null";

        return $"'{clip.name}' length={clip.length:0.000}s samples={clip.samples} frequency={clip.frequency} channels={clip.channels} loadState={clip.loadState}";
    }

    string GetFocusedPronunciationWord()
    {
        string word = SpellRegistry.NormalizeWord(focusedSpellWord);
        if (!string.IsNullOrEmpty(word))
            return word;

        EnemyDefinition definition = GetFocusedEnemyDefinition();
        return definition != null ? SpellRegistry.NormalizeWord(definition.weaknessSpell) : "";
    }

    Button MakePronounceButton(Transform parent, string name, Vector2 anchoredPosition, Vector2 size, out TextMeshProUGUI label)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = size;
        go.GetComponent<Image>().color = new Color(0.12f, 0.18f, 0.29f, 0.95f);

        label = MakeText(go.transform, "Label", 17f, FontStyles.Bold, GameUiTheme.Text);
        label.alignment = TextAlignmentOptions.Center;
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(8f, 4f);
        label.rectTransform.offsetMax = new Vector2(-8f, -4f);
        return go.GetComponent<Button>();
    }

    Button MakeTabButton(Transform parent, string name, string text, Vector2 anchoredPosition, out TextMeshProUGUI label)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = new Vector2(200f, 40f);

        Button button = go.GetComponent<Button>();
        GameUiTheme.StyleButton(button, GameUiTheme.ButtonRole.Secondary);

        label = MakeText(go.transform, "Label", 17f, FontStyles.Bold, GameUiTheme.Text);
        label.text = text;
        label.alignment = TextAlignmentOptions.Center;
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(10f, 4f);
        label.rectTransform.offsetMax = new Vector2(-10f, -4f);

        return button;
    }

    static TextMeshProUGUI MakeText(Transform parent, string name, float size, FontStyles style, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }
}
