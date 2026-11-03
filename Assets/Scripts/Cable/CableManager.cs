using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mengelola kabel runtime yang menghubungkan inverter → furnitur (elektronik).
/// Kabel dibuat OTOMATIS (auto-route menyusuri lantai) — tidak perlu penempatan manual.
/// Mendukung BANYAK inverter: kabel dirutekan ke inverter terdekat dari furnitur
/// (atau ke inverter eksplisit yang dipilih pengguna).
/// </summary>
public class CableManager : MonoBehaviour
{
    public static CableManager Instance { get; private set; }

    [Header("Inverter")]
    [Tooltip("Kosongkan agar auto-detect semua GameObject bernama mengandung 'Inverter'.")]
    [SerializeField] private List<Transform> inverterAnchors = new();

    [Header("Cable Visual")]
    [SerializeField] private float cableRadius = 3f;
    [SerializeField] private float cableSag = 40f;
    [SerializeField] private float cableFanOut = 25f;

    [Header("Routing")]
    [Tooltip("Dijkstra = rute mengikuti graf waypoint manual (direkomendasikan). FloorAStar = voxel lantai A*. Arc = melengkung udara.")]
    [SerializeField] private RoutingMode routingMode = RoutingMode.Dijkstra;

    [Header("Waypoint Graph (Dijkstra)")]
    [Tooltip("Graf waypoint manual. Jika kosong, akan otomatis dicari di scene.")]
    [SerializeField] private CableWaypointGraph waypointGraph;
    [Tooltip("Radius maksimal pencarian simpul graf terdekat dari port colokan/inverter.")]
    [SerializeField] private float maxWaypointSnapDistance = 500f;
    [Tooltip("Penalti biaya sambungan mentah port→waypoint. Semakin besar, semakin Dijkstra dipaksa mengikuti chain waypoint manual Invoice (nilai >= 1, default 3).")]
    [Min(1f)]
    [SerializeField] private float endpointConnectorCostScale = 3f;
    [Tooltip("Aktifkan Catmull-Rom spline halus. Nonaktifkan jika ingin segmen lurus/siku-siku tajam.")]
    [SerializeField] private bool smoothCableCurves = true;

    [Header("Routing Grid A* (Fallback)")]
    [SerializeField] private float cellSize = 4f;
    [Tooltip("Ketinggian kabel di atas lantai (clearance).")]
    [SerializeField] private float floorClearance = 2f;
    [Range(0f, 0.5f)]
    [Tooltip("Semakin besar, semakin kabel cenderung menyusuri tepi/dinding.")]
    [SerializeField] private float edgeBias = 0.35f;
    [Tooltip("Toleransi ketinggian lantai agar kabel tidak pindah lantai di tengah rute.")]
    [SerializeField] private float floorBandThreshold = 40f;
    [Tooltip("Batas maksimum pencarian sel lantai terdekat dari port (dalam sel).")]
    [SerializeField] private int maxSnapCells = 200;

    [Header("Layer")]
    [Tooltip("Kosongkan agar otomatis memakai layer 'Floor'.")]
    [SerializeField] private LayerMask floorMask = 0;
    [Tooltip("Penghalang rute (dinding). Kosongkan agar otomatis memakai SEMUA layer kecuali 'Floor'.")]
    [SerializeField] private LayerMask obstacleMask = 0;

    public enum RoutingMode { Dijkstra, FloorAStar, Arc }

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

