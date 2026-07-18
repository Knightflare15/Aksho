using UnityEngine;

[CreateAssetMenu(fileName = "PropSpawnProfile", menuName = "The Script/World Generation/Prop Spawn Profile")]
public sealed class PropSpawnProfile : ScriptableObject
{
    public PropRule[] rules;
    public int candidateGridStep = 3;
}
