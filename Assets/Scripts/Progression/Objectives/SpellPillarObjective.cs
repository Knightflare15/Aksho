using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
/// <summary>
/// Spell target for stage objectives.
///
/// Prefab checklist:
/// - Put this component on the prefab root.
/// - Include any visible child mesh you want tinted on active/inactive states.
/// - Include a child collider if you want precise hits; otherwise one is auto-added.
/// - Include a child TextMeshPro if you want custom word placement; otherwise one is auto-created.
/// </summary>
public class SpellPillarObjective : MonoBehaviour
{
    public enum PillarState
    {
        Dormant,
        Defending,
        Completed,
    }

    [Tooltip("Set by the director at runtime. Leaving this blank on the prefab is fine.")]
    public string requiredSpellWord = "CAT";
    [Tooltip("Director that owns this pillar's defense encounter. Assigned by LevelObjectiveDirector at runtime.")]
    public EnemyWaveDirector waveDirector;
    [Tooltip("Optional manual hit collider. If empty, the first child collider is used. If none exists, one is added to the root.")]
    public Collider hitCollider;
    [Tooltip("Optional renderer list for active/inactive tinting. If empty, all child renderers are used.")]
    public Renderer[] visualRenderers;
    [Tooltip("Optional custom word label. If empty, a child TextMeshPro is used. If none exists, one is created.")]
    public TextMeshPro labelOverride;

    [Header("Status Material")]
    [Tooltip("Only material slots with this name receive pillar-state tinting. Other slots, such as rock materials, remain unchanged.")]
    public string statusMaterialName = "Material.001";
    public Color dormantColor = new Color(0.1f, 0.9f, 1f, 1f);
    public Color defendingColor = new Color(1f, 0.18f, 0.12f, 1f);
    public Color completedColor = new Color(0.2f, 0.65f, 0.3f, 1f);
    [Min(0f)] public float dormantEmissionIntensity = 2f;
    [Min(0f)] public float defendingEmissionIntensity = 4f;
    [Min(0f)] public float completedEmissionIntensity = 0.8f;

    [Header("Idle Motion")]
    [Tooltip("Rotates authored ring children so custom pillar prefabs have a visible idle animation.")]
    public bool animateRings = true;
    public Vector3 ringRotationAxis = Vector3.forward;
    public float smallRingDegreesPerSecond = 38f;
    public float bigRingDegreesPerSecond = -24f;

    public PillarState State { get; private set; }
    public bool IsActivated => State == PillarState.Completed;
    public bool IsTargetable => State == PillarState.Dormant;

    static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
    static readonly int ColorProperty = Shader.PropertyToID("_Color");
    static readonly int EmissionColorProperty = Shader.PropertyToID("_EmissionColor");

    private Renderer[] renderers;
    private TextMeshPro label;
    private Transform smallRing;
    private Transform bigRing;
    private Animator importedModelAnimator;
    private bool tintAllMaterialSlots;
    private bool warnedMissingStatusMaterial;

    public event System.Action<SpellPillarObjective> OnActivated;

    public static SpellPillarObjective Create(Vector3 position, string spellWord)
    {
        var root = new GameObject($"Pillar_{SpellRegistry.NormalizeWord(spellWord)}");
        root.transform.position = position;

        var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        body.name = "Body";
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = Vector3.up * 1.1f;
        body.transform.localScale = new Vector3(0.8f, 1.1f, 0.8f);

        var trigger = body.GetComponent<Collider>();
        if (trigger != null)
            trigger.isTrigger = false;

        var labelGo = new GameObject("WordLabel", typeof(TextMeshPro));
        labelGo.transform.SetParent(root.transform, false);
        labelGo.transform.localPosition = Vector3.up * 7.3f;
        labelGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var text = labelGo.GetComponent<TextMeshPro>();
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = 4.2f;
        text.color = Color.white;
        text.text = SpellRegistry.NormalizeWord(spellWord);

        var pillar = root.AddComponent<SpellPillarObjective>();
        pillar.requiredSpellWord = SpellRegistry.NormalizeWord(spellWord);
        pillar.label = text;
        pillar.tintAllMaterialSlots = true;
        pillar.CacheRenderers();
        pillar.ApplyVisualState();
        return pillar;
    }

