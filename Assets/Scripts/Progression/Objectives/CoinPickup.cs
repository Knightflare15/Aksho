using UnityEngine;

[DisallowMultipleComponent]
/// <summary>
/// Coin pickup used for enemy drops and exploration rewards.
///
/// Prefab checklist:
/// - Put this component on the prefab root.
/// - Include any visible child mesh. Its original materials are preserved.
/// - A root trigger sphere is created automatically so child mesh colliders are not required.
/// </summary>
public class CoinPickup : MonoBehaviour
{
    [Tooltip("Optional manual pickup collider. A root trigger sphere is added when this is empty or belongs to a child.")]
    public Collider pickupCollider;
    [Tooltip("Optional renderer used to normalize custom coin visuals. Its materials are never replaced.")]
    public Renderer visualRenderer;
    public int value = 1;
    public float magnetSpeed = 10f;
    [Tooltip("Automatically center and resize child FBX visuals whose imported transforms are unsuitable for gameplay.")]
    public bool normalizeCustomVisual = true;
    [Min(0.05f)]
    public float customVisualDiameter = 0.55f;

    private Transform target;
    private bool visualNormalized;

    public static CoinPickup Create(Vector3 position, int value)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = $"Coin_{value}";
        go.transform.position = position + Vector3.up * 0.55f;
        go.transform.localScale = Vector3.one * 0.38f;

        var collider = go.GetComponent<Collider>();
        if (collider != null)
            collider.isTrigger = true;

        var renderer = go.GetComponent<Renderer>();
        if (renderer != null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            renderer.material = new Material(shader);
            renderer.material.color = new Color(1f, 0.82f, 0.22f, 1f);
        }

        var pickup = go.AddComponent<CoinPickup>();
        pickup.value = Mathf.Max(1, value);
        return pickup;
    }

    public void Initialize(int coinValue)
    {
        value = Mathf.Max(1, coinValue);
        EnsureSetup();
    }

    void Awake()
    {
        EnsureSetup();
    }

    void Update()
    {
        if (target == null)
        {
            AcquireTarget();
            return;
        }

        transform.position = Vector3.MoveTowards(
            transform.position,
            target.position + Vector3.up * 0.9f,
            magnetSpeed * Time.deltaTime);
    }

    void OnTriggerEnter(Collider other)
    {
        TryCollect(other);
    }

    public void HandleTrigger(Collider other)
    {
        TryCollect(other);
    }

    void TryCollect(Collider other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null)
            return;

        WorldEconomyService.EnsureExists().AddCoins(value);
        Destroy(gameObject);
    }

    void AcquireTarget()
    {
        PlayerController player = FindAnyObjectByType<PlayerController>();
        if (player == null)
            return;

        const float radius = 2.3f;

        if (Vector3.Distance(transform.position, player.transform.position) <= radius)
            target = player.transform;
    }

    void EnsureSetup()
    {
        if (pickupCollider == null || pickupCollider.gameObject != gameObject)
            pickupCollider = GetComponent<SphereCollider>();
        if (pickupCollider == null)
        {
            SphereCollider sphere = gameObject.AddComponent<SphereCollider>();
            sphere.radius = 0.55f;
            pickupCollider = sphere;
        }
        pickupCollider.isTrigger = true;

        if (visualRenderer == null)
            visualRenderer = GetComponentInChildren<Renderer>(true);

        NormalizeCustomVisual();

        Rigidbody body = GetComponent<Rigidbody>();
        if (body == null)
            body = gameObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
    }

    void NormalizeCustomVisual()
    {
        if (visualNormalized || !normalizeCustomVisual || visualRenderer == null || visualRenderer.transform == transform)
            return;

        Transform visualRoot = visualRenderer.transform;
        while (visualRoot.parent != null && visualRoot.parent != transform)
            visualRoot = visualRoot.parent;

        visualRoot.localPosition = Vector3.zero;

        Renderer[] visualRenderers = visualRoot.GetComponentsInChildren<Renderer>(true);
        if (visualRenderers.Length == 0)
            return;

        Bounds bounds = visualRenderers[0].bounds;
        for (int i = 1; i < visualRenderers.Length; i++)
            bounds.Encapsulate(visualRenderers[i].bounds);

        float largestDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (largestDimension > 0.001f)
        {
            float scale = customVisualDiameter / largestDimension;
            visualRoot.localScale *= scale;
        }

        visualNormalized = true;
    }
}
