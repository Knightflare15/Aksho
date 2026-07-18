using System;
using System.Collections.Generic;
using UnityEngine;


public sealed partial class TacticalGrammarBattler
{
    bool IsPassableForPreposition(TacticalBattlePosition position, bool allowHazard)
    {
        if (!State.IsInside(position) || State.IsBlocked(position) || State.IsOccupied(position))
            return false;
        return allowHazard || !State.IsHazard(position);
    }

    TacticalBattlePosition FindTerrain(string objectToken)
    {
        if (string.IsNullOrWhiteSpace(objectToken))
            return new TacticalBattlePosition(-1, -1);

        for (int x = 0; x < State.width; x++)
        {
            for (int y = 0; y < State.height; y++)
            {
                if (NormalizeTerrainToken(State.terrain[x, y]) == objectToken)
                    return new TacticalBattlePosition(x, y);
            }
        }

        return new TacticalBattlePosition(-1, -1);
    }

    TacticalBattlePosition ResolvePrepositionTarget(string objectToken)
    {
        TacticalBattlePosition terrain = FindTerrain(objectToken);
        if (State.IsInside(terrain))
            return terrain;
        return State.enemyUnit != null ? State.enemyUnit.position : new TacticalBattlePosition(-1, -1);
    }

    static bool IsUnderCoverToken(string objectToken)
    {
        switch (CreaturePhraseUtility.NormalizeToken(objectToken))
        {
            case "ROOF":
            case "WALL":
            case "BOX":
            case "TREE":
                return true;
            default:
                return false;
        }
    }

    IEnumerable<TacticalBattlePosition> GetNeighbors(TacticalBattlePosition position)
    {
        bool oddRow = (position.y & 1) != 0;
        yield return new TacticalBattlePosition(position.x + 1, position.y);
        yield return new TacticalBattlePosition(position.x - 1, position.y);
        yield return new TacticalBattlePosition(position.x + (oddRow ? 1 : 0), position.y + 1);
        yield return new TacticalBattlePosition(position.x + (oddRow ? 0 : -1), position.y + 1);
        yield return new TacticalBattlePosition(position.x + (oddRow ? 1 : 0), position.y - 1);
        yield return new TacticalBattlePosition(position.x + (oddRow ? 0 : -1), position.y - 1);
    }

    bool SubjectsMatch(string subjectNoun)
    {
        string normalized = CreaturePhraseUtility.NormalizeToken(subjectNoun);
        return registry != null
            ? registry.AreInSameNounFamily(normalized, State.playerUnit.noun)
            : normalized == State.playerUnit.noun;
    }

    bool TryParseActionPhrase(string phrase, out ParsedActionPhrase action)
    {
        action = default;
        if (registry == null)
            return false;

        List<string> tokens = CreaturePhraseUtility.Tokenize(phrase);
        if (tokens.Count < 2)
            return false;

        int index = 0;
        if (tokens.Count >= 2 && IsDeterminer(tokens[0]) && registry.TryGetNoun(tokens[1], out NounDefinition determinerNoun))
        {
            action.subjectNoun = determinerNoun.canonicalNoun;
            index = 2;
        }
        else if (registry.TryGetNoun(tokens[0], out NounDefinition subjectNoun))
        {
            action.subjectNoun = subjectNoun.canonicalNoun;
            index = 1;
        }
        else
        {
            return false;
        }

        if (index >= tokens.Count)
            return false;

        string verbToken = tokens[index];
        index++;
        if (!ResolveVerb(verbToken, out VerbActionDefinition verbDefinition))
            return false;

        action.verb = verbDefinition.verb;
        action.verbDefinition = verbDefinition;

        while (index < tokens.Count)
        {
            string token = CreaturePhraseUtility.NormalizeToken(tokens[index]);
            if (action.adverbModifier == null && registry.TryGetModifier(token, ModifierGrammarRole.Adverb, out ModifierDefinition adverb))
            {
                action.adverbModifier = adverb;
                index++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(action.direction) && IsRelativeDirection(token))
            {
                action.direction = CanonicalDirection(token);
                index++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(action.preposition) && IsPreposition(token))
            {
                action.preposition = CanonicalPreposition(token);
                index++;
                if (action.preposition == "AWAY" && index < tokens.Count && tokens[index] == "FROM")
                    index++;
                if (index < tokens.Count && IsDeterminer(tokens[index]))
                    index++;
                if (index < tokens.Count)
                {
                    action.objectToken = CreaturePhraseUtility.NormalizeToken(tokens[index]);
                    index++;
                }
                continue;
            }

            index++;
        }

        return true;
    }

