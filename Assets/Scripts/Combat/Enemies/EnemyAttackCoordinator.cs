using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class EnemyAttackCoordinator : MonoBehaviour
{
    [Header("Attack Turns")]
    [Min(1)] public int maxConcurrentAttackers = 1;
    [Min(1)] public int crowdConcurrentAttackers = 2;
    [Min(1)] public int crowdThreshold = 5;
    [Min(0f)] public float attackGapSeconds = 0.65f;
    [Min(0f)] public float sameEnemyLockoutSeconds = 1.1f;

    [Header("Support Positioning")]
    [Min(0f)] public float supportRingExtraDistance = 1.25f;
    [Min(0.05f)] public float supportRepositionInterval = 0.35f;

    readonly List<CheeseEnemyAgent> agents = new List<CheeseEnemyAgent>();
    readonly HashSet<CheeseEnemyAgent> activeAttackers = new HashSet<CheeseEnemyAgent>();
    readonly Dictionary<CheeseEnemyAgent, float> nextEligibleAttackAt = new Dictionary<CheeseEnemyAgent, float>();

    float nextGroupAttackAt;

    public int RegisteredAgentCount
    {
        get
        {
            CleanupRoster();
            return agents.Count;
        }
    }

    public int ActiveAttackersCount
    {
        get
        {
            CleanupRoster();
            return activeAttackers.Count;
        }
    }

    public void RegisterAgent(CheeseEnemyAgent agent)
    {
        if (agent == null || agents.Contains(agent))
            return;

        agents.Add(agent);
    }

    public void UnregisterAgent(CheeseEnemyAgent agent)
    {
        if (agent == null)
            return;

        ReleaseAttack(agent);
        agents.Remove(agent);
        nextEligibleAttackAt.Remove(agent);
    }

    public bool TryRequestAttack(CheeseEnemyAgent agent)
    {
        if (agent == null || !agent.isActiveAndEnabled)
            return false;

        CleanupRoster();
        RegisterAgent(agent);

        if (activeAttackers.Contains(agent))
            return true;

        if (activeAttackers.Count >= GetAllowedAttackers())
            return false;

        float now = Time.time;
        if (now < nextGroupAttackAt)
            return false;

        if (nextEligibleAttackAt.TryGetValue(agent, out float nextEligibleAt) && now < nextEligibleAt)
            return false;

        activeAttackers.Add(agent);
        nextGroupAttackAt = now + Mathf.Max(0f, attackGapSeconds);
        return true;
    }

    public void ReleaseAttack(CheeseEnemyAgent agent)
    {
        if (agent == null)
            return;

        if (!activeAttackers.Remove(agent))
            return;

        float now = Time.time;
        nextEligibleAttackAt[agent] = now + Mathf.Max(0f, sameEnemyLockoutSeconds);
        nextGroupAttackAt = Mathf.Max(nextGroupAttackAt, now + Mathf.Max(0f, attackGapSeconds));
    }

    public bool TryGetSupportPosition(CheeseEnemyAgent agent, Vector3 playerPosition, float holdRange, out Vector3 supportPosition)
    {
        supportPosition = default;
        if (agent == null)
            return false;

        CleanupRoster();
        RegisterAgent(agent);

        int index = agents.IndexOf(agent);
        if (index < 0)
            return false;

        float radius = Mathf.Max(0.1f, holdRange) + Mathf.Max(0f, supportRingExtraDistance);
        float angle = index * 137.50776f * Mathf.Deg2Rad;
        Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
        Vector3 candidate = playerPosition + offset;

        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, Mathf.Max(1f, supportRingExtraDistance + 1f), NavMesh.AllAreas))
            supportPosition = hit.position;
        else
            supportPosition = candidate;

        return true;
    }

    int GetAllowedAttackers()
    {
        int singleLimit = Mathf.Max(1, maxConcurrentAttackers);
        if (agents.Count >= Mathf.Max(1, crowdThreshold))
            return Mathf.Max(singleLimit, crowdConcurrentAttackers);

        return singleLimit;
    }

    void CleanupRoster()
    {
        for (int i = agents.Count - 1; i >= 0; i--)
        {
            CheeseEnemyAgent agent = agents[i];
            if (agent != null && agent.isActiveAndEnabled)
                continue;

            if (agent != null)
            {
                activeAttackers.Remove(agent);
                nextEligibleAttackAt.Remove(agent);
            }

            agents.RemoveAt(i);
        }

        activeAttackers.RemoveWhere(agent => agent == null || !agent.isActiveAndEnabled || !agents.Contains(agent));
    }
}
