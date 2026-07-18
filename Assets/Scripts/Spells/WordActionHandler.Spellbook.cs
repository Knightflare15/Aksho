using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Routes spoken/written battle words into grammar creature combat. Legacy spell pages remain for non-grammar modes.
/// </summary>
[RequireComponent(typeof(CreatureCombatController))]

public partial class WordActionHandler : MonoBehaviour
{
    void Awake()
    {
        challengeMode = GetComponent<ChallengeMode>();
        ResolveSceneReferences();
        spellRegistry = GetComponent<SpellRegistry>();
        if (legacyWordSpellCastingEnabled && spellRegistry == null)
            spellRegistry = gameObject.AddComponent<SpellRegistry>();
        creatureCombat = GetComponent<CreatureCombatController>();
        if (creatureCombat == null)
            creatureCombat = gameObject.AddComponent<CreatureCombatController>();
        tacticalBattle = GetComponent<TacticalGrammarBattleController>();
        if (tacticalBattle == null)
            tacticalBattle = gameObject.AddComponent<TacticalGrammarBattleController>();
        creatureCombat.tacticalBattle = tacticalBattle;
        if ((useCreatureCombat || legacyWordSpellCastingEnabled) && GetComponent<SpellCombatHud>() == null)
            gameObject.AddComponent<SpellCombatHud>();
        if (legacyWordSpellCastingEnabled)
        {
            if (GetComponent<GrimoireUI>() == null)
                gameObject.AddComponent<GrimoireUI>();
            if (GetComponent<EnemyWaveDirector>() == null && FindAnyObjectByType<EnemyWaveDirector>() == null)
                gameObject.AddComponent<EnemyWaveDirector>();
        }

        runProgression = legacyWordSpellCastingEnabled ? RunProgressionManager.EnsureExists() : null;
        voiceCastRecognizer = GetComponent<VoiceUnlockRecognizer>() ?? gameObject.AddComponent<VoiceUnlockRecognizer>();
        learningProfile = GetComponent<PlayerLearningProfile>() ?? FindAnyObjectByType<PlayerLearningProfile>();
        aimAssist = GetComponent<PlayerAimAssist>() ?? gameObject.AddComponent<PlayerAimAssist>();
        phoneticDisplayState = PhoneticDisplayState.EnsureExists(gameObject);
        if (legacyWordSpellCastingEnabled)
            EnsureSpellbookSlots();
    }

    void OnEnable()
    {
        runProgression = legacyWordSpellCastingEnabled ? RunProgressionManager.EnsureExists() : null;
        if (runProgression != null)
            runProgression.OnUpgradesChanged += HandleRunUpgradesChanged;
        if (legacyWordSpellCastingEnabled && voiceCastRecognizer != null)
        {
            voiceCastRecognizer.OnRecognitionResolved += HandleVoiceRecognitionResolved;
            voiceCastRecognizer.OnDisplayStateChanged += HandleCastVoiceStateChanged;
            voiceCastRecognizer.OnPronunciationInsightReady += HandleCastPronunciationInsightReady;
        }
        if (creatureCombat != null)
            creatureCombat.OnStatus += HandleCreatureCombatStatus;
        if (tacticalBattle != null)
            tacticalBattle.OnStatus += HandleCreatureCombatStatus;
        CurriculumSessionManager curriculum = CurriculumSessionManager.Instance;
        if (curriculum != null)
            curriculum.OnServerPronunciationInsightReady += HandleServerPronunciationInsightReady;
    }

    void OnDisable()
    {
        if (runProgression != null)
            runProgression.OnUpgradesChanged -= HandleRunUpgradesChanged;
        if (legacyWordSpellCastingEnabled && voiceCastRecognizer != null)
        {
            voiceCastRecognizer.OnRecognitionResolved -= HandleVoiceRecognitionResolved;
            voiceCastRecognizer.OnDisplayStateChanged -= HandleCastVoiceStateChanged;
            voiceCastRecognizer.OnPronunciationInsightReady -= HandleCastPronunciationInsightReady;
        }
        if (creatureCombat != null)
            creatureCombat.OnStatus -= HandleCreatureCombatStatus;
        if (tacticalBattle != null)
            tacticalBattle.OnStatus -= HandleCreatureCombatStatus;
        CurriculumSessionManager curriculum = CurriculumSessionManager.Instance;
        if (curriculum != null)
            curriculum.OnServerPronunciationInsightReady -= HandleServerPronunciationInsightReady;
    }