    bool ResolveVerb(string spokenVerb, out VerbActionDefinition verbDefinition)
    {
        foreach (VerbActionDefinition candidate in registry.Verbs)
        {
            if (candidate == null)
                continue;

            if (candidate.Matches(spokenVerb))
            {
                verbDefinition = candidate;
                return true;
            }

            if (MatchesForm(candidate.thirdPersonSingularForms, spokenVerb) ||
                MatchesForm(candidate.pastTenseForms, spokenVerb) ||
                MatchesForm(candidate.progressiveForms, spokenVerb))
            {
                verbDefinition = candidate;
                return true;
            }
        }

        verbDefinition = null;
        return false;
    }

    static bool MatchesForm(List<string> forms, string spokenVerb)
    {
        if (forms == null)
            return false;

        string normalized = CreaturePhraseUtility.NormalizeToken(spokenVerb);
        foreach (string form in forms)
        {
            if (CreaturePhraseUtility.NormalizeToken(form) == normalized)
                return true;
        }
        return false;
    }

    static bool IsDeterminer(string token)
    {
        string normalized = CreaturePhraseUtility.NormalizeToken(token);
        return normalized == "THE" || normalized == "A" || normalized == "AN";
    }

    static bool IsPreposition(string token)
    {
        switch (CreaturePhraseUtility.NormalizeToken(token))
        {
            case "BESIDE":
            case "NEXT":
            case "OVER":
            case "UNDER":
            case "AROUND":
            case "BEHIND":
            case "NEAR":
            case "ACROSS":
            case "INTO":
            case "THROUGH":
            case "TOWARD":
            case "TOWARDS":
            case "AWAY":
                return true;
            default:
                return false;
        }
    }

    static bool IsRelativeDirection(string token)
    {
        switch (CreaturePhraseUtility.NormalizeToken(token))
        {
            case "FORWARD":
            case "FORWARDS":
            case "AHEAD":
            case "STRAIGHT":
            case "BACK":
            case "BACKWARD":
            case "BACKWARDS":
            case "LEFT":
            case "RIGHT":
                return true;
            default:
                return false;
        }
    }

    static string CanonicalDirection(string token)
    {
        string normalized = CreaturePhraseUtility.NormalizeToken(token);
        return normalized switch
        {
            "FORWARDS" => "FORWARD",
            "AHEAD" => "FORWARD",
            "STRAIGHT" => "FORWARD",
            "BACK" => "BACKWARD",
            "BACKWARDS" => "BACKWARD",
            _ => normalized,
        };
    }

    static string CanonicalPreposition(string token)
    {
        string normalized = CreaturePhraseUtility.NormalizeToken(token);
        return normalized switch
        {
            "NEXT" => "BESIDE",
            "TOWARDS" => "TOWARD",
            _ => normalized,
        };
    }

    static string NormalizeTerrainToken(TacticalBattleCellType cellType)
    {
        return cellType switch
        {
            TacticalBattleCellType.Box => "BOX",
            TacticalBattleCellType.Spikes => "SPIKES",
            TacticalBattleCellType.Wall => "WALL",
            TacticalBattleCellType.Roof => "ROOF",
            TacticalBattleCellType.Bridge => "BRIDGE",
            TacticalBattleCellType.Water => "WATER",
            TacticalBattleCellType.Tree => "TREE",
            TacticalBattleCellType.Rock => "ROCK",
            _ => "",
        };
    }

