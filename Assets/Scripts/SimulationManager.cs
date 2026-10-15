using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using SolarEdu;

/// <summary>
/// Mengontrol state simulasi secara global.
/// Durasi simulasi dinyatakan dalam jam in-game; simulasi berhenti
/// ketika SunController.TimeOfDay mencapai jam akhir yang dikonfigurasi.
/// </summary>
public class SimulationManager : MonoBehaviour
{
    public static SimulationManager Instance { get; private set; }

    [Header("UI")]
    [SerializeField] Button simulationButton;
    [SerializeField] GameObject configOverlay;
    [SerializeField] ObjectiveWarningUI objectiveWarningUI;

    [Tooltip("Panel UI Right yang muncul saat simulasi dimulai.")]
    [SerializeField] private UIPanelSlider uiRightSlider;

    [Tooltip("Panel Objectives yang disembunyikan saat simulasi berjalan.")]
    [SerializeField] private GameObject objectivesPanel;

    [Tooltip("Sun Indicator yang hanya ditampilkan saat simulasi berjalan.")]
    [SerializeField] private GameObject sunIndicatorPanel;

    [Tooltip("Tombol untuk kembali ke menu utama.")]
    [SerializeField] private Button backToMenuButton;

    [Tooltip("UI elements yang disembunyikan saat simulasi berjalan dan dikembalikan saat simulasi selesai.")]
    [SerializeField] private GameObject[] simulationHideList;

    /// <summary>State publik yang dibaca SolarPanel, SunController, dll.</summary>
    public bool IsSimulationRunning { get; private set; } = false;

    /// <summary>Jam mulai simulasi terakhir (0–24).</summary>
    public float SimulationStartHour { get; private set; } = 0f;

    /// <summary>Durasi simulasi yang dikonfigurasi dalam jam in-game.</summary>
    public float SimulationDurationHours { get; private set; } = 0f;

    /// <summary>Dipanggil saat simulasi berhenti (baik otomatis maupun manual).</summary>
    public UnityEvent OnSimulationEnded;

    private float _simulationEndHour = 0f;
    private float _elapsedSimHours = 0f;
    private bool _inResultsState = false;
    private bool _lastPlacementActive = false;
    private SunController _sunController;

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
        _sunController = FindFirstObjectByType<SunController>();

        if (configOverlay == null)
        {
            var canvas = FindFirstObjectByType<Canvas>();
            if (canvas != null)
            {
                Transform t = canvas.transform.Find("ConfigOverlay");
                if (t != null) configOverlay = t.gameObject;
            }
        }

        if (simulationButton == null)
        {
            var canvas = FindFirstObjectByType<Canvas>();
            if (canvas != null)
            {
                Transform t = canvas.transform.Find("SimulasiButton");
                if (t != null) simulationButton = t.GetComponent<Button>();
            }
        }

        if (simulationButton != null)
            simulationButton.onClick.AddListener(OnSimulationButtonPressed);

        // Auto-find ObjectiveWarningUI jika belum di-assign via Inspector
        if (objectiveWarningUI == null)
            objectiveWarningUI = FindFirstObjectByType<ObjectiveWarningUI>(FindObjectsInactive.Include);

        // Auto-find UIPanelSlider pada UI Right
        if (uiRightSlider == null)
        {
            var canvas = FindFirstObjectByType<Canvas>();
            if (canvas != null)
            {
                Transform t = canvas.transform.Find("UI Right");
                if (t != null) uiRightSlider = t.GetComponent<UIPanelSlider>();
            }
        }

        // Auto-find Objectives panel
        if (objectivesPanel == null)
        {
            var canvas = FindFirstObjectByType<Canvas>();
            if (canvas != null)
            {
                Transform t = canvas.transform.Find("Objectives");
                if (t != null) objectivesPanel = t.gameObject;
            }
        }

        // Auto-find Sun Indicator panel
        if (sunIndicatorPanel == null)
        {
            var canvas = FindFirstObjectByType<Canvas>();
            if (canvas != null)
            {
                Transform t = canvas.transform.Find("Sun INdicator");
                if (t != null) sunIndicatorPanel = t.gameObject;
            }
        }

        // Sembunyikan Sun Indicator di awal (hanya muncul saat simulasi)
        if (sunIndicatorPanel != null)
            sunIndicatorPanel.SetActive(false);

        // Auto-find tombol kembali ke menu
        if (backToMenuButton == null)
        {
            var canvas = FindFirstObjectByType<Canvas>();
            if (canvas != null)
            {
                Transform t = canvas.transform.Find("BtnKembali");
                if (t != null) backToMenuButton = t.GetComponent<Button>();
            }
        }

        if (backToMenuButton != null)
        {
            backToMenuButton.onClick.AddListener(BackToMenu);

            // Set UISprite bawaan Unity sebagai source image
            var img = backToMenuButton.GetComponent<Image>();
            if (img != null && img.sprite == null)
            {
                var defaultSprite = UnityEngine.Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
                if (defaultSprite != null)
                    img.sprite = defaultSprite;
            }
        }

