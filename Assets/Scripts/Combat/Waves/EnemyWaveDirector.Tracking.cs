using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
#if UNITY_EDITOR
using UnityEditor;
#endif


public partial class EnemyWaveDirector : MonoBehaviour
{
    void RegisterEnemy(SpellTarget enemy, EncounterType type, Vector3 anchor)
    {
        if (enemy == null || aliveEnemies.Contains(enemy))
            return;
        aliveEnemies.Add(enemy);
        enemyStates[enemy] = new EnemyEncounterState { type = type, anchor = anchor };
        enemy.OnDefeated += HandleEnemyDefeated;
        CheeseEnemyAgent agent = enemy.GetComponent<CheeseEnemyAgent>();
        if (agent != null)
        {
            agent.SetAttackCoordinator(attackCoordinator);
            attackCoordinator?.RegisterAgent(agent);
        }
    }

    void UnregisterEnemy(SpellTarget enemy)
    {
        if (enemy != null)
        {
            enemy.OnDefeated -= HandleEnemyDefeated;
            CheeseEnemyAgent agent = enemy.GetComponent<CheeseEnemyAgent>();
            if (agent != null)
                attackCoordinator?.UnregisterAgent(agent);
        }
        aliveEnemies.Remove(enemy);
        if (enemy != null)
            enemyStates.Remove(enemy);
    }

    void DespawnEnemy(SpellTarget enemy)
    {
        UnregisterEnemy(enemy);
        if (enemy != null)
            Destroy(enemy.gameObject);
    }

    void ClearAliveEnemies(bool resetPillar = true)
    {
        for (int i = aliveEnemies.Count - 1; i >= 0; i--)
            DespawnEnemy(aliveEnemies[i]);
        aliveEnemies.Clear();
        enemyStates.Clear();
        if (resetPillar)
            ResetActivePillar();
    }

    public void CompleteCurrentLevelFromObjective(int expectedWaves)
    {
        if (levelAdvanceRoutine == null)
            levelAdvanceRoutine = StartCoroutine(ObjectiveLevelCompleteRoutine());
    }

    IEnumerator ObjectiveLevelCompleteRoutine()
    {
        ClearAliveEnemies(false);
        NotifyEncounterEnded(EncounterOutcome.Completed);
        ClearEncounterLock();
        wordActionHandler?.ClearLoadedSpell();
        OnLevelCompleted?.Invoke(CurrentLevel);
        playerLearningProfile?.MarkLevelCompleted(CurrentLevel, GetMaxAvailableLevel());
        yield return new WaitForSeconds(levelAdvanceDelaySeconds);
    }

    void HandlePlayerDied()
    {
        if (arenaResetRoutine != null || levelAdvanceRoutine != null)
            return;

        playerLearningProfile?.RecordWaveOutcome(
            activeEncounter != null ? activeEncounter.targetSpellWord : "",
            encounterStartingHp > 0 ? encounterStartingHp : playerHealth != null ? playerHealth.maxHp : 0,
            true,
            encounterStartedAt > 0f ? Time.unscaledTime - encounterStartedAt : 0f);

        if (useObjectiveEscapeMode)
        {
            objectiveDirector?.ClearStageObjects();
            wordActionHandler?.ClearLoadedSpell();
            playerController?.ClearDrawSession();
            ClearAliveEnemies(false);
            NotifyEncounterEnded(EncounterOutcome.Failed);
            ClearEncounterLock();
            arenaResetRoutine = StartCoroutine(RetryArenaRoutine());
            return;
        }

        arenaResetRoutine = StartCoroutine(RetryArenaRoutine());
    }

    IEnumerator RetryArenaRoutine()
    {
        isResettingArena = true;
        objectiveDirector?.ClearStageObjects();
        ClearAliveEnemies(false);
        NotifyEncounterEnded(EncounterOutcome.Failed);
        ClearEncounterLock();
        yield return new WaitForSeconds(retryDelaySeconds);
        playerController?.ResetToSpawnPoint();
        playerHealth?.RestoreFull();
        objectiveDirector?.BeginStage();
        phase = EncounterPhase.Exploration;
        ScheduleNextPressure(ResolveOpeningGraceSeconds());
        isResettingArena = false;
        arenaResetRoutine = null;
    }
}
