using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using SolarEdu;

/// <summary>
/// Mengatur mode instalasi panel surya.
/// Saat mode aktif: kamera pindah ke atas atap (top-down),
/// klik pada atap untuk menempatkan panel surya.
/// </summary>
public class SolarPanelInstaller : MonoBehaviour
{
    public static SolarPanelInstaller Instance { get; private set; }

    [Header("Mode Toggle")]
    public Button toggleButton;                 // Tombol "Instal Panel Surya" / "Kembali"
    public TextMeshProUGUI toggleButtonText;

    [Header("Panel Surya")]
    public GameObject solarPanelPrefab;         // Prefab panel surya (buat dari cube)
    public float panelWattOutput = 400f;        // Watt per panel
    public int maxPanels = 12;                  // Maksimal panel yang bisa dipasang

    [Tooltip("Gap antar panel di sumbu X (unit). Grid X = 200 + gapX")]
    public float panelGapX = 10f;

    [Tooltip("Gap antar panel di sumbu Z (unit). Grid Z = 130 + gapZ")]
    public float panelGapZ = 10f;

    [Header("Konfigurasi SolarPanel")]
    public SunController sunController;         // Referensi SunController untuk kalkulasi daya
    public float panelArea = 1.6f;              // Luas panel (m²)
    public float panelEfficiency = 0.20f;       // Efisiensi panel (0–1)
    public float peakIrradiance = 1000f;        // Irradiansi puncak (W/m²)

    [Header("Panel List UI")]
    public GameObject panelCardPrefab;          // Prefab card untuk setiap panel di list
    public Transform panelListContainer;        // Content di dalam ScrollView

    [Header("Kamera Atap")]
    public Transform roofCameraPosition;        // Empty object di atas atap
    public float roofCameraHeight = 15f;        // Tinggi kamera di atas rumah (jika tidak pakai roofCameraPosition)
    public float cameraTransitionDuration = 1.5f; // Durasi transisi kamera (detik)

    [Header("UI References")]
    public GameObject catalogPanel;             // Panel perabotan (disembunyikan saat mode atap)
    public GameObject roomButtonContainer;      // Tombol ruangan (disembunyikan saat mode atap)
    public GameObject roofUI;                   // Panel UI khusus mode atap (info panel surya)

    [Tooltip("Panel UI Left SP yang berisi catalog jenis panel surya (ditampilkan saat mode atap)")]
    public UIPanelSlider solarPanelCatalogSlider;   // UIPanelSlider pada UI Left SP
    public TextMeshProUGUI panelCountText;      // "Panel: 3/12"
    public TextMeshProUGUI totalWattText;       // "Daya: 1200 W"

    [Header("UI Right – Ringkasan")]
    public TextMeshProUGUI totalPowerText;      // Total Power di UI kanan (real-time, W)
    public TextMeshProUGUI totalEnergyText;     // Total Energy di UI kanan (kumulatif, kWh)
    public TextMeshProUGUI bebanElektronikText; // Beban Elektronik (jumlah watt semua elektronik, konstan)
    public TextMeshProUGUI statusArusBateraiText; // Status Arus Baterai (tenaga surya - beban elektronik)

    [Header("Panel Info Card")]
    [Tooltip("Prefab card info panel surya. Harus memiliki child: InfoText (TMP), DeleteButton (Button), CloseButton (Button).")]
    public GameObject panelInfoCardPrefab;
    public GameObject panelInfoCard;            // Card yang muncul saat klik panel terpasang
    public TextMeshProUGUI panelInfoText;       // Info panel di card (nomor + watt)
    public Button deletePanelButton;            // Tombol hapus di card
    public Button closePanelCardButton;         // Tombol tutup card
    public Vector2 cardScreenOffset = new Vector2(160f, 0f); // Offset dari posisi panel di layar

    [Header("Tombol yang Memblokir Placement")]
    [Tooltip("Daftarkan tombol-tombol UI yang kliknya tidak boleh menembus ke placement (misal: tombol simulasi, back, dll)")]
    public List<RectTransform> placementBlockerButtons = new(); // Tombol yang kliknya memblokir placement

    [Header("Visual")]
    public Material panelGhostMaterial;         // Material transparan untuk preview
    public LayerMask roofLayer;                 // Layer atap untuk raycast

    // ── State ──
    private bool isRoofMode = false;
    private bool isTransitioning = false;       // Sedang transisi kamera
    private bool homeStatePreset = false;       // true saat home state di-set dari installer lain
    private List<GameObject> placedPanels = new();
    private Dictionary<GameObject, GameObject> panelToCard = new(); // panel GO → card GO
    private int totalPanelCounter = 0;          // Counter nomor panel (tidak reset saat hapus)

    // ── Events & Data ─────────────────────────────────────────────────────

    /// <summary>Fired when the number of installed panels changes. Passes the new count.</summary>
    [HideInInspector] public UnityEvent<int> OnPanelCountChanged = new();

    /// <summary>Info for a single installed solar panel.</summary>
    public struct InstalledPanelInfo
    {
        public int panelIndex;
        public SolarPanel solarPanel;
    }

    private readonly List<InstalledPanelInfo> _installedPanelInfos = new();
    private GameObject ghostPanel;              // Preview panel saat hover
    private GameObject selectedPanel;          // Panel yang sedang dipilih untuk dihapus
    private OrbitCamera orbitCamera;
    private Vector3 savedCameraPos;
    private Quaternion savedCameraRot;

    // Saved orbit camera settings
    private Transform savedOrbitTarget;
    private float savedOrbitDistance;
    private float savedOrbitPitch;
    private float savedOrbitYaw;
    private Vector3 savedOrbitOffset;

    // ── Active Panel Type ──
    private SolarPanelData activePanelData;

    /// <summary>Nama jenis panel yang sedang aktif.</summary>
    public string ActivePanelTypeName => activePanelData != null ? activePanelData.panelName : "Default";

    // ── Public State Properties ──
    public bool      IsRoofMode         => isRoofMode;
    public Vector3   SavedCameraPos     => savedCameraPos;
    public Quaternion SavedCameraRot    => savedCameraRot;
    public Transform SavedOrbitTarget   => savedOrbitTarget;
    public float     SavedOrbitDistance => savedOrbitDistance;
    public float     SavedOrbitPitch    => savedOrbitPitch;
    public float     SavedOrbitYaw      => savedOrbitYaw;
    public Vector3   SavedOrbitOffset   => savedOrbitOffset;

    // ── Roof Objects ──
    private List<GameObject> roofObjects = new();

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        orbitCamera = Camera.main.GetComponent<OrbitCamera>();

