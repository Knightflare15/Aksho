using System;

public enum RunEndReason
{
    Victory,
    Defeat,
    TimeUp,
    Abandoned,
}

[Serializable]
public sealed class RunSummary
{
    public RunEndReason reason;
    public int stagesCompleted;
    public int subarenasCleared;
    public int fullLoopsCleared;
    public int enemiesDefeated;
    public int coinsCollected;
    public int coinsSpent;
    public int upgradesPurchased;
    public float elapsedSeconds;
    public int configuredDurationSeconds;
    public string missionId;

    public int CoinsRemaining => Math.Max(0, coinsCollected - coinsSpent);
}
