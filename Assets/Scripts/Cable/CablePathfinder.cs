using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Implementasi algoritma Dijkstra untuk mencari rute kabel terpendek pada CableWaypointGraph.
/// Menangani sambungan dinamis dari port inverter dan port peralatan dengan validasi Line-of-Sight
/// agar kabel tidak pernah menembus dinding.
/// </summary>
public static class CablePathfinder
{
    /// <summary>
    /// Line-of-Sight check bebas self-hit: garis dari `from` ke `to` dianggap bebas
    /// bila tidak ada collider pada `mask` yang menghalang, KECUALI collider yang
    /// merupakan bagian dari GO `source` (mis. collider inverter / alat itu sendiri).
    /// </summary>
    public static bool LinecastFree(Vector3 from, Vector3 to, LayerMask mask,
        Transform source = null, QueryTriggerInteraction trigger = QueryTriggerInteraction.Ignore)
    {
        if (mask.value == 0) return true;

        Vector3 delta = to - from;
        float dist = delta.magnitude;
        if (dist < 0.0001f) return true;

        if (source == null)
            return !Physics.Linecast(from, to, mask, trigger);

        // RaycastAll agar hit milik source sendiri bisa diabaikan (fix self-hit)
        var hits = Physics.RaycastAll(from, delta / dist, dist, mask, trigger);
        for (int i = 0; i < hits.Length; i++)
        {
            var col = hits[i].collider;
            if (col == null) continue;
            if (col.transform.IsChildOf(source))
                continue;
            return false;
        }
        return true;
    }

    /// <summary>
    /// Mencari rute terbaik dari startPort ke endPort melalui simpul graf yang tersedia.
    /// Radius pencarian kandidat adaptif: bila kandidat kosong, radius dinaikkan hingga 2x.
    /// Mengembalikan daftar titik Vector3 terurut: [startPort, wp1, wp2, ..., endPort],
    /// atau null bila tidak ada rute valid yang dapat dicapai.
    /// </summary>
    public static List<Vector3> FindRoute(
        Vector3 startPort,
        Vector3 endPort,
        CableWaypointGraph graph,
        LayerMask obstacleMask,
        float maxSnapRadius = 500f,
        Transform startSource = null,
        Transform endSource = null,
        float connectorCostScale = 1f)
    {
        if (graph == null)
        {
            Debug.LogWarning("[CablePathfinder] CableWaypointGraph bernilai null.");
            return null;
        }

        // 1. Dapatkan kandidat simpul masuk (dekat inverter) dan keluar (dekat alat) yang bebas rintangan.
        //    Radius adaptif: 1x → 1.5x → 2x dari maxSnapRadius agar koneksi tidak gagal karena
        //    selisih beberapa unit saja (mis. 501 > 500).
        List<CableWaypointGraph.CandidateWaypoint> startCandidates = null;
        List<CableWaypointGraph.CandidateWaypoint> endCandidates = null;

        for (float factor = 1f; factor <= 2.01f; factor += 0.5f)
        {
            float radius = maxSnapRadius * factor;
            startCandidates = graph.FindCandidates(startPort, obstacleMask, radius, startSource);
            endCandidates = graph.FindCandidates(endPort, obstacleMask, radius, endSource);

            if (startCandidates.Count > 0 && endCandidates.Count > 0)
                break;

            if (startCandidates.Count > 0 && endCandidates.Count == 0)
                Debug.LogWarning($"[CablePathfinder] Kandidat akhir kosong pada radius {radius:F0} — mencoba radius lebih besar.");
            else if (endCandidates.Count > 0 && startCandidates.Count == 0)
                Debug.LogWarning($"[CablePathfinder] Kandidat awal kosong pada radius {radius:F0} — mencoba radius lebih besar.");
        }

        if (startCandidates.Count == 0)
        {
            Debug.LogWarning("[CablePathfinder] Tidak ada waypoint yang dapat dijangkau dari Inverter (terhalang dinding atau di luar radius).");
            return null;
        }

        if (endCandidates.Count == 0)
        {
            Debug.LogWarning("[CablePathfinder] Tidak ada waypoint yang dapat dijangkau dari Peralatan (terhalang dinding atau di luar radius).");
            return null;
        }

        CableWaypointGraph.CandidateWaypoint terminal = default;
        bool terminalFound = false;
        float terminalDistance = float.MaxValue;
        foreach (var candidate in endCandidates)
        {
            if (candidate.waypoint == null) continue;
            if (candidate.distance < terminalDistance)
            {
                terminal = candidate;
                terminalDistance = candidate.distance;
                terminalFound = true;
            }
        }

        if (!terminalFound)
        {
            Debug.LogWarning("[CablePathfinder] Tidak ada terminal waypoint valid di dekat alat.");
            return null;
        }

        float bestTotalCost = float.MaxValue;
        List<CableWaypoint> bestWaypointPath = null;
        var evaluatedStarts = new HashSet<CableWaypoint>();

        foreach (var startCand in startCandidates)
        {
            CableWaypoint s = startCand.waypoint;
            CableWaypoint e = terminal.waypoint;
            if (s == null || evaluatedStarts.Contains(s)) continue;
            evaluatedStarts.Add(s);

            RunDijkstra(s, out var distMap, out var prevMap);
            if (!distMap.TryGetValue(e, out float graphCost) || graphCost == float.MaxValue)
                continue;

            float totalCost = startCand.distance * connectorCostScale
                            + graphCost
                            + terminal.distance * connectorCostScale;
            if (totalCost < bestTotalCost)
            {
                bestTotalCost = totalCost;
                bestWaypointPath = ReconstructPath(s, e, prevMap);
            }
        }

        if (bestWaypointPath == null || bestWaypointPath.Count == 0)
        {
            Debug.LogWarning("[CablePathfinder] Graf terputus: tidak ditemukan jalur waypoint dari Inverter ke Peralatan.");
            return null;
        }

        // 3. Susun urutan titik rute final
        var finalPath = new List<Vector3> { startPort };

        foreach (var wp in bestWaypointPath)
        {
            finalPath.Add(wp.Position);
        }

        finalPath.Add(endPort);

        return finalPath;
    }

