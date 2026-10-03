using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using SolarEdu;

/// <summary>
/// Mengatur mode instalasi baterai.
/// Saat mode aktif: kamera pindah ke area dinding lalu OrbitCamera diaktifkan,
/// hover slot kosong → ghost baterai muncul, klik kiri → pasang,
/// klik baterai terpasang → card hapus/batal muncul.
/// </summary>
public class BatteryInstaller : MonoBehaviour
{
    public static BatteryInstaller Instance { get; private set; }

    // ── Inspector ──────────────────────────────────────────────────────────

    [Header("Batas Instalasi")]
    public int maxBatteries = 5;

    [Header("Tombol Toggle")]
    public Button installButton;
    public TextMeshProUGUI buttonText;

    [Header("UI References")]
    [Tooltip("GameObject UI Left yang di-show/hide saat mode furnitur aktif.")]
    [SerializeField] private GameObject uiLeftBatt;

    [Tooltip("Referensi UIPanelSlider pada UI Left untuk mereset posisi panel saat masuk mode.")]
    [SerializeField] private UIPanelSlider uiLeftSlider;

    [Header("Baterai")]
    public GameObject batteryPrefab;

    [Tooltip("Daftar slot posisi baterai di scene.")]
    public Transform[] batterySlots;

    [Tooltip("Ukuran BoxCollider auto-ditambahkan ke tiap slot (unit dunia). " +
             "Sesuaikan agar satu slot menutupi satu sel grid.")]
    public Vector3 slotColliderSize = new Vector3(40f, 80f, 100f);

    [Header("Kamera Baterai")]
    [Tooltip("Transform titik awal transisi kamera ke dinding baterai.")]
    public Transform batteryCameraPoint;

    public float cameraTransitionDuration = 1.2f;

    [Header("Ghost Preview")]
    public Material ghostMaterial;

    [Header("UI Utama")]
    public GameObject catalogPanel;
    public GameObject roomButtonContainer;

    [Header("Battery Info Card")]
    [Tooltip("Prefab card info baterai. Harus memiliki child: InfoText (TMP), DeleteButton (Button), CloseButton (Button).")]
    public GameObject batteryInfoCardPrefab;
    public GameObject batteryInfoCard;
    public TextMeshProUGUI batteryInfoText;
    public Button deleteBatteryButton;
    public Button closeBatteryCardButton;
    public Vector2 cardScreenOffset = new Vector2(170f, 40f);

    [Header("Limit Kamera Mode Baterai")]
    [Tooltip("Pitch minimum saat mode baterai aktif (negatif = boleh lihat ke atas).")]
    public float batteryModeMinPitch = -60f;
    [Tooltip("Pitch maximum saat mode baterai aktif.")]
    public float batteryModeMaxPitch = 30f;

    // ── Private State ──────────────────────────────────────────────────────

    private GameObject[] _slotBatteries;
    private int[]        _batteryNumbers;   // nomor urut pasang per slot

    private bool _isInstallMode   = false;
    private bool _isTransitioning = false;
    private bool _homeStatePreset = false;
    private OrbitCamera _orbitCamera;

    // Simpan pitch limits OrbitCamera asli untuk di-restore saat keluar
    private float _savedMinPitch;
    private float _savedMaxPitch;

    // Saved orbit / free-cam state (untuk restore saat keluar mode)
    private Transform  _savedOrbitTarget;
    private float      _savedOrbitDistance;
    private float      _savedOrbitPitch;
    private float      _savedOrbitYaw;
    private Vector3    _savedOrbitOffset;
    private Vector3    _savedCamPos;
    private Quaternion _savedCamRot;

    // Ghost tunggal yang berpindah ke slot saat hover
    private GameObject _ghost;
    private int        _hoveredSlotIndex    = -1;
    private int        _selectedBatterySlot = -1;

    // ── Public Properties ──────────────────────────────────────────────────

    public bool       IsInstallMode      => _isInstallMode;
    public int        InstalledCount     => CountInstalled();
    public bool       IsFull             => InstalledCount >= maxBatteries;
    public Vector3    SavedCamPos        => _savedCamPos;
    public Quaternion SavedCamRot        => _savedCamRot;
    public Transform  SavedOrbitTarget   => _savedOrbitTarget;
    public float      SavedOrbitDistance => _savedOrbitDistance;
    public float      SavedOrbitPitch    => _savedOrbitPitch;
    public float      SavedOrbitYaw      => _savedOrbitYaw;
    public Vector3    SavedOrbitOffset   => _savedOrbitOffset;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        // Sembunyikan UI Left di awal
        if (uiLeftBatt != null)
            uiLeftBatt.SetActive(false);

