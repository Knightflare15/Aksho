using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
#if UNITY_EDITOR
using UnityEditor;
#endif


public partial class EnemyWaveDirector : MonoBehaviour
{
    void Awake()
    {
        useObjectiveEscapeMode = false;
        ResolveReferences();
        ResolveEnemyCatalog();
        if (loadProgressFromProfile && playerLearningProfile != null)
            currentLevel = playerLearningProfile.ResolveStartingLevel(currentLevel, GetMaxAvailableLevel());

        playerLearningProfile?.SetCurrentLevel(currentLevel, GetMaxAvailableLevel());
    }

    void OnValidate()
    {
        ResolveEnemyCatalog();
    }

    void OnEnable()
    {
        TryHookPlayerDeath();
    }

    void OnDisable()
    {
        if (playerHealth != null && deathHookRegistered)
        {
            playerHealth.OnDied -= HandlePlayerDied;
            deathHookRegistered = false;
        }
    }

    void Start()
    {
        TryHookPlayerDeath();

        if (autoStart && !startWhenPlayerEntersArena)
            ActivateArena();
    }

    void Update()
    {
        if (!arenaActivated || isResettingArena || levelAdvanceRoutine != null)
            return;

        if (Time.time >= nextLeashCheckAt)
        {
            nextLeashCheckAt = Time.time + Mathf.Max(0.1f, leashCheckInterval);
            UpdateLeashing();
        }

        if (phase == EncounterPhase.Exploration && !isSpawningEncounter && aliveEnemies.Count == 0 &&
            Time.time >= nextPressureAt)
        {
            StartPressureEncounter();
        }
        else if (phase == EncounterPhase.Escape && !isSpawningEncounter && Time.time >= nextEscapeBatchAt)
        {
            SpawnEscapeBatch();
        }
    }

    public void ActivateArena()
    {
        if (arenaActivated || activationRoutineStarted)
            return;

        activationRoutineStarted = true;
        // Activation state is established synchronously so a dialogue-driven
        // encounter can activate and request its battle in the same frame.
        arenaActivated = true;
        encountersStarted = 0;
        phase = EncounterPhase.Waiting;
        loggedArenaNavMeshNotReady = false;
        if (useObjectiveEscapeMode)
            objectiveDirector?.BeginStage();
        StartCoroutine(ActivateArenaRoutine());
    }

    IEnumerator ActivateArenaRoutine()
    {
        yield return BeginExplorationAfterDelay();
        Debug.Log("[EnemyWaveDirector] Large-map encounter director activated.");
    }

    IEnumerator BeginExplorationAfterDelay()
    {
        yield return new WaitForSeconds(Mathf.Max(0f, initialDelay));
        // A scripted NPC encounter may have started during the opening delay.
        // Never replace its Combat phase with Exploration.
        if (phase != EncounterPhase.Waiting)
            yield break;
        phase = EncounterPhase.Exploration;
        ScheduleNextPressure(Mathf.Max(0f, ResolveOpeningGraceSeconds()));
    }

    public bool RequestPillarDefense(SpellPillarObjective pillar)
    {
        Debug.LogWarning("[EnemyWaveDirector] Spell pillar defenses are disabled in grammar-creature combat.", this);
        return false;
    }

    public bool RequestOptionalEncounter(Vector3 anchor, int enemyCount = 0)
    {
        return RequestZoneEncounter(anchor, enemyCount, SemanticZoneKind.Route, "", null);
    }

