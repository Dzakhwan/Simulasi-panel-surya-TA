using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Mengelola interaksi 3D furniture:
///  1. CLICK-TO-PLACE — ghost transparan mengikuti kursor di lantai,
///     rotasi dengan Q/E, klik untuk menempatkan instance baru.
///  2. SELECTION — klik model 3D yang sudah terpasang memunculkan
///     floating action icons ala The Sims FreePlay:
///     - Ikon melayang di sekitar furnitur (Pasang, Batal/Hapus, Kabel, Jam, Pindah)
///     - Ikon gizmo di kanan bawah furnitur sebagai acuan memindahkan
///     - Pill label nama furnitur di bagian bawah (tanpa label harga)
///     - Mendukung Scene Mode UI Builder agar tata letak & visual dapat diedit di Scene View.
/// </summary>
public class FurniturePlacementController : MonoBehaviour
{
    public static FurniturePlacementController Instance { get; private set; }

    [Header("Placement")]
    [Tooltip("Layer untuk raycast lantai saat menempatkan. Jika 0, otomatis memakai layer 'Floor'.")]
    [SerializeField] private LayerMask floorLayerMask = 0;

    [Tooltip("Layer untuk raycast dinding saat menempatkan furnitur dinding seperti Inverter. Jika 0, otomatis memakai layer 'Wall'.")]
    [SerializeField] private LayerMask wallLayerMask = 0;

    [Tooltip("Jarak placement barang")]
    [SerializeField] private float placementDistance = 900f;

    [Tooltip("Tidak dipakai lagi (fallback bidang lantai dihapus). Dipertahankan agar nilai lama tidak hilang.")]
    [SerializeField] private float floorPlaneY = 0f;

    [Tooltip("Langkah rotasi furnitur per klik tombol / tombol Q/E (derajat).")]
    [SerializeField] private float rotationStep = 90f;

    [Tooltip("Cooldown setelah BeginPlacement agar tap kartu tidak langsung menempatkan (detik).")]
    [SerializeField] private float placementCooldown = 0.2f;

    [Tooltip("Toleransi ruang kosong di sekitar bounds barang dalam Unity units.")]
    [SerializeField] private float placementCollisionMargin = 0.05f;

    [Header("Info & Colors")]
    [SerializeField] private Color configButtonColor = new Color(0.95f, 0.6f, 0.1f, 1f);     // Oranye jam
    [SerializeField] private Color deleteButtonColor = new Color(0.90f, 0.18f, 0.15f, 1f);   // Merah hapus/batal
    [SerializeField] private Color confirmButtonColor = new Color(0.20f, 0.78f, 0.35f, 1f);  // Hijau pasang
    [SerializeField] private Color cableButtonColor = new Color(0.00f, 0.72f, 0.96f, 1f);    // Cyan kabel
    [SerializeField] private Color moveButtonColor = new Color(0.25f, 0.45f, 0.85f, 1f);     // Biru pindah

    [Header("Floating Icon Sizes")]
    [SerializeField] private float floatingIconSize = 64f;
    [SerializeField] private float floatingIconRadius = 105f;

    [Header("Scene Floating UI (Dapat diedit di Scene Mode)")]
    [Tooltip("Root container untuk floating UI di Scene")]
    public GameObject floatingUIRoot;
    [Tooltip("UI floating saat penempatan (Placement)")]
    public GameObject scenePlacementUI;
    [Tooltip("UI floating saat furnitur dipilih (Selection)")]
    public GameObject sceneSelectionUI;

    [Header("Scene Placement UI References")]
    public Button sceneBtnConfirm;
    public Button sceneBtnRotate;
    public Button sceneBtnCancel;
    public GameObject sceneMoveGizmoPlacement;
    public GameObject sceneNameLabelPlacement;

    [Header("Scene Selection UI References")]
    public Button sceneBtnDelete;
    public Button sceneBtnCable;
    public Button sceneBtnRotateSelection;
    public Button sceneBtnMove;
    public Button sceneBtnClock;
    public GameObject sceneMoveGizmoSelection;
    public GameObject sceneNameLabelSelection;

    public bool IsPlacing { get; private set; }

    private FurnitureData _data;
    private FurnitureCardUI _sourceCard;
    private GameObject _ghost;
    private GameObject _movingInstance;
    private Vector3 _moveOriginalPosition;
    private Quaternion _moveOriginalRotation;
    private bool _isMoving;
    private bool _simsPlacementMode;
    private float _ghostRotY;
    private float _beginTime;
    private Camera _cam;

    // Validitas posisi ghost: true = di atas lantai & tidak menembus bangunan
    private bool _ghostValid;
    private Vector3 _currentWallNormal = Vector3.forward;
    private bool _hasWallNormal;
    private bool _lastTintValid = true;

    private readonly Color _validTint = new Color(0.35f, 0.85f, 0.45f, 0.45f);
    private readonly Color _invalidTint = new Color(1f, 0.22f, 0.2f, 0.45f);

    // Seleksi
    private GameObject _selectedInstance;
    private GameObject _infoCard;
    private GameObject _placementUI;
    private FurnitureInstaller[] _installers = System.Array.Empty<FurnitureInstaller>();

    // Floating icons - placement mode
    private GameObject _floatingConfirmBtn;
    private GameObject _floatingCancelBtn;
    private GameObject _floatingRotateBtn;
    private GameObject _floatingNameLabel;
    private GameObject _floatingMoveGizmo;

    // Floating icons - selection mode
    private GameObject _floatingDeleteBtn;
    private GameObject _floatingCableBtn;
    private GameObject _floatingSelectRotateBtn;
    private GameObject _floatingMoveBtn;
    private GameObject _floatingClockBtn;
    private GameObject _floatingSelectGizmo;
    private GameObject _floatingSelectNameLabel;

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
        if (wallLayerMask.value == 0)
            wallLayerMask = LayerMask.GetMask("Wall");