        _orbitCamera = Camera.main != null ? Camera.main.GetComponent<OrbitCamera>() : null;

        int slotCount = batterySlots != null ? batterySlots.Length : 0;
        _slotBatteries = new GameObject[slotCount];
        _batteryNumbers = new int[slotCount];

        SetupSlotColliders();
        CreateGhost();
        EnsureBatteryCard();

        if (installButton     != null) installButton.onClick.AddListener(ToggleMode);
        if (deleteBatteryButton  != null) deleteBatteryButton.onClick.AddListener(DeleteSelectedBattery);
        if (closeBatteryCardButton != null) closeBatteryCardButton.onClick.AddListener(HideBatteryCard);
        if (batteryInfoCard   != null) batteryInfoCard.SetActive(false);

        UpdateButtonText();
    }

    private void Update()
    {
        if (!_isInstallMode || _isTransitioning) return;
        if (Camera.main == null) return;

        // Update posisi card agar mengikuti baterai terpilih
        if (_selectedBatterySlot >= 0 && _selectedBatterySlot < _slotBatteries.Length)
        {
            GameObject sel = _slotBatteries[_selectedBatterySlot];
            if (sel != null && batteryInfoCard != null && batteryInfoCard.activeSelf)
                PositionCardNearBattery(sel.transform.position);
        }

        if (IsPointerOverBlockerUI())
        {
            if (_ghost != null) _ghost.SetActive(false);
            _hoveredSlotIndex = -1;
            return;
        }

        HandleHover();

        if (GameInput.Player.Click.WasPressedThisFrame())
            HandleClick();
    }

    // ── Hover & Click ──────────────────────────────────────────────────────

    private void HandleHover()
    {
        Ray ray = Camera.main.ScreenPointToRay(GameInput.PointerPosition);

        if (!Physics.Raycast(ray, out RaycastHit hit, 1000f))
        {
            SetGhostVisible(false);
            _hoveredSlotIndex = -1;
            return;
        }

        int slotIdx = FindSlotIndex(hit.transform.gameObject);

        if (slotIdx >= 0 && _slotBatteries[slotIdx] == null && !IsFull)
        {
            _hoveredSlotIndex = slotIdx;
            if (_ghost != null)
            {
                _ghost.transform.SetPositionAndRotation(
                    batterySlots[slotIdx].position,
                    batterySlots[slotIdx].rotation);
                if (batteryPrefab != null)
                    _ghost.transform.localScale = batteryPrefab.transform.localScale;
                _ghost.SetActive(true);
            }
        }
        else
        {
            SetGhostVisible(false);
            _hoveredSlotIndex = -1;
        }
    }

    private void HandleClick()
    {
        // Pasang di slot kosong yang sedang di-hover
        if (_hoveredSlotIndex >= 0 && _slotBatteries[_hoveredSlotIndex] == null)
        {
            HideBatteryCard();
            InstallAt(_hoveredSlotIndex);
            return;
        }

        // Raycast untuk deteksi klik ke slot terisi atau baterai langsung
        Ray ray = Camera.main.ScreenPointToRay(GameInput.PointerPosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
        {
            // Slot collider menghalangi raycast ke battery mesh — cek dulu apakah
            // slot yang kena sudah berisi baterai, lalu munculkan info card
            int slotIdx = FindSlotIndex(hit.transform.gameObject);
            if (slotIdx >= 0 && _slotBatteries[slotIdx] != null)
            {
                SelectBattery(slotIdx);
                return;
            }

            // Fallback: raycast tembus ke mesh baterai secara langsung
            for (int i = 0; i < _slotBatteries.Length; i++)
            {
                GameObject b = _slotBatteries[i];
                if (b != null &&
                    (hit.transform == b.transform || hit.transform.IsChildOf(b.transform)))
                {
                    SelectBattery(i);
                    return;
                }
            }
        }

        HideBatteryCard();
    }

    // ── Install / Remove ───────────────────────────────────────────────────

    private void InstallAt(int slotIndex)
    {
        if (_slotBatteries == null || slotIndex >= _slotBatteries.Length) return;
        if (IsFull) return;

        Transform slot = batterySlots[slotIndex];
        if (slot == null) return;

        GameObject battery = batteryPrefab != null
            ? Instantiate(batteryPrefab, slot.position, slot.rotation)
            : GameObject.CreatePrimitive(PrimitiveType.Cube);

        battery.name = $"Battery_{slotIndex + 1}";
        if (batteryPrefab != null)
            battery.transform.localScale = batteryPrefab.transform.localScale;

        _slotBatteries[slotIndex] = battery;

        // Nomor urut = berapa baterai yang sudah terpasang sesudah ini
        _batteryNumbers[slotIndex] = CountInstalled();

        BatteryManager.Instance?.NotifyBatteryInstalled();
        UpdateButtonText();
        Debug.Log($"[BatteryInstaller] Baterai dipasang di slot {slotIndex + 1}. Total: {InstalledCount}/{maxBatteries}");

        // ── SolarEdu Tracking ──
        if (SolarEduManager.Instance != null)
        {
            SolarEduManager.Instance.SendStatement(
                SolarEduVerb.Memasang,
                $"solaredu://baterai/slot-{slotIndex + 1}",
                $"Baterai #{InstalledCount}"
            );
        }
    }

    private void DeleteSelectedBattery()
    {
        if (_selectedBatterySlot < 0 || _selectedBatterySlot >= _slotBatteries.Length) return;
        GameObject battery = _slotBatteries[_selectedBatterySlot];
        if (battery == null) return;

        _slotBatteries[_selectedBatterySlot] = null;
        Destroy(battery);
        BatteryManager.Instance?.NotifyBatteryRemoved();
        HideBatteryCard();
        UpdateButtonText();
        Debug.Log($"[BatteryInstaller] Baterai slot {_selectedBatterySlot + 1} dihapus. Sisa: {InstalledCount}/{maxBatteries}");

        // ── SolarEdu Tracking ──
        if (SolarEduManager.Instance != null)
        {
            SolarEduManager.Instance.SendStatement(
                SolarEduVerb.Mengamati,
                $"solaredu://baterai/slot-{_selectedBatterySlot + 1}/hapus",
                "Hapus Baterai"
            );
        }
    }

    private void SelectBattery(int slotIndex)
    {
        _selectedBatterySlot = slotIndex;
        if (batteryInfoCard == null) return;

        if (batteryInfoText != null)
        {
            int num = (_batteryNumbers != null && slotIndex < _batteryNumbers.Length && _batteryNumbers[slotIndex] > 0)
                ? _batteryNumbers[slotIndex]
                : slotIndex + 1;
            batteryInfoText.text = $"Baterai #{num}";
        }

        batteryInfoCard.SetActive(true);
        PositionCardNearBattery(_slotBatteries[slotIndex].transform.position);
    }

    private void HideBatteryCard()
    {
        _selectedBatterySlot = -1;
        if (batteryInfoCard != null)
            batteryInfoCard.SetActive(false);
    }

    private void PositionCardNearBattery(Vector3 worldPos)
    {
        if (batteryInfoCard == null || Camera.main == null) return;
        Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPos);
        if (screenPos.z < 0f) return;
        RectTransform rt = batteryInfoCard.GetComponent<RectTransform>();
        if (rt != null)
            rt.position = screenPos + new Vector3(cardScreenOffset.x, cardScreenOffset.y, 0f);
    }

    // ── Public API ─────────────────────────────────────────────────────────

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>Data untuk satu unit baterai yang terpasang.</summary>
    public struct InstalledBatteryInfo
    {
        public int slotIndex;
        public int batteryNumber;
    }

    /// <summary>
    /// Mengembalikan daftar semua slot yang saat ini terisi baterai,
    /// terurut berdasarkan nomor urut instalasi.
    /// </summary>
    public List<InstalledBatteryInfo> GetInstalledBatteries()
    {
        var result = new List<InstalledBatteryInfo>();
        if (_slotBatteries == null) return result;

        for (int i = 0; i < _slotBatteries.Length; i++)
        {
            if (_slotBatteries[i] != null)
                result.Add(new InstalledBatteryInfo { slotIndex = i, batteryNumber = _batteryNumbers[i] });
        }

        // Urutkan berdasarkan nomor urut instalasi agar tampil konsisten di UI
        result.Sort((a, b) => a.batteryNumber.CompareTo(b.batteryNumber));
        return result;
    }

    public void ReinitCameraAngles() { /* tidak dipakai — OrbitCamera mengatur kamera */ }

    /// <summary>Tampilkan teks tombol seperti di luar install mode.</summary>
    public void ShowNormalButtonText()
    {
        if (buttonText == null) return;
        int c = InstalledCount;
        if (c == 0)      buttonText.text = "Pasang Baterai";
        else if (IsFull) buttonText.text = $"Baterai ({c}/{maxBatteries})";
        else             buttonText.text = $"Tambah Baterai ({c}/{maxBatteries})";
    }

    /// <summary>Tampilkan teks "← Kembali" karena install mode masih aktif.</summary>
    public void ShowBackButtonText()
    {
        if (buttonText != null) buttonText.text = "← Kembali";
    }

    /// <summary>Keluar dari mode baterai secara paksa tanpa transisi kamera.</summary>
    public void ForceExitMode()
    {
        StopAllCoroutines();
        _isInstallMode   = false;
        _isTransitioning = false;
        _homeStatePreset = false;
        SetGhostVisible(false);
        HideBatteryCard();

        if (uiLeftSlider != null)
            uiLeftSlider.SlideOut(); // atau ResetToVisible() sesuai kebutuhan sistem UI Anda
        else if (uiLeftBatt != null)
            uiLeftBatt.SetActive(false);

        // Restore orbit state supaya installer lain mendapat posisi home yang benar
        if (_orbitCamera != null)
        {
            if (_savedOrbitTarget != null)
                _orbitCamera.target = _savedOrbitTarget;

            _orbitCamera.distance     = _savedOrbitDistance;
            _orbitCamera.initialPitch = _savedOrbitPitch;
            _orbitCamera.initialYaw   = _savedOrbitYaw;
            _orbitCamera.targetOffset = _savedOrbitOffset;

            _orbitCamera.minVerticalAngle = _savedMinPitch;
            _orbitCamera.maxVerticalAngle = _savedMaxPitch;

            SetOrbitField("yaw",       _savedOrbitYaw);
            SetOrbitField("pitch",     _savedOrbitPitch);
            SetOrbitField("panOffset", Vector3.zero);

            _orbitCamera.enabled = false;
        }

        if (catalogPanel        != null) catalogPanel.SetActive(true);
        if (roomButtonContainer != null) roomButtonContainer.SetActive(true);

        ShowNormalButtonText();
    }

    // ── Mode Toggle ────────────────────────────────────────────────────────

    public void ToggleMode()
    {
        if (_isTransitioning) return;

        bool roofModeActive      = SolarPanelInstaller.Instance != null && SolarPanelInstaller.Instance.IsRoofMode;
        bool furnitureModeActive = FurnitureInstaller.Instance != null && FurnitureInstaller.Instance.IsFurnitureMode;

        if (_isInstallMode)
        {
            if (roofModeActive)
                StartCoroutine(RejoinBatteryView());
            else
                StartCoroutine(ExitModeTransition());
        }
        else
        {
            // Force-exit furniture mode saat memasuki mode baterai
            if (furnitureModeActive)
                FurnitureInstaller.Instance.ForceExitMode();

            if (roofModeActive)
            {
                _savedCamPos        = SolarPanelInstaller.Instance.SavedCameraPos;
                _savedCamRot        = SolarPanelInstaller.Instance.SavedCameraRot;
                _savedOrbitTarget   = SolarPanelInstaller.Instance.SavedOrbitTarget;
                _savedOrbitDistance = SolarPanelInstaller.Instance.SavedOrbitDistance;
                _savedOrbitPitch    = SolarPanelInstaller.Instance.SavedOrbitPitch;
                _savedOrbitYaw      = SolarPanelInstaller.Instance.SavedOrbitYaw;
                _savedOrbitOffset   = SolarPanelInstaller.Instance.SavedOrbitOffset;
                SolarPanelInstaller.Instance.ForceExitMode();
                _homeStatePreset = true;
            }
            StartCoroutine(EnterModeTransition());
        }
    }

    // ── Camera Transitions ─────────────────────────────────────────────────

    private IEnumerator EnterModeTransition()
    {
        _isTransitioning = true;
        _isInstallMode   = true;

        Vector3    fromPos;
        Quaternion fromRot;

        if (!_homeStatePreset)
        {
            _savedCamPos = Camera.main.transform.position;
            _savedCamRot = Camera.main.transform.rotation;

            if (_orbitCamera != null)
            {
                _savedOrbitTarget   = _orbitCamera.target;
                _savedOrbitDistance = _orbitCamera.distance;
                _savedOrbitPitch    = _orbitCamera.initialPitch;
                _savedOrbitYaw      = _orbitCamera.initialYaw;
                _savedOrbitOffset   = _orbitCamera.targetOffset;
                _orbitCamera.enabled = false;
            }
            fromPos = _savedCamPos;
            fromRot = _savedCamRot;
        }
        else
        {
            _homeStatePreset = false;
            fromPos = Camera.main.transform.position;
            fromRot = Camera.main.transform.rotation;
        }

        if (catalogPanel      != null) catalogPanel.SetActive(false);
        if (roomButtonContainer != null) roomButtonContainer.SetActive(false);
        if (buttonText        != null) buttonText.text = "← Kembali";

        ShowUILeftBatt();

        Vector3    targetPos = batteryCameraPoint != null ? batteryCameraPoint.position : fromPos;
        Quaternion targetRot = batteryCameraPoint != null ? batteryCameraPoint.rotation : fromRot;

        yield return StartCoroutine(SmoothCameraMove(fromPos, fromRot, targetPos, targetRot, cameraTransitionDuration));

        // Inisialisasi sudut kamera dari posisi WallCameraPoint
        InitWallCameraAngles();

        _isTransitioning = false;
        Debug.Log("[BatteryInstaller] Mode instalasi baterai AKTIF");
    }

    private IEnumerator RejoinBatteryView()
    {
        _isTransitioning = true;

        if (_orbitCamera != null) _orbitCamera.enabled = false;
        SolarPanelInstaller.Instance.ForceExitMode();

        if (catalogPanel      != null) catalogPanel.SetActive(false);
        if (roomButtonContainer != null) roomButtonContainer.SetActive(false);

        ShowUILeftBatt();

        Vector3    fromPos   = Camera.main.transform.position;
        Quaternion fromRot   = Camera.main.transform.rotation;
        Vector3    targetPos = batteryCameraPoint != null ? batteryCameraPoint.position : fromPos;
        Quaternion targetRot = batteryCameraPoint != null ? batteryCameraPoint.rotation : fromRot;

        yield return StartCoroutine(SmoothCameraMove(fromPos, fromRot, targetPos, targetRot, cameraTransitionDuration));

        InitWallCameraAngles();
        ShowBackButtonText();
        _isTransitioning = false;
    }

    private IEnumerator ExitModeTransition()
    {
        _isTransitioning = true;

        // TAMBAHKAN KODE INI: Animasikan slide-out katalog baterai
        if (uiLeftSlider != null)
            uiLeftSlider.SlideOut();
        else if (uiLeftBatt != null)
            uiLeftBatt.SetActive(false);

        if (_orbitCamera != null) _orbitCamera.enabled = false;
        SetGhostVisible(false);

        if (_orbitCamera != null) _orbitCamera.enabled = false;
        SetGhostVisible(false);
        HideBatteryCard();

        Vector3    startPos = Camera.main.transform.position;
        Quaternion startRot = Camera.main.transform.rotation;

        yield return StartCoroutine(SmoothCameraMove(startPos, startRot, _savedCamPos, _savedCamRot, cameraTransitionDuration));

        if (_orbitCamera != null)
        {
            _orbitCamera.target       = _savedOrbitTarget;
            _orbitCamera.distance     = _savedOrbitDistance;
            _orbitCamera.initialPitch = _savedOrbitPitch;
            _orbitCamera.initialYaw   = _savedOrbitYaw;
            _orbitCamera.targetOffset = _savedOrbitOffset;

            // Restore pitch limits ke nilai semula
            _orbitCamera.minVerticalAngle = _savedMinPitch;
            _orbitCamera.maxVerticalAngle = _savedMaxPitch;

            SetOrbitField("yaw",       _savedOrbitYaw);
            SetOrbitField("pitch",     _savedOrbitPitch);
            SetOrbitField("panOffset", Vector3.zero);

            _orbitCamera.enabled = true;
        }

        if (catalogPanel      != null) catalogPanel.SetActive(true);
        if (roomButtonContainer != null) roomButtonContainer.SetActive(true);

        _isInstallMode   = false;
        _isTransitioning = false;
        UpdateButtonText();
        Debug.Log("[BatteryInstaller] Mode instalasi baterai SELESAI");
    }

    private IEnumerator SmoothCameraMove(Vector3 fromPos, Quaternion fromRot,
                                          Vector3 toPos,   Quaternion toRot, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            t = t * t * (3f - 2f * t);
            Camera.main.transform.position = Vector3.Lerp(fromPos, toPos, t);
            Camera.main.transform.rotation = Quaternion.Slerp(fromRot, toRot, t);
            yield return null;
        }
        Camera.main.transform.position = toPos;
        Camera.main.transform.rotation = toRot;
    }

    /// <summary>
    /// Aktifkan OrbitCamera dari posisi WallCameraPoint tanpa loncatan.
    /// Override pitch limits agar kamera bisa lihat ke atas saat mode baterai.
    /// </summary>
    private void InitWallCameraAngles()
    {
        if (_orbitCamera == null) return;

        // Simpan dan override pitch limits
        _savedMinPitch = _orbitCamera.minVerticalAngle;
        _savedMaxPitch = _orbitCamera.maxVerticalAngle;
        _orbitCamera.minVerticalAngle = batteryModeMinPitch;
        _orbitCamera.maxVerticalAngle = batteryModeMaxPitch;

        // Gunakan target orbit yang sama seperti sebelum masuk mode baterai
        Transform orbitTarget = _savedOrbitTarget != null ? _savedOrbitTarget : _orbitCamera.target;
        if (orbitTarget == null) return;

        // Ambil sudut dari posisi kamera sekarang (tepat di batteryCameraPoint setelah transisi)
        Vector3 euler = Camera.main.transform.eulerAngles;
        float yaw   = euler.y;
        float pitch = euler.x > 180f ? euler.x - 360f : euler.x;

        // Clamp pitch ke dalam range baru
        pitch = Mathf.Clamp(pitch, batteryModeMinPitch, batteryModeMaxPitch);

        // Gunakan distance yang sama seperti sebelum masuk mode agar skala zoom tetap wajar
        float dist = _savedOrbitDistance > 0f ? _savedOrbitDistance : _orbitCamera.distance;

        // Hitung panOffset supaya ApplyTransform() menempatkan kamera tepat di posisi saat ini
        Quaternion rot       = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 targetOffset = _savedOrbitOffset;
        Vector3 panOffset    = Camera.main.transform.position
                               - (orbitTarget.position + targetOffset)
                               + rot * Vector3.forward * dist;

        _orbitCamera.target       = orbitTarget;
        _orbitCamera.distance     = dist;
        _orbitCamera.targetOffset = targetOffset;

        SetOrbitField("yaw",       yaw);
        SetOrbitField("pitch",     pitch);
        SetOrbitField("panOffset", panOffset);

        _orbitCamera.enabled = true;
    }

    private void CleanupTempTarget() { /* tidak digunakan */ }

    // ── Setup ──────────────────────────────────────────────────────────────

    private void SetupSlotColliders()
    {
        if (batterySlots == null) return;
        for (int i = 0; i < batterySlots.Length; i++)
        {
            if (batterySlots[i] == null) continue;
            if (batterySlots[i].GetComponent<Collider>() == null)
            {
                BoxCollider col = batterySlots[i].gameObject.AddComponent<BoxCollider>();
                col.size = slotColliderSize;
            }
        }
    }

    private void CreateGhost()
    {
        if (batteryPrefab == null) return;

        _ghost = Instantiate(batteryPrefab, Vector3.zero, Quaternion.identity);
        _ghost.name = "GhostBattery";
        _ghost.SetActive(false);

        if (ghostMaterial != null)
        {
            foreach (Renderer rend in _ghost.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = new Material[rend.sharedMaterials.Length];
                for (int m = 0; m < mats.Length; m++) mats[m] = ghostMaterial;
                rend.materials = mats;
            }
        }

        // Nonaktifkan collider agar ghost tidak memblokir raycast
        foreach (Collider col in _ghost.GetComponentsInChildren<Collider>(true))
            col.enabled = false;

        foreach (MonoBehaviour mb in _ghost.GetComponentsInChildren<MonoBehaviour>(true))
            Destroy(mb);
    }

    /// <summary>
    /// Instantiate batteryInfoCardPrefab ke Canvas jika card belum di-assign di Inspector.
    /// Auto-resolve referensi child: InfoText, DeleteButton, CloseButton.
    /// </summary>
    private void EnsureBatteryCard()
    {
        if (batteryInfoCard != null) return;
        if (batteryInfoCardPrefab == null)
        {
            Debug.LogWarning("[BatteryInstaller] batteryInfoCardPrefab tidak di-assign di Inspector.");
            return;
        }

        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        GameObject card  = Instantiate(batteryInfoCardPrefab, canvas.transform);
        batteryInfoCard  = card;

        batteryInfoText        = card.transform.Find("InfoText")?.GetComponent<TextMeshProUGUI>();
        deleteBatteryButton    = card.transform.Find("DeleteButton")?.GetComponent<Button>();
        closeBatteryCardButton = card.transform.Find("CloseButton")?.GetComponent<Button>();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void SetGhostVisible(bool visible)
    {
        if (_ghost != null) _ghost.SetActive(visible);
    }

    private int FindSlotIndex(GameObject go)
    {
        if (batterySlots == null) return -1;
        for (int i = 0; i < batterySlots.Length; i++)
        {
            if (batterySlots[i] != null && batterySlots[i].gameObject == go)
                return i;
        }
        return -1;
    }

    private int CountInstalled()
    {
        if (_slotBatteries == null) return 0;
        int c = 0;
        foreach (GameObject b in _slotBatteries) if (b != null) c++;
        return c;
    }

    private void UpdateButtonText()
    {
        if (buttonText == null) return;
        if (_isInstallMode) { buttonText.text = "← Kembali"; return; }
        ShowNormalButtonText();
    }

    private bool IsPointerOverBlockerUI()
    {
        if (batteryInfoCard != null && batteryInfoCard.activeSelf)
        {
            RectTransform rt = batteryInfoCard.GetComponent<RectTransform>();
            if (rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, GameInput.PointerPosition, null))
                return true;
        }
        return false;
    }


    /// <summary>
    /// Ganti jenis baterai aktif. Mengupdate prefab, spesifikasi,
    /// dan mengganti semua baterai yang sudah terpasang ke tipe baru.
    /// </summary>
    public void SetActiveBattType(BattData newBattType)
    {
        if (newBattType == null) return;

        if (newBattType.prefab3D == null)
        {
            Debug.LogWarning("[BatteryInstaller] Prefab 3D dari BattData kosong!");
            return;
        }

        // 1. Update prefab utama
        batteryPrefab = newBattType.prefab3D;

        // 2. Perbarui tampilan Ghost Preview dengan model yang baru
        if (_ghost != null)
        {
            Destroy(_ghost);
        }
        CreateGhost();

        // 3. Ganti semua baterai yang sudah terpasang di slot
        for (int i = 0; i < _slotBatteries.Length; i++)
        {
            if (_slotBatteries[i] != null)
            {
                // Hapus model baterai lama
                Destroy(_slotBatteries[i]);

                // Pasang model baterai baru di posisi slot yang sama
                Transform slot = batterySlots[i];
                GameObject newBattery = Instantiate(batteryPrefab, slot.position, slot.rotation);

                newBattery.name = $"Battery_{i + 1}";
                newBattery.transform.localScale = batteryPrefab.transform.localScale;

                // Update referensi di array
                _slotBatteries[i] = newBattery;
            }
        }

        Debug.Log($"[BatteryInstaller] Tipe baterai diubah menjadi {newBattType.battName}! Semua baterai terpasang telah di-update.");
    }


    private void SetOrbitField(string fieldName, object value)
    {
        if (_orbitCamera == null) return;
        FieldInfo fi = typeof(OrbitCamera).GetField(fieldName,
            BindingFlags.NonPublic | BindingFlags.Instance);
        fi?.SetValue(_orbitCamera, value);
    }

    private void ShowUILeftBatt()
    {
        if (uiLeftSlider != null)
        {
            uiLeftSlider.SlideIn();
        }
        else if (uiLeftBatt != null)
        {
            uiLeftBatt.SetActive(true);
        }

        // Memaksa child CatalogPanel di dalam UI Left Batt agar ikut aktif
        if (uiLeftBatt != null)
        {
            Transform childCatalog = uiLeftBatt.transform.Find("CatalogPanel");
            if (childCatalog != null)
            {
                childCatalog.gameObject.SetActive(true);
            }
        }
    }
}
