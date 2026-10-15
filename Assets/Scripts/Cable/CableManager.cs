using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mengelola kabel runtime yang menghubungkan inverter → furnitur (elektronik).
/// Kabel dibuat OTOMATIS (auto-route) — tidak perlu penempatan manual.
/// Mendukung BANYAK inverter: kabel dirutekan ke inverter terdekat dari furnitur.
/// </summary>
public class CableManager : MonoBehaviour
{
    public static CableManager Instance { get; private set; }

    [Header("Inverter")]
    [Tooltip("Kosongkan agar auto-detect semua GameObject bernama mengandung 'Inverter'.")
    ] [SerializeField] private List<Transform> inverterAnchors = new();

    [Header("Cable Visual")]
    [SerializeField] private float cableRadius = 3f;
    [SerializeField] private float cableSag = 40f;
    [SerializeField] private float cableFanOut = 25f;

    [Header("Routing")]
    [Tooltip("Wall = kabel menyusuri dinding (butuh collider layer 'Wall'); Arc = melengkung di udara (lama).")]
    [SerializeField] private RoutingMode routingMode = RoutingMode.Wall;
    [SerializeField] private float cellSize = 4f;
    [Tooltip("Tinggi kabel di atas lantai saat menempel dinding.")]
    [SerializeField] private float baseboardHeight = 8f;
    [Range(0f, 0.5f)]
    [SerializeField] private float edgeBias = 0.25f;
    [Tooltip("Toleransi ketinggian lantai agar kabel tidak pindah lantai di tengah rute.")]
    [SerializeField] private float floorBandThreshold = 40f;

    [Header("Layer")]
    [Tooltip("Kosongkan agar otomatis memakai layer 'Floor'.")]
    [SerializeField] private LayerMask floorMask = 0;
    [Tooltip("Kosongkan agar otomatis memakai layer 'Wall'.")]
    [SerializeField] private LayerMask wallMask = 0;

    private enum RoutingMode { Wall, Arc }

    [Header("Elektron")]
    [SerializeField] private int electronCount = 15;
    [SerializeField] private float electronSpeed = 20f;
    [SerializeField] private float electronRadius = 4f;

    [Header("Legacy")]
    [Tooltip("Nonaktifkan kabel manual lama (parent 'Cables') saat berjalan.")]
    [SerializeField] private bool deactivateLegacyCables = true;

    // instance furnitur → cable GameObject
    private readonly Dictionary<GameObject, GameObject> _cables = new();
    // inverter anchor → jumlah kabel terpasang (untuk pemerataan)
    private readonly Dictionary<Transform, int> _anchorLoad = new();

    private Transform _systemRoot;
    private Transform[] _resolvedAnchors = System.Array.Empty<Transform>();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        if (floorMask.value == 0)
            floorMask = LayerMask.GetMask("Floor");
        if (wallMask.value == 0)
            wallMask = LayerMask.GetMask("Wall");

        if (deactivateLegacyCables)
        {
            var legacy = GameObject.Find("Cables");
            if (legacy != null) legacy.SetActive(false);
        }

