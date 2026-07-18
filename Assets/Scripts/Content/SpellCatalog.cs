using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "SpellCatalog", menuName = "The Script/Content/Spell Catalog")]
public class SpellCatalog : ScriptableObject
{
    public List<SpellDefinition> spellDefinitions = new List<SpellDefinition>();
}
