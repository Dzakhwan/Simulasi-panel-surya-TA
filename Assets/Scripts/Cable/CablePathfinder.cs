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

        // Radius graph adalah batas mutlak. Tidak ada pelebaran otomatis yang
        // memungkinkan kabel melompat jauh dari waypoint yang dirancang user.
        float candidateRadius = Mathf.Min(maxSnapRadius, graph.MaxCandidateRadius);
        List<CableWaypointGraph.CandidateWaypoint> startCandidates =
            graph.FindCandidates(startPort, obstacleMask, candidateRadius, startSource);
        List<CableWaypointGraph.CandidateWaypoint> endCandidates =
            graph.FindCandidates(endPort, obstacleMask, candidateRadius, endSource);

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

        CableWaypointGraph.CandidateWaypoint startNodeCandidate = default;
        CableWaypointGraph.CandidateWaypoint terminal = default;
        List<CableWaypoint> bestWaypointPath = null;

        foreach (var endCandidate in endCandidates)
        {
            if (endCandidate.waypoint == null) continue;

            float bestStartCost = float.MaxValue;
            CableWaypointGraph.CandidateWaypoint bestStart = default;
            List<CableWaypoint> bestPathForEnd = null;

            foreach (var startCandidate in startCandidates)
            {
                if (startCandidate.waypoint == null) continue;
                RunDijkstra(startCandidate.waypoint, out var distMap, out var prevMap, graph.MaxWaypointLinkDistance);

                if (!distMap.TryGetValue(endCandidate.waypoint, out float graphCost) || graphCost == float.MaxValue)
                    continue;

                List<CableWaypoint> path = ReconstructPath(startCandidate.waypoint, endCandidate.waypoint, prevMap);
                if (path == null || path.Count == 0) continue;

                float startCost = startCandidate.distance * connectorCostScale + graphCost;
                if (startCost < bestStartCost)
                {
                    bestStartCost = startCost;
                    bestStart = startCandidate;
                    bestPathForEnd = path;
                }
            }

            if (bestPathForEnd != null)
            {
                startNodeCandidate = bestStart;
                terminal = endCandidate;
                bestWaypointPath = bestPathForEnd;
                break;
            }
        }

        if (bestWaypointPath != null)
            Debug.Log($"[CablePathfinder] Rute node demi node: {startNodeCandidate.waypoint.name} -> {terminal.waypoint.name} ({bestWaypointPath.Count} node)");

        if (bestWaypointPath == null || bestWaypointPath.Count == 0)
        {
            Debug.LogWarning("[CablePathfinder] Graf terputus: tidak ada jalur bertetangga dari inverter ke terminal alat.");
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
        out Dictionary<CableWaypoint, CableWaypoint> prev,
        float maxWaypointLinkDistance = float.PositiveInfinity)
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
                if (v != null && Vector3.Distance(u.Position, v.Position) <= maxWaypointLinkDistance && !discovered.Contains(v))
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
                if (v == null || !unvisited.Contains(v) || Vector3.Distance(u.Position, v.Position) > maxWaypointLinkDistance) continue;

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