        // Auto-resolve UI Right text references jika belum di-assign di Inspector
        if (totalPowerText == null)
            totalPowerText = FindTMPByPath("Canvas/UI Right/BG Right 2/Total Power");
        if (totalEnergyText == null)
            totalEnergyText = FindTMPByPath("Canvas/UI Right/BG Right 2/Total Energy");
        if (bebanElektronikText == null)
            bebanElektronikText = FindTMPByPath("Canvas/UI Right/BG Right 2/Beban TExt");
        if (statusArusBateraiText == null)
            statusArusBateraiText = FindTMPByPath("Canvas/UI Right/BG Right 2/pengeluaran textt (1)");

        if (toggleButton != null)
            toggleButton.onClick.AddListener(ToggleMode);

        // Instantiate card dari prefab jika belum di-assign di Inspector
        EnsurePanelCard();

        // Setup panel info card
        if (deletePanelButton != null)
            deletePanelButton.onClick.AddListener(DeleteSelectedPanel);
        if (closePanelCardButton != null)
            closePanelCardButton.onClick.AddListener(HidePanelCard);
        if (panelInfoCard != null)
            panelInfoCard.SetActive(false);

        // Sembunyikan roof UI di awal
        if (roofUI != null)
            roofUI.SetActive(false);

        // Kumpulkan semua objek atap (layer Roof)
        CollectRoofObjects();

        // Sembunyikan atap saat di dalam rumah
        SetRoofVisible(false);

        // Buat ghost panel (preview transparan)
        CreateGhostPanel();

