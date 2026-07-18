using System.Collections.Generic;
using UnityEngine;

public class EnemyTarget : SpellTarget
{
    public string enemyId = "enemy";
    public string displayName = "Enemy";
    public int unlockLevel = 1;
    public string learningFocus = "";

    public virtual void ApplyDefinition(EnemyDefinition definition)
    {
        if (definition == null)
            return;

        enemyId = definition.enemyId;
        displayName = definition.displayName;
        unlockLevel = definition.unlockLevel;
        learningFocus = definition.learningFocus;
        // Legacy projectile compatibility mirrors the current creature noun so it
        // cannot disagree with the production grammar-creature encounter.
        requiredSpell = SpellRegistry.NormalizeWord(definition.EffectiveCreatureFamilyNoun);
        requiredCreatureNoun = SpellRegistry.NormalizeWord(definition.EffectiveCreatureFamilyNoun);
        maxHp = Mathf.Max(1, definition.maxHp);
        hitsToDefeat = Mathf.Max(1, definition.hitsToDefeat);

        if (additionalAcceptedSpells == null)
            additionalAcceptedSpells = new List<string>();
        additionalAcceptedSpells.Clear();
        AddAdditionalWeaknessSpells(definition.additionalWeaknessSpells);

        if (additionalAcceptedCreatureNouns == null)
            additionalAcceptedCreatureNouns = new List<string>();
        additionalAcceptedCreatureNouns.Clear();
        AddAdditionalCreatureNouns(definition.additionalCreatureFamilyNouns);
    }

    protected void AddAdditionalWeaknessSpells(IEnumerable<string> spells)
    {
        if (spells == null)
            return;

        foreach (string spell in spells)
        {
            string normalized = SpellRegistry.NormalizeWord(spell);
            if (string.IsNullOrEmpty(normalized) ||
                string.Equals(normalized, requiredSpell, System.StringComparison.OrdinalIgnoreCase) ||
                additionalAcceptedSpells.Exists(existing => string.Equals(
                    SpellRegistry.NormalizeWord(existing),
                    normalized,
                    System.StringComparison.OrdinalIgnoreCase)))
                continue;

            additionalAcceptedSpells.Add(normalized);
        }
    }

    protected void AddAdditionalCreatureNouns(IEnumerable<string> nouns)
    {
        if (nouns == null)
            return;

        foreach (string noun in nouns)
        {
            string normalized = SpellRegistry.NormalizeWord(noun);
            if (string.IsNullOrEmpty(normalized) ||
                string.Equals(normalized, RequiredCreatureNoun, System.StringComparison.OrdinalIgnoreCase) ||
                additionalAcceptedCreatureNouns.Exists(existing => string.Equals(
                    SpellRegistry.NormalizeWord(existing),
                    normalized,
                    System.StringComparison.OrdinalIgnoreCase)))
                continue;

            additionalAcceptedCreatureNouns.Add(normalized);
        }
    }
}