    void Update()
    {
        if (PauseMenuController.IsPaused)
        {
            CancelVoiceCast();
            return;
        }

        if (TemplateRecorderUI.IsOpen || GrimoireUI.IsOpen)
        {
            CancelVoiceCast();
            return;
        }

        ResolveSceneReferences();

        if (playerController != null && playerController.IsDrawingMode)
        {
            CancelVoiceCast();
            return;
        }

        if (drawController != null && drawController.canDraw)
        {
            CancelVoiceCast();
            return;
        }

        if (legacyWordSpellCastingEnabled && Input.GetKeyDown(KeyCode.Q))
            CycleSelectedSlot(-1);

        if (legacyWordSpellCastingEnabled && Input.GetKeyDown(KeyCode.E))
            CycleSelectedSlot(1);

        if (attackButtonHeld && (legacyWordSpellCastingEnabled || IsGrammarBattleFlowActive))
            EnsureHeldVoiceCast();
    }

    public void HandleWord(string word)
    {
        if (PauseMenuController.IsPaused)
            return;

        if (challengeMode != null &&
            drawController != null &&
            ReferenceEquals(drawController.ActiveMode, challengeMode) &&
            !challengeMode.LastSubmissionWasCorrect)
        {
            Debug.Log("[WordActionHandler] Submission was not accepted by ChallengeMode, so no spell was loaded.");
            return;
        }

        if (TryHandleCreaturePhrase(word))
        {
            ScheduleNextVoiceListen(voiceCastInitialDelay);
            return;
        }

        if (IsGrammarBattleFlowActive)
        {
            SetStatusHint($"\"{word}\" is not a grammar battle phrase yet. Try a noun like RAT or a command like RAT BITES.", 3f);
            return;
        }

        if (!legacyWordSpellCastingEnabled)
        {
            SetStatusHint("Use an unlocked grammar phrase in battle; word-spell casting is not part of this game mode.", 3f);
            return;
        }

        if (spellRegistry == null)
            return;

        string key = SpellRegistry.NormalizeWord(word);
        CurriculumSessionManager curriculum = CurriculumSessionManager.Instance;
        if (challengeMode != null && challengeMode.CurrentForgeMode == ChallengeMode.ForgePageMode.LetterPage)
        {
            if (key.Length != 1) return;
            if (curriculum != null && curriculum.IsSchoolModeActive && !curriculum.IsLetterAllowed(key[0]))
            {
                SetStatusHint($"{key} is not in this world goal practice set.");
                return;
            }
            int shots = Mathf.Clamp(5 + GetQualityBonus(3), 1, GetLetterAmmoCap());
            LoadLetter(key[0], shots);
            return;
        }

        if (curriculum != null && curriculum.IsSchoolModeActive && !curriculum.IsWordAllowed(key))
        {
            SetStatusHint($"{key} is not in this world goal practice set.");
            return;
        }

        if (!spellRegistry.TryGetSpell(key, out SpellDefinition definition))
        {
            Debug.Log($"[WordActionHandler] No spell registered for '{key}'.");
            return;
        }

        int specialShots = Mathf.Clamp(2 + GetQualityBonus(1), 1, GetSpecialAmmoCap());
        LoadSpecial(definition, specialShots);
    }

    int GetAwardedShotCount(SpellDefinition definition)
    {
        int fallbackShots = definition != null
            ? Mathf.Clamp(definition.fallbackShots, 3, 8)
            : 3;

        if (challengeMode != null && challengeMode.LastSubmissionWasCorrect)
            return Mathf.Clamp(challengeMode.LastAwardedShots, 3, 8);

        return fallbackShots;
    }

    int GetQualityBonus(int maxBonus)
    {
        if (challengeMode == null || !challengeMode.LastSubmissionWasCorrect)
            return 0;
        float closeness = 1f - Mathf.InverseLerp(0f, PDollarRecognizer.SCORE_THRESHOLD, challengeMode.LastAverageLetterScore);
        return Mathf.Clamp(Mathf.RoundToInt(closeness * maxBonus), 0, maxBonus);
    }

