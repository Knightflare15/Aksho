using UnityEngine;

[RequireComponent(typeof(PlayerController))]
public class PlayerHealth : MonoBehaviour
{
    public int maxHp = 6;
    public float damageInvulnerabilitySeconds = 0.75f;

    private float _lastDamageAt = -999f;
    private int baseMaxHp;
    private int currentHp;
    private bool healthInitialized;

    public int CurrentHp
    {
        get
        {
            EnsureHealthInitialized();
            return currentHp;
        }
        private set
        {
            currentHp = value;
            healthInitialized = true;
        }
    }
    public bool IsDead => CurrentHp <= 0;
    public string LastDamageSource { get; private set; } = "";

    public event System.Action<int, int> OnHealthChanged;
    public event System.Action OnDied;

    void Awake()
    {
        InitializeHealth(restoreToFull: true);
    }

    void OnEnable()
    {
        EnsureHealthInitialized();
        ApplyWorldHealth(restoreToFull: false);
    }

    void OnDisable()
    {
    }

    void EnsureHealthInitialized()
    {
        if (!healthInitialized)
            InitializeHealth(restoreToFull: true);
    }

    void InitializeHealth(bool restoreToFull)
    {
        baseMaxHp = Mathf.Max(1, maxHp);
        healthInitialized = true;
        ApplyWorldHealth(restoreToFull);
    }

    public bool TakeDamage(int amount, string damageSource = null)
    {
        return TakeDamage(amount, damageSource, null);
    }

    public bool TakeDamage(int amount, string damageSource, Vector3? hitOrigin)
    {
        if (amount <= 0 || Time.time < _lastDamageAt + damageInvulnerabilitySeconds || IsDead)
            return false;

        int finalAmount = amount;
        string mitigationMessage = "";
        CreatureCombatController creatureCombat = GetComponent<CreatureCombatController>() ??
            GetComponentInChildren<CreatureCombatController>(true) ??
            FindAnyObjectByType<CreatureCombatController>();
        bool mitigated = creatureCombat != null &&
            creatureCombat.TryMitigateIncomingDamage(amount, damageSource, out finalAmount, out mitigationMessage);

        _lastDamageAt = Time.time;
        LastDamageSource = string.IsNullOrWhiteSpace(damageSource) ? "Unknown" : damageSource;
        if (mitigated && finalAmount <= 0)
        {
            Debug.Log($"[PlayerHealth] Avoided {amount} damage from {LastDamageSource} with {mitigationMessage}.");
            return true;
        }

        CurrentHp = Mathf.Max(0, CurrentHp - finalAmount);
        OnHealthChanged?.Invoke(CurrentHp, maxHp);
        string mitigationSuffix = mitigated && !string.IsNullOrWhiteSpace(mitigationMessage)
            ? $" ({mitigationMessage})"
            : "";
        Debug.Log($"[PlayerHealth] Took {finalAmount} damage from {LastDamageSource}{mitigationSuffix}. HP {CurrentHp}/{maxHp}.");

        if (hitOrigin.HasValue)
            GetComponent<PlayerController>()?.ApplyKnockback(hitOrigin.Value);

        if (CurrentHp <= 0)
        {
            Debug.Log("[PlayerHealth] Player HP depleted.");
            OnDied?.Invoke();
        }

        return true;
    }

    public void RestoreFull()
    {
        ApplyWorldHealth(restoreToFull: false);
        CurrentHp = Mathf.Max(1, maxHp);
        LastDamageSource = "";
        OnHealthChanged?.Invoke(CurrentHp, maxHp);
        Debug.Log($"[PlayerHealth] Restored to full HP ({CurrentHp}/{maxHp}).");
    }

    void ApplyWorldHealth(bool restoreToFull)
    {
        if (baseMaxHp <= 0)
            baseMaxHp = Mathf.Max(1, maxHp);

        maxHp = Mathf.Clamp(baseMaxHp, 1, 12);
        if (restoreToFull)
        {
            CurrentHp = maxHp;
        }
        else
        {
            CurrentHp = Mathf.Clamp(CurrentHp, 0, maxHp);
        }
    }
}