        _systemRoot = new GameObject("[CableSystem]").transform;
        _resolvedAnchors = ResolveInverterAnchors();
        if (_resolvedAnchors.Length == 0)
            Debug.LogWarning("[CableManager] Tidak ada inverter ditemukan. Buat GO bernama 'Inverter' atau isi daftar manual.");
    }

    void Update()
    {
        // Bersihkan kabel untuk instance yang lenyap di luar sistem
        if (_cables.Count == 0) return;
        var dead = new List<GameObject>();
        foreach (var key in _cables.Keys)
            if (key == null) dead.Add(key);
        foreach (var key in dead)
            Disconnect(key);
    }

    // ── Public API ──────────────────────────────────────────────────────────

    public bool IsConnected(GameObject instance)
    {
        return instance != null && _cables.ContainsKey(instance);
    }

    /// <summary>Sambung kalau belum, putus kalau sudah. Return true bila akhirnya tersambung.</summary>
    public bool ToggleConnect(GameObject instance, FurnitureData data)
    {
        if (IsConnected(instance))
        {
            Disconnect(instance);
            return false;
        }
        return Connect(instance, data);
    }

    public bool Connect(GameObject instance, FurnitureData data)
    {
        if (instance == null || data == null || data.prefab3D == null) return false;
        if (_cables.ContainsKey(instance)) return true;
        if (_systemRoot == null) _systemRoot = new GameObject("[CableSystem]").transform;

        if (_resolvedAnchors.Length == 0)
            _resolvedAnchors = ResolveInverterAnchors();
        if (_resolvedAnchors.Length == 0)
        {
            Debug.LogWarning("[CableManager] Sambungan dibatalkan — tidak ada inverter.");
            return false;
        }

        Transform inverter = PickInverter(instance.transform.position);
        GameObject cable = BuildCable(instance, data, inverter);

        _cables[instance] = cable;
        FurnitureManager.Instance?.SetConnected(instance, true);
        Debug.Log($"[CableManager] {data.furnitureName} tersambung ke {inverter.name}.");
        return true;
    }

    public bool Disconnect(GameObject instance)
    {
        if (instance == null) return false;

        if (_cables.TryGetValue(instance, out var cable))
        {
            if (cable != null) Destroy(cable);
            _cables.Remove(instance);
        }

        FurnitureManager.Instance?.SetConnected(instance, false);
        return true;
    }

    // ── Internal ────────────────────────────────────────────────────────────

    private Transform[] ResolveInverterAnchors()
    {
        var list = new List<Transform>();

        if (inverterAnchors != null && inverterAnchors.Count > 0)
        {
            foreach (var t in inverterAnchors)
                if (t != null) list.Add(t);
        }
        else
        {
            foreach (var tr in FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (tr != null && tr.name.Contains("Inverter"))
                    list.Add(GetOrCreateAnchor(tr));
            }
        }

        return list.ToArray();
    }

    /// <summary>Buat child 'CableAnchor' di sisi atas inverter sekali saja.</summary>
    private Transform GetOrCreateAnchor(Transform inverter)
    {
        Transform anchor = inverter.Find("CableAnchor");
        if (anchor != null) return anchor;

        var go = new GameObject("CableAnchor");
        go.transform.SetParent(inverter, false);
        go.transform.position = ComputeAnchorPosition(inverter);
        return go.transform;
    }

    private Vector3 ComputeAnchorPosition(Transform inverter)
    {
        Bounds b = GetBounds(inverter.gameObject);
        Vector3 c = b.size == Vector3.zero ? inverter.position : b.center;
        float top = b.size == Vector3.zero ? inverter.position.y : b.max.y;
        return new Vector3(c.x, top + 0.5f, c.z);
    }

    private Transform PickInverter(Vector3 fromPos)
    {
        float bestDist = float.MaxValue;
        Transform best = _resolvedAnchors[0];

        foreach (var anchor in _resolvedAnchors)
        {
            if (anchor == null) continue;
            float d = XZDistance(fromPos, anchor.position);
            _anchorLoad.TryGetValue(anchor, out int load);
            d += load * 40f; // tie-break: merata ke beberapa inverter
            if (d < bestDist) { bestDist = d; best = anchor; }
        }
        if (!_anchorLoad.ContainsKey(best)) _anchorLoad[best] = 0;
        _anchorLoad[best]++;
        return best;
    }

    private GameObject BuildCable(GameObject instance, FurnitureData data, Transform inverter)
    {
        GameObject cable = new GameObject($"Cable_{data.furnitureName}_{instance.GetInstanceID()}");
        cable.transform.SetParent(_systemRoot, false);

        Vector3 start = inverter.position;
        Vector3 end = ComputeFurnitureNode(instance);

        // Mode Wall: coba rute menyusuri dinding (A* di grid lantai)
        List<Vector3> wallPath = null;
        if (routingMode == RoutingMode.Wall)
        {
            wallPath = CableRouter.Route(start, end, floorMask, wallMask,
                cellSize, baseboardHeight, edgeBias, floorBandThreshold);
        }

        if (wallPath != null && wallPath.Count > 1)
        {
            for (int i = 0; i < wallPath.Count; i++)
                MakeWaypoint(cable.transform, $"WP_{i:00}", wallPath[i]);
            Debug.Log($"[CableManager] Wall route OK: {wallPath.Count} titik ({data.furnitureName}).");
        }
        else
        {
            // Fallback: arc lama (melengkung di udara)
            int idx = _cables.Count;
            Vector3 dir = (end - start);
            dir.y = 0f;
            Vector3 lateral = dir.sqrMagnitude > 0.001f
                ? Vector3.Cross(Vector3.up, dir.normalized)
                : Vector3.right;
            lateral = lateral.normalized;
            float sign = (idx % 2 == 0) ? 1f : -1f;

            Vector3 mid = (start + end) * 0.5f;
            mid.y = Mathf.Max(start.y, end.y) + cableSag;
            mid += lateral * sign * (cableFanOut * (1f + idx / 2f));

            MakeWaypoint(cable.transform, "WP_inv", start);
            MakeWaypoint(cable.transform, "WP_mid", mid);
            MakeWaypoint(cable.transform, "WP_fur", end);

            if (routingMode == RoutingMode.Wall)
                Debug.Log("[CableManager] Rute dinding tidak ditemukan — fallback ke arc.");
        }

        var c3d = cable.AddComponent<Cable3DTextured>();
        c3d.ConfigureCable(null, data.prefab3D,
            electronCount, electronSpeed, electronRadius, cableRadius, null,
            smooth: false);

        return cable;
    }

    private Transform MakeWaypoint(Transform parent, string name, Vector3 pos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        return go.transform;
    }

    private Vector3 ComputeFurnitureNode(GameObject instance)
    {
        Renderer[] rends = instance.GetComponentsInChildren<Renderer>(true);
        if (rends.Length == 0)
            return instance.transform.position + Vector3.up * 2f;

        Bounds b = rends[0].bounds;
        foreach (Renderer r in rends) b.Encapsulate(r.bounds);
        return new Vector3(b.center.x, b.max.y + 2f, b.center.z) + Vector3.forward * 1f;
    }

    private static Bounds GetBounds(GameObject root)
    {
        Renderer[] rends = root.GetComponentsInChildren<Renderer>(true);
        if (rends.Length == 0) return new Bounds(root.transform.position, Vector3.zero);
        Bounds b = rends[0].bounds;
        foreach (Renderer r in rends) b.Encapsulate(r.bounds);
        return b;
    }

    private static float XZDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    // ── Bootstrap ────────────────────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (FindFirstObjectByType<CableManager>() == null)
        {
            var go = new GameObject("[CableManager]");
            go.AddComponent<CableManager>();
        }
    }
}