    void LoadLetter(char letter, int shots)
    {
        SpellbookSlot slot = SelectedSlot;
        if (!slot.IsEmpty)
        {
            SetStatusHint("Selected page is already charged.");
            return;
        }
        slot.FillLetter(letter, shots, GetLetterAmmoCap());
        configuredCastPageSignature = "";
        ScheduleNextVoiceListen(voiceCastInitialDelay);
        SetStatusHint($"Page {selectedSlotIndex + 1}: LETTER {slot.pageLetter} {slot.currentAmmo}/{slot.maxAmmo}. Hold attack to cast.");
    }

    void LoadSpecial(SpellDefinition definition, int shots)
    {
        if (definition == null)
            return;

        SpellbookSlot slot = SelectedSlot;
        if (!slot.IsEmpty)
        {
            SetStatusHint($"Page already charged: {slot.spellWord} {slot.currentAmmo}/{slot.maxAmmo}");
            Debug.Log("[WordActionHandler] Selected spellbook page is already charged.");
            return;
        }

        slot.FillSpecial(definition, shots, GetSpecialAmmoCap());
        configuredCastPageSignature = "";
        ScheduleNextVoiceListen(voiceCastInitialDelay);
        SetStatusHint($"Page {selectedSlotIndex + 1}: SPECIAL {slot.spellWord} {slot.currentAmmo}/{slot.maxAmmo}. Hold attack to cast.");

        Debug.Log($"[WordActionHandler] Loaded page {selectedSlotIndex + 1} with {slot.spellWord} ({slot.currentAmmo} shot{(slot.currentAmmo == 1 ? "" : "s")}).");
    }

    public void ClearLoadedSpell()
    {
        ClearAllSlots();
    }

    public void ClearAllSlots()
    {
        if (legacyWordSpellCastingEnabled)
        {
            EnsureSpellbookSlots();
            foreach (SpellbookSlot slot in spellbookSlots)
                slot?.Clear();
        }
        creatureCombat?.ClearActiveCreature();
        CancelVoiceCast();
        SetStatusHint(IsGrammarBattleFlowActive || !legacyWordSpellCastingEnabled ? "Creature cleared." : "Spellbook cleared.");
    }

    public bool CanForgeSelectedSlot(out string message)
    {
        if (IsGrammarBattleFlowActive)
        {
            message = "Press F to say and write a battle noun.";
            return true;
        }

        if (!legacyWordSpellCastingEnabled)
        {
            message = "Word-spell pages are not used in the grammar-creature game.";
            return false;
        }

        SpellbookSlot slot = SelectedSlot;
        if (slot.IsEmpty)
        {
            message = $"Page {selectedSlotIndex + 1} empty: press F to forge.";
            return true;
        }

        string page = slot.pageType == SpellbookPageType.Letter ? $"LETTER {slot.pageLetter}" : $"SPECIAL {slot.spellWord}";
        message = $"Page already charged: {page} {slot.currentAmmo}/{slot.maxAmmo}";
        return false;
    }

    public void SetStatusHint(string message, float seconds = 1.8f)
    {
        statusHint = message ?? "";
        statusHintUntil = Time.unscaledTime + Mathf.Max(0.2f, seconds);
    }

    public bool TryGetSlotState(int index, out string spellWord, out int currentAmmo, out int maxAmmo, out bool isSelected, out bool isEmpty)
    {
        if (!legacyWordSpellCastingEnabled)
        {
            spellWord = "";
            currentAmmo = 0;
            maxAmmo = 0;
            isSelected = false;
            isEmpty = true;
            return false;
        }

        EnsureSpellbookSlots();
        if (index < 0 || index >= spellbookSlots.Length)
        {
            spellWord = "";
            currentAmmo = 0;
            maxAmmo = 0;
            isSelected = false;
            isEmpty = true;
            return false;
        }

        SpellbookSlot slot = spellbookSlots[index];
        spellWord = slot.pageType == SpellbookPageType.Letter ? $"LETTER {slot.pageLetter}" : $"SPECIAL {slot.spellWord}";
        currentAmmo = slot.currentAmmo;
        maxAmmo = slot.maxAmmo;
        isSelected = index == selectedSlotIndex;
        isEmpty = slot.IsEmpty;
        return true;
    }

