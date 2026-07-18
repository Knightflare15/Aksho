using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpellTarget : MonoBehaviour
{
    [Header("References")]
    public Animator animator;
    public Collider targetCollider;
    public GameObject rootToDisable;

    [Header("Weakness")]
    public string requiredSpell = "CAT";
    public List<string> additionalAcceptedSpells = new List<string>();

    [Header("Creature Family")]
    [Tooltip("Canonical noun family that can affect this enemy in creature combat. Defaults to Required Spell for compatibility.")]
    public string requiredCreatureNoun = "";
    public List<string> additionalAcceptedCreatureNouns = new List<string>();

    [Header("Durability")]
    [Min(1)] public int maxHp = 3;
    public int hitsToDefeat = 1;
    public string hitTrigger = "Hit";
    public string defeatTrigger = "Defeat";
    public float hideDelaySeconds = 0.75f;

    private bool defeated;
    private int hitsTaken;
    private bool durabilityInitialized;
    private int currentHp;
    private SpellRegistry spellRegistry;

    public bool IsDefeated => defeated;
    public int CurrentHp
    {
        get
        {
            EnsureDurabilityInitialized();
            return currentHp;
        }
        private set => currentHp = value;
    }
    public int HitsRemaining => Mathf.Max(0, CurrentHp);
    public string RequiredSpell => requiredSpell;
    public string RequiredCreatureNoun => string.IsNullOrWhiteSpace(requiredCreatureNoun)
        ? requiredSpell
        : requiredCreatureNoun;

    public event System.Action<SpellTarget> OnDefeated;

    protected virtual void Awake()
    {
        if (rootToDisable == null)
            rootToDisable = gameObject;

        spellRegistry = FindAnyObjectByType<SpellRegistry>();
        ResetDurability();
    }

    protected virtual void OnEnable()
    {
        ResetDurability();
    }

    void ResetDurability()
    {
        defeated = false;
        hitsTaken = 0;
        CurrentHp = Mathf.Max(1, maxHp);
        durabilityInitialized = true;
        if (targetCollider != null)
            targetCollider.enabled = true;
    }

    void EnsureDurabilityInitialized()
    {
        if (!durabilityInitialized)
            ResetDurability();
    }

    public virtual Vector3 GetAimPoint()
    {
        if (targetCollider != null)
            return targetCollider.bounds.center;

        return transform.position + Vector3.up * 0.75f;
    }

    public bool IsWeakTo(string spellWord)
    {
        if (string.IsNullOrWhiteSpace(spellWord))
            return false;

        return string.Equals(
            SpellRegistry.NormalizeWord(spellWord),
            SpellRegistry.NormalizeWord(requiredSpell),
            System.StringComparison.OrdinalIgnoreCase);
    }

    public bool IsMatchingCreatureNoun(string noun)
    {
        string normalized = SpellRegistry.NormalizeWord(noun);
        if (string.IsNullOrEmpty(normalized))
            return false;
        string canonical = CreatureCombatRegistry.ResolveCanonicalNounInScene(normalized);
        string requiredCanonical = CreatureCombatRegistry.ResolveCanonicalNounInScene(RequiredCreatureNoun);

        if (string.Equals(
                canonical,
                requiredCanonical,
                System.StringComparison.OrdinalIgnoreCase))
            return true;

        if (additionalAcceptedCreatureNouns != null)
        {
            foreach (string accepted in additionalAcceptedCreatureNouns)
            {
                if (string.Equals(
                        canonical,
                        CreatureCombatRegistry.ResolveCanonicalNounInScene(accepted),
                        System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        if (additionalAcceptedSpells != null)
        {
            foreach (string accepted in additionalAcceptedSpells)
            {
                if (string.Equals(
                        normalized,
                        SpellRegistry.NormalizeWord(accepted),
                        System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    public virtual bool CanBeDamagedBy(string spellWord)
    {
        return GetDamageForSpell(spellWord) > 0;
    }

    public virtual int GetDamageForSpell(string spellWord)
    {
        string normalized = SpellRegistry.NormalizeWord(spellWord);
        if (string.IsNullOrEmpty(normalized))
            return 0;

        if (IsWeakTo(normalized))
            return GetStrongSpellDamage();

        if (IsAdditionalAcceptedSpell(normalized))
            return GetStrongSpellDamage();

        return IsRegisteredSpell(normalized) ? 1 : 0;
    }

    int GetStrongSpellDamage()
    {
        int defeatHits = Mathf.Max(1, hitsToDefeat);
        return Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(1, maxHp) / (float)defeatHits));
    }

    bool IsAdditionalAcceptedSpell(string normalizedSpell)
    {
        foreach (string accepted in additionalAcceptedSpells)
        {
            if (string.Equals(normalizedSpell, SpellRegistry.NormalizeWord(accepted), System.StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    bool IsRegisteredSpell(string normalizedSpell)
    {
        if (spellRegistry == null)
            spellRegistry = FindAnyObjectByType<SpellRegistry>();

        return spellRegistry != null && spellRegistry.HasSpell(normalizedSpell);
    }

    public virtual bool ReceiveHit(string spellWord)
    {
        EnsureDurabilityInitialized();
        if (defeated)
            return false;

        int damage = GetDamageForSpell(spellWord);
        if (damage <= 0)
            return false;

        return ApplyDamage(damage);
    }

    public virtual bool ReceiveDirectDamage(int damage, string damageSource = null)
    {
        EnsureDurabilityInitialized();
        if (defeated || damage <= 0)
            return false;

        return ApplyDamage(damage);
    }

    public virtual bool ReceiveCreatureAction(string creatureNoun, string verb, int damage)
    {
        EnsureDurabilityInitialized();
        if (defeated || damage <= 0 || !IsMatchingCreatureNoun(creatureNoun))
            return false;

        return ApplyDamage(damage);
    }

    bool ApplyDamage(int damage)
    {
        hitsTaken++;
        CurrentHp = Mathf.Max(0, CurrentHp - damage);

        if (CurrentHp <= 0)
        {
            defeated = true;
            OnDefeated?.Invoke(this);

            if (targetCollider != null)
                targetCollider.enabled = false;

            if (animator != null && !string.IsNullOrEmpty(defeatTrigger))
                animator.SetTrigger(defeatTrigger);

            StartCoroutine(HideAfterDelay());
        }
        else if (animator != null && !string.IsNullOrEmpty(hitTrigger))
        {
            animator.SetTrigger(hitTrigger);
        }

        return true;
    }

    public bool Defeat(string spellWord)
    {
        CurrentHp = Mathf.Min(CurrentHp, Mathf.Max(1, GetDamageForSpell(spellWord)));
        return ReceiveHit(spellWord);
    }

    private IEnumerator HideAfterDelay()
    {
        yield return new WaitForSeconds(hideDelaySeconds);

        if (rootToDisable != null)
            rootToDisable.SetActive(false);
    }
}