        Canvas canvas = FindOverlayCanvas();
        if (canvas != null)
        {
            TryBindExistingSceneUI(canvas);
        }
    }

    private void Update()
    {
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;

        TrackPointerPeek();

        if (IsPlacing)
        {
            HandlePlacement();
            UpdateFloatingPlacementUI();
            return;
        }

        HandleSelection();
        UpdateFloatingSelectionUI();
    }

    /// <summary>
    /// Mencari dan mengikat elemen UI floating yang sudah dibuat di Scene View.
    /// Memungkinkan user mengatur posisi, warna, dan ukuran di Scene Mode tanpa di-reset saat runtime.
    /// </summary>
    public bool TryBindExistingSceneUI(Canvas canvas)
    {
        Transform root = floatingUIRoot != null ? floatingUIRoot.transform : canvas.transform.Find("FloatingUI_Root");
        if (root == null)
        {
            Transform p = canvas.transform.Find("FloatingPlacementUI");
            Transform s = canvas.transform.Find("FloatingSelectionUI");
            if (p == null && s == null && scenePlacementUI == null && sceneSelectionUI == null)
                return false;
            root = canvas.transform;
        }

        floatingUIRoot = root.gameObject;

        // 1. Placement UI
        if (scenePlacementUI == null)
        {
            Transform p = root.Find("FloatingPlacementUI");
            if (p != null) scenePlacementUI = p.gameObject;
        }

        if (scenePlacementUI != null)
        {
            if (sceneBtnConfirm == null) sceneBtnConfirm = scenePlacementUI.transform.Find("BtnConfirm")?.GetComponent<Button>();
            if (sceneBtnRotate == null) sceneBtnRotate = scenePlacementUI.transform.Find("BtnRotate")?.GetComponent<Button>();
            if (sceneBtnCancel == null) sceneBtnCancel = scenePlacementUI.transform.Find("BtnCancel")?.GetComponent<Button>();
            if (sceneMoveGizmoPlacement == null) sceneMoveGizmoPlacement = scenePlacementUI.transform.Find("MoveGizmo")?.gameObject;
            if (sceneNameLabelPlacement == null) sceneNameLabelPlacement = scenePlacementUI.transform.Find("NameLabel")?.gameObject;

            scenePlacementUI.SetActive(false);
        }

        // 2. Selection UI
        if (sceneSelectionUI == null)
        {
            Transform s = root.Find("FloatingSelectionUI");
            if (s != null) sceneSelectionUI = s.gameObject;
        }

        if (sceneSelectionUI != null)
        {
            if (sceneBtnDelete == null) sceneBtnDelete = sceneSelectionUI.transform.Find("BtnHapus")?.GetComponent<Button>();
            if (sceneBtnCable == null) sceneBtnCable = sceneSelectionUI.transform.Find("BtnKabel")?.GetComponent<Button>();
            if (sceneBtnRotateSelection == null) sceneBtnRotateSelection = sceneSelectionUI.transform.Find("BtnRotate")?.GetComponent<Button>();
            if (sceneBtnMove == null) sceneBtnMove = sceneSelectionUI.transform.Find("BtnPindah")?.GetComponent<Button>();
            if (sceneBtnClock == null) sceneBtnClock = sceneSelectionUI.transform.Find("BtnJam")?.GetComponent<Button>();
            if (sceneMoveGizmoSelection == null) sceneMoveGizmoSelection = sceneSelectionUI.transform.Find("MoveGizmo")?.gameObject;
            if (sceneNameLabelSelection == null) sceneNameLabelSelection = sceneSelectionUI.transform.Find("NameLabel")?.gameObject;

            if (sceneBtnCable != null && sceneBtnCable.GetComponent<UIElectricParticleEffect>() == null)
            {
                sceneBtnCable.gameObject.AddComponent<UIElectricParticleEffect>();
            }

            sceneSelectionUI.SetActive(false);
        }

        Debug.Log("[FurniturePlacement] Berhasil mengikat Floating UI dari Scene Mode.");
        return (scenePlacementUI != null || sceneSelectionUI != null);
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
    public void BeginPlacementFromSims(FurnitureData data)
    {
        _simsPlacementMode = true;
        BeginPlacement(data, null);
    }

    public void BeginPlacement(FurnitureData data, FurnitureCardUI sourceCard = null)
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

        if (!_simsPlacementMode && !IsFurnitureModeActive())
        {
            var installers = FindObjectsByType<FurnitureInstaller>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            FurnitureInstaller installer = null;
            foreach (var candidate in installers)
            {
                if (candidate == null) continue;
                if (installer == null) installer = candidate;
                if (candidate.IsFurnitureMode) { installer = candidate; break; }
            }
            if (installer == null)
            {
                Debug.LogWarning("[FurniturePlacement] FurnitureInstaller tidak ditemukan.");
                return;
            }
            installer.EnterMode();
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

        // Matikan collider & jangan kena raycast apa pun
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

        // Spawn langsung di tengah layar atau pada dinding jika furnitur dinding
        Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        bool isWall = IsWallMounted(data);
        if (isWall)
        {
            _hasWallNormal = false;
            _ghostRotY = 0f;
            if (TryFindFirstWallInView(out Vector3 wallPt, out Vector3 wallNorm))
            {
                _ghostValid = true;
                _hasWallNormal = true;
                _currentWallNormal = wallNorm;
                _ghost.transform.position = wallPt + wallNorm * 0.12f;
                Vector3 fwd = new Vector3(wallNorm.x, 0f, wallNorm.z);
                if (fwd.sqrMagnitude > 0.001f)
                    _ghost.transform.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);
                _ghost.SetActive(true);
                SetGhostTint(true);
            }
            else
            {
                if (TryGetFloorPoint(center, out Vector3 floorPt))
                    _ghost.transform.position = floorPt + Vector3.up * 1.5f;
                else
                    _ghost.transform.position = _cam.transform.position + _cam.transform.forward * 4f;

                _ghost.SetActive(true);
                _ghostValid = false;
                SetGhostTint(false);
            }
        }
        else
        {
            _ghostValid = TryGetFloorPoint(center, out Vector3 floorPoint);
            if (_ghostValid)
            {
                _ghost.transform.position = floorPoint;

                // Pusatkan ke tengah fisik objek (X & Z)
                if (TryGetCenterOffsetXZ(_ghost, out Vector3 centerOffset))
                    _ghost.transform.position += centerOffset;

                // Grounding (Y)
                if (TryGetLowestY(_ghost, out float lowY))
                    _ghost.transform.position += Vector3.up * (floorPoint.y - lowY);

                _ghost.SetActive(true);
                SetGhostTint(true);
            }
        }

        ShowPlacementUI();
    }

    /// <summary>
    /// Cek apakah MODE FURNITUR sedang aktif pada salah satu FurnitureInstaller.
    /// </summary>
    private bool IsFurnitureModeActive()
    {
        if (_installers == null || _installers.Length == 0)
            _installers = FindObjectsByType<FurnitureInstaller>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var inst in _installers)
            if (inst != null && inst.IsFurnitureMode)
                return true;
        return false;
    }

    /// <summary>Mulai memindahkan instance furnitur yang sudah terpasang.</summary>
    public void BeginMove(GameObject instance)
    {
        if (instance == null || FurnitureManager.Instance == null) return;
        if (!FurnitureManager.Instance.TryGetDataOf(instance, out var data)) return;

        CancelSelection();
        CableManager.Instance?.Disconnect(instance);
        _movingInstance = instance;
        _moveOriginalPosition = instance.transform.position;
        _moveOriginalRotation = instance.transform.rotation;
        _ghost = instance;
        _data = data;
        _isMoving = true;
        _simsPlacementMode = true;
        IsPlacing = true;
        _ghostValid = true;
        _ghostRotY = instance.transform.eulerAngles.y;
        bool isWallMove = IsWallMounted(data);
        if (isWallMove)
        {
            _hasWallNormal = true;
            _currentWallNormal = instance.transform.forward;
        }
        else
        {
            _hasWallNormal = false;
        }
        SetLayerRecursively(_ghost, LayerMask.NameToLayer("Ignore Raycast"));
        foreach (var col in _ghost.GetComponentsInChildren<Collider>(true)) col.enabled = false;
        ShowPlacementUI();
    }

    public void CancelPlacement()
    {
        if (!IsPlacing) return;
        IsPlacing = false;

        if (_isMoving && _movingInstance != null)
        {
            _movingInstance.transform.SetPositionAndRotation(_moveOriginalPosition, _moveOriginalRotation);
            SetLayerRecursively(_movingInstance, 0);
            foreach (var col in _movingInstance.GetComponentsInChildren<Collider>(true)) col.enabled = true;
            ClearPlacementTint(_movingInstance);
            if (FurnitureManager.Instance.TryGetDataOf(_movingInstance, out var data))
                CableManager.Instance?.Connect(_movingInstance, data);
            _movingInstance = null;
            _isMoving = false;
            _ghost = null;
        }
        else if (_ghost != null)
        {
            Destroy(_ghost);
            _ghost = null;
        }
        DestroyPlacementUI();
        _data = null;
        _sourceCard = null;
        _simsPlacementMode = false;
    }

    /// <summary>Tutup floating icons seleksi (jika terbuka).</summary>
    public void CancelSelection()
    {
        if (_infoCard != null)
        {
            if (_infoCard == sceneSelectionUI)
            {
                _infoCard.SetActive(false);
            }
            else
            {
                Destroy(_infoCard);
            }
            _infoCard = null;
        }
        _floatingDeleteBtn = null;
        _floatingCableBtn = null;
        _floatingSelectRotateBtn = null;
        _floatingMoveBtn = null;
        _floatingClockBtn = null;
        _floatingSelectGizmo = null;
        _floatingSelectNameLabel = null;
        _selectedInstance = null;
    }

    // ── Placement ──────────────────────────────────────────────────────────

    private void HandlePlacement()
    {
        // Batalkan saat keluar furniture mode (mis. tombol Kembali)
        if (!_simsPlacementMode && !IsFurnitureModeActive())
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
                bool isWall = IsWallMounted(_data);
                if (isWall)
                {
                    _ghostValid = TryGetWallPoint(pointer, out Vector3 wallPoint, out Vector3 wallNormal);
                    if (_ghostValid)
                    {
                        _hasWallNormal = true;
                        _currentWallNormal = wallNormal;
                        _ghost.SetActive(true);
                        _ghost.transform.position = wallPoint + wallNormal * 0.12f;

                        Vector3 forwardNormal = new Vector3(wallNormal.x, 0f, wallNormal.z);
                        if (forwardNormal.sqrMagnitude > 0.001f)
                        {
                            _ghost.transform.rotation = Quaternion.LookRotation(forwardNormal.normalized, Vector3.up)
                                                        * Quaternion.Euler(0f, _ghostRotY, 0f);
                        }

                        if (!HasValidPlacementSpace())
                            _ghostValid = false;
                    }
                    else
                    {
                        // Kena selain layer Wall (misal lantai, furnitur lain, luar) -> INVALID
                        _ghost.SetActive(true);
                        Ray ray = _cam.ScreenPointToRay(pointer);
                        if (Physics.Raycast(ray, out RaycastHit nonWallHit, placementDistance, ~LayerMask.GetMask("Ignore Raycast"), QueryTriggerInteraction.Ignore))
                        {
                            _ghost.transform.position = nonWallHit.point;
                        }
                        else if (TryGetFloorPoint(pointer, out Vector3 floorPt))
                        {
                            _ghost.transform.position = floorPt;
                        }
                        _ghostValid = false;
                    }
                    SetGhostTint(_ghostValid);
                }
                else
                {
                    _ghostValid = TryGetFloorPoint(pointer, out Vector3 floorPoint);

                    if (_ghostValid)
                    {
                        _ghost.SetActive(true);
                        _ghost.transform.position = floorPoint;

                        // Pusatkan ke tengah fisik objek (X & Z)
                        if (TryGetCenterOffsetXZ(_ghost, out Vector3 centerOffset))
                            _ghost.transform.position += centerOffset;

                        // Grounding (Y)
                        if (TryGetLowestY(_ghost, out float lowY))
                            _ghost.transform.position += Vector3.up * (floorPoint.y - lowY);

                        // INVALID: ada bangunan/objek di antara kamera & titik lantai
                        Ray occRay = _cam.ScreenPointToRay(pointer);
                        float distToFloor = Vector3.Distance(_cam.transform.position, floorPoint);
                        if (Physics.Raycast(occRay, out _, distToFloor - 0.1f, ~floorLayerMask, QueryTriggerInteraction.Ignore))
                            _ghostValid = false;

                        if (_ghostValid && !HasValidPlacementSpace())
                            _ghostValid = false;
                    }
                    else
                    {
                        _ghost.SetActive(false);
                    }

                    SetGhostTint(_ghostValid);
                }
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
        _ghostRotY = Mathf.Round((_ghostRotY + degrees) / 90f) * 90f;
        if (_ghost != null)
        {
            if (IsWallMounted(_data) && _hasWallNormal)
            {
                Vector3 fwd = new Vector3(_currentWallNormal.x, 0f, _currentWallNormal.z);
                if (fwd.sqrMagnitude > 0.001f)
                    _ghost.transform.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up) * Quaternion.Euler(0f, _ghostRotY, 0f);
                else
                    _ghost.transform.rotation = Quaternion.Euler(0f, _ghostRotY, 0f);
            }
            else
            {
                _ghost.transform.rotation = Quaternion.Euler(0f, _ghostRotY, 0f);
            }
        }
    }

    /// <summary>Rotasi furnitur yang sedang dipilih sebesar 90 derajat.</summary>
    public void RotateSelectedFurniture(float degrees = 90f)
    {
        if (_selectedInstance == null) return;

        Vector3 cur = _selectedInstance.transform.eulerAngles;
        float newY = Mathf.Round((cur.y + degrees) / 90f) * 90f;
        _selectedInstance.transform.rotation = Quaternion.Euler(cur.x, newY, cur.z);

        // Jika Inverter yang dirotasi, perbarui seluruh rute kabel
        if (_selectedInstance != null && _selectedInstance.name.Contains("Inverter"))
        {
            CableManager.Instance?.RefreshAllCables();
        }
        else if (FurnitureManager.Instance != null && FurnitureManager.Instance.TryGetDataOf(_selectedInstance, out var data))
        {
            if (CableManager.Instance != null && CableManager.Instance.IsConnected(_selectedInstance))
            {
                CableManager.Instance.Disconnect(_selectedInstance);
                CableManager.Instance.Connect(_selectedInstance, data);
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  FLOATING PLACEMENT UI  (Sims FreePlay style)
    // ══════════════════════════════════════════════════════════════════════

    private void ShowPlacementUI()
    {
        DestroyPlacementUI();

        // 1. Cek apakah ada UI yang dibuat di Scene Mode
        if (scenePlacementUI != null)
        {
            scenePlacementUI.SetActive(true);

            if (sceneBtnConfirm != null)
            {
                sceneBtnConfirm.onClick.RemoveAllListeners();
                sceneBtnConfirm.onClick.AddListener(CommitPlacement);
            }
            if (sceneBtnRotate != null)
            {
                sceneBtnRotate.onClick.RemoveAllListeners();
                sceneBtnRotate.onClick.AddListener(() => RotateGhost(rotationStep));
            }
            if (sceneBtnCancel != null)
            {
                sceneBtnCancel.onClick.RemoveAllListeners();
                sceneBtnCancel.onClick.AddListener(CancelPlacement);
            }

            if (sceneNameLabelPlacement != null)
            {
                var tmp = sceneNameLabelPlacement.GetComponentInChildren<TextMeshProUGUI>();
                if (tmp != null) tmp.text = _data != null ? _data.furnitureName : "Furnitur";
            }

            _floatingConfirmBtn = sceneBtnConfirm != null ? sceneBtnConfirm.gameObject : null;
            _floatingRotateBtn = sceneBtnRotate != null ? sceneBtnRotate.gameObject : null;
            _floatingCancelBtn = sceneBtnCancel != null ? sceneBtnCancel.gameObject : null;
            _floatingMoveGizmo = sceneMoveGizmoPlacement;
            _floatingNameLabel = sceneNameLabelPlacement;
            _placementUI = scenePlacementUI;
            return;
        }

        // 2. Fallback: Buat otomatis saat runtime jika belum ada di scene
        Canvas canvas = FindOverlayCanvas();
        if (canvas == null) return;

        GameObject ui = new GameObject("PlacementUI_Floating", typeof(RectTransform));
        ui.transform.SetParent(canvas.transform, false);
        RectTransform uiRt = ui.GetComponent<RectTransform>();
        uiRt.anchorMin = Vector2.zero;
        uiRt.anchorMax = Vector2.one;
        uiRt.offsetMin = Vector2.zero;
        uiRt.offsetMax = Vector2.zero;

        _floatingConfirmBtn = CreateFloatingCircleButton(ui.transform, "BtnConfirm",
            "\u2713", confirmButtonColor, floatingIconSize, CommitPlacement);

        _floatingRotateBtn = CreateFloatingCircleButton(ui.transform, "BtnRotate",
            "\u21BB", new Color(0.12f, 0.72f, 0.50f, 1f), floatingIconSize, () => RotateGhost(rotationStep));

        _floatingCancelBtn = CreateFloatingCircleButton(ui.transform, "BtnCancel",
            "\u2715", deleteButtonColor, floatingIconSize, CancelPlacement);

        _floatingMoveGizmo = CreateMoveGizmo(ui.transform, null);

        _floatingNameLabel = CreateFloatingLabel(ui.transform, "NameLabel",
            _data != null ? _data.furnitureName : "Furnitur", 14f);

        _placementUI = ui;
    }

    private void DestroyPlacementUI()
    {
        if (_placementUI != null)
        {
            if (_placementUI == scenePlacementUI)
            {
                _placementUI.SetActive(false);
            }
            else
            {
                Destroy(_placementUI);
            }
            _placementUI = null;
        }
        _floatingConfirmBtn = null;
        _floatingCancelBtn = null;
        _floatingRotateBtn = null;
        _floatingNameLabel = null;
        _floatingMoveGizmo = null;
    }

    /// <summary>Update posisi floating icons setiap frame mengikuti posisi ghost di world space.</summary>
    private void UpdateFloatingPlacementUI()
    {
        if (_placementUI == null || _ghost == null || _cam == null) return;
        if (!_ghost.activeSelf) return;

        Canvas canvas = _placementUI.GetComponentInParent<Canvas>();
        if (canvas == null) return;
        RectTransform canvasRt = canvas.transform as RectTransform;
        if (canvasRt == null) return;

        Bounds? bounds = GetCombinedBounds(_ghost);
        if (bounds == null) return;

        Vector3 center = bounds.Value.center;
        Vector3 topCenter = center + Vector3.up * bounds.Value.extents.y;

        Vector2 screenCenter = _cam.WorldToScreenPoint(center);
        Vector2 screenTop = _cam.WorldToScreenPoint(topCenter);
        float screenObjHeight = Mathf.Abs(screenTop.y - screenCenter.y);
        float dynamicRadius = Mathf.Max(floatingIconRadius, screenObjHeight + 45f);

        Camera uiCam = canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceCamera ? canvas.rootCanvas.worldCamera : null;

        // ── Confirm: kiri atas (135°) ──
        PositionFloatingIcon(_floatingConfirmBtn, center, dynamicRadius, 135f, canvasRt, uiCam);

        // ── Rotate: atas (90°) ──
        PositionFloatingIcon(_floatingRotateBtn, center, dynamicRadius, 80f, canvasRt, uiCam);

        // ── Cancel: kiri (180°) ──
        PositionFloatingIcon(_floatingCancelBtn, center, dynamicRadius, 180f, canvasRt, uiCam);

        // ── Move gizmo: tepat di kanan bawah furnitur pada permukaan lantai ──
        PositionGizmoAtBottomRight(_floatingMoveGizmo, bounds.Value, canvasRt, uiCam);

        // ── Name label: di bawah furnitur (tanpa harga) ──
        Vector3 bottom = center - Vector3.up * bounds.Value.extents.y;
        Vector2 screenBottom = _cam.WorldToScreenPoint(bottom);
        screenBottom.y -= 40f;
        PositionFloatingIconAtScreen(_floatingNameLabel, screenBottom, canvasRt, uiCam);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  FLOATING SELECTION UI  (Sims FreePlay style)
    // ══════════════════════════════════════════════════════════════════════

    private void ShowInfoCard(GameObject instance)
    {
        var fm = FurnitureManager.Instance;
        if (fm == null || !fm.TryGetDataOf(instance, out var data)) return;

        CancelSelection();
        _selectedInstance = instance;

        int index = 1;
        foreach (var (d, go) in fm.GetPlacedInstances())
        {
            if (go == instance) break;
            index++;
        }
        bool isInverter = data.furnitureName.Contains("Inverter");
        string labelText = isInverter 
            ? $"#{index} {data.furnitureName} • Konverter DC-AC (5000W)" 
            : $"#{index} {data.furnitureName} • {data.wattConsumption}W";

        // 1. Cek apakah ada UI seleksi dari Scene Mode
        if (sceneSelectionUI != null)
        {
            sceneSelectionUI.SetActive(true);

            if (sceneBtnDelete != null)
            {
                sceneBtnDelete.onClick.RemoveAllListeners();
                sceneBtnDelete.onClick.AddListener(() =>
                {
                    fm.RemoveFurniture(instance);
                    CancelSelection();
                });
            }

            bool isConnected = CableManager.Instance != null && CableManager.Instance.IsConnected(instance);
            if (sceneBtnCable != null)
            {
                // Inverter adalah pusat daya, tidak perlu tombol colok kabel ke dirinya sendiri
                sceneBtnCable.gameObject.SetActive(!isInverter);

                if (!isInverter)
                {
                    Color cableCol = isConnected ? new Color(0.85f, 0.45f, 0.1f, 1f) : cableButtonColor;
                    var img = sceneBtnCable.GetComponent<Image>();
                    if (img != null) img.color = cableCol;

                    sceneBtnCable.onClick.RemoveAllListeners();
                    sceneBtnCable.onClick.AddListener(() =>
                    {
                        if (!isConnected && CableManager.Instance != null && CableManager.Instance.InverterCount == 0)
                        {
                            ShowInverterWarningToast();
                            return;
                        }
                        CableManager.Instance?.ToggleConnect(instance, data);
                        CancelSelection();
                    });

                    if (sceneBtnCable.GetComponent<UIElectricParticleEffect>() == null)
                        sceneBtnCable.gameObject.AddComponent<UIElectricParticleEffect>();
                }
            }

            if (sceneBtnMove != null)
            {
                sceneBtnMove.onClick.RemoveAllListeners();
                sceneBtnMove.onClick.AddListener(() =>
                {
                    BeginMove(instance);
                });
            }

            if (sceneBtnRotateSelection != null)
            {
                sceneBtnRotateSelection.onClick.RemoveAllListeners();
                sceneBtnRotateSelection.onClick.AddListener(() =>
                {
                    RotateSelectedFurniture(rotationStep);
                });
            }

            if (sceneBtnClock != null)
            {
                sceneBtnClock.gameObject.SetActive(!isInverter);
                if (!isInverter)
                {
                    sceneBtnClock.onClick.RemoveAllListeners();
                    sceneBtnClock.onClick.AddListener(() =>
                    {
                        OpenConfigPanel(data);
                        CancelSelection();
                    });
                }
            }

            if (sceneMoveGizmoSelection != null)
            {
                var btn = sceneMoveGizmoSelection.GetComponent<Button>();
                if (btn == null) btn = sceneMoveGizmoSelection.AddComponent<Button>();
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() =>
                {
                    BeginMove(instance);
                });
            }

            if (sceneNameLabelSelection != null)
            {
                var tmp = sceneNameLabelSelection.GetComponentInChildren<TextMeshProUGUI>();
                if (tmp != null) tmp.text = labelText;
            }

            _floatingDeleteBtn = sceneBtnDelete != null ? sceneBtnDelete.gameObject : null;
            _floatingCableBtn = sceneBtnCable != null ? sceneBtnCable.gameObject : null;
            _floatingSelectRotateBtn = sceneBtnRotateSelection != null ? sceneBtnRotateSelection.gameObject : null;
            _floatingMoveBtn = sceneBtnMove != null ? sceneBtnMove.gameObject : null;
            _floatingClockBtn = sceneBtnClock != null ? sceneBtnClock.gameObject : null;
            _floatingSelectGizmo = sceneMoveGizmoSelection;
            _floatingSelectNameLabel = sceneNameLabelSelection;
            _infoCard = sceneSelectionUI;
            return;
        }

        // 2. Fallback runtime UI
        Canvas canvas = FindOverlayCanvas();
        if (canvas == null)
        {
            Debug.LogWarning("[FurniturePlacement] Tidak ada canvas untuk kartu info.");
            return;
        }

        GameObject card = new GameObject("FurnitureFloatingIcons", typeof(RectTransform));
        card.transform.SetParent(canvas.transform, false);
        RectTransform cardRt = card.GetComponent<RectTransform>();
        cardRt.anchorMin = Vector2.zero;
        cardRt.anchorMax = Vector2.one;
        cardRt.offsetMin = Vector2.zero;
        cardRt.offsetMax = Vector2.zero;

        _floatingDeleteBtn = CreateFloatingCircleButton(card.transform, "BtnHapus",
            "\u2715", deleteButtonColor, floatingIconSize, () =>
            {
                fm.RemoveFurniture(instance);
                CancelSelection();
            });

        bool isConnectedRt = CableManager.Instance != null && CableManager.Instance.IsConnected(instance);
        if (!isInverter)
        {
            Color cableColRt = isConnectedRt ? new Color(0.85f, 0.45f, 0.1f, 1f) : cableButtonColor;
            _floatingCableBtn = CreateFloatingCircleButton(card.transform, "BtnKabel",
                "\u26A1", cableColRt, floatingIconSize, () =>
                {
                    if (!isConnectedRt && CableManager.Instance != null && CableManager.Instance.InverterCount == 0)
                    {
                        ShowInverterWarningToast();
                        return;
                    }
                    CableManager.Instance?.ToggleConnect(instance, data);
                    CancelSelection();
                });

            if (_floatingCableBtn != null)
            {
                _floatingCableBtn.AddComponent<UIElectricParticleEffect>();
            }
        }

        _floatingSelectRotateBtn = CreateFloatingCircleButton(card.transform, "BtnRotate",
            "\u21BB", new Color(0.12f, 0.72f, 0.50f, 1f), floatingIconSize, () =>
            {
                RotateSelectedFurniture(rotationStep);
            });

        _floatingMoveBtn = CreateFloatingCircleButton(card.transform, "BtnPindah",
            "\u2725", moveButtonColor, floatingIconSize, () =>
            {
                BeginMove(instance);
            });

        if (!isInverter)
        {
            _floatingClockBtn = CreateFloatingCircleButton(card.transform, "BtnJam",
                "\u23F0", configButtonColor, floatingIconSize, () =>
                {
                    OpenConfigPanel(data);
                    CancelSelection();
                });
        }

        _floatingSelectGizmo = CreateMoveGizmo(card.transform, () =>
        {
            BeginMove(instance);
        });

        _floatingSelectNameLabel = CreateFloatingLabel(card.transform, "NameLabel", labelText, 14f);

        _infoCard = card;
    }

    /// <summary>Update posisi floating icons setiap frame mengikuti posisi selected furniture.</summary>
    private void UpdateFloatingSelectionUI()
    {
        if (_infoCard == null || _selectedInstance == null || _cam == null) return;

        Canvas canvas = _infoCard.GetComponentInParent<Canvas>();
        if (canvas == null) return;
        RectTransform canvasRt = canvas.transform as RectTransform;
        if (canvasRt == null) return;

        Bounds? bounds = GetCombinedBounds(_selectedInstance);
        if (bounds == null) return;

        Vector3 center = bounds.Value.center;
        Vector3 topCenter = center + Vector3.up * bounds.Value.extents.y;

        Vector2 screenCenter = _cam.WorldToScreenPoint(center);
        Vector2 screenTop = _cam.WorldToScreenPoint(topCenter);
        float screenObjHeight = Mathf.Abs(screenTop.y - screenCenter.y);
        float dynamicRadius = Mathf.Max(floatingIconRadius, screenObjHeight + 45f);

        Camera uiCam = canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceCamera ? canvas.rootCanvas.worldCamera : null;

        // ── Hapus: kiri (180°) ──
        PositionFloatingIcon(_floatingDeleteBtn, center, dynamicRadius, 180f, canvasRt, uiCam);

        // ── Kabel: kiri atas (135°) ──
        PositionFloatingIcon(_floatingCableBtn, center, dynamicRadius, 135f, canvasRt, uiCam);

        // ── Rotasi 90°: atas (90°) ──
        PositionFloatingIcon(_floatingSelectRotateBtn, center, dynamicRadius, 90f, canvasRt, uiCam);

        // ── Jam: kanan atas (45°) ──
        PositionFloatingIcon(_floatingClockBtn, center, dynamicRadius, 45f, canvasRt, uiCam);

        // ── Pindah: kanan (0°) ──
        PositionFloatingIcon(_floatingMoveBtn, center, dynamicRadius, 0f, canvasRt, uiCam);

        // ── Move gizmo: acuan memindahkan furnitur di kanan bawah pada lantai ──
        PositionGizmoAtBottomRight(_floatingSelectGizmo, bounds.Value, canvasRt, uiCam);

        // ── Name label: di bawah furnitur (tanpa harga) ──
        Vector3 bottom = center - Vector3.up * bounds.Value.extents.y;
        Vector2 screenBottom = _cam.WorldToScreenPoint(bottom);
        screenBottom.y -= 40f;
        PositionFloatingIconAtScreen(_floatingSelectNameLabel, screenBottom, canvasRt, uiCam);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  FLOATING UI HELPERS
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>Buat tombol lingkaran floating dengan icon text (emoji/unicode).</summary>
    public GameObject CreateFloatingCircleButton(Transform parent, string goName, string iconText,
        Color bgColor, float size, UnityAction onClick)
    {
        GameObject btnGO = new GameObject(goName, typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Image), typeof(Button), typeof(Outline));
        btnGO.transform.SetParent(parent, false);

        RectTransform rt = btnGO.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(size, size);
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);

        Image img = btnGO.GetComponent<Image>();
        Sprite circleSprite = null;
        if (SimsInventoryUI.Instance != null && SimsInventoryUI.Instance.circleButtonSprite != null)
            circleSprite = SimsInventoryUI.Instance.circleButtonSprite;
        if (circleSprite == null)
        {
            var allSprites = Resources.FindObjectsOfTypeAll<Sprite>();
            foreach (var s in allSprites)
                if (s.name == "Button Circle") { circleSprite = s; break; }
        }
        img.sprite = circleSprite;
        img.type = Image.Type.Simple;
        img.color = bgColor;

        var outline = btnGO.GetComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.95f);
        outline.effectDistance = new Vector2(3f, -3f);

        // Shadow
        var shadow = btnGO.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.45f);
        shadow.effectDistance = new Vector2(2f, -3f);

        // Icon text
        GameObject textGO = new GameObject("IconText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textGO.transform.SetParent(btnGO.transform, false);
        RectTransform textRt = textGO.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        TextMeshProUGUI tmp = textGO.GetComponent<TextMeshProUGUI>();
        tmp.text = iconText;
        tmp.fontSize = size * 0.44f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;

        TMP_FontAsset font = null;
        if (SimsInventoryUI.Instance != null && SimsInventoryUI.Instance.fontAsset != null)
            font = SimsInventoryUI.Instance.fontAsset;
        if (font == null)
        {
            var allFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            foreach (var f in allFonts)
                if (f.name.Contains("Varela")) { font = f; break; }
        }
        if (font != null) tmp.font = font;

        Button btn = btnGO.GetComponent<Button>();
        btn.targetGraphic = img;
        if (onClick != null) btn.onClick.AddListener(onClick);

        // Hover scale effect
        btnGO.AddComponent<UIButtonHoverScale>();

        return btnGO;
    }

    /// <summary>Buat move gizmo di kanan bawah furnitur (lingkaran putih dengan chevron 4 arah).</summary>
    public GameObject CreateMoveGizmo(Transform parent, UnityAction onClick)
    {
        float gizmoSize = floatingIconSize * 0.88f;

        GameObject gizmo = new GameObject("MoveGizmo", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Image));
        gizmo.transform.SetParent(parent, false);

        RectTransform rt = gizmo.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(gizmoSize, gizmoSize);
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);

        Image img = gizmo.GetComponent<Image>();
        Sprite circleSprite = null;
        if (SimsInventoryUI.Instance != null && SimsInventoryUI.Instance.circleButtonSprite != null)
            circleSprite = SimsInventoryUI.Instance.circleButtonSprite;
        if (circleSprite == null)
        {
            var allSprites = Resources.FindObjectsOfTypeAll<Sprite>();
            foreach (var s in allSprites)
                if (s.name == "Button Circle") { circleSprite = s; break; }
        }
        img.sprite = circleSprite;
        img.type = Image.Type.Simple;
        img.color = new Color(1f, 1f, 1f, 0.85f);

        var outline = gizmo.AddComponent<Outline>();
        outline.effectColor = new Color(0.2f, 0.2f, 0.2f, 0.4f);
        outline.effectDistance = new Vector2(2f, -2f);

        var shadow = gizmo.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.35f);
        shadow.effectDistance = new Vector2(2f, -3f);

        // Center cross icon ✥
        GameObject cross = new GameObject("CrossIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        cross.transform.SetParent(gizmo.transform, false);
        RectTransform cRt = cross.GetComponent<RectTransform>();
        cRt.anchorMin = Vector2.zero;
        cRt.anchorMax = Vector2.one;
        cRt.offsetMin = Vector2.zero;
        cRt.offsetMax = Vector2.zero;

        TextMeshProUGUI cTmp = cross.GetComponent<TextMeshProUGUI>();
        cTmp.text = "\u2725";
        cTmp.fontSize = gizmoSize * 0.48f;
        cTmp.alignment = TextAlignmentOptions.Center;
        cTmp.color = new Color(0.25f, 0.25f, 0.25f, 0.9f);
        cTmp.raycastTarget = false;

        // Chevron arrows (▲ ▼ ◀ ▶) around the circle
        float chevronOffset = gizmoSize * 0.52f;
        float chevronSize = gizmoSize * 0.30f;

        CreateChevron(gizmo.transform, "\u25B2", new Vector2(0f, chevronOffset), chevronSize);   // Up
        CreateChevron(gizmo.transform, "\u25BC", new Vector2(0f, -chevronOffset), chevronSize);  // Down
        CreateChevron(gizmo.transform, "\u25C0", new Vector2(-chevronOffset, 0f), chevronSize);  // Left
        CreateChevron(gizmo.transform, "\u25B6", new Vector2(chevronOffset, 0f), chevronSize);   // Right

        if (onClick != null)
        {
            var btn = gizmo.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);
            gizmo.AddComponent<UIButtonHoverScale>();
            img.raycastTarget = true;
        }
        else
        {
            img.raycastTarget = false;
        }

        return gizmo;
    }

    private void CreateChevron(Transform parent, string symbol, Vector2 offset, float size)
    {
        GameObject chevron = new GameObject("Chevron", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        chevron.transform.SetParent(parent, false);
        RectTransform rt = chevron.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);
        rt.anchoredPosition = offset;

        TextMeshProUGUI tmp = chevron.GetComponent<TextMeshProUGUI>();
        tmp.text = symbol;
        tmp.fontSize = size * 0.72f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(0.35f, 0.35f, 0.35f, 0.9f);
        tmp.raycastTarget = false;
    }

    /// <summary>Buat label nama floating (pill gelap tanpa harga).</summary>
    public GameObject CreateFloatingLabel(Transform parent, string goName, string text, float fontSize)
    {
        GameObject labelGO = new GameObject(goName, typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Image));
        labelGO.transform.SetParent(parent, false);

        RectTransform rt = labelGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);

        Image bg = labelGO.GetComponent<Image>();
        Sprite roundedSprite = null;
        if (SimsInventoryUI.Instance != null && SimsInventoryUI.Instance.roundedBoxSprite != null)
            roundedSprite = SimsInventoryUI.Instance.roundedBoxSprite;
        if (roundedSprite == null)
        {
            var allSprites = Resources.FindObjectsOfTypeAll<Sprite>();
            foreach (var s in allSprites)
                if (s.name == "UI") { roundedSprite = s; break; }
        }
        bg.sprite = roundedSprite;
        bg.type = Image.Type.Sliced;
        bg.color = new Color(0.1f, 0.12f, 0.18f, 0.88f);
        bg.raycastTarget = false;

        // Outline tipis
        var outline = labelGO.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.5f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        // Text
        GameObject textGO = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textGO.transform.SetParent(labelGO.transform, false);
        RectTransform textRt = textGO.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(14f, 4f);
        textRt.offsetMax = new Vector2(-14f, -4f);

        TextMeshProUGUI tmp = textGO.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        tmp.enableWordWrapping = false;

        TMP_FontAsset font = null;
        if (SimsInventoryUI.Instance != null && SimsInventoryUI.Instance.fontAsset != null)
            font = SimsInventoryUI.Instance.fontAsset;
        if (font == null)
        {
            var allFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            foreach (var f in allFonts)
                if (f.name.Contains("Varela")) { font = f; break; }
        }
        if (font != null) tmp.font = font;

        // Auto-size berdasarkan text
        float estimatedWidth = text.Length * fontSize * 0.65f + 32f;
        rt.sizeDelta = new Vector2(Mathf.Max(120f, estimatedWidth), fontSize + 18f);

        return labelGO;
    }

    /// <summary>Posisikan floating icon berdasarkan posisi world + sudut offset melingkar.</summary>
    private void PositionFloatingIcon(GameObject icon, Vector3 worldCenter, float radius,
        float angleDeg, RectTransform canvasRt, Camera uiCam)
    {
        if (icon == null || _cam == null) return;

        Vector2 screenCenter = _cam.WorldToScreenPoint(worldCenter);
        float rad = angleDeg * Mathf.Deg2Rad;
        Vector2 screenPos = screenCenter + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * radius;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, screenPos, uiCam, out Vector2 local))
        {
            RectTransform rt = icon.GetComponent<RectTransform>();
            if (rt != null)
            {
                Rect canvasRect = canvasRt.rect;
                float margin = floatingIconSize * 0.5f + 5f;
                local.x = Mathf.Clamp(local.x, canvasRect.xMin + margin, canvasRect.xMax - margin);
                local.y = Mathf.Clamp(local.y, canvasRect.yMin + margin, canvasRect.yMax - margin);
                rt.anchoredPosition = local;
            }
        }
    }

    /// <summary>
    /// Posisikan gizmo acuan memindahkan tepat di sudut kanan bawah furnitur pada permukaan lantai.
    /// </summary>
    private void PositionGizmoAtBottomRight(GameObject gizmo, Bounds bounds, RectTransform canvasRt, Camera uiCam)
    {
        if (gizmo == null || _cam == null) return;

        Vector3 camRight = Vector3.ProjectOnPlane(_cam.transform.right, Vector3.up).normalized;
        Vector3 camFwd = Vector3.ProjectOnPlane(_cam.transform.forward, Vector3.up).normalized;

        Vector3 brWorld = bounds.center
            + (camRight * (bounds.extents.x + 0.35f))
            - (camFwd * (bounds.extents.z + 0.35f));
        brWorld.y = bounds.min.y;

        Vector2 screenBR = _cam.WorldToScreenPoint(brWorld);
        PositionFloatingIconAtScreen(gizmo, screenBR, canvasRt, uiCam);
    }

    /// <summary>Posisikan floating icon di screen position.</summary>
    private void PositionFloatingIconAtScreen(GameObject icon, Vector2 screenPos,
        RectTransform canvasRt, Camera uiCam)
    {
        if (icon == null) return;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, screenPos, uiCam, out Vector2 local))
        {
            RectTransform rt = icon.GetComponent<RectTransform>();
            if (rt != null)
            {
                Rect canvasRect = canvasRt.rect;
                float margin = 10f;
                local.x = Mathf.Clamp(local.x, canvasRect.xMin + margin, canvasRect.xMax - margin);
                local.y = Mathf.Clamp(local.y, canvasRect.yMin + margin, canvasRect.yMax - margin);
                rt.anchoredPosition = local;
            }
        }
    }

    private void CommitPlacement()
    {
        if (_ghost == null || !_ghost.activeSelf || !_ghostValid)
        {
            if (IsWallMounted(_data) && !_ghostValid)
            {
                ShowWallOnlyWarningToast();
            }
            return;
        }
        if (FurnitureManager.Instance == null) return;

        Vector3 pos = _ghost.transform.position;
        Quaternion rot = _ghost.transform.rotation;
        FurnitureData data = _data;
        FurnitureCardUI card = _sourceCard;

        if (_isMoving && _movingInstance != null)
        {
            GameObject moved = _movingInstance;
            moved.transform.SetPositionAndRotation(pos, rot);
            SetLayerRecursively(moved, 0);
            foreach (var col in moved.GetComponentsInChildren<Collider>(true)) col.enabled = true;
            ClearPlacementTint(moved);
            _movingInstance = null;
            _isMoving = false;
            _ghost = null;
            IsPlacing = false;
            DestroyPlacementUI();
            _data = null;
            if (data != null && data.furnitureName.Contains("Inverter"))
            {
                CableManager.Instance?.RefreshAllCables();
            }
            else
            {
                CableManager.Instance?.Connect(moved, data);
            }
            CancelSelection();
            return;
        }

        CancelPlacement();
        FurnitureManager.Instance.AddFurniture(data, pos, rot);
        if (card != null)
        {
            card.OnPlacementComplete();
        }
        else if (SimsInventoryUI.Instance != null)
        {
            SimsInventoryUI.Instance.ReopenStore();
        }
        else
        {
            var cfg = FurnitureConfigPanel.Instance ?? FindFirstObjectByType<FurnitureConfigPanel>(FindObjectsInactive.Include);
            if (cfg != null) cfg.Open(data);
        }
    }

    private bool HasValidPlacementSpace()
    {
        if (_ghost == null) return false;

        Renderer[] renderers = _ghost.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return false;

        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers)
            bounds.Encapsulate(renderer.bounds);

        int floorLayer = Mathf.RoundToInt(Mathf.Log(Mathf.Max(1, floorLayerMask.value), 2f));
        int wallLayer = LayerMask.NameToLayer("Wall");
        bool isWall = IsWallMounted(_data);
        Vector3 extents = bounds.extents - Vector3.one * placementCollisionMargin;
        extents.x = Mathf.Max(0.01f, extents.x);
        extents.y = Mathf.Max(0.01f, extents.y);
        extents.z = Mathf.Max(0.01f, extents.z);
        Collider[] hits = Physics.OverlapBox(bounds.center, extents, _ghost.transform.rotation,
            ~0, QueryTriggerInteraction.Ignore);

        foreach (Collider hit in hits)
        {
            if (hit == null || hit.transform.IsChildOf(_ghost.transform)) continue;
            if (hit.gameObject.layer == floorLayer) continue;
            if (isWall && hit.gameObject.layer == wallLayer) continue;

            Bounds other = hit.bounds;
            bool supportBelow = other.max.y <= bounds.min.y + 0.12f;
            if (supportBelow) continue;

            Debug.Log($"[FurniturePlacement] Posisi invalid: collider '{hit.name}' layer={hit.gameObject.layer} bounds={other}");
            return false;
        }

        return true;
    }

    /// <summary>Cek apakah furnitur adalah tipe pemasangan di dinding (seperti Inverter).</summary>
    private bool IsWallMounted(FurnitureData data)
    {
        if (data == null) return false;
        return data.furnitureName.IndexOf("Inverter", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Raycast khusus untuk dinding (Layer 'Wall').
    /// Mengembalikan true HANYA jika mengenai collider yang ber-layer 'Wall' dan memiliki orientasi bidang vertikal.
    /// </summary>
    private bool TryGetWallPoint(Vector2 screenPos, out Vector3 point, out Vector3 normal)
    {
        point = Vector3.zero;
        normal = Vector3.forward;
        if (_cam == null) return false;

        Ray ray = _cam.ScreenPointToRay(screenPos);
        int ignoreRaycast = LayerMask.NameToLayer("Ignore Raycast");
        int mask = ~(1 << ignoreRaycast);

        if (Physics.Raycast(ray, out RaycastHit hit, placementDistance, mask, QueryTriggerInteraction.Ignore))
        {
            point = hit.point;
            normal = hit.normal;

            int hitLayer = hit.collider.gameObject.layer;
            int wallLayer = LayerMask.NameToLayer("Wall");
            bool isWall = (wallLayerMask.value != 0 && (wallLayerMask.value & (1 << hitLayer)) != 0)
                          || (wallLayer >= 0 && hitLayer == wallLayer);

            if (isWall)
            {
                // Pastikan yang terkena adalah permukaan vertikal dinding (bukan bagian horizontal atas/bawah)
                if (Mathf.Abs(normal.y) < 0.7f)
                {
                    return true;
                }
            }
            return false;
        }

        return false;
    }

    /// <summary>Mencari titik dinding terdekat di layar saat pertama kali spawn.</summary>
    private bool TryFindFirstWallInView(out Vector3 point, out Vector3 normal)
    {
        point = Vector3.zero;
        normal = Vector3.forward;
        if (_cam == null) return false;

        float w = Screen.width;
        float h = Screen.height;
        Vector2[] testPoints = new Vector2[]
        {
            new Vector2(w * 0.5f, h * 0.5f),
            new Vector2(w * 0.5f, h * 0.6f),
            new Vector2(w * 0.4f, h * 0.5f),
            new Vector2(w * 0.6f, h * 0.5f),
            new Vector2(w * 0.3f, h * 0.5f),
            new Vector2(w * 0.7f, h * 0.5f),
            new Vector2(w * 0.5f, h * 0.4f)
        };

        foreach (var p in testPoints)
        {
            if (TryGetWallPoint(p, out point, out normal))
                return true;
        }

        return false;
    }

    /// <summary>Menampilkan toast peringatan saat user mencoba menaruh inverter di luar dinding.</summary>
    private void ShowWallOnlyWarningToast()
    {
        Canvas canvas = FindOverlayCanvas();
        if (canvas == null) return;

        Transform oldToast = canvas.transform.Find("WallOnlyWarningToast");
        if (oldToast != null) Destroy(oldToast.gameObject);

        GameObject toastGO = CreateFloatingLabel(canvas.transform, "WallOnlyWarningToast",
            "⚠️ Inverter hanya bisa dipasang pada dinding (layer Wall)!", 15f);

        RectTransform rt = toastGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.82f);
        rt.anchorMax = new Vector2(0.5f, 0.82f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;

        Image img = toastGO.GetComponent<Image>();
        if (img != null) img.color = new Color(0.85f, 0.22f, 0.15f, 0.95f);

        Destroy(toastGO, 3.5f);
    }

    private bool TryGetFloorPoint(Vector2 screenPos, out Vector3 point)
    {
        point = Vector3.zero;
        if (_cam == null) return false;

        Ray ray = _cam.ScreenPointToRay(screenPos);

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

    private static void ClearPlacementTint(GameObject root)
    {
        if (root == null) return;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            renderer.SetPropertyBlock(null);
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

    private static bool TryGetCenterOffsetXZ(GameObject obj, out Vector3 offset)
    {
        offset = Vector3.zero;
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return false;

        Bounds bounds = renderers[0].bounds;
        foreach (Renderer r in renderers)
            bounds.Encapsulate(r.bounds);

        Vector3 center = bounds.center;
        offset = obj.transform.position - new Vector3(center.x, obj.transform.position.y, center.z);
        return true;
    }

    // ── Selection ──────────────────────────────────────────────────────────

    private void HandleSelection()
    {
        if (SimulationManager.Instance != null && SimulationManager.Instance.IsSimulationRunning)
            return;

        if (CableConnectController.IsConnectModeActive)
        {
            CancelSelection();
            return;
        }

        if (!IsFurnitureModeActive() && SimsInventoryUI.Instance == null)
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

    /// <summary>Pilih instance furnitur di posisi layar (dipakai CableConnectController).</summary>
    public bool TryPickInstance(Vector2 screenPos, out GameObject picked)
    {
        return TryPickFurniture(screenPos, out picked);
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

    // ── Info Card UI helpers ────────────────────────────────────────────

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

    /// <summary>Menampilkan toast peringatan saat user mencoba menyambung kabel tanpa inverter.</summary>
    private void ShowInverterWarningToast()
    {
        Canvas canvas = FindOverlayCanvas();
        if (canvas == null) return;

        Transform oldToast = canvas.transform.Find("InverterWarningToast");
        if (oldToast != null) Destroy(oldToast.gameObject);

        GameObject toastGO = CreateFloatingLabel(canvas.transform, "InverterWarningToast",
            "⚠️ Pasang Inverter terlebih dahulu sebagai sumber listrik!", 15f);

        RectTransform rt = toastGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.82f);
        rt.anchorMax = new Vector2(0.5f, 0.82f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;

        Image img = toastGO.GetComponent<Image>();
        if (img != null) img.color = new Color(0.85f, 0.22f, 0.15f, 0.95f);

        Destroy(toastGO, 3.5f);
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
