using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class CombatHitbox : MonoBehaviour
{
    public string hitboxId = "Melee";
    public CombatActor owner;
    public int damage = 1;
    public bool activeOnStart;
    public bool hitEachTargetOncePerActivation = true;
    public bool showDebugGizmos = true;

    private readonly HashSet<CombatActor> hitActors = new HashSet<CombatActor>();
    private Collider hitCollider;
    private bool isActive;

    public bool IsActive => isActive;

    void Awake()
    {
        Resolve();
        SetActive(activeOnStart);
    }

    void OnValidate()
    {
        Resolve();
    }

    public void Resolve()
    {
        hitCollider = GetComponent<Collider>();
        if (hitCollider != null)
            hitCollider.isTrigger = true;

        owner = owner != null ? owner : GetComponentInParent<CombatActor>();
    }

    public void SetDamage(int value)
    {
        damage = Mathf.Max(0, value);
    }

    public void Open()
    {
        hitActors.Clear();
        SetActive(true);
    }

    public void Close()
    {
        SetActive(false);
    }

    void SetActive(bool value)
    {
        isActive = value;
        if (hitCollider == null)
            hitCollider = GetComponent<Collider>();
        if (hitCollider != null)
            hitCollider.enabled = value;
    }

    void OnTriggerEnter(Collider other)
    {
        TryHit(other);
    }

    void OnTriggerStay(Collider other)
    {
        TryHit(other);
    }

    void TryHit(Collider other)
    {
        if (!isActive || other == null)
            return;

        CombatActor target = ResolveTargetActor(other);
        if (target == null || target == owner || !target.IsAlive)
            return;

        if (owner != null && target.team == owner.team && target.team != CombatTeam.Neutral)
            return;

        if (hitEachTargetOncePerActivation && hitActors.Contains(target))
            return;

        string source = owner != null ? owner.gameObject.name : gameObject.name;
        Vector3 hitOrigin = owner != null ? owner.transform.position : transform.position;
        if (target.ReceiveDamage(damage, source, hitOrigin))
            hitActors.Add(target);
    }

    CombatActor ResolveTargetActor(Collider other)
    {
        CombatHurtbox combatHurtbox = other.GetComponent<CombatHurtbox>() ?? other.GetComponentInParent<CombatHurtbox>();
        if (combatHurtbox != null && combatHurtbox.owner != null)
            return combatHurtbox.owner;

        PlayerHurtbox playerHurtbox = other.GetComponent<PlayerHurtbox>() ?? other.GetComponentInParent<PlayerHurtbox>();
        if (playerHurtbox != null && playerHurtbox.HealthTarget != null)
        {
            CombatActor actor = playerHurtbox.GetComponent<CombatActor>() ?? playerHurtbox.GetComponentInParent<CombatActor>();
            if (actor != null)
                return actor;
        }

        return other.GetComponent<CombatActor>() ?? other.GetComponentInParent<CombatActor>();
    }

    void OnDrawGizmosSelected()
    {
        if (!showDebugGizmos)
            return;

        Collider collider = hitCollider != null ? hitCollider : GetComponent<Collider>();
        if (collider == null)
            return;

        Gizmos.color = isActive ? new Color(1f, 0.3f, 0.2f, 0.42f) : new Color(1f, 0.3f, 0.2f, 0.18f);
        Gizmos.DrawWireCube(collider.bounds.center, collider.bounds.size);
    }
}
