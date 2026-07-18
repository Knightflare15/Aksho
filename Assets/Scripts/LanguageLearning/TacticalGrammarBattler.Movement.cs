using System;
using System.Collections.Generic;
using UnityEngine;


public sealed partial class TacticalGrammarBattler
{
    bool TryResolveMovement(ParsedActionPhrase action, TacticalBattleActionProfile profile, out TacticalBattlePosition destination, out string error)
    {
        destination = State.playerUnit.position;
        error = "That movement is not valid on this grid.";

        switch (action.preposition)
        {
            case "BESIDE":
                return TryMoveBeside(action.objectToken, profile.movementCells, out destination, out error);
            case "AROUND":
                return TryMoveAround(action.objectToken, profile.movementCells, out destination, out error);
            case "OVER":
                return TryMoveOver(action.objectToken, profile, action.direction, out destination, out error);
            case "BEHIND":
                return TryMoveBehind(action.objectToken, out destination, out error);
            case "UNDER":
                return TryMoveUnder(action.objectToken, out destination, out error);
            case "NEAR":
                return TryMoveNear(action.objectToken, profile.movementCells, out destination, out error);
            case "TOWARD":
                return TryMoveTowardOrAway(action.objectToken, profile, moveAway: false, out destination, out error);
            case "AWAY":
                return TryMoveTowardOrAway(action.objectToken, profile, moveAway: true, out destination, out error);
            case "ACROSS":
                return TryMoveAcross(action.objectToken, out destination, out error);
            case "INTO":
                return TryMoveInto(action.objectToken, out destination, out error);
            case "THROUGH":
                return TryMoveThrough(action.objectToken, out destination, out error);
            default:
                error = $"The preposition '{action.preposition}' is not implemented yet.";
                return false;
        }
    }

    bool TryMoveBeside(string objectToken, int movementCells, out TacticalBattlePosition destination, out string error)
    {
        destination = State.playerUnit.position;
        error = "There is no open adjacent cell beside that target.";

        TacticalBattlePosition target = ResolvePrepositionTarget(objectToken);
        if (!State.IsInside(target))
            return false;

        var candidates = new List<TacticalBattlePosition>();
        foreach (TacticalBattlePosition next in GetNeighbors(target))
        {
            if (HexDistance(next, target) == 1 && IsPassableForPreposition(next, allowHazard: false))
                candidates.Add(next);
        }
        if (!TryChooseReachableLandmarkDestination(candidates, movementCells, out destination))
            return false;
        error = "";
        return true;
    }

    bool TryMoveAround(string objectToken, int movementCells, out TacticalBattlePosition destination, out string error)
    {
        destination = State.playerUnit.position;
        error = "No safe path around that obstacle was found.";
        TacticalBattlePosition obstacle = FindTerrain(objectToken);
        if (!State.IsInside(obstacle))
            return false;

        var candidates = new List<TacticalBattlePosition>();
        foreach (TacticalBattlePosition next in GetNeighbors(obstacle))
        {
            if (IsPassableForPreposition(next, allowHazard: false))
                candidates.Add(next);
        }
        if (!TryChooseReachableLandmarkDestination(candidates, movementCells, out destination))
            return false;
        error = "";
        return true;
    }

    bool TryMoveOver(string objectToken, TacticalBattleActionProfile profile, string relativeDirection, out TacticalBattlePosition destination, out string error)
    {
        destination = State.playerUnit.position;
        error = "There is no valid obstacle or hazard to move over.";

        if (profile == null || profile.movementCells < 2 || !CanBypassObstacle(profile.verb))
        {
            error = $"{profile?.verb ?? "That verb"} cannot clear an obstacle. Use JUMP, FLY, or GLIDE with at least 2 movement.";
            return false;
        }

        TacticalBattlePosition terrainTarget = FindTerrain(objectToken);
        HexCube direction = State.IsInside(terrainTarget)
            ? HexDirectionFromTo(State.playerUnit.position, terrainTarget)
            : ResolveRelativeCubeDirection(State.playerUnit.position, State.playerFacing, relativeDirection);
        TacticalBattlePosition middle = FromHexCube(ToHexCube(State.playerUnit.position) + direction);
        TacticalBattlePosition landing = FromHexCube(ToHexCube(State.playerUnit.position) + ScaleHex(direction, 2));
        if (!State.IsInside(middle) || !State.IsInside(landing))
            return false;

        string middleToken = NormalizeTerrainToken(State.GetCell(middle));
        if (!string.IsNullOrWhiteSpace(objectToken) && middleToken != objectToken)
            return false;

        if (!(State.IsBlocked(middle) || State.IsHazard(middle)))
        {
            error = "OVER needs an obstacle or hazard in the first cell.";
            return false;
        }
        if (State.IsBlocked(landing) || State.IsHazard(landing) || State.IsOccupied(landing))
        {
            error = "The landing cell beyond the obstacle is not safe.";
            return false;
        }

        destination = landing;
        error = "";
        return true;
    }

