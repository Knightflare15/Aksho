using UnityEngine;

[RequireComponent(typeof(PlayerHealth))]
public class PlayerHurtbox : MonoBehaviour
{
    public Collider triggerCollider;
    public PlayerHealth playerHealth;
    public CombatActor combatActor;
    public Vector3 triggerCenter = new Vector3(0f, 0.9f, 0f);
    public float triggerRadius = 0.55f;
    public float triggerHeight = 1.75f;
    public bool showDebugGizmos = true;

    public PlayerHealth HealthTarget => playerHealth;
    public Collider TriggerCollider => triggerCollider;

    void Awake()
    {
        playerHealth = playerHealth != null ? playerHealth : GetComponent<PlayerHealth>();
        EnsureTriggerCollider();
        Debug.Log($"[PlayerHurtbox] Ready on '{name}' using trigger '{triggerCollider?.name}'.");
    }

    void OnValidate()
    {
        if (Application.isPlaying)
            return;

        playerHealth = playerHealth != null ? playerHealth : GetComponent<PlayerHealth>();
        EnsureTriggerCollider();
    }

    void EnsureTriggerCollider()
    {
        combatActor = combatActor != null ? combatActor : GetComponent<CombatActor>();
        if (combatActor == null)
            combatActor = gameObject.AddComponent<CombatActor>();
        combatActor.team = CombatTeam.Player;
        combatActor.playerHealth = playerHealth;

        if (triggerCollider == null)
        {
            Transform existing = transform.Find("PlayerHurtboxTrigger");
            if (existing != null)
                triggerCollider = existing.GetComponent<Collider>();
        }

        GameObject triggerObject;
        if (triggerCollider == null)
        {
            triggerObject = new GameObject("PlayerHurtboxTrigger");
            triggerObject.transform.SetParent(transform, false);
            triggerCollider = triggerObject.AddComponent<CapsuleCollider>();
        }
        else
        {
            triggerObject = triggerCollider.gameObject;
            if (triggerObject.transform.parent != transform)
                triggerObject.transform.SetParent(transform, false);
        }

        triggerObject.layer = gameObject.layer;

        var capsule = triggerCollider as CapsuleCollider;
        if (capsule == null)
        {
            if (triggerCollider != null)
                DestroyImmediate(triggerCollider);

            capsule = triggerObject.AddComponent<CapsuleCollider>();
            triggerCollider = capsule;
        }

        capsule.isTrigger = true;
        capsule.center = triggerCenter;
        capsule.radius = Mathf.Max(0.15f, triggerRadius);
        capsule.height = Mathf.Max(triggerHeight, capsule.radius * 2f);
        capsule.direction = 1;

        Rigidbody rb = triggerObject.GetComponent<Rigidbody>();
        if (rb == null)
            rb = triggerObject.AddComponent<Rigidbody>();

        rb.isKinematic = true;
        rb.useGravity = false;
        rb.constraints = RigidbodyConstraints.FreezeAll;

        CombatHurtbox hurtbox = triggerObject.GetComponent<CombatHurtbox>();
        if (hurtbox == null)
            hurtbox = triggerObject.AddComponent<CombatHurtbox>();
        hurtbox.owner = combatActor;
    }

    void OnDrawGizmosSelected()
    {
        if (!showDebugGizmos)
            return;

        CapsuleCollider capsule = triggerCollider as CapsuleCollider;
        if (capsule == null)
            return;

        Gizmos.color = new Color(0.25f, 0.9f, 0.8f, 0.28f);
        Matrix4x4 old = Gizmos.matrix;
        Gizmos.matrix = capsule.transform.localToWorldMatrix;
        Gizmos.DrawWireCube(capsule.center, new Vector3(capsule.radius * 2f, capsule.height, capsule.radius * 2f));
        Gizmos.matrix = old;
    }
}
