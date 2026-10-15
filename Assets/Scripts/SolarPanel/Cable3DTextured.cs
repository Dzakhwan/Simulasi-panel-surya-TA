using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

/// <summary>
/// Versi Cable3D yang menggunakan Texture2D prosedural untuk simbol elektron (−),
/// tanpa TMP dan tanpa child object tambahan per elektron.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class Cable3DTextured : MonoBehaviour
{
    [Header("Waypoints Kabel")]
    [Tooltip("Isi otomatis dari child Transform jika dikosongkan")]
    [SerializeField] Transform[] waypoints;

    [Header("Spesifikasi Kabel")]
    [SerializeField] float cableRadius = 0.05f;
    [SerializeField] int radialSegments = 8;
    [SerializeField] bool smoothBend = true;
    [SerializeField] int smoothStepsPerSegment = 8;
    [SerializeField] Material cableMaterial;

    [Header("Referensi Daya")]
    [Tooltip("Gunakan BatteryManager (total semua panel). Jika false, pakai solarPanel langsung.")]
    [SerializeField] bool useBatteryManager = true;
    [SerializeField] SolarPanel solarPanel;

    [Tooltip("Prefab 3D peralatan yang terhubung ke kabel ini. " +
             "Elektron hanya mengalir jika prefab ini sudah di-place di scene. " +
             "Kosongkan untuk mengikuti daya solar secara global.")]
    [SerializeField] GameObject linkedFurniturePrefab;

    [Header("Elektron")]
    [Tooltip("Jumlah elektron maksimum dalam pool (bisa aktif bersamaan).")]
    [SerializeField] int electronCount = 15;
    [SerializeField] float electronSpeed = 2f;
    [SerializeField] float electronRadius = 0.06f;
    [SerializeField] Color electronColor = new Color(1f, 0.9f, 0.2f);

    [Header("Spawn Rate Elektron")]
    [Tooltip("Daya referensi (Watt) di mana spawn interval paling cepat tercapai.")]
    [SerializeField] float maxReferencePower = 500f;
    [Tooltip("Interval spawn tercepat (detik) — pada daya ≥ maxReferencePower.")]
    [SerializeField] float minSpawnInterval = 0.1f;
    [Tooltip("Interval spawn terlambat (detik) — pada daya mendekati nol.")]
    [SerializeField] float maxSpawnInterval = 1.5f;

    [Header("Simbol Elektron (Texture)")]
    [SerializeField] bool showElectronSymbol = true;
    [Tooltip("Resolusi texture yang di-generate (power of 2 disarankan)")]
    [SerializeField] int textureResolution = 128;
    [Tooltip("Lebar bar minus relatif terhadap texture (0–1)")]
    [SerializeField, Range(0.2f, 0.85f)] float symbolWidthRatio = 0.55f;
    [Tooltip("Tinggi bar minus relatif terhadap texture (0–1)")]
    [SerializeField, Range(0.05f, 0.35f)] float symbolHeightRatio = 0.14f;
    [SerializeField] Color symbolColor = Color.black;

    [Header("Kalibrasi Billboard")]
    [Tooltip("Offset horizontal pusat simbol dalam UV (0–1). Default 0.75 = kompensasi seam UV sphere Unity.")]
    [SerializeField, Range(0f, 1f)] float symbolUCenter = 0.75f;
    [Tooltip("Offset vertikal pusat simbol dalam UV (0–1). Default 0.5 = tengah.")]
    [SerializeField, Range(0f, 1f)] float symbolVCenter = 0.5f;

    // --- Runtime data ---

    List<Vector3> splinePoints = new List<Vector3>();
    List<float> cumulativeDistances = new List<float>();
    float totalLength;

    GameObject[] electrons;
    float[] electronProgress;
    float _spawnTimer = 0f;

    SunController _sunController;

    // =========================================================
    // Lifecycle
    // =========================================================

    /// <summary>Konfigurasi kabel dari luar (dipakai CableManager untuk kabel runtime).</summary>
    public void ConfigureCable(SolarPanel panel, GameObject linkedPrefab,
        int count, float speed, float eRadius, float cRadius, Material mat,
        bool smooth = true)
    {
        useBatteryManager = true;
        solarPanel         = panel;
        linkedFurniturePrefab = linkedPrefab;
        electronCount      = count;
        electronSpeed      = speed;
        electronRadius     = eRadius;
        cableRadius        = cRadius;
        cableMaterial      = mat;
        smoothBend         = smooth;
    }

    void Start()
    {
        // Auto-collect child Transforms sebagai waypoints jika array kosong
        if (waypoints == null || waypoints.Length < 2)
        {
            var childTransforms = new List<Transform>();
            foreach (Transform child in transform)
                childTransforms.Add(child);
            waypoints = childTransforms.ToArray();
        }

        if (waypoints.Length < 2)
        {
            Debug.LogWarning("[Cable3DTextured] Butuh minimal 2 waypoints (child Transform).");
            return;
        }

        MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
        meshRenderer.material = cableMaterial != null
            ? cableMaterial
            : CreateFallbackMaterial(Color.gray);

        _sunController = FindFirstObjectByType<SunController>();

        BuildSpline();
        BuildMesh();
        CreateElectrons();
    }

    void Update()
    {
        // Elektron tidak muncul sebelum simulasi dijalankan.
        bool simulationRunning = SimulationManager.Instance != null
                                 && SimulationManager.Instance.IsSimulationRunning;

        if (!simulationRunning)
        {
            UpdateElectrons(false, 0f);
            return;
        }

        float currentPower = GetCurrentSolarPower();
        UpdateElectrons(currentPower > 0f, currentPower);
    }

    /// <summary>
    /// Kembalikan "daya aktif" (Watt) yang menentukan apakah elektron mengalir.
    /// </summary>
    float GetCurrentSolarPower()
    {
        if (linkedFurniturePrefab != null)
        {
            var fm = FurnitureManager.Instance;
            if (fm == null) return 0f;

            // 1. Cari FurnitureData yang prefab3D-nya cocok
            FurnitureData linkedData = null;
            foreach (var data in fm.GetPlacedFurnitureData())
            {
                if (data.prefab3D == linkedFurniturePrefab)
                {
                    linkedData = data;
                    break;
                }
            }

            if (linkedData == null) return 0f; // Alat belum dipasang di scene

            // 2. CEK PEMADAMAN LISTRIK
            // Jika baterai kosong / tidak ada listrik, alat otomatis mati!
            if (BatteryManager.Instance != null && !BatteryManager.Instance.HasPower)
            {
                return 0f;
            }

            // 3. CEK JADWAL JAM PEMAKAIAN
            var sim = SimulationManager.Instance;
            if (sim != null && sim.IsSimulationRunning && _sunController != null)
            {
                var (startHour, endHour) = fm.GetUsageHours(linkedData);
                float currentHour = _sunController.TimeOfDay;

                bool isTimeActive = false;
                if (startHour <= endHour)
                {
                    // Jadwal normal (misal 06:00 - 18:00)
                    isTimeActive = (currentHour >= startHour && currentHour < endHour);
                }
                else
                {
                    // Jadwal begadang/overnight (misal 18:00 - 06:00)
                    isTimeActive = (currentHour >= startHour || currentHour < endHour);
                }

                if (!isTimeActive) return 0f; // Di luar jam nyala, matikan elektron
            }

            // Jika alat dipasang, ada listrik, dan sedang jam nyala -> alirkan elektron!
            return linkedData.wattConsumption > 0f ? linkedData.wattConsumption : 1f;
        }

        // Mode global: ikuti daya solar (misal: kabel dari panel surya ke baterai)
        if (useBatteryManager && BatteryManager.Instance != null)
            return BatteryManager.Instance.CurrentSolarPower;

        if (solarPanel != null)
            return solarPanel.CurrentPower;

        return 0f;
    }

    // =========================================================
    // Material Fallback
    // =========================================================

    /// <summary>
    /// Membuat material yang kompatibel dengan render pipeline aktif.
    /// </summary>
    Material CreateFallbackMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Unlit/Color")
                     ?? Shader.Find("Standard");

        var mat = new Material(shader != null
            ? shader
            : GraphicsSettings.defaultRenderPipeline != null
                ? GraphicsSettings.defaultRenderPipeline.defaultMaterial.shader
                : Shader.Find("Standard"));

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color"))     mat.SetColor("_Color", color);
        return mat;
    }

    // =========================================================
    // Spline (Catmull-Rom)
    // =========================================================

    Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * (
            2f * p1 +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }

    void BuildSpline()
    {
        splinePoints.Clear();
        cumulativeDistances.Clear();

        int n = waypoints.Length;

        if (smoothBend)
        {
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 p0 = waypoints[Mathf.Max(0, i - 1)].position;
                Vector3 p1 = waypoints[i].position;
                Vector3 p2 = waypoints[i + 1].position;
                Vector3 p3 = waypoints[Mathf.Min(n - 1, i + 2)].position;

                for (int j = 0; j < smoothStepsPerSegment; j++)
                {
                    float t = (float)j / smoothStepsPerSegment;
                    splinePoints.Add(CatmullRom(p0, p1, p2, p3, t));
                }
            }
        }
        else
        {
            for (int i = 0; i < n - 1; i++)
                splinePoints.Add(waypoints[i].position);
        }

        splinePoints.Add(waypoints[n - 1].position);

        float dist = 0f;
        cumulativeDistances.Add(0f);
        for (int i = 1; i < splinePoints.Count; i++)
        {
            dist += Vector3.Distance(splinePoints[i], splinePoints[i - 1]);
            cumulativeDistances.Add(dist);
        }
        totalLength = dist;
    }

    // =========================================================
    // Mesh Tube
    // =========================================================

    void BuildMesh()
    {
        if (smoothBend)
            BuildMeshSmooth();
        else
            BuildMeshSharp();
    }

    void BuildMeshSmooth()
    {
        int pointCount = splinePoints.Count;
        int vertsPerRing = radialSegments + 1;

        var vertices  = new List<Vector3>();
        var triangles = new List<int>();
        var uvs       = new List<Vector2>();

        var rights = new Vector3[pointCount];
        var ups    = new Vector3[pointCount];

        for (int i = 0; i < pointCount; i++)
        {
            Vector3 forward = i < pointCount - 1
                ? (splinePoints[i + 1] - splinePoints[i]).normalized
                : (splinePoints[i] - splinePoints[i - 1]).normalized;

            Vector3 refUp = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.99f
                ? Vector3.right : Vector3.up;
            rights[i] = Vector3.Cross(forward, refUp).normalized;
            ups[i]    = Vector3.Cross(rights[i], forward).normalized;
        }

        for (int i = 0; i < pointCount; i++)
        {
            float uvY = cumulativeDistances[i] / totalLength;
            for (int j = 0; j <= radialSegments; j++)
            {
                float angle  = (float)j / radialSegments * Mathf.PI * 2f;
                Vector3 off  = rights[i] * Mathf.Cos(angle) + ups[i] * Mathf.Sin(angle);
                vertices.Add(transform.InverseTransformPoint(splinePoints[i] + off * cableRadius));
                uvs.Add(new Vector2((float)j / radialSegments, uvY));
            }
        }

        for (int i = 0; i < pointCount - 1; i++)
        {
            for (int j = 0; j < radialSegments; j++)
            {
                int a = i * vertsPerRing + j;
                int b = a + 1;
                int c = (i + 1) * vertsPerRing + j;
                int d = c + 1;
                triangles.Add(a); triangles.Add(c); triangles.Add(b);
                triangles.Add(b); triangles.Add(c); triangles.Add(d);
            }
        }

        ApplyMesh(vertices, triangles, uvs);
    }

    void BuildMeshSharp()
    {
        int segCount     = splinePoints.Count - 1;
        int vertsPerRing = radialSegments + 1;

        var vertices  = new List<Vector3>();
        var triangles = new List<int>();
        var uvs       = new List<Vector2>();

        for (int seg = 0; seg < segCount; seg++)
        {
            Vector3 p0      = splinePoints[seg];
            Vector3 p1      = splinePoints[seg + 1];
            Vector3 forward = (p1 - p0).normalized;

            Vector3 refUp = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.99f
                ? Vector3.right : Vector3.up;
            Vector3 right = Vector3.Cross(forward, refUp).normalized;
            Vector3 up    = Vector3.Cross(right, forward).normalized;

            float uvY0 = cumulativeDistances[seg] / totalLength;
            float uvY1 = cumulativeDistances[seg + 1] / totalLength;

            for (int j = 0; j <= radialSegments; j++)
            {
                float angle = (float)j / radialSegments * Mathf.PI * 2f;
                Vector3 off = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                vertices.Add(transform.InverseTransformPoint(p0 + off * cableRadius));
                uvs.Add(new Vector2((float)j / radialSegments, uvY0));
            }
            for (int j = 0; j <= radialSegments; j++)
            {
                float angle = (float)j / radialSegments * Mathf.PI * 2f;
                Vector3 off = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                vertices.Add(transform.InverseTransformPoint(p1 + off * cableRadius));
                uvs.Add(new Vector2((float)j / radialSegments, uvY1));
            }

            int baseIndex = seg * 2 * vertsPerRing;
            for (int j = 0; j < radialSegments; j++)
            {
                int a = baseIndex + j;
                int b = a + 1;
                int c = baseIndex + vertsPerRing + j;
                int d = c + 1;
                triangles.Add(a); triangles.Add(c); triangles.Add(b);
                triangles.Add(b); triangles.Add(c); triangles.Add(d);
            }
        }

        ApplyMesh(vertices, triangles, uvs);
    }

    void ApplyMesh(List<Vector3> vertices, List<int> triangles, List<Vector2> uvs)
    {
        Mesh mesh = new Mesh { name = "Cable Mesh" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        GetComponent<MeshFilter>().mesh = mesh;
    }

    // =========================================================
    // Elektron
    // =========================================================

    void CreateElectrons()
    {
        electrons        = new GameObject[electronCount];
        electronProgress = new float[electronCount];

        Material electronMat = CreateElectronMaterial();

        for (int i = 0; i < electronCount; i++)
        {
            GameObject e = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            e.name = $"Electron {i}";
            e.transform.SetParent(transform);
            e.transform.localScale = Vector3.one * electronRadius * 2f;
            e.GetComponent<Renderer>().material = electronMat;
            Destroy(e.GetComponent<Collider>());
            e.SetActive(false);

            electrons[i]        = e;
            electronProgress[i] = 1f; // tandai sebagai "selesai" agar pool tahu slot ini kosong
        }
    }

    /// <summary>
    /// Membuat material elektron dengan texture prosedural yang memuat simbol minus (−).
    /// Menggunakan Unlit + ZTest Always agar elektron selalu terlihat di atas mesh kabel.
    /// </summary>
    Material CreateElectronMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Unlit/Texture")
                     ?? Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Standard");

        var mat = new Material(shader);

        // Render di atas mesh kabel — tanpa depth occlusion
        mat.renderQueue = 4000;
        if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 0);
        if (mat.HasProperty("_ZTest"))
            mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);

        Texture2D tex = GenerateSymbolTexture();

        if (mat.HasProperty("_BaseMap"))   mat.SetTexture("_BaseMap", tex);
        if (mat.HasProperty("_MainTex"))   mat.SetTexture("_MainTex", tex);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
        if (mat.HasProperty("_Color"))     mat.SetColor("_Color", Color.white);

        return mat;
    }

    /// <summary>
    /// Generate Texture2D prosedural: background warna elektron, bar horizontal simbol minus.
    /// Pusat bar ditempatkan di (symbolUCenter, symbolVCenter) dalam UV space untuk kompensasi
    /// wrap UV sphere Unity — sumbu +Z sphere berada di U ≈ 0.75, bukan 0.5.
    /// </summary>
    Texture2D GenerateSymbolTexture()
    {
        int res = Mathf.Max(32, textureResolution);
        var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Repeat;

        Color[] pixels = new Color[res * res];

        // Fill background
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = electronColor;

        if (showElectronSymbol)
        {
            int barW = Mathf.RoundToInt(res * symbolWidthRatio);
            int barH = Mathf.RoundToInt(res * symbolHeightRatio);

            // Pusat bar mengikuti symbolUCenter & symbolVCenter, dengan wrapping modulo
            int centerX = Mathf.RoundToInt(symbolUCenter * res);
            int centerY = Mathf.RoundToInt(symbolVCenter * res);

            int xMin = centerX - barW / 2;
            int xMax = xMin + barW;
            int yMin = centerY - barH / 2;
            int yMax = yMin + barH;

            const float aaRadius = 1.5f;

            for (int y = yMin; y < yMax; y++)
            {
                for (int x = xMin; x < xMax; x++)
                {
                    // Wrap koordinat agar bisa melewati seam texture
                    int px = ((x % res) + res) % res;
                    int py = ((y % res) + res) % res;

                    float distEdge = Mathf.Min(
                        x - xMin, xMax - 1 - x,
                        y - yMin, yMax - 1 - y
                    );
                    float t = Mathf.Clamp01(distEdge / aaRadius);
                    pixels[py * res + px] = Color.Lerp(electronColor, symbolColor, t);
                }
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    void UpdateElectrons(bool isPowered, float currentPower)
    {
        if (electrons == null || totalLength <= 0f) return;

        Camera cam = Camera.main;

        // 1. SELALU GERAKKAN elektron yang sedang aktif. 
        // (Walaupun mati listrik, biarkan elektron yang sudah terlanjur jalan sampai ke ujung kabel)
        for (int i = 0; i < electrons.Length; i++)
        {
            if (electrons[i] == null || !electrons[i].activeSelf) continue;

            electronProgress[i] += Time.deltaTime * electronSpeed / totalLength;

            if (electronProgress[i] >= 1f)
            {
                electrons[i].SetActive(false); // Kembalikan ke pool saat sampai di ujung
                continue;
            }

            electrons[i].transform.position = GetPositionAtProgress(electronProgress[i]);

            if (cam != null)
            {
                Vector3 dir = (cam.transform.position - electrons[i].transform.position).normalized;
                electrons[i].transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            }
        }

        // 2. KERAN SPAWN (Hanya buka jika sedang isPowered / ada listrik)
        if (isPowered && currentPower > 0f)
        {
            // Spawn interval: semakin besar power → interval makin pendek → elektron lebih sering muncul
            float normalizedPower = Mathf.Clamp01(currentPower / Mathf.Max(1f, maxReferencePower));
            float spawnInterval = Mathf.Lerp(maxSpawnInterval, minSpawnInterval, normalizedPower);

            _spawnTimer += Time.deltaTime;
            if (_spawnTimer >= spawnInterval)
            {
                _spawnTimer = 0f;
                SpawnElectron();
            }
        }
        else
        {
            // Jika mati, reset timer agar saat nyala lagi keluarnya rapi
            _spawnTimer = 0f;
        }
    }

    /// <summary>Aktifkan satu elektron dari pool di posisi awal kabel (progress = 0).</summary>
    void SpawnElectron()
    {
        for (int i = 0; i < electrons.Length; i++)
        {
            if (electrons[i] != null && !electrons[i].activeSelf)
            {
                electronProgress[i] = 0f;
                electrons[i].transform.position = GetPositionAtProgress(0f);
                electrons[i].SetActive(true);
                return;
            }
        }
        // Pool penuh — tidak ada spawn baru sampai ada elektron yang selesai
    }

    /// <summary>
    /// Mengembalikan posisi di sepanjang kabel berdasarkan nilai progress 0–1.
    /// </summary>
    Vector3 GetPositionAtProgress(float t)
    {
        if (splinePoints.Count == 0) return transform.position;

        float targetDist = t * totalLength;

        for (int i = 1; i < cumulativeDistances.Count; i++)
        {
            if (cumulativeDistances[i] >= targetDist)
            {
                float segT = (targetDist - cumulativeDistances[i - 1]) /
                             (cumulativeDistances[i] - cumulativeDistances[i - 1]);
                return Vector3.Lerp(splinePoints[i - 1], splinePoints[i], segT);
            }
        }

        return splinePoints[splinePoints.Count - 1];
    }

    // =========================================================
    // Editor Preview
    // =========================================================

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!Application.isPlaying && waypoints != null && waypoints.Length >= 2)
        {
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                BuildSpline();
                BuildMesh();
            };
        }
    }
#endif
}
