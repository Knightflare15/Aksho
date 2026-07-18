using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class BattleEncounterController : MonoBehaviour
{
    [Header("References")]
    public EnemyWaveDirector waveDirector;
    public PlayerController playerController;
    public CreatureCombatController creatureCombat;
    public Camera battleCamera;

    [Header("Battle Presentation")]
    public bool enableBattlePresentation = true;
    public bool lockPlayerMovement = true;
    public bool useFixedBattleCamera = true;
    public bool disableCameraFlyController = true;
    [Min(0f)] public float cameraTransitionSeconds = 0.45f;
    [Min(2f)] public float sideCameraDistance = 13f;
    [Min(1f)] public float cameraHeight = 5.5f;
    [Min(0.25f)] public float lookHeight = 1.4f;
    [Range(25f, 75f)] public float battleFieldOfView = 43f;

    bool battleActive;
    bool cameraStateSaved;
    Transform savedCameraParent;
    Vector3 savedCameraLocalPosition;
    Quaternion savedCameraLocalRotation;
    Vector3 savedCameraWorldPosition;
    Quaternion savedCameraWorldRotation;
    float savedCameraFov;
    float savedCameraOrthographicSize;
    bool savedCameraOrthographic;
    CameraFlyController disabledFlyController;
    bool disabledFlyControllerWasEnabled;
    Coroutine cameraRoutine;

    public bool BattleActive => battleActive;

    void Awake()
    {
        ResolveReferences();
    }

    void OnEnable()
    {
        ResolveReferences();
        Subscribe();
    }

    void OnDisable()
    {
        Unsubscribe();
        EndBattlePresentation(null);
    }

    void LateUpdate()
    {
        if (!battleActive || !useFixedBattleCamera || cameraRoutine != null)
            return;

        ApplyBattleCameraPose();
    }

    public void BeginBattlePresentation(WaveDescriptor descriptor)
    {
        if (!enableBattlePresentation)
            return;

        ResolveReferences();
        if (battleActive)
            return;

        battleActive = true;
        if (lockPlayerMovement && playerController != null)
            playerController.SetBattleInputLocked(true);
        if (creatureCombat != null)
            creatureCombat.enabledForPhrases = true;

        if (useFixedBattleCamera)
        {
            SaveCameraState();
            DisableFlyController();
            if (cameraRoutine != null)
                StopCoroutine(cameraRoutine);
            cameraRoutine = StartCoroutine(TransitionCameraToBattle());
        }
    }

    public void EndBattlePresentation(WaveDescriptor descriptor)
    {
        if (!battleActive && !cameraStateSaved)
            return;

        battleActive = false;
        if (cameraRoutine != null)
        {
            StopCoroutine(cameraRoutine);
            cameraRoutine = null;
        }

        if (lockPlayerMovement && playerController != null)
            playerController.SetBattleInputLocked(false);

        RestoreCameraState();
        RestoreFlyController();
    }

    IEnumerator TransitionCameraToBattle()
    {
        Camera camera = ResolveBattleCamera();
        if (camera == null)
        {
            cameraRoutine = null;
            yield break;
        }

        Vector3 startPosition = camera.transform.position;
        Quaternion startRotation = camera.transform.rotation;
        float startFov = camera.fieldOfView;
        float duration = Mathf.Max(0f, cameraTransitionSeconds);
        float elapsed = 0f;

        while (battleActive && elapsed < duration)
        {
            ResolveBattleCameraPose(out Vector3 targetPosition, out Quaternion targetRotation);
            float t = duration <= 0.001f ? 1f : Mathf.SmoothStep(0f, 1f, elapsed / duration);
            camera.transform.SetPositionAndRotation(
                Vector3.Lerp(startPosition, targetPosition, t),
                Quaternion.Slerp(startRotation, targetRotation, t));
            camera.fieldOfView = Mathf.Lerp(startFov, battleFieldOfView, t);
            camera.orthographic = false;

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (battleActive)
            ApplyBattleCameraPose();
        cameraRoutine = null;
    }

    void ApplyBattleCameraPose()
    {
        Camera camera = ResolveBattleCamera();
        if (camera == null)
            return;

        ResolveBattleCameraPose(out Vector3 targetPosition, out Quaternion targetRotation);
        camera.transform.SetPositionAndRotation(targetPosition, targetRotation);
        camera.orthographic = false;
        camera.fieldOfView = battleFieldOfView;
    }

    void ResolveBattleCameraPose(out Vector3 position, out Quaternion rotation)
    {
        Transform player = playerController != null ? playerController.transform : null;
        Vector3 playerPosition = player != null ? player.position : transform.position;
        Vector3 enemyCenter = ResolveEnemyCenter(playerPosition, out bool hasEnemy);
        Vector3 battleLine = enemyCenter - playerPosition;
        battleLine.y = 0f;
        if (battleLine.sqrMagnitude < 0.01f)
        {
            battleLine = player != null ? player.forward : transform.forward;
            battleLine.y = 0f;
        }
        if (battleLine.sqrMagnitude < 0.01f)
            battleLine = Vector3.forward;
        battleLine.Normalize();

        Vector3 midpoint = hasEnemy
            ? (playerPosition + enemyCenter) * 0.5f
            : playerPosition + battleLine * 4f;
        Vector3 side = Vector3.Cross(Vector3.up, battleLine).normalized;
        Camera camera = ResolveBattleCamera();
        if (camera != null && Vector3.Dot(side, camera.transform.position - midpoint) < 0f)
            side = -side;

        position = midpoint + side * Mathf.Max(2f, sideCameraDistance) + Vector3.up * Mathf.Max(1f, cameraHeight);
        Vector3 lookTarget = midpoint + Vector3.up * Mathf.Max(0.25f, lookHeight);
        Vector3 lookDirection = lookTarget - position;
        rotation = lookDirection.sqrMagnitude > 0.001f
            ? Quaternion.LookRotation(lookDirection.normalized, Vector3.up)
            : Quaternion.identity;
    }

    Vector3 ResolveEnemyCenter(Vector3 playerPosition, out bool hasEnemy)
    {
        SpellTarget[] targets = FindObjectsByType<SpellTarget>(FindObjectsInactive.Exclude);
        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (SpellTarget target in targets)
        {
            if (target == null || target.IsDefeated)
                continue;
            if (Vector3.Distance(playerPosition, target.transform.position) > 120f)
                continue;

            sum += target.transform.position;
            count++;
        }

        hasEnemy = count > 0;
        if (hasEnemy)
            return sum / count;

        Transform player = playerController != null ? playerController.transform : null;
        Vector3 forward = player != null ? player.forward : transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f)
            forward = Vector3.forward;
        return playerPosition + forward.normalized * 8f;
    }

    void SaveCameraState()
    {
        Camera camera = ResolveBattleCamera();
        if (camera == null || cameraStateSaved)
            return;

        Transform cameraTransform = camera.transform;
        savedCameraParent = cameraTransform.parent;
        savedCameraLocalPosition = cameraTransform.localPosition;
        savedCameraLocalRotation = cameraTransform.localRotation;
        savedCameraWorldPosition = cameraTransform.position;
        savedCameraWorldRotation = cameraTransform.rotation;
        savedCameraFov = camera.fieldOfView;
        savedCameraOrthographicSize = camera.orthographicSize;
        savedCameraOrthographic = camera.orthographic;
        cameraStateSaved = true;
    }

    void RestoreCameraState()
    {
        Camera camera = ResolveBattleCamera();
        if (camera == null || !cameraStateSaved)
            return;

        Transform cameraTransform = camera.transform;
        if (cameraTransform.parent == savedCameraParent)
            cameraTransform.SetLocalPositionAndRotation(savedCameraLocalPosition, savedCameraLocalRotation);
        else
            cameraTransform.SetPositionAndRotation(savedCameraWorldPosition, savedCameraWorldRotation);
        camera.fieldOfView = savedCameraFov;
        camera.orthographicSize = savedCameraOrthographicSize;
        camera.orthographic = savedCameraOrthographic;
        cameraStateSaved = false;
    }

    void DisableFlyController()
    {
        if (!disableCameraFlyController)
            return;

        Camera camera = ResolveBattleCamera();
        disabledFlyController = camera != null ? camera.GetComponent<CameraFlyController>() : null;
        if (disabledFlyController == null)
            return;

        disabledFlyControllerWasEnabled = disabledFlyController.enabled;
        disabledFlyController.enabled = false;
    }

    void RestoreFlyController()
    {
        if (disabledFlyController == null)
            return;

        disabledFlyController.enabled = disabledFlyControllerWasEnabled;
        disabledFlyController = null;
    }

    Camera ResolveBattleCamera()
    {
        if (battleCamera != null)
            return battleCamera;

        if (playerController != null && playerController.cameraTransform != null)
            battleCamera = playerController.cameraTransform.GetComponent<Camera>();
        if (battleCamera == null)
            battleCamera = Camera.main;
        return battleCamera;
    }

    void Subscribe()
    {
        if (waveDirector == null)
            return;

        waveDirector.OnWaveStarted -= BeginBattlePresentation;
        waveDirector.OnEncounterEnded -= EndBattlePresentation;
        waveDirector.OnWaveStarted += BeginBattlePresentation;
        waveDirector.OnEncounterEnded += EndBattlePresentation;
    }

    void Unsubscribe()
    {
        if (waveDirector == null)
            return;

        waveDirector.OnWaveStarted -= BeginBattlePresentation;
        waveDirector.OnEncounterEnded -= EndBattlePresentation;
    }

    void EndBattlePresentation(WaveDescriptor descriptor, EncounterOutcome outcome)
    {
        EndBattlePresentation(descriptor);
    }

    void ResolveReferences()
    {
        waveDirector ??= GetComponent<EnemyWaveDirector>() ?? FindAnyObjectByType<EnemyWaveDirector>();
        playerController ??= FindAnyObjectByType<PlayerController>();
        creatureCombat ??= playerController != null
            ? playerController.GetComponent<CreatureCombatController>() ?? playerController.GetComponentInChildren<CreatureCombatController>(true)
            : FindAnyObjectByType<CreatureCombatController>();
        ResolveBattleCamera();
    }
}
