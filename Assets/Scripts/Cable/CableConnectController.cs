using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;

/// <summary>
/// Alur sambung kabel: KETUK INVERTER dulu → lalu KETUK ALAT elektronik di dunia 3D
/// untuk menyambungkannya. Ketuk area kosong = batal.
/// Kontrol kamera tetap aktif selama mode sambung (tap vs drag sudah dibedakan).
/// </summary>
public class CableConnectController : MonoBehaviour
{
    public static CableConnectController Instance { get; private set; }

    /// <summary>True saat mode sambung aktif (dipakai controller lain untuk menahan seleksi).</summary>
    public static bool IsConnectModeActive => Instance != null && Instance._connectMode;

    [Header("UI")]
    [SerializeField] private Color bannerColor = new Color(0.12f, 0.13f, 0.16f, 0.95f);
    [SerializeField] private Color cancelColor = new Color(0.6f, 0.2f, 0.2f, 1f);

    private Camera _cam;
    private bool _connectMode;
    private Transform _activeInverter;
    private GameObject _banner;

    // Deteksi TAP (tekan-lepas tanpa geser)
    private bool _pressActive;
    private Vector2 _pressStart;
    private bool _tapMoved;
    private bool _tapReleased;
    private const float TapMaxMovement = 20f;

    // Debounce: 1 aksi per ketukan, token 9x menyambung sekaligus
    private const float ActionCooldown = 0.4f;
    private float _actionCooldown;

    // ── Bootstrap ──────────────────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (FindFirstObjectByType<CableConnectController>() == null)
        {
            var go = new GameObject("[CableConnectController]");
            go.AddComponent<CableConnectController>();
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        _cam = Camera.main;
    }

    private void Update()
    {
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;

        // Gate: simulasi jalan atau sedang placement → batalkan mode sambung
        if (SimulationManager.Instance != null && SimulationManager.Instance.IsSimulationRunning)
        {
            ExitConnectMode();
            return;
        }
        if (SimulationManager.IsAnyPlacementActive())
        {
            ExitConnectMode();
            return;
        }

        TrackPointerPeek();
        if (!_tapReleased) return;
        if (GameInput.IsPointerOverInteractiveUI(GameInput.PointerPosition)) return;
        if (_actionCooldown > 0f) { _actionCooldown -= Time.deltaTime; return; }

        Vector2 screen = GameInput.PointerPosition;

        // 1. Ketuk inverter?
        if (TryPickInverter(screen, out Transform inverter))
        {
            if (_connectMode && inverter == _activeInverter)
                ExitConnectMode();
            else
                EnterConnectMode(inverter);
            _actionCooldown = ActionCooldown;
            return;
        }

        // 2. Dalam mode sambung: ketuk alat → sambung
        if (_connectMode)
        {
            if (TryPickAppliance(screen, out GameObject instance))
            {
                var fm = FurnitureManager.Instance;
                if (fm != null && fm.TryGetDataOf(instance, out var data))
                {
                    bool ok = CableManager.Instance != null &&
                              CableManager.Instance.ConnectTo(instance, data, _activeInverter);
                    if (ok) Debug.Log($"[CableConnect] {data.furnitureName} disambungkan.");
                }
                // Tetap di mode agar bisa sambung beberapa alat
            }
            else
            {
                ExitConnectMode();
            }
            _actionCooldown = ActionCooldown;
        }
    }

    // ── Tap detection ────────────────────────────────────────────────────────

    private void TrackPointerPeek()
    {
        _tapReleased = false;

        var click = GameInput.Player.Click;

        if (click.WasPressedThisFrame())
        {
            _pressActive = true;
            _pressStart = GameInput.PointerPosition;
            _tapMoved = false;
        }
        else if (_pressActive)
        {
            if ((GameInput.PointerPosition - _pressStart).magnitude > TapMaxMovement)
                _tapMoved = true;

            if (click.WasReleasedThisFrame())
            {
                _pressActive = false;
                _tapReleased = !_tapMoved;
            }
        }
    }

