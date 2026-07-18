using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// First-person character controller.
///
/// Draw mode (press F):
///   • Movement and jumping continue while the panel is open.
///   • Camera look is disabled.
///   • Cursor becomes visible and free.
///   • Time.timeScale stays at 1 — no slow-motion.
///
/// Mode selection:
///   Assign defaultMode in the Inspector (SandboxMode or ChallengeMode component).
///   Swap at runtime by calling SetDrawMode(IDrawMode) from your UI.
///
/// ExitDrawMode() is public so DrawController can call it after word submission.
/// </summary>
public partial class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float speed     = 6f;
    public float jumpForce = 2f;
    public float gravity   = -9.8f;
    public float acceleration = 34f;
    public float deceleration = 42f;
    [Range(0f, 1f)] public float airControl = 0.55f;
    [Range(0.2f, 1f)] public float backpedalSpeedMultiplier = 0.62f;
    public float coyoteTime = 0.12f;
    public float jumpBufferTime = 0.12f;
    public float fallGravityMultiplier = 1.65f;
    public float terminalVelocity = -28f;

    [Header("Knockback")]
    public float knockbackSpeed = 9f;
    public float knockbackDuration = 0.22f;
    public float knockbackUpwardSpeed = 1.5f;

    [Header("Camera")]
    public float     mouseSensitivity = 0.3f;
    public Transform cameraTransform;
    [Range(0.25f, 1f)] public float verticalLookRangeMultiplier = 1f;
    public float minCameraPitch = -30f;
    public float maxCameraPitch = 25f;
    public Transform spawnPoint;

    [Header("References")]
    public GameObject    drawingPanel;
    public DrawController drawController;
    public Animator animator;

    [Header("Bookie")]
    public Transform bookieTransform;
    public SkinnedMeshRenderer bookieRenderer;
    public string bookieCloseBlendShapeName = "close";
    public int bookieToggleBlendShapeIndex = 2;
    public float bookieClosedWeight = 100f;
    public float bookieOpenWeight = 0f;
    [Min(0.01f)] public float bookieMovementBlendSeconds = 1f;
    [Min(0.01f)] public float bookieToggleBlendSeconds = 1f;

    [Header("Draw Mode")]
    [Tooltip("Drag either SandboxMode or ChallengeMode component here")]
    public MonoBehaviour defaultMode;   // assigned in Inspector as SandboxMode/ChallengeMode
    [Min(0.2f)] public float specialForgeHoldSeconds = 0.45f;

    [Header("Animation Triggers")]
    public string moveTriggerName = "move";
    public string stopTriggerName = "stop";
    public string jumpTriggerName = "jump";
    public string attackTriggerName = "attack";

    // ── Input ──────────────────────────────────────────────────────────────
    private PlayerControls controls;
    private Vector2 moveInput;
    private Vector2 lookInput;
    private Vector2 mobileMoveInput;
    private Vector2 mobileLookInput;

    // ── Internal ───────────────────────────────────────────────────────────
    private CharacterController controller;
    private WordActionHandler wordActionHandler;
    private GrammarVoiceCombatController grammarVoiceCombat;
    private float baseSpeed;
    private float   yVelocity         = 0f;
    private float   xRotation         = 0f;
    private float   lastGroundedAt    = -999f;
    private float   lastJumpPressedAt = -999f;
    private Vector3 horizontalVelocity;
    private Vector3 animatorHorizontalVelocity;
    private Vector3 knockbackVelocity;
    private float   knockbackEndsAt = -999f;
    private bool    isDrawingMode     = false;
    private bool    wasGrounded       = false;
    private bool    wasMoving         = false;
    private bool    battleInputLocked = false;
    private Vector3 spawnPosition;
    private Quaternion spawnRotation;
    private float forgeKeyPressedAt = -1f;
    private bool forgeHoldTriggered;
    private bool attackInputHeld;
    private int bookieCloseBlendShapeIndex = -1;
    private bool bookieToggleBlendShapeOn;
    private bool bookieToggleBlendShapeInitialized;
    private float bookieToggleTargetWeight;
    private bool warnedMissingBookieRenderer;
    private bool warnedMissingBookieCloseBlendShape;
    private bool loggedBookieBlendShapeSetup;

    public bool IsDrawingMode => isDrawingMode;
    public bool IsBattleInputLocked => battleInputLocked;
    public Vector3 SpawnPosition => spawnPosition;

    // ── Unity lifecycle ────────────────────────────────────────────────────

    void Awake()
    {
        if (GetComponent<PlayerHealth>() == null)
            gameObject.AddComponent<PlayerHealth>();
        if (GetComponent<PlayerHurtbox>() == null)
            gameObject.AddComponent<PlayerHurtbox>();

        ResolveWordActionHandler();
        grammarVoiceCombat = GetComponent<GrammarVoiceCombatController>() ?? gameObject.AddComponent<GrammarVoiceCombatController>();
        baseSpeed = speed;

        controls = new PlayerControls();
        GameSettings.ApplyBindingOverrides(controls.asset);
        GameSettings.ControlsChanged += ReloadBindingOverrides;

        controls.Movement.Newaction.performed += ctx => moveInput = battleInputLocked ? Vector2.zero : ctx.ReadValue<Vector2>();
        controls.Movement.Newaction.canceled  += ctx => moveInput = Vector2.zero;

        controls.Movement.Look.performed += ctx => lookInput = IsLookInputBlocked() ? Vector2.zero : ctx.ReadValue<Vector2>();
        controls.Movement.Look.canceled  += ctx => lookInput = Vector2.zero;

        controls.Movement.Jump.performed      += ctx => { if (!IsGameplayInputPaused() && !battleInputLocked) TryJump(); };
        controls.Movement.Attack.started      += ctx => { if (!IsGameplayInputPaused() && grammarVoiceCombat != null && grammarVoiceCombat.IsCombatEncounterActive) HandleAttackStarted(); };
        controls.Movement.Attack.canceled     += ctx => HandleAttackCanceled();
    }

    void OnEnable()  => controls.Enable();
    void OnDisable() => controls.Disable();

    void OnDestroy()
    {
        GameSettings.ControlsChanged -= ReloadBindingOverrides;
        controls?.Dispose();
    }

    void Start()
    {
        controller = GetComponent<CharacterController>();

        if (cameraTransform == null)
            cameraTransform = Camera.main != null ? Camera.main.transform : null;
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
        ResolveBookieReferences();

        if (drawingPanel != null)
            drawingPanel.SetActive(false);

        ApplyRunDrawMode();

        CaptureSpawnPoint();
        if (cameraTransform != null)
        {
            Vector3 localEuler = cameraTransform.localEulerAngles;
            xRotation = NormalizePitch(localEuler.x);
            cameraTransform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        }

        LockCursor();
    }

    void Update()
    {
        if (RunEndScreenController.IsOpen)
        {
            attackInputHeld = false;
            grammarVoiceCombat?.Cancel();
            FreeCursor();
            return;
        }

        if (IsGameplayInputPaused())
        {
            lookInput = Vector2.zero;
            forgeKeyPressedAt = -1f;
            attackInputHeld = false;
            grammarVoiceCombat?.Cancel();
            FreeCursor();
            return;
        }

        if (TemplateRecorderUI.IsOpen || GrimoireUI.IsOpen || ChestMiniGameState.IsOpen)
        {
            attackInputHeld = false;
            grammarVoiceCombat?.Cancel();
            return;
        }
        UpdateBookieSlotToggleBlendShape();
        UpdateAttackHoldInput();
        if (!isDrawingMode && !battleInputLocked)
            HandleMouseLook();

        HandleMovement();
        UpdateBookieMovementBlendShapes();
        UpdateAnimator();
    }

    // ── Look ───────────────────────────────────────────────────────────────

    void HandleMouseLook()
    {
        if (IsLookInputBlocked())
            return;

        Vector2 effectiveLookInput = lookInput + mobileLookInput;
        mobileLookInput = Vector2.zero;

        float mouseX = effectiveLookInput.x * mouseSensitivity;
        float mouseY = effectiveLookInput.y * mouseSensitivity;

        xRotation -= mouseY;
        GetPitchClampBounds(out float minPitch, out float maxPitch);
        xRotation = Mathf.Clamp(xRotation, minPitch, maxPitch);
        transform.Rotate(Vector3.up * mouseX);

        if (cameraTransform != null)
            cameraTransform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
    }

    // ── Movement ───────────────────────────────────────────────────────────

    void HandleMovement()
    {
        if (controller == null)
            controller = GetComponent<CharacterController>();
        if (controller == null)
            return;

        if (battleInputLocked)
        {
            HandleBattleLockedMovement();
            return;
        }

        bool isGrounded = controller.isGrounded;
        if (isGrounded)
            lastGroundedAt = Time.time;

        Vector2 clampedInput = Vector2.ClampMagnitude(moveInput + mobileMoveInput, 1f);
        float forwardInput = clampedInput.y < 0f
            ? clampedInput.y * Mathf.Clamp(backpedalSpeedMultiplier, 0.2f, 1f)
            : clampedInput.y;
        Vector3 inputDir = transform.right * clampedInput.x + transform.forward * forwardInput;

        Vector3 targetHorizontalVelocity = inputDir * GetEffectiveSpeed();
        float response = controller.isGrounded
            ? inputDir.sqrMagnitude > 0.001f ? acceleration : deceleration
            : acceleration * Mathf.Clamp01(airControl);
        horizontalVelocity = Vector3.MoveTowards(
            horizontalVelocity,
            targetHorizontalVelocity,
            Mathf.Max(0.1f, response) * Time.deltaTime);

        if (CanUseBufferedJump())
        {
            yVelocity = Mathf.Sqrt(Mathf.Max(0.01f, jumpForce) * -2f * gravity);
            lastJumpPressedAt = -999f;
            TriggerAnimator("Jump");
            TriggerConfiguredAnimatorTrigger(jumpTriggerName);
        }
        else
        {
            if (isGrounded && yVelocity < 0f)
                yVelocity = -2f;

            float gravityScale = yVelocity < 0f ? Mathf.Max(1f, fallGravityMultiplier) : 1f;
            yVelocity = Mathf.Max(terminalVelocity, yVelocity + gravity * gravityScale * Time.deltaTime);
        }

        UpdateKnockbackVelocity();

        Vector3 positionBeforeMove = transform.position;
        controller.Move((horizontalVelocity + knockbackVelocity + Vector3.up * yVelocity) * Time.deltaTime);
        animatorHorizontalVelocity = GetHorizontalVelocitySince(positionBeforeMove);

        bool groundedAfterMove = controller.isGrounded;
        bool isMovingNow = groundedAfterMove &&
                            inputDir.sqrMagnitude > 0.001f &&
                            animatorHorizontalVelocity.sqrMagnitude > 0.04f;
        if (!wasGrounded && groundedAfterMove)
        {
            TriggerAnimator("Land");
            TriggerMovementState(isMovingNow);
        }

        if (groundedAfterMove && isMovingNow != wasMoving)
            TriggerMovementState(isMovingNow);

        wasGrounded = groundedAfterMove;
        wasMoving = isMovingNow;
    }

    void TryJump()
    {
        lastJumpPressedAt = Time.time;
    }

    bool CanUseBufferedJump()
    {
        return Time.time <= lastJumpPressedAt + Mathf.Max(0f, jumpBufferTime) &&
               Time.time <= lastGroundedAt + Mathf.Max(0f, coyoteTime);
    }

    public void ApplyKnockback(Vector3 sourcePosition)
    {
        Vector3 direction = transform.position - sourcePosition;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f)
            direction = -transform.forward;

        knockbackVelocity = direction.normalized * Mathf.Max(0f, knockbackSpeed);
        knockbackEndsAt = Time.time + Mathf.Max(0.01f, knockbackDuration);
        yVelocity = Mathf.Max(yVelocity, Mathf.Max(0f, knockbackUpwardSpeed));
    }

    void UpdateKnockbackVelocity()
    {
        if (Time.time >= knockbackEndsAt)
        {
            knockbackVelocity = Vector3.zero;
            return;
        }

        float duration = Mathf.Max(0.01f, knockbackDuration);
        float deceleration = Mathf.Max(0f, knockbackSpeed) / duration;
        knockbackVelocity = Vector3.MoveTowards(knockbackVelocity, Vector3.zero, deceleration * Time.deltaTime);
    }

    // ── Draw mode ──────────────────────────────────────────────────────────

    WordActionHandler ResolveWordActionHandler()
    {
        if (wordActionHandler != null)
            return wordActionHandler;

        wordActionHandler = GetComponent<WordActionHandler>();
        if (wordActionHandler != null)
            return wordActionHandler;

        if (drawController != null)
        {
            wordActionHandler =
                drawController.GetComponent<WordActionHandler>() ??
                drawController.GetComponentInParent<WordActionHandler>() ??
                drawController.GetComponentInChildren<WordActionHandler>(true);
            if (wordActionHandler != null)
                return wordActionHandler;
        }

        wordActionHandler =
            GetComponentInParent<WordActionHandler>() ??
            GetComponentInChildren<WordActionHandler>(true) ??
            FindAnyObjectByType<WordActionHandler>();

        return wordActionHandler;
    }

    public void ResetToSpawnPoint()
    {
        if (controller == null)
            controller = GetComponent<CharacterController>();

        if (controller != null)
            controller.enabled = false;

        transform.SetPositionAndRotation(spawnPosition, spawnRotation);
        horizontalVelocity = Vector3.zero;
        animatorHorizontalVelocity = Vector3.zero;
        knockbackVelocity = Vector3.zero;
        knockbackEndsAt = -999f;
        yVelocity = 0f;

        if (cameraTransform != null)
        {
            Vector3 localEuler = cameraTransform.localEulerAngles;
            xRotation = NormalizePitch(localEuler.x);
            cameraTransform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        }

        if (controller != null)
            controller.enabled = true;
    }

    void CaptureSpawnPoint()
    {
        Transform source = spawnPoint != null ? spawnPoint : transform;
        spawnPosition = source.position;
        spawnRotation = source.rotation;
    }

    static float NormalizePitch(float angle)
    {
        if (angle > 180f)
            angle -= 360f;

        return angle;
    }

    void GetPitchClampBounds(out float minPitch, out float maxPitch)
    {
        float baseMin = Mathf.Min(minCameraPitch, maxCameraPitch);
        float baseMax = Mathf.Max(minCameraPitch, maxCameraPitch);
        float midpoint = (baseMin + baseMax) * 0.5f;
        float halfRange = (baseMax - baseMin) * 0.5f * Mathf.Clamp(verticalLookRangeMultiplier, 0.25f, 1f);
        minPitch = midpoint - halfRange;
        maxPitch = midpoint + halfRange;
    }

    // ── Public API for UI ──────────────────────────────────────────────────

    /// <summary>
    /// Call from a mode-select UI button to switch modes.
    /// e.g. SetDrawMode(GetComponent<ChallengeMode>())
    /// </summary>
    public void SetDrawMode(IDrawMode mode)
    {
        if (drawController != null)
            drawController.SetMode(mode);
    }

    void ApplyRunDrawMode()
    {
        if (drawController == null)
            return;

        ChallengeMode challengeMode = ResolveDrawComponent<ChallengeMode>();
        if (challengeMode != null)
        {
            drawController.SetMode(challengeMode);
            return;
        }

        if (defaultMode is IDrawMode mode)
            drawController.SetMode(mode);
    }

    T ResolveDrawComponent<T>() where T : MonoBehaviour, IDrawMode
    {
        if (defaultMode is T defaultModeComponent)
            return defaultModeComponent;

        if (drawController != null)
        {
            T component = drawController.GetComponent<T>();
            if (component != null)
                return component;

            component = drawController.GetComponentInParent<T>();
            if (component != null)
                return component;

            component = drawController.GetComponentInChildren<T>(true);
            if (component != null)
                return component;
        }

        T localComponent = GetComponent<T>();
        if (localComponent != null)
            return localComponent;

        return GetComponentInChildren<T>(true);
    }

    // ── Cursor helpers ─────────────────────────────────────────────────────

    void LockCursor()
    {
        if (RunEndScreenController.IsOpen || IsGameplayInputPaused())
        {
            FreeCursor();
            return;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }

    void FreeCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
    }

    float GetEffectiveSpeed()
    {
        return baseSpeed;
    }

    bool IsGameplayInputPaused()
    {
        return PauseMenuController.IsPaused || ChestMiniGameState.IsOpen || NpcDialogueUI.IsOpen;
    }

    bool IsLookInputBlocked()
    {
        return IsGameplayInputPaused() || battleInputLocked;
    }

    void HandleBattleLockedMovement()
    {
        moveInput = Vector2.zero;
        mobileMoveInput = Vector2.zero;
        horizontalVelocity = Vector3.zero;
        knockbackVelocity = Vector3.zero;
        knockbackEndsAt = -999f;
        lastJumpPressedAt = -999f;

        bool groundedBeforeMove = controller.isGrounded;
        if (groundedBeforeMove)
        {
            lastGroundedAt = Time.time;
            if (yVelocity < 0f)
                yVelocity = -2f;
        }
        else
        {
            float gravityScale = yVelocity < 0f ? Mathf.Max(1f, fallGravityMultiplier) : 1f;
            yVelocity = Mathf.Max(terminalVelocity, yVelocity + gravity * gravityScale * Time.deltaTime);
        }

        Vector3 positionBeforeMove = transform.position;
        controller.Move(Vector3.up * yVelocity * Time.deltaTime);
        animatorHorizontalVelocity = GetHorizontalVelocitySince(positionBeforeMove);

        bool groundedAfterMove = controller.isGrounded;
        if (!wasGrounded && groundedAfterMove)
            TriggerAnimator("Land");

        if (wasMoving)
            TriggerMovementState(false);

        wasGrounded = groundedAfterMove;
        wasMoving = false;
    }

    void UpdateAnimator()
    {
        if (animator == null)
            return;

        Vector3 localVelocity = transform.InverseTransformDirection(animatorHorizontalVelocity);
        SetAnimatorFloat("MoveSpeed", animatorHorizontalVelocity.magnitude);
        SetAnimatorFloat("MoveX", localVelocity.x);
        SetAnimatorFloat("MoveY", localVelocity.z);
        SetAnimatorFloat("VerticalSpeed", yVelocity);
        SetAnimatorBool("Grounded", controller != null && controller.isGrounded);
        SetAnimatorBool("Casting", isDrawingMode);
    }

    void TriggerMovementState(bool isMoving)
    {
        string triggerToSet = isMoving ? moveTriggerName : stopTriggerName;
        string triggerToReset = isMoving ? stopTriggerName : moveTriggerName;

        if (!string.Equals(triggerToReset, triggerToSet, System.StringComparison.Ordinal))
            ResetConfiguredAnimatorTrigger(triggerToReset);

        TriggerConfiguredAnimatorTrigger(triggerToSet);
    }

    Vector3 GetHorizontalVelocitySince(Vector3 previousPosition)
    {
        float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 displacement = transform.position - previousPosition;
        displacement.y = 0f;
        return displacement / deltaTime;
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

    void TriggerAnimator(string parameter)
    {
        if (HasAnimatorParameter(parameter, AnimatorControllerParameterType.Trigger))
            animator.SetTrigger(parameter);
    }

    void TriggerConfiguredAnimatorTrigger(string parameter)
    {
        if (!string.IsNullOrWhiteSpace(parameter))
            TriggerAnimator(parameter);
    }

    void ResetAnimatorTrigger(string parameter)
    {
        if (HasAnimatorParameter(parameter, AnimatorControllerParameterType.Trigger))
            animator.ResetTrigger(parameter);
    }

    void ResetConfiguredAnimatorTrigger(string parameter)
    {
        if (!string.IsNullOrWhiteSpace(parameter))
            ResetAnimatorTrigger(parameter);
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
}
