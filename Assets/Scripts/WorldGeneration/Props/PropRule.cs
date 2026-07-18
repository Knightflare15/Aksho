using UnityEngine;

[System.Serializable]
public sealed class PropRule
{
    public string category = "Trees";
    public GameObject[] prefabs;
    public int minCount = 20;
    public int maxCount = 60;
    public float minSlope = 0f;
    public float maxSlope = 28f;
    public float minDistanceFromPath = 2f;
    public float minDistanceFromObjectives = 5f;
    public bool allowNearWater;
    public Vector2 scaleRange = new Vector2(0.85f, 1.2f);
}
