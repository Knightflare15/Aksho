using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
#if UNITY_EDITOR
using UnityEditor;
#endif


public partial class EnemyWaveDirector : MonoBehaviour
{
    List<EnemyDefinition> BuildSpawnDefinitionsForWave(WaveDescriptor wave, int count)
    {
        var result = new List<EnemyDefinition>();
        List<EnemyDefinition> definitions = wave.enemyDefinitions != null && wave.enemyDefinitions.Count > 0
            ? wave.enemyDefinitions : new List<EnemyDefinition> { wave.enemyDefinition };
        for (int i = 0; i < count; i++)
        {
            EnemyDefinition definition = definitions[i % Mathf.Min(2, definitions.Count)];
            if (definition != null)
                result.Add(definition);
        }
        return result;
    }

    EnemyDefinition SelectEnemyDefinitionForWave(List<EnemyDefinition> unlocked)
    {
        if (unlocked == null || unlocked.Count == 0)
            return null;
        float total = 0f;
        foreach (EnemyDefinition definition in unlocked)
            total += GetSpawnWeight(definition);
        float roll = Random.value * total;
        foreach (EnemyDefinition definition in unlocked)
        {
            roll -= GetSpawnWeight(definition);
            if (roll <= 0f)
                return definition;
        }
        return unlocked[unlocked.Count - 1];
    }

    List<EnemyDefinition> SelectEnemyDefinitionsForWave(List<EnemyDefinition> unlocked)
    {
        var selected = new List<EnemyDefinition>();
        EnemyDefinition primary = SelectEnemyDefinitionForWave(unlocked);
        if (primary == null)
            return selected;
        selected.Add(primary);
        if (CurrentLevel < 2 || playerLearningProfile == null ||
            !playerLearningProfile.IsWordMastered(primary.weaknessSpell))
            return selected;
        List<EnemyDefinition> alternatives = unlocked.FindAll(x => x != null && x != primary &&
            playerLearningProfile.HasSuccessfullyUsedWord(x.weaknessSpell));
        EnemyDefinition secondary = SelectEnemyDefinitionForWave(alternatives);
        if (secondary != null)
            selected.Add(secondary);
        return selected;
    }

    float GetSpawnWeight(EnemyDefinition definition)
    {
        if (definition == null)
            return 0f;
        string word = SpellRegistry.NormalizeWord(definition.weaknessSpell);
        float profileFocus = playerLearningProfile != null ? playerLearningProfile.GetLearningFocusWeight(word) : 1f;
        float performanceFocus = spellPerformanceTracker != null ? spellPerformanceTracker.GetDifficultyWeight(word) : 1f;
        float adaptive = Mathf.Sqrt(Mathf.Max(0.05f, profileFocus) * Mathf.Max(0.05f, performanceFocus));
        if (word == lastPlannedPrimaryWord && consecutivePrimaryPlans >= 2)
            adaptive *= 0.05f;
        return Mathf.Max(0.05f, definition.baseSpawnWeight) * Mathf.Max(0.05f, adaptive);
    }

    float GetAverageDifficultyWeight(List<EnemyDefinition> definitions)
    {
        if (definitions == null || definitions.Count == 0)
            return 1f;
        float total = 0f;
        foreach (EnemyDefinition definition in definitions)
            total += playerLearningProfile != null && playerLearningProfile.IsWordMastered(definition.weaknessSpell) ? 1.5f : 1f;
        return total / definitions.Count;
    }

    string GetAliveEnemyWeaknessRecommendation()
    {
        var counts = new Dictionary<string, int>();
        foreach (SpellTarget enemy in aliveEnemies)
        {
            if (enemy == null || enemy.IsDefeated)
                continue;
            string word = SpellRegistry.NormalizeWord(enemy.RequiredCreatureNoun);
            counts.TryGetValue(word, out int count);
            counts[word] = count + 1;
        }
        string best = "";
        int bestCount = 0;
        foreach (var pair in counts)
        {
            if (pair.Value > bestCount)
            {
                best = pair.Key;
                bestCount = pair.Value;
            }
        }
        return best;
    }

