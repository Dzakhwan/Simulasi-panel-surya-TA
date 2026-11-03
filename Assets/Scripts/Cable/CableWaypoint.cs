using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Kategori peran waypoint pada jalur kabel untuk memudahkan identifikasi dan pewarnaan gizmos.
/// </summary>
public enum WaypointCategory
{
    Wall,               // Menempel di dinding lurus
    Corner,             // Sudut belokan dinding
    Floor,              // Berada di lantai
    VerticalTransition, // Jalur vertikal naik/turun antar ketinggian
    InverterDrop,       // Titik awal turun dari inverter
    Socket              // Titik cabang dekat colokan/peralatan
}

/// <summary>
/// Simpul (node) pada graf rute kabel manual.
/// Setiap simpul memiliki posisi Transform dan daftar simpul tetangga yang terhubung secara eksplisit.
/// </summary>
[DisallowMultipleComponent]
[SelectionBase]
public class CableWaypoint : MonoBehaviour
{
    [Header("Identitas Waypoint")]
    [Tooltip("Kategori untuk klasifikasi dan pewarnaan visual di Scene View.")]
    [SerializeField] private WaypointCategory category = WaypointCategory.Wall;

    [Header("Koneksi Tetangga")]
    [Tooltip("Daftar waypoint yang terhubung langsung dengan titik ini. Jalur hanya boleh melintasi koneksi ini.")]
    [SerializeField] private List<CableWaypoint> neighbors = new();

    public WaypointCategory Category
    {
        get => category;
        set => category = value;
    }

    public Vector3 Position => transform.position;

    public IReadOnlyList<CableWaypoint> Neighbors => neighbors;

    /// <summary>
    /// Apakah simpul ini sudah terhubung dengan simpul lain.
    /// </summary>
    public bool HasNeighbor(CableWaypoint other)
    {
        return other != null && neighbors.Contains(other);
    }

    /// <summary>
    /// Tambahkan tetangga ke daftar koneksi.
    /// </summary>
    public void AddNeighbor(CableWaypoint other, bool bidirectional = true)
    {
        if (other == null || other == this) return;

        if (!neighbors.Contains(other))
            neighbors.Add(other);

        if (bidirectional && !other.neighbors.Contains(this))
            other.neighbors.Add(this);
    }

    /// <summary>
    /// Hapus tetangga dari daftar koneksi.
    /// </summary>
    public void RemoveNeighbor(CableWaypoint other, bool bidirectional = true)
    {
        if (other == null) return;

        neighbors.Remove(other);

        if (bidirectional)
            other.neighbors.Remove(this);
    }

    /// <summary>
    /// Bersihkan referensi null atau self-reference dari daftar tetangga.
    /// </summary>
    public void CleanupNeighbors()
    {
        neighbors.RemoveAll(n => n == null || n == this);
    }

    // ── Gizmos Editor Visualization ─────────────────────────────────────────

    private void OnDrawGizmos()
    {
        Gizmos.color = GetCategoryColor(category);
        Gizmos.DrawSphere(transform.position, 1.5f);

        // Gambar garis koneksi ke tetangga
        Gizmos.color = new Color(1f, 1f, 1f, 0.65f);
        int currentId = GetInstanceID();
        foreach (var neighbor in neighbors)
        {
            if (neighbor == null) continue;
            // Hindari menggambar garis dua kali (hanya gambar jika ID lebih kecil)
            if (currentId < neighbor.GetInstanceID())
            {
                Gizmos.DrawLine(transform.position, neighbor.transform.position);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        // Sorot simpul yang sedang dipilih
        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(transform.position, 2.5f);

        // Sorot garis ke tetangga dengan warna kuning tebal
        Gizmos.color = Color.yellow;
        foreach (var neighbor in neighbors)
        {
            if (neighbor == null) continue;
            Gizmos.DrawLine(transform.position, neighbor.transform.position);
            Gizmos.DrawWireSphere(neighbor.transform.position, 1.8f);
        }
    }

    private static Color GetCategoryColor(WaypointCategory cat)
    {
        return cat switch
        {
            WaypointCategory.InverterDrop       => new Color(0.2f, 0.9f, 1f, 0.95f),  // Cyan
            WaypointCategory.Corner             => new Color(1f, 0.85f, 0.1f, 0.95f), // Kuning emas
            WaypointCategory.Wall               => new Color(0.2f, 0.85f, 0.3f, 0.95f),// Hijau
            WaypointCategory.Floor              => new Color(0.3f, 0.5f, 1f, 0.95f),  // Biru
            WaypointCategory.VerticalTransition => new Color(0.9f, 0.3f, 0.9f, 0.95f),// Magenta
            WaypointCategory.Socket             => new Color(1f, 0.5f, 0.2f, 0.95f),  // Oranye
            _                                   => Color.white
        };
    }
}
