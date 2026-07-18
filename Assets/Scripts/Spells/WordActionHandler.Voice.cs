using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Routes spoken/written battle words into grammar creature combat. Legacy spell pages remain for non-grammar modes.
/// </summary>
[RequireComponent(typeof(CreatureCombatController))]

public partial class WordActionHandler : MonoBehaviour
{
    void ResolveSceneReferences()
    {
        playerController ??= GetComponent<PlayerController>() ??
                             GetComponentInParent<PlayerController>() ??
                             GetComponentInChildren<PlayerController>(true) ??
                             FindAnyObjectByType<PlayerController>();

        drawController ??= GetComponent<DrawController>() ??
                           GetComponentInParent<DrawController>() ??
                           GetComponentInChildren<DrawController>(true) ??
                           FindAnyObjectByType<DrawController>();
    }

    public void BeginVoiceCast()
    {
        if (PauseMenuController.IsPaused || TemplateRecorderUI.IsOpen || GrimoireUI.IsOpen)
        {
            LogAttackVoiceDebug(
                $"[WordActionHandler] Attack voice cast ignored. paused={PauseMenuController.IsPaused} templateOpen={TemplateRecorderUI.IsOpen} grimoireOpen={GrimoireUI.IsOpen}");
            return;
        }

        if (!legacyWordSpellCastingEnabled && !IsGrammarBattleFlowActive)
        {
            SetStatusHint("Enter a grammar battle to use voice commands.");
            return;
        }

        attackButtonHeld = true;
        castListeningStartedAt = Time.unscaledTime;
        lastConsumedVoiceActivityAt = Time.unscaledTime;
        LogAttackVoiceDebug(
            $"[WordActionHandler] Attack voice cast requested. slotEmpty={SelectedSlot.IsEmpty} recognizer={(voiceCastRecognizer != null ? voiceCastRecognizer.ProviderName : "null")} available={(voiceCastRecognizer != null && voiceCastRecognizer.IsAvailable)} state={(voiceCastRecognizer != null ? voiceCastRecognizer.CurrentDisplayState.ToString() : "missing")}",
            true);
        EnsureHeldVoiceCast();
    }

    public void EndVoiceCast()
    {
        attackButtonHeld = false;
        if (voiceCastRecognizer != null &&
            voiceCastRecognizer.ActiveMode == VoiceUnlockRecognizer.VoiceInputMode.CombatAutoListen)
            voiceCastRecognizer.FinishListeningAttempt();
    }

    void EnsureHeldVoiceCast()
    {
        if (PauseMenuController.IsPaused)
        {
            CancelVoiceCast();
            return;
        }

        bool grammarBattle = IsGrammarBattleFlowActive;
        SpellbookSlot slot = grammarBattle ? null : SelectedSlot;
        if (!automaticVoiceCasting || (!grammarBattle && (slot == null || slot.IsEmpty)) || voiceCastRecognizer == null)
        {
            StopCombatVoiceListening();
            if (!grammarBattle)
                SetStatusHint("Press F to say and write a battle word.");
            LogAttackVoiceDebug(
                $"[WordActionHandler] Attack voice cast cannot start. automatic={automaticVoiceCasting} grammarBattle={grammarBattle} slotEmpty={(slot == null || slot.IsEmpty)} recognizerMissing={voiceCastRecognizer == null}");
            return;
        }

        if (!grammarBattle && spellRegistry == null)
        {
            LogAttackVoiceDebug("[WordActionHandler] Attack voice cast cannot start. SpellRegistry missing.");
            return;
        }

        string signature = BuildCastPageSignature(slot);
        if (signature != configuredCastPageSignature)
        {
            ConfigureVoiceCast(slot);
            configuredCastPageSignature = signature;
        }

        if (voiceCastRecognizer.CurrentDisplayState == VoiceUnlockRecognizer.VoiceDisplayState.PermissionDenied ||
            voiceCastRecognizer.CurrentDisplayState == VoiceUnlockRecognizer.VoiceDisplayState.Unavailable)
        {
            LogAttackVoiceDebug(
                $"[WordActionHandler] Attack voice cast blocked by recognizer state {voiceCastRecognizer.CurrentDisplayState}: {voiceCastRecognizer.StatusMessage}");
            return;
        }

        UpdateCombatPronunciationGate();
        if (TryConsumeLiveVoiceGuess(slot))
            return;

        if (!voiceCastRecognizer.IsListening && Time.unscaledTime >= nextContinuousListenAt)
        {
            UpdateCombatPronunciationGate();
            castListeningStartedAt = Time.unscaledTime;
            Debug.Log($"[WordActionHandler] Starting combat voice listen for: {configuredCastKeywords}");
            voiceCastRecognizer.StartListening(VoiceUnlockRecognizer.VoiceInputMode.CombatAutoListen);
        }
    }