    /// <summary>Semua anchor inverter yang terdeteksi (transform child 'CableAnchor').</summary>
    public IReadOnlyList<Transform> Inverters
    {
        get
        {
            if (_resolvedAnchors.Length == 0) _resolvedAnchors = ResolveInverterAnchors();
            return _resolvedAnchors;
        }
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        if (floorMask.value == 0)
            floorMask = LayerMask.GetMask("Floor");
        if (obstacleMask.value == 0)
            obstacleMask = BuildAutoObstacleMask();

        if (deactivateLegacyCables)
        {
            var legacy = GameObject.Find("Cables");
            if (legacy != null) legacy.SetActive(false);
        }

        if (waypointGraph == null)
            waypointGraph = FindFirstObjectByType<CableWaypointGraph>();

        _systemRoot = new GameObject("[CableSystem]").transform;
        _resolvedAnchors = ResolveInverterAnchors();
        if (_resolvedAnchors.Length == 0)
            Debug.LogWarning("[CableManager] Tidak ada inverter ditemukan. Buat GO bernama 'Inverter' atau isi daftar manual.");

        CleanupOrphanShells();
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

    /// <summary>Sambung ke inverter terdekat (auto).</summary>
    public bool Connect(GameObject instance, FurnitureData data)
    {
        return ConnectTo(instance, data, null);
    }

    /// <summary>Sambung instance ke inverter tertentu (eksplisit). inverter null = auto terdekat.</summary>
    public bool ConnectTo(GameObject instance, FurnitureData data, Transform inverter)
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

        if (inverter == null || System.Array.IndexOf(_resolvedAnchors, inverter) < 0)
        {
            inverter = PickInverter(instance.transform.position);
        }
        else
        {
            _anchorLoad.TryGetValue(inverter, out int l);
            _anchorLoad[inverter] = l + 1;
        }

        CleanupOrphanShells();

        GameObject cable = BuildCable(instance, data, inverter);
        if (cable == null)
        {
            Debug.LogWarning($"[CableManager] Tidak dapat menyambungkan {data.furnitureName}: rute kabel valid tidak ditemukan.");
            return false;
        }

        _cables[instance] = cable;
        FurnitureManager.Instance?.SetConnected(instance, true);

        string invName = inverter.parent != null ? inverter.parent.name : inverter.name;
        Debug.Log($"[CableManager] {data.furnitureName} tersambung ke {invName}.");
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

    private static int BuildAutoObstacleMask()
    {
        int mask = ~0;
        int floor = LayerMask.NameToLayer("Floor");
        int ignore = LayerMask.NameToLayer("Ignore Raycast");
        if (floor >= 0)  mask &= ~(1 << floor);
        if (ignore >= 0) mask &= ~(1 << ignore);
        return mask;
    }

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

        // Titik koneksi eksplisit (port)
        Transform invRoot = inverter.parent != null ? inverter.parent : inverter;
        Vector3 applianceCenter = GetBounds(instance).center;
        Vector3 startPort = CablePort.Resolve(invRoot.gameObject, PortRole.Output, applianceCenter);
        Vector3 endPort = CablePort.Resolve(instance, PortRole.Input, startPort);

        // Resolusi rute kabel sesuai routingMode
        List<Vector3> path = null;
        if (routingMode == RoutingMode.Dijkstra)
        {
            if (waypointGraph == null)
                waypointGraph = FindFirstObjectByType<CableWaypointGraph>();

            if (waypointGraph != null)
            {
                path = CablePathfinder.FindRoute(startPort, endPort, waypointGraph, obstacleMask,
                    maxWaypointSnapDistance, invRoot, instance.transform, endpointConnectorCostScale);
            }
            else
            {
                Debug.LogWarning("[CableManager] RoutingMode = Dijkstra namun tidak ada CableWaypointGraph di scene! Fallback ke FloorAStar.");
                path = CableRouter.Route(startPort, endPort, floorMask, obstacleMask,
                    cellSize, floorClearance, edgeBias, floorBandThreshold, maxSnapCells);
            }
        }
        else if (routingMode == RoutingMode.FloorAStar)
        {
            path = CableRouter.Route(startPort, endPort, floorMask, obstacleMask,
                cellSize, floorClearance, edgeBias, floorBandThreshold, maxSnapCells);
        }

        // Sambungan ujung dibentuk pola L (turun/bangkit vertikal) agar kabel mengikuti
        // dinding seperti kabel legacy — tidak melayang diagonal menembus udara.
        if (path != null && path.Count > 1)
            path = RefineEndpointConnectors(path, startPort, endPort, invRoot, instance.transform, obstacleMask);

        if (path != null && path.Count > 1)
        {
            for (int i = 0; i < path.Count; i++)
                MakeWaypoint(cable.transform, $"WP_{i:00}", path[i]);
            Debug.Log($"[CableManager] {routingMode} route OK: {path.Count} titik ({data.furnitureName}).");
        }
        else
        {
            // Jika Dijkstra gagal, jangan buat kabel sembarangan menembus dinding (fail safe)!
            if (routingMode == RoutingMode.Dijkstra)
            {
                Debug.LogError($"[CableManager] Dijkstra gagal: Tidak ada jalur waypoint yang menghubungkan inverter ke {data.furnitureName}. Kabel dibatalkan.");
                Destroy(cable);
                return null;
            }

            // Fallback: arc melengkung di udara
            int idx = _cables.Count;
            Vector3 dir = (endPort - startPort);
            dir.y = 0f;
            Vector3 lateral = dir.sqrMagnitude > 0.001f
                ? Vector3.Cross(Vector3.up, dir.normalized)
                : Vector3.right;
            lateral = lateral.normalized;
            float sign = (idx % 2 == 0) ? 1f : -1f;

            Vector3 mid = (startPort + endPort) * 0.5f;
            mid.y = Mathf.Max(startPort.y, endPort.y) + cableSag;
            mid += lateral * sign * (cableFanOut * (1f + idx / 2f));

            MakeWaypoint(cable.transform, "WP_inv", startPort);
            MakeWaypoint(cable.transform, "WP_mid", mid);
            MakeWaypoint(cable.transform, "WP_fur", endPort);

            Debug.Log("[CableManager] Menggunakan fallback Arc.");
        }

        var c3d = cable.AddComponent<Cable3DTextured>();
        c3d.ConfigureCable(null, data.prefab3D,
            electronCount, electronSpeed, electronRadius, cableRadius, null,
            smooth: smoothCableCurves);

        return cable;
    }

