using UnityEngine;

public class EncounterLockZone : MonoBehaviour
{
    public float radius = 24f;
    public float warningDistance = 2f;
    public Transform player;

    public static EncounterLockZone Create(Vector3 center, float radius, Transform player)
    {
        GameObject go = new GameObject("EncounterLockZone");
        go.transform.position = center;
        EncounterLockZone zone = go.AddComponent<EncounterLockZone>();
        zone.radius = Mathf.Max(2f, radius);
        zone.player = player;
        return zone;
    }

    void LateUpdate()
    {
        if (player == null)
            return;

        Vector3 offset = player.position - transform.position;
        offset.y = 0f;
        if (offset.magnitude <= radius)
            return;

        Vector3 clamped = transform.position + offset.normalized * radius;
        clamped.y = player.position.y;
        player.position = clamped;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.15f, 0.28f);
        Gizmos.DrawSphere(transform.position, radius);
    }
}
