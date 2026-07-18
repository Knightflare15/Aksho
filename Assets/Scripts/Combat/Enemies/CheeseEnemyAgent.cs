using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(SpellTarget))]
public class CheeseEnemyAgent : EnemyAgentBase
{
    [Header("Movement")]
    public float repathInterval = 0.2f;
    public float stoppingDistance = 1.1f;
    public float moveSpeed = 2.25f;
    public float turnSpeed = 320f;
    public float moveAcceleration = 10f;
    public float offMeshJumpHeight = 1.15f;
    public float offMeshTraverseDuration = 0.45f;
    public float attackRangePadding = 0.2f;

    [Header("Combat")]
    public Animator animator;
    public CombatActor combatActor;
    public AttackHitboxController hitboxController;
    public EnemyAttackCoordinator attackCoordinator;
    public bool useAnimationEventsForHitboxes;
    [Min(0f)] public float postAttackIdleSeconds = 1f;
    public List<EnemyAttackDefinition> attacks = new List<EnemyAttackDefinition>();

    [Header("Fallback Hitbox")]
    public Vector3 fallbackHitboxLocalPosition = new Vector3(0f, 0.9f, 0.9f);
    public float fallbackHitboxRadius = 0.45f;
    public float fallbackHitboxHeight = 1.35f;

    private SpellTarget _target;
    private NavMeshAgent _agent;
    private PlayerController _playerController;
    private float _nextRepathAt;
    private float _nextSupportRepathAt;
    private float _movementLockedUntil = -999f;
    private bool _isTraversingLink;
    private bool _isAttacking;
    private readonly Dictionary<string, float> _nextAttackAtById = new Dictionary<string, float>();

    void Awake()
    {
        _target = GetComponent<SpellTarget>();
        animator = animator != null ? animator : _target != null ? _target.animator : null;
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
        if (animator != null)
            animator.applyRootMotion = false;
        _agent = GetComponent<NavMeshAgent>();
        if (_agent == null)
            _agent = gameObject.AddComponent<NavMeshAgent>();

        _agent.stoppingDistance = stoppingDistance;
        _agent.autoTraverseOffMeshLink = false;
        _agent.angularSpeed = Mathf.Max(60f, turnSpeed);
        _agent.acceleration = Mathf.Max(2f, moveAcceleration);
        _agent.speed = Mathf.Max(1f, moveSpeed);
        EnsureCombatComponents();
        EnsureDefaultAttacks();
    }

    void OnEnable()
    {
        attackCoordinator?.RegisterAgent(this);
    }

    void OnDisable()
    {
        attackCoordinator?.ReleaseAttack(this);
        hitboxController?.CloseAllHitboxes();
        _isAttacking = false;
    }

    void Start()
    {
        _playerController = FindAnyObjectByType<PlayerController>();
        if (attackCoordinator == null)
            attackCoordinator = FindAnyObjectByType<EnemyAttackCoordinator>();
        attackCoordinator?.RegisterAgent(this);
    }

    public void SetAttackCoordinator(EnemyAttackCoordinator coordinator)
    {
        if (attackCoordinator == coordinator)
            return;

        attackCoordinator?.UnregisterAgent(this);
        attackCoordinator = coordinator;
        attackCoordinator?.RegisterAgent(this);
    }

