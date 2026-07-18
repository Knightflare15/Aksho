using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
#if UNITY_EDITOR
using UnityEditor;
#endif


public partial class EnemyWaveDirector : MonoBehaviour
{
    void ResolveReferences()
    {
        playerController = playerController != null ? playerController : FindAnyObjectByType<PlayerController>();
        playerHealth = playerHealth != null ? playerHealth :
            playerController != null ? playerController.GetComponent<PlayerHealth>() : FindAnyObjectByType<PlayerHealth>();
        wordActionHandler = wordActionHandler != null ? wordActionHandler : FindAnyObjectByType<WordActionHandler>();
        spellRegistry = spellRegistry != null ? spellRegistry : GetComponent<SpellRegistry>();
        if (legacyWordSpellFallbackEnabled && spellRegistry == null)
            spellRegistry = gameObject.AddComponent<SpellRegistry>();
        creatureRegistry = creatureRegistry != null ? creatureRegistry : GetComponent<CreatureCombatRegistry>();
        if (creatureRegistry == null)
            creatureRegistry = FindAnyObjectByType<CreatureCombatRegistry>();
        if (creatureRegistry == null)
            creatureRegistry = gameObject.AddComponent<CreatureCombatRegistry>();
        battleEncounterController = battleEncounterController != null ? battleEncounterController : GetComponent<BattleEncounterController>();
        if (battleEncounterController == null)
            battleEncounterController = gameObject.AddComponent<BattleEncounterController>();
        battleEncounterController.waveDirector = this;
        battleEncounterController.playerController = playerController;
        battleEncounterController.creatureCombat = playerController != null
            ? playerController.GetComponent<CreatureCombatController>() ?? playerController.GetComponentInChildren<CreatureCombatController>(true)
            : FindAnyObjectByType<CreatureCombatController>();
        spellPerformanceTracker = spellPerformanceTracker != null ? spellPerformanceTracker : GetComponent<SpellPerformanceTracker>();
        if (spellPerformanceTracker == null)
            spellPerformanceTracker = gameObject.AddComponent<SpellPerformanceTracker>();
        playerLearningProfile = playerLearningProfile != null ? playerLearningProfile : GetComponent<PlayerLearningProfile>();
        if (playerLearningProfile == null)
            playerLearningProfile = gameObject.AddComponent<PlayerLearningProfile>();
        objectiveDirector = null;
        attackCoordinator = attackCoordinator != null ? attackCoordinator : GetComponent<EnemyAttackCoordinator>();
        if (attackCoordinator == null)
            attackCoordinator = gameObject.AddComponent<EnemyAttackCoordinator>();
        if (arenaCenter == null && playerController != null)
            arenaCenter = playerController.transform;
    }

    void ResolveEnemyCatalog()
    {
        if (enemyCatalog != null)
            return;

#if UNITY_EDITOR
        string[] guids = AssetDatabase.FindAssets("t:EnemyCatalog");
        if (guids.Length > 0)
        {
            System.Array.Sort(guids, (left, right) =>
            {
                string leftPath = AssetDatabase.GUIDToAssetPath(left);
                string rightPath = AssetDatabase.GUIDToAssetPath(right);
                bool leftMain = leftPath.Contains("EnemyCatalog_Main");
                bool rightMain = rightPath.Contains("EnemyCatalog_Main");
                if (leftMain != rightMain)
                    return leftMain ? -1 : 1;
                return string.CompareOrdinal(leftPath, rightPath);
            });

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            enemyCatalog = AssetDatabase.LoadAssetAtPath<EnemyCatalog>(path);
            if (enemyCatalog != null)
                Debug.Log($"[EnemyWaveDirector] Auto-assigned EnemyCatalog '{enemyCatalog.name}' from {path}.", enemyCatalog);
        }
#endif
    }

    void TryHookPlayerDeath()
    {
        ResolveReferences();
        if (playerHealth == null || deathHookRegistered)
            return;
        playerHealth.OnDied += HandlePlayerDied;
        deathHookRegistered = true;
    }

    List<EnemyDefinition> GetUnlockedEnemyDefinitions()
    {
        var unlocked = new List<EnemyDefinition>();
        CurriculumSessionManager curriculum = CurriculumSessionManager.Instance;
        foreach (EnemyDefinition definition in GetConfiguredEnemyDefinitions())
        {
            if (definition == null || definition.unlockLevel > CurrentLevel)
                continue;
            bool hasLegacySpell = spellRegistry != null && spellRegistry.HasSpell(definition.weaknessSpell);
            bool hasCreatureNoun = creatureRegistry != null &&
                                   creatureRegistry.TryGetNoun(definition.EffectiveCreatureFamilyNoun, out _);
            if (!hasLegacySpell && !hasCreatureNoun)
                continue;
            if (curriculum != null && curriculum.IsSchoolModeActive && !curriculum.IsWordAllowed(definition.weaknessSpell) && !hasCreatureNoun)
                continue;
            if (playerLearningProfile != null && !playerLearningProfile.IsWordUnlocked(definition.weaknessSpell) && !hasCreatureNoun)
                continue;
            unlocked.Add(definition);
        }
        return unlocked;
    }

    int GetMaxAvailableLevel()
    {
        int max = 1;
        foreach (EnemyDefinition definition in GetConfiguredEnemyDefinitions())
            if (definition != null)
                max = Mathf.Max(max, definition.unlockLevel);
        return max;
    }

    List<EnemyDefinition> GetConfiguredEnemyDefinitions()
    {
        if (enemyCatalog == null)
        {
            if (!loggedMissingEnemyCatalog)
            {
                Debug.LogWarning("[EnemyWaveDirector] No EnemyCatalog assigned. Using runtime noun-family placeholder enemies.");
                loggedMissingEnemyCatalog = true;
            }
            return GetRuntimeFamilyEnemyDefinitions();
        }

        if (enemyCatalog.enemyDefinitions == null)
            return GetRuntimeFamilyEnemyDefinitions();

        if (enemyCatalog.enemyDefinitions.Count == 0)
            return GetRuntimeFamilyEnemyDefinitions();

        return enemyCatalog.enemyDefinitions;
    }

    List<EnemyDefinition> GetRuntimeFamilyEnemyDefinitions()
    {
        runtimeFamilyEnemyDefinitions.Clear();
        if (creatureRegistry != null)
        {
            foreach (NounDefinition noun in creatureRegistry.Nouns)
            {
                if (noun == null)
                    continue;
                EnemyDefinition definition = BuildFamilyEnemyDefinition(noun.canonicalNoun);
                if (definition != null)
                    runtimeFamilyEnemyDefinitions.Add(definition);
            }
        }

        return runtimeFamilyEnemyDefinitions.Count > 0
            ? runtimeFamilyEnemyDefinitions
            : emptyEnemyDefinitions;
    }
}