    /// <summary>
    /// Algoritma Dijkstra standar untuk satu simpul asal ke semua simpul yang dapat dijangkau.
    /// </summary>
    public static void RunDijkstra(
        CableWaypoint start,
        out Dictionary<CableWaypoint, float> dist,
        out Dictionary<CableWaypoint, CableWaypoint> prev)
    {
        dist = new Dictionary<CableWaypoint, float>();
        prev = new Dictionary<CableWaypoint, CableWaypoint>();

        if (start == null) return;

        // Min-Priority Queue sederhana untuk simpul unvisited
        var unvisited = new HashSet<CableWaypoint>();
        dist[start] = 0f;
        unvisited.Add(start);

        // Koleksi semua simpul yang terhubung dalam komponen graf
        var queue = new Queue<CableWaypoint>();
        queue.Enqueue(start);
        var discovered = new HashSet<CableWaypoint> { start };

        while (queue.Count > 0)
        {
            var u = queue.Dequeue();
            foreach (var v in u.Neighbors)
            {
                if (v != null && !discovered.Contains(v))
                {
                    discovered.Add(v);
                    dist[v] = float.MaxValue;
                    unvisited.Add(v);
                    queue.Enqueue(v);
                }
            }
        }

        while (unvisited.Count > 0)
        {
            // Ambil simpul unvisited dengan jarak terkecil
            CableWaypoint u = null;
            float minDist = float.MaxValue;

            foreach (var node in unvisited)
            {
                if (dist.TryGetValue(node, out float d) && d < minDist)
                {
                    minDist = d;
                    u = node;
                }
            }

            if (u == null || minDist == float.MaxValue)
                break; // Simpul yang tersisa tidak dapat dijangkau

            unvisited.Remove(u);

            // Relaksasi tetangga
            foreach (var v in u.Neighbors)
            {
                if (v == null || !unvisited.Contains(v)) continue;

                float edgeWeight = Vector3.Distance(u.Position, v.Position);
                float alt = minDist + edgeWeight;

                if (!dist.TryGetValue(v, out float currentDist) || alt < currentDist)
                {
                    dist[v] = alt;
                    prev[v] = u;
                }
            }
        }
    }

    private static List<CableWaypoint> ReconstructPath(
        CableWaypoint start,
        CableWaypoint end,
        Dictionary<CableWaypoint, CableWaypoint> prev)
    {
        var path = new List<CableWaypoint>();
        if (start == end)
        {
            path.Add(start);
            return path;
        }

        CableWaypoint curr = end;
        while (curr != null && curr != start)
        {
            path.Add(curr);
            if (!prev.TryGetValue(curr, out curr))
                break;
        }

        if (curr != start)
        {
            // Tidak dapat direkonstruksi ke start
            return null;
        }

        path.Add(start);
        path.Reverse();
        return path;
    }
}