    void ConfigureEnemyInstance(SpellTarget enemy, EnemyDefinition definition)
    {
        if (enemy is EnemyTarget typedEnemy)
            typedEnemy.ApplyDefinition(definition);
        else
        {
            // Retain this serialized field only so old saves/prefabs deserialize safely.
            // Primary combat uses requiredCreatureNoun below.
            enemy.requiredSpell = SpellRegistry.NormalizeWord(definition.EffectiveCreatureFamilyNoun);
            enemy.requiredCreatureNoun = SpellRegistry.NormalizeWord(definition.EffectiveCreatureFamilyNoun);
            enemy.maxHp = Mathf.Max(1, definition.maxHp);
            enemy.hitsToDefeat = Mathf.Max(1, definition.hitsToDefeat);
            if (enemy.additionalAcceptedSpells == null)
                enemy.additionalAcceptedSpells = new List<string>();
            enemy.additionalAcceptedSpells.Clear();
            AddAdditionalWeaknessSpells(enemy, definition.additionalWeaknessSpells);
            if (enemy.additionalAcceptedCreatureNouns == null)
                enemy.additionalAcceptedCreatureNouns = new List<string>();
            enemy.additionalAcceptedCreatureNouns.Clear();
            AddAdditionalCreatureNouns(enemy, definition.additionalCreatureFamilyNouns);
        }
    }

    static void AddAdditionalWeaknessSpells(SpellTarget enemy, IEnumerable<string> spells)
    {
        if (enemy == null || spells == null)
            return;

        foreach (string spell in spells)
        {
            string normalized = SpellRegistry.NormalizeWord(spell);
            if (string.IsNullOrEmpty(normalized) ||
                string.Equals(normalized, enemy.requiredSpell, System.StringComparison.OrdinalIgnoreCase) ||
                enemy.additionalAcceptedSpells.Exists(existing => string.Equals(
                    SpellRegistry.NormalizeWord(existing),
                    normalized,
                    System.StringComparison.OrdinalIgnoreCase)))
                continue;

            enemy.additionalAcceptedSpells.Add(normalized);
        }
    }

    static void AddAdditionalCreatureNouns(SpellTarget enemy, IEnumerable<string> nouns)
    {
        if (enemy == null || nouns == null)
            return;

        foreach (string noun in nouns)
        {
            string normalized = SpellRegistry.NormalizeWord(noun);
            if (string.IsNullOrEmpty(normalized) ||
                string.Equals(normalized, enemy.RequiredCreatureNoun, System.StringComparison.OrdinalIgnoreCase) ||
                enemy.additionalAcceptedCreatureNouns.Exists(existing => string.Equals(
                    SpellRegistry.NormalizeWord(existing),
                    normalized,
                    System.StringComparison.OrdinalIgnoreCase)))
                continue;

            enemy.additionalAcceptedCreatureNouns.Add(normalized);
        }
    }

    List<EnemyDefinition> FilterEnemyDefinitionsForPendingZone(List<EnemyDefinition> definitions)
    {
        if (pendingEncounterNounFamilies.Count == 0 || definitions == null || definitions.Count == 0)
            return definitions;

        var filtered = new List<EnemyDefinition>();
        var matchedFamilies = new HashSet<string>();
        foreach (string noun in pendingEncounterNounFamilies)
        {
            string canonical = CreatureCombatRegistry.ResolveCanonicalNounInScene(noun);
            if (string.IsNullOrEmpty(canonical))
                continue;

            foreach (EnemyDefinition definition in definitions)
            {
                if (definition == null)
                    continue;

                if (definition.MatchesCreatureFamily(noun))
                {
                    if (!filtered.Contains(definition))
                        filtered.Add(definition);
                    matchedFamilies.Add(canonical);
                    break;
                }
            }
        }

        foreach (string noun in pendingEncounterNounFamilies)
        {
            string canonical = CreatureCombatRegistry.ResolveCanonicalNounInScene(noun);
            if (string.IsNullOrEmpty(canonical) || matchedFamilies.Contains(canonical))
                continue;

            EnemyDefinition definition = BuildFamilyEnemyDefinition(canonical);
            if (definition != null)
            {
                filtered.Add(definition);
                matchedFamilies.Add(canonical);
            }
        }

        if (filtered.Count > 0)
            return filtered;

        List<EnemyDefinition> familyFallback = BuildFamilyEnemyDefinitions(pendingEncounterNounFamilies);
        return familyFallback.Count > 0 ? familyFallback : definitions;
    }