    bool TryMoveBehind(string objectToken, out TacticalBattlePosition destination, out string error)
    {
        destination = State.playerUnit.position;
        error = "There is no valid cover position behind that object.";
        if (State.enemyUnit == null)
            return false;

        TacticalBattlePosition objectPosition = FindTerrain(objectToken);
        if (!State.IsInside(objectPosition))
            return false;

        HexCube awayFromEnemy = HexDirectionFromTo(State.enemyUnit.position, objectPosition);
        TacticalBattlePosition candidate = FromHexCube(ToHexCube(objectPosition) + awayFromEnemy);
        if (!State.IsInside(candidate) || State.IsBlocked(candidate) || State.IsHazard(candidate) || State.IsOccupied(candidate))
            return false;

        destination = candidate;
        error = "";
        return true;
    }

    bool TryMoveUnder(string objectToken, out TacticalBattlePosition destination, out string error)
    {
        destination = State.playerUnit.position;
        error = "Under only works for authored cover or obstacle cases.";
        if (!IsUnderCoverToken(objectToken))
            return false;

        TacticalBattlePosition coverPosition = FindTerrain(objectToken);
        if (!State.IsInside(coverPosition))
            return false;

        if (IsPassableForPreposition(coverPosition, allowHazard: false))
        {
            destination = coverPosition;
            error = "";
            return true;
        }

        foreach (TacticalBattlePosition next in GetNeighbors(coverPosition))
        {
            if (IsPassableForPreposition(next, allowHazard: false))
            {
                destination = next;
                error = "";
                return true;
            }
        }

        return false;
    }

    bool TryMoveNear(string objectToken, int movementCells, out TacticalBattlePosition destination, out string error)
    {
        destination = State.playerUnit.position;
        error = "There is no open cell near that target.";

        TacticalBattlePosition target = ResolvePrepositionTarget(objectToken);
        if (!State.IsInside(target))
            return false;

        var candidates = new List<TacticalBattlePosition>();
        for (int x = 0; x < State.width; x++)
        {
            for (int y = 0; y < State.height; y++)
            {
                var candidate = new TacticalBattlePosition(x, y);
                int targetDistance = HexDistance(candidate, target);
                if (targetDistance < 1 || targetDistance > 2)
                    continue;
                if (!IsPassableForPreposition(candidate, allowHazard: false))
                    continue;

                candidates.Add(candidate);
            }
        }

        if (!TryChooseReachableLandmarkDestination(candidates, movementCells, out destination))
            return false;
        error = "";
        return true;
    }

    bool TryChooseReachableLandmarkDestination(
        List<TacticalBattlePosition> candidates,
        int movementCells,
        out TacticalBattlePosition destination)
    {
        destination = State.playerUnit.position;
        bool found = false;
        int bestAlignment = int.MinValue;
        int bestPathLength = int.MaxValue;
        HexCube origin = ToHexCube(State.playerUnit.position);
        HexCube facing = OffsetDeltaToCubeDirection(State.playerUnit.position, State.playerFacing);

        foreach (TacticalBattlePosition candidate in candidates)
        {
            if (!TryBuildSafePath(State.playerUnit.position, candidate, out List<TacticalBattlePosition> path) ||
                path.Count > Mathf.Max(0, movementCells))
                continue;

            HexCube target = ToHexCube(candidate);
            int dx = target.x - origin.x;
            int dy = target.y - origin.y;
            int dz = target.z - origin.z;
            int alignment = dx * facing.x + dy * facing.y + dz * facing.z;
            bool better = !found ||
                          alignment > bestAlignment ||
                          (alignment == bestAlignment && path.Count < bestPathLength) ||
                          (alignment == bestAlignment && path.Count == bestPathLength &&
                           (candidate.y < destination.y || (candidate.y == destination.y && candidate.x < destination.x)));
            if (!better)
                continue;

            found = true;
            bestAlignment = alignment;
            bestPathLength = path.Count;
            destination = candidate;
        }

        return found;
    }

