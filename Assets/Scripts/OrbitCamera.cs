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

    [Header("Pivot Rotasi")]
    public Transform orbitCenterTarget;   // Drag CameraOrbitPivot di sini
    private Vector3 orbitCenter;


    [Header("Orbit Settings")]
    public float initialYaw = 180f;
    public float initialPitch = 35f;
    public float rotationSpeed = 5f;
    public float distance = 12f;
    public float minDistance = 3f;
    public float maxDistance = 30f;
    public float zoomSpeed = 3f;

    [Header("Framing")]
    [Tooltip("Auto-atur jarak kamera agar seluruh rumah (GO bernama 'House') terlihat. Matikan agar bisa set 'distance' manual via Inspector.")]
    public bool autoFrame = true;

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
    public float touchPitchSensitivity = 0.5f;
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
        yaw = initialYaw;
        pitch = initialPitch;
        panOffset = Vector3.zero;
        _initialDistance = distance;

        if (target == null)
        {
            GameObject pivot = new GameObject("CameraTarget");
            pivot.transform.position = Vector3.zero;
            target = pivot.transform;
        }
        if (orbitCenterTarget != null)
            orbitCenter = orbitCenterTarget.position;
        else
        {
            GameObject hc = GameObject.Find("HouseCenter");
            if (hc != null)
            {
                orbitCenterTarget = hc.transform;
                orbitCenter = hc.transform.position;
            }
            else
            {
                orbitCenter = (target != null) ? target.position + targetOffset : Vector3.zero;
            }
        }

        if (autoFrame)
            TryAutoFrame();
    }

    /// <summary>Hitung bounds gabungan semua renderer lalu set jarak kamera agar rumah terlihat penuh.</summary>
    void TryAutoFrame()
    {
        GameObject house = GameObject.Find("House");
        if (house == null) return;

        Renderer[] rends = house.GetComponentsInChildren<Renderer>(true);
        if (rends.Length == 0) return;

        Bounds b = rends[0].bounds;
        foreach (Renderer r in rends)
            b.Encapsulate(r.bounds);

        if (b.size == Vector3.zero) return;

        float extents = b.extents.magnitude;
        distance = extents * 2.8f;
        maxDistance = Mathf.Max(maxDistance, distance * 2f);
        minDistance = extents * 0.6f;
        _initialDistance = distance;
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

        // Jeda rotasi kamera saat menempatkan: 1-jari dipakai ghost/panel/baterai.
        // Zoom (HandleZoom) tetap aktif; pan 1-jari juga dijeda di HandlePan.
        if (IsPlacementActive())
            return;

        if (player.RightClick.IsPressed())
        {
            Vector2 delta = player.MouseDelta.ReadValue<Vector2>();
            yaw += delta.x * rotationSpeed * 0.1f;
            pitch -= delta.y * rotationSpeed * 0.1f;
            pitch = Mathf.Clamp(pitch, minVerticalAngle, maxVerticalAngle);
        }

        // 2-jari twist (1 atas 1 bawah) = putar yaw (horizontal)
        if (TouchGestureController.RotateDelta != 0f)
        {
            yaw += TouchGestureController.RotateDelta * touchRotateSensitivity;
        }

        // 2-jari geser vertikal bersamaan = putar pitch (hanya bila twist & pinch tidak dominan)
        Vector2 twoDrag = TouchGestureController.TwoFingerDragDelta;
        if (Mathf.Abs(TouchGestureController.RotateDelta) < 0.1f &&
            Mathf.Abs(TouchGestureController.PinchDelta) < 1f &&
            twoDrag.y != 0f)
        {
            pitch -= twoDrag.y * touchPitchSensitivity;
            pitch = Mathf.Clamp(pitch, minVerticalAngle, maxVerticalAngle);
        }
    }

    /// <summary>True jika salah satu mode penempatan aktif (furnitur/satelit/baterai).</summary>
    static bool IsPlacementActive()
    {
        if (FurniturePlacementController.Instance != null && FurniturePlacementController.Instance.IsPlacing)
            return true;
        if (SolarPanelInstaller.Instance != null && SolarPanelInstaller.Instance.IsRoofMode)
            return true;
        if (BatteryInstaller.Instance != null && BatteryInstaller.Instance.IsInstallMode)
            return true;
        return false;
    }

    void HandleZoom()
    {
        Vector2 scroll = GameInput.Player.Zoom.ReadValue<Vector2>();
        if (Mathf.Abs(scroll.y) > 0.01f)
        {
            distance -= scroll.y * zoomSpeed * 0.01f;
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
        }

        if (TouchGestureController.PinchDelta != 0f)
        {
            distance -= TouchGestureController.PinchDelta * touchZoomSensitivity;
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
        }
    }

    void HandlePan()
    {
        // Jeda pan saat menempatkan: 1-jari dipakai ghost/panel/baterai.
        if (IsPlacementActive())
            return;

        if (GameInput.Player.MiddleClick.IsPressed())
        {
            Vector2 delta = GameInput.Player.MouseDelta.ReadValue<Vector2>();
            float h = -delta.x * panSpeed * 0.01f;
            float v = -delta.y * panSpeed * 0.01f;
            panOffset += transform.right * h + transform.up * v;
        }

        // 1-jari drag = geser pandangan (One-Finger Map Pan, seperti The Sims Mobile).
        // Proporsional antar-frame (first-order), tanpa raycast/anchor → tanpa loop umpan balik.
        // Hanya saat PERSIS 1 sentuhan → tidak bentrok dengan zoom/pitch (2 jari).
        if (GameInput.Player.Click.IsPressed() && TouchGestureController.ActiveTouchCount == 1)
        {
            Vector2 d = TouchGestureController.DragDelta;
            if (d != Vector2.zero)
            {
                Vector3 right = transform.right;                       // sudah horizontal
                Vector3 fwd   = transform.forward; fwd.y = 0; fwd.Normalize(); // proyeksi lantai
                float speed   = touchPanSensitivity * (distance / _initialDistance);
                panOffset -= (right * d.x + fwd * d.y) * speed;
            }
        }
    }

    void HandleWASD()
    {
        Vector2 move = GameInput.Player.Move.ReadValue<Vector2>();
        float moveY = GameInput.Player.MoveY.ReadValue<float>();
        bool sprint = GameInput.Player.Sprint.IsPressed();

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
        Vector3 right = transform.right; right.y = 0; right.Normalize();

        panOffset += (right * h + forward * v + Vector3.up * upDown) * speed;
    }

    void ApplyTransform()
    {

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0);

        // PIVOT ROTASI = orbitCenter (TETAP, tidak dipengaruhi panOffset)
        // Baca posisi live agar pivot (HouseCenter) yang dipindah tetap diikuti.
        Vector3 pivot = (orbitCenterTarget != null) ? orbitCenterTarget.position
                                                    : orbitCenter;

        // panOffset = translasi DUNIA (horizontal). Tidak diputar ulang.
        Vector3 position = pivot - rotation * Vector3.forward * distance;
        position += panOffset;

        transform.rotation = rotation;
        transform.position = position;

    }

    public void FocusOnPosition(Vector3 worldPosition, float duration = 1.2f)
    {
        transitionStartOffset = panOffset;
        Vector3 pivot = (orbitCenterTarget != null) ? orbitCenterTarget.position : orbitCenter;
        transitionTargetPos = worldPosition - pivot;
        transitionDuration = duration;
        transitionElapsed = 0f;
        isTransitioning = true;
    }

    public void ResetToOrigin(float duration = 1.2f)
    {
        yaw = initialYaw;
        pitch = initialPitch;
        distance = _initialDistance;
        panOffset = Vector3.zero;
        Vector3 pivot = (orbitCenterTarget != null) ? orbitCenterTarget.position : orbitCenter;
        FocusOnPosition(pivot, duration);
    }

    public void SnapToCurrentOrbitState()
    {
        isTransitioning = false;
        panOffset = Vector3.zero;

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0);
        Vector3 pivot = (orbitCenterTarget != null) ? orbitCenterTarget.position : orbitCenter;
        transform.rotation = rotation;
        transform.position = pivot + panOffset - rotation * Vector3.forward * distance;
    }

    public Vector3 GetOrbitPosition()
    {
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0);
        Vector3 pivot = (orbitCenterTarget != null) ? orbitCenterTarget.position : orbitCenter;
        return pivot + panOffset - rotation * Vector3.forward * distance;
    }

    public Quaternion GetOrbitRotation()
    {
        return Quaternion.Euler(pitch, yaw, 0);
    }

    // ── API publik untuk installer (satelit/baterai) ───────────────────────
    // Menggantikan reflection hack ke field private yaw/pitch/panOffset.

    public Vector3 CurrentPivot()
    {
        return (orbitCenterTarget != null) ? orbitCenterTarget.position : orbitCenter;
    }

    /// <summary>Atur sumbu presisi agar kamera tepat di posisi yang diberikan (tanpa transisi).</summary>
    public void SetOrbitView(Vector3 camPos, float newYaw, float newPitch, float newDist)
    {
        yaw        = newYaw;
        pitch      = Mathf.Clamp(newPitch, minVerticalAngle, maxVerticalAngle);
        distance   = Mathf.Clamp(newDist, minDistance, maxDistance);
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0);
        panOffset = camPos - CurrentPivot() + rot * Vector3.forward * distance;
        isTransitioning = false;
        ApplyTransform();
    }

    /// <summary>Arahkan fokus ke titik dunia (mis. tengah atap) untuk mode panel surya.</summary>
    public void SetRoofView(Vector3 focusPoint, float newYaw, float newPitch, float newDist)
    {
        yaw        = newYaw;
        pitch      = Mathf.Clamp(newPitch, minVerticalAngle, maxVerticalAngle);
        distance   = Mathf.Clamp(newDist, minDistance, maxDistance);
        panOffset  = focusPoint - CurrentPivot();
        isTransitioning = false;
        ApplyTransform();
    }

    /// <summary>Kembali ke pandangan overview (panOffset = 0) dengan sudut/jarak tersimpan.</summary>
    public void RestoreOrbitView(float savedYaw, float savedPitch, float savedDist)
    {
        yaw        = savedYaw;
        pitch      = savedPitch;
        distance   = savedDist;
        panOffset  = Vector3.zero;
        isTransitioning = false;
        ApplyTransform();
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
