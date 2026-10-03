using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public static VirtualJoystick Instance { get; private set; }

    public static Vector2 MoveInput { get; private set; }
    public static float MoveYInput { get; private set; }

    private RectTransform _bg;
    private RectTransform _knob;
    private Button _upBtn;
    private Button _downBtn;

    private int _joystickPointerId = -1;
    private Vector2 _bgCenter;
    private float _bgRadius;

    private float _moveY;
    private Canvas _canvas;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Update()
    {
        MoveYInput = _moveY;
    }

    public static void SetVisible(bool visible)
    {
        if (Instance != null)
            Instance.gameObject.SetActive(visible);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Application.isMobilePlatform) return;
        if (Instance != null) return;

        var canvasGo = new GameObject("[VirtualJoystick_Canvas]");
        DontDestroyOnLoad(canvasGo);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();

        var go = new GameObject("[VirtualJoystick]");
        go.transform.SetParent(canvasGo.transform, false);
        var vj = go.AddComponent<VirtualJoystick>();
        vj._canvas = canvas;
        vj.BuildUI(canvas);
        go.SetActive(false);
    }

    void BuildUI(Canvas canvas)
    {
        float screenH = Screen.height;
        float bgSize = screenH * 0.22f;
        float knobSize = bgSize * 0.45f;
        float margin = bgSize * 0.6f;

        var bgGo = new GameObject("JoystickBG");
        bgGo.transform.SetParent(transform, false);
        _bg = bgGo.AddComponent<RectTransform>();
        _bg.sizeDelta = new Vector2(bgSize, bgSize);
        _bg.anchorMin = _bg.anchorMax = new Vector2(0f, 0f);
        _bg.anchoredPosition = new Vector2(margin, margin);
        var bgImg = bgGo.AddComponent<Image>();
        bgImg.color = new Color(1f, 1f, 1f, 0.15f);
        bgGo.AddComponent<VirtualJoystickReceiver>().joystick = this;

        var knobGo = new GameObject("JoystickKnob");
        knobGo.transform.SetParent(bgGo.transform, false);
        _knob = knobGo.AddComponent<RectTransform>();
        _knob.sizeDelta = new Vector2(knobSize, knobSize);
        _knob.anchorMin = _knob.anchorMax = new Vector2(0.5f, 0.5f);
        _knob.anchoredPosition = Vector2.zero;
        var knobImg = knobGo.AddComponent<Image>();
        knobImg.color = new Color(1f, 1f, 1f, 0.35f);

        _bgRadius = bgSize * 0.4f;

        float btnSize = bgSize * 0.5f;
        float btnX = margin + bgSize + btnSize * 0.4f;

        _upBtn = CreateYBtn("▲", new Vector2(btnX, margin + bgSize * 0.55f + btnSize * 0.1f), btnSize);
        _downBtn = CreateYBtn("▼", new Vector2(btnX, margin + bgSize * 0.3f - btnSize * 0.1f), btnSize);

        _upBtn.GetComponent<UnityEngine.UI.Button>().onClick.RemoveAllListeners();
        _downBtn.GetComponent<UnityEngine.UI.Button>().onClick.RemoveAllListeners();

        var upEvents = _upBtn.gameObject.AddComponent<ButtonHoldEvents>();
        upEvents.onHold = () => _moveY = 1f;
        upEvents.onRelease = () => { if (_moveY > 0f) _moveY = 0f; };

        var downEvents = _downBtn.gameObject.AddComponent<ButtonHoldEvents>();
        downEvents.onHold = () => _moveY = -1f;
        downEvents.onRelease = () => { if (_moveY < 0f) _moveY = 0f; };
    }

    Button CreateYBtn(string label, Vector2 pos, float size)
    {
        var go = new GameObject("Btn_" + label);
        go.transform.SetParent(transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(size, size);
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.anchoredPosition = pos;
        var img = go.AddComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.2f);
        var btn = go.AddComponent<Button>();
        var txt = new GameObject("Label").AddComponent<Text>();
        txt.transform.SetParent(go.transform, false);
        txt.text = label;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = Color.white;
        txt.fontSize = Mathf.RoundToInt(size * 0.4f);
        var txtRt = txt.GetComponent<RectTransform>();
        txtRt.anchorMin = Vector2.zero;
        txtRt.anchorMax = Vector2.one;
        txtRt.offsetMin = txtRt.offsetMax = Vector2.zero;
        return btn;
    }

    public void OnPointerDown(PointerEventData e)
    {
        if (_joystickPointerId >= 0) return;
        _joystickPointerId = e.pointerId;
        RectTransformUtility.ScreenPointToWorldPointInRectangle(_bg, e.position, e.pressEventCamera, out var world);
        _bgCenter = _bg.position;
        UpdateKnob(e.position);
    }

    public void OnDrag(PointerEventData e)
    {
        if (e.pointerId != _joystickPointerId) return;
        UpdateKnob(e.position);
    }

    public void OnPointerUp(PointerEventData e)
    {
        if (e.pointerId != _joystickPointerId) return;
        _joystickPointerId = -1;
        _knob.anchoredPosition = Vector2.zero;
        MoveInput = Vector2.zero;
    }

    void UpdateKnob(Vector2 screenPos)
    {
        Vector2 local = screenPos - (Vector2)_bg.position;
        local /= _bg.lossyScale.x;
        if (local.magnitude > _bgRadius)
            local = local.normalized * _bgRadius;
        _knob.anchoredPosition = local;
        MoveInput = local / _bgRadius;
    }
}

public class VirtualJoystickReceiver : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public VirtualJoystick joystick;
    public void OnPointerDown(PointerEventData e) => joystick.OnPointerDown(e);
    public void OnDrag(PointerEventData e) => joystick.OnDrag(e);
    public void OnPointerUp(PointerEventData e) => joystick.OnPointerUp(e);
}

public class ButtonHoldEvents : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    public System.Action onHold;
    public System.Action onRelease;
    private bool _held;

    public void OnPointerDown(PointerEventData e) { _held = true; }
    public void OnPointerUp(PointerEventData e) { _held = false; onRelease?.Invoke(); }

    void Update()
    {
        if (_held) onHold?.Invoke();
    }
}
