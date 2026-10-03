using System.Collections;
using UnityEngine;
using TMPro;

/// <summary>
/// Mengelola mode instalasi furnitur: menampilkan/menyembunyikan panel kiri (UI Left)
/// dan mereset kamera ke posisi overview saat tombol ditekan.
/// </summary>
public class FurnitureInstaller : MonoBehaviour
{
    public static FurnitureInstaller Instance { get; private set; }

    [Header("Button")]
    [Tooltip("Label teks pada tombol Install Furnitur.")]
    [SerializeField] private TextMeshProUGUI buttonText;

    [Header("UI References")]
    [Tooltip("GameObject UI Left yang di-show/hide saat mode furnitur aktif.")]
    [SerializeField] private GameObject uiLeft;

    [Tooltip("Referensi UIPanelSlider pada UI Left untuk mereset posisi panel saat masuk mode.")]
    [SerializeField] private UIPanelSlider uiLeftSlider;

    [Header("Settings")]
    [Tooltip("Durasi transisi kamera saat reset ke origin.")]
    [SerializeField] private float cameraTransitionDuration = 1.2f;

    private const string LabelInstall = "Pasang Alat Elektronik";
    private const string LabelBack    = "Kembali";

    private OrbitCamera _orbitCamera;
    private bool _isFurnitureMode;


    /// <summary>True ketika mode instalasi furnitur sedang aktif.</summary>
    public bool IsFurnitureMode => _isFurnitureMode;

    private void Awake()
    {
        // NOTE: Singleton sengaja NONAKTIF. Di scene Simulasi ada 2 instance FurniturInstaller
        // (satu nyasar di tombol "InstallBattery"); memaksa singleton akan me-Destroy salah
        // satu dan bisa menghilangkan tombol "Pasang Alat Elektronik" (installer menempel
        // langsung di tombol tsb). Gunakan FindObjectsByType di luar untuk menemukan yang benar.
    }

    private void Start()
    {
        _orbitCamera = Camera.main != null ? Camera.main.GetComponent<OrbitCamera>() : null;

        // Sembunyikan UI Left di awal
        if (uiLeft != null)
            uiLeft.SetActive(false);

        UpdateButtonText();
    }

    /// <summary>Dipanggil oleh tombol Install Furnitur.</summary>
    public void ToggleMode()
    {
        if (_isFurnitureMode)
            ExitMode();
        else
            EnterMode();
    }

    private void EnterMode()
    {
        bool wasRoofMode    = SolarPanelInstaller.Instance != null && SolarPanelInstaller.Instance.IsRoofMode;
        bool wasBatteryMode = BatteryInstaller.Instance    != null && BatteryInstaller.Instance.IsInstallMode;

        // Force-exit mode lain yang sedang aktif
        if (wasRoofMode)    SolarPanelInstaller.Instance.ForceExitMode();
        if (wasBatteryMode) BatteryInstaller.Instance.ForceExitMode();

        _isFurnitureMode = true;

        // Animasikan slide-in panel dari kiri (mirip UI Left SP)
        if (uiLeftSlider != null)
            uiLeftSlider.SlideIn();
        else if (uiLeft != null)
            uiLeft.SetActive(true);

        // Jika orbit sedang disabled (artinya baru exit dari mode lain), jalankan transisi
        // kamera dari posisi saat ini (misalnya atap) ke posisi orbit home, lalu re-enable orbit.
        // Jika orbit sudah enabled (flow normal), tidak perlu gerakan kamera apa pun.
        if (_orbitCamera != null && !_orbitCamera.enabled)
            StartCoroutine(TransitionCameraToOrbitHome());

        UpdateButtonText();
        Debug.Log("[FurnitureInstaller] Mode instalasi furnitur AKTIF");
    }

    /// <summary>
    /// Smooth-transition kamera dari posisi saat ini ke posisi orbit home,
    /// lalu aktifkan kembali OrbitCamera.
    /// </summary>
    private IEnumerator TransitionCameraToOrbitHome()
    {
        Vector3    startPos = Camera.main.transform.position;
        Quaternion startRot = Camera.main.transform.rotation;
        Vector3    endPos   = _orbitCamera.GetOrbitPosition();
        Quaternion endRot   = _orbitCamera.GetOrbitRotation();

        float elapsed = 0f;
        while (elapsed < cameraTransitionDuration)
        {
            elapsed += Time.deltaTime;
            float t  = Mathf.Clamp01(elapsed / cameraTransitionDuration);
            t = t * t * (3f - 2f * t); // smooth ease

            Camera.main.transform.position = Vector3.Lerp(startPos, endPos, t);
            Camera.main.transform.rotation = Quaternion.Slerp(startRot, endRot, t);
            yield return null;
        }

        Camera.main.transform.position = endPos;
        Camera.main.transform.rotation = endRot;

        _orbitCamera.enabled = true;
    }

    /// <summary>Keluar dari mode furnitur.</summary>
    public void ExitMode()
    {
        _isFurnitureMode = false;

        // Batalkan proses penempatan / seleksi yang sedang berjalan
        if (FurniturePlacementController.Instance != null)
            FurniturePlacementController.Instance.CancelPlacement();

        // Animasikan slide-out lalu nonaktifkan panel
        if (uiLeftSlider != null)
            uiLeftSlider.SlideOut();
        else if (uiLeft != null)
            uiLeft.SetActive(false);

        UpdateButtonText();
        Debug.Log("[FurnitureInstaller] Mode instalasi furnitur SELESAI");
    }

    /// <summary>
    /// Keluar dari mode furnitur secara paksa saat installer lain mengambil alih.
    /// </summary>
    public void ForceExitMode()
    {
        _isFurnitureMode = false;

        // Batalkan proses penempatan / seleksi yang sedang berjalan
        if (FurniturePlacementController.Instance != null)
            FurniturePlacementController.Instance.CancelPlacement();

        // Langsung sembunyikan tanpa animasi saat force exit
        if (uiLeftSlider != null)
            uiLeftSlider.ResetToVisible();

        if (uiLeft != null)
            uiLeft.SetActive(false);

        UpdateButtonText();
        Debug.Log("[FurnitureInstaller] Mode furnitur di-force exit");
    }

    private void UpdateButtonText()
    {
        if (buttonText != null)
            buttonText.text = _isFurnitureMode ? LabelBack : LabelInstall;
    }
}
