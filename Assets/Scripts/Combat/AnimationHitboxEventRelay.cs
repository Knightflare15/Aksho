using UnityEngine;

public class AnimationHitboxEventRelay : MonoBehaviour
{
    public AttackHitboxController hitboxController;

    void Awake()
    {
        Resolve();
    }

    void OnValidate()
    {
        Resolve();
    }

    void Resolve()
    {
        hitboxController = hitboxController != null
            ? hitboxController
            : GetComponent<AttackHitboxController>() ?? GetComponentInParent<AttackHitboxController>();
    }

    public void OpenHitbox(string hitboxId)
    {
        if (hitboxController == null)
            Resolve();
        hitboxController?.OpenHitbox(hitboxId);
    }

    public void CloseHitbox(string hitboxId)
    {
        if (hitboxController == null)
            Resolve();
        hitboxController?.CloseHitbox(hitboxId);
    }

    public void CloseAllHitboxes()
    {
        if (hitboxController == null)
            Resolve();
        hitboxController?.CloseAllHitboxes();
    }
}
