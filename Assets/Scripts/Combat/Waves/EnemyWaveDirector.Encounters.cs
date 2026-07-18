using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
#if UNITY_EDITOR
using UnityEditor;
#endif


public partial class EnemyWaveDirector : MonoBehaviour
{
    void BeginEncounter(EncounterType type, Vector3 anchor, int targetCount)
    {
        if (!IsArenaReadyForEnemySpawning(true))
        {
            if (type == EncounterType.PillarDefense)
                ResetActivePillar();
            return;
        }

        activeEncounter = BuildEncounterDescriptor(type, targetCount);
        if (activeEncounter == null)
        {
            if (type == EncounterType.PillarDefense)
                ResetActivePillar();
            return;
        }

        phase = type == EncounterType.PillarDefense ? EncounterPhase.PillarDefense : EncounterPhase.Combat;
        int deficit = Mathf.Max(0, targetCount - aliveEnemies.Count);
        StartEncounterTracking(activeEncounter);
        CreateEncounterLock(anchor);
        if (deficit > 0)
            StartCoroutine(SpawnEncounterRoutine(activeEncounter, anchor, deficit, true));
        else
            isSpawningEncounter = false;
    }

    void StartEncounterTracking(WaveDescriptor descriptor)
    {
        encountersStarted++;
        activeEncounterDefeats = 0;
        descriptor.waveIndex = encountersStarted;
        encounterStartedAt = Time.unscaledTime;
        encounterStartingHp = playerHealth != null ? playerHealth.CurrentHp : 0;
        OnWaveStarted?.Invoke(descriptor);
    }

    IEnumerator SpawnEncounterRoutine(WaveDescriptor descriptor, Vector3 anchor, int count, bool trackedEncounter)
    {
        isSpawningEncounter = true;
        int spawnedCount = 0;
        List<EnemyDefinition> definitions = BuildSpawnDefinitionsForWave(descriptor, count);
        foreach (EnemyDefinition definition in definitions)
        {
            SpellTarget enemy = SpawnEnemyInstance(definition, anchor, descriptor.encounterType);
            if (enemy != null)
            {
                RegisterEnemy(enemy, descriptor.encounterType, anchor);
                spawnedCount++;
            }
            yield return new WaitForSeconds(0.18f);
        }
        isSpawningEncounter = false;

        if (trackedEncounter && aliveEnemies.Count == 0)
        {
            if (ShouldCompleteEmptyTrackedEncounter(descriptor.encounterType, spawnedCount, activeEncounterDefeats))
                CompleteTrackedEncounter();
            else
                HandleEncounterEmptied();
        }
    }

    static bool ShouldCompleteEmptyTrackedEncounter(EncounterType type, int spawnedCount, int defeatCount)
    {
        if (defeatCount > 0)
            return true;

        return type == EncounterType.PillarDefense && spawnedCount == 0;
    }

    WaveDescriptor BuildEncounterDescriptor(EncounterType type, int requestedCount)
    {
        List<EnemyDefinition> unlocked = FilterEnemyDefinitionsForPendingZone(GetUnlockedEnemyDefinitions());
        if (unlocked.Count == 0)
            return null;

        List<EnemyDefinition> selected = SelectEnemyDefinitionsForWave(unlocked);
        if (selected.Count == 0)
            return null;

        string word = SpellRegistry.NormalizeWord(selected[0].EffectiveCreatureFamilyNoun);
        if (word == lastPlannedPrimaryWord)
            consecutivePrimaryPlans++;
        else
        {
            lastPlannedPrimaryWord = word;
            consecutivePrimaryPlans = 1;
        }

        return new WaveDescriptor
        {
            encounterType = type,
            waveIndex = encountersStarted + 1,
            effectiveWaveTier = 1 + Mathf.Max(0, CurrentLevel - 1) / 3,
            semanticZoneKind = pendingSemanticZoneKind,
            grammarTopic = pendingGrammarTopic,
            grammarTopicTier = pendingGrammarTopicTier,
            encounterNounFamilies = new List<string>(pendingEncounterNounFamilies),
            practicePatterns = new List<GrammarPhrasePattern>(pendingPracticePatterns),
            masteryTags = new List<string>(pendingMasteryTags),
            enemyDefinition = selected[0],
            enemyDefinitions = selected,
            targetSpellWord = word,
            difficultyWeight = GetAverageDifficultyWeight(selected),
            enemyCount = Mathf.Max(1, requestedCount),
        };
    }

    void ConvertPressureEnemiesToPillar(Vector3 pillarPosition)
    {
        for (int i = aliveEnemies.Count - 1; i >= 0; i--)
        {
            SpellTarget enemy = aliveEnemies[i];
            if (enemy == null)
            {
                aliveEnemies.RemoveAt(i);
                continue;
            }

            if (Vector3.Distance(enemy.transform.position, pillarPosition) <= pressureConversionRadius)
            {
                enemyStates[enemy] = new EnemyEncounterState
                {
                    type = EncounterType.PillarDefense,
                    anchor = pillarPosition,
                };
            }
            else
            {
                DespawnEnemy(enemy);
            }
        }
    }

