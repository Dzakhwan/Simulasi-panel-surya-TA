using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

[DefaultExecutionOrder(-100)]
public class TouchGestureController : MonoBehaviour
{
    public static TouchGestureController Instance { get; private set; }

    public static Vector2 DragDelta { get; private set; }
    public static float RotateDelta { get; private set; }
    public static float PinchDelta { get; private set; }
    public static bool TappedThisFrame { get; private set; }

    private const float TapMaxDistance = 20f;
    private const float TapMaxDuration = 0.3f;

    private Vector2 _touchStartPos;
    private float _touchStartTime;
    private bool _wasTapping;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        EnhancedTouchSupport.Enable();
    }

    void OnDestroy()
    {
        if (Instance == this)
            EnhancedTouchSupport.Disable();
    }

    void Update()
    {
        DragDelta = Vector2.zero;
        RotateDelta = 0f;
        PinchDelta = 0f;
        TappedThisFrame = false;

        var touches = Touch.activeTouches;

        if (touches.Count == 1)
        {
            var t = touches[0];

            if (GameInput.IsPointerOverInteractiveUI(t.screenPosition)) return;

            if (t.phase == UnityEngine.InputSystem.TouchPhase.Began)
            {
                _touchStartPos = t.screenPosition;
                _touchStartTime = Time.time;
                _wasTapping = true;
            }

            if (t.phase == UnityEngine.InputSystem.TouchPhase.Moved)
            {
                if (Vector2.Distance(t.screenPosition, _touchStartPos) > TapMaxDistance)
                    _wasTapping = false;

                DragDelta = t.delta;
            }

            if (t.phase == UnityEngine.InputSystem.TouchPhase.Ended)
            {
                float dist = Vector2.Distance(t.screenPosition, _touchStartPos);
                float dur = Time.time - _touchStartTime;
                if (_wasTapping && dist < TapMaxDistance && dur < TapMaxDuration)
                    TappedThisFrame = true;
            }
        }
        else if (touches.Count == 2)
        {
            var t0 = touches[0];
            var t1 = touches[1];

            if (GameInput.IsPointerOverInteractiveUI(t0.screenPosition) ||
                GameInput.IsPointerOverInteractiveUI(t1.screenPosition)) return;

            Vector2 prev0 = t0.screenPosition - t0.delta;
            Vector2 prev1 = t1.screenPosition - t1.delta;

            float prevDist = Vector2.Distance(prev0, prev1);
            float currDist = Vector2.Distance(t0.screenPosition, t1.screenPosition);
            PinchDelta = currDist - prevDist;

            Vector2 prevMid = (prev0 + prev1) * 0.5f;
            Vector2 currMid = (t0.screenPosition + t1.screenPosition) * 0.5f;

            Vector2 prevDir = (prev1 - prev0).normalized;
            Vector2 currDir = (t1.screenPosition - t0.screenPosition).normalized;
            float angle = Vector2.SignedAngle(prevDir, currDir);

            float pinchMagnitude = Mathf.Abs(PinchDelta);
            float rotateMagnitude = Mathf.Abs(angle);

            if (rotateMagnitude > pinchMagnitude * 0.3f)
                RotateDelta = angle;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("[TouchGestureController]");
        go.AddComponent<TouchGestureController>();
    }
}
