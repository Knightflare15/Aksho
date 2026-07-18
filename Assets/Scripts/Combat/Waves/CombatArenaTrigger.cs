using UnityEngine;

[RequireComponent(typeof(Collider))]
public class CombatArenaTrigger : MonoBehaviour
{
    public EnemyWaveDirector waveDirector;
    public bool triggerOnce = true;

    private bool _hasTriggered;

    void Reset()
    {
        var colliderComponent = GetComponent<Collider>();
        if (colliderComponent != null)
            colliderComponent.isTrigger = true;
    }

    void Awake()
    {
        if (waveDirector == null)
            waveDirector = FindAnyObjectByType<EnemyWaveDirector>();

        var colliderComponent = GetComponent<Collider>();
        if (colliderComponent != null)
            colliderComponent.isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (_hasTriggered && triggerOnce)
            return;

        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null)
            return;

        if (waveDirector != null)
            waveDirector.ActivateArena();

        _hasTriggered = true;
    }
}
