using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using SolarEdu;

/// <summary>
/// Manager singleton yang mengurus semua furnitur yang sudah di-install di rumah.
/// Attach ke GameObject kosong bernama "FurnitureManager" di scene.
/// Mendukung MULTI-INSTANCE: satu jenis furnitur boleh dipasang lebih dari sekali.
/// </summary>
public class FurnitureManager : MonoBehaviour
{
    public static FurnitureManager Instance { get; private set; }

    // ── Events ─────────────────────────────────────────────────────────────
    /// <summary>Dipanggil setiap total watt berubah. Parameter: total watt.</summary>
    public UnityEvent<float> OnPowerChanged;

    /// <summary>Dipanggil setiap jumlah furnitur (instance) berubah. Parameter: jumlah instance.</summary>
    public UnityEvent<int> OnFurnitureCountChanged;

    // ── Inspector ──────────────────────────────────────────────────────────
    [Header("Electricity Icon")]
    [Tooltip("Sprite ikon listrik yang ditampilkan di atas furnitur saat nyala.")]
    [SerializeField] private Sprite electricityIconSprite;

    // ── State ──────────────────────────────────────────────────────────────
    // Semua instance furnitur (satu jenis boleh lebih dari satu).
    private readonly List<GameObject> _instances = new();

    // Mapping instance → data asalnya.
    private readonly Dictionary<GameObject, FurnitureData> _instanceData = new();

    // Key: FurnitureData, Value: (jam mulai, jam selesai) dalam format 0-24.
    // Jadwal bersifat SHARED per jenis (berlaku untuk semua instance tipe sama).
    private readonly Dictionary<FurnitureData, (float startHour, float endHour)> _usageHours = new();

    private SunController _sunController;

    public float TotalWattConsumed { get; private set; } = 0f;

    /// <summary>Jumlah total instance furnitur yang terpasang di rumah.</summary>
    public int PlacedCount => _instances.Count;

    /// <summary>Daftar semua GameObject instance yang terpasang (urutan pemasangan).</summary>
    public IReadOnlyList<GameObject> PlacedInstances => _instances;

