using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Pengelola graf rute kabel manual.
/// Bertanggung jawab mengumpulkan semua simpul CableWaypoint, memastikan koneksi dua arah (bidirectional),
/// memvalidasi integritas graf, serta mencari kandidat simpul terdekat yang memiliki Line-of-Sight dari port.
/// </summary>
[ExecuteAlways]
public class CableWaypointGraph : MonoBehaviour
{
    public static CableWaypointGraph Instance { get; private set; }

    [Header("Daftar Waypoint")]
    [Tooltip("Daftar simpul pada graf ini. Bisa diisi manual atau otomatis via tombol di Context Menu.")]
    [SerializeField] private List<CableWaypoint> waypoints = new();

    [Header("Pengaturan Pencarian")]
    [Tooltip("Jarak maksimum antar waypoint bertetangga. Mencegah kabel melompati node yang terlalu jauh.")]
    [SerializeField] private float maxWaypointLinkDistance = 1f;

    [Header("Radius Kandidat Port")]
    [Tooltip("Jarak maksimum port inverter/alat ke waypoint awal/akhir.")]
    [SerializeField] private float maxCandidateRadius = 500f;

    public float MaxCandidateRadius => maxCandidateRadius;
    public float MaxWaypointLinkDistance => maxWaypointLinkDistance;

    [Header("Visualisasi Editor")]
    [SerializeField] private bool showGraphGizmos = true;
    [SerializeField] private Color edgeColor = new Color(0.2f, 0.8f, 1f, 0.6f);

    public IReadOnlyList<CableWaypoint> Waypoints => waypoints;