    void LogAttackVoiceDebug(string message, bool immediate = false)
    {
        if (!immediate && Time.unscaledTime - lastAttackVoiceDebugAt < 0.75f)
            return;

        lastAttackVoiceDebugAt = Time.unscaledTime;
        Debug.Log(message);
    }

    void HandleCastVoiceStateChanged(VoiceUnlockRecognizer.VoiceDisplayState state)
    {
        if (!automaticVoiceCasting || voiceCastRecognizer == null)
            return;
        if (!IsGrammarBattleFlowActive && (!legacyWordSpellCastingEnabled || SelectedSlot.IsEmpty))
            return;

        if (state != VoiceUnlockRecognizer.VoiceDisplayState.Error)
            return;

        float retryAt = Time.unscaledTime + voiceCastErrorRetryDelay;
        if (retryAt > nextContinuousListenAt)
            nextContinuousListenAt = retryAt;
    }

    void ScheduleNextVoiceListen(float delay)
    {
        nextContinuousListenAt = Time.unscaledTime + Mathf.Max(0f, delay);
    }

    string BuildCastPageSignature(SpellbookSlot slot)
    {
        if (slot == null)
            return useCreatureCombat ? "C:grammar-creature-combat" : "";
        if (slot.IsEmpty)
            return useCreatureCombat ? "C:grammar-creature-combat" : "";
        return slot.pageType == SpellbookPageType.Letter
            ? $"L:{slot.pageLetter}:{string.Join("|", GetCastWordsForLetter(slot.pageLetter[0]))}"
            : $"S:{slot.spellWord}";
    }

    void ConfigureVoiceCast(SpellbookSlot slot)
    {
        castAliasesToCanonical.Clear();
        lastConsumedVoiceActivityAt = -1f;
        if (slot == null || slot.IsEmpty)
        {
            CreatureCombatRegistry registry = creatureCombat != null
                ? creatureCombat.registry ?? FindAnyObjectByType<CreatureCombatRegistry>()
                : FindAnyObjectByType<CreatureCombatRegistry>();
            if (registry == null)
            {
                configuredCastKeywords = "";
                return;
            }

            List<string> phrases = BuildUnlockedCreatureCombatKeywords(registry);
            voiceCastRecognizer.ConfigureKeywords(phrases, null, autoStart: false);
            configuredCastKeywords = "grammar battle phrases";
            SetStatusHint("Listening for grammar battle commands.", 2.5f);
        }
        else if (slot.pageType == SpellbookPageType.Letter && !string.IsNullOrEmpty(slot.pageLetter))
        {
            List<string> words = GetCastWordsForLetter(slot.pageLetter[0]);
            Dictionary<string, string> aliases = spellRegistry.GetPronunciationAliases(words);
            foreach (var pair in aliases)
                castAliasesToCanonical[pair.Key] = pair.Value;
            voiceCastRecognizer.ConfigureKeywords(words, aliases, null, autoStart: false);
            configuredCastKeywords = string.Join(", ", words);
            SetStatusHint($"Listening for {slot.pageLetter} spells: {configuredCastKeywords}", 3.5f);
        }
        else
        {
            configuredCastKeywords = slot.spellWord;
            Dictionary<string, string> aliases = spellRegistry.GetPronunciationAliases(new[] { slot.spellWord });
            foreach (var pair in aliases)
                castAliasesToCanonical[pair.Key] = pair.Value;
            voiceCastRecognizer.ConfigureKeywords(
                new[] { slot.spellWord },
                aliases,
                slot.spellWord,
                autoStart: false);
        }
    }

