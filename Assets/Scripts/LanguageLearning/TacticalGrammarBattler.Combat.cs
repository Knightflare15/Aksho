using System;
using System.Collections.Generic;
using UnityEngine;


public sealed partial class TacticalGrammarBattler
{
    public bool CanPlayerAttackTarget(TacticalBattleActionProfile profile, out string reason)
    {
        reason = "";
        if (profile == null || !profile.AttackAction)
        {
            reason = "That verb is not an attack.";
            return false;
        }

        if (State.playerUnit == null || State.enemyUnit == null)
        {
            reason = "There is no target on the tactical grid.";
            return false;
        }

        TacticalBattlePosition from = State.playerUnit.position;
        TacticalBattlePosition to = State.enemyUnit.position;
        int distance = HexDistance(from, to);
        int range = Mathf.Clamp(profile.rangeCells, MinAttackRangeCells, MaxAttackRangeCells);
        if (distance > range)
        {
            reason = $"Target is {distance} cells away; {profile.verb} reaches {range}.";
            return false;
        }

        if (!IsInFacingArc(from, to, State.playerFacing))
        {
            reason = "Target is outside the current attack arc.";
            return false;
        }

        if (!HasClearLine(from, to, out TacticalBattleCellType blocker, out TacticalBattlePosition blockedAt))
        {
            reason = $"{blocker} blocks the direct path at ({blockedAt.x}, {blockedAt.y}).";
            return false;
        }

        return true;
    }

    public int ResolvePlayerAttackDamage(TacticalBattleActionProfile profile)
    {
        if (profile == null || !profile.AttackAction || State.playerUnit == null)
            return 0;

        float varietyEffectiveness = Mathf.Clamp(profile.varietyEffectiveness, 0.1f, 1f);
        return Mathf.Max(1, Mathf.RoundToInt(
            (State.playerUnit.stats.attack + profile.power) *
            profile.damageMultiplier *
            varietyEffectiveness));
    }

    public bool CanEnemyAttackPlayer()
    {
        return State.enemyUnit != null &&
               State.playerUnit != null &&
               HexDistance(State.enemyUnit.position, State.playerUnit.position) == EnemyAttackRangeCells &&
               IsInFacingArc(State.enemyUnit.position, State.playerUnit.position, State.enemyFacing);
    }

    public bool TryAdvanceEnemyTowardPlayer(int movementCells, out int cellsMoved)
    {
        cellsMoved = 0;
        if (State.enemyUnit == null || State.playerUnit == null)
            return false;

        if (!TryBuildUnitPathToRange(State.enemyUnit, State.playerUnit.position, EnemyAttackRangeCells, out List<TacticalBattlePosition> path) || path.Count == 0)
            return false;

        cellsMoved = Mathf.Min(Mathf.Max(0, movementCells), path.Count);
        if (cellsMoved <= 0)
            return false;
        State.enemyUnit.position = path[cellsMoved - 1];
        FaceEnemyTowardPlayer();
        return true;
    }

    bool TryBuildUnitPathToRange(
        TacticalBattleUnit movingUnit,
        TacticalBattlePosition target,
        int targetRange,
        out List<TacticalBattlePosition> path)
    {
        path = new List<TacticalBattlePosition>();
        TacticalBattlePosition start = movingUnit.position;
        if (HexDistance(start, target) <= Mathf.Max(0, targetRange))
            return true;

        var frontier = new Queue<TacticalBattlePosition>();
        var cameFrom = new Dictionary<string, TacticalBattlePosition>();
        var visited = new HashSet<string> { Key(start) };
        frontier.Enqueue(start);
        while (frontier.Count > 0)
        {
            TacticalBattlePosition current = frontier.Dequeue();
            foreach (TacticalBattlePosition next in GetNeighbors(current))
            {
                if (!IsPassableForUnit(next, movingUnit) || !visited.Add(Key(next)))
                    continue;

                cameFrom[Key(next)] = current;
                if (HexDistance(next, target) <= Mathf.Max(0, targetRange))
                {
                    TacticalBattlePosition cursor = next;
                    path.Add(cursor);
                    while (!PositionsEqual(cameFrom[Key(cursor)], start))
                    {
                        cursor = cameFrom[Key(cursor)];
                        path.Add(cursor);
                    }
                    path.Reverse();
                    return true;
                }
                frontier.Enqueue(next);
            }
        }

        path.Clear();
        return false;
    }

