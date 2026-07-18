using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
[AddComponentMenu("The Script/Debug/Camera Fly Controller")]
public class CameraFlyController : MonoBehaviour
{
    [Header("Movement")]
    [Min(0.1f)] public float moveSpeed = 18f;
    [Min(1f)] public float fastMultiplier = 3f;
    public bool useUnscaledTime = true;

    [Header("Look")]
    [Min(0.01f)] public float mouseSensitivity = 0.12f;
    [Range(1f, 89f)] public float maxPitch = 85f;
    public bool lockCursorOnEnable = true;
    public bool requireRightMouseButton = false;

    float yaw;
    float pitch;

    void OnEnable()
    {
        Vector3 euler = transform.eulerAngles;
        yaw = euler.y;
        pitch = euler.x > 180f ? euler.x - 360f : euler.x;

        if (lockCursorOnEnable)
            SetCursorLocked(true);
    }

    void OnDisable()
    {
        if (lockCursorOnEnable)
            SetCursorLocked(false);
    }

    void Update()
    {
        HandleCursorLock();
        HandleLook();
        HandleMovement();
    }

    void HandleCursorLock()
    {
        if (WasEscapePressed())
            SetCursorLocked(false);
        else if (lockCursorOnEnable && WasMousePressed())
            SetCursorLocked(true);
    }

    void HandleLook()
    {
        if (requireRightMouseButton && !IsRightMouseHeld())
            return;

        Vector2 delta = ReadMouseDelta();
        if (delta.sqrMagnitude <= 0f)
            return;

        yaw += delta.x * mouseSensitivity;
        pitch = Mathf.Clamp(pitch - delta.y * mouseSensitivity, -maxPitch, maxPitch);
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    void HandleMovement()
    {
        Vector3 input = ReadMoveInput();
        if (input.sqrMagnitude > 1f)
            input.Normalize();

        if (input.sqrMagnitude <= 0f)
            return;

        float speed = moveSpeed * (IsFastHeld() ? fastMultiplier : 1f);
        float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        transform.position += transform.TransformDirection(input) * speed * deltaTime;
    }

    static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    static Vector3 ReadMoveInput()
    {
        float x = 0f;
        float y = 0f;
        float z = 0f;

        if (IsKeyHeld(KeyCode.A))
            x -= 1f;
        if (IsKeyHeld(KeyCode.D))
            x += 1f;
        if (IsKeyHeld(KeyCode.S))
            z -= 1f;
        if (IsKeyHeld(KeyCode.W))
            z += 1f;
        if (IsKeyHeld(KeyCode.Space))
            y += 1f;
        if (IsKeyHeld(KeyCode.LeftControl) || IsKeyHeld(KeyCode.RightControl))
            y -= 1f;

        return new Vector3(x, y, z);
    }

    static Vector2 ReadMouseDelta()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
            return Mouse.current.delta.ReadValue();
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
#else
        return Vector2.zero;
#endif
    }

    static bool IsKeyHeld(KeyCode key)
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKey(key))
            return true;
#endif
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return false;

        return key switch
        {
            KeyCode.W => keyboard.wKey.isPressed,
            KeyCode.A => keyboard.aKey.isPressed,
            KeyCode.S => keyboard.sKey.isPressed,
            KeyCode.D => keyboard.dKey.isPressed,
            KeyCode.Space => keyboard.spaceKey.isPressed,
            KeyCode.LeftControl => keyboard.leftCtrlKey.isPressed,
            KeyCode.RightControl => keyboard.rightCtrlKey.isPressed,
            KeyCode.LeftShift => keyboard.leftShiftKey.isPressed,
            KeyCode.RightShift => keyboard.rightShiftKey.isPressed,
            KeyCode.Escape => keyboard.escapeKey.isPressed,
            _ => false,
        };
#else
        return false;
#endif
    }

    static bool IsFastHeld()
    {
        return IsKeyHeld(KeyCode.LeftShift) || IsKeyHeld(KeyCode.RightShift);
    }

    static bool WasEscapePressed()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.Escape))
            return true;
#endif
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        return false;
#endif
    }

    static bool WasMousePressed()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
            return true;
#endif
#if ENABLE_INPUT_SYSTEM
        Mouse mouse = Mouse.current;
        return mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame);
#else
        return false;
#endif
    }

    static bool IsRightMouseHeld()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButton(1))
            return true;
#endif
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.rightButton.isPressed;
#else
        return false;
#endif
    }
}
