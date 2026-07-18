using UnityEngine;

[RequireComponent(typeof(Collider))]
public class CombatHurtbox : MonoBehaviour
{
    public CombatActor owner;
    public bool showDebugGizmos = true;

    public Collider Collider { get; private set; }

    void Awake()
    {
        Resolve();
    }

    void OnValidate()
    {
        Resolve();
    }

    public void Resolve()
    {
        Collider = GetComponent<Collider>();
        if (Collider != null)
            Collider.isTrigger = true;

        owner = owner != null ? owner : GetComponentInParent<CombatActor>();
    }

    void OnDrawGizmosSelected()
    {
        if (!showDebugGizmos)
            return;

        Collider collider = Collider != null ? Collider : GetComponent<Collider>();
        if (collider == null)
            return;

        Gizmos.color = new Color(0.25f, 0.9f, 0.8f, 0.28f);
        Gizmos.DrawWireCube(collider.bounds.center, collider.bounds.size);
    }
}