    List<string> BuildUnlockedCreatureCombatKeywords(CreatureCombatRegistry registry)
    {
        if (registry == null)
            return new List<string>();

        var unlockedPatterns = new HashSet<GrammarPhrasePattern>();
        var unlockedVocabulary = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<NaturalGrammarRegion> regions = NaturalGrammarProgression.Regions;
        if (regions != null)
        {
            foreach (NaturalGrammarRegion region in regions)
            {
                if (region == null || !region.combatUnlocked)
                    continue;
                if (region.unlockedPhrasePatterns != null)
                {
                    foreach (GrammarPhrasePattern pattern in region.unlockedPhrasePatterns)
                    {
                        if (pattern != GrammarPhrasePattern.LetterOnly &&
                            pattern != GrammarPhrasePattern.FullSentence)
                            unlockedPatterns.Add(pattern);
                    }
                }
            }
        }

        GrammarWorldProgressService progressService = GrammarWorldProgressService.Instance;
        GrammarWorldProgressData progress = progressService != null ? progressService.Data : null;
        if (progress != null)
        {
            if (progress.unlockedGrammarPatterns != null)
            {
                foreach (string storedPattern in progress.unlockedGrammarPatterns)
                {
                    if (Enum.TryParse(storedPattern, ignoreCase: true, out GrammarPhrasePattern pattern) &&
                        pattern != GrammarPhrasePattern.LetterOnly &&
                        pattern != GrammarPhrasePattern.FullSentence)
                        unlockedPatterns.Add(pattern);
                }
            }

            if (progress.unlockedVocabulary != null)
            {
                foreach (string word in progress.unlockedVocabulary)
                {
                    string normalized = CreaturePhraseUtility.NormalizeToken(word);
                    if (!string.IsNullOrEmpty(normalized))
                        unlockedVocabulary.Add(normalized);
                }
            }

            if (!string.IsNullOrWhiteSpace(progress.currentAreaId) && progress.areas != null)
            {
                GrammarMapAreaState currentArea = progress.areas.Find(area => area != null && area.areaId == progress.currentAreaId);
                if (currentArea != null)
                    AddRegionCombatCurriculum(unlockedPatterns, unlockedVocabulary, NaturalGrammarProgression.ResolveByTopicOrTier(currentArea.grammarTopic, currentArea.grammarTopicTier));
            }
        }

        GrammarSceneController sceneController = FindAnyObjectByType<GrammarSceneController>();
        if (sceneController != null)
            AddRegionCombatCurriculum(unlockedPatterns, unlockedVocabulary, NaturalGrammarProgression.ResolveByTopicOrTier(sceneController.grammarTopic, sceneController.grammarTopicTier));

        List<string> phrases = registry.GetVoiceKeywordsForProgression(unlockedPatterns, unlockedVocabulary);
        if (tacticalBattle != null && tacticalBattle.enableTacticalBattles)
            AddUniquePhrases(phrases, tacticalBattle.BuildVoiceKeywords());

        return phrases;
    }

    static void AddUniquePhrases(List<string> phrases, IEnumerable<string> values)
    {
        if (phrases == null || values == null)
            return;

        foreach (string value in values)
        {
            string normalized = CreaturePhraseUtility.NormalizeToken(value);
            if (!string.IsNullOrEmpty(normalized) && !phrases.Contains(normalized))
                phrases.Add(normalized);
        }
    }

