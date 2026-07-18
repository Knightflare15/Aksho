using System;
using System.Collections.Generic;

public enum EncounterType
{
    Pressure,
    PillarDefense,
    Optional,
    EscapeSurge,
}

public enum EncounterPhase
{
    Waiting,
    Exploration,
    Combat,
    PillarDefense,
    Escape,
}

public enum EncounterOutcome
{
    Cancelled,
    Completed,
    Failed,
}

[Serializable]
public class WaveDescriptor
{
    public EncounterType encounterType = EncounterType.Pressure;
    public string targetSpellWord = "CAT";
    public int waveIndex = 1;
    public int effectiveWaveTier = 1;
    public int enemyCount = 1;
    public SemanticZoneKind semanticZoneKind = SemanticZoneKind.Route;
    public string grammarTopic = "";
    public int grammarTopicTier = 1;
    public List<string> encounterNounFamilies = new List<string>();
    public List<GrammarPhrasePattern> practicePatterns = new List<GrammarPhrasePattern>();
    public List<string> masteryTags = new List<string>();
    public EnemyDefinition enemyDefinition;
    public List<EnemyDefinition> enemyDefinitions = new List<EnemyDefinition>();
    public float difficultyWeight = 1f;
}
