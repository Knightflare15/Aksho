using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

[DisallowMultipleComponent]
public sealed class EnemyWaveTrigger : MonoBehaviour
{
    public float triggerRadius = 5f;
    public int enemyCount = 4;
    public SemanticZoneKind semanticZoneKind = SemanticZoneKind.Route;
    public string grammarTopic = "Route practice";
    [Min(1)] public int grammarTopicTier = 1;
    public TranslatorAssistMode translatorAssist = TranslatorAssistMode.Partial;
    public List<string> encounterNounFamilies = new List<string>();
    public List<GrammarPhrasePattern> practicePatterns = new List<GrammarPhrasePattern>();
    public List<string> masteryTags = new List<string>();
    public bool drawDebugGizmo = true;

    bool consumed;
    public bool Consumed => consumed;

    void OnTriggerEnter(Collider other)
    {
        if (consumed)
            return;
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null)
            return;

        TranslatorBuddyService buddy = FindAnyObjectByType<TranslatorBuddyService>();
        if (buddy != null)
            buddy.SetAssistMode(translatorAssist);

        EnemyWaveDirector director = FindAnyObjectByType<EnemyWaveDirector>();
        if (director == null)
        {
            Debug.LogWarning("[EnemyWaveTrigger] No EnemyWaveDirector found; generated trigger cannot start an encounter.", this);
            return;
        }

        NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
        if (triangulation.vertices == null || triangulation.vertices.Length == 0)
        {
            Debug.LogWarning("[EnemyWaveTrigger] Encounter trigger reached, but enemy spawning still requires a scene NavMesh in this version.", this);
            return;
        }

        CreatureCombatRegistry registry = FindAnyObjectByType<CreatureCombatRegistry>();
        List<string> resolvedNouns = GrammarRouteContext.Instance.ResolveEncounterNounFamilies(
            registry,
            semanticZoneKind,
            grammarTopic,
            grammarTopicTier,
            encounterNounFamilies);

        if (director.RequestZoneEncounter(
                transform.position,
                enemyCount,
                semanticZoneKind,
                grammarTopic,
                resolvedNouns,
                grammarTopicTier,
                practicePatterns,
                masteryTags))
        {
            consumed = true;
        }
        else
        {
            Debug.LogWarning("[EnemyWaveTrigger] EnemyWaveDirector declined optional generated encounter.", this);
        }
    }

    void OnDrawGizmosSelected()
    {
        if (!drawDebugGizmo)
            return;
        Gizmos.color = new Color(1f, 0.15f, 0.08f, 0.32f);
        Gizmos.DrawSphere(transform.position, triggerRadius);
    }
}
