using System.Collections.Generic;
using UnityEngine;

public class SpellProjectile : MonoBehaviour
{
    public float hitRadius = 0.45f;
    public float maxLifetime = 3f;
    public float visualScale = 0.24f;
    public float trailTime = 0.22f;
    public Transform visualRoot;
    public bool createFallbackVisual = true;
    public bool tintVisualWithSpellColour;
    public GameObject impactEffectPrefab;
    public GameObject areaImpactEffectPrefab;
    public bool enableHoming = true;
    [Min(90f)] public float homingTurnDegreesPerSecond = 1080f;
    [Min(0.2f)] public float assistedHitRadius = 1.15f;
    [Min(0.1f)] public float impactEffectLifetime = 2f;

    private SpellTarget _target;
    private SpellPillarObjective _pillarTarget;
    private string _spellWord;
    private Color _colour;
    private float _speed;
    private float _lifetime;
    private Vector3 _direction;
    private Transform _visual;
    private bool _usingFallbackVisual;
    private bool _isAreaSpell;
    private float _areaRadius;
    private readonly HashSet<Collider> _debuggedPillarColliderHits = new HashSet<Collider>();

    public void Launch(
        SpellTarget target,
        string spellWord,
        Vector3 direction,
        float speed,
        Color colour,
        bool isAreaSpell = false,
        float areaRadius = 5f,
        GameObject impactEffect = null,
        GameObject areaImpactEffect = null,
        float effectLifetime = 2f,
        SpellPillarObjective pillarTarget = null)
    {
        _target = target;
        _pillarTarget = pillarTarget != null && pillarTarget.IsTargetable ? pillarTarget : null;
        _spellWord = spellWord;
        _colour = colour;
        _direction = direction.sqrMagnitude > 0.001f ? direction.normalized : transform.forward;
        _speed = Mathf.Max(4f, speed);
        _isAreaSpell = isAreaSpell;
        _areaRadius = Mathf.Max(0.5f, areaRadius);
        impactEffectPrefab = impactEffect != null ? impactEffect : impactEffectPrefab;
        areaImpactEffectPrefab = areaImpactEffect != null ? areaImpactEffect : areaImpactEffectPrefab;
        impactEffectLifetime = Mathf.Max(0.1f, effectLifetime);
        transform.forward = _direction;
        EnsureVisual(colour);
    }

    void Update()
    {
        _lifetime += Time.deltaTime;
        if (_lifetime >= maxLifetime)
        {
            Destroy(gameObject);
            return;
        }

        if (_target != null && _target.IsDefeated)
            _target = null;
        if (_pillarTarget != null && !_pillarTarget.IsTargetable)
            _pillarTarget = null;

        float step = _speed * Time.deltaTime;
        Vector3 currentPosition = transform.position;
        Vector3? homingAimPoint = GetHomingAimPoint();
        if (enableHoming && homingAimPoint.HasValue)
        {
            Vector3 toAimPoint = homingAimPoint.Value - currentPosition;
            if (toAimPoint.sqrMagnitude > 0.001f)
            {
                float radians = homingTurnDegreesPerSecond * Mathf.Deg2Rad * Time.deltaTime;
                _direction = Vector3.RotateTowards(_direction, toAimPoint.normalized, radians, 0f).normalized;
            }
        }

        if (Physics.SphereCast(currentPosition, hitRadius, _direction, out RaycastHit hitInfo, step,
                ~0, QueryTriggerInteraction.Collide))
        {
            SpellPillarObjective pillar = hitInfo.collider.GetComponentInParent<SpellPillarObjective>();
            if (pillar != null)
            {
                bool activated = pillar.TryActivate(_spellWord);
                LogPillarColliderHit(pillar, hitInfo.collider, hitInfo.point, activated);
                if (activated)
                {
                    transform.position = hitInfo.point;
                    SpawnImpactEffect(hitInfo.point);
                    Destroy(gameObject);
                    return;
                }
            }

            if (_isAreaSpell)
            {
                transform.position = hitInfo.point;
                DetonateArea();
                Destroy(gameObject);
                return;
            }

            SpellTarget hitTarget = hitInfo.collider.GetComponentInParent<SpellTarget>();
            if (hitTarget != null && hitTarget.ReceiveHit(_spellWord))
            {
                transform.position = hitInfo.point;
                SpawnImpactEffect(hitInfo.point);
                Destroy(gameObject);
                return;
            }
        }

        if (_target != null)
        {
            Vector3 toAimPoint = _target.GetAimPoint() - currentPosition;
            if (toAimPoint.magnitude <= Mathf.Max(assistedHitRadius, step))
            {
                transform.position = _target.GetAimPoint();
                if (_isAreaSpell)
                {
                    DetonateArea();
                    Destroy(gameObject);
                    return;
                }
                if (_target.ReceiveHit(_spellWord))
                {
                    SpawnImpactEffect(transform.position);
                    Destroy(gameObject);
                    return;
                }
            }
        }

        if (_pillarTarget != null)
        {
            Vector3 toAimPoint = _pillarTarget.GetAimPoint() - currentPosition;
            if (toAimPoint.magnitude <= Mathf.Max(assistedHitRadius, step))
            {
                transform.position = _pillarTarget.GetAimPoint();
                if (_isAreaSpell)
                {
                    DetonateArea();
                    Destroy(gameObject);
                    return;
                }

                if (_pillarTarget.TryActivate(_spellWord))
                {
                    SpawnImpactEffect(transform.position);
                    Destroy(gameObject);
                    return;
                }

                _pillarTarget = null;
            }
        }

        transform.position = currentPosition + _direction * step;
        transform.forward = _direction;
    }