        // Auto-find simulationHideList jika belum di-assign via Inspector
        if (simulationHideList == null || simulationHideList.Length == 0)
        {
            var canvas = FindFirstObjectByType<Canvas>();
            if (canvas != null)
            {
                var hideNames = new string[]
                {
                    "RoofUI", "FreeModeButton", "RoomButtonContainer",
                    "ResetViewButton", "Instal buttons", "BtnKembali"
                };
                var found = new System.Collections.Generic.List<GameObject>();
                foreach (var n in hideNames)
                {
                    Transform t = canvas.transform.Find(n);
                    if (t != null) found.Add(t.gameObject);
                }
                simulationHideList = found.ToArray();
            }
        }

        ApplyButtonState();
    }

    void Update()
    {
        // Sembunyikan/tampilkan tombol Mulai Simulasi saat mode penempatan aktif
        bool placing = IsAnyPlacementActive();
        if (placing != _lastPlacementActive)
        {
            _lastPlacementActive = placing;
            ApplyButtonState();
        }

        if (!IsSimulationRunning) return;
        if (_sunController == null) return;

        _elapsedSimHours += _sunController.SimulationHoursPerSecond * Time.deltaTime;
        if (_elapsedSimHours >= SimulationDurationHours)
            StopSimulation();
    }

    /// <summary>
    /// Dipanggil oleh onClick tombol SimulasiButton.
    /// Jika ada objective yang belum terpenuhi, tampilkan warning terlebih dahulu.
    /// Jika semua terpenuhi (atau user pilih "Lanjutkan"), buka panel konfigurasi.
    /// </summary>
    public void OnSimulationButtonPressed()
    {
        if (IsSimulationRunning) return;

        var objManager = ObjectiveManager.Instance;
        if (objManager != null && !objManager.AreAllObjectivesComplete())
        {
            if (objectiveWarningUI != null)
            {
                objectiveWarningUI.Show(objManager.GetIncompleteObjectives(), OpenConfigOverlay);
                return;
            }
        }

        OpenConfigOverlay();
    }

    private void OpenConfigOverlay()
    {
        if (configOverlay != null)
            configOverlay.SetActive(true);
        else
            Debug.LogError("[SimulationManager] configOverlay adalah NULL!");
    }

    /// <summary>
    /// Dipanggil oleh SimulationConfigPanel saat user menekan "Mulai".
    /// </summary>
    /// <summary>
    /// Dipanggil oleh SimulationConfigPanel saat user menekan "Mulai".
    /// </summary>
    public void StartSimulation(float startHour, float durationHours)
    {
        SimulationStartHour = startHour;
        SimulationDurationHours = durationHours;
        _simulationEndHour = startHour + durationHours;
        _elapsedSimHours = 0f;
        IsSimulationRunning = true;

        // ── BARU: Mengatur kecepatan Day Cycle berdasarkan durasi simulasi ──
        if (_sunController != null)
        {
            if (durationHours < 6f)
            {
                // 2 detik real-world = 1 jam in-game (Lebih lambat agar tidak terlalu cepat selesai)
                _sunController.SimulationHoursPerSecond = 0.25f;
            }
            else
            {
                // 1 detik real-world = 1 jam in-game (Kecepatan normal)
                _sunController.SimulationHoursPerSecond = 0.5f;
            }
        }
        // ────────────────────────────────────────────────────────────────────

        if (BatteryManager.Instance != null)
            BatteryManager.Instance.ResetConsumption();

        // Mulai merekam log per jam
        if (SimulationHourlyLogger.Instance != null)
            SimulationHourlyLogger.Instance.BeginLogging(startHour);

        // Tampilkan UI Right dengan animasi slide
        if (uiRightSlider != null)
            uiRightSlider.SlideIn();

        // Sembunyikan Objectives saat simulasi berjalan
        if (objectivesPanel != null)
            objectivesPanel.SetActive(false);

        // Tampilkan Sun Indicator saat simulasi berjalan
        if (sunIndicatorPanel != null)
            sunIndicatorPanel.SetActive(true);

        // Sembunyikan semua UI yang tidak relevan saat simulasi berjalan
        SetSimulationHideList(false);

        // Paksa keluar dari semua mode instalasi yang sedang aktif
        if (BatteryInstaller.Instance != null && BatteryInstaller.Instance.IsInstallMode)
            BatteryInstaller.Instance.ForceExitMode();

        if (SolarPanelInstaller.Instance != null && SolarPanelInstaller.Instance.IsRoofMode)
            SolarPanelInstaller.Instance.ForceExitMode();

        if (FurnitureInstaller.Instance != null && FurnitureInstaller.Instance.IsFurnitureMode)
            FurnitureInstaller.Instance.ForceExitMode();

        // Tutup FurnitureConfigPanel jika masih terbuka
        var furnitureConfigPanel = FurnitureConfigPanel.Instance
            ?? FindFirstObjectByType<FurnitureConfigPanel>(FindObjectsInactive.Include);
        if (furnitureConfigPanel != null && furnitureConfigPanel.gameObject.activeSelf)
            furnitureConfigPanel.gameObject.SetActive(false);

        // Aktifkan free mode agar player bisa jalan bebas selama simulasi
        var roomFocus = FindFirstObjectByType<RoomFocusManager>();
        if (roomFocus != null)
            roomFocus.ForceEnterFreeMode();

        var orbitCam = Camera.main != null ? Camera.main.GetComponent<OrbitCamera>() : null;
        if (orbitCam != null)
        {
            orbitCam.enabled = true;
            orbitCam.ResetToOrigin();
        }

        ApplyButtonState();
        Debug.Log($"[SimulationManager] Simulasi dimulai pukul {startHour:00.00}, selesai pukul {_simulationEndHour:00.00} dengan kecepatan {_sunController?.SimulationHoursPerSecond} jam/detik");

        // ── SolarEdu Tracking ──
        if (SolarEduManager.Instance != null)
        {
            SolarEduManager.Instance.SendStatement(
                SolarEduVerb.Memulai,
                "solaredu://simulasi/panel-surya",
                $"Simulasi ({startHour:0}:00 - {_simulationEndHour:0}:00, {durationHours} jam)"
            );
        }
    }

    /// <summary>Hentikan simulasi dan reset day cycle.</summary>
    public void StopSimulation()
    {
        IsSimulationRunning = false;
        _inResultsState = true;

        // Nonaktifkan free mode dan aktifkan kembali OrbitCamera saat simulasi selesai
        var roomFocusOnStop = FindFirstObjectByType<RoomFocusManager>();
        if (roomFocusOnStop != null)
            roomFocusOnStop.ForceExitFreeMode();

        // Aktifkan kembali OrbitCamera jika sempat di-disable
        var orbitCam = Camera.main != null ? Camera.main.GetComponent<OrbitCamera>() : null;
        if (orbitCam != null)
            orbitCam.enabled = true;

        if (_sunController != null) _sunController.SetDayCycleActive(false);

        // Sembunyikan Sun Indicator saat masuk ke state hasil
        if (sunIndicatorPanel != null)
            sunIndicatorPanel.SetActive(false);

        // Slide keluar UI Right — panel hasil akan mengambil alih layar
        if (uiRightSlider != null)
            uiRightSlider.SlideOut();

        // simulationHideList dan objectivesPanel sengaja TIDAK dikembalikan di sini;
        // akan dikembalikan oleh ExitResultsState() setelah panel hasil ditutup.

        ApplyButtonState();
        OnSimulationEnded?.Invoke();

        // ── SolarEdu Tracking ──
        if (SolarEduManager.Instance != null)
        {
            SolarEduManager.Instance.SendStatement(
                SolarEduVerb.Menyelesaikan,
                "solaredu://simulasi/panel-surya",
                $"Simulasi Selesai ({SimulationDurationHours} jam)"
            );
        }

        Debug.Log("[SimulationManager] Simulasi dihentikan.");
    }

    /// <summary>
    /// Dipanggil oleh SimulationResultsUI saat panel hasil ditutup (tombol Lanjutkan).
    /// Mengembalikan UI ke kondisi sebelum simulasi.
    /// </summary>
    public void ExitResultsState()
    {
        _inResultsState = false;

        // Tampilkan kembali Objectives
        if (objectivesPanel != null)
            objectivesPanel.SetActive(true);

        // Kembalikan UI yang disembunyikan saat simulasi
        SetSimulationHideList(true);

        ApplyButtonState();
        Debug.Log("[SimulationManager] State hasil simulasi selesai, UI dikembalikan.");
    }

    /// <summary>
    /// Menyembunyikan atau menampilkan kembali semua UI yang terdaftar di simulationHideList.
    /// </summary>
    /// <param name="visible">true = tampilkan, false = sembunyikan.</param>
    private void SetSimulationHideList(bool visible)
    {
        if (simulationHideList == null) return;
        foreach (var obj in simulationHideList)
        {
            if (obj != null)
                obj.SetActive(visible);
        }
    }

    private void ApplyButtonState()
    {
        if (simulationButton != null)
            simulationButton.gameObject.SetActive(!IsSimulationRunning && !_inResultsState && !IsAnyPlacementActive());
    }

    /// <summary>True jika salah satu mode penempatan (furnitur/satelit/baterai) sedang aktif.</summary>
    static bool IsAnyPlacementActive()
    {
        if (FurniturePlacementController.Instance != null && FurniturePlacementController.Instance.IsPlacing)
            return true;
        if (SolarPanelInstaller.Instance != null && SolarPanelInstaller.Instance.IsRoofMode)
            return true;
        if (BatteryInstaller.Instance != null && BatteryInstaller.Instance.IsInstallMode)
            return true;
        return false;
    }

    /// <summary>Kembali ke scene menu utama (scene index 0).</summary>
    private void BackToMenu()
    {
        SceneManager.LoadScene(1);
    }

    void OnDestroy()
    {
        // Bersihkan memori singleton saat pindah scene
        if (Instance == this)
        {
            Instance = null;
        }
    }
}