    void UpdateLeashing()
    {
        if (TacticalGrammarBattleController.AnyActiveTacticalBattle())
            return;

        if (playerController == null)
            return;

        Vector3 playerPosition = playerController.transform.position;
        for (int i = aliveEnemies.Count - 1; i >= 0; i--)
        {
            SpellTarget enemy = aliveEnemies[i];
            if (enemy == null)
            {
                aliveEnemies.RemoveAt(i);
                continue;
            }

            if (!enemyStates.TryGetValue(enemy, out EnemyEncounterState state))
                continue;

            if (Vector3.Distance(enemy.transform.position, playerPosition) <= leashDistance)
            {
                state.farSince = -1f;
                continue;
            }

            if (state.farSince < 0f)
                state.farSince = Time.time;
            else if (Time.time - state.farSince >= leashDelaySeconds)
                DespawnEnemy(enemy);
        }

        if (!isSpawningEncounter && aliveEnemies.Count == 0 &&
            (phase == EncounterPhase.Combat || phase == EncounterPhase.PillarDefense))
        {
            HandleEncounterEmptied();
        }
    }

    void HandleEncounterEmptied()
    {
        NotifyEncounterEnded(EncounterOutcome.Failed);
        ClearEncounterLock();
        if (phase == EncounterPhase.PillarDefense)
        {
            // A defense only completes through defeats. If all enemies leashed away, it resets.
            ResetActivePillar();
        }
        else if (phase == EncounterPhase.Combat)
        {
            activeEncounter = null;
            phase = EncounterPhase.Exploration;
            ScheduleNextPressure();
        }
    }

    void HandleEnemyDefeated(SpellTarget enemy)
    {
        UnregisterEnemy(enemy);
        if (phase != EncounterPhase.Escape)
            activeEncounterDefeats++;
        if (aliveEnemies.Count != 0 || isSpawningEncounter)
            return;

        CompleteTrackedEncounter();
    }

    void CompleteTrackedEncounter()
    {
        NotifyEncounterEnded(EncounterOutcome.Completed);
        ClearEncounterLock();
        if (phase != EncounterPhase.Escape)
            RecordEncounterSuccess();
        if (phase == EncounterPhase.PillarDefense)
        {
            SpellPillarObjective completed = activePillar;
            activePillar = null;
            activeEncounter = null;
            phase = EncounterPhase.Exploration;
            ScheduleNextPressure();
            completed?.CompleteDefense();
        }
        else if (phase == EncounterPhase.Combat)
        {
            activeEncounter = null;
            phase = EncounterPhase.Exploration;
            ScheduleNextPressure();
        }
    }

    void RecordEncounterSuccess()
    {
        if (activeEncounter == null)
            return;

        int damageTaken = playerHealth != null && encounterStartingHp > 0
            ? Mathf.Max(0, encounterStartingHp - playerHealth.CurrentHp)
            : 0;
        playerLearningProfile?.RecordWaveOutcome(
            activeEncounter.targetSpellWord,
            damageTaken,
            false,
            encounterStartedAt > 0f ? Time.unscaledTime - encounterStartedAt : 0f);
    }

    void ResetActivePillar()
    {
        activePillar?.ResetDefense();
        activePillar = null;
        activeEncounter = null;
        phase = EncounterPhase.Exploration;
        ScheduleNextPressure();
    }

    void ScheduleNextPressure(float minimumDelay = -1f)
    {
        float minInterval = ResolveMinPressureInterval();
        float maxInterval = ResolveMaxPressureInterval();
        float delay = minimumDelay >= 0f
            ? minimumDelay
            : Random.Range(Mathf.Max(1f, minInterval), Mathf.Max(minInterval, maxInterval));
        nextPressureAt = Time.time + delay;
    }

    int GetPressureEnemyCap()
    {
        float progress = Mathf.InverseLerp(1f, 8f, CurrentLevel);
        return ClampEncounterCount(Mathf.RoundToInt(Mathf.Lerp(earlyPressureEnemyCap, latePressureEnemyCap, progress)));
    }

    float ResolveOpeningGraceSeconds()
    {
        return IsKidFriendlySchoolMode()
            ? Mathf.Max(0f, schoolOpeningGraceSeconds)
            : openingGraceSeconds;
    }

    float ResolveMinPressureInterval()
    {
        return IsKidFriendlySchoolMode()
            ? Mathf.Max(1f, schoolMinPressureInterval)
            : minPressureInterval;
    }

    float ResolveMaxPressureInterval()
    {
        return IsKidFriendlySchoolMode()
            ? Mathf.Max(ResolveMinPressureInterval(), schoolMaxPressureInterval)
            : maxPressureInterval;
    }

    int ClampEncounterCount(int requested)
    {
        int count = Mathf.Max(1, requested);
        if (IsKidFriendlySchoolMode())
            count = Mathf.Min(count, Mathf.Max(1, schoolEncounterEnemyCap));
        return count;
    }

    bool IsKidFriendlySchoolMode()
    {
        CurriculumSessionManager curriculum = CurriculumSessionManager.Instance;
        return useKidFriendlyPacing && curriculum != null && curriculum.IsSchoolModeActive;
    }

    bool IsArenaReadyForEnemySpawning(bool logWarning)
    {
        NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
        bool ready = triangulation.vertices != null && triangulation.vertices.Length > 0;
        if (ready)
        {
            loggedArenaNavMeshNotReady = false;
            return true;
        }

        if (logWarning && !loggedArenaNavMeshNotReady)
        {
            Debug.LogWarning("[EnemyWaveDirector] Enemy spawning blocked until a scene NavMesh is available.", this);
            loggedArenaNavMeshNotReady = true;
        }

        return false;
    }
}