    bool TryMoveAcross(string objectToken, out TacticalBattlePosition destination, out string error)
    {
        destination = State.playerUnit.position;
        error = "There is no clear route across that terrain.";

        TacticalBattlePosition bridge = FindTerrain(string.IsNullOrWhiteSpace(objectToken) ? "BRIDGE" : objectToken);
        if (State.IsInside(bridge) && IsPassableForPreposition(bridge, allowHazard: false))
        {
            destination = bridge;
            error = "";
            return true;
        }

        return TryMoveAlongSafePath(1, out destination, out error);
    }

    bool TryMoveInto(string objectToken, out TacticalBattlePosition destination, out string error)
    {
        destination = State.playerUnit.position;
        error = "There is no safe space to move into.";

        TacticalBattlePosition target = FindTerrain(objectToken);
        if (!State.IsInside(target))
            return false;

        if (IsPassableForPreposition(target, allowHazard: objectToken == "WATER"))
        {
            destination = target;
            error = "";
            return true;
        }

        foreach (TacticalBattlePosition next in GetNeighbors(target))
        {
            if (IsPassableForPreposition(next, allowHazard: false))
            {
                destination = next;
                error = "";
                return true;
            }
        }

        return false;
    }

    bool TryMoveThrough(string objectToken, out TacticalBattlePosition destination, out string error)
    {
        destination = State.playerUnit.position;
        error = "There is no passable route through that space.";

        TacticalBattlePosition target = FindTerrain(objectToken);
        if (State.IsInside(target) && IsPassableForPreposition(target, allowHazard: false))
        {
            destination = target;
            error = "";
            return true;
        }

        return TryMoveAlongSafePath(1, out destination, out error);
    }

    bool TryMoveTowardOrAway(
        string objectToken,
        TacticalBattleActionProfile profile,
        bool moveAway,
        out TacticalBattlePosition destination,
        out string error)
    {
        destination = State.playerUnit.position;
        TacticalBattlePosition target = ResolvePrepositionTarget(objectToken);
        if (!State.IsInside(target))
        {
            error = $"There is no {objectToken.ToLowerInvariant()} to move {(moveAway ? "away from" : "toward")}.";
            return false;
        }

        HexCube direction = HexDirectionFromTo(State.playerUnit.position, target);
        if (moveAway)
            direction = ScaleHex(direction, -1);
        return TryMoveLinear(profile, direction, out destination, out _, out error);
    }

    bool TryTurnRelative(string relativeDirection, out string error)
    {
        error = "";
        if (State.playerUnit == null)
        {
            error = "Summon a unit before turning.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(relativeDirection))
        {
            error = "Say turn left, turn right, turn forward, or turn backward.";
            return false;
        }

        HexCube direction = ResolveRelativeCubeDirection(State.playerUnit.position, State.playerFacing, relativeDirection);
        TacticalBattlePosition adjacent = FromHexCube(ToHexCube(State.playerUnit.position) + direction);
        State.playerFacing = new TacticalBattlePosition(
            adjacent.x - State.playerUnit.position.x,
            adjacent.y - State.playerUnit.position.y);
        return true;
    }

    bool TryMoveInFacingDirection(
        TacticalBattleActionProfile profile,
        string relativeDirection,
        out TacticalBattlePosition destination,
        out List<TacticalBattlePosition> path,
        out string error)
    {
        HexCube direction = ResolveRelativeCubeDirection(
            State.playerUnit.position,
            State.playerFacing,
            string.IsNullOrWhiteSpace(relativeDirection) ? "FORWARD" : relativeDirection);
        return TryMoveLinear(profile, direction, out destination, out path, out error);
    }
}
