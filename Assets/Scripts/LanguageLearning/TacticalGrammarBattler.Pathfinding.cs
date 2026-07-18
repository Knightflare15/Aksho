using System;
using System.Collections.Generic;
using UnityEngine;


public sealed partial class TacticalGrammarBattler
{
    bool TryMoveLinear(
        TacticalBattleActionProfile profile,
        HexCube direction,
        out TacticalBattlePosition destination,
        out List<TacticalBattlePosition> path,
        out string error)
    {
        destination = State.playerUnit.position;
        path = new List<TacticalBattlePosition>();
        error = $"No safe cell lies {(string.IsNullOrWhiteSpace(profile.direction) ? "forward" : profile.direction.ToLowerInvariant())} within {profile.verb}'s movement range.";
        HexCube current = ToHexCube(State.playerUnit.position);
        bool foundLanding = false;
        bool aerial = CanFlyOverTerrain(profile.verb);
        bool leap = CreaturePhraseUtility.NormalizeToken(profile.verb) == "JUMP";

        for (int index = 0; index < Mathf.Max(0, profile.movementCells); index++)
        {
            current += direction;
            TacticalBattlePosition next = FromHexCube(current);
            if (!State.IsInside(next) || State.IsOccupied(next))
                break;

            if (State.IsBlocked(next) || State.IsHazard(next))
            {
                path.Add(next);
                if (aerial)
                    continue;

                if (leap && index + 1 < profile.movementCells)
                {
                    current += direction;
                    TacticalBattlePosition landing = FromHexCube(current);
                    if (!State.IsInside(landing) || State.IsBlocked(landing) || State.IsHazard(landing) || State.IsOccupied(landing))
                        break;
                    path.Add(landing);
                    destination = landing;
                    foundLanding = true;
                    index++;
                    continue;
                }

                break;
            }

            path.Add(next);
            destination = next;
            foundLanding = true;
        }

        if (!foundLanding)
        {
            path.Clear();
            return false;
        }

        error = "";
        return true;
    }

    public List<TacticalBattlePosition> GetPlayerMovementPreviewCells(int movementCells, string verb = "WALK", string relativeDirection = "FORWARD")
    {
        if (State.playerUnit == null)
            return new List<TacticalBattlePosition>();

        var profile = new TacticalBattleActionProfile
        {
            verb = CreaturePhraseUtility.NormalizeToken(verb),
            direction = CreaturePhraseUtility.NormalizeToken(relativeDirection),
            movementCells = Mathf.Clamp(movementCells, 0, MaxMovementCells),
            movementAction = true,
            category = CreatureVerbCategory.Movement,
        };
        return TryMoveInFacingDirection(profile, relativeDirection, out _, out List<TacticalBattlePosition> path, out _)
            ? path
            : new List<TacticalBattlePosition>();
    }

    bool TryMoveAlongSafePath(int movementCells, out TacticalBattlePosition destination, out string error)
    {
        destination = State.playerUnit.position;
        error = "No safe path was found.";

        TacticalBattlePosition enemyPosition = State.enemyUnit != null
            ? State.enemyUnit.position
            : new TacticalBattlePosition(State.width - 1, State.playerUnit.position.y);
        TacticalBattlePosition current = State.playerUnit.position;
        for (int step = 0; step < Mathf.Max(0, movementCells); step++)
        {
            TacticalBattlePosition best = current;
            int bestDistance = HexDistance(best, enemyPosition);
            foreach (TacticalBattlePosition next in GetNeighbors(current))
            {
                if (!IsPassableForUnit(next, State.playerUnit))
                    continue;

                int distance = HexDistance(next, enemyPosition);
                if (distance < bestDistance)
                {
                    best = next;
                    bestDistance = distance;
                }
            }

            if (PositionsEqual(best, current))
                break;
            current = best;
            if (bestDistance <= EnemyAttackRangeCells)
                break;
        }

        if (PositionsEqual(current, State.playerUnit.position))
            return false;

        destination = current;
        error = "";
        return true;
    }

