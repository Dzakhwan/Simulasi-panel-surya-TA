using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Menghitung rute kabel yang "menempel dinding" pada ketinggian baseboard.
/// Menggunakan A* di grid lantai (layer Floor) dengan collider layer 'Wall' sebagai
/// penghalang. Sel yang bersebelahan dinding diberi cost lebih murah (edge-bias)
/// sehingga kabel menyusuri sepanjang dinding dari sisi dalam ruangan.
/// Grid di-cache per scene; A* berjalan setiap kali rute diminta.
/// </summary>
public static class CableRouter
{
    private static string _cachedScene;
    private static Bounds _bounds;
    private static float _cell;
    private static int _width, _depth;
    private static bool[] _hasFloor;
    private static float[] _floorY;
    private static bool[] _wallBlocked;

    /// <summary>
    /// Rute kabel dari start → end (termasuk start & end), atau null bila tidak ada jalur.
    /// Titik interior berada di y = floorY + baseboardHeight.
    /// </summary>
    public static List<Vector3> Route(Vector3 start, Vector3 end,
        LayerMask floorMask, LayerMask wallMask,
        float cellSize, float baseboardHeight, float edgeBias, float bandThreshold)
    {
        EnsureGrid(floorMask, wallMask, cellSize, baseboardHeight);

        // Band lantai ditentukan dari FURNITUR (end). Inverter boleh di luar/atas lantai.
        float endFloor = RaycastFloorY(end, floorMask);
        if (endFloor < 0f) return null;

        float bandY = endFloor;

        // Cari sel walkable terdekat dari inverter (bisa di luar area lantai) &
        // dari furnitur, pada band lantai yang sama.
        if (!TryFindWalkableCell(start, bandY, bandThreshold, out int sx, out int sz)) return null;
        if (!TryFindWalkableCell(end, bandY, bandThreshold, out int ex, out int ez)) return null;
        if (sx == ex && sz == ez) return new List<Vector3> { start, end };

        // A*
        var path = AStar(sx, sz, ex, ez, bandY, bandThreshold, edgeBias);
        if (path == null) return null;

        path = Simplify(path);
        if (path == null || path.Count == 0) return new List<Vector3> { start, end };

        var result = new List<Vector3> { start };
        foreach (var c in path)
        {
            Vector3 p = CellCenter(c.x, c.z);
            float fy = _floorY[c.z * _width + c.x];
            p.y = fy + baseboardHeight;
            result.Add(p);
        }
        result.Add(end);
        return result;
    }

    /// <summary>Buang titik yang segaris (hanya simpan titik belokan).</summary>
    private static List<(int x, int z)> Simplify(List<(int x, int z)> path)
    {
        if (path == null || path.Count < 3) return path;

        var outp = new List<(int x, int z)> { path[0] };
        for (int i = 1; i < path.Count - 1; i++)
        {
            var a = path[i - 1];
            var b = path[i];
            var c = path[i + 1];
            bool collinear = (a.x == b.x && b.x == c.x) || (a.z == b.z && b.z == c.z);
            if (!collinear) outp.Add(b);
        }
        outp.Add(path[path.Count - 1]);
        return outp;
    }

    // ── Grid ────────────────────────────────────────────────────────────────