        UpdateUI();
    }

    /// <summary>
    /// Instantiate panelInfoCardPrefab ke Canvas jika card belum di-assign di Inspector.
    /// Auto-resolve referensi child: InfoText, DeleteButton, CloseButton.
    /// </summary>
    private void EnsurePanelCard()
    {
        if (panelInfoCard != null) return;
        if (panelInfoCardPrefab == null)
        {
            Debug.LogWarning("[SolarPanelInstaller] panelInfoCardPrefab tidak di-assign di Inspector.");
            return;
        }

        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        GameObject card = Instantiate(panelInfoCardPrefab, canvas.transform);
        panelInfoCard   = card;

        panelInfoText      = card.transform.Find("InfoText")?.GetComponent<TextMeshProUGUI>();
        deletePanelButton  = card.transform.Find("DeleteButton")?.GetComponent<Button>();
        closePanelCardButton = card.transform.Find("CloseButton")?.GetComponent<Button>();
    }

    /// <summary>
    /// Kumpulkan semua objek dengan layer "Roof" di scene
    /// </summary>
    void CollectRoofObjects()
    {
        roofObjects.Clear();
        int roofLayerIndex = LayerMask.NameToLayer("Roof");

        // Cari semua GameObject di scene
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        foreach (var obj in allObjects)
        {
            // Hanya ambil objek yang layer-nya Roof dan bukan SolarPanelInstaller
            if (obj.layer == roofLayerIndex && obj != this.gameObject && obj != roofCameraPosition?.gameObject)
            {
                // Hanya root roof objects (bukan child)
                if (obj.GetComponent<Renderer>() != null)
                {
                    roofObjects.Add(obj);
                }
            }
        }

        Debug.Log($"[SolarPanel] Ditemukan {roofObjects.Count} objek atap");
    }

    /// <summary>
    /// Tampilkan atau sembunyikan semua objek atap
    /// </summary>
    void SetRoofVisible(bool visible)
    {
        foreach (var roof in roofObjects)
        {
            if (roof != null)
            {
                var renderer = roof.GetComponent<Renderer>();
                if (renderer != null)
                    renderer.enabled = visible;

                var col = roof.GetComponent<Collider>();
                if (col != null)
                    col.enabled = visible;
            }
        }

        // Sembunyikan/tampilkan panel surya yang sudah dipasang
        SetPanelsVisible(visible);
    }

    /// <summary>
    /// Tampilkan atau sembunyikan semua panel surya yang dipasang.
    /// Hanya Renderer yang di-toggle — GameObject tetap aktif agar SolarPanel.Update() terus berjalan.
    /// </summary>
    void SetPanelsVisible(bool visible)
    {
        foreach (var panel in placedPanels)
        {
            if (panel == null) continue;

            foreach (var rend in panel.GetComponentsInChildren<Renderer>(true))
                rend.enabled = visible;
        }
    }

    void Update()
    {
        // Update total power & energy dari semua SolarPanel setiap frame (tidak perlu roof mode)
        UpdateTotalStats();

        // Perbarui label tombol sesuai state simulasi (di luar roof mode)
        if (!isRoofMode)
            RefreshToggleButtonLabel();

        if (!isRoofMode || isTransitioning) return;

        bool simulationRunning = SimulationManager.Instance != null && SimulationManager.Instance.IsSimulationRunning;

        // Saat simulasi berjalan, placement diblokir dan ghost disembunyikan
        if (simulationRunning)
        {
            if (ghostPanel != null) ghostPanel.SetActive(false);
        }
        else
        {
            HandlePanelPlacement();
        }

        // Update posisi card agar ikut panel saat kamera digerakkan
        if (selectedPanel != null && panelInfoCard != null && panelInfoCard.activeSelf)
            PositionCardNearPanel(selectedPanel.transform.position);
    }

    /// <summary>
    /// Sinkronkan label tombol toggle dengan state simulasi saat berada di luar roof mode.
    /// </summary>
    void RefreshToggleButtonLabel()
    {
        if (toggleButtonText == null) return;

        bool simulationRunning = SimulationManager.Instance != null && SimulationManager.Instance.IsSimulationRunning;
        toggleButtonText.text = simulationRunning ? "Lihat Atap" : "Pasang Panel Surya";
    }

    /// <summary>
    /// Jumlahkan CurrentPower dan TotalEnergy dari semua SolarPanel terpasang,
    /// lalu tampilkan di UI kanan secara real-time setiap frame.
    /// </summary>
    void UpdateTotalStats()
    {
        float sumPower = 0f;

        // Gunakan BatteryManager.CurrentSolarPower sebagai sumber utama
        // karena _registeredPanels di BatteryManager selalu up-to-date via auto-discover di Start().
        // Fallback ke placedPanels hanya jika BatteryManager tidak tersedia.
        if (BatteryManager.Instance != null)
        {
            sumPower = BatteryManager.Instance.CurrentSolarPower;
        }
        else
        {
            foreach (var panel in placedPanels)
            {
                if (panel == null) continue;
                SolarPanel sp = panel.GetComponent<SolarPanel>();
                if (sp == null) continue;
                sumPower += sp.CurrentPower;
            }
        }

        if (totalPowerText != null)
            totalPowerText.text = $"{sumPower:F1} W";

        // Total Energy menampilkan EnergyPool dari BatteryManager:
        // energi yang sudah dihasilkan panel tapi belum tersimpan di battery.
        if (totalEnergyText != null)
        {
            float pool = BatteryManager.Instance != null ? BatteryManager.Instance.EnergyPool : 0f;
            totalEnergyText.text = $"{pool:F3} kWh";
        }

        // Beban Elektronik: hanya furnitur yang aktif pada jam simulasi saat ini
        float currentSimHour = sunController != null ? sunController.TimeOfDay : 0f;
        float bebanElektronik = FurnitureManager.Instance != null
            ? FurnitureManager.Instance.GetActiveWattConsumed(currentSimHour)
            : 0f;
        if (bebanElektronikText != null)
            bebanElektronikText.text = $"{bebanElektronik:F1} W";

        // Status Arus Baterai: tenaga surya (AC output) - beban elektronik
        float acOutput = BatteryManager.Instance != null ? BatteryManager.Instance.CurrentACOutput : 0f;
        float statusArus = acOutput - bebanElektronik;
        if (statusArusBateraiText != null)
            statusArusBateraiText.text = $"{statusArus:F1} W";
    }

    // ── Mode Toggle ───────────────────────────────────────────────────────

    public void ToggleMode()
    {
        if (isTransitioning) return; // Jangan toggle saat transisi

        bool simulationRunning  = SimulationManager.Instance != null && SimulationManager.Instance.IsSimulationRunning;
        bool batteryModeActive  = BatteryInstaller.Instance != null && BatteryInstaller.Instance.IsInstallMode;
        bool furnitureModeActive = FurnitureInstaller.Instance != null && FurnitureInstaller.Instance.IsFurnitureMode;

        // Force-exit furniture mode saat memasuki mode panel surya
        if (!isRoofMode && furnitureModeActive)
            FurnitureInstaller.Instance.ForceExitMode();

        if (isRoofMode)
        {
            StartCoroutine(ExitRoofModeTransition());
        }
        else if (batteryModeActive)
        {
            // Ambil home state dari battery sebelum force-exit
            savedCameraPos    = BatteryInstaller.Instance.SavedCamPos;
            savedCameraRot    = BatteryInstaller.Instance.SavedCamRot;
            savedOrbitTarget  = BatteryInstaller.Instance.SavedOrbitTarget;
            savedOrbitDistance = BatteryInstaller.Instance.SavedOrbitDistance;
            savedOrbitPitch   = BatteryInstaller.Instance.SavedOrbitPitch;
            savedOrbitYaw     = BatteryInstaller.Instance.SavedOrbitYaw;
            savedOrbitOffset  = BatteryInstaller.Instance.SavedOrbitOffset;
            BatteryInstaller.Instance.ForceExitMode();
            homeStatePreset = true;
            StartCoroutine(simulationRunning ? EnterRoofViewOnlyTransition() : EnterRoofModeTransition());
        }
        else if (simulationRunning)
        {
            StartCoroutine(EnterRoofViewOnlyTransition());
        }
        else
        {
            StartCoroutine(EnterRoofModeTransition());
        }
    }

    /// <summary>
    /// Keluar dari roof mode secara paksa tanpa transisi kamera.
    /// Digunakan saat battery installer atau furniture installer mengambil alih.
    /// </summary>
    public void ForceExitMode()
    {
        StopAllCoroutines();
        isRoofMode      = false;
        isTransitioning = false;
        homeStatePreset = false;

        if (ghostPanel != null) ghostPanel.SetActive(false);
        if (roofUI      != null) roofUI.SetActive(false);
        SetRoofVisible(false);

        // Restore UI yang di-hide saat masuk roof mode
        if (catalogPanel            != null) catalogPanel.SetActive(true);
        if (roomButtonContainer     != null) roomButtonContainer.SetActive(true);
        if (solarPanelCatalogSlider != null) solarPanelCatalogSlider.gameObject.SetActive(false);

        if (orbitCamera != null)
        {
            // Restore orbit camera ke state sebelum masuk roof mode.
            // Tanpa ini, orbitCamera.target masih roofCameraPosition sehingga
            // posisi kamera akan salah saat orbit di-enable kembali.
            if (savedOrbitTarget != null) orbitCamera.target = savedOrbitTarget;
            orbitCamera.distance     = savedOrbitDistance;
            orbitCamera.targetOffset = savedOrbitOffset;
            orbitCamera.initialPitch = savedOrbitPitch;  // restore agar ResetToOrigin() pakai sudut home
            orbitCamera.initialYaw   = savedOrbitYaw;

            var bi = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            typeof(OrbitCamera).GetField("yaw",       bi)?.SetValue(orbitCamera, savedOrbitYaw);
            typeof(OrbitCamera).GetField("pitch",     bi)?.SetValue(orbitCamera, savedOrbitPitch);
            typeof(OrbitCamera).GetField("panOffset", bi)?.SetValue(orbitCamera, Vector3.zero);

            // Biarkan orbit tetap disabled — caller (FurnitureInstaller) yang akan
            // menjalankan transisi kamera dan me-re-enable orbit setelah selesai.
            orbitCamera.enabled = false;
        }

        RefreshToggleButtonLabel();
    }

    /// <summary>
    /// Ganti jenis panel surya aktif. Mengupdate prefab, spesifikasi,
    /// dan mengganti semua panel yang sudah terpasang ke tipe baru.
    /// </summary>
    public void SetActivePanelType(SolarPanelData newPanelData)
    {
        if (newPanelData == null) return;

        activePanelData = newPanelData;

        // Update prefab dan spesifikasi installer
        if (newPanelData.prefab3D != null)
            solarPanelPrefab = newPanelData.prefab3D;

        panelWattOutput  = newPanelData.wattOutput;
        panelEfficiency  = newPanelData.efficiency;
        panelArea        = newPanelData.panelArea;
        peakIrradiance   = newPanelData.peakIrradiance;

        // Rebuild ghost panel dengan prefab baru
        if (ghostPanel != null) Destroy(ghostPanel);
        CreateGhostPanel();

        // Jika sudah berada di roof mode, tampilkan ghost segera —
        // konsisten dengan perilaku EnterRoofModeTransition (line 654).
        // Tanpa ini, ghost tetap hidden sampai user hover ke atap.
        if (isRoofMode && ghostPanel != null)
            ghostPanel.SetActive(true);

        // Ganti semua panel yang sudah terpasang ke tipe baru
        ReplaceAllPlacedPanels();

        UpdateUI();
        Debug.Log($"[SolarPanelInstaller] Tipe panel aktif: {newPanelData.panelName} ({newPanelData.wattOutput}W, {newPanelData.efficiency * 100f:F0}%)");
    }

    /// <summary>
    /// Ganti semua panel terpasang dengan prefab tipe baru.
    /// Posisi, rotasi, dan skala dipertahankan.
    /// </summary>
    private void ReplaceAllPlacedPanels()
    {
        if (solarPanelPrefab == null || placedPanels.Count == 0) return;

        for (int i = 0; i < placedPanels.Count; i++)
        {
            GameObject oldPanel = placedPanels[i];
            if (oldPanel == null) continue;

            Vector3 pos = oldPanel.transform.position;
            Quaternion rot = oldPanel.transform.rotation;
            Vector3 scale = oldPanel.transform.localScale;
            string panelName = oldPanel.name;

            // Unregister panel lama dari BatteryManager
            SolarPanel oldSP = oldPanel.GetComponent<SolarPanel>();
            if (oldSP != null && BatteryManager.Instance != null)
                BatteryManager.Instance.UnregisterPanel(oldSP);

            // Hapus card UI lama
            RemovePanelCard(oldPanel);
            Destroy(oldPanel);

            // Buat panel baru
            GameObject newPanel = Instantiate(solarPanelPrefab, pos, rot);
            newPanel.transform.localScale = scale;
            newPanel.name = panelName;

            SolarPanel newSP = newPanel.AddComponent<SolarPanel>();
            ConfigureSolarPanel(newSP);

            if (BatteryManager.Instance != null)
                BatteryManager.Instance.RegisterPanel(newSP);

            placedPanels[i] = newPanel;

            // Perbarui installed panel info
            int panelIndex = 0;
            for (int j = 0; j < _installedPanelInfos.Count; j++)
            {
                if (_installedPanelInfos[j].solarPanel == oldSP)
                {
                    panelIndex = _installedPanelInfos[j].panelIndex;
                    _installedPanelInfos[j] = new InstalledPanelInfo { panelIndex = panelIndex, solarPanel = newSP };
                    break;
                }
            }

            // Buat card UI baru
            CreatePanelCard(panelIndex, newPanel, newSP);
        }
    }

    /// <summary>
    /// Transisi masuk mode atap: kamera bergerak perlahan + atap muncul
    /// </summary>
    IEnumerator EnterRoofModeTransition()
    {
        isTransitioning = true;
        isRoofMode = true;

        Vector3 fromPos;
        Quaternion fromRot;

        if (!homeStatePreset)
        {
            savedCameraPos = Camera.main.transform.position;
            savedCameraRot = Camera.main.transform.rotation;

            if (orbitCamera != null)
            {
                savedOrbitTarget   = orbitCamera.target;
                savedOrbitDistance = orbitCamera.distance;
                savedOrbitPitch    = orbitCamera.initialPitch;
                savedOrbitYaw      = orbitCamera.initialYaw;
                savedOrbitOffset   = orbitCamera.targetOffset;
            }

            if (orbitCamera != null)
                orbitCamera.enabled = false;

            fromPos = savedCameraPos;
            fromRot = savedCameraRot;
        }
        else
        {
            homeStatePreset = false;
            fromPos = Camera.main.transform.position;
            fromRot = Camera.main.transform.rotation;
            // OrbitCamera sudah disabled oleh ForceExitMode caller
        }

        Vector3    targetPos;
        Quaternion targetRot;

        if (roofCameraPosition != null)
        {
            targetPos = roofCameraPosition.position;
            targetRot = roofCameraPosition.rotation;
        }
        else
        {
            Vector3 houseCenter = orbitCamera != null && orbitCamera.target != null
                ? orbitCamera.target.position
                : Vector3.zero;
            targetPos = houseCenter + Vector3.up * roofCameraHeight;
            targetRot = Quaternion.Euler(90f, 0f, 0f);
        }

        // Toggle UI
        if (catalogPanel != null) catalogPanel.SetActive(false);
        if (roomButtonContainer != null) roomButtonContainer.SetActive(false);
        if (toggleButtonText != null) toggleButtonText.text = "Kembali ke Rumah";

        // Saat roof mode aktif, tombol battery kembali ke label normal (bukan "← Kembali")
        if (BatteryInstaller.Instance != null && BatteryInstaller.Instance.IsInstallMode)
            BatteryInstaller.Instance.ShowNormalButtonText();

        // ── Fase 1: Kamera bergerak perlahan ke atas atap ──
        float elapsed = 0f;
        while (elapsed < cameraTransitionDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / cameraTransitionDuration;
            t = t * t * (3f - 2f * t);

            Camera.main.transform.position = Vector3.Lerp(fromPos, targetPos, t);
            Camera.main.transform.rotation = Quaternion.Slerp(fromRot, targetRot, t);

            yield return null;
        }

        // Pastikan posisi final tepat
        Camera.main.transform.position = targetPos;
        Camera.main.transform.rotation = targetRot;

        // ── Fase 2: Atap muncul perlahan ──
        yield return StartCoroutine(FadeInRoof(0.8f));

        // ── Fase 3: Aktifkan orbit camera di mode atap ──
        if (orbitCamera != null && roofCameraPosition != null)
        {
            // Buat target sementara di posisi atap
            orbitCamera.target = roofCameraPosition;
            orbitCamera.targetOffset = Vector3.zero;
            orbitCamera.distance = 15f;
            orbitCamera.initialPitch = 80f;  // Dari atas
            orbitCamera.initialYaw = 0f;
            orbitCamera.minVerticalAngle = 10f;
            orbitCamera.maxVerticalAngle = 89f;
            orbitCamera.minDistance = 5f;
            orbitCamera.maxDistance = 40f;

            // Reset orbit camera state
            // Panggil Start ulang via reflection atau set field langsung
            var yawField = typeof(OrbitCamera).GetField("yaw", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var pitchField = typeof(OrbitCamera).GetField("pitch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var panField = typeof(OrbitCamera).GetField("panOffset", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (yawField != null) yawField.SetValue(orbitCamera, 0f);
            if (pitchField != null) pitchField.SetValue(orbitCamera, 80f);
            if (panField != null) panField.SetValue(orbitCamera, Vector3.zero);

            orbitCamera.enabled = true;
        }

        // Tampilkan roof UI
        if (roofUI != null) roofUI.SetActive(true);

        // Tampilkan ghost panel
        if (ghostPanel != null) ghostPanel.SetActive(true);

        // ── Fase 4: Slide-in catalog panel surya ──
        if (solarPanelCatalogSlider != null)
            solarPanelCatalogSlider.SlideIn();

        isTransitioning = false;
        Debug.Log("[SolarPanel] Mode Atap AKTIF");
    }

    /// <summary>
    /// Pindahkan kamera ke atas atap tanpa masuk mode instalasi (simulasi sedang berjalan).
    /// Placement diblokir; panel yang sudah ada tetap terlihat.
    /// </summary>
    IEnumerator EnterRoofViewOnlyTransition()
    {
        isTransitioning = true;
        isRoofMode = true;

        Vector3 fromPos;
        Quaternion fromRot;

        if (!homeStatePreset)
        {
            savedCameraPos = Camera.main.transform.position;
            savedCameraRot = Camera.main.transform.rotation;

            if (orbitCamera != null)
            {
                savedOrbitTarget   = orbitCamera.target;
                savedOrbitDistance = orbitCamera.distance;
                savedOrbitPitch    = orbitCamera.initialPitch;
                savedOrbitYaw      = orbitCamera.initialYaw;
                savedOrbitOffset   = orbitCamera.targetOffset;
                orbitCamera.enabled = false;
            }

            fromPos = savedCameraPos;
            fromRot = savedCameraRot;
        }
        else
        {
            homeStatePreset = false;
            fromPos = Camera.main.transform.position;
            fromRot = Camera.main.transform.rotation;
        }

        Vector3 targetPos;
        Quaternion targetRot;

        if (roofCameraPosition != null)
        {
            targetPos = roofCameraPosition.position;
            targetRot = roofCameraPosition.rotation;
        }
        else
        {
            Vector3 houseCenter = orbitCamera != null && orbitCamera.target != null
                ? orbitCamera.target.position : Vector3.zero;
            targetPos = houseCenter + Vector3.up * roofCameraHeight;
            targetRot = Quaternion.Euler(90f, 0f, 0f);
        }

        // Toggle UI — label tombol "← Kembali ke Rumah"
        if (catalogPanel != null) catalogPanel.SetActive(false);
        if (roomButtonContainer != null) roomButtonContainer.SetActive(false);
        if (toggleButtonText != null) toggleButtonText.text = "← Kembali ke Rumah";

        // Saat roof mode aktif, tombol battery kembali ke label normal (bukan "← Kembali")
        if (BatteryInstaller.Instance != null && BatteryInstaller.Instance.IsInstallMode)
            BatteryInstaller.Instance.ShowNormalButtonText();

        // Transisi kamera
        float elapsed = 0f;
        while (elapsed < cameraTransitionDuration)
        {
            elapsed += Time.deltaTime;
            float t  = Mathf.Clamp01(elapsed / cameraTransitionDuration);
            t = t * t * (3f - 2f * t);
            Camera.main.transform.position = Vector3.Lerp(fromPos, targetPos, t);
            Camera.main.transform.rotation = Quaternion.Slerp(fromRot, targetRot, t);
            yield return null;
        }

        Camera.main.transform.position = targetPos;
        Camera.main.transform.rotation = targetRot;

        // Tampilkan atap dan panel yang sudah dipasang
        SetRoofVisible(true);

        // Aktifkan orbit camera di mode atap
        if (orbitCamera != null && roofCameraPosition != null)
        {
            orbitCamera.target        = roofCameraPosition;
            orbitCamera.targetOffset  = Vector3.zero;
            orbitCamera.distance      = 15f;
            orbitCamera.initialPitch  = 80f;
            orbitCamera.initialYaw    = 0f;
            orbitCamera.minVerticalAngle = 10f;
            orbitCamera.maxVerticalAngle = 89f;
            orbitCamera.minDistance   = 5f;
            orbitCamera.maxDistance   = 40f;

            var yawField   = typeof(OrbitCamera).GetField("yaw",       System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var pitchField = typeof(OrbitCamera).GetField("pitch",     System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var panField   = typeof(OrbitCamera).GetField("panOffset", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (yawField   != null) yawField.SetValue(orbitCamera, 0f);
            if (pitchField != null) pitchField.SetValue(orbitCamera, 80f);
            if (panField   != null) panField.SetValue(orbitCamera, Vector3.zero);

            orbitCamera.enabled = true;
        }

        // Tampilkan roofUI (info panel) tapi ghost tetap tersembunyi
        if (roofUI != null) roofUI.SetActive(true);
        if (ghostPanel != null) ghostPanel.SetActive(false);

        // Slide-in catalog panel surya setelah atap muncul
        if (solarPanelCatalogSlider != null)
            solarPanelCatalogSlider.SlideIn();

        isTransitioning = false;
        Debug.Log("[SolarPanel] Mode Atap VIEW-ONLY (simulasi berjalan)");
    }

    /// <summary>
    /// Transisi keluar mode atap: atap menghilang + kamera kembali
    /// </summary>
    IEnumerator ExitRoofModeTransition()
    {
        isTransitioning = true;

        // Sembunyikan ghost
        if (ghostPanel != null) ghostPanel.SetActive(false);

        // Sembunyikan roof UI
        if (roofUI != null) roofUI.SetActive(false);

        // ── Fase 1: Atap menghilang perlahan ──
        yield return StartCoroutine(FadeOutRoof(0.8f));

        // ── Fase 2: Kamera kembali perlahan ──
        // Nonaktifkan orbit dulu untuk transisi manual
        if (orbitCamera != null)
            orbitCamera.enabled = false;

        Vector3 startPos = Camera.main.transform.position;
        Quaternion startRot = Camera.main.transform.rotation;

        float elapsed = 0f;
        while (elapsed < cameraTransitionDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / cameraTransitionDuration;
            t = t * t * (3f - 2f * t); // Smooth easing

            Camera.main.transform.position = Vector3.Lerp(startPos, savedCameraPos, t);
            Camera.main.transform.rotation = Quaternion.Slerp(startRot, savedCameraRot, t);

            yield return null;
        }

        Camera.main.transform.position = savedCameraPos;
        Camera.main.transform.rotation = savedCameraRot;

        bool batteryModeActive = BatteryInstaller.Instance != null && BatteryInstaller.Instance.IsInstallMode;

        // Re-enable orbit camera dengan settings asli — HANYA jika battery mode tidak aktif
        if (!batteryModeActive)
        {
            if (orbitCamera != null)
            {
                orbitCamera.target = savedOrbitTarget;
                orbitCamera.distance = savedOrbitDistance;
                orbitCamera.initialPitch = savedOrbitPitch;
                orbitCamera.initialYaw = savedOrbitYaw;
                orbitCamera.targetOffset = savedOrbitOffset;

                // Restore orbit state
                var yawField = typeof(OrbitCamera).GetField("yaw", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var pitchField = typeof(OrbitCamera).GetField("pitch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var panField = typeof(OrbitCamera).GetField("panOffset", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (yawField != null) yawField.SetValue(orbitCamera, savedOrbitYaw);
                if (pitchField != null) pitchField.SetValue(orbitCamera, savedOrbitPitch);
                if (panField != null) panField.SetValue(orbitCamera, Vector3.zero);

                orbitCamera.enabled = true;
            }

            // Toggle UI
            if (catalogPanel != null) catalogPanel.SetActive(true);
            if (roomButtonContainer != null) roomButtonContainer.SetActive(true);
            if (solarPanelCatalogSlider != null) solarPanelCatalogSlider.gameObject.SetActive(false);
        }
        else
        {
            // Battery mode masih aktif — reinit yaw/pitch dan kembalikan teks tombol
            BatteryInstaller.Instance.ReinitCameraAngles();
            BatteryInstaller.Instance.ShowBackButtonText();
            if (solarPanelCatalogSlider != null) solarPanelCatalogSlider.gameObject.SetActive(false);
        }
        if (toggleButtonText != null) toggleButtonText.text = "Instal Panel Surya";

        isRoofMode = false;
        isTransitioning = false;
        Debug.Log("[SolarPanel] Mode Atap NONAKTIF");
    }

    /// <summary>
    /// Fade in atap: objek muncul satu per satu dari bawah ke atas
    /// </summary>
    IEnumerator FadeInRoof(float duration)
    {
        // Aktifkan semua renderer dan collider
        SetRoofVisible(true);

        // Scale animation: mulai dari kecil, membesar
        float elapsed = 0f;
        
        // Simpan scale asli
        Dictionary<GameObject, Vector3> originalScales = new();
        foreach (var roof in roofObjects)
        {
            if (roof != null)
            {
                originalScales[roof] = roof.transform.localScale;
                roof.transform.localScale = Vector3.zero;
            }
        }

        // Animasi scale dari 0 ke original
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            t = t * t * (3f - 2f * t); // Smooth easing

            foreach (var roof in roofObjects)
            {
                if (roof != null && originalScales.ContainsKey(roof))
                {
                    roof.transform.localScale = Vector3.Lerp(Vector3.zero, originalScales[roof], t);
                }
            }

            yield return null;
        }

        // Pastikan scale final tepat
        foreach (var roof in roofObjects)
        {
            if (roof != null && originalScales.ContainsKey(roof))
            {
                roof.transform.localScale = originalScales[roof];
            }
        }
    }

    /// <summary>
    /// Fade out atap: objek mengecil lalu hilang
    /// </summary>
    IEnumerator FadeOutRoof(float duration)
    {
        float elapsed = 0f;

        // Simpan scale asli
        Dictionary<GameObject, Vector3> originalScales = new();
        foreach (var roof in roofObjects)
        {
            if (roof != null)
            {
                originalScales[roof] = roof.transform.localScale;
            }
        }

        // Animasi scale dari original ke 0
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            t = t * t * (3f - 2f * t);

            foreach (var roof in roofObjects)
            {
                if (roof != null && originalScales.ContainsKey(roof))
                {
                    roof.transform.localScale = Vector3.Lerp(originalScales[roof], Vector3.zero, t);
                }
            }

            yield return null;
        }

        // Sembunyikan sepenuhnya
        SetRoofVisible(false);

        // Kembalikan scale ke original (supaya bisa di-show lagi nanti)
        foreach (var roof in roofObjects)
        {
            if (roof != null && originalScales.ContainsKey(roof))
            {
                roof.transform.localScale = originalScales[roof];
            }
        }
    }

    // ── Panel Placement ───────────────────────────────────────────────────

    void HandlePanelPlacement()
    {
        Ray ray = Camera.main.ScreenPointToRay(GameInput.PointerPosition);
        RaycastHit hit;

        // Blokir klik hanya jika pointer tepat di atas UI yang terdaftar sebagai blocker
        bool overAnyUI = IsPointerOverBlockerUI();

        // Raycast ke layer Roof untuk pasang panel
        if (Physics.Raycast(ray, out hit, 5000f, roofLayer))
        {
            Vector3 snappedPos = SnapToGrid(hit.point);

            // Raycast ke bawah dari posisi snapped untuk dapat Y permukaan atap yang tepat,
            // bukan Y titik klik asli yang mungkin berbeda setelah snap X/Z
            if (TryGetRoofSurfaceY(snappedPos, out float surfaceY))
                snappedPos.y = surfaceY + 3f;
            else
                snappedPos.y = hit.point.y + 3f;

            bool occupied = IsOccupied(snappedPos);
            bool validPlacement = IsOverRoof(snappedPos) && !occupied;

            // Tampilkan ghost hanya jika posisi valid (seluruh panel di atas atap)
            if (ghostPanel != null)
            {
                ghostPanel.SetActive(validPlacement);
                if (validPlacement)
                {
                    ghostPanel.transform.position = snappedPos;
                    ghostPanel.transform.localScale = new Vector3(200f, 3f, 130f);
                    ghostPanel.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
                }
            }

            if (GameInput.PrimaryPressed() && !overAnyUI)
            {
                if (occupied)
                {
                    // Klik kiri di cell terisi → tampilkan card info panel
                    ShowPanelCard(FindPanelAtPosition(snappedPos));
                }
                else if (validPlacement && placedPanels.Count < maxPanels)
                {
                    // Klik kiri di cell kosong yang valid → pasang panel
                    HidePanelCard();
                    PlacePanel(snappedPos);
                }
            }
        }
        else
        {
            if (ghostPanel != null)
                ghostPanel.SetActive(false);

            // Klik kiri di luar atap → tutup card (hanya jika tidak klik UI)
            if (GameInput.PrimaryPressed() && !overAnyUI)
                HidePanelCard();
        }
    }

    /// <summary>
    /// Cek apakah pointer mouse saat ini tepat berada di atas salah satu UI blocker yang terdaftar
    /// (panelInfoCard atau tombol-tombol di placementBlockerButtons).
    /// Lebih presisi dari IsPointerOverGameObject() karena tidak terpengaruh panel UI fullscreen.
    /// </summary>
    bool IsPointerOverBlockerUI()
    {
        Vector2 screenPos = GameInput.PointerPosition;

        // Cek panelInfoCard
        if (panelInfoCard != null && panelInfoCard.activeSelf)
        {
            var rt = panelInfoCard.GetComponent<RectTransform>();
            if (rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, null))
                return true;
        }

        // Cek semua tombol yang didaftarkan sebagai blocker
        foreach (var rt in placementBlockerButtons)
        {
            if (rt != null && rt.gameObject.activeInHierarchy &&
                RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, null))
                return true;
        }

        return false;
    }

    /// <summary>
    /// PlacePanel menerima posisi final yang sudah di-snap dan di-validasi dari HandlePanelPlacement.
    /// Attach SolarPanel component dan buat card di panel list UI.
    /// </summary>
    void PlacePanel(Vector3 snappedPos)
    {
        if (solarPanelPrefab == null)
        {
            Debug.LogWarning("[SolarPanel] Prefab panel surya belum di-assign!");
            return;
        }

        totalPanelCounter++;
        GameObject panel = Instantiate(solarPanelPrefab, snappedPos, Quaternion.Euler(0f, 0f, 0f));
        panel.transform.localScale = new Vector3(200f, 3f, 130f);
        panel.name = $"SolarPanel_{totalPanelCounter}";

        // Attach dan konfigurasi SolarPanel component
        SolarPanel sp = panel.AddComponent<SolarPanel>();
        ConfigureSolarPanel(sp);

        // Daftarkan panel ke battery agar output-nya dihitung
        if (BatteryManager.Instance != null)
            BatteryManager.Instance.RegisterPanel(sp);

        placedPanels.Add(panel);
        _installedPanelInfos.Add(new InstalledPanelInfo { panelIndex = totalPanelCounter, solarPanel = sp });

        // Buat card di panel list UI
        CreatePanelCard(totalPanelCounter, panel, sp);

        OnPanelCountChanged.Invoke(placedPanels.Count);
        UpdateUI();
        Debug.Log($"[SolarPanel] Panel #{totalPanelCounter} dipasang di {snappedPos}");

        // ── SolarEdu Tracking ──
        if (SolarEduManager.Instance != null)
        {
            SolarEduManager.Instance.SendStatement(
                SolarEduVerb.Memasang,
                $"solaredu://panel-surya/panel-{totalPanelCounter}",
                $"Panel Surya #{totalPanelCounter}"
            );
        }
    }

    /// <summary>
    /// Isi properti SolarPanel via reflection (field SerializeField tidak bisa diset langsung dari luar).
    /// </summary>
    void ConfigureSolarPanel(SolarPanel sp)
    {
        var type = typeof(SolarPanel);
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

        SetField(type, sp, flags, "sunController", sunController);
        SetField(type, sp, flags, "panelArea", panelArea);
        SetField(type, sp, flags, "panelEfficiency", panelEfficiency);
        SetField(type, sp, flags, "peakIrradiance", peakIrradiance);
    }

    void SetField(System.Type type, object target, System.Reflection.BindingFlags flags, string fieldName, object value)
    {
        var field = type.GetField(fieldName, flags);
        if (field != null)
            field.SetValue(target, value);
        else
            Debug.LogWarning($"[SolarPanelInstaller] Field '{fieldName}' tidak ditemukan di SolarPanel.");
    }

    /// <summary>
    /// Buat card UI di panel list container untuk panel yang baru dipasang.
    /// </summary>
    void CreatePanelCard(int panelIndex, GameObject panel, SolarPanel sp)
    {
        if (panelCardPrefab == null || panelListContainer == null)
        {
            Debug.LogWarning($"[SolarPanelInstaller] CreatePanelCard gagal: prefab={panelCardPrefab}, container={panelListContainer}");
            return;
        }

        GameObject spawned = Instantiate(panelCardPrefab);
        Debug.Log($"[SolarPanelInstaller] Spawned '{spawned.name}', childCount={spawned.transform.childCount}");

        PanelCardUI cardUI = spawned.GetComponent<PanelCardUI>();
        GameObject card;

        if (cardUI != null)
        {
            card = spawned;
            card.transform.SetParent(panelListContainer, false);
            Debug.Log($"[SolarPanelInstaller] Card root langsung, parent={panelListContainer.name}");
        }
        else
        {
            cardUI = spawned.GetComponentInChildren<PanelCardUI>(true);
            if (cardUI == null)
            {
                Debug.LogWarning($"[SolarPanelInstaller] PanelCardUI tidak ditemukan sama sekali di prefab '{panelCardPrefab.name}'");
                Destroy(spawned);
                return;
            }

            card = cardUI.gameObject;
            Debug.Log($"[SolarPanelInstaller] Card ditemukan di child '{card.name}', memindahkan ke container");
            card.transform.SetParent(panelListContainer, false);
            Destroy(spawned);
        }

        card.name = $"Card_Panel_{panelIndex}";
        cardUI.Init(panelIndex, sp);
        panelToCard[panel] = card;
        Debug.Log($"[SolarPanelInstaller] Card #{panelIndex} dibuat, container childCount={panelListContainer.childCount}");
    }

    /// <summary>
    /// Hapus card UI yang terhubung ke panel dari list container.
    /// </summary>
    void RemovePanelCard(GameObject panel)
    {
        if (panelToCard.TryGetValue(panel, out GameObject card))
        {
            Destroy(card);
            panelToCard.Remove(panel);
        }
    }

    void RemoveLastPanel()
    {
        if (placedPanels.Count == 0) return;

        var last = placedPanels[placedPanels.Count - 1];
        SolarPanel sp = last != null ? last.GetComponent<SolarPanel>() : null;

        placedPanels.RemoveAt(placedPanels.Count - 1);
        if (sp != null) _installedPanelInfos.RemoveAll(info => info.solarPanel == sp);
        RemovePanelCard(last);
        Destroy(last);

        OnPanelCountChanged.Invoke(placedPanels.Count);
        UpdateUI();
        Debug.Log("[SolarPanel] Panel terakhir dihapus");

        // ── SolarEdu Tracking ──
        if (SolarEduManager.Instance != null)
        {
            SolarEduManager.Instance.SendStatement(
                SolarEduVerb.Mengamati,
                "solaredu://panel-surya/hapus",
                "Hapus Panel Surya"
            );
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Snap posisi ke grid dua sumbu berdasarkan dimensi panel + gap Inspector.
    /// Grid X = 200 (lebar panel) + panelGapX
    /// Grid Z = 130 (tinggi panel) + panelGapZ
    /// </summary>
    Vector3 SnapToGrid(Vector3 pos)
    {
        const float PanelWidth = 200f;
        const float PanelDepth = 130f;

        float gridX = PanelWidth + panelGapX;
        float gridZ = PanelDepth + panelGapZ;

        float x = Mathf.Round(pos.x / gridX) * gridX;
        float z = Mathf.Round(pos.z / gridZ) * gridZ;
        return new Vector3(x, pos.y, z);
    }

    /// <summary>
    /// Validasi apakah seluruh area panel (pusat + 4 sudut) berada di atas atap.
    /// Mencegah penempatan panel yang melewati tepi atau sudut atap tidak beraturan.
    /// </summary>
    bool IsOverRoof(Vector3 snappedPos)
    {
        const float RayOriginOffset = 500f;
        const float HalfWidth = 100f;  // Half dari localScale.x = 200f
        const float HalfDepth = 65f;   // Half dari localScale.z = 130f

        Vector3[] checkPoints = new Vector3[]
        {
            snappedPos,
            snappedPos + new Vector3( HalfWidth, 0f,  HalfDepth),
            snappedPos + new Vector3(-HalfWidth, 0f,  HalfDepth),
            snappedPos + new Vector3( HalfWidth, 0f, -HalfDepth),
            snappedPos + new Vector3(-HalfWidth, 0f, -HalfDepth),
        };

        foreach (var point in checkPoints)
        {
            Vector3 origin = new Vector3(point.x, point.y + RayOriginOffset, point.z);
            if (!Physics.Raycast(origin, Vector3.down, RayOriginOffset + 50f, roofLayer))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Raycast ke bawah dari posisi snapped (X/Z) untuk mendapat Y permukaan atap yang tepat.
    /// Dipakai agar semua panel rata di ketinggian yang konsisten, bukan ketinggian titik klik.
    /// </summary>
    bool TryGetRoofSurfaceY(Vector3 pos, out float surfaceY)
    {
        const float RayOriginOffset = 500f;
        Vector3 origin = new Vector3(pos.x, pos.y + RayOriginOffset, pos.z);
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit roofHit, RayOriginOffset + 50f, roofLayer))
        {
            surfaceY = roofHit.point.y;
            return true;
        }
        surfaceY = 0f;
        return false;
    }

    /// <summary>
    /// Cek apakah grid cell di posisi snapped sudah diisi panel lain.
    /// Perbandingan hanya di sumbu X dan Z karena Y bisa beda tipis antar panel.
    /// </summary>
    bool IsOccupied(Vector3 snappedPos)
    {
        foreach (var panel in placedPanels)
        {
            if (panel == null) continue;
            Vector3 existing = SnapToGrid(panel.transform.position);
            if (Mathf.Approximately(existing.x, snappedPos.x) &&
                Mathf.Approximately(existing.z, snappedPos.z))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Kembalikan panel terpasang yang menempati grid cell di posisi snapped.
    /// </summary>
    GameObject FindPanelAtPosition(Vector3 snappedPos)
    {
        foreach (var panel in placedPanels)
        {
            if (panel == null) continue;
            Vector3 existing = SnapToGrid(panel.transform.position);
            if (Mathf.Approximately(existing.x, snappedPos.x) &&
                Mathf.Approximately(existing.z, snappedPos.z))
                return panel;
        }
        return null;
    }

    /// <summary>
    /// Tampilkan card info di samping panel yang dipilih (posisi mengikuti world position panel).
    /// </summary>
    void ShowPanelCard(GameObject panel)
    {
        if (panelInfoCard == null || panel == null) return;

        selectedPanel = panel;
        int panelIndex = placedPanels.IndexOf(panel) + 1;

        if (panelInfoText != null)
            panelInfoText.text = $"Panel #{panelIndex}\nDaya: {panelWattOutput} W";

        panelInfoCard.SetActive(true);
        PositionCardNearPanel(panel.transform.position);
    }

    /// <summary>
    /// Hitung posisi card di canvas berdasarkan world position panel.
    /// Card muncul di samping panel di layar, mengikuti saat kamera bergerak.
    /// </summary>
    void PositionCardNearPanel(Vector3 worldPos)
    {
        if (panelInfoCard == null) return;

        RectTransform cardRect = panelInfoCard.GetComponent<RectTransform>();
        Canvas canvas = panelInfoCard.GetComponentInParent<Canvas>();
        if (cardRect == null || canvas == null) return;

        // Konversi posisi 3D panel ke screen space
        Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPos);

        // Tambahkan offset layar agar card tidak menimpa panel
        screenPos.x += cardScreenOffset.x;
        screenPos.y += cardScreenOffset.y;

        // Konversi screen position ke local position di dalam canvas
        Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvas.GetComponent<RectTransform>(),
            screenPos,
            uiCamera,
            out Vector2 localPoint))
        {
            cardRect.localPosition = localPoint;
        }
    }

    /// <summary>
    /// Sembunyikan card info dan reset panel yang dipilih.
    /// </summary>
    public void HidePanelCard()
    {
        if (panelInfoCard != null)
            panelInfoCard.SetActive(false);
        selectedPanel = null;
    }

    /// <summary>
    /// Hapus panel yang sedang dipilih via card, lalu tutup card.
    /// </summary>
    public void DeleteSelectedPanel()
    {
        if (selectedPanel == null) return;

        // Unregister dari battery sebelum destroy
        SolarPanel sp = selectedPanel.GetComponent<SolarPanel>();
        if (sp != null && BatteryManager.Instance != null)
            BatteryManager.Instance.UnregisterPanel(sp);

        placedPanels.Remove(selectedPanel);
        if (sp != null) _installedPanelInfos.RemoveAll(info => info.solarPanel == sp);
        RemovePanelCard(selectedPanel);
        Destroy(selectedPanel);
        HidePanelCard();
        OnPanelCountChanged.Invoke(placedPanels.Count);
        UpdateUI();
        Debug.Log($"[SolarPanel] Panel dihapus via card. Sisa: {placedPanels.Count}");
    }

    void CreateGhostPanel()
    {
        if (solarPanelPrefab == null) return;

        ghostPanel = Instantiate(solarPanelPrefab);
        ghostPanel.name = "GhostPanel";

        // Buat transparan
        var renderers = ghostPanel.GetComponentsInChildren<Renderer>();
        foreach (var r in renderers)
        {
            if (panelGhostMaterial != null)
                r.material = panelGhostMaterial;
            else
            {
                // Fallback: buat semi-transparan
                foreach (var mat in r.materials)
                {
                    Color c = mat.color;
                    c.a = 0.4f;
                    mat.color = c;
                }
            }
        }

        // Nonaktifkan collider pada ghost
        var colliders = ghostPanel.GetComponentsInChildren<Collider>();
        foreach (var c in colliders)
            c.enabled = false;

        ghostPanel.SetActive(false);
    }

    void UpdateUI()
    {
        if (panelCountText != null)
            panelCountText.text = $"Panel: {placedPanels.Count}/{maxPanels}";

        // totalWattText di RoofUI: tampilkan kapasitas terpasang (panel × watt peak)
        float installedCapacity = placedPanels.Count * panelWattOutput;
        if (totalWattText != null)
            totalWattText.text = $"Kapasitas: {installedCapacity} W";
    }

    // ── Public API ─────────────────────────────────────────────────────────
    public float GetTotalSolarWatt()
    {
        return placedPanels.Count * panelWattOutput;
    }

    public int GetPanelCount()
    {
        return placedPanels.Count;
    }

    /// <summary>
    /// Returns a snapshot of all currently installed panels with their index and SolarPanel reference.
    /// Used by SolarPanelTopDownUI to rebuild the card list.
    /// </summary>
    public List<InstalledPanelInfo> GetInstalledPanels() => new List<InstalledPanelInfo>(_installedPanelInfos);

    /// <summary>
    /// Cari TextMeshProUGUI di scene berdasarkan path GameObject (separator '/').
    /// Digunakan sebagai fallback jika referensi belum di-assign di Inspector.
    /// </summary>
    /// <summary>
    /// Cari TextMeshProUGUI berdasarkan path relatif dari Canvas.
    /// Menggunakan Transform.Find yang bisa menemukan child inactive.
    /// </summary>
    TextMeshProUGUI FindTMPByPath(string path)
    {
        // Coba GameObject.Find dulu (hanya bekerja pada GO aktif)
        GameObject go = GameObject.Find(path);
        if (go != null)
            return go.GetComponent<TextMeshProUGUI>();

        // Fallback: cari via Transform.Find dari Canvas (bekerja pada GO inactive)
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas != null)
        {
            // Hapus prefix "Canvas/" jika ada, karena Transform.Find mencari dari child
            string relativePath = path.StartsWith("Canvas/") ? path.Substring("Canvas/".Length) : path;
            Transform t = canvas.transform.Find(relativePath);
            if (t != null)
                return t.GetComponent<TextMeshProUGUI>();
        }

        Debug.LogWarning($"[SolarPanelInstaller] GameObject tidak ditemukan: '{path}'");
        return null;
    }
}