    void Update()
    {
        if (_target != null && _target.IsDefeated)
        {
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
                StopMovement();
            hitboxController?.CloseAllHitboxes();
            return;
        }

        if (_playerController == null)
            _playerController = FindAnyObjectByType<PlayerController>();

        if (_playerController == null || _agent == null || !_agent.enabled)
            return;

        if (!EnsureAgentOnNavMesh())
            return;

        float distanceToPlayer = Vector3.Distance(transform.position, _playerController.transform.position);
        bool isInsideAnyAttackRange = IsInsideAnyAttackRange(distanceToPlayer);
        float holdRange = GetHoldRange();
        if (IsMovementLocked())
        {
            StopMovement();
            FacePlayer();
            UpdateAnimator();
            return;
        }

        if (!_isAttacking)
        {
            EnemyAttackDefinition attack = SelectAttack(distanceToPlayer);
            if (attack != null)
            {
                if (attackCoordinator == null || attackCoordinator.TryRequestAttack(this))
                    StartCoroutine(AttackRoutine(attack));
                else
                    HoldSupportPosition(holdRange);
                UpdateAnimator();
                return;
            }
        }

        if (_isAttacking)
        {
            StopMovement();
            FacePlayer();
            UpdateAnimator();
            return;
        }

        if (!_isTraversingLink && (distanceToPlayer <= holdRange || isInsideAnyAttackRange))
        {
            StopMovement();
            FacePlayer();
            UpdateAnimator();
            return;
        }

        if (Time.time >= _nextRepathAt && !_isTraversingLink)
        {
            _nextRepathAt = Time.time + repathInterval;
            _agent.isStopped = false;
            _agent.stoppingDistance = holdRange;
            Vector3 desiredTarget = _playerController.transform.position;
            if (NavMesh.SamplePosition(desiredTarget, out NavMeshHit hit, 4f, NavMesh.AllAreas))
                desiredTarget = hit.position;
            _agent.SetDestination(desiredTarget);
        }

        if (_agent.isOnOffMeshLink && !_isTraversingLink)
            StartCoroutine(TraverseOffMeshLink());

        if (!_isTraversingLink)
            UpdateFacing();

        UpdateAnimator();
    }

    void UpdateFacing()
    {
        if (_agent.desiredVelocity.sqrMagnitude < 0.01f)
            return;

        Vector3 faceDirection = _agent.desiredVelocity.normalized;
        faceDirection.y = 0f;
        if (faceDirection.sqrMagnitude < 0.001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(faceDirection, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 10f);
    }

    void HoldSupportPosition(float holdRange)
    {
        if (_playerController == null || _agent == null || !_agent.enabled || !_agent.isOnNavMesh)
            return;

        FacePlayer();
        if (attackCoordinator == null || Time.time < _nextSupportRepathAt)
            return;

        _nextSupportRepathAt = Time.time + Mathf.Max(0.05f, attackCoordinator.supportRepositionInterval);
        if (!attackCoordinator.TryGetSupportPosition(this, _playerController.transform.position, holdRange, out Vector3 supportPosition))
        {
            StopMovement();
            return;
        }

        if (Vector3.Distance(transform.position, supportPosition) <= 0.45f)
        {
            StopMovement();
            return;
        }

        _agent.isStopped = false;
        _agent.stoppingDistance = 0.2f;
        _agent.SetDestination(supportPosition);
    }

    bool EnsureAgentOnNavMesh()
    {
        if (_agent == null || !_agent.enabled)
            return false;

        if (_agent.isOnNavMesh)
            return true;

        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 6f, NavMesh.AllAreas))
            return _agent.Warp(hit.position);