    List<EnemyDefinition> BuildFamilyEnemyDefinitions(IEnumerable<string> nounFamilies)
    {
        var result = new List<EnemyDefinition>();
        if (nounFamilies == null)
            return result;

        foreach (string nounFamily in nounFamilies)
        {
            EnemyDefinition definition = BuildFamilyEnemyDefinition(nounFamily);
            if (definition != null &&
                !result.Exists(existing => string.Equals(
                    SpellRegistry.NormalizeWord(existing.EffectiveCreatureFamilyNoun),
                    SpellRegistry.NormalizeWord(definition.EffectiveCreatureFamilyNoun),
                    System.StringComparison.OrdinalIgnoreCase)))
                result.Add(definition);
        }

        return result;
    }

    EnemyDefinition BuildFamilyEnemyDefinition(string nounFamily)
    {
        string canonical = CreatureCombatRegistry.ResolveCanonicalNounInScene(nounFamily);
        if (string.IsNullOrEmpty(canonical))
            return null;

        NounDefinition noun = null;
        creatureRegistry?.TryGetNoun(canonical, out noun);
        var synonyms = new List<string>();
        if (noun != null)
        {
            foreach (string accepted in noun.AcceptedForms())
            {
                string normalized = SpellRegistry.NormalizeWord(accepted);
                if (!string.IsNullOrEmpty(normalized) &&
                    !string.Equals(normalized, canonical, System.StringComparison.OrdinalIgnoreCase) &&
                    !synonyms.Contains(normalized))
                    synonyms.Add(normalized);
            }
        }

        return new EnemyDefinition
        {
            enemyId = $"{canonical.ToLowerInvariant()}_family_placeholder",
            displayName = $"{ToDisplayWord(canonical)} Family",
            unlockLevel = noun != null ? Mathf.Max(1, noun.unlockLevel) : 1,
            weaknessSpell = canonical,
            creatureFamilyNoun = canonical,
            additionalCreatureFamilyNouns = synonyms,
            maxHp = noun != null ? Mathf.Max(2, noun.baseStats.maxHp) : 4,
            hitsToDefeat = 1,
            baseSpawnWeight = 1f,
            learningFocus = $"{canonical.ToLowerInvariant()} noun family",
        };
    }

    static string ToDisplayWord(string value)
    {
        string normalized = SpellRegistry.NormalizeWord(value);
        if (string.IsNullOrEmpty(normalized))
            return "Noun";
        return normalized.Substring(0, 1) + normalized.Substring(1).ToLowerInvariant();
    }

    void CreateEncounterLock(Vector3 anchor)
    {
        if (!lockPlayerDuringEncounters || playerController == null)
            return;

        ClearEncounterLock();
        activeEncounterLock = EncounterLockZone.Create(anchor, encounterLockRadius, playerController.transform);
    }

    void ClearEncounterLock()
    {
        if (activeEncounterLock == null)
            return;

        Destroy(activeEncounterLock.gameObject);
        activeEncounterLock = null;
    }

    void NotifyEncounterEnded(EncounterOutcome outcome)
    {
        OnEncounterEnded?.Invoke(activeEncounter, outcome);
    }
}