    bool TryMoveAwayFromEnemy(int movementCells, out TacticalBattlePosition destination, out string error)
    {
        destination = State.playerUnit.position;
        error = "No dodge space was found.";
        if (State.enemyUnit == null)
            return TryMoveAlongSafePath(movementCells, out destination, out error);

        TacticalBattlePosition current = State.playerUnit.position;
        for (int step = 0; step < Mathf.Max(0, movementCells); step++)
        {
            TacticalBattlePosition best = current;
            int bestDistance = HexDistance(current, State.enemyUnit.position);
            foreach (TacticalBattlePosition next in GetNeighbors(current))
            {
                if (!IsPassableForUnit(next, State.playerUnit))
                    continue;

                int distance = HexDistance(next, State.enemyUnit.position);
                if (distance > bestDistance)
                {
                    best = next;
                    bestDistance = distance;
                }
            }

            if (PositionsEqual(best, current))
                break;
            current = best;
        }

        if (PositionsEqual(current, State.playerUnit.position))
            return false;

        destination = current;
        error = "";
        return true;
    }

    bool TryMoveToSelectedTacticalCell(TacticalBattleActionProfile profile, out TacticalBattlePosition destination, out string error)
    {
        destination = State.playerUnit.position;
        error = "";
        if (!hasSelectedTacticalCell)
            return false;

        if (PositionsEqual(selectedTacticalCell, State.playerUnit.position))
        {
            error = "Selected cell is already occupied by the player.";
            return false;
        }

        if (!State.IsInside(selectedTacticalCell))
        {
            error = "Selected cell is outside the grid.";
            ClearSelectedTacticalCell();
            return false;
        }

        if (State.IsBlocked(selectedTacticalCell) || State.IsHazard(selectedTacticalCell) || State.IsOccupied(selectedTacticalCell))
        {
            error = $"Selected cell ({selectedTacticalCell.x}, {selectedTacticalCell.y}) is not a safe movement destination.";
            return false;
        }

        if (!CanReachWithinBudget(State.playerUnit.position, selectedTacticalCell, profile.movementCells, out int requiredCells))
        {
            error = requiredCells >= 0
                ? $"{profile.verb} moves {profile.movementCells} cell(s), but selected cell needs {requiredCells}."
                : $"{profile.verb} cannot reach the selected cell safely.";
            return false;
        }

        destination = selectedTacticalCell;
        return true;
    }

    bool CanReachWithinBudget(TacticalBattlePosition start, TacticalBattlePosition destination, int movementCells, out int requiredCells)
    {
        requiredCells = -1;
        if (!TryBuildSafePath(start, destination, out List<TacticalBattlePosition> path))
            return false;
        requiredCells = path.Count;
        return requiredCells <= Mathf.Max(0, movementCells);
    }

    bool TryBuildSafePath(TacticalBattlePosition start, TacticalBattlePosition destination, out List<TacticalBattlePosition> path)
    {
        path = new List<TacticalBattlePosition>();
        if (PositionsEqual(start, destination))
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
                if (!State.IsInside(next) || State.IsBlocked(next) || State.IsHazard(next))
                    continue;
                if (State.IsOccupied(next) && !PositionsEqual(next, destination))
                    continue;
                if (!visited.Add(Key(next)))
                    continue;

                cameFrom[Key(next)] = current;
                if (PositionsEqual(next, destination))
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

    bool IsPassableForUnit(TacticalBattlePosition position, TacticalBattleUnit movingUnit)
    {
        if (!State.IsInside(position) || State.IsBlocked(position) || State.IsHazard(position))
            return false;
        if (State.playerUnit != null && State.playerUnit != movingUnit && PositionsEqual(State.playerUnit.position, position))
            return false;
        if (State.enemyUnit != null && State.enemyUnit != movingUnit && PositionsEqual(State.enemyUnit.position, position))
            return false;
        return true;
    }

    void SpendPlayerPp(int amount)
    {
        State.playerUnit.currentPp = Mathf.Max(0, State.playerUnit.currentPp - Mathf.Max(0, amount));
    }

    static bool PositionsEqual(TacticalBattlePosition a, TacticalBattlePosition b)
    {
        return a.x == b.x && a.y == b.y;
    }
}
