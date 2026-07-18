using UnityEngine;

public sealed class CompanionFollower : MonoBehaviour
{
    public Transform target;
    public Vector3 followOffset = new Vector3(1.1f, 1.25f, -0.75f);
    public float followSharpness = 9f;
    public float bobHeight = 0.12f;
    public float bobSpeed = 2.7f;

    void LateUpdate()
    {
        if (target == null)
            return;

        Vector3 desired = target.TransformPoint(followOffset);
        desired.y += Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        float t = 1f - Mathf.Exp(-Mathf.Max(0.1f, followSharpness) * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position == Vector3.zero ? desired : transform.position, desired, t);
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(target.forward, Vector3.up), t);
    }
}
