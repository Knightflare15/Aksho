using System.Collections.Generic;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public partial class ChallengeMode
{
    void EnsureHintButton()
    {
        if (hintButton != null || drawController == null || drawController.drawingPanel == null)
            return;

        RectTransform parent = VoiceControlParent();
        if (parent == null)
            return;

        var buttonGo = new GameObject("HintButton", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonGo.transform.SetParent(parent, false);
        var rt = buttonGo.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(1f, 1f);
        rt.sizeDelta = new Vector2(HintButtonWidth, HintButtonHeight);
        PlaceHintButtonOutsideDrawingPanel(rt);

        var image = buttonGo.GetComponent<Image>();
        image.color = new Color(GameUiTheme.PanelRaised.r, GameUiTheme.PanelRaised.g, GameUiTheme.PanelRaised.b, 0.95f);

        hintButton = buttonGo.GetComponent<Button>();
        hintButton.onClick.AddListener(HandleHintButtonPressed);

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(buttonGo.transform, false);
        var labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;

        hintButtonLabel = labelGo.GetComponent<TextMeshProUGUI>();
        hintButtonLabel.font = TMP_Settings.defaultFontAsset;
        hintButtonLabel.text = "Hint";
        hintButtonLabel.fontSize = 30f;
        hintButtonLabel.fontStyle = FontStyles.Bold;
        hintButtonLabel.alignment = TextAlignmentOptions.Center;
        hintButtonLabel.verticalAlignment = VerticalAlignmentOptions.Middle;
        hintButtonLabel.color = GameUiTheme.Text;
    }

    void PlaceHintButtonOutsideDrawingPanel(RectTransform rect)
    {
        PlaceHintButtonInDrawingHeader(rect);
    }

    void PlaceHintButtonInDrawingHeader(RectTransform rect)
    {
        if (rect == null || drawController == null || drawController.drawingPanel == null)
            return;

        RectTransform parent = VoiceControlParent();
        if (parent == null)
            return;

        if (rect.parent != parent)
            rect.SetParent(parent, false);

        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);

        NotebookWritingGuide.NotebookSlot slot = NotebookWritingGuide.CalculateSlot(
            drawController.drawingPanel.rect,
            string.IsNullOrEmpty(targetWord) ? "C" : targetWord,
            Mathf.Clamp(letterIndex, 0, Mathf.Max(0, targetWord.Length - 1)));

        float pageTop = drawController.drawingPanel.anchoredPosition.y + drawController.drawingPanel.rect.yMax;
        float pageRight = drawController.drawingPanel.anchoredPosition.x + drawController.drawingPanel.rect.xMax;
        float upperRule = drawController.drawingPanel.anchoredPosition.y + slot.topY;
        float headerHeight = Mathf.Max(58f, pageTop - upperRule);
        float headerCenterY = upperRule + headerHeight * 0.5f;
        float inset = Mathf.Clamp(drawController.drawingPanel.rect.width * 0.035f, 24f, 58f);

        rect.anchoredPosition = new Vector2(pageRight - inset, headerCenterY);
    }

    void RefreshHintButtonVisibility()
    {
        EnsureHintButton();
        if (hintButton != null)
        {
            PlaceHintButtonOutsideDrawingPanel(hintButton.GetComponent<RectTransform>());
            bool drawingActive = drawController != null && drawController.canDraw;
            bool showForVoiceChoice = drawingActive && choosingForgeSpell && !speechUnlocked;
            bool showForLetterHelp = drawingActive && speechUnlocked && letterIndex < targetWord.Length;
            hintButton.gameObject.SetActive(showForVoiceChoice || showForLetterHelp);
            if (hintButtonLabel != null)
                hintButtonLabel.text = showForLetterHelp ? "Help" : "Hint";
        }
    }

    void HandleHintButtonPressed()
    {
        if (speechUnlocked && letterIndex < targetWord.Length)
        {
            EnsureFormationCoach();
            helpLevel = Mathf.Clamp(helpLevel + 1, 1, 2);
            StopGuidePulse();
            formationCoach?.SetStartMarkerEnabled(true);
            if (helpLevel == 1)
            {
                guidePulseRoutine = StartCoroutine(ShowGuideBriefly());
                hint = $"Watch '{targetWord[letterIndex]}' once, then follow the glow.";
            }
            else
            {
                formationCoach?.ShowTraceOverlay();
                hint = $"Follow the faint '{targetWord[letterIndex]}' template.";
            }

            if (attemptsLabel != null)
                attemptsLabel.text = $"Help {helpLevel}/2";
            return;
        }

        string recommended = ResolveForgeFallbackChoice();
        if (string.IsNullOrEmpty(recommended))
            recommended = spellRegistry != null ? spellRegistry.ResolveLessonWord("CAT") : "CAT";

        hint = $"Try: {recommended}";
        hintedForgeWord = recommended;
        spellHintSpeaker?.Speak(recommended);

        if (attemptsLabel != null)
            attemptsLabel.text = hint;
    }

    string ResolveLessonSpellWord()
    {
        if (spellRegistry == null)
            return spellLessonWord.ToUpperInvariant().Trim();

        string resolved = spellRegistry.ResolveLessonWord(spellLessonWord);
        if (!string.IsNullOrEmpty(resolved))
            return resolved;

        return spellLessonWord.ToUpperInvariant().Trim();
    }

    string ResolvePracticeSpellWord()
    {
        if (waveDirector != null)
        {
            string enemyDrivenWord = waveDirector.GetRecommendedSpellWord();
            if (!string.IsNullOrEmpty(enemyDrivenWord))
                return enemyDrivenWord;
        }

        if (spellRegistry == null)
            return spellLessonWord.ToUpperInvariant().Trim();

        int unlockedLevel = waveDirector != null
            ? waveDirector.CurrentLevel
            : Mathf.Max(1, currentTier + 1);

        string resolved = spellRegistry.ResolveLessonWord(spellLessonWord, unlockedLevel);
        if (!string.IsNullOrEmpty(resolved))
            return resolved;

        return ResolveLessonSpellWord();
    }

    string BuildLessonStatusLabel()
    {
        if (waveDirector == null)
            return "Spell Lesson";

        WaveDescriptor focusWave = waveDirector.CurrentWaveDescriptor;
        return focusWave == null
            ? $"Stage {waveDirector.CurrentLevel}  {waveDirector.CurrentPhase}"
            : $"Stage {waveDirector.CurrentLevel}  {focusWave.encounterType}";
    }

    bool CanUseEditorSpeechFallback()
    {
#if UNITY_EDITOR
        return allowEditorSpeechFallback;
#else
        return false;
#endif
    }

    string BuildSpeechStatusLabel()
    {
        if (choosingForgeSpell)
        {
            if (voiceUnlockRecognizer != null && voiceUnlockRecognizer.IsAvailable)
                return voiceUnlockRecognizer.IsListening
                    ? IsGrammarBattleForgeMode()
                        ? "Listening: battle phrase"
                        : $"Listening: unlocked {VoiceSelectionNoun()}"
                    : voiceUnlockRecognizer.StatusMessage;

            return CanUseEditorSpeechFallback()
                ? $"Voice unavailable. Editor fallback: {editorSpeechKey}"
                : "Voice unavailable.";
        }

        if (voiceUnlockRecognizer != null && voiceUnlockRecognizer.IsAvailable)
            return voiceUnlockRecognizer.IsListening
                ? $"Listening: {targetWord}"
                : voiceUnlockRecognizer.StatusMessage;

        return CanUseEditorSpeechFallback()
            ? $"Voice unavailable. Editor fallback: {editorSpeechKey}"
            : "Voice unavailable.";
    }

    string ForgeSelectionPrompt()
    {
        if (IsGrammarBattleForgeMode())
            return "Say a battle noun";
        return requestedForgeMode == ForgePageMode.LetterPage ? "Say a letter" : "Say a word";
    }

    string VoiceSelectionNoun()
    {
        return requestedForgeMode == ForgePageMode.LetterPage ? "letter" : "word";
    }

    List<string> GetForgeFallbackOptions()
    {
        var options = new List<string>();
        if (IsGrammarBattleForgeMode())
        {
            CreatureCombatRegistry registry = ResolveCreatureCombatRegistry();
            if (registry != null)
                options.AddRange(BuildGrammarForgeKeywords(registry));
            return options;
        }

        if (spellRegistry == null)
            return options;

        if (requestedForgeMode == ForgePageMode.LetterPage)
        {
            options.AddRange(spellRegistry.GetUnlockedLetters());
            return options;
        }

        int unlockedLevel = waveDirector != null ? waveDirector.CurrentLevel : Mathf.Max(1, currentTier + 1);
        options.AddRange(spellRegistry.GetUnlockedWords(unlockedLevel));
        return options;
    }

    string ResolveForgeFallbackChoice()
    {
        List<string> options = GetForgeFallbackOptions();
        if (options.Count > 0)
            return options[0];

        return requestedForgeMode == ForgePageMode.LetterPage ? "C" : ResolvePracticeSpellWord();
    }

    bool IsGrammarBattleForgeMode()
    {
        if (requestedForgeMode != ForgePageMode.SpecialWordPage)
            return false;

        CreatureCombatController combat = ResolveCreatureCombat();
        return combat != null && combat.enabledForPhrases;
    }

    bool IsGrammarBattleAvailable()
    {
        CreatureCombatController combat = ResolveCreatureCombat();
        return combat != null && combat.enabledForPhrases;
    }

    CreatureCombatController ResolveCreatureCombat()
    {
        if (creatureCombat == null)
            creatureCombat = GetComponent<CreatureCombatController>() ?? FindAnyObjectByType<CreatureCombatController>();
        return creatureCombat;
    }

    CreatureCombatRegistry ResolveCreatureCombatRegistry()
    {
        if (creatureCombatRegistry == null)
        {
            CreatureCombatController combat = ResolveCreatureCombat();
            creatureCombatRegistry = combat != null && combat.registry != null
                ? combat.registry
                : GetComponent<CreatureCombatRegistry>() ?? FindAnyObjectByType<CreatureCombatRegistry>();
        }

        return creatureCombatRegistry;
    }

    static List<string> BuildGrammarForgeKeywords(CreatureCombatRegistry registry)
    {
        var result = new List<string>();
        if (registry == null || registry.Nouns == null)
            return result;

        foreach (NounDefinition noun in registry.Nouns)
        {
            if (noun == null)
                continue;

            AddUniqueGrammarForgeKeyword(result, noun.canonicalNoun);
            if (noun.synonyms == null)
                continue;
            foreach (string synonym in noun.synonyms)
                AddUniqueGrammarForgeKeyword(result, synonym);
        }

        return result;
    }

    static void AddUniqueGrammarForgeKeyword(List<string> values, string value)
    {
        string normalized = CreaturePhraseUtility.NormalizeToken(value);
        if (!string.IsNullOrEmpty(normalized) && !values.Contains(normalized))
            values.Add(normalized);
    }

    static readonly string[] WrongMessagesWarm =
    {
        "Close. That looked like {wrong}, but I need {expected}.",
        "Almost. Try shaping that into {expected}.",
        "Nice effort. Let's bring it back to {expected}.",
    };

    static readonly string[] WrongMessagesMid =
    {
        "That came out as {wrong}. Shape it into {expected}.",
        "Not quite. We need {expected} for this spell.",
        "Reset and try {expected} again.",
    };

    static readonly string[] WrongMessagesVeryWrong =
    {
        "That shape is far from {expected}. Try the help button.",
        "That drifted far away from {expected}. Try again.",
        "Let's slow down and trace {expected}.",
    };
}
