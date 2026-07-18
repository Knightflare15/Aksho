using System;
using System.Collections.Generic;
using UnityEngine;

public enum TacticalBattleCellType
{
    Empty,
    Box,
    Spikes,
    Wall,
    Roof,
    Bridge,
    Water,
    Tree,
    Rock,
}

[Serializable]
public struct TacticalBattlePosition
{
    public int x;
    public int y;

    public TacticalBattlePosition(int x, int y)
    {
        this.x = x;
        this.y = y;
    }
}

[Serializable]
public struct TacticalBattleStats
{
    public int maxHp;
    public int attack;
    public int defense;
    public int speed;
    public int accuracy;
    public int evasion;
    public int maxPp;

    public static TacticalBattleStats FromCreatureStats(CreatureStatBlock stats)
    {
        return new TacticalBattleStats
        {
            maxHp = Mathf.Max(1, stats.maxHp),
            attack = Mathf.Max(1, stats.attack),
            defense = Mathf.Max(1, stats.defense),
            speed = Mathf.Max(1, stats.speed),
            accuracy = 85,
            evasion = 10,
            maxPp = Mathf.Max(1, stats.maxPp),
        };
    }
}

[Serializable]
public sealed class TacticalBattleUnit
{
    public string displayPhrase = "";
    public string noun = "";
    public string determiner = "";
    public string adjective = "";
    public float adjectiveEffectiveness = 1f;
    public TacticalBattleStats stats;
    public int currentHp;
    public int currentPp;
    public TacticalBattlePosition position;
    public Dictionary<string, float> verbCooldownReadyAt = new Dictionary<string, float>();
}

[Serializable]
public sealed class TacticalBattleActionProfile
{
    public string verb = "";
    public string adverb = "";
    public string preposition = "";
    public string direction = "";
    public CreatureVerbCategory category = CreatureVerbCategory.Unspecified;
    public int power;
    public float accuracy;
    public float speedScore;
    public float actionSpeed;
    public int ppCost;
    public int rangeCells;
    public int movementCells;
    public int shieldAmount;
    public float shieldDurationSeconds;
    public float cooldownSeconds;
    public float damageMultiplier = 1f;
    public float varietyEffectiveness = 1f;
    public int varietyRepeatLevel;
    public string varietyFeedback = "";
    public bool movementAction;
    public bool AttackAction => category == CreatureVerbCategory.Attack;
    public bool DefenseAction => category == CreatureVerbCategory.Defense;
    public bool UtilityAction => category == CreatureVerbCategory.Utility;
}

[Serializable]
public sealed class TacticalBattleCommandResult
{
    public bool success;
    public string message = "";
    public TacticalBattleActionProfile actionProfile;
    public TacticalBattlePosition finalPosition;
    public List<TacticalBattlePosition> movementPath = new List<TacticalBattlePosition>();
}

public sealed class TacticalGrammarBattleState
{
    public const int DefaultSize = 5;

    public int width = DefaultSize;
    public int height = DefaultSize;
    public TacticalBattleCellType[,] terrain = new TacticalBattleCellType[DefaultSize, DefaultSize];
    public TacticalBattleUnit playerUnit;
    public TacticalBattleUnit enemyUnit;
    public TacticalBattlePosition playerFacing = new TacticalBattlePosition(0, 1);
    public TacticalBattlePosition enemyFacing = new TacticalBattlePosition(0, -1);

    public bool IsInside(TacticalBattlePosition position)
    {
        return position.x >= 0 && position.x < width && position.y >= 0 && position.y < height;
    }

    public TacticalBattleCellType GetCell(TacticalBattlePosition position)
    {
        return IsInside(position) ? terrain[position.x, position.y] : TacticalBattleCellType.Wall;
    }

    public bool IsBlocked(TacticalBattlePosition position)
    {
        TacticalBattleCellType cell = GetCell(position);
        return cell == TacticalBattleCellType.Box ||
               cell == TacticalBattleCellType.Wall ||
               cell == TacticalBattleCellType.Tree ||
               cell == TacticalBattleCellType.Rock;
    }

    public bool IsHazard(TacticalBattlePosition position)
    {
        TacticalBattleCellType cell = GetCell(position);
        return cell == TacticalBattleCellType.Spikes || cell == TacticalBattleCellType.Water;
    }

    public bool IsOccupied(TacticalBattlePosition position)
    {
        return (playerUnit != null && playerUnit.position.x == position.x && playerUnit.position.y == position.y) ||
               (enemyUnit != null && enemyUnit.position.x == position.x && enemyUnit.position.y == position.y);
    }
}

public sealed partial class TacticalGrammarBattler
{
    public const int MinAttackRangeCells = 1;
    public const int MaxAttackRangeCells = 3;
    public const int MaxMovementCells = 3;
    public const int EnemyAttackRangeCells = 1;
    public const int EnemyRetreatCells = 3;
    public const float BaseShieldDurationSeconds = 5f;

    readonly CreatureCombatRegistry registry;
    readonly CombatWordVarietyTracker wordVariety = new CombatWordVarietyTracker();
    bool hasSelectedTacticalCell;
    TacticalBattlePosition selectedTacticalCell;

    public TacticalGrammarBattleState State { get; }
    public bool HasSelectedTacticalCell => hasSelectedTacticalCell;
    public TacticalBattlePosition SelectedTacticalCell => selectedTacticalCell;


}
