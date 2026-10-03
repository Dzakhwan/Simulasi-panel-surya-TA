using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Kamera orbit 360° — klik kanan + drag untuk putar, scroll untuk zoom.
/// Attach ke Main Camera.  Set 'target' ke titik tengah rumah.
/// </summary>
public class OrbitCamera : MonoBehaviour
{
    [Header("Target")]
    public Transform target;
    public Vector3 targetOffset = Vector3.up;

    [Header("Orbit Settings")]
    public float initialYaw    = 180f;
    public float initialPitch  = 35f;
    public float rotationSpeed = 5f;
    public float distance      = 12f;
    public float minDistance    = 3f;
    public float maxDistance    = 30f;
    public float zoomSpeed     = 3f;

    [Header("Vertical Limits (derajat)")]
    public float minVerticalAngle = 10f;
    public float maxVerticalAngle = 80f;

    [Header("Pan (Geser)")]
    public float panSpeed = 0.3f;
    public float wasdSpeed = 500f;
    public float flyForce = 5f;
    Vector3 velocity;

    [Header("Touch Sensitivity")]
    public float touchPanSensitivity = 0.02f;
    public float touchRotateSensitivity = 1.5f;
    public float touchZoomSensitivity = 0.05f;

    [HideInInspector] public bool freeMode = false;
    [HideInInspector] public bool inputLocked = false;

    private float yaw;
    private float pitch;
    private Vector3 panOffset;
    private float _initialDistance;

    private bool isTransitioning = false;
    private Vector3 transitionTargetPos;
    private float transitionDuration;
    private float transitionElapsed;
    private Vector3 transitionStartOffset;

    void Start()
    {
        yaw   = initialYaw;
        pitch = initialPitch;
        panOffset = Vector3.zero;
        _initialDistance = distance;

        if (target == null)
        {
            GameObject pivot = new GameObject("CameraTarget");
            pivot.transform.position = Vector3.zero;
            target = pivot.transform;
        }
    }

    void LateUpdate()
    {
        if (target == null) return;

        if (isTransitioning)
        {
            HandleTransition();
            return;
        }

        if (!inputLocked)
        {
            HandleRotation();
            HandleZoom();
            HandlePan();

            if (freeMode)
                HandleWASD();
        }

        ApplyTransform();
    }

    void HandleRotation()
    {
        var player = GameInput.Player;

        if (player.RightClick.IsPressed())
        {
            Vector2 delta = player.MouseDelta.ReadValue<Vector2>();
            yaw   += delta.x * rotationSpeed * 0.1f;
            pitch -= delta.y * rotationSpeed * 0.1f;
            pitch  = Mathf.Clamp(pitch, minVerticalAngle, maxVerticalAngle);
        }

        if (TouchGestureController.RotateDelta != 0f)
        {
            yaw += TouchGestureController.RotateDelta * touchRotateSensitivity;
        }
    }

    void HandleZoom()
    {
        Vector2 scroll = GameInput.Player.Zoom.ReadValue<Vector2>();
        if (Mathf.Abs(scroll.y) > 0.01f)
        {
            distance -= scroll.y * zoomSpeed * 0.01f;
            distance  = Mathf.Clamp(distance, minDistance, maxDistance);
        }

        if (TouchGestureController.PinchDelta != 0f)
        {
            distance -= TouchGestureController.PinchDelta * touchZoomSensitivity;
            distance  = Mathf.Clamp(distance, minDistance, maxDistance);
        }
    }

    void HandlePan()
    {
        if (GameInput.Player.MiddleClick.IsPressed())
        {
            Vector2 delta = GameInput.Player.MouseDelta.ReadValue<Vector2>();
            float h = -delta.x * panSpeed * 0.01f;
            float v = -delta.y * panSpeed * 0.01f;
            panOffset += transform.right * h + transform.up * v;
        }

        if (TouchGestureController.DragDelta != Vector2.zero)
        {
            Vector2 drag = TouchGestureController.DragDelta;
            panOffset -= transform.right * drag.x * touchPanSensitivity;
            panOffset -= transform.up    * drag.y * touchPanSensitivity;
        }
    }

    void HandleWASD()
    {
        Vector2 move = GameInput.Player.Move.ReadValue<Vector2>();
        float moveY  = GameInput.Player.MoveY.ReadValue<float>();
        bool sprint  = GameInput.Player.Sprint.IsPressed();

        if (VirtualJoystick.Instance != null && VirtualJoystick.Instance.gameObject.activeSelf)
        {
            if (move == Vector2.zero)
                move = VirtualJoystick.MoveInput;
            if (moveY == 0f)
                moveY = VirtualJoystick.MoveYInput;
        }

        float h = move.x;
        float v = move.y;
        float upDown = moveY;

        float speed = wasdSpeed * Time.deltaTime;
        if (sprint) speed *= 2.5f;

        Vector3 forward = transform.forward; forward.y = 0; forward.Normalize();
        Vector3 right   = transform.right;   right.y   = 0; right.Normalize();

        panOffset += (right * h + forward * v + Vector3.up * upDown) * speed;
    }

    void ApplyTransform()
    {
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0);
        Vector3 focusPoint  = target.position + targetOffset + panOffset;
        Vector3 position    = focusPoint - rotation * Vector3.forward * distance;

        transform.rotation = rotation;
        transform.position = position;
    }

    public void FocusOnPosition(Vector3 worldPosition, float duration = 1.2f)
    {
        transitionStartOffset = panOffset;
        transitionTargetPos = worldPosition - target.position - targetOffset;
        transitionDuration = duration;
        transitionElapsed = 0f;
        isTransitioning = true;
    }

    public void ResetToOrigin(float duration = 1.2f)
    {
        yaw      = initialYaw;
        pitch    = initialPitch;
        distance = _initialDistance;
        panOffset = Vector3.zero;
        FocusOnPosition(target.position + targetOffset, duration);
    }

    public void SnapToCurrentOrbitState()
    {
        isTransitioning = false;
        panOffset       = Vector3.zero;
        if (target == null) return;

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0);
        Vector3 focusPoint  = target.position + targetOffset;
        transform.rotation  = rotation;
        transform.position  = focusPoint - rotation * Vector3.forward * distance;
    }

    public Vector3 GetOrbitPosition()
    {
        if (target == null) return transform.position;
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0);
        Vector3 focusPoint  = target.position + targetOffset + panOffset;
        return focusPoint - rotation * Vector3.forward * distance;
    }

    public Quaternion GetOrbitRotation()
    {
        return Quaternion.Euler(pitch, yaw, 0);
    }

    void HandleTransition()
    {
        transitionElapsed += Time.deltaTime;
        float t = Mathf.Clamp01(transitionElapsed / transitionDuration);
        t = t * t * (3f - 2f * t);

        panOffset = Vector3.Lerp(transitionStartOffset, transitionTargetPos, t);

        ApplyTransform();

        if (transitionElapsed >= transitionDuration)
        {
            panOffset = transitionTargetPos;
            isTransitioning = false;
        }
    }
}
