using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum ContentValidationSeverity
{
    Warning,
    Error,
}

public sealed class ContentValidationIssue
{
    public ContentValidationSeverity severity;
    public string message;
    public Object context;

    public ContentValidationIssue(ContentValidationSeverity severity, string message, Object context = null)
    {
        this.severity = severity;
        this.message = message;
        this.context = context;
    }
}

public static class ContentValidation
{
    public static List<ContentValidationIssue> Validate(
        IReadOnlyList<SpellDefinition> spells,
        IReadOnlyList<EnemyDefinition> enemies)
    {
        var issues = new List<ContentValidationIssue>();
        var spellWords = new HashSet<string>();
        var creatureNouns = BuildCreatureNounSet();
        var enemyIds = new HashSet<string>();

        if (spells != null)
        {
            for (int index = 0; index < spells.Count; index++)
            {
                SpellDefinition spell = spells[index];
                if (spell == null)
                {
                    issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Spell entry {index} is null."));
                    continue;
                }

                string word = SpellRegistry.NormalizeWord(spell.word);
                if (string.IsNullOrEmpty(word))
                    issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Spell entry {index} has no word."));
                else if (!spellWords.Add(word))
                    issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Spell word '{word}' is duplicated."));

                if (spell.unlockLevel < 1)
                    issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Spell '{word}' has an invalid unlock level."));
                if (spell.projectilePrefab == null)
                    issues.Add(new ContentValidationIssue(ContentValidationSeverity.Warning, $"Spell '{word}' uses the fallback projectile."));
                else if (spell.projectilePrefab.GetComponent<SpellProjectile>() == null)
                    issues.Add(new ContentValidationIssue(ContentValidationSeverity.Warning, $"Spell '{word}' projectile prefab has no SpellProjectile component; one will be added at runtime.", spell.projectilePrefab));
                if (spell.projectileSpeed <= 0f)
                    issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Spell '{word}' must have a positive projectile speed."));
                if (spell.castEffectPrefab == null)
                    issues.Add(new ContentValidationIssue(ContentValidationSeverity.Warning, $"Spell '{word}' has no cast effect prefab."));
                if (spell.impactEffectPrefab == null)
                    issues.Add(new ContentValidationIssue(ContentValidationSeverity.Warning, $"Spell '{word}' has no impact effect prefab."));
                if (spell.specialAreaRadius <= 0f)
                    issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Spell '{word}' must have a positive special area radius."));
            }
        }

        if (enemies != null)
        {
            for (int index = 0; index < enemies.Count; index++)
            {
                EnemyDefinition enemy = enemies[index];
                if (enemy == null)
                {
                    issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Enemy entry {index} is null."));
                    continue;
                }

                string id = string.IsNullOrWhiteSpace(enemy.enemyId) ? "" : enemy.enemyId.Trim();
                if (string.IsNullOrEmpty(id))
                    issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Enemy entry {index} has no stable id."));
                else if (!enemyIds.Add(id))
                    issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Enemy id '{id}' is duplicated."));

                AddEnemyCreatureFamilyNouns(creatureNouns, enemy);

                string weakness = SpellRegistry.NormalizeWord(enemy.weaknessSpell);
                if (string.IsNullOrEmpty(weakness))
                    issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Enemy '{id}' has no weakness spell."));
                else if (spellWords.Count > 0 && !spellWords.Contains(weakness) && !creatureNouns.Contains(weakness))
                    issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Enemy '{id}' weakness '{weakness}' is not registered."));

                if (enemy.additionalWeaknessSpells != null)
                {
                    foreach (string additionalWeakness in enemy.additionalWeaknessSpells)
                    {
                        string normalizedAdditionalWeakness = SpellRegistry.NormalizeWord(additionalWeakness);
                        if (string.IsNullOrEmpty(normalizedAdditionalWeakness))
                            issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Enemy '{id}' has an empty additional weakness spell."));
                        else if (spellWords.Count > 0 && !spellWords.Contains(normalizedAdditionalWeakness) && !creatureNouns.Contains(normalizedAdditionalWeakness))
                            issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Enemy '{id}' additional weakness '{normalizedAdditionalWeakness}' is not registered."));
                    }
                }

                if (enemy.maxHp < 1)
                    issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Enemy '{id}' must have at least 1 HP."));
                if (enemy.unlockLevel < 1)
                    issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Enemy '{id}' has an invalid unlock level."));
                if (enemy.baseSpawnWeight <= 0f)
                    issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Enemy '{id}' must have a positive spawn weight."));

                ValidateEnemyPrefab(enemy, id, issues);
            }
        }

        return issues;
    }