    private void Awake()
    {
        if (Application.isPlaying)
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }
    }

    private void OnEnable()
    {
        if (Instance == null) Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Mencari simpul-simpul kandidat terdekat dari titik world (mis. port colokan)
    /// yang memiliki jalur bebas halangan (Line-of-Sight) tanpa menembus collider obstacle.
    /// Collider milik `source` (inverter/peralatan itu sendiri) diabaikan agar tidak self-hit.
    /// Hasil diurutkan dari jarak terdekat ke terjauh.
    /// </summary>
    public List<CandidateWaypoint> FindCandidates(Vector3 fromPos, LayerMask obstacleMask, float maxRadius = -1f, Transform source = null)
    {
        if (maxRadius <= 0f) maxRadius = maxCandidateRadius;

        var candidates = new List<CandidateWaypoint>();
        if (waypoints.Count == 0) CollectWaypoints();

        foreach (var wp in waypoints)
        {
            if (wp == null) continue;

            float dist = Vector3.Distance(fromPos, wp.Position);
            if (dist > maxRadius) continue;

            // Line-of-sight check: pastikan garis dari fromPos ke waypoint tidak menembus dinding
            if (obstacleMask.value != 0)
            {
                if (!CablePathfinder.LinecastFree(fromPos, wp.Position, obstacleMask, source))
                {
                    // Terhalang dinding/obstacle
                    continue;
                }
            }

            candidates.Add(new CandidateWaypoint { waypoint = wp, distance = dist });
        }

        candidates.Sort((a, b) => a.distance.CompareTo(b.distance));
        return candidates;
    }

    /// <summary>
    /// Struktur hasil kandidat simpul beserta jaraknya ke port.
    /// </summary>
    public struct CandidateWaypoint
    {
        public CableWaypoint waypoint;
        public float distance;
    }

    // ── Context Menu Helpers untuk Mempermudah Authoring ────────────────────

    /// <summary>
    /// Kumpulkan semua CableWaypoint yang ada di child GameObject ini.
    /// </summary>
    [ContextMenu("1. Kumpulkan Semua Waypoint Anak (Auto-Collect)")]
    public void CollectWaypoints()
    {
        waypoints.Clear();
        var found = GetComponentsInChildren<CableWaypoint>(true);
        foreach (var wp in found)
        {
            if (wp != null && !waypoints.Contains(wp))
                waypoints.Add(wp);
        }
        Debug.Log($"[CableWaypointGraph] {waypoints.Count} waypoint berhasil dikumpulkan.");
    }

    /// <summary>
    /// Memastikan setiap koneksi A -> B otomatis membuat B -> A agar kabel bisa mengalir dua arah.
    /// </summary>
    [ContextMenu("2. Jadikan Semua Koneksi Dua Arah (Make Bidirectional)")]
    public void MakeBidirectional()
    {
        int added = 0;
        foreach (var wp in waypoints)
        {
            if (wp == null) continue;
            wp.CleanupNeighbors();

            foreach (var neighbor in wp.Neighbors)
            {
                if (neighbor == null || neighbor == wp) continue;
                if (!neighbor.HasNeighbor(wp))
                {
                    neighbor.AddNeighbor(wp, false);
                    added++;
                }
            }
        }
        Debug.Log($"[CableWaypointGraph] Selesai. {added} koneksi balik dua arah ditambahkan.");
    }

    /// <summary>
    /// Validasi integritas graf: mencari simpul tanpa tetangga atau koneksi yang menembus collider.
    /// </summary>
    [ContextMenu("3. Validasi Integritas Graf (Validate)")]
    public void ValidateGraph()
    {
        CollectWaypoints();
        int isolatedCount = 0;

        foreach (var wp in waypoints)
        {
            if (wp == null) continue;
            wp.CleanupNeighbors();
            if (wp.Neighbors.Count == 0)
            {
                Debug.LogWarning($"[CableWaypointGraph] Peringatan: Waypoint '{wp.name}' tidak memiliki tetangga sama sekali (terisolasi)!", wp);
                isolatedCount++;
            }
        }

        if (isolatedCount == 0)
        {
            Debug.Log($"[CableWaypointGraph] Validasi sukses: Semua {waypoints.Count} waypoint terhubung dengan baik.");
        }
        else
        {
            Debug.LogWarning($"[CableWaypointGraph] Ditemukan {isolatedCount} waypoint terisolasi. Sambungkan tetangga sebelum digunakan.");
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// Helper praktis: Ekstrak rute waypoint dari kabel-kabel legacy di objek 'Cables' untuk dijadikan basis awal graf.
    /// Titik yang berdekatan (< 4 unit) otomatis dimerger, dan titik berurutan disambung jadi tetangga.
    /// </summary>
    [ContextMenu("4. Impor Rute Awal Dari Kabel Legacy (Cables)")]
    public void ImportFromLegacyCables()
    {
        var legacyRoot = GameObject.Find("Cables");
        if (legacyRoot == null)
        {
            Debug.LogWarning("[CableWaypointGraph] GameObject 'Cables' tidak ditemukan di scene.");
            return;
        }

        var c3dList = legacyRoot.GetComponentsInChildren<Cable3DTextured>(true);
        if (c3dList.Length == 0)
        {
            Debug.LogWarning("[CableWaypointGraph] Tidak ada Cable3DTextured di bawah 'Cables'.");
            return;
        }

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(transform.GetChild(i).gameObject);
        }
        waypoints.Clear();

        var createdWaypoints = new List<CableWaypoint>();

        CableWaypoint GetOrCreateWaypoint(Vector3 worldPos, string nameHint)
        {
            foreach (var wp in createdWaypoints)
            {
                if (Vector3.Distance(wp.Position, worldPos) < 4.0f)
                    return wp;
            }

            var go = new GameObject(nameHint);
            go.transform.SetParent(transform, false);
            go.transform.position = worldPos;
            var newWp = go.AddComponent<CableWaypoint>();
            createdWaypoints.Add(newWp);
            return newWp;
        }

        var field = typeof(Cable3DTextured).GetField("waypoints", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        int edgeCount = 0;
        foreach (var c3d in c3dList)
        {
            if (c3d == null) continue;
            var wps = field?.GetValue(c3d) as Transform[];
            if (wps == null || wps.Length < 2) continue;

            CableWaypoint prev = null;
            for (int i = 0; i < wps.Length; i++)
            {
                if (wps[i] == null) continue;
                var current = GetOrCreateWaypoint(wps[i].position, $"WP_{c3d.name}_{i:02}");
                if (prev != null && prev != current)
                {
                    prev.AddNeighbor(current, true);
                    edgeCount++;
                }
                prev = current;
            }
        }

        CollectWaypoints();
        MakeBidirectional();
        Debug.Log($"[CableWaypointGraph] Berhasil mengimpor {createdWaypoints.Count} simpul dan {edgeCount} koneksi dari kabel legacy!");
    }
#endif

    // ── Gizmos ──────────────────────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        if (!showGraphGizmos) return;

        Gizmos.color = edgeColor;
        foreach (var wp in waypoints)
        {
            if (wp == null) continue;
            int currentId = wp.GetInstanceID();

            foreach (var neighbor in wp.Neighbors)
            {
                if (neighbor == null) continue;
                if (currentId < neighbor.GetInstanceID())
                {
                    Gizmos.DrawLine(wp.Position, neighbor.Position);
                }
            }
        }
    }
}
