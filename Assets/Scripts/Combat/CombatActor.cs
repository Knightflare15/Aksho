using UnityEngine;

public class CombatActor : MonoBehaviour
{
    public CombatTeam team = CombatTeam.Enemy;
    public PlayerHealth playerHealth;
    public SpellTarget spellTarget;

    public bool IsAlive
    {
        get
        {
            if (playerHealth != null)
                return !playerHealth.IsDead;
            if (spellTarget != null)
                return !spellTarget.IsDefeated;
            return isActiveAndEnabled;
        }
    }

    void Awake()
    {
        ResolveReferences();
    }

    void OnValidate()
    {
        ResolveReferences();
    }

    public void ResolveReferences()
    {
        playerHealth = playerHealth != null ? playerHealth : GetComponent<PlayerHealth>() ?? GetComponentInParent<PlayerHealth>();
        spellTarget = spellTarget != null ? spellTarget : GetComponent<SpellTarget>() ?? GetComponentInParent<SpellTarget>();
        if (playerHealth != null)
            team = CombatTeam.Player;
        else if (spellTarget != null && team == CombatTeam.Neutral)
            team = CombatTeam.Enemy;
    }

    public bool ReceiveDamage(int amount, string source)
    {
        return ReceiveDamage(amount, source, null);
    }

    public bool ReceiveDamage(int amount, string source, Vector3? hitOrigin)
    {
        if (amount <= 0 || !IsAlive)
            return false;

        if (playerHealth != null)
            return playerHealth.TakeDamage(amount, source, hitOrigin);

        if (spellTarget != null)
            return spellTarget.ReceiveDirectDamage(amount, source);

        return false;
    }
}