    // ── Picking ──────────────────────────────────────────────────────────────

    private bool TryPickInverter(Vector2 screen, out Transform inverter)
    {
        inverter = null;
        var cm = CableManager.Instance;
        if (cm == null || _cam == null) return false;

        Ray ray = _cam.ScreenPointToRay(screen);
        if (!Physics.Raycast(ray, out RaycastHit hit, 100000f, ~0, QueryTriggerInteraction.Ignore))
            return false;

        foreach (var anchor in cm.Inverters)
        {
            if (anchor == null) continue;
            Transform root = anchor.parent != null ? anchor.parent : anchor;
            if (hit.transform == root || hit.transform.IsChildOf(root))
            {
                inverter = anchor;
                return true;
            }
        }
        return false;
    }

    private bool TryPickAppliance(Vector2 screen, out GameObject instance)
    {
        instance = null;
        var fpc = FurniturePlacementController.Instance;
        if (fpc != null && fpc.TryPickInstance(screen, out instance))
            return instance != null;
        return false;
    }

    // ── Mode ─────────────────────────────────────────────────────────────────

    private void EnterConnectMode(Transform inverter)
    {
        _connectMode = true;
        _activeInverter = inverter;
        ShowBanner();
        Debug.Log("[CableConnect] Mode sambung aktif — ketuk alat untuk menyambung, ketuk kosong untuk batal.");
    }

    private void ExitConnectMode()
    {
        if (!_connectMode && _banner == null) return;
        _connectMode = false;
        _activeInverter = null;
        if (_banner != null) _banner.SetActive(false);
    }

    // ── Banner UI ─────────────────────────────────────────────────────────────

    private void ShowBanner()
    {
        if (_banner != null) { _banner.SetActive(true); return; }

        Canvas canvas = FindOverlayCanvas();
        if (canvas == null) return;

        _banner = new GameObject("ConnectBanner",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(HorizontalLayoutGroup));
        _banner.transform.SetParent(canvas.transform, false);

        RectTransform rect = _banner.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -20f);
        rect.sizeDelta = new Vector2(620f, 64f);

        Image bg = _banner.GetComponent<Image>();
        bg.color = bannerColor;
        bg.raycastTarget = true;

        var layout = _banner.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(18, 12, 8, 8);
        layout.spacing = 12f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;
        layout.childAlignment = TextAnchor.MiddleLeft;

        AddTmpText(_banner.transform, "Msg", "Ketuk alat untuk menyambung ke inverter", 20,
            TextAlignmentOptions.Left);
        AddButton(_banner.transform, "Batal", cancelColor, ExitConnectMode, 130f);
    }

    private Canvas FindOverlayCanvas()
    {
        Canvas fallback = null;
        foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            Canvas root = c.rootCanvas;
            if (root == null) continue;
            if (fallback == null) fallback = root;
            if (root.renderMode == RenderMode.ScreenSpaceOverlay)
                return root;
        }
        return fallback;
    }

    private void AddButton(Transform parent, string label, Color color, UnityAction onClick, float width)
    {
        GameObject btnGO = new GameObject(label,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        btnGO.transform.SetParent(parent, false);

        Image img = btnGO.GetComponent<Image>();
        img.color = color;

        var layoutEl = btnGO.AddComponent<LayoutElement>();
        layoutEl.preferredWidth = width;
        layoutEl.preferredHeight = 44f;

        Button btn = btnGO.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        AddTmpText(btnGO.transform, "Text", label, 18, TextAlignmentOptions.Center);
    }

    private static void AddTmpText(Transform parent, string name, string text,
        int fontSize, TextAlignmentOptions alignment)
    {
        GameObject go = new GameObject(name,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null)
            tmp.font = TMP_Settings.defaultFontAsset;
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = alignment;
        tmp.color = Color.white;
        tmp.raycastTarget = false;

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
