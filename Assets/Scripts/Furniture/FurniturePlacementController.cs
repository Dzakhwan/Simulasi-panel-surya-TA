using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Mengelola interaksi 3D furniture:
///  1. CLICK-TO-PLACE — ghost transparan mengikuti kursor di lantai,
///     rotasi dengan Q/E, klik untuk menempatkan instance baru.
///  2. SELECTION — klik model 3D yang sudah terpasang memunculkan kartu info
///     (Atur Jam / Hapus per-instance).
/// Objek dibuat otomatis saat scene dimuat, tidak perlu di-assign di scene.
/// </summary>
public class FurniturePlacementController : MonoBehaviour
{
    public static FurniturePlacementController Instance { get; private set; }

    [Header("Placement")]
    [Tooltip("Layer untuk raycast lantai saat menempatkan. Jika 0, otomatis memakai layer 'Floor'.")]
    [SerializeField] private LayerMask floorLayerMask = 0;

    [Tooltip("Jarak placement barang")]
    [SerializeField] private float placementDistance = 900f;

    [Tooltip("Tidak dipakai lagi (fallback bidang lantai dihapus). Dipertahankan agar nilai lama tidak hilang.")]
    [SerializeField] private float floorPlaneY = 0f;

    [Tooltip("Langkah rotasi ghost per tekanan Q/E (derajat).")]
    [SerializeField] private float rotationStep = 15f;

    [Tooltip("Cooldown setelah BeginPlacement agar tap kartu tidak langsung menempatkan (detik).")]
    [SerializeField] private float placementCooldown = 0.2f;

    [Header("Info Card")]
    [Tooltip("Warna latar kartu info seleksi.")]
    [SerializeField] private Color cardBackground = new Color(0.12f, 0.13f, 0.16f, 0.96f);
    [SerializeField] private Color configButtonColor = new Color(0.15f, 0.45f, 0.85f, 1f);
    [SerializeField] private Color deleteButtonColor = new Color(0.85f, 0.2f, 0.15f, 1f);

    public bool IsPlacing { get; private set; }

    private FurnitureData _data;
    private FurnitureCardUI _sourceCard;
    private GameObject _ghost;
    private float _ghostRotY;
    private float _beginTime;
    private Camera _cam;

    // Validitas posisi ghost: true = di atas lantai & tidak menembus bangunan
    private bool _ghostValid;
    private bool _lastTintValid = true;

    private readonly Color _validTint = new Color(0.35f, 0.85f, 0.45f, 0.45f);
    private readonly Color _invalidTint = new Color(1f, 0.22f, 0.2f, 0.45f);

    // Seleksi
    private GameObject _selectedInstance;
    private GameObject _infoCard;
    private GameObject _placementUI;
    private FurnitureInstaller[] _installers = System.Array.Empty<FurnitureInstaller>();

    // Deteksi TAP (tekan-lepas tanpa geser) agar drag kamera tidak menempatkan/memilih
    private bool _pressActive;
    private Vector2 _pressStart;
    private bool _tapMoved;
    private bool _tapReleased;

    private const float TapMaxMovement = 20f;

