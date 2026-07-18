using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "EnemyCatalog", menuName = "The Script/Content/Enemy Catalog")]
public class EnemyCatalog : ScriptableObject
{
    public List<EnemyDefinition> enemyDefinitions = new List<EnemyDefinition>();
}