    /// <summary>
    /// Mengganti sambungan lurus port→waypoint dengan pola L (mengikuti gaya kabel legacy):
    /// sisi inverter  : startPort → (vertikal ke ketinggian WP) → WP
    /// sisi peralatan : WP → (horizontal di ketinggian WP di atas port) → (vertikal turun ke port)
    /// Tiap sub-segmen wajib lolos LOS; bila terblokir, segmen lurus semula dipertahankan.
    /// </summary>
    private static List<Vector3> RefineEndpointConnectors(
        List<Vector3> route, Vector3 startPort, Vector3 endPort,
        Transform startSource, Transform endSource, LayerMask mask)
    {
        var result = new List<Vector3>(route);

        // Sisi inverter
        if (result.Count >= 3)
        {
            Vector3 wp = result[1];
            if (Mathf.Abs(startPort.y - wp.y) > 0.5f)
            {
                Vector3 elbow = new Vector3(startPort.x, wp.y, startPort.z);
                if ((elbow - wp).sqrMagnitude > 0.25f && (elbow - startPort).sqrMagnitude > 0.25f &&
                    CablePathfinder.LinecastFree(startPort, elbow, mask, startSource) &&
                    CablePathfinder.LinecastFree(elbow, wp, mask, startSource))
                {
                    result.Insert(1, elbow);
                }
            }
        }

        // Sisi peralatan
        if (result.Count >= 3)
        {
            int last = result.Count - 1;
            Vector3 wpLast = result[last - 1];
            if (Mathf.Abs(endPort.y - wpLast.y) > 0.5f)
            {
                Vector3 elbowEnd = new Vector3(endPort.x, wpLast.y, endPort.z);
                if ((elbowEnd - wpLast).sqrMagnitude > 0.25f && (elbowEnd - endPort).sqrMagnitude > 0.25f &&
                    CablePathfinder.LinecastFree(wpLast, elbowEnd, mask, endSource) &&
                    CablePathfinder.LinecastFree(elbowEnd, endPort, mask, endSource))
                {
                    result.Insert(last, elbowEnd);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Hapus GO kabel yatim dari percobaan koneksi yang gagal (shell tanpa Cable3DTextured).
    /// </summary>
    private void CleanupOrphanShells()
    {
        if (_systemRoot == null) return;
        for (int i = _systemRoot.childCount - 1; i >= 0; i--)
        {
            var child = _systemRoot.GetChild(i);
            if (child.GetComponent<Cable3DTextured>() == null)
            {
                Debug.LogWarning($"[CableManager] Membersihkan shell kabel yatim '{child.name}'.");
                Destroy(child.gameObject);
            }
        }
    }

    private Transform MakeWaypoint(Transform parent, string name, Vector3 pos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        return go.transform;
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