    static void AddEnemyCreatureFamilyNouns(HashSet<string> creatureNouns, EnemyDefinition enemy)
    {
        if (creatureNouns == null || enemy == null)
            return;

        string primary = CreaturePhraseUtility.NormalizeToken(enemy.creatureFamilyNoun);
        if (!string.IsNullOrWhiteSpace(primary))
            creatureNouns.Add(primary);

        if (enemy.additionalCreatureFamilyNouns == null)
            return;

        foreach (string additional in enemy.additionalCreatureFamilyNouns)
        {
            string normalized = CreaturePhraseUtility.NormalizeToken(additional);
            if (!string.IsNullOrWhiteSpace(normalized))
                creatureNouns.Add(normalized);
        }
    }

    static void ValidateEnemyPrefab(EnemyDefinition enemy, string id, List<ContentValidationIssue> issues)
    {
        if (enemy.prefabOverride == null)
        {
            issues.Add(new ContentValidationIssue(ContentValidationSeverity.Warning, $"Enemy '{id}' has no Prefab Override; runtime will spawn a labeled placeholder cube."));
            return;
        }

        GameObject prefab = enemy.prefabOverride.gameObject;
        if (prefab.GetComponent<NavMeshAgent>() == null)
            issues.Add(new ContentValidationIssue(ContentValidationSeverity.Warning, $"Enemy '{id}' prefab has no NavMeshAgent.", prefab));
        if (prefab.GetComponent<CombatActor>() == null)
            issues.Add(new ContentValidationIssue(ContentValidationSeverity.Warning, $"Enemy '{id}' prefab has no CombatActor.", prefab));
        if (prefab.GetComponentInChildren<CombatHurtbox>(true) == null)
            issues.Add(new ContentValidationIssue(ContentValidationSeverity.Warning, $"Enemy '{id}' prefab has no CombatHurtbox in its hierarchy.", prefab));
        if (prefab.GetComponentInChildren<CombatHitbox>(true) == null)
            issues.Add(new ContentValidationIssue(ContentValidationSeverity.Warning, $"Enemy '{id}' prefab has no CombatHitbox in its hierarchy.", prefab));
        if (prefab.GetComponent<AttackHitboxController>() == null)
            issues.Add(new ContentValidationIssue(ContentValidationSeverity.Warning, $"Enemy '{id}' prefab has no AttackHitboxController.", prefab));
        if (prefab.GetComponent<EnemyAgentBase>() == null)
            issues.Add(new ContentValidationIssue(ContentValidationSeverity.Warning, $"Enemy '{id}' prefab uses the default runtime-added enemy brain.", prefab));
        if (prefab.GetComponentInChildren<Animator>(true) == null)
            issues.Add(new ContentValidationIssue(ContentValidationSeverity.Warning, $"Enemy '{id}' prefab has no Animator in its hierarchy.", prefab));
    }

    static HashSet<string> BuildCreatureNounSet()
    {
        var nouns = new HashSet<string>();
        CreatureCombatCatalog catalog = CreatureCombatCatalog.CreateRuntimeDefault();
        if (catalog?.nouns == null)
            return nouns;

        foreach (NounDefinition noun in catalog.nouns)
        {
            if (noun == null)
                continue;
            foreach (string acceptedForm in noun.AcceptedForms())
            {
                string normalized = CreaturePhraseUtility.NormalizeToken(acceptedForm);
                if (!string.IsNullOrWhiteSpace(normalized))
                    nouns.Add(normalized);
            }
        }

        return nouns;
    }

    public static void ValidateObjectiveDirector(
        LevelObjectiveDirector director,
        List<ContentValidationIssue> issues)
    {
        if (director == null || issues == null)
            return;

        if (director.pillarPrefab == null)
            issues.Add(new ContentValidationIssue(ContentValidationSeverity.Warning, $"Scene '{director.gameObject.scene.name}' uses fallback pillar primitives.", director));
        if (director.coinPrefab == null)
            issues.Add(new ContentValidationIssue(ContentValidationSeverity.Warning, $"Scene '{director.gameObject.scene.name}' uses fallback coin primitives.", director));
        if (director.exitPortalPrefab == null)
            issues.Add(new ContentValidationIssue(ContentValidationSeverity.Warning, $"Scene '{director.gameObject.scene.name}' uses fallback exit portal primitives.", director));
        if (director.minPillars < 1 || director.maxPillars < director.minPillars)
            issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Scene '{director.gameObject.scene.name}' has invalid pillar count settings.", director));
        if (director.placementRadius <= 0f)
            issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, $"Scene '{director.gameObject.scene.name}' has an invalid objective placement radius.", director));
    }
}
