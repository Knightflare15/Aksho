using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
#if UNITY_EDITOR
using UnityEditor;
#endif


public partial class EnemyWaveDirector : MonoBehaviour
{
    SpellTarget SpawnEnemyInstance(EnemyDefinition definition, Vector3 anchor, EncounterType type)
    {
        if (definition == null || playerController == null)
            return null;

        if (!TryFindSpawnPoint(anchor, out Vector3 spawnPosition))
            return null;

        GameObject instance = definition.prefabOverride != null
            ? Instantiate(definition.prefabOverride.gameObject, spawnPosition, Quaternion.identity)
            : CreatePlaceholderEnemy(definition, spawnPosition);
        instance.name = $"{definition.displayName}_{type}_L{CurrentLevel}";
        instance.SetActive(true);
        SpellTarget enemy = instance.GetComponent<SpellTarget>();
        if (enemy == null)
        {
            Destroy(instance);
            return null;
        }

        ConfigureEnemyInstance(enemy, definition);
        EnemyBestiaryIdentity identity = instance.GetComponent<EnemyBestiaryIdentity>();
        if (identity == null)
            identity = instance.AddComponent<EnemyBestiaryIdentity>();
        identity.definition = definition;
        CheeseEnemyAgent cheeseAgent = instance.GetComponent<CheeseEnemyAgent>();
        if (cheeseAgent == null && instance.GetComponent<EnemyAgentBase>() == null)
            cheeseAgent = instance.AddComponent<CheeseEnemyAgent>();
        if (cheeseAgent != null)
        {
            cheeseAgent.SetAttackCoordinator(attackCoordinator);
            ConfigureGrammarEnemyAttacks(cheeseAgent, activeEncounter, definition);
        }
        NavMeshAgent agent = instance.GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            if (!TryPlaceAgentOnNavMesh(agent, spawnPosition, out Vector3 placedPosition))
            {
                Debug.LogWarning($"[EnemyWaveDirector] Enemy '{definition.enemyId}' spawned, but its NavMeshAgent could not be placed at {spawnPosition}. It will retry from its enemy brain.");
                placedPosition = spawnPosition;
            }

            AlignAgentBaseToRenderedGround(instance, agent, placedPosition);
            if (agent.isOnNavMesh)
                agent.isStopped = false;
        }
        return enemy;
    }

    static void ConfigureGrammarEnemyAttacks(CheeseEnemyAgent agent, WaveDescriptor descriptor, EnemyDefinition definition)
    {
        if (agent == null || descriptor == null)
            return;

        if (descriptor.semanticZoneKind != SemanticZoneKind.Route &&
            descriptor.semanticZoneKind != SemanticZoneKind.Gym)
            return;

        List<EnemyAttackDefinition> attacks = NaturalGrammarProgression.BuildEnemyAttackSet(
            descriptor.semanticZoneKind,
            descriptor.grammarTopic,
            descriptor.grammarTopicTier,
            definition != null ? definition.EffectiveCreatureFamilyNoun : "",
            descriptor.practicePatterns);
        if (attacks == null || attacks.Count == 0)
            return;

        agent.attacks = attacks;
    }

    static string NormalizeMasteryTag(string tag)
    {
        return string.IsNullOrWhiteSpace(tag) ? "" : tag.Trim().ToLowerInvariant();
    }

    static GameObject CreatePlaceholderEnemy(EnemyDefinition definition, Vector3 spawnPosition)
    {
        GameObject instance = GameObject.CreatePrimitive(PrimitiveType.Cube);
        instance.transform.SetPositionAndRotation(spawnPosition + Vector3.up * 0.75f, Quaternion.identity);
        instance.transform.localScale = new Vector3(1.35f, 1.5f, 1.35f);
        EnemyTarget target = instance.AddComponent<EnemyTarget>();
        target.rootToDisable = instance;
        target.targetCollider = instance.GetComponent<Collider>();
        Renderer renderer = instance.GetComponent<Renderer>();
        if (renderer != null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader != null)
                renderer.material = new Material(shader) { color = ResolvePlaceholderEnemyColor(definition) };
        }
        AddPlaceholderEnemyLabel(instance, definition);
        return instance;
    }

    static void AddPlaceholderEnemyLabel(GameObject instance, EnemyDefinition definition)
    {
        if (instance == null)
            return;

        string labelText = SpellRegistry.NormalizeWord(definition != null ? definition.EffectiveCreatureFamilyNoun : "");
        if (string.IsNullOrWhiteSpace(labelText))
            labelText = SpellRegistry.NormalizeWord(definition != null ? definition.weaknessSpell : "");
        if (string.IsNullOrWhiteSpace(labelText))
            labelText = "WORD";

        GameObject labelGo = new GameObject("Enemy Family Label", typeof(TextMesh));
        labelGo.transform.SetParent(instance.transform, false);
        labelGo.transform.localPosition = new Vector3(0f, 1.25f, 0f);
        labelGo.transform.localRotation = Quaternion.Euler(68f, 0f, 0f);
        TextMesh label = labelGo.GetComponent<TextMesh>();
        label.text = labelText;
        label.anchor = TextAnchor.MiddleCenter;
        label.alignment = TextAlignment.Center;
        label.fontSize = 32;
        label.characterSize = 0.08f;
        label.color = Color.white;
    }

    static Color ResolvePlaceholderEnemyColor(EnemyDefinition definition)
    {
        string family = SpellRegistry.NormalizeWord(definition != null ? definition.EffectiveCreatureFamilyNoun : "");
        return family switch
        {
            "RAT" => new Color(0.55f, 0.52f, 0.48f, 1f),
            "CAT" => new Color(0.95f, 0.62f, 0.32f, 1f),
            "DOG" => new Color(0.55f, 0.36f, 0.22f, 1f),
            "BUG" => new Color(0.24f, 0.65f, 0.28f, 1f),
            "BIRD" => new Color(0.32f, 0.56f, 0.95f, 1f),
            "FISH" => new Color(0.18f, 0.78f, 0.9f, 1f),
            "HORSE" => new Color(0.44f, 0.28f, 0.14f, 1f),
            "COW" => new Color(0.88f, 0.86f, 0.78f, 1f),
            _ => new Color(0.72f, 0.5f, 0.86f, 1f),
        };
    }

    static bool TryPlaceAgentOnNavMesh(NavMeshAgent agent, Vector3 spawnPosition, out Vector3 placedPosition)
    {
        placedPosition = spawnPosition;
        if (agent == null)
            return false;

        if (!agent.enabled)
            agent.enabled = true;

        float sampleRadius = Mathf.Max(1f, agent.radius * 4f);
        if (NavMesh.SamplePosition(spawnPosition, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
            placedPosition = hit.position;

        return agent.Warp(placedPosition) || agent.isOnNavMesh;
    }

    static void AlignAgentBaseToRenderedGround(GameObject instance, NavMeshAgent agent, Vector3 groundPosition)
    {
        if (instance == null || agent == null)
            return;

        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return;

        bool hasBounds = false;
        Bounds combinedBounds = default;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            if (!hasBounds)
            {
                combinedBounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                combinedBounds.Encapsulate(renderer.bounds);
            }
        }

        if (!hasBounds)
            return;

        float sinkDepth = groundPosition.y - combinedBounds.min.y;
        if (sinkDepth > 0.001f)
        {
            agent.baseOffset = Mathf.Max(agent.baseOffset, sinkDepth);
            if (agent.isOnNavMesh)
                agent.Warp(groundPosition);
        }
    }

    bool TryFindSpawnPoint(Vector3 anchor, out Vector3 spawnPosition)
    {
        if (!TrySampleNavMeshPoint(anchor, navMeshSampleRadius * 1.5f, out Vector3 navAnchor))
        {
            spawnPosition = default;
            return false;
        }

        for (int pass = 0; pass < 2; pass++)
        {
            for (int attempt = 0; attempt < Mathf.Max(8, spawnAttemptsPerEnemy); attempt++)
            {
                Vector2 direction = Random.insideUnitCircle.normalized;
                Vector3 candidate = navAnchor + new Vector3(direction.x, 0f, direction.y) *
                    Random.Range(minSpawnDistance, maxSpawnDistance);
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, navMeshSampleRadius, NavMesh.AllAreas))
                    continue;
                if (!HasUsablePath(hit.position, navAnchor))
                    continue;
                if (pass == 0 && avoidVisibleSpawnPoints && IsVisibleFromMainCamera(hit.position))
                    continue;
                spawnPosition = hit.position;
                return true;
            }
        }

        spawnPosition = default;
        return false;
    }

    static bool IsVisibleFromMainCamera(Vector3 position)
    {
        Camera camera = Camera.main;
        if (camera == null)
            return false;
        Vector3 viewport = camera.WorldToViewportPoint(position + Vector3.up);
        return viewport.z > 0f && viewport.x > 0f && viewport.x < 1f && viewport.y > 0f && viewport.y < 1f;
    }

    static bool HasUsablePath(Vector3 from, Vector3 to)
    {
        var path = new NavMeshPath();
        return NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
    }

    static bool TrySampleNavMeshPoint(Vector3 source, float radius, out Vector3 point)
    {
        if (NavMesh.SamplePosition(source, out NavMeshHit hit, radius, NavMesh.AllAreas))
        {
            point = hit.position;
            return true;
        }
        point = default;
        return false;
    }
}
