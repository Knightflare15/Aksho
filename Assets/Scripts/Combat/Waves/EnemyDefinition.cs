using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class EnemyDefinition
{
    public string enemyId = "cheese";
    public string displayName = "Cheese Enemy";
    [Min(1)] public int unlockLevel = 1;
    public string weaknessSpell = "CAT";
    public List<string> additionalWeaknessSpells = new List<string>();
    [Header("Creature Combat")]
    [Tooltip("Canonical noun family required for creature combat. Defaults to Weakness Spell for compatibility.")]
    public string creatureFamilyNoun = "";
    public List<string> additionalCreatureFamilyNouns = new List<string>();
    public List<string> zoneTags = new List<string>();
    [Header("Bestiary")]
    public Sprite bestiaryPhoto;
    public AudioClip weaknessPronunciationClip;
    [Min(1)] public int maxHp = 3;
    [Min(1)] public int hitsToDefeat = 1;
    [Min(0.1f)] public float baseSpawnWeight = 1f;
    [TextArea] public string learningFocus = "Short a CVC";
    public SpellTarget prefabOverride;

    public string EffectiveCreatureFamilyNoun =>
        string.IsNullOrWhiteSpace(creatureFamilyNoun) ? weaknessSpell : creatureFamilyNoun;

    public bool MatchesCreatureFamily(string noun)
    {
        string normalized = SpellRegistry.NormalizeWord(noun);
        if (string.IsNullOrEmpty(normalized))
            return false;
        string canonical = CreatureCombatRegistry.ResolveCanonicalNounInScene(normalized);
        string familyCanonical = CreatureCombatRegistry.ResolveCanonicalNounInScene(EffectiveCreatureFamilyNoun);

        if (canonical == familyCanonical)
            return true;

        if (additionalCreatureFamilyNouns != null)
            foreach (string accepted in additionalCreatureFamilyNouns)
                if (canonical == CreatureCombatRegistry.ResolveCanonicalNounInScene(accepted))
                    return true;

        if (additionalWeaknessSpells != null)
            foreach (string accepted in additionalWeaknessSpells)
                if (normalized == SpellRegistry.NormalizeWord(accepted))
                    return true;

        return false;
    }
}
