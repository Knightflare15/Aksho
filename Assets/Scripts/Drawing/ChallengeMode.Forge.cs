using System.Collections.Generic;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public partial class ChallengeMode
{
    public void SetTier(int tier)
    {
        currentTier = Mathf.Clamp(tier, 0, (wordDatabase?.TierCount ?? 1) - 1);
        if (tierLabel != null)
            tierLabel.text = useSpellLessonSlice
                ? BuildLessonStatusLabel()
                : wordDatabase != null
                    ? wordDatabase.TierName(currentTier)
                    : $"Tier {currentTier + 1}";
    }

    public string TargetWord => targetWord;
    public int LetterIndex => letterIndex;
    public bool IsWordComplete => !string.IsNullOrEmpty(targetWord) && letterIndex >= targetWord.Length;
    public bool IsChoosingForgeSpell => choosingForgeSpell;
    public bool HasAttemptProgress =>
        letterIndex > 0 ||
        attemptsUsed > 0 ||
        wrongLetterAttempts > 0 ||
        guideCorrections > 0 ||
        giftedLetters > 0 ||
        acceptedLetterScores.Count > 0 ||
        acceptedLetterTries.Count > 0;

    public void PrepareForgeSelection()
    {
        if (IsGrammarBattleAvailable())
            PrepareSpecialForge();
        else if (legacySpellLessonsEnabled)
            PrepareLetterForge();
    }

    public void PrepareLetterForge()
    {
        if (!legacySpellLessonsEnabled)
        {
            if (IsGrammarBattleAvailable())
                PrepareSpecialForge();
            return;
        }

        requestedForgeMode = ForgePageMode.LetterPage;
        pendingForgeSelection = true;
    }

    public void PrepareSpecialForge()
    {
        requestedForgeMode = ForgePageMode.SpecialWordPage;
        pendingForgeSelection = true;
    }

    public void ResetForRetry()
    {
        acceptedLetterScores.Clear();
        acceptedLetterTries.Clear();
        letterIndex = 0;
        attemptsUsed = 0;
        wrongLetterAttempts = 0;
        guideCorrections = 0;
        giftedLetters = 0;
        lastFormationState = LetterFormationCoach.FormationState.Hidden;
        helpLevel = 0;
        LastSubmissionWasCorrect = false;
        LastAwardedShots = 3;
        LastAverageLetterScore = PDollarRecognizer.SCORE_THRESHOLD;
        voiceOneShotFeedback = "";
        lastPronunciationInsight = default;
        sessionStartedAt = Time.unscaledTime;
        speechUnlockedAt = speechUnlocked ? sessionStartedAt : -1f;
        RefreshUi();
        RefreshHintButtonVisibility();

        if (speechUnlocked)
            BeginGuideForCurrentLetter();
        else
            ConfigureVoiceUnlock();
    }

    public void DiscardPartialAttempt()
    {
        ResetForRetry();
        hint = speechUnlocked && !string.IsNullOrEmpty(targetWord)
            ? $"Cleared. Try \"{targetWord}\" again."
            : BuildSpeechHint();
    }

    char AcceptLetter(char letter, List<GameObject> strokes, float acceptedScore, int triesForLetter, string formationNote = "")
    {
        if (feedback != null)
            feedback.PlayCorrectFeedback(strokes);

        acceptedLetterScores.Add(Mathf.Clamp(acceptedScore, 0f, thresholdWrong));
        acceptedLetterTries.Add(Mathf.Max(1, triesForLetter));
        phoneticDisplayState?.RecordLetterRecognition(letter, acceptedScore, triesForLetter);
        formationCoach?.CompleteLetter();
        attemptsUsed = 0;
        helpLevel = 0;
        letterIndex++;
        lastFormationState = LetterFormationCoach.FormationState.Hidden;
        RefreshUi();

        if (letterIndex < targetWord.Length)
            BeginGuideForCurrentLetter();
        else
            HideGuide();

        if (!string.IsNullOrWhiteSpace(formationNote))
            hint = formationNote;

        return letter;
    }

    void PickNewWord()
    {
        choosingForgeSpell = false;
        hintedForgeWord = "";
        voiceOneShotFeedback = "";
        lastPronunciationInsight = default;
        targetWord = useSpellLessonSlice && legacySpellLessonsEnabled
            ? ResolvePracticeSpellWord()
            : wordDatabase != null
                ? wordDatabase.GetRandomWord(currentTier)
                : "CAT";

        acceptedLetterScores.Clear();
        acceptedLetterTries.Clear();
        letterIndex = 0;
        attemptsUsed = 0;
        wrongLetterAttempts = 0;
        guideCorrections = 0;
        giftedLetters = 0;
        lastFormationState = LetterFormationCoach.FormationState.Hidden;
        helpLevel = 0;
        LastSubmissionWasCorrect = false;
        LastAwardedShots = 3;
        LastAverageLetterScore = PDollarRecognizer.SCORE_THRESHOLD;
        sessionStartedAt = Time.unscaledTime;
        speechUnlocked = !useSpellLessonSlice || !requireSpeechUnlock;
        speechUnlockedAt = speechUnlocked ? sessionStartedAt : -1f;
    }

    void BeginForgeSelection()
    {
        pendingForgeSelection = false;
        choosingForgeSpell = true;
        hintedForgeWord = "";
        voiceOneShotFeedback = "";
        lastPronunciationInsight = default;
        targetWord = "";
        acceptedLetterScores.Clear();
        acceptedLetterTries.Clear();
        letterIndex = 0;
        attemptsUsed = 0;
        wrongLetterAttempts = 0;
        guideCorrections = 0;
        giftedLetters = 0;
        lastFormationState = LetterFormationCoach.FormationState.Hidden;
        helpLevel = 0;
        LastSubmissionWasCorrect = false;
        LastAwardedShots = 3;
        LastAverageLetterScore = PDollarRecognizer.SCORE_THRESHOLD;
        sessionStartedAt = Time.unscaledTime;
        speechUnlocked = false;
        speechUnlockedAt = -1f;
        ConfigureVoiceUnlock();
    }

    void RefreshUi()
    {
        ApplyUITheme();
        LayoutDrawingHeaderUi();

        if (promptLabel != null)
        {
            promptLabel.gameObject.SetActive(true);
            promptLabel.text = choosingForgeSpell
                ? ForgeSelectionPrompt()
                : !speechUnlocked
                ? $"Say:  {targetWord}"
                : BuildPromptString();
        }

        if (attemptsLabel != null)
        {
            attemptsLabel.gameObject.SetActive(speechUnlocked);
            if (speechUnlocked)
                attemptsLabel.text = $"Tries left: {maxAttempts - attemptsUsed}";
        }

        if (!speechUnlocked)
        {
            hint = BuildSpeechHint();
            RefreshHintButtonVisibility();
            return;
        }

        if (letterIndex < targetWord.Length)
            hint = $"Write the letter \"{targetWord[letterIndex]}\".";
        else
            hint = IsGrammarBattleForgeMode()
                ? "Press Enter to send this battle word."
                : "Press Enter to cast the spell.";

        RefreshHintButtonVisibility();
    }

    void UnlockSpeechGate()
    {
        voiceUnlockRecognizer?.StopListening(preserveRecentActivity: true);
        speechUnlocked = true;
        speechUnlockedAt = Time.unscaledTime;
        string unlockedHint = HasActionablePronunciationInsight(lastPronunciationInsight)
            ? $"{BuildPronunciationRetryHint(lastPronunciationInsight)} Now write \"{targetWord}\" one letter at a time."
            : $"Nice. Now write \"{targetWord}\" one letter at a time.";
        RefreshUi();
        hint = unlockedHint;
        ShowFeedbackPopup("Speech Accepted", unlockedHint, FeedbackPopupTone.Success);
        RefreshHintButtonVisibility();
        BeginGuideForCurrentLetter();
    }

    void SetForgeTarget(string word)
    {
        string normalized = SpellRegistry.NormalizeWord(word);
        acceptedForgePhrase = "";
        if (requestedForgeMode == ForgePageMode.LetterPage)
        {
            if (normalized.Length != 1 || spellRegistry == null || !spellRegistry.GetUnlockedLetters().Contains(normalized))
            {
                voiceOneShotFeedback = "Say an unlocked letter.";
                hint = voiceOneShotFeedback;
                ShowFeedbackPopup("Speech Rejected", hint, FeedbackPopupTone.Warning);
                return;
            }
        }
        else if (IsGrammarBattleForgeMode())
        {
            CreatureCombatRegistry registry = ResolveCreatureCombatRegistry();
            if (registry == null ||
                !registry.TryParsePhrase(word, out CreaturePhraseParseResult parsed) ||
                parsed.kind != CreaturePhraseKind.NounSummon ||
                parsed.noun == null)
            {
                voiceOneShotFeedback = string.IsNullOrEmpty(normalized)
                    ? "Say a battle noun."
                    : $"\"{word}\" is not a battle noun yet.";
                hint = voiceOneShotFeedback;
                ShowFeedbackPopup("Speech Rejected", hint, FeedbackPopupTone.Warning);
                return;
            }

            // The player only writes the noun, but we retain the canonical
            // spoken phrase so modifiers such as BIG still apply when the
            // finished word is sent to creature combat.
            acceptedForgePhrase = parsed.canonicalText;
            normalized = CreaturePhraseUtility.NormalizeToken(parsed.noun.canonicalNoun);
        }
        else if (!IsUnlockedSpellWord(normalized))
        {
            voiceOneShotFeedback = string.IsNullOrEmpty(normalized)
                ? "Say an unlocked spell word."
                : $"\"{normalized}\" is not unlocked yet.";
            hint = voiceOneShotFeedback;
            ShowFeedbackPopup("Speech Rejected", hint, FeedbackPopupTone.Warning);
            return;
        }

        voiceOneShotFeedback = "";
        targetWord = normalized;
        choosingForgeSpell = false;
        hintedForgeWord = "";
        UnlockSpeechGate();
    }

    public string ResolveWordActionPhrase(string drawnWord)
    {
        if (IsGrammarBattleForgeMode() && LastSubmissionWasCorrect &&
            !string.IsNullOrWhiteSpace(acceptedForgePhrase))
            return acceptedForgePhrase;

        return drawnWord ?? "";
    }

    bool IsUnlockedSpellWord(string word)
    {
        if (string.IsNullOrEmpty(word) || spellRegistry == null)
            return false;

        int unlockedLevel = waveDirector != null
            ? waveDirector.CurrentLevel
            : Mathf.Max(1, currentTier + 1);

        List<string> unlockedWords = spellRegistry.GetUnlockedWords(unlockedLevel);
        foreach (string unlocked in unlockedWords)
        {
            if (string.Equals(unlocked, word, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    void EnsureFormationCoach()
    {
        if ((formationCoach != null && notebookGuide != null) || drawController == null || drawController.drawingPanel == null)
            return;

        Vector2 panelSize = drawController.drawingPanel.rect.size;
        if (panelSize.x < 1f || panelSize.y < 1f)
            panelSize = new Vector2(400f, 400f);

        formationCoach ??= new LetterFormationCoach(
            drawController.drawingPanel,
            feedback,
            panelSize * 0.55f,
            templateLibrary,
            guideStrokeThickness);
        notebookGuide ??= new NotebookWritingGuide(drawController.drawingPanel, templateLibrary);
        ApplyHandwritingDevDiagnosticsVisibility();
    }
}
