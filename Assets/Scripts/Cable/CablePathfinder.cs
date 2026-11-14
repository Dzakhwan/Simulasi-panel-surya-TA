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
        List<CableWaypointGraph.CandidateWaypoint> startCandidates = FilterCandidates(
            graph.FindCandidates(startPort, obstacleMask, candidateRadius, startSource), false, float.PositiveInfinity);
        List<CableWaypointGraph.CandidateWaypoint> endCandidates =
            graph.FindCandidates(endPort, obstacleMask, candidateRadius, endSource);

        endCandidates = FilterCandidatesByBranchDistance(endCandidates, graph);

        if (startCandidates.Count == 0)
        {
            Debug.LogWarning($"[CablePathfinder] FAIL start: tidak ada waypoint valid dari port inverter {startPort} dalam radius {candidateRadius:F1}.");
            return null;
        }

        if (endCandidates.Count == 0)
        {
            Debug.LogWarning($"[CablePathfinder] FAIL end: tidak ada waypoint valid dari port alat {endPort} dalam radius {candidateRadius:F1}.");
            return null;
        }

        Debug.Log($"[CablePathfinder] Kandidat start={startCandidates.Count}, end={endCandidates.Count}; startPort={startPort}; endPort={endPort}");

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

                bool usesFloorBranch = endCandidate.waypoint.Category == WaypointCategory.Floor;
                if (usesFloorBranch && !HasBackboneBeforeFloor(path))
                {
                    Debug.LogWarning($"[CablePathfinder] END ditolak: {endCandidate.waypoint.name}; tidak ada jalur backbone Wall menuju Floor.");
                    continue;
                }

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
            Debug.LogWarning("[CablePathfinder] FAIL graph: semua kandidat terminal tidak tercapai dari kandidat start.");
            foreach (var endCandidate in endCandidates)
                Debug.LogWarning($"[CablePathfinder] END tidak tercapai: {endCandidate.waypoint.name} dPort={endCandidate.distance:F1}");
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
    private static bool HasBackboneBeforeFloor(List<CableWaypoint> path)
    {
        bool backboneSeen = false;
        foreach (var waypoint in path)
        {
            if (waypoint == null) continue;
            if (waypoint.Category == WaypointCategory.Floor)
                return backboneSeen;
            backboneSeen = true;
        }
        return false;
    }

    private static List<CableWaypointGraph.CandidateWaypoint> FilterCandidatesByBranchDistance(
        List<CableWaypointGraph.CandidateWaypoint> candidates, CableWaypointGraph graph)
    {
        candidates.Sort((a, b) => a.distance.CompareTo(b.distance));
        return candidates;
    }

    private static List<CableWaypointGraph.CandidateWaypoint> FilterCandidates(
        List<CableWaypointGraph.CandidateWaypoint> candidates, bool floorOnly, float maxDistance)
    {
        var result = new List<CableWaypointGraph.CandidateWaypoint>();
        foreach (var candidate in candidates)
        {
            if (candidate.waypoint == null || candidate.distance > maxDistance) continue;
            bool isFloor = candidate.waypoint.Category == WaypointCategory.Floor;
            if (floorOnly != isFloor) continue;
            result.Add(candidate);
        }
        return result;
    }

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
                if (v != null && Vector3.Distance(u.Position, v.Position) > maxWaypointLinkDistance)
                    Debug.LogWarning($"[CablePathfinder] EDGE ditolak: {u.name} -> {v.name}; jarak={Vector3.Distance(u.Position, v.Position):F1} > limit={maxWaypointLinkDistance:F1}");
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