    Vector3? GetHomingAimPoint()
    {
        if (_target != null)
            return _target.GetAimPoint();
        if (_pillarTarget != null)
            return _pillarTarget.GetAimPoint();
        return null;
    }

    void DetonateArea()
    {
        SpawnEffectPrefab(
            areaImpactEffectPrefab != null ? areaImpactEffectPrefab : impactEffectPrefab,
            transform.position,
            Quaternion.LookRotation(-_direction, Vector3.up),
            _colour,
            impactEffectLifetime,
            Mathf.Max(1f, _areaRadius * 0.35f),
            "Area Impact FX");

        var hitTargets = new System.Collections.Generic.HashSet<SpellTarget>();
        var hitPillars = new System.Collections.Generic.HashSet<SpellPillarObjective>();
        foreach (Collider hit in Physics.OverlapSphere(transform.position, _areaRadius, ~0, QueryTriggerInteraction.Collide))
        {
            SpellPillarObjective pillar = hit != null ? hit.GetComponentInParent<SpellPillarObjective>() : null;
            if (pillar != null && hitPillars.Add(pillar))
                pillar.TryActivate(_spellWord);

            SpellTarget target = hit != null ? hit.GetComponentInParent<SpellTarget>() : null;
            if (target != null && hitTargets.Add(target))
                target.ReceiveHit(_spellWord);
        }
    }

    void LogPillarColliderHit(SpellPillarObjective pillar, Collider hitCollider, Vector3 hitPoint, bool activated)
    {
        if (pillar == null || hitCollider == null)
            return;

        if (!_debuggedPillarColliderHits.Add(hitCollider))
            return;

        Debug.Log(
            $"[SpellProjectile] Hit pillar collider '{hitCollider.name}' on '{pillar.name}' " +
            $"with spell '{_spellWord}' at {hitPoint}. Pillar state: {pillar.State}. Activated: {activated}.",
            pillar);
    }

    void SpawnImpactEffect(Vector3 position)
    {
        SpawnEffectPrefab(
            impactEffectPrefab,
            position,
            Quaternion.LookRotation(-_direction, Vector3.up),
            _colour,
            impactEffectLifetime,
            1f,
            "Spell Impact FX");
    }

    public static void SpawnEffectPrefab(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        Color colour,
        float lifetime,
        float scale,
        string fallbackName)
    {
        bool usingPrefab = prefab != null;
        GameObject instance = usingPrefab
            ? Instantiate(prefab, position, rotation)
            : CreateFallbackParticleBurst(position, rotation, colour, scale, fallbackName);

        if (usingPrefab)
            instance.transform.localScale *= Mathf.Max(0.05f, scale);

        PlayParticles(instance);
        Destroy(instance, Mathf.Max(0.1f, lifetime));
    }

    void EnsureVisual(Color colour)
    {
        if (_visual == null)
        {
            _visual = visualRoot != null ? visualRoot : FindAuthoredVisual();

            if (_visual == null && createFallbackVisual)
            {
                var visualGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                visualGo.name = "Visual";
                visualGo.transform.SetParent(transform, false);
                visualGo.transform.localScale = Vector3.one * visualScale;

                var collider = visualGo.GetComponent<Collider>();
                if (collider != null)
                    Destroy(collider);

                _visual = visualGo.transform;
                _usingFallbackVisual = true;
            }
        }

        var renderer = _visual != null ? _visual.GetComponentInChildren<Renderer>() : null;
        if (renderer != null && (_usingFallbackVisual || tintVisualWithSpellColour))
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ??
                         Shader.Find("Standard");
            renderer.material = new Material(shader);
            renderer.material.color = colour;
        }

        var trail = GetComponent<TrailRenderer>();
        if (trail == null)
            trail = gameObject.AddComponent<TrailRenderer>();

        var trailShader = Shader.Find("Sprites/Default") ??
                          Shader.Find("Universal Render Pipeline/Unlit") ??
                          Shader.Find("Standard");
        trail.material = new Material(trailShader);
        trail.time = trailTime;
        trail.widthMultiplier = visualScale * 0.65f;
        trail.minVertexDistance = 0.05f;
        trail.alignment = LineAlignment.View;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.emitting = true;
        trail.startColor = new Color(colour.r, colour.g, colour.b, 0.95f);
        trail.endColor = new Color(colour.r, colour.g, colour.b, 0.05f);
    }

    Transform FindAuthoredVisual()
    {
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is TrailRenderer || renderer is LineRenderer)
                continue;

            return renderer.transform;
        }

        return null;
    }

    static GameObject CreateFallbackParticleBurst(Vector3 position, Quaternion rotation, Color colour, float scale, string name)
    {
        var go = new GameObject(string.IsNullOrWhiteSpace(name) ? "Spell FX" : name);
        go.transform.SetPositionAndRotation(position, rotation);

        var particles = go.AddComponent<ParticleSystem>();
        var main = particles.main;
        main.duration = 0.45f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f * scale, 3.5f * scale);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f * scale, 0.22f * scale);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(colour.r, colour.g, colour.b, 0.95f),
            new Color(1f, 1f, 1f, 0.75f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 24) });

        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.2f * scale;

        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        Shader shader = Shader.Find("Sprites/Default") ??
                        Shader.Find("Universal Render Pipeline/Unlit") ??
                        Shader.Find("Standard");
        renderer.material = new Material(shader);
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortingFudge = 8f;

        return go;
    }

    static void PlayParticles(GameObject root)
    {
        if (root == null)
            return;

        foreach (ParticleSystem particles in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            particles.Clear(true);
            particles.Play(true);
        }
    }
}