    public bool RequestZoneEncounter(
        Vector3 anchor,
        int enemyCount,
        SemanticZoneKind semanticZoneKind,
        string grammarTopic,
        IEnumerable<string> encounterNounFamilies,
        int grammarTopicTier = 1,
        IEnumerable<GrammarPhrasePattern> practicePatterns = null,
        IEnumerable<string> masteryTags = null)
    {
        // Grammar template scenes use NPC dialogue as the encounter trigger and
        // do not require a separate arena-volume trigger. Make the request
        // self-sufficient so progression cannot hard-lock on scene authoring.
        if (!arenaActivated)
            ActivateArena();

        if (!arenaActivated || phase == EncounterPhase.Escape || phase == EncounterPhase.PillarDefense ||
            aliveEnemies.Count > 0 || isSpawningEncounter)
            return false;
        if (!IsArenaReadyForEnemySpawning(true))
            return false;

        if (Time.time < nextOptionalEncounterAllowedAt)
            return false;

        int count = ClampEncounterCount(enemyCount > 0 ? enemyCount : GetPressureEnemyCap() + 1);

        // Tactical grammar battles currently model one logical enemy on the
        // grid. Keep the world encounter one-to-one with that board unit until
        // the tactical state owns a real enemy list/initiative sequence.
        if (NaturalGrammarProgression.IsTacticalCombatUnlocked(grammarTopic, grammarTopicTier))
            count = 1;
        pendingSemanticZoneKind = semanticZoneKind;
        pendingGrammarTopic = grammarTopic ?? "";
        pendingGrammarTopicTier = Mathf.Max(1, grammarTopicTier);
        pendingEncounterNounFamilies.Clear();
        pendingPracticePatterns.Clear();
        pendingMasteryTags.Clear();
        if (encounterNounFamilies != null)
        {
            foreach (string noun in encounterNounFamilies)
            {
                string normalized = SpellRegistry.NormalizeWord(noun);
                if (!string.IsNullOrEmpty(normalized) && !pendingEncounterNounFamilies.Contains(normalized))
                    pendingEncounterNounFamilies.Add(normalized);
            }
        }
        if (practicePatterns != null)
        {
            foreach (GrammarPhrasePattern pattern in practicePatterns)
                if (!pendingPracticePatterns.Contains(pattern))
                    pendingPracticePatterns.Add(pattern);
        }
        if (masteryTags != null)
        {
            foreach (string tag in masteryTags)
            {
                string normalized = NormalizeMasteryTag(tag);
                if (!string.IsNullOrEmpty(normalized) && !pendingMasteryTags.Contains(normalized))
                    pendingMasteryTags.Add(normalized);
            }
        }
        nextOptionalEncounterAllowedAt = Time.time + Mathf.Max(0f, optionalEncounterCooldownSeconds);
        BeginEncounter(EncounterType.Optional, anchor, count);
        return true;
    }

    public void BeginEscapeSurge()
    {
        if (!arenaActivated || phase == EncounterPhase.Escape)
            return;

        ClearAliveEnemies(false);
        NotifyEncounterEnded(EncounterOutcome.Cancelled);
        ClearEncounterLock();
        activePillar = null;
        activeEncounter = null;
        phase = EncounterPhase.Escape;
        escapeStartedAt = Time.time;
        nextEscapeBatchAt = Time.time + 1f;
        Debug.Log("[EnemyWaveDirector] Escape surge started.");
    }

    public string GetRecommendedSpellWord()
    {
        string aliveWeakness = GetAliveEnemyWeaknessRecommendation();
        if (!string.IsNullOrEmpty(aliveWeakness))
            return aliveWeakness;

        List<EnemyDefinition> unlocked = GetUnlockedEnemyDefinitions();
        EnemyDefinition definition = SelectEnemyDefinitionForWave(unlocked);
        if (definition != null)
            return SpellRegistry.NormalizeWord(definition.EffectiveCreatureFamilyNoun);

        return spellRegistry != null && legacyWordSpellFallbackEnabled
            ? spellRegistry.ResolveLessonWord("CAT", CurrentLevel)
            : "RAT";
    }

    void StartPressureEncounter()
    {
        if (!IsArenaReadyForEnemySpawning(true))
        {
            ScheduleNextPressure(Mathf.Max(2f, minPressureInterval * 0.25f));
            return;
        }

        Vector3 anchor = playerController != null ? playerController.transform.position : transform.position;
        pendingSemanticZoneKind = SemanticZoneKind.Route;
        pendingGrammarTopic = "";
        pendingGrammarTopicTier = 1;
        pendingEncounterNounFamilies.Clear();
        pendingPracticePatterns.Clear();
        pendingMasteryTags.Clear();
        BeginEncounter(EncounterType.Pressure, anchor, ClampEncounterCount(GetPressureEnemyCap()));
    }

    void SpawnEscapeBatch()
    {
        if (!IsArenaReadyForEnemySpawning(true))
        {
            nextEscapeBatchAt = Time.time + Mathf.Max(1f, escapeBatchInterval);
            return;
        }

        bool escalated = EscapeEscalated;
        int cap = escalated ? escalatedEscapeEnemyCap : escapeEnemyCap;
        int available = Mathf.Max(0, cap - aliveEnemies.Count);
        if (available > 0)
        {
            Vector3 anchor = playerController != null ? playerController.transform.position : transform.position;
            activeEncounter = BuildEncounterDescriptor(EncounterType.EscapeSurge, ClampEncounterCount(Mathf.Min(available, escalated ? 4 : 3)));
            if (activeEncounter != null)
                StartCoroutine(SpawnEncounterRoutine(activeEncounter, anchor, activeEncounter.enemyCount, false));
        }

        nextEscapeBatchAt = Time.time + (escalated ? escalatedEscapeBatchInterval : escapeBatchInterval);
    }
}