    // ── Lifecycle ──────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Update()
    {
        // Bersihkan instance yang sudah dihancurkan di luar sistem
        for (int i = _instances.Count - 1; i >= 0; i--)
        {
            if (_instances[i] == null)
            {
                _instances.RemoveAt(i);
            }
        }

        // Update state aktif tiap furnitur hanya saat simulasi berjalan
        if (SimulationManager.Instance == null || !SimulationManager.Instance.IsSimulationRunning)
            return;

        var sun = SunController.Instance;
        if (sun == null) return;

        float currentHour = sun.TimeOfDay;

        foreach (var instance in _instances)
        {
            if (instance == null || !_instanceData.TryGetValue(instance, out var data))
                continue;

            var (start, end) = GetUsageHours(data);
            bool isActive = currentHour >= start && currentHour < end;

            var fp = instance.GetComponent<FurniturePowered>();
            if (fp != null)
                fp.SetFurnitureActive(isActive);
        }
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>Cek apakah ada minimal satu instance furnitur jenis ini di rumah.</summary>
    public bool IsPlaced(FurnitureData data)
    {
        if (data == null) return false;
        foreach (var instance in _instances)
        {
            if (instance != null && _instanceData.TryGetValue(instance, out var d) && d == data)
                return true;
        }
        return false;
    }

    /// <summary>Jumlah instance dari satu jenis furnitur.</summary>
    public int CountOf(FurnitureData data)
    {
        if (data == null) return 0;
        int count = 0;
        foreach (var instance in _instances)
        {
            if (instance != null && _instanceData.TryGetValue(instance, out var d) && d == data)
                count++;
        }
        return count;
    }

    /// <summary>Ambil data asal sebuah instance furnitur.</summary>
    public bool TryGetDataOf(GameObject instance, out FurnitureData data)
    {
        return _instanceData.TryGetValue(instance, out data);
    }

    /// <summary>
    /// Daftar FurnitureData untuk SEMUA instance (boleh duplikat).
    /// Dipakai oleh list top-down & logger (kini menghitung jumlah instance).
    /// </summary>
    public IEnumerable<FurnitureData> GetPlacedFurnitureData()
    {
        foreach (var instance in _instances)
        {
            if (instance != null && _instanceData.TryGetValue(instance, out var data))
                yield return data;
        }
    }

    /// <summary>Daftar pasangan (data, instance) untuk semua furnitur terpasang.</summary>
    public List<(FurnitureData data, GameObject instance)> GetPlacedInstances()
    {
        var result = new List<(FurnitureData data, GameObject instance)>();
        foreach (var instance in _instances)
        {
            if (instance != null && _instanceData.TryGetValue(instance, out var data))
                result.Add((data, instance));
        }
        return result;
    }

    /// <summary>
    /// Tambahkan furnitur ke rumah (kompat dulu: spawn di fixedPosition asset).
    /// Tidak memblokir duplikat — setiap panggilan = 1 instance baru.
    /// </summary>
    public void AddFurniture(FurnitureData data)
    {
        if (data == null) return;
        AddFurniture(data, data.fixedPosition, Quaternion.Euler(data.fixedRotation));
    }

    /// <summary>
    /// Tambahkan furnitur di posisi & rotasi yang ditentukan (dipakai click-to-place).
    /// Setiap panggilan = 1 instance baru.
    /// </summary>
    public void AddFurniture(FurnitureData data, Vector3 position, Quaternion rotation)
    {
        if (data == null)
        {
            Debug.LogWarning("[FurnitureManager] FurnitureData null.");
            return;
        }

        if (data.prefab3D == null)
        {
            Debug.LogWarning($"[FurnitureManager] {data.furnitureName} tidak punya prefab3D!");
            return;
        }

        // Spawn instance baru
        GameObject instance = Instantiate(data.prefab3D, position, rotation);
        instance.transform.localScale = data.fixedScale;
        instance.name = $"[Placed] {data.furnitureName}";

        _instances.Add(instance);
        _instanceData[instance] = data;

        // Tambahkan komponen visual powered jika belum ada di prefab
        FurniturePowered fp = instance.GetComponent<FurniturePowered>();
        if (fp == null) fp = instance.AddComponent<FurniturePowered>();
        fp.Initialize(electricityIconSprite);

        // Hitung ulang total daya
        TotalWattConsumed += data.wattConsumption;
        OnPowerChanged?.Invoke(TotalWattConsumed);
        OnFurnitureCountChanged?.Invoke(_instances.Count);

        // Animasi kemunculan
        PlayPlaceAnimation(instance);

        Debug.Log($"[FurnitureManager] {data.furnitureName} ({data.wattConsumption}W) instance #{_instances.Count} ditambahkan. Total: {TotalWattConsumed}W");

        // ── SolarEdu Tracking ──
        if (SolarEduManager.Instance != null)
        {
            SolarEduManager.Instance.SendStatement(
                SolarEduVerb.Memasang,
                $"solaredu://furniture/{data.furnitureName.Replace(" ", "-").ToLower()}",
                $"{data.furnitureName} ({data.wattConsumption}W)"
            );
        }
    }

    /// <summary>Hapus SATU instance furnitur tertentu dari rumah.</summary>
    public void RemoveFurniture(GameObject instance)
    {
        if (instance == null || !_instanceData.TryGetValue(instance, out var data))
        {
            Debug.Log("[FurnitureManager] Pratinjau/instance tidak ditemukan.");
            return;
        }

        _instances.Remove(instance);
        _instanceData.Remove(instance);
        Destroy(instance);

        TotalWattConsumed -= data.wattConsumption;
        if (TotalWattConsumed < 0) TotalWattConsumed = 0;

        OnPowerChanged?.Invoke(TotalWattConsumed);
        OnFurnitureCountChanged?.Invoke(_instances.Count);

        Debug.Log($"[FurnitureManager] {data.furnitureName} dihapus. Sisa instance: {_instances.Count}, Total: {TotalWattConsumed}W");

        // ── SolarEdu Tracking ──
        if (SolarEduManager.Instance != null)
        {
            SolarEduManager.Instance.SendStatement(
                SolarEduVerb.Mengamati,
                $"solaredu://furniture/{data.furnitureName.Replace(" ", "-").ToLower()}/hapus",
                $"Hapus {data.furnitureName}"
            );
        }
    }

    /// <summary>Hapus SATU instance (yang pertama ditemukan) dari jenis furnitur tertentu.</summary>
    public void RemoveFurniture(FurnitureData data)
    {
        if (data == null) return;

        foreach (var instance in _instances)
        {
            if (instance != null && _instanceData.TryGetValue(instance, out var d) && d == data)
            {
                RemoveFurniture(instance);
                return;
            }
        }

        Debug.Log($"[FurnitureManager] {data.furnitureName} tidak ada di rumah.");
    }

    // ── Usage Hours API ────────────────────────────────────────────────────

    /// <summary>Atur jam aktif furnitur (format 0-24). Berlaku untuk semua instance jenis ini.</summary>
    public void SetUsageHours(FurnitureData data, float startHour, float endHour)
    {
        if (data == null) return;
        _usageHours[data] = (startHour, endHour);
        Debug.Log($"[FurnitureManager] {data.furnitureName} aktif pukul {startHour:0.##} - {endHour:0.##}");
    }

    /// <summary>Ambil jam aktif furnitur. Default 0-24 (sepanjang hari) jika belum dikonfigurasi.</summary>
    public (float startHour, float endHour) GetUsageHours(FurnitureData data)
    {
        if (data == null) return (0f, 24f);
        return _usageHours.TryGetValue(data, out var hours) ? hours : (0f, 24f);
    }

    /// <summary>
    /// Hitung total watt furnitur yang sedang aktif pada jam simulasi tertentu.
    /// Digunakan oleh BatteryManager untuk menghitung drain per frame.
    /// </summary>
    public float GetActiveWattConsumed(float currentSimHour)
    {
        float total = 0f;
        foreach (var instance in _instances)
        {
            if (instance == null || !_instanceData.TryGetValue(instance, out var data))
                continue;

            var (start, end) = GetUsageHours(data);
            if (currentSimHour >= start && currentSimHour < end)
                total += data.wattConsumption;
        }
        return total;
    }

    // ── Private Helpers ────────────────────────────────────────────────────
    /// <summary>Animasi scale bounce saat furnitur muncul</summary>
    void PlayPlaceAnimation(GameObject obj)
    {
        StartCoroutine(BounceScale(obj));
    }

    IEnumerator BounceScale(GameObject obj)
    {
        if (obj == null) yield break;

        Vector3 originalScale = obj.transform.localScale;
        obj.transform.localScale = Vector3.zero;

        float t = 0f;
        float duration = 0.35f;

        while (t < duration)
        {
            t += Time.deltaTime;
            float progress = t / duration;

            // Kurva overshoot (elastic kecil)
            float scale = Mathf.Sin(progress * Mathf.PI * 0.5f);
            if (progress > 0.7f)
                scale = 1f + Mathf.Sin((progress - 0.7f) / 0.3f * Mathf.PI) * 0.15f;

            obj.transform.localScale = originalScale * scale;
            yield return null;
        }

        obj.transform.localScale = originalScale;
    }
}