        return false;
    }

    void OnAnimatorMove()
    {
        if (animator == null || !animator.applyRootMotion)
            return;

        if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            _agent.nextPosition = transform.position;
    }

    void EnsureCombatComponents()
    {
        combatActor = combatActor != null ? combatActor : GetComponent<CombatActor>();
        if (combatActor == null)
            combatActor = gameObject.AddComponent<CombatActor>();
        combatActor.team = CombatTeam.Enemy;
        combatActor.spellTarget = _target;

        hitboxController = hitboxController != null ? hitboxController : GetComponent<AttackHitboxController>();
        if (hitboxController == null)
            hitboxController = gameObject.AddComponent<AttackHitboxController>();

        if (!hitboxController.HasHitbox("Melee"))
            CreateFallbackMeleeHitbox();
        hitboxController.RefreshHitboxes();
        hitboxController.CloseAllHitboxes();
    }

    void EnsureDefaultAttacks()
    {
        if (attacks == null)
            attacks = new List<EnemyAttackDefinition>();

        if (attacks.Count == 0)
        {
            attacks.Add(new EnemyAttackDefinition
            {
                attackId = "Punch",
                animationTrigger = "Attack",
                hitboxId = "Melee",
                range = 1.75f,
                cooldown = 1.35f,
                windupSeconds = 0.28f,
                activeSeconds = 0.22f,
                recoverySeconds = 0.45f,
                weight = 1f,
                damage = 1,
            });
        }

        foreach (EnemyAttackDefinition attack in attacks)
            NormalizeAttackDefinition(attack);
    }

    static void NormalizeAttackDefinition(EnemyAttackDefinition attack)
    {
        if (attack == null)
            return;

        if (string.IsNullOrWhiteSpace(attack.attackId))
            attack.attackId = "Punch";
        if (string.IsNullOrWhiteSpace(attack.animationTrigger))
            attack.animationTrigger = "Attack";
        if (string.IsNullOrWhiteSpace(attack.hitboxId))
            attack.hitboxId = "Melee";

        if (attack.range <= 0f)
            attack.range = 1.45f;
        attack.cooldown = Mathf.Max(attack.cooldown, 1.2f);
        attack.windupSeconds = Mathf.Max(attack.windupSeconds, 0.2f);
        attack.activeSeconds = Mathf.Max(attack.activeSeconds, 0.15f);
        attack.recoverySeconds = Mathf.Max(attack.recoverySeconds, 0.45f);
        attack.weight = Mathf.Max(attack.weight, 1f);
        attack.damage = attack.battleRole == BattleActionRole.Offense
            ? Mathf.Max(attack.damage, 1)
            : Mathf.Max(attack.damage, 0);
    }

    void CreateFallbackMeleeHitbox()
    {
        var hitboxObject = new GameObject("MeleeHitbox");
        hitboxObject.transform.SetParent(transform, false);
        hitboxObject.transform.localPosition = fallbackHitboxLocalPosition;

        var capsule = hitboxObject.AddComponent<CapsuleCollider>();
        capsule.isTrigger = true;
        capsule.radius = Mathf.Max(0.05f, fallbackHitboxRadius);
        capsule.height = Mathf.Max(fallbackHitboxHeight, capsule.radius * 2f);
        capsule.direction = 1;

        var hitbox = hitboxObject.AddComponent<CombatHitbox>();
        hitbox.hitboxId = "Melee";
        hitbox.owner = combatActor;
        hitbox.damage = 1;
        hitbox.activeOnStart = false;
    }

    EnemyAttackDefinition SelectAttack(float distanceToPlayer)
    {
        if (attacks == null || attacks.Count == 0)
            return null;

        float totalWeight = 0f;
        foreach (EnemyAttackDefinition attack in attacks)
        {
            if (!IsAttackAvailable(attack, distanceToPlayer))
                continue;
            totalWeight += Mathf.Max(0.01f, attack.weight);
        }

        if (totalWeight <= 0.001f)
            return null;

        float roll = Random.value * totalWeight;
        foreach (EnemyAttackDefinition attack in attacks)
        {
            if (!IsAttackAvailable(attack, distanceToPlayer))
                continue;

            roll -= Mathf.Max(0.01f, attack.weight);
            if (roll <= 0f)
                return attack;
        }

        return null;
    }

    bool IsAttackAvailable(EnemyAttackDefinition attack, float distanceToPlayer)
    {
        if (attack == null)
            return false;

        if (!IsInsideAttackRange(attack, distanceToPlayer))
            return false;

        string key = string.IsNullOrWhiteSpace(attack.attackId) ? attack.hitboxId : attack.attackId;
        return !_nextAttackAtById.TryGetValue(key, out float nextAttackAt) || Time.time >= nextAttackAt;
    }

    bool IsInsideAttackRange(EnemyAttackDefinition attack, float distanceToPlayer)
    {
        return attack != null &&
            distanceToPlayer <= Mathf.Max(0.1f, attack.range) + Mathf.Max(0f, attackRangePadding);
    }

    bool IsInsideAnyAttackRange(float distanceToPlayer)
    {
        if (attacks == null)
            return false;

        foreach (EnemyAttackDefinition attack in attacks)
        {
            if (attack == null)
                continue;

            float attackRange = Mathf.Max(0.1f, attack.range) + Mathf.Max(0f, attackRangePadding);
            if (distanceToPlayer <= attackRange)
                return true;
        }

        return false;
    }

    IEnumerator AttackRoutine(EnemyAttackDefinition attack)
    {
        _isAttacking = true;
        if (_agent != null && _agent.enabled)
            StopMovement();

        ResetAnimatorTrigger("Move");
        SetAnimatorFloat("MoveSpeed", 0f);
        SetAnimatorBool("Attacking", true);
        FacePlayer();
        if (animator != null && !string.IsNullOrWhiteSpace(attack.animationTrigger) && HasAnimatorParameter(attack.animationTrigger, AnimatorControllerParameterType.Trigger))
            animator.SetTrigger(attack.animationTrigger);

        ApplyGrammarBattleEffect(attack);
        bool opensHitbox = attack.battleRole == BattleActionRole.Offense && attack.damage > 0;
        if (attack.battleRole == BattleActionRole.Dodge)
            PerformDodgeMovement(attack);

        if (opensHitbox)
            hitboxController.SetHitboxDamage(attack.hitboxId, attack.damage);

        if (!useAnimationEventsForHitboxes || !opensHitbox)
        {
            yield return new WaitForSeconds(Mathf.Max(0f, attack.windupSeconds));
            if (opensHitbox)
            {
                hitboxController.OpenHitbox(attack.hitboxId);
                yield return new WaitForSeconds(Mathf.Max(0.02f, attack.activeSeconds));
                hitboxController.CloseHitbox(attack.hitboxId);
            }
            yield return new WaitForSeconds(Mathf.Max(0f, attack.recoverySeconds));
        }
        else
        {
            float totalDuration = Mathf.Max(0.05f, attack.windupSeconds + attack.activeSeconds + attack.recoverySeconds);
            yield return new WaitForSeconds(totalDuration);
            hitboxController.CloseAllHitboxes();
        }

        string key = string.IsNullOrWhiteSpace(attack.attackId) ? attack.hitboxId : attack.attackId;
        _nextAttackAtById[key] = Time.time + Mathf.Max(0.05f, attack.cooldown);
        _movementLockedUntil = Time.time + Mathf.Max(0f, postAttackIdleSeconds);
        _nextRepathAt = _movementLockedUntil;
        StopMovement();
        hitboxController?.CloseAllHitboxes();
        ResetAnimatorTrigger(attack.animationTrigger);
        ResetAnimatorTrigger("Move");
        SetAnimatorFloat("MoveSpeed", 0f);
        _isAttacking = false;
        SetAnimatorBool("Attacking", false);
        attackCoordinator?.ReleaseAttack(this);
    }

    void ApplyGrammarBattleEffect(EnemyAttackDefinition attack)
    {
        if (attack == null)
            return;

        CreatureCombatController creatureCombat = _playerController != null
            ? _playerController.GetComponent<CreatureCombatController>() ?? _playerController.GetComponentInChildren<CreatureCombatController>(true)
            : FindAnyObjectByType<CreatureCombatController>();
        creatureCombat?.NoteEnemyGrammarAction(attack);
        if (attack.inflictedGrammarCurse == GrammarBattleCurse.None)
            return;

        creatureCombat?.ApplyGrammarCurse(attack.inflictedGrammarCurse);
    }

    float GetHoldRange()
    {
        if (attacks == null || attacks.Count == 0)
            return stoppingDistance;

        float shortest = float.MaxValue;
        foreach (EnemyAttackDefinition attack in attacks)
        {
            if (attack != null)
                shortest = Mathf.Min(shortest, attack.range);
        }

        return Mathf.Max(0.1f, shortest * 0.82f);
    }

    bool IsMovementLocked()
    {
        return Time.time < _movementLockedUntil;
    }

    void FacePlayer()
    {
        if (_playerController == null)
            return;

        Vector3 direction = _playerController.transform.position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 18f);
    }

    void StopMovement()
    {
        if (_agent == null || !_agent.enabled)
            return;

        if (!_agent.isOnNavMesh)
            return;

        _agent.isStopped = true;
        if (_agent.hasPath || _agent.pathPending)
            _agent.ResetPath();
        _agent.velocity = Vector3.zero;
        if (_agent.isOnNavMesh)
            _agent.nextPosition = transform.position;
    }

    void UpdateAnimator()
    {
        if (animator == null)
            return;

        float moveMagnitude = _agent != null && _agent.enabled && _agent.isOnNavMesh && !_agent.isStopped && !_isAttacking
            ? _agent.velocity.magnitude
            : 0f;
        SetAnimatorFloat("MoveSpeed", moveMagnitude);
        SetAnimatorBool("Attacking", _isAttacking);
        if (!_isAttacking && moveMagnitude > 0.05f)
            SetAnimatorTrigger("Move");
        else
            ResetAnimatorTrigger("Move");
    }

    void SetAnimatorFloat(string parameter, float value)
    {
        if (HasAnimatorParameter(parameter, AnimatorControllerParameterType.Float))
            animator.SetFloat(parameter, value);
    }

    void SetAnimatorBool(string parameter, bool value)
    {
        if (HasAnimatorParameter(parameter, AnimatorControllerParameterType.Bool))
            animator.SetBool(parameter, value);
    }

    void SetAnimatorTrigger(string parameter)
    {
        if (HasAnimatorParameter(parameter, AnimatorControllerParameterType.Trigger))
            animator.SetTrigger(parameter);
    }

    void ResetAnimatorTrigger(string parameter)
    {
        if (HasAnimatorParameter(parameter, AnimatorControllerParameterType.Trigger))
            animator.ResetTrigger(parameter);
    }

    bool HasAnimatorParameter(string parameter, AnimatorControllerParameterType type)
    {
        if (animator == null || string.IsNullOrWhiteSpace(parameter))
            return false;

        foreach (AnimatorControllerParameter candidate in animator.parameters)
        {
            if (candidate.type == type && candidate.name == parameter)
                return true;
        }

        return false;
    }

    void PerformDodgeMovement(EnemyAttackDefinition attack)
    {
        Vector3 dodgeDirection = transform.right;
        if (_playerController != null)
        {
            Vector3 away = transform.position - _playerController.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude > 0.01f)
                dodgeDirection = (away.normalized + transform.right * 0.65f).normalized;
        }

        float distance = Mathf.Clamp(Mathf.Max(0.1f, attack != null ? attack.range : 1f) * 0.35f, 0.75f, 2.75f);
        Vector3 destination = transform.position + dodgeDirection.normalized * distance;
        if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
        {
            if (NavMesh.SamplePosition(destination, out NavMeshHit hit, Mathf.Max(1f, _agent.radius * 4f), NavMesh.AllAreas))
            {
                _agent.Warp(hit.position);
                transform.position = hit.position;
            }
        }
        else
        {
            transform.position = destination;
        }

        _movementLockedUntil = Mathf.Max(_movementLockedUntil, Time.time + Mathf.Max(0.15f, attack != null ? attack.dodgeSeconds : 0.25f));
    }

    IEnumerator TraverseOffMeshLink()
    {
        _isTraversingLink = true;

        OffMeshLinkData link = _agent.currentOffMeshLinkData;
        Vector3 start = transform.position;
        Vector3 end = link.endPos + Vector3.up * _agent.baseOffset;

        float elapsed = 0f;
        while (elapsed < offMeshTraverseDuration)
        {
            float t = elapsed / offMeshTraverseDuration;
            Vector3 position = Vector3.Lerp(start, end, t);
            position.y += Mathf.Sin(t * Mathf.PI) * offMeshJumpHeight;
            transform.position = position;
            yield return null;
            elapsed += Time.deltaTime;
        }

        transform.position = end;
        _agent.CompleteOffMeshLink();
        _isTraversingLink = false;
    }
}
