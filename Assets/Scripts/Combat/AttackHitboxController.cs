using System.Collections.Generic;
using UnityEngine;

public class AttackHitboxController : MonoBehaviour
{
    public List<CombatHitbox> hitboxes = new List<CombatHitbox>();

    private readonly Dictionary<string, List<CombatHitbox>> hitboxesById = new Dictionary<string, List<CombatHitbox>>();

    void Awake()
    {
        RefreshHitboxes();
        CloseAllHitboxes();
    }

    void OnValidate()
    {
        RefreshHitboxes();
    }

    public void RefreshHitboxes()
    {
        hitboxes.RemoveAll(hitbox => hitbox == null);
        foreach (CombatHitbox hitbox in GetComponentsInChildren<CombatHitbox>(true))
        {
            if (hitbox != null && !hitboxes.Contains(hitbox))
                hitboxes.Add(hitbox);
        }

        hitboxesById.Clear();
        foreach (CombatHitbox hitbox in hitboxes)
        {
            if (hitbox == null)
                continue;

            string id = NormalizeId(hitbox.hitboxId);
            if (!hitboxesById.TryGetValue(id, out List<CombatHitbox> group))
            {
                group = new List<CombatHitbox>();
                hitboxesById[id] = group;
            }

            group.Add(hitbox);
        }
    }

    public bool HasHitbox(string hitboxId)
    {
        RefreshHitboxes();
        return hitboxesById.ContainsKey(NormalizeId(hitboxId));
    }

    public void SetHitboxDamage(string hitboxId, int damage)
    {
        foreach (CombatHitbox hitbox in GetHitboxes(hitboxId))
            hitbox.SetDamage(damage);
    }

    public void OpenHitbox(string hitboxId)
    {
        foreach (CombatHitbox hitbox in GetHitboxes(hitboxId))
            hitbox.Open();
    }

    public void CloseHitbox(string hitboxId)
    {
        foreach (CombatHitbox hitbox in GetHitboxes(hitboxId))
            hitbox.Close();
    }

    public void CloseAllHitboxes()
    {
        foreach (CombatHitbox hitbox in hitboxes)
        {
            if (hitbox != null)
                hitbox.Close();
        }
    }

    List<CombatHitbox> GetHitboxes(string hitboxId)
    {
        RefreshHitboxes();
        if (hitboxesById.TryGetValue(NormalizeId(hitboxId), out List<CombatHitbox> group))
            return group;

        return new List<CombatHitbox>();
    }

    static string NormalizeId(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "MELEE" : value.Trim().ToUpperInvariant();
    }
}
