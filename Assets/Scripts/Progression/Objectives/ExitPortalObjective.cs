using UnityEngine;

[DisallowMultipleComponent]
/// <summary>
/// Escape trigger opened after all pillars are activated.
///
/// Prefab checklist:
/// - Put this component on the prefab root.
/// - Include any visible child mesh you want tinted cyan when open.
/// - Include a child collider if you want a custom trigger shape; otherwise one is auto-added.
/// </summary>
public class ExitPortalObjective : MonoBehaviour
{
    [Tooltip("Optional manual visual renderer. If empty, the first child renderer is used for the portal tint.")]
    public Renderer portalRendererOverride;
    [Tooltip("Optional manual trigger collider. If empty, the first child collider is used. If none exists, a trigger capsule is added.")]
    public Collider triggerCollider;
    [Tooltip("Fallback escape radius used if trigger enter is missed because the player is already inside the portal.")]
    public float proximityEscapeRadius = 2.2f;

    private Renderer portalRenderer;
    private PlayerController playerController;
    private bool isOpen;
    private bool hasEscaped;

    public event System.Action OnPlayerEscaped;

    public static ExitPortalObjective Create(Vector3 position)
    {
        var root = new GameObject("SpawnExitPortal");
        root.transform.position = position;

        var visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        visual.name = "ExitVisual";
        visual.transform.SetParent(root.transform, false);
        visual.transform.localPosition = Vector3.up * 0.05f;
        visual.transform.localScale = new Vector3(2.2f, 0.08f, 2.2f);

        var collider = visual.GetComponent<Collider>();
        if (collider != null)
            collider.isTrigger = true;

        var portal = root.AddComponent<ExitPortalObjective>();
        portal.portalRenderer = visual.GetComponent<Renderer>();
        portal.SetOpen(false);
        return portal;
    }

    public void Initialize()
    {
        if (portalRendererOverride == null)
            portalRendererOverride = GetComponentInChildren<Renderer>(true);
        portalRenderer = portalRendererOverride;

        if (triggerCollider == null)
            triggerCollider = GetComponentInChildren<Collider>(true);
        if (triggerCollider == null)
            triggerCollider = gameObject.AddComponent<CapsuleCollider>();
        triggerCollider.isTrigger = true;
        EnsureTriggerRelay();

        SetOpen(false);
    }

    void Awake()
    {
        Initialize();
    }

    public void SetOpen(bool open)
    {
        isOpen = open;
        hasEscaped = false;
        gameObject.SetActive(open);

        if (portalRenderer != null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            portalRenderer.material = new Material(shader);
            portalRenderer.material.color = new Color(0.35f, 0.95f, 1f, 0.85f);
        }
    }

    void Update()
    {
        if (!isOpen || hasEscaped)
            return;

        if (playerController == null)
            playerController = FindAnyObjectByType<PlayerController>();
        if (playerController == null)
            return;

        if (Vector3.Distance(transform.position, playerController.transform.position) <= proximityEscapeRadius)
            Escape(playerController);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!isOpen)
            return;

        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null)
            return;

        Escape(player);
    }

    public void HandleTrigger(Collider other)
    {
        OnTriggerEnter(other);
    }

    void Escape(PlayerController player)
    {
        if (hasEscaped)
            return;

        playerController = player;
        hasEscaped = true;
        OnPlayerEscaped?.Invoke();
    }

    void EnsureTriggerRelay()
    {
        if (triggerCollider == null || triggerCollider.gameObject == gameObject)
            return;

        ExitPortalTriggerRelay relay = triggerCollider.GetComponent<ExitPortalTriggerRelay>();
        if (relay == null)
            relay = triggerCollider.gameObject.AddComponent<ExitPortalTriggerRelay>();

        relay.owner = this;
    }
}

public class ExitPortalTriggerRelay : MonoBehaviour
{
    public ExitPortalObjective owner;

    void OnTriggerEnter(Collider other)
    {
        owner?.HandleTrigger(other);
    }
}
