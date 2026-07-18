using System.Collections.Generic;
using UnityEngine;

public enum ZoneKind
{
    Start,
    Objective,
    Side
}

[System.Serializable]
public class ZoneData
{
    public ZoneKind kind;
    public SemanticZoneKind semanticKind;
    public Vector2Int gridPosition;
    public Vector3 worldPosition;
    public float radius;
    public float desiredHeight;
    public string grammarTopic;
    public TranslatorAssistMode translatorAssist;
    public List<string> encounterNounFamilies = new List<string>();
}

[System.Serializable]
public class PathData
{
    public List<Vector2Int> points = new List<Vector2Int>();
}

[System.Serializable]
public class ObjectivePoint
{
    public Vector2Int gridPosition;
    public Vector3 worldPosition;
    public ZoneData zone;
}

[System.Serializable]
public class ChestPoint
{
    public Vector2Int gridPosition;
    public Vector3 worldPosition;
    public string category;
}

[System.Serializable]
public class EnemyTriggerData
{
    public Vector2Int gridPosition;
    public Vector3 worldPosition;
    public float radius;
    public string category;
    public SemanticZoneKind semanticKind = SemanticZoneKind.Route;
    public string grammarTopic;
    public TranslatorAssistMode translatorAssist = TranslatorAssistMode.Partial;
    public List<string> encounterNounFamilies = new List<string>();
    public List<SpawnPointData> spawnPoints = new List<SpawnPointData>();
}

[System.Serializable]
public class SpawnPointData
{
    public Vector2Int gridPosition;
    public Vector3 worldPosition;
    public EnemyTriggerData trigger;
}
