using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class GrimoireUI : MonoBehaviour
{
    void RefreshContents()
    {
        if (spellList == null || spellRegistry == null)
            return;

        if (learningProfile == null)
            learningProfile = GetComponent<PlayerLearningProfile>() ?? FindAnyObjectByType<PlayerLearningProfile>();
        if (pronunciationSpeaker == null)
            pronunciationSpeaker = PronunciationSpeaker.EnsureExists();

        if (showingGrammar)
        {
            RefreshGrammarContents();
            return;
        }

        if (showingBestiary)
        {
            RefreshBestiaryContents();
            return;
        }

        if (!string.IsNullOrEmpty(focusedSpellWord))
        {
            RefreshSpellEntryContents();
            return;
        }

        SetDetailImage(null);
        SetPronounceButtonsVisible(false);
        SetOpenSpellEntryButtonVisible(false);
        SetBackButtonVisible(false);
        RefreshTabButtons(GrimoireTab.SpellWords);

        RefreshSpellWordButtons();
        return;

#pragma warning disable CS0162
        var text = new System.Text.StringBuilder();
        List<string> letters = spellRegistry.GetUnlockedLetters();
        foreach (string letterValue in letters)
        {
            if (string.IsNullOrEmpty(letterValue))
                continue;

            List<string> words = spellRegistry.GetWordsForLetter(letterValue[0], unlockedOnly: true);
            if (words.Count == 0)
                continue;

            text.Append("<color=#FFCF61><b>").Append(letterValue).Append("</b></color>   ");
            for (int i = 0; i < words.Count; i++)
            {
                string word = words[i];
                if (learningProfile != null && learningProfile.IsWordMastered(word))
                    text.Append("★ ");
                text.Append(word);
                if (i < words.Count - 1)
                    text.Append("    ");
            }
            text.AppendLine().AppendLine();
        }

        spellList.text = text.Length > 0
            ? text.ToString()
            : "No spell words unlocked yet. Forge and practice a letter to begin.";
    }

#pragma warning restore CS0162
    void RefreshSpellWordButtons()
    {
        ClearSpellWordButtons();
        SetSpellWordButtonsVisible(true);
        SetDetailImage(null);

        var text = new System.Text.StringBuilder();
        List<string> letters = spellRegistry.GetUnlockedLetters();
        foreach (string letterValue in letters)
        {
            if (string.IsNullOrEmpty(letterValue))
                continue;

            List<string> words = spellRegistry.GetWordsForLetter(letterValue[0], unlockedOnly: true);
            if (words.Count == 0)
                continue;

            text.Append("<color=#FFCF61><b>").Append(letterValue).Append("</b></color>").AppendLine();
            foreach (string word in words)
            {
                string normalized = SpellRegistry.NormalizeWord(word);
                if (string.IsNullOrEmpty(normalized))
                    continue;

                string mastery = learningProfile != null && learningProfile.IsWordMastered(normalized)
                    ? "  MASTERED"
                    : "";
                MakeSpellWordButton($"{letterValue}    {normalized}{mastery}", normalized);
            }
            text.AppendLine();
        }

        spellList.text = spellWordButtons.Count > 0
            ? ""
            : "No spell words unlocked yet. Forge and practice a letter to begin.";
        SetSpellWordButtonsVisible(spellWordButtons.Count > 0);
    }

    void RefreshGrammarContents()
    {
        ClearSpellWordButtons();
        SetDetailImage(null);
        SetPronounceButtonsVisible(false);
        SetOpenSpellEntryButtonVisible(false);
        RefreshTabButtons(GrimoireTab.Grammar);

        if (focusedConceptId != GrammarConceptId.None)
        {
            SetSpellWordButtonsVisible(false);
            SetBackButtonVisible(true);
            if (GrammarGrimoireCatalog.TryGetPage(focusedConceptId, out GrammarGrimoirePage page))
            {
                spellList.text = GrammarGrimoireCatalog.BuildPageText(page, focusedGrammarHighlightKey);
                return;
            }

            spellList.text = "This grammar page is not available yet.";
            return;
        }

        SetBackButtonVisible(false);
        List<GrammarGrimoirePage> pages = GrammarGrimoireCatalog.GetUnlockedPages();
        SetSpellWordButtonsVisible(pages.Count > 0);
        if (pages.Count == 0)
        {
            spellList.text = "No grammar pages unlocked yet. Talk to the first guide to begin.";
            return;
        }

        foreach (GrammarGrimoirePage page in pages)
            MakeGrammarConceptButton(page);
        spellList.text = "";
    }

    Button MakeGrammarConceptButton(GrammarGrimoirePage page)
    {
        string label = page != null && !string.IsNullOrWhiteSpace(page.title)
            ? page.title.Trim()
            : "Grammar Page";
        GrammarConceptId conceptId = page != null ? page.conceptId : GrammarConceptId.None;

        GameObject go = new GameObject($"GrammarConcept_{conceptId}", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(spellWordButtonRoot, false);

        LayoutElement layout = go.GetComponent<LayoutElement>();
        layout.preferredHeight = 58f;
        layout.minHeight = 58f;

        Image image = go.GetComponent<Image>();
        image.color = new Color(0.12f, 0.18f, 0.29f, 0.95f);

        Button button = go.GetComponent<Button>();
        button.onClick.AddListener(() => OpenConceptFor(conceptId));
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

    void RefreshBestiaryContents()
    {
        ClearSpellWordButtons();
        SetDetailImage(null);
        SetPronounceButtonsVisible(false);
        SetOpenSpellEntryButtonVisible(false);
        RefreshTabButtons(GrimoireTab.Bestiary);

        EnemyDefinition definition = GetFocusedEnemyDefinition();
        if (definition == null)
        {
            RefreshBestiaryListContents();
            return;
        }

        SetSpellWordButtonsVisible(false);
        SetBackButtonVisible(true);

        string word = SpellRegistry.NormalizeWord(definition.weaknessSpell);
        string letter = string.IsNullOrEmpty(word) ? "" : word[0].ToString();
        SetDetailImage(definition.bestiaryPhoto);
        spellList.text =
            $"<color=#FFCF61><b>{definition.displayName}</b></color>\n\n" +
            $"Spell that defeats it: <b>{word}</b>\n" +
            $"Starts with letter: <b>{letter}</b>\n\n" +
            $"{definition.learningFocus}";

        SetPronounceButtonsVisible(!string.IsNullOrEmpty(word));
        SetOpenSpellEntryButtonVisible(!string.IsNullOrEmpty(word));
        if (pronounceWordLabel != null)
            pronounceWordLabel.text = $"Hear {word}";
        if (pronounceLetterLabel != null)
            pronounceLetterLabel.text = $"Hear {letter}";
        if (openSpellEntryLabel != null)
            openSpellEntryLabel.text = $"Open {word} spell";
    }

    void RefreshBestiaryListContents()
    {
        List<EnemyDefinition> definitions = GetAvailableEnemyDefinitions();
        SetBackButtonVisible(false);
        SetSpellWordButtonsVisible(definitions.Count > 0);

        if (definitions.Count == 0)
        {
            spellList.text = "No bestiary entries are available yet.";
            return;
        }

        foreach (EnemyDefinition definition in definitions)
            MakeBestiaryEntryButton(definition);

        spellList.text = "";
    }

    void RefreshSpellEntryContents()
    {
        ClearSpellWordButtons();
        SetSpellWordButtonsVisible(false);
        string word = SpellRegistry.NormalizeWord(focusedSpellWord);
        if (string.IsNullOrEmpty(word))
        {
            focusedSpellWord = "";
            RefreshContents();
            return;
        }

        spellRegistry.TryGetSpell(word, out SpellDefinition definition);
        string letter = word[0].ToString();

        SetDetailImage(definition != null ? definition.grimoirePhoto : null);
        SetPronounceButtonsVisible(true);
        SetOpenSpellEntryButtonVisible(false);
        SetBackButtonVisible(true);
        RefreshTabButtons(GrimoireTab.SpellWords);

        string focus = definition != null && !string.IsNullOrWhiteSpace(definition.instructionalFocus)
            ? definition.instructionalFocus
            : "Practice the word, then listen for its first sound.";

        spellList.text =
            $"<color=#FFCF61><b>{word}</b></color>\n\n" +
            $"Starts with letter: <b>{letter}</b>\n\n" +
            $"{focus}";

        if (pronounceWordLabel != null)
            pronounceWordLabel.text = $"Hear {word}";
        if (pronounceLetterLabel != null)
            pronounceLetterLabel.text = $"Hear {letter}";
    }

    EnemyDefinition FindFirstAvailableEnemyDefinition()
    {
        List<EnemyDefinition> definitions = GetAvailableEnemyDefinitions();
        return definitions.Count > 0 ? definitions[0] : null;
    }

    List<EnemyDefinition> GetAvailableEnemyDefinitions()
    {
        var definitions = new List<EnemyDefinition>();
        if (enemyCatalog == null)
            enemyCatalog = Resources.Load<EnemyCatalog>("EnemyCatalog_Main");
        if (enemyCatalog == null)
        {
            EnemyWaveDirector waveDirector = FindAnyObjectByType<EnemyWaveDirector>();
            if (waveDirector != null)
                enemyCatalog = waveDirector.enemyCatalog;
        }
        if (enemyCatalog == null || enemyCatalog.enemyDefinitions == null)
            return definitions;

        CurriculumSessionManager curriculum = CurriculumSessionManager.Instance;
        foreach (EnemyDefinition definition in enemyCatalog.enemyDefinitions)
        {
            if (definition == null)
                continue;

            string word = SpellRegistry.NormalizeWord(definition.weaknessSpell);
            if (curriculum != null && curriculum.IsSchoolModeActive && !curriculum.IsWordAllowed(word))
                continue;
            if (learningProfile != null && !learningProfile.IsWordUnlocked(word))
                continue;

            definitions.Add(definition);
        }

        return definitions;
    }

    EnemyDefinition GetFocusedEnemyDefinition()
    {
        if (focusedEnemy != null && focusedEnemy.definition != null)
            return focusedEnemy.definition;
        return focusedEnemyDefinition;
    }
}