    private static void EnsureGrid(LayerMask floorMask, LayerMask wallMask,
        float cellSize, float baseboardHeight)
    {
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (_cachedScene == scene && _cell == cellSize && _bounds.size != Vector3.zero)
            return;

        _cachedScene = scene;
        _cell = cellSize;

        GameObject house = GameObject.Find("House");
        _bounds = house != null ? GetBounds(house) : GetFloorBounds(floorMask);
        if (_bounds.size == Vector3.zero)
        {
            _bounds = new Bounds(Vector3.zero, new Vector3(2000f, 1f, 2000f));
        }

        _width = Mathf.Max(1, Mathf.CeilToInt(_bounds.size.x / _cell));
        _depth = Mathf.Max(1, Mathf.CeilToInt(_bounds.size.z / _cell));

        int n = _width * _depth;
        _hasFloor = new bool[n];
        _floorY = new float[n];
        _wallBlocked = new bool[n];

        Vector3 origin = _bounds.min;
        float probeY = _bounds.max.y + 50f;
        float probeRadius = Mathf.Max(0.5f, _cell * 0.3f);

        for (int iz = 0; iz < _depth; iz++)
        {
            for (int ix = 0; ix < _width; ix++)
            {
                int idx = iz * _width + ix;
                float cx = origin.x + (ix + 0.5f) * _cell;
                float cz = origin.z + (iz + 0.5f) * _cell;
                Vector3 world = new Vector3(cx, probeY, cz);

                float fy = RaycastFloorY(world, floorMask);
                _hasFloor[idx] = fy >= 0f;
                _floorY[idx] = fy;

                if (_hasFloor[idx])
                {
                    Vector3 probeCenter = new Vector3(cx, fy + baseboardHeight, cz);
                    _wallBlocked[idx] = Physics.OverlapSphere(probeCenter, probeRadius, wallMask,
                        QueryTriggerInteraction.Ignore).Length > 0;
                }
            }
        }
    }

    private static bool IsWalkable(int ix, int iz, float bandY, float bandThreshold, out float floorY)
    {
        floorY = 0f;
        if (ix < 0 || iz < 0 || ix >= _width || iz >= _depth) return false;
        int idx = iz * _width + ix;
        if (!_hasFloor[idx] || _wallBlocked[idx]) return false;
        floorY = _floorY[idx];
        return Mathf.Abs(floorY - bandY) <= bandThreshold;
    }

    private static bool IsWallNeighbor(int ix, int iz, float bandY, float bandThreshold)
    {
        // Terlarang (di luar grid / sel terblok) dianggap "dinding"
        if (ix < 0 || iz < 0 || ix >= _width || iz >= _depth) return true;
        int idx = iz * _width + ix;
        if (!_hasFloor[idx] || _wallBlocked[idx]) return true;
        return Mathf.Abs(_floorY[idx] - bandY) > bandThreshold;
    }