    public void Initialize(string spellWord, EnemyWaveDirector ownerWaveDirector = null)
    {
        requiredSpellWord = SpellRegistry.NormalizeWord(spellWord);
        if (ownerWaveDirector != null)
            waveDirector = ownerWaveDirector;
        EnsureSetup();
        ApplyVisualState();
    }

    void Awake()
    {
        EnsureSetup();
        requiredSpellWord = SpellRegistry.NormalizeWord(requiredSpellWord);
        ApplyVisualState();
    }

    void LateUpdate()
    {
        AnimateRings();

        if (label == null || Camera.main == null)
            return;

        Vector3 toCamera = label.transform.position - Camera.main.transform.position;
        if (toCamera.sqrMagnitude > 0.01f)
            label.transform.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
    }

    public bool TryActivate(string spellWord)
    {
        if (State != PillarState.Dormant)
            return false;

        string normalized = SpellRegistry.NormalizeWord(spellWord);
        if (!string.Equals(normalized, requiredSpellWord, System.StringComparison.OrdinalIgnoreCase))
            return false;

        EnemyWaveDirector director = waveDirector != null ? waveDirector : FindAnyObjectByType<EnemyWaveDirector>();
        return director != null && director.RequestPillarDefense(this);
    }

    public Vector3 GetAimPoint()
    {
        if (hitCollider != null)
            return hitCollider.bounds.center;

        return transform.position + Vector3.up * 2.2f;
    }

    public bool BeginDefense()
    {
        if (State != PillarState.Dormant)
            return false;

        State = PillarState.Defending;
        ApplyVisualState();
        return true;
    }

    public void CompleteDefense()
    {
        if (State == PillarState.Completed)
            return;

        State = PillarState.Completed;
        ApplyVisualState();
        OnActivated?.Invoke(this);
    }

    public void ResetDefense()
    {
        if (State != PillarState.Defending)
            return;

        State = PillarState.Dormant;
        ApplyVisualState();
    }

    void CacheRenderers()
    {
        renderers = visualRenderers != null && visualRenderers.Length > 0
            ? visualRenderers
            : GetComponentsInChildren<Renderer>(true);
    }

    void ApplyVisualState()
    {
        if (label != null)
        {
            label.text = State == PillarState.Completed
                ? $"{requiredSpellWord}\nDONE"
                : State == PillarState.Defending
                    ? $"{requiredSpellWord}\nDEFEND"
                    : requiredSpellWord;
            label.color = State == PillarState.Completed
                ? new Color(0.55f, 1f, 0.55f, 1f)
                : State == PillarState.Defending
                    ? new Color(1f, 0.45f, 0.2f, 1f)
                    : Color.white;
        }

        if (renderers == null)
            CacheRenderers();

        Color bodyColor = State == PillarState.Completed
            ? completedColor
            : State == PillarState.Defending
                ? defendingColor
                : dormantColor;
        float emissionIntensity = State == PillarState.Completed
            ? completedEmissionIntensity
            : State == PillarState.Defending
                ? defendingEmissionIntensity
                : dormantEmissionIntensity;
        Color emissionColor = bodyColor * Mathf.Max(0f, emissionIntensity);
        bool matchedStatusMaterial = false;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || renderer.GetComponent<TextMeshPro>() != null)
                continue;

            Material[] materials = renderer.sharedMaterials;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                Material material = materials[materialIndex];
                bool shouldTint = tintAllMaterialSlots ||
                    material != null &&
                    string.Equals(material.name, statusMaterialName, System.StringComparison.OrdinalIgnoreCase);
                if (!shouldTint)
                    continue;