    static int ManhattanDistance(TacticalBattlePosition a, TacticalBattlePosition b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    static int HexDistance(TacticalBattlePosition a, TacticalBattlePosition b)
    {
        HexCube ca = ToHexCube(a);
        HexCube cb = ToHexCube(b);
        return Mathf.Max(Mathf.Abs(ca.x - cb.x), Mathf.Abs(ca.y - cb.y), Mathf.Abs(ca.z - cb.z));
    }

    static bool TryGetHexLineStep(TacticalBattlePosition from, TacticalBattlePosition to, out HexCube step)
    {
        step = new HexCube(0, 0, 0);
        HexCube start = ToHexCube(from);
        HexCube end = ToHexCube(to);
        int dx = end.x - start.x;
        int dy = end.y - start.y;
        int dz = end.z - start.z;
        int distance = HexDistance(from, to);
        if (distance <= 0)
            return false;

        if (!((dx == 0 && dy == -dz) || (dy == 0 && dx == -dz) || (dz == 0 && dx == -dy)))
            return false;

        step = new HexCube(dx / distance, dy / distance, dz / distance);
        return true;
    }

    static bool IsDodgeVerb(string verb)
    {
        string normalized = CreaturePhraseUtility.NormalizeToken(verb);
        return normalized == "DODGE" || normalized == "JUMP" || normalized == "BLINK";
    }

    static TacticalBattlePosition DirectionTo(TacticalBattlePosition from, TacticalBattlePosition to)
    {
        return NormalizeDirection(HexDirectionToOffsetDelta(from, to));
    }

    static TacticalBattlePosition NormalizeDirection(TacticalBattlePosition direction)
    {
        return new TacticalBattlePosition(Math.Sign(direction.x), Math.Sign(direction.y));
    }

    static bool IsInFacingArc(TacticalBattlePosition from, TacticalBattlePosition to, TacticalBattlePosition facing)
    {
        HexCube fromCube = ToHexCube(from);
        HexCube toCube = ToHexCube(to);
        HexCube facingCube = OffsetDeltaToCubeDirection(from, NormalizeDirection(facing));
        int dx = toCube.x - fromCube.x;
        int dy = toCube.y - fromCube.y;
        int dz = toCube.z - fromCube.z;
        if (dx == 0 && dy == 0 && dz == 0)
            return false;

        float dot = dx * facingCube.x + dy * facingCube.y + dz * facingCube.z;
        return dot > 0f;
    }

    bool HasClearLine(TacticalBattlePosition from, TacticalBattlePosition to, out TacticalBattleCellType blocker, out TacticalBattlePosition blockedAt)
    {
        blocker = TacticalBattleCellType.Empty;
        blockedAt = from;
        int distance = HexDistance(from, to);
        if (distance <= 1)
            return true;

        HexCube start = ToHexCube(from);
        HexCube end = ToHexCube(to);
        for (int index = 1; index < distance; index++)
        {
            float amount = index / (float)distance;
            HexCube currentCube = RoundHex(
                Mathf.Lerp(start.x, end.x, amount),
                Mathf.Lerp(start.y, end.y, amount),
                Mathf.Lerp(start.z, end.z, amount));
            TacticalBattlePosition current = FromHexCube(currentCube);
            TacticalBattleCellType cell = State.GetCell(current);
            if (State.IsBlocked(current) || State.IsHazard(current))
            {
                blocker = cell;
                blockedAt = current;
                return false;
            }
        }

        return true;
    }

    static TacticalBattlePosition HexDirectionToOffsetDelta(TacticalBattlePosition from, TacticalBattlePosition to)
    {
        HexCube start = ToHexCube(from);
        HexCube direction = HexDirectionFromTo(from, to);
        TacticalBattlePosition adjacent = FromHexCube(start + direction);
        return new TacticalBattlePosition(adjacent.x - from.x, adjacent.y - from.y);
    }

    static HexCube HexDirectionFromTo(TacticalBattlePosition from, TacticalBattlePosition to)
    {
        HexCube start = ToHexCube(from);
        HexCube end = ToHexCube(to);
        HexCube delta = new HexCube(end.x - start.x, end.y - start.y, end.z - start.z);
        int distance = HexDistance(from, to);
        if (distance <= 0)
            return new HexCube(0, 0, 0);
        return RoundHex(delta.x / (float)distance, delta.y / (float)distance, delta.z / (float)distance);
    }

    static HexCube ResolveRelativeCubeDirection(TacticalBattlePosition origin, TacticalBattlePosition facing, string relativeDirection)
    {
        HexCube current = OffsetDeltaToCubeDirection(origin, NormalizeDirection(facing));
        int index = HexDirectionIndex(current);
        int turn = CreaturePhraseUtility.NormalizeToken(relativeDirection) switch
        {
            "LEFT" => 1,
            "RIGHT" => -1,
            "BACKWARD" => 3,
            _ => 0,
        };
        return HexDirectionAt((index + turn + 6) % 6);
    }

    static int HexDirectionIndex(HexCube direction)
    {
        for (int index = 0; index < 6; index++)
        {
            HexCube candidate = HexDirectionAt(index);
            if (candidate.x == direction.x && candidate.y == direction.y && candidate.z == direction.z)
                return index;
        }
        return 0;
    }

    static HexCube HexDirectionAt(int index)
    {
        return index switch
        {
            0 => new HexCube(1, -1, 0),
            1 => new HexCube(0, -1, 1),
            2 => new HexCube(-1, 0, 1),
            3 => new HexCube(-1, 1, 0),
            4 => new HexCube(0, 1, -1),
            _ => new HexCube(1, 0, -1),
        };
    }

    static HexCube ScaleHex(HexCube direction, int scale)
    {
        return new HexCube(direction.x * scale, direction.y * scale, direction.z * scale);
    }

    static bool CanFlyOverTerrain(string verb)
    {
        string normalized = CreaturePhraseUtility.NormalizeToken(verb);
        return normalized == "FLY" || normalized == "GLIDE" || normalized == "HOVER";
    }

    static bool CanBypassObstacle(string verb)
    {
        string normalized = CreaturePhraseUtility.NormalizeToken(verb);
        return CanFlyOverTerrain(normalized) || normalized == "JUMP" || normalized == "VAULT" || normalized == "LEAP";
    }

    static HexCube OffsetDeltaToCubeDirection(TacticalBattlePosition origin, TacticalBattlePosition offsetDelta)
    {
        TacticalBattlePosition adjacent = new TacticalBattlePosition(origin.x + offsetDelta.x, origin.y + offsetDelta.y);
        if (offsetDelta.x == 0 && offsetDelta.y == 0)
            adjacent = new TacticalBattlePosition(origin.x + 1, origin.y);

        HexCube originCube = ToHexCube(origin);
        HexCube adjacentCube = ToHexCube(adjacent);
        HexCube delta = new HexCube(adjacentCube.x - originCube.x, adjacentCube.y - originCube.y, adjacentCube.z - originCube.z);
        if (Mathf.Abs(delta.x) + Mathf.Abs(delta.y) + Mathf.Abs(delta.z) == 0)
            return new HexCube(1, -1, 0);
        return delta;
    }

    static HexCube ToHexCube(TacticalBattlePosition position)
    {
        int x = position.x - (position.y - (position.y & 1)) / 2;
        int z = position.y;
        int y = -x - z;
        return new HexCube(x, y, z);
    }

    static TacticalBattlePosition FromHexCube(HexCube cube)
    {
        int x = cube.x + (cube.z - (cube.z & 1)) / 2;
        int y = cube.z;
        return new TacticalBattlePosition(x, y);
    }

    static HexCube RoundHex(float x, float y, float z)
    {
        int rx = Mathf.RoundToInt(x);
        int ry = Mathf.RoundToInt(y);
        int rz = Mathf.RoundToInt(z);

        float xDiff = Mathf.Abs(rx - x);
        float yDiff = Mathf.Abs(ry - y);
        float zDiff = Mathf.Abs(rz - z);

        if (xDiff > yDiff && xDiff > zDiff)
            rx = -ry - rz;
        else if (yDiff > zDiff)
            ry = -rx - rz;
        else
            rz = -rx - ry;

        return new HexCube(rx, ry, rz);
    }

    struct HexCube
    {
        public int x;
        public int y;
        public int z;

        public HexCube(int x, int y, int z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static HexCube operator +(HexCube a, HexCube b)
        {
            return new HexCube(a.x + b.x, a.y + b.y, a.z + b.z);
        }
    }

    static string Key(TacticalBattlePosition position)
    {
        return $"{position.x}:{position.y}";
    }

    static TacticalBattleCommandResult Fail(string message, TacticalBattleActionProfile actionProfile = null)
    {
        return new TacticalBattleCommandResult
        {
            success = false,
            message = message,
            actionProfile = actionProfile,
        };
    }
}