    private static bool TryFindWalkableCell(Vector3 world, float bandY, float bandThreshold,
        out int ox, out int oz)
    {
        int cx = Mathf.FloorToInt((world.x - _bounds.min.x) / _cell);
        int cz = Mathf.FloorToInt((world.z - _bounds.min.z) / _cell);

        // Pencarian melebar dari sel awal (bisa jauh bila ujung di luar area lantai)
        for (int r = 0; r < 400; r++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                    int nix = cx + dx, niz = cz + dz;
                    if (IsWalkable(nix, niz, bandY, bandThreshold, out _))
                    {
                        ox = nix; oz = niz;
                        return true;
                    }
                }
            }
        }
        ox = cx; oz = cz;
        return false;
    }

    // ── A* ──────────────────────────────────────────────────────────────────

    private static List<(int x, int z)> AStar(int sx, int sz, int ex, int ez,
        float bandY, float bandThreshold, float edgeBias)
    {
        int n = _width * _depth;
        var g = new float[n];
        var parent = new int[n];
        var closed = new bool[n];
        for (int i = 0; i < n; i++) { g[i] = float.MaxValue; parent[i] = -1; }

        var open = new PriorityQueueTarget();
        int startIdx = sz * _width + sx;
        int endIdx = ez * _width + ex;
        g[startIdx] = 0f;
        open.Push(startIdx, Heuristic(sx, sz, ex, ez));

        int[] dx = { 1, -1, 0, 0 };
        int[] dz = { 0, 0, 1, -1 };

        while (open.Count > 0)
        {
            int cur = open.Pop();
            if (closed[cur]) continue;
            closed[cur] = true;
            if (cur == endIdx)
            {
                return RebuildPath(parent, startIdx, endIdx);
            }

            int cix = cur % _width;
            int ciz = cur / _width;

            for (int k = 0; k < 4; k++)
            {
                int nix = cix + dx[k];
                int niz = ciz + dz[k];
                if (!IsWalkable(nix, niz, bandY, bandThreshold, out _)) continue;

                int ni = niz * _width + nix;
                if (closed[ni]) continue;

                // Sel yang menempel dinding lebih murah → kabel menyusuri dinding
                float step = IsWallNeighbor(nix, niz, bandY, bandThreshold) ? (1f - edgeBias) : 1f;
                float ng = g[cur] + step;
                if (ng < g[ni])
                {
                    g[ni] = ng;
                    parent[ni] = cur;
                    open.Push(ni, ng + Heuristic(nix, niz, ex, ez));
                }
            }
        }
        return null;
    }

    private static float Heuristic(int ix, int iz, int ex, int ez)
    {
        return Mathf.Abs(ix - ex) + Mathf.Abs(iz - ez);
    }

    private static List<(int x, int z)> RebuildPath(int[] parent, int start, int end)
    {
        if (parent[end] == -1) return null;

        var list = new List<(int, int)>();
        int cur = end;
        while (cur != start)
        {
            list.Add((cur % _width, cur / _width));
            if (parent[cur] == -1) break;
            cur = parent[cur];
        }
        list.Reverse();
        return list;
    }

    private static Vector3 CellCenter(int ix, int iz)
    {
        return new Vector3(
            _bounds.min.x + (ix + 0.5f) * _cell,
            0f,
            _bounds.min.z + (iz + 0.5f) * _cell);
    }

    // ── Helper ──────────────────────────────────────────────────────────────

    private static float RaycastFloorY(Vector3 world, LayerMask floorMask)
    {
        if (Physics.Raycast(world, Vector3.down, out RaycastHit hit, 10000f, floorMask,
            QueryTriggerInteraction.Ignore))
        {
            return hit.point.y;
        }
        return -1f;
    }

    private static Bounds GetBounds(GameObject root)
    {
        Renderer[] rends = root.GetComponentsInChildren<Renderer>(true);
        if (rends.Length == 0) return new Bounds(root.transform.position, Vector3.zero);
        Bounds b = rends[0].bounds;
        foreach (Renderer r in rends) b.Encapsulate(r.bounds);
        return b;
    }

    private static Bounds GetFloorBounds(LayerMask floorMask)
    {
        var cols = Physics.OverlapBox(Vector3.zero, Vector3.one * 5000f, Quaternion.identity, floorMask);
        if (cols.Length == 0) return new Bounds(Vector3.zero, Vector3.one);
        Bounds b = cols[0].bounds;
        foreach (var c in cols) b.Encapsulate(c.bounds);
        return b;
    }

    // Min-heap untuk A*
    private class PriorityQueueTarget
    {
        private readonly List<(float f, int idx)> _list = new();

        public int Count => _list.Count;

        public void Push(int idx, float f)
        {
            _list.Add((f, idx));
            int i = _list.Count - 1;
            while (i > 0)
            {
                int p = (i - 1) / 2;
                if (_list[p].f <= _list[i].f) break;
                (_list[p], _list[i]) = (_list[i], _list[p]);
                i = p;
            }
        }

        public int Pop()
        {
            int top = _list[0].idx;
            int last = _list.Count - 1;
            _list[0] = _list[last];
            _list.RemoveAt(last);

            int i = 0;
            int n = _list.Count;
            while (true)
            {
                int l = i * 2 + 1, r = i * 2 + 2, smallest = i;
                if (l < n && _list[l].f < _list[smallest].f) smallest = l;
                if (r < n && _list[r].f < _list[smallest].f) smallest = r;
                if (smallest == i) break;
                (_list[i], _list[smallest]) = (_list[smallest], _list[i]);
                i = smallest;
            }
            return top;
        }
    }
}