                matchedStatusMaterial = true;
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties, materialIndex);
                // Store the exact authored channel values. SetColor performs
                // color-space conversion in some editor render paths, making
                // state feedback non-deterministic to both the renderer and
                // accessibility checks that read the property block back.
                properties.SetVector(BaseColorProperty, bodyColor);
                properties.SetVector(ColorProperty, bodyColor);
                properties.SetVector(EmissionColorProperty, emissionColor);
                renderer.SetPropertyBlock(properties, materialIndex);
            }
        }

        if (!tintAllMaterialSlots && !matchedStatusMaterial && !warnedMissingStatusMaterial)
        {
            warnedMissingStatusMaterial = true;
            Debug.LogWarning(
                $"[SpellPillarObjective] No material slot named '{statusMaterialName}' was found on '{name}'. " +
                "Pillar materials were left unchanged.",
                this);
        }
    }

    void AnimateRings()
    {
        if (!animateRings)
            return;

        float stateMultiplier = State == PillarState.Defending ? 1.6f : State == PillarState.Completed ? 0.35f : 1f;
        float deltaTime = Time.deltaTime * stateMultiplier;
        Vector3 axis = ringRotationAxis.sqrMagnitude > 0.001f ? ringRotationAxis.normalized : Vector3.forward;

        if (smallRing != null)
            smallRing.Rotate(axis, smallRingDegreesPerSecond * deltaTime, Space.Self);
        if (bigRing != null)
            bigRing.Rotate(axis, bigRingDegreesPerSecond * deltaTime, Space.Self);
    }

    void EnsureSetup()
    {
        if (hitCollider == null)
            hitCollider = GetComponentInChildren<Collider>(true);
        if (hitCollider == null)
            hitCollider = gameObject.AddComponent<CapsuleCollider>();

        if (labelOverride == null)
            labelOverride = GetComponentInChildren<TextMeshPro>(true);
        if (labelOverride == null)
            labelOverride = CreateFallbackLabel();

        label = labelOverride;
        CacheRenderers();
        if (renderers == null || renderers.Length == 0)
        {
            Renderer fallbackRenderer = GetComponentInChildren<Renderer>(true);
            if (fallbackRenderer != null)
                renderers = new[] { fallbackRenderer };
        }

        EnsureImportedAnimatorRoot();
        CacheAnimatedRings();
    }

    void EnsureImportedAnimatorRoot()
    {
        Animator rootAnimator = GetComponent<Animator>();
        if (rootAnimator == null || rootAnimator.runtimeAnimatorController == null || transform.childCount == 0)
            return;

        Transform modelRoot = transform.GetChild(0);
        if (modelRoot == transform)
            return;

        importedModelAnimator = modelRoot.GetComponent<Animator>();
        if (importedModelAnimator == null)
            importedModelAnimator = modelRoot.gameObject.AddComponent<Animator>();

        if (importedModelAnimator.runtimeAnimatorController == null)
            importedModelAnimator.runtimeAnimatorController = rootAnimator.runtimeAnimatorController;

        importedModelAnimator.applyRootMotion = false;
        importedModelAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        importedModelAnimator.updateMode = AnimatorUpdateMode.Normal;
        rootAnimator.enabled = false;
    }

    void CacheAnimatedRings()
    {
        smallRing = null;
        bigRing = null;

        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            string childName = child.name;
            if (smallRing == null && childName.IndexOf("Ring_small", System.StringComparison.OrdinalIgnoreCase) >= 0)
                smallRing = child;
            else if (bigRing == null && childName.IndexOf("Ring_Big", System.StringComparison.OrdinalIgnoreCase) >= 0)
                bigRing = child;
        }
    }

    TextMeshPro CreateFallbackLabel()
    {
        var labelGo = new GameObject("WordLabel", typeof(TextMeshPro));
        labelGo.transform.SetParent(transform, false);
        labelGo.transform.localPosition = Vector3.up * 7.3f;

        var text = labelGo.GetComponent<TextMeshPro>();
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = 4.2f;
        text.color = Color.white;
        return text;
    }
}
