using UnityEngine;

/// <summary>Peran titik koneksi kabel.</summary>
public enum PortRole { Output, Input }

/// <summary>
/// Titik koneksi kabel yang eksplisit. Tempelkan komponen ini ke GameObject
/// (mis. inverter) untuk menentukan posisi port secara manual tanpa mengandalkan pivot.
/// Bila tidak ada, posisi dihitung otomatis dari bounds renderer (lihat Resolve).
/// Ringan: tidak punya Update dan tidak melakukan pencarian mahal.
/// </summary>
public class CablePort : MonoBehaviour
{
    [Tooltip("Output = titik keluar (inverter). Input = titik masuk (alat elektronik).")]
    [SerializeField] private PortRole role = PortRole.Input;

    [Tooltip("Offset titik port relatif terhadap transform ini (space lokal).")]
    [SerializeField] private Vector3 localOffset = Vector3.zero;

    public PortRole Role => role;

    /// <summary>Posisi port dalam world space.</summary>
    public Vector3 WorldPosition => transform.TransformPoint(localOffset);

    /// <summary>
    /// Ambil titik port pada objek.
    /// 1) Bila ada komponen <see cref="CablePort"/> dengan role cocok → pakai itu
    ///    (fallback ke CablePort apa pun yang tersedia).
    /// 2) Jika tidak → hitung dari bounds renderer:
    ///    - Output: sisi bawah objek, digeser ke arah target.
    ///    - Input : sisi objek yang menghadap sumber, pada tinggi "colokan" (~20% tinggi).
    /// </summary>
    public static Vector3 Resolve(GameObject obj, PortRole role, Vector3 towardWorld)
    {
        if (obj == null) return towardWorld;

        // 1. Port eksplisit
        CablePort best = null;
        foreach (var p in obj.GetComponentsInChildren<CablePort>(true))
        {
            if (p == null) continue;
            if (p.role == role) { best = p; break; }
            if (best == null) best = p; // fallback: port apa pun
        }
        if (best != null) return best.WorldPosition;

        // 2. Hitung otomatis dari bounds
        if (!TryGetBounds(obj, out Bounds b))
            return obj.transform.position;

        Vector3 center = b.center;
        Vector3 flat = new Vector3(towardWorld.x - center.x, 0f, towardWorld.z - center.z);

        float socketY = b.min.y + b.size.y * 0.2f;

        if (flat.sqrMagnitude < 1e-4f)
            return new Vector3(center.x, socketY, center.z);

        flat.Normalize();

        // Titik tembus sisi AABB searah 'flat' dari pusat
        float tx = Mathf.Abs(flat.x) > 1e-4f ? b.extents.x / Mathf.Abs(flat.x) : float.MaxValue;
        float tz = Mathf.Abs(flat.z) > 1e-4f ? b.extents.z / Mathf.Abs(flat.z) : float.MaxValue;
        float t = Mathf.Min(tx, tz);

        Vector3 side = center + flat * t;
        return new Vector3(side.x, socketY, side.z);
    }

    public static bool TryGetBounds(GameObject obj, out Bounds bounds)
    {
        bounds = default;
        if (obj == null) return false;

        Renderer[] rends = obj.GetComponentsInChildren<Renderer>(true);
        if (rends.Length == 0) return false;

        bounds = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++)
            bounds.Encapsulate(rends[i].bounds);
        return true;
    }
}