    void CycleSelectedSlot(int direction)
    {
        if (!legacyWordSpellCastingEnabled)
        {
            SetStatusHint("Grammar battle uses spoken or written phrases, not spellbook pages.");
            return;
        }

        if (IsGrammarBattleFlowActive)
        {
            SetStatusHint("Grammar battle uses spoken/written phrases, not spellbook pages.");
            return;
        }

        EnsureSpellbookSlots();
        if (spellbookSlots.Length == 0)
            return;

        selectedSlotIndex = (selectedSlotIndex + direction) % spellbookSlots.Length;
        if (selectedSlotIndex < 0)
            selectedSlotIndex += spellbookSlots.Length;
        configuredCastPageSignature = "";
        StopCombatVoiceListening();
        ScheduleNextVoiceListen(voiceCastInitialDelay);

        SpellbookSlot slot = SelectedSlot;
        SetStatusHint(slot.IsEmpty
            ? $"Page {selectedSlotIndex + 1}: EMPTY - press F to forge"
            : $"Page {selectedSlotIndex + 1}: {(slot.pageType == SpellbookPageType.Letter ? $"LETTER {slot.pageLetter}" : $"SPECIAL {slot.spellWord}")} {slot.currentAmmo}/{slot.maxAmmo}. Hold attack to cast.");
    }

    public void SelectPreviousSlot()
    {
        CycleSelectedSlot(-1);
    }

    public void SelectNextSlot()
    {
        CycleSelectedSlot(1);
    }

    void EnsureSpellbookSlots()
    {
        if (!legacyWordSpellCastingEnabled)
            return;

        int desiredCount = Mathf.Max(1, spellbookSlotCount <= 0 ? DefaultSpellbookSlotCount : spellbookSlotCount);
        if (runProgression == null)
            runProgression = RunProgressionManager.Instance;
        if (runProgression != null)
            desiredCount = Mathf.Max(desiredCount, DefaultSpellbookSlotCount + runProgression.ExtraSpellbookSlots);

        if (spellbookSlots != null && spellbookSlots.Length == desiredCount)
        {
            for (int i = 0; i < spellbookSlots.Length; i++)
            {
                if (spellbookSlots[i] == null)
                    spellbookSlots[i] = new SpellbookSlot();
            }

            selectedSlotIndex = Mathf.Clamp(selectedSlotIndex, 0, spellbookSlots.Length - 1);
            return;
        }

        SpellbookSlot[] previousSlots = spellbookSlots;
        spellbookSlots = new SpellbookSlot[desiredCount];
        for (int i = 0; i < spellbookSlots.Length; i++)
        {
            spellbookSlots[i] = previousSlots != null && i < previousSlots.Length && previousSlots[i] != null
                ? previousSlots[i]
                : new SpellbookSlot();
        }

        selectedSlotIndex = Mathf.Clamp(selectedSlotIndex, 0, spellbookSlots.Length - 1);
    }

    int GetLetterAmmoCap()
    {
        if (runProgression == null)
            runProgression = RunProgressionManager.Instance;

        return Mathf.Clamp(8 + (runProgression != null ? runProgression.MaxAmmoBonus : 0), 3, 10);
    }

    int GetSpecialAmmoCap()
    {
        if (runProgression == null)
            runProgression = RunProgressionManager.Instance;
        return Mathf.Clamp(3 + (runProgression != null ? runProgression.MaxAmmoBonus : 0), 2, 5);
    }

    void HandleRunUpgradesChanged()
    {
        if (legacyWordSpellCastingEnabled)
            EnsureSpellbookSlots();
    }

    void HandleCreatureCombatStatus(string message)
    {
        SetStatusHint(message);
    }

    bool TryHandleCreaturePhrase(string phrase)
    {
        if (!useCreatureCombat)
            return false;

        if (creatureCombat == null)
            creatureCombat = GetComponent<CreatureCombatController>() ?? gameObject.AddComponent<CreatureCombatController>();
        if (tacticalBattle == null)
            tacticalBattle = GetComponent<TacticalGrammarBattleController>() ?? gameObject.AddComponent<TacticalGrammarBattleController>();
        if (creatureCombat != null)
            creatureCombat.tacticalBattle = tacticalBattle;

        return creatureCombat != null && creatureCombat.TryHandlePhrase(phrase, GetLastCastPronunciationInsight(phrase));
    }
}
