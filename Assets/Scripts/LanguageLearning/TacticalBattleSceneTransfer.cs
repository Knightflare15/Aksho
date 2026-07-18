using System;
using System.Collections.Generic;

[Serializable]
public sealed class TacticalBattleScenePayload
{
    public string sourceSceneName = "";
    public string battleSceneName = "";
    public string enemyNoun = "";
    public string enemyDisplayName = "";
    public string grammarTopic = "";
    public int grammarTopicTier = 1;
    public int width = TacticalGrammarBattleState.DefaultSize;
    public int height = TacticalGrammarBattleState.DefaultSize;
    public bool showDebugGrid;
    public TacticalBattlePosition playerStart;
    public TacticalBattlePosition enemyStart;
    public bool playerSummoned;
    public TacticalBattlePosition playerPosition;
    public TacticalBattlePosition enemyPosition;
    public bool hasSelectedTacticalCell;
    public TacticalBattlePosition selectedTacticalCell;
    public int playerCurrentHp;
    public int playerMaxHp;
    public int enemyCurrentHp;
    public int enemyMaxHp;
    public bool pendingEnemyAttack;
    public string activeCurse = "";
    public List<TacticalBattleTerrainPayload> terrain = new List<TacticalBattleTerrainPayload>();
    public List<TacticalBattlePosition> playerAttackArc = new List<TacticalBattlePosition>();
    public List<TacticalBattlePosition> playerMovementPath = new List<TacticalBattlePosition>();
    public List<TacticalBattlePosition> enemyAttackArc = new List<TacticalBattlePosition>();
    public List<string> enemyMoves = new List<string>();
    public List<string> enemyStatuses = new List<string>();
    public List<GrammarPhrasePattern> practicePatterns = new List<GrammarPhrasePattern>();
    public List<string> masteryTags = new List<string>();
}

[Serializable]
public sealed class TacticalBattleTerrainPayload
{
    public int x;
    public int y;
    public TacticalBattleCellType cellType;
}

public static class TacticalBattleSceneTransfer
{
    public static TacticalBattleScenePayload CurrentPayload { get; private set; }
    public static bool HasPayload => CurrentPayload != null;
    public static event Action<TacticalBattleScenePayload> PayloadChanged;

    public static void SetPayload(TacticalBattleScenePayload payload)
    {
        CurrentPayload = payload;
        PayloadChanged?.Invoke(CurrentPayload);
    }

    public static void NotifyPayloadChanged()
    {
        PayloadChanged?.Invoke(CurrentPayload);
    }

    public static void Clear()
    {
        CurrentPayload = null;
        PayloadChanged?.Invoke(null);
    }
}