    public bool TryRetreatEnemyFromPlayer(out int cellsMoved)
    {
        cellsMoved = 0;
        if (State.enemyUnit == null || State.playerUnit == null)
            return false;

        TacticalBattlePosition current = State.enemyUnit.position;
        for (int step = 0; step < EnemyRetreatCells; step++)
        {
            TacticalBattlePosition best = current;
            int bestDistance = HexDistance(current, State.playerUnit.position);
            foreach (TacticalBattlePosition next in GetNeighbors(current))
            {
                if (!IsPassableForUnit(next, State.enemyUnit))
                    continue;

                int distance = HexDistance(next, State.playerUnit.position);
                if (distance > bestDistance)
                {
                    best = next;
                    bestDistance = distance;
                }
            }

            if (PositionsEqual(best, current))
                break;

            current = best;
            cellsMoved++;
        }

        State.enemyUnit.position = current;
        FaceEnemyTowardPlayer();
        return cellsMoved > 0;
    }

    public bool SetPlayerFacing(TacticalBattlePosition direction)
    {
        TacticalBattlePosition normalized = NormalizeDirection(direction);
        if (normalized.x == 0 && normalized.y == 0)
            return false;

        State.playerFacing = normalized;
        return true;
    }

    public bool FacePlayerToward(TacticalBattlePosition target)
    {
        if (State.playerUnit == null)
            return false;

        return SetPlayerFacing(DirectionTo(State.playerUnit.position, target));
    }

    public bool TrySelectTacticalCell(TacticalBattlePosition target, out string error)
    {
        error = "";
        if (State.playerUnit == null)
        {
            error = "Summon a unit before selecting a tactical cell.";
            return false;
        }

        if (!State.IsInside(target))
        {
            error = "That tactical cell is outside the grid.";
            return false;
        }

        if (PositionsEqual(target, State.playerUnit.position))
        {
            error = "Choose a cell away from the player to set a facing direction.";
            return false;
        }

        hasSelectedTacticalCell = true;
        selectedTacticalCell = target;
        FacePlayerToward(target);
        return true;
    }

    public void ClearSelectedTacticalCell()
    {
        hasSelectedTacticalCell = false;
        selectedTacticalCell = default;
    }

    public List<TacticalBattlePosition> GetPlayerAttackArcCells(int rangeCells)
    {
        if (State.playerUnit == null)
            return new List<TacticalBattlePosition>();

        return GetAttackArcCells(State.playerUnit.position, State.playerFacing, rangeCells);
    }

    public List<TacticalBattlePosition> GetEnemyAttackArcCells(int rangeCells)
    {
        if (State.enemyUnit == null)
            return new List<TacticalBattlePosition>();

        return GetAttackArcCells(State.enemyUnit.position, State.enemyFacing, rangeCells);
    }

    List<TacticalBattlePosition> GetAttackArcCells(TacticalBattlePosition origin, TacticalBattlePosition facing, int rangeCells)
    {
        var result = new List<TacticalBattlePosition>();
        int range = Mathf.Clamp(rangeCells, MinAttackRangeCells, MaxAttackRangeCells);
        for (int x = 0; x < State.width; x++)
        {
            for (int y = 0; y < State.height; y++)
            {
                var position = new TacticalBattlePosition(x, y);
                if (PositionsEqual(position, origin))
                    continue;
                if (HexDistance(origin, position) > range)
                    continue;
                if (!IsInFacingArc(origin, position, facing))
                    continue;
                if (!HasClearLine(origin, position, out _, out _))
                    continue;
                result.Add(position);
            }
        }

        return result;
    }

    public void FaceEnemyTowardPlayer()
    {
        if (State.enemyUnit != null && State.playerUnit != null)
            State.enemyFacing = DirectionTo(State.enemyUnit.position, State.playerUnit.position);
    }
}