    static void AddRegionCombatCurriculum(
        HashSet<GrammarPhrasePattern> unlockedPatterns,
        HashSet<string> unlockedVocabulary,
        NaturalGrammarRegion region)
    {
        if (region == null)
            return;

        if (region.unlockedPhrasePatterns != null)
        {
            foreach (GrammarPhrasePattern pattern in region.unlockedPhrasePatterns)
            {
                if (pattern != GrammarPhrasePattern.LetterOnly &&
                    pattern != GrammarPhrasePattern.FullSentence)
                    unlockedPatterns.Add(pattern);
            }
        }

        AddVocabulary(unlockedVocabulary, region.vocabularyPool);
        AddVocabulary(unlockedVocabulary, region.currentNounFamilies);
        AddVocabulary(unlockedVocabulary, region.reviewNounFamilies);
    }

    static void AddVocabulary(HashSet<string> vocabulary, IEnumerable<string> values)
    {
        if (vocabulary == null || values == null)
            return;

        foreach (string value in values)
        {
            string normalized = CreaturePhraseUtility.NormalizeToken(value);
            if (!string.IsNullOrEmpty(normalized))
                vocabulary.Add(normalized);
        }
    }

    bool TryConsumeLiveVoiceGuess(SpellbookSlot slot)
    {
        if (PauseMenuController.IsPaused)
            return false;

        if (voiceCastRecognizer == null)
            return false;

        if (slot == null || slot.IsEmpty)
        {
            if (voiceCastRecognizer.LastActivityAt < castListeningStartedAt ||
                voiceCastRecognizer.LastActivityAt <= lastConsumedVoiceActivityAt)
                return false;
            string phraseGuess = voiceCastRecognizer.LastRecognizedText;
            if (voiceCastRecognizer.CombatPronunciationInsightEnabled)
                voiceCastRecognizer.AnalyzePronunciationGuess(phraseGuess, phraseGuess, true);
            if (!TryHandleCreaturePhrase(phraseGuess))
                return false;
            lastConsumedVoiceActivityAt = voiceCastRecognizer.LastActivityAt;
            if (voiceCastRecognizer.ActiveMode == VoiceUnlockRecognizer.VoiceInputMode.CombatAutoListen)
                voiceCastRecognizer.StopListening(true);
            ScheduleNextVoiceListen(CombatVoiceRearmSeconds);
            return true;
        }

        if (spellRegistry == null)
            return false;

        if (voiceCastRecognizer.LastActivityAt < castListeningStartedAt)
            return false;
        if (voiceCastRecognizer.LastActivityAt <= lastConsumedVoiceActivityAt)
            return false;

        string guess = voiceCastRecognizer.LastRecognizedText;
        if (!TryResolveCastGuess(slot, guess, out string spellWord))
            return false;

        Debug.Log($"[WordActionHandler] Combat voice live guess accepted text='{spellWord}' raw='{guess}' keywords='{configuredCastKeywords}'");
        lastConsumedVoiceActivityAt = voiceCastRecognizer.LastActivityAt;
        UpdateCombatPronunciationGate();
        if (voiceCastRecognizer.CombatPronunciationInsightEnabled)
            voiceCastRecognizer.AnalyzePronunciationGuess(spellWord, guess, true);
        if (voiceCastRecognizer.ActiveMode == VoiceUnlockRecognizer.VoiceInputMode.CombatAutoListen)
            voiceCastRecognizer.StopListening(true);
        SetStatusHint($"Heard {spellWord}.", 0.8f);
        HandleCastWordRecognized(spellWord);
        return true;
    }

    bool TryResolveCastGuess(SpellbookSlot slot, string guess, out string spellWord)
    {
        spellWord = "";
        string normalized = SpellRegistry.NormalizeWord(guess);
        if (string.IsNullOrEmpty(normalized))
            return false;

        if (castAliasesToCanonical.TryGetValue(normalized, out string canonical))
            normalized = SpellRegistry.NormalizeWord(canonical);

        bool fitsPage = slot.pageType == SpellbookPageType.Letter
            ? !string.IsNullOrEmpty(slot.pageLetter) && normalized.StartsWith(slot.pageLetter)
            : normalized == slot.spellWord;
        if (!fitsPage || !spellRegistry.HasSpell(normalized))
            return false;

        spellWord = normalized;
        return true;
    }
}