    // ── Bootstrap ──────────────────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (FindFirstObjectByType<FurniturePlacementController>() == null)
        {
            var go = new GameObject("[FurniturePlacementController]");
            go.AddComponent<FurniturePlacementController>();
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Start()
    {
        _cam = Camera.main;
        if (floorLayerMask.value == 0)
            floorLayerMask = LayerMask.GetMask("Floor");
    }

    private void Update()
    {
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;

        TrackPointerPeek();

        if (IsPlacing)
        {
            HandlePlacement();
            return;
        }

        HandleSelection();
    }

    /// <summary>Tandai apakah pointer baru saja melakukan TAP (tanpa geser &amp; sudah lepas).</summary>
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

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>Mulai mode penempatan untuk satu jenis furnitur.</summary>
    public void BeginPlacement(FurnitureData data, FurnitureCardUI sourceCard)
    {
        if (data == null || data.prefab3D == null)
        {
            Debug.LogWarning("[FurniturePlacement] data atau prefab3D null.");
            return;
        }

        if (SimulationManager.Instance != null && SimulationManager.Instance.IsSimulationRunning)
        {
            Debug.Log("[FurniturePlacement] Tidak bisa memasang saat simulasi berjalan.");
            return;
        }

        if (!IsFurnitureModeActive())
        {
            Debug.Log("[FurniturePlacement] Masuk mode furnitur dulu.");
            return;
        }

        CancelSelection();

        _data = data;
        _sourceCard = sourceCard;
        IsPlacing = true;
        _ghostRotY = 0f;
        _beginTime = Time.time;

        if (_ghost != null) Destroy(_ghost);
        _ghost = Instantiate(data.prefab3D);
        _ghost.name = $"Ghost_{data.furnitureName}";
        _ghost.transform.localScale = data.fixedScale;

        // Matikan collider & jangan kena raycast apa pun (audio, dll.)
        foreach (var col in _ghost.GetComponentsInChildren<Collider>(true))
            col.enabled = false;
        SetLayerRecursively(_ghost, LayerMask.NameToLayer("Ignore Raycast"));

        // Transparansi via MaterialPropertyBlock (URP Lit: _BaseColor)
        foreach (var rend in _ghost.GetComponentsInChildren<Renderer>(true))
        {
            var mpb = new MaterialPropertyBlock();
            rend.GetPropertyBlock(mpb);
            Color c = rend.material.HasProperty("_BaseColor") ? rend.material.color : Color.white;
            c.a = 0.45f;
            mpb.SetColor("_BaseColor", c);
            rend.SetPropertyBlock(mpb);
        }

        _ghost.SetActive(false);
        Debug.Log($"[FurniturePlacement] Mode penempatan: {data.furnitureName}");

        // Spawn langsung di tengah layar
        Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        _ghostValid = TryGetFloorPoint(center, out Vector3 floorPoint);
        if (_ghostValid)
        {
            _ghost.transform.position = floorPoint;
            if (TryGetLowestY(_ghost, out float lowY))
                _ghost.transform.position = floorPoint + Vector3.up * (floorPoint.y - lowY);
            _ghost.SetActive(true);
            SetGhostTint(true);
        }

        ShowPlacementUI();
    }

    /// <summary>
    /// Cek apakah MODE FURNITUR sedang aktif pada salah satu FurnitureInstaller.
    /// (Scene punya installer asli + duplikat nyasar, karena itu ditentukan via scan.)
    /// </summary>
    private bool IsFurnitureModeActive()
    {
        if (_installers.Length == 0)
            _installers = FindObjectsByType<FurnitureInstaller>(FindObjectsSortMode.None);
        foreach (var inst in _installers)
            if (inst != null && inst.IsFurnitureMode)
                return true;
        return false;
    }

    /// <summary>Batalkan mode penempatan (ghost dihancurkan).</summary>
    public void CancelPlacement()
    {
        if (!IsPlacing) return;
        IsPlacing = false;

        if (_ghost != null)
        {
            Destroy(_ghost);
            _ghost = null;
        }
        if (_placementUI != null)
        {
            Destroy(_placementUI);
            _placementUI = null;
        }
        _data = null;
        _sourceCard = null;
    }

    /// <summary>Tutup kartu info seleksi (jika terbuka).</summary>
    public void CancelSelection()
    {
        if (_infoCard != null)
        {
            Destroy(_infoCard);
            _infoCard = null;
        }
        _selectedInstance = null;
    }

    // ── Placement ──────────────────────────────────────────────────────────

    private void HandlePlacement()
    {
        // Batalkan saat keluar furniture mode (mis. tombol Kembali)
        if (!IsFurnitureModeActive())
        {
            CancelPlacement();
            return;
        }

        if ((GameInput.Player.RotateLeft.WasPressedThisFrame() || TouchGestureController.RotateDelta < -0.1f))
            RotateGhost(-rotationStep);
        if ((GameInput.Player.RotateRight.WasPressedThisFrame() || TouchGestureController.RotateDelta > 0.1f))
            RotateGhost(rotationStep);

        // Update posisi ghost mengikuti pointer hanya jika di-drag
        if (_ghost != null && GameInput.Player.Click.IsPressed())
        {
            Vector2 pointer = GameInput.PointerPosition;
            if (!GameInput.IsPointerOverInteractiveUI(pointer))
            {
                _ghostValid = TryGetFloorPoint(pointer, out Vector3 floorPoint);

                if (_ghostValid)
                {
                    _ghost.SetActive(true);
                    _ghost.transform.position = floorPoint;
                    if (TryGetLowestY(_ghost, out float lowY))
                        _ghost.transform.position = floorPoint + Vector3.up * (floorPoint.y - lowY);

                    // INVALID: ada bangunan/objek di antara kamera & titik lantai
                    Ray occRay = _cam.ScreenPointToRay(pointer);
                    float distToFloor = Vector3.Distance(_cam.transform.position, floorPoint);
                    if (Physics.Raycast(occRay, out _, distToFloor - 0.1f, ~floorLayerMask, QueryTriggerInteraction.Ignore))
                        _ghostValid = false;
                }
                else
                {
                    _ghost.SetActive(false);
                }

                SetGhostTint(_ghostValid);
            }
        }

        // Batalkan dengan Esc (Cancel)
        if (GameInput.Player.Cancel.WasPressedThisFrame())
        {
            _pressActive = false;
            CancelPlacement();
            return;
        }
    }

    private void RotateGhost(float degrees)
    {
        _ghostRotY += degrees;
        if (_ghost != null)
            _ghost.transform.rotation = Quaternion.Euler(0f, _ghostRotY, 0f);
    }

    private void ShowPlacementUI()
    {
        if (_placementUI != null) return;
        Canvas canvas = FindOverlayCanvas();
        if (canvas == null) return;

        GameObject ui = new GameObject("PlacementUI", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        ui.transform.SetParent(canvas.transform, false);

        RectTransform rt = ui.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.05f);
        rt.anchorMax = new Vector2(0.5f, 0.05f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(300f, 45f);

        var layout = ui.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 20f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;

        AddButton(ui.transform, "Batal (X)", deleteButtonColor, CancelPlacement);
        AddButton(ui.transform, "Pasang (V)", new Color(0.2f, 0.7f, 0.3f, 1f), CommitPlacement);

        _placementUI = ui;
    }

    private void CommitPlacement()
    {
        if (_ghost == null || !_ghost.activeSelf || !_ghostValid) return;
        if (FurnitureManager.Instance == null) return;

        Vector3 pos = _ghost.transform.position;
        Quaternion rot = _ghost.transform.rotation;
        FurnitureData data = _data;
        FurnitureCardUI card = _sourceCard;

        CancelPlacement();

        FurnitureManager.Instance.AddFurniture(data, pos, rot);
        card?.OnPlacementComplete();
    }

    private bool TryGetFloorPoint(Vector2 screenPos, out Vector3 point)
    {
        point = Vector3.zero;
        if (_cam == null) return false;

        Ray ray = _cam.ScreenPointToRay(screenPos);

        // Hanya raycast ke layer lantai. Tanpa lantai (mask 0) → posisi tidak valid,
        // sehingga furnitur tidak bisa ditempatkan sembarangan.
        if (floorLayerMask.value != 0
            && Physics.Raycast(ray, out RaycastHit hit, placementDistance, floorLayerMask, QueryTriggerInteraction.Ignore))
        {
            point = hit.point;
            return true;
        }

        return false;
    }

    /// <summary>Ubah warna tint ghost: hijau = valid (siap dipasang), merah = tidak valid.</summary>
    private void SetGhostTint(bool valid)
    {
        if (_ghost == null || valid == _lastTintValid) return;
        _lastTintValid = valid;

        Color color = valid ? _validTint : _invalidTint;
        foreach (var rend in _ghost.GetComponentsInChildren<Renderer>(true))
        {
            var mpb = new MaterialPropertyBlock();
            rend.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", color);
            rend.SetPropertyBlock(mpb);
        }
    }

    private static bool TryGetLowestY(GameObject obj, out float lowY)
    {
        lowY = 0f;
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return false;

        Bounds bounds = renderers[0].bounds;
        foreach (Renderer r in renderers)
            bounds.Encapsulate(r.bounds);

        lowY = bounds.min.y;
        return true;
    }

    // ── Selection ──────────────────────────────────────────────────────────

    private void HandleSelection()
    {
        if (SimulationManager.Instance != null && SimulationManager.Instance.IsSimulationRunning)
            return;

        if (!IsFurnitureModeActive())
        {
            CancelSelection();
            return;
        }

        if (!_tapReleased) return;
        if (GameInput.IsPointerOverInteractiveUI(GameInput.PointerPosition)) return;

        if (TryPickFurniture(GameInput.PointerPosition, out GameObject picked))
        {
            if (picked == _selectedInstance)
            {
                CancelSelection();
                return;
            }
            _selectedInstance = picked;
            ShowInfoCard(picked);
        }
        else
        {
            CancelSelection();
        }
    }

    private bool TryPickFurniture(Vector2 screenPos, out GameObject picked)
    {
        picked = null;
        var fm = FurnitureManager.Instance;
        if (fm == null || _cam == null) return false;

        Ray ray = _cam.ScreenPointToRay(screenPos);
        float bestDist = float.MaxValue;

        foreach (var instance in fm.PlacedInstances)
        {
            if (instance == null) continue;
            Bounds? b = GetCombinedBounds(instance);
            if (b == null) continue;
            if (RayIntersectsBounds(ray, b.Value, out float dist) && dist < bestDist)
            {
                bestDist = dist;
                picked = instance;
            }
        }
        return picked != null;
    }

    private Bounds? GetCombinedBounds(GameObject obj)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return null;

        Bounds bounds = renderers[0].bounds;
        foreach (Renderer r in renderers)
            bounds.Encapsulate(r.bounds);
        return bounds;
    }

    /// <summary>Ray vs AABB (slab method).</summary>
    private static bool RayIntersectsBounds(Ray ray, Bounds b, out float distance)
    {
        distance = 0f;

        Vector3 min = b.min;
        Vector3 max = b.max;
        Vector3 origin = ray.origin;
        Vector3 dir = ray.direction;

        float tMin = 0f;
        float tMax = 1e9f;

        for (int i = 0; i < 3; i++)
        {
            if (Mathf.Abs(dir[i]) < 1e-6f)
            {
                if (origin[i] < min[i] || origin[i] > max[i]) return false;
            }
            else
            {
                float inv = 1f / dir[i];
                float t1 = (min[i] - origin[i]) * inv;
                float t2 = (max[i] - origin[i]) * inv;
                if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
                tMin = Mathf.Max(tMin, t1);
                tMax = Mathf.Min(tMax, t2);
                if (tMin > tMax) return false;
            }
        }

        distance = tMin;
        return true;
    }

    // ── Info Card UI (dibuat runtime, tak butuh prefab) ────────────────────

    private void ShowInfoCard(GameObject instance)
    {
        var fm = FurnitureManager.Instance;
        if (fm == null || !fm.TryGetDataOf(instance, out var data)) return;

        // Hapus kartu lama bila ada
        if (_infoCard != null)
        {
            Destroy(_infoCard);
            _infoCard = null;
        }

        Canvas canvas = FindOverlayCanvas();
        if (canvas == null)
        {
            Debug.LogWarning("[FurniturePlacement] Tidak ada canvas untuk kartu info.");
            return;
        }

        int index = 1;
        foreach (var (d, go) in fm.GetPlacedInstances())
        {
            if (go == instance) break;
            index++;
        }

        GameObject card = new GameObject("FurnitureInfoCard",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(VerticalLayoutGroup));
        card.transform.SetParent(canvas.transform, false);

        RectTransform cardRect = card.GetComponent<RectTransform>();
        cardRect.anchorMin = new Vector2(0.5f, 0.5f);
        cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.sizeDelta = new Vector2(230f, 130f);

        Image bg = card.GetComponent<Image>();
        bg.color = cardBackground;
        bg.raycastTarget = true;

        var layout = card.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 10, 10);
        layout.spacing = 5f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;

        // Title
        AddTmpText(card.transform, "Title", $"#{index} {data.furnitureName}",
            16, TextAlignmentOptions.Left, FontStyles.Bold);

        // Watt
        AddTmpText(card.transform, "Watt", $"{data.wattConsumption} W",
            14, TextAlignmentOptions.Left);

        // Row tombol
        GameObject row = new GameObject("ButtonRow",
            typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(card.transform, false);

        var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 8f;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = false;

        AddButton(row.transform, "Atur Jam", configButtonColor, () =>
        {
            OpenConfigPanel(data);
            CancelSelection();
        });

        AddButton(row.transform, "Hapus", deleteButtonColor, () =>
        {
            fm.RemoveFurniture(instance);
            CancelSelection();
        });

        // Posisi: di atas object, clamp ke layar
        PositionInfoCard(cardRect, GetCombinedBounds(instance), canvas);

        _infoCard = card;
    }

    private void OpenConfigPanel(FurnitureData data)
    {
        var panel = FurnitureConfigPanel.Instance
                    ?? FindFirstObjectByType<FurnitureConfigPanel>(FindObjectsInactive.Include);
        if (panel != null)
            panel.Open(data);
        else
            Debug.LogWarning("[FurniturePlacement] FurnitureConfigPanel tidak ditemukan di scene.");
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

    private void PositionInfoCard(RectTransform cardRect, Bounds? bounds, Canvas canvas)
    {
        if (bounds == null || _cam == null) return;

        Vector2 screenPos = _cam.WorldToScreenPoint(bounds.Value.center);
        screenPos.y += Mathf.Clamp(bounds.Value.size.y, 40f, 120f) + 30f;

        RectTransform canvasRect = canvas.transform as RectTransform;
        if (canvasRect == null) return;

        Camera cam = canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceCamera ? canvas.rootCanvas.worldCamera : null;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPos, cam, out Vector2 local))
            return;

        Rect rect = canvasRect.rect;
        float marginX = cardRect.sizeDelta.x * 0.5f + 10f;
        float marginY = cardRect.sizeDelta.y * 0.5f + 10f;
        local.x = Mathf.Clamp(local.x, rect.xMin + marginX, rect.xMax - marginX);
        local.y = Mathf.Clamp(local.y, rect.yMin + marginY, rect.yMax - marginY);

        cardRect.anchoredPosition = local;
    }

    private void AddButton(Transform parent, string label, Color color, UnityAction onClick)
    {
        GameObject btnGO = new GameObject(label,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        btnGO.transform.SetParent(parent, false);

        Image img = btnGO.GetComponent<Image>();
        img.color = color;

        var layoutEl = btnGO.AddComponent<LayoutElement>();
        layoutEl.preferredHeight = 36f;

        Button btn = btnGO.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        AddTmpText(btnGO.transform, "Text", label, 15, TextAlignmentOptions.Center);
    }

    private static void AddTmpText(Transform parent, string name, string text,
        int fontSize, TextAlignmentOptions alignment, FontStyles fontStyle = FontStyles.Normal)
    {
        GameObject go = new GameObject(name,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null)
            tmp.font = TMP_Settings.defaultFontAsset;
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = fontStyle;
        tmp.alignment = alignment;
        tmp.color = Color.white;
        tmp.raycastTarget = false;

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    // ── Misc Helpers ───────────────────────────────────────────────────────

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        if (layer < 0) layer = 0;
        root.layer = layer;
        foreach (Transform child in root.transform)
            SetLayerRecursively(child.gameObject, layer);
    }
}