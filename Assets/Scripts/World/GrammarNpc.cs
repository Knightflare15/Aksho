using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;


public class GrammarNpc : MonoBehaviour
{
    public string npcId = "npc";
    public string displayName = "Guide";
    public SemanticZoneKind sceneKind = SemanticZoneKind.Town;
    public string grammarTopic = "Nouns and verbs";
    [Min(1)] public int grammarTopicTier = 1;
    public TranslatorAssistMode translatorAssist = TranslatorAssistMode.Full;
    public float interactionRadius = 3f;
    public List<LocalizedDialogueLine> dialogueLines = new List<LocalizedDialogueLine>();
    public bool startsTrainerBattle;
    [Min(1)] public int trainerEnemyCount = 1;
    public List<string> trainerEncounterNounFamilies = new List<string>();
    public List<GrammarPhrasePattern> trainerPracticePatterns = new List<GrammarPhrasePattern>();
    public List<string> trainerMasteryTags = new List<string>();

    PlayerController player;
    int currentLineIndex;
    bool trainerBattleConsumed;

    public void Configure(
        GrammarNpcSpawnDefinition spawn,
        SemanticZoneKind sceneKind,
        string grammarTopic,
        int grammarTopicTier,
        TranslatorAssistMode translatorAssist)
    {
        if (spawn != null)
        {
            npcId = string.IsNullOrWhiteSpace(spawn.npcId) ? npcId : spawn.npcId;
            displayName = string.IsNullOrWhiteSpace(spawn.displayName) ? displayName : spawn.displayName;
            dialogueLines = spawn.dialogueLines ?? new List<LocalizedDialogueLine>();
            startsTrainerBattle = spawn.startsTrainerBattle;
            trainerEnemyCount = Mathf.Max(1, spawn.trainerEnemyCount);
            trainerEncounterNounFamilies = spawn.trainerEncounterNounFamilies ?? new List<string>();
            trainerPracticePatterns = spawn.trainerPracticePatterns ?? new List<GrammarPhrasePattern>();
            trainerMasteryTags = spawn.trainerMasteryTags ?? new List<string>();
        }

        this.sceneKind = sceneKind;
        this.grammarTopic = grammarTopic;
        this.grammarTopicTier = Mathf.Max(1, grammarTopicTier);
        this.translatorAssist = translatorAssist;
        EnsureCollider();
        EnsureFallbackLabel();
    }

    void Awake()
    {
        EnsureCollider();
        EnsureFallbackLabel();
    }

    void Update()
    {
        if (IsUiBlocking())
            return;

        player ??= FindAnyObjectByType<PlayerController>();
        if (player == null)
            return;

        float distance = Vector3.Distance(transform.position, player.transform.position);
        if (distance > Mathf.Max(0.5f, interactionRadius))
            return;

        NpcInteractionPromptUI.EnsureExists().Show(this);
        if (WasInteractPressed())
            Talk();
    }

    public void Talk()
    {
        TranslatorBuddyService buddy = TranslatorBuddyService.EnsureExists();
        buddy.SetAssistMode(translatorAssist);

        LocalizedDialogueLine line = GetCurrentLine();
        NpcDialogueUI.EnsureExists().Show(this, line, buddy);
        currentLineIndex = dialogueLines.Count > 0
            ? (currentLineIndex + 1) % dialogueLines.Count
            : 0;

        if (!RequiresSpokenResponse(line))
            TryStartTrainerBattle();
    }

    public void HandleDialogueResponseAccepted(LocalizedDialogueLine line)
    {
        bool tasksComplete = GrammarWorldProgressService.Instance.RegisterCurrentAreaDialogueTaskCompleted(
            line,
            sceneKind,
            grammarTopic,
            grammarTopicTier);
        if (sceneKind != SemanticZoneKind.Gym || tasksComplete)
            TryStartTrainerBattle();
    }

    public LocalizedDialogueLine GetCurrentLine()
    {
        if (dialogueLines == null || dialogueLines.Count == 0)
        {
            return new LocalizedDialogueLine
            {
                lineId = $"{npcId}-fallback",
                sourceText = $"This {sceneKind.ToString().ToLowerInvariant()} is about {grammarTopic}.",
                sourceLanguage = "en",
                expectedEnglishResponse = "I understand",
            };
        }

        return dialogueLines[Mathf.Clamp(currentLineIndex, 0, dialogueLines.Count - 1)];
    }

    void EnsureCollider()
    {
        Collider collider = GetComponentInChildren<Collider>();
        if (collider == null)
            collider = gameObject.AddComponent<BoxCollider>();
        collider.isTrigger = false;
    }

    void EnsureFallbackLabel()
    {
        TextMeshPro existing = GetComponentInChildren<TextMeshPro>();
        if (existing != null)
        {
            existing.text = displayName;
            return;
        }

        GameObject labelGo = new GameObject("NPC Label", typeof(TextMeshPro));
        labelGo.transform.SetParent(transform, false);
        labelGo.transform.localPosition = Vector3.up * 1.35f;
        labelGo.transform.localRotation = Quaternion.identity;
        TextMeshPro label = labelGo.GetComponent<TextMeshPro>();
        label.text = displayName;
        label.fontSize = 2.2f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
    }

    void TryStartTrainerBattle()
    {
        if (!startsTrainerBattle || trainerBattleConsumed)
            return;

        GrammarEncounterMode encounterMode = NaturalGrammarProgression.ResolveEncounterMode(grammarTopic, grammarTopicTier);
        if (encounterMode != GrammarEncounterMode.TacticalCommand &&
            encounterMode != GrammarEncounterMode.NounRecognition)
            return;

        EnemyWaveDirector director = FindAnyObjectByType<EnemyWaveDirector>();
        if (director == null)
        {
            Debug.LogWarning($"[GrammarNpc] Trainer '{displayName}' cannot start a battle because no EnemyWaveDirector exists.", this);
            return;
        }

        TranslatorBuddyService buddy = FindAnyObjectByType<TranslatorBuddyService>();
        if (buddy != null)
            buddy.SetAssistMode(translatorAssist);

        CreatureCombatRegistry registry = FindAnyObjectByType<CreatureCombatRegistry>();
        List<string> resolvedNouns = GrammarRouteContext.Instance.ResolveEncounterNounFamilies(
            registry,
            sceneKind,
            grammarTopic,
            grammarTopicTier,
            trainerEncounterNounFamilies);

        bool started = director.RequestZoneEncounter(
            transform.position,
            Mathf.Max(1, trainerEnemyCount),
            sceneKind,
            grammarTopic,
            resolvedNouns,
            grammarTopicTier,
            trainerPracticePatterns,
            trainerMasteryTags);
        if (started)
            trainerBattleConsumed = true;
        else
            Debug.LogWarning($"[GrammarNpc] Trainer '{displayName}' battle was declined by EnemyWaveDirector.", this);
    }

    static bool WasInteractPressed()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.eKey.wasPressedThisFrame)
            return true;
        if (Input.GetMouseButtonDown(1))
            return true;
        Mouse mouse = Mouse.current;
        return mouse != null && mouse.rightButton.wasPressedThisFrame;
    }

    static bool IsUiBlocking()
    {
        return PauseMenuController.IsPaused ||
               TemplateRecorderUI.IsOpen ||
               GrimoireUI.IsOpen ||
               ChestMiniGameState.IsOpen ||
               RunEndScreenController.IsOpen;
    }

    static bool RequiresSpokenResponse(LocalizedDialogueLine line)
    {
        return line != null && !string.IsNullOrWhiteSpace(line.expectedEnglishResponse);
    }
}
