using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed partial class TacticalGrammarBattleController : MonoBehaviour
{
    void PopulateTerrain(WaveDescriptor descriptor)
    {
        int variant = descriptor != null ? Mathf.Abs(descriptor.waveIndex) % 3 : 0;
        switch (variant)
        {
            case 0:
                SetTerrain(1, 2, TacticalBattleCellType.Rock);
                SetTerrain(2, 3, TacticalBattleCellType.Wall);
                SetTerrain(3, 2, TacticalBattleCellType.Spikes);
                SetTerrain(3, 3, TacticalBattleCellType.Water);
                break;
            case 1:
                SetTerrain(1, 2, TacticalBattleCellType.Rock);
                SetTerrain(1, 1, TacticalBattleCellType.Box);
                SetTerrain(3, 2, TacticalBattleCellType.Spikes);
                SetTerrain(1, 3, TacticalBattleCellType.Tree);
                break;
            default:
                SetTerrain(1, 2, TacticalBattleCellType.Rock);
                SetTerrain(2, 2, TacticalBattleCellType.Bridge);
                SetTerrain(3, 1, TacticalBattleCellType.Wall);
                SetTerrain(3, 3, TacticalBattleCellType.Roof);
                break;
        }
    }

    void SetTerrain(int x, int y, TacticalBattleCellType cellType)
    {
        battler?.SetTerrain(ClampPosition(new TacticalBattlePosition(x, y)), cellType);
    }

    TacticalEnemyTurnResult ResolveEnemyResponse(bool playerDamagedEnemy, TacticalBattleActionProfile playerAction)
    {
        var result = new TacticalEnemyTurnResult();
        if (!IsActive || State?.enemyUnit == null)
            return result;

        TacticalBattleUnit enemy = State.enemyUnit;
        if (enemy.currentHp <= 0)
        {
            pendingEnemyAttack = default;
            result.message = $"Enemy {enemy.noun} is defeated.";
            return result;
        }

        if (playerDamagedEnemy)
        {
            pendingEnemyAttack = default;
            ScheduleNextEnemyDecision();
            if (battler.TryRetreatEnemyFromPlayer(out int cellsMoved))
                result.message = $"Enemy {enemy.noun} recoiled {cellsMoved} cell(s) to recover.";
            else
                result.message = $"Enemy {enemy.noun} reels but has nowhere safe to recover.";
            return result;
        }

        if (TryResolvePendingDodge(playerAction, out string dodgeMessage))
        {
            result.message = dodgeMessage;
            return result;
        }

        if (pendingEnemyAttack.active)
        {
            result.message = $"Enemy {enemy.noun}'s attack is still incoming.";
            return result;
        }

        if (enableRealtimeEnemyAI)
            return result;

        return ResolveEnemyTurn();
    }

    bool TryResolvePendingDodge(TacticalBattleActionProfile profile, out string message)
    {
        message = "";
        if (!pendingEnemyAttack.active || profile == null || !profile.movementAction)
            return false;

        if (Time.time > pendingEnemyAttack.hitsAt)
            return false;

        if (profile.actionSpeed <= pendingEnemyAttack.attackSpeed)
        {
            message = $"{profile.verb} was too slow. {ResolvePendingEnemyAttack(requireArc: false)}";
            return true;
        }

        if (battler.CanEnemyAttackPlayer())
        {
            message = $"{profile.verb} moved, but not out of the attack arc.";
            return true;
        }

        pendingEnemyAttack = default;
        ScheduleNextEnemyDecision();
        message = $"{profile.verb} beat the attack speed and dodged out of the arc.";
        return true;
    }

    TacticalEnemyTurnResult ResolveEnemyTurn()
    {
        var result = new TacticalEnemyTurnResult();
        if (!IsActive || State?.enemyUnit == null || State.playerUnit == null)
            return result;

        TacticalBattleUnit enemy = State.enemyUnit;
        TacticalBattleUnit player = State.playerUnit;
        if (enemy.currentHp <= 0)
        {
            result.message = $"Enemy {enemy.noun} is defeated.";
            return result;
        }

        battler.FaceEnemyTowardPlayer();
        if (battler.CanEnemyAttackPlayer())
        {
            StartPendingEnemyAttack(enemy);
            result.message = $"Enemy {enemy.noun} is winding up an attack: {pendingEnemyAttack.damage} damage, speed {pendingEnemyAttack.attackSpeed:0.#}, {pendingEnemyAttack.WindowSeconds:0.#}s to dodge or shield.";
            return result;
        }

        if (battler.TryAdvanceEnemyTowardPlayer(1, out int cellsMoved))
        {
            result.message = $"Enemy {enemy.noun} advanced {cellsMoved} cell(s) to {FormatPosition(enemy.position)}.";
            return result;
        }

        result.message = $"Enemy {enemy.noun} waits behind the hazards.";
        return result;
    }

    void UpdateRealtimeEnemyMind()
    {
        if (!enableRealtimeEnemyAI || !IsActive || State?.playerUnit == null || State.enemyUnit == null)
            return;
        if (State.playerUnit.currentHp <= 0 || State.enemyUnit.currentHp <= 0 || pendingEnemyAttack.active)
            return;
        if (Time.time < nextEnemyDecisionAt)
            return;

        enemyDecisionCount++;
        string message;
        int curseCadence = Mathf.Max(2, enemyDecisionsPerCurse);
        if (activeTacticalCurse == GrammarBattleCurse.None && enemyDecisionCount % curseCadence == 0)
        {
            GrammarBattleCurse curse = ResolveEnemyCurse(enemyDecisionCount / curseCadence);
            ApplyTacticalCurse(curse);
            message = IsPronounCurse(curse)
                ? $"Enemy {State.enemyUnit.noun} inflicted {FormatCurse(curse)}. It governs every command for 30 seconds."
                : $"Enemy {State.enemyUnit.noun} inflicted {FormatCurse(curse)}. It governs every command for the rest of this battle.";
        }
        else
        {
            message = ResolveEnemyTurn().message;
        }

        ScheduleNextEnemyDecision();
        if (!string.IsNullOrWhiteSpace(message))
            Publish(message);
        RefreshBoardVisuals();
    }

    void ScheduleNextEnemyDecision(float extraDelay = 0f)
    {
        if (!enableRealtimeEnemyAI || State?.enemyUnit == null)
        {
            nextEnemyDecisionAt = float.PositiveInfinity;
            return;
        }

        nextEnemyDecisionAt = Time.time + ResolveEnemyDecisionIntervalSeconds() + Mathf.Max(0f, extraDelay);
    }

    float ResolveEnemyPace01()
    {
        if (!adaptEnemyPaceToLearner || learningProfile == null)
            return Mathf.Clamp01(fallbackEnemyPace);

        float levelPace = Mathf.InverseLerp(1f, 10f, learningProfile.HighestUnlockedLevel);
        PlayerLearningProfile.CombatProgress combat = learningProfile.Combat;
        float experiencePace = combat != null ? Mathf.InverseLerp(0f, 18f, combat.wavesCompleted) : 0f;
        float pace = 0.1f + levelPace * 0.55f + experiencePace * 0.35f;

        if (combat != null)
        {
            if (combat.recentReliefWaves > 0)
                pace *= 0.55f;
            float deathRate = combat.deaths / (float)Mathf.Max(1, combat.wavesCompleted);
            pace -= Mathf.Clamp01(deathRate) * 0.2f;
        }

        return Mathf.Clamp01(pace);
    }

    float ResolveEnemyDecisionIntervalSeconds()
    {
        float baseInterval = Mathf.Lerp(
            Mathf.Max(0.25f, noviceEnemyDecisionIntervalSeconds),
            Mathf.Max(0.25f, enemyDecisionIntervalSeconds),
            ResolveEnemyPace01());
        float enemySpeed = State?.enemyUnit != null ? State.enemyUnit.stats.speed : 6f;
        float speedScale = Mathf.Clamp(6f / Mathf.Max(1f, enemySpeed), 0.7f, 1.35f);
        return Mathf.Clamp(baseInterval * speedScale, 1.8f, 6.5f);
    }

    float ResolveEnemyOpeningGraceSeconds()
    {
        return Mathf.Lerp(
            Mathf.Max(0.25f, noviceEnemyOpeningGraceSeconds),
            Mathf.Max(0.25f, enemyOpeningGraceSeconds),
            ResolveEnemyPace01());
    }

    string BuildEnemyPaceGauge()
    {
        float pace = ResolveEnemyPace01();
        int filled = Mathf.Clamp(Mathf.RoundToInt(pace * 10f), 1, 10);
        string label = pace < 0.38f ? "Gentle" : pace < 0.72f ? "Steady" : "Fast";
        return $"Enemy pace: {label} [{new string('=', filled)}{new string('-', 10 - filled)}]  ~{ResolveEnemyDecisionIntervalSeconds():0.0}s decisions";
    }
}
