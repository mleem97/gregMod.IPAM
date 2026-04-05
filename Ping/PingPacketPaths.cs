using System.Collections.Generic;
using UnityEngine;

namespace DHCPSwitches;

/// <summary>
/// Ping visuals follow scene cable geometry when possible. Paths are built <b>per hop</b> (router → L2 switch → … → target)
/// so traffic is not drawn as a straight chord through intermediate rack gear. Uses <see cref="Resources.FindObjectsOfTypeAll{T}"/>
/// for inactive <see cref="LineRenderer"/>s and merges mesh / name-heuristic cable edges.
/// </summary>
internal static class PingPacketPaths
{
    private const float VertexMergeEpsilon = 0.08f;
    private const float CableAttachRadius = 4.5f;
    private const int MaxGraphVertices = 18000;
    private const int FallbackNearestCount = 8;
    private const float CableTransformLinkMeters = 0.65f;
    private const float L2BetweenLateralMaxSq = 14f;
    private const float AnchorBridgeMeters = 3.2f;

    private static readonly List<LineRenderer> LineRendererBuffer = new(128);
    private static readonly List<Transform> HopChainBuffer = new(12);
    private static readonly List<Vector3> SegmentBuffer = new(128);

    internal static void BuildPath(NetworkSwitch sourceDevice, Transform destination, List<Vector3> pathOut)
    {
        if (sourceDevice == null)
        {
            pathOut.Clear();
            return;
        }

        BuildPathFromTransform(sourceDevice, sourceDevice.transform, destination, pathOut);
    }

    /// <summary>Start the cable graph from a specific chassis point (e.g. Gi0/n socket) instead of the device pivot.</summary>
    internal static void BuildPathFromTransform(NetworkSwitch sourceDevice, Transform pathStartRoot, Transform destination, List<Vector3> pathOut)
    {
        pathOut.Clear();
        if (sourceDevice == null || destination == null || pathStartRoot == null)
        {
            return;
        }

        var srcT = pathStartRoot;
        var src = srcT.position;
        var dst = destination.position;

        AppendHopChain(srcT, destination, sourceDevice, HopChainBuffer);
        var (regionMid, regionSq) = ComputeGraphRegion(HopChainBuffer);

        var nodes = new List<Vector3>(768);
        var adj = new List<List<int>>();

        int FindOrAdd(Vector3 p)
        {
            var mergeSq = VertexMergeEpsilon * VertexMergeEpsilon;
            for (var i = 0; i < nodes.Count; i++)
            {
                if ((nodes[i] - p).sqrMagnitude <= mergeSq)
                {
                    return i;
                }
            }

            if (nodes.Count >= MaxGraphVertices)
            {
                return -1;
            }

            nodes.Add(p);
            adj.Add(new List<int>(4));
            return nodes.Count - 1;
        }

        void Edge(int a, int b)
        {
            if (a < 0 || b < 0 || a == b)
            {
                return;
            }

            var la = adj[a];
            if (!la.Contains(b))
            {
                la.Add(b);
            }

            var lb = adj[b];
            if (!lb.Contains(a))
            {
                lb.Add(a);
            }
        }

        PopulateCableGraph(regionMid, regionSq, nodes, adj, FindOrAdd, Edge);
        AddAnchorBridgesForAllRackDevices(nodes, adj, FindOrAdd, Edge);

        pathOut.Clear();
        var anySegment = false;
        for (var h = 0; h < HopChainBuffer.Count - 1; h++)
        {
            SegmentBuffer.Clear();
            if (TryRouteOnGraph(nodes, adj, HopChainBuffer[h], HopChainBuffer[h + 1], SegmentBuffer))
            {
                anySegment = true;
            }
            else
            {
                StraightSegment(HopChainBuffer[h].position, HopChainBuffer[h + 1].position, SegmentBuffer);
                anySegment = true;
            }

            if (h == 0)
            {
                pathOut.AddRange(SegmentBuffer);
            }
            else
            {
                MergeAppendPath(pathOut, SegmentBuffer);
            }
        }

        if (!anySegment || pathOut.Count < 2)
        {
            pathOut.Clear();
            pathOut.Add(src);
            pathOut.Add(dst);
        }

        EnsureDistinctEndpoints(pathOut, src, dst);
    }

    /// <summary>Router → L2 switches geometrically between source and destination (ordered along the chord) → destination.</summary>
    private static void AppendHopChain(Transform sourceRoot, Transform destRoot, NetworkSwitch sourceSwitch, List<Transform> hops)
    {
        hops.Clear();
        hops.Add(sourceRoot);

        var a = sourceRoot.position;
        var b = destRoot.position;
        var ab = b - a;
        var abLen = ab.magnitude;
        if (abLen < 0.02f)
        {
            hops.Add(destRoot);
            return;
        }

        var dir = ab / abLen;
        var scored = new List<(float t, Transform tr)>(8);
        var allSw = Resources.FindObjectsOfTypeAll<NetworkSwitch>();
        if (allSw != null)
        {
            foreach (var sw in allSw)
            {
                if (sw == null)
                {
                    continue;
                }

                if (!sw.gameObject.scene.IsValid() || !sw.gameObject.scene.isLoaded)
                {
                    continue;
                }

                if (NetworkDeviceClassifier.GetKind(sw) != NetworkDeviceKind.Layer2Switch)
                {
                    continue;
                }

                if (sourceSwitch != null && sw == sourceSwitch)
                {
                    continue;
                }

                var tr = sw.transform;
                if (tr == sourceRoot || tr == destRoot)
                {
                    continue;
                }

                var p = tr.position;
                var ap = p - a;
                var tProj = Vector3.Dot(ap, dir);
                if (tProj < 0.08f || tProj > abLen - 0.08f)
                {
                    continue;
                }

                var lateral = ap - dir * tProj;
                if (lateral.sqrMagnitude > L2BetweenLateralMaxSq)
                {
                    continue;
                }

                scored.Add((tProj, tr));
            }
        }

        scored.Sort((x, y) => x.t.CompareTo(y.t));
        var seen = new HashSet<int>();
        foreach (var (_, tr) in scored)
        {
            var id = tr.GetInstanceID();
            if (!seen.Add(id))
            {
                continue;
            }

            hops.Add(tr);
        }

        hops.Add(destRoot);
    }

    private static (Vector3 mid, float regionSq) ComputeGraphRegion(List<Transform> hops)
    {
        if (hops == null || hops.Count == 0)
        {
            return (Vector3.zero, 40f * 40f);
        }

        var mid = Vector3.zero;
        foreach (var h in hops)
        {
            mid += h.position;
        }

        mid /= hops.Count;
        var maxSq = 4f;
        foreach (var h in hops)
        {
            var d = (h.position - mid).sqrMagnitude;
            if (d > maxSq)
            {
                maxSq = d;
            }
        }

        var ext = Mathf.Sqrt(maxSq) + 22f;
        var regionSq = ext * ext;
        return (mid, regionSq);
    }

    private static void PopulateCableGraph(
        Vector3 regionMid,
        float regionSq,
        List<Vector3> nodes,
        List<List<int>> adj,
        System.Func<Vector3, int> findOrAdd,
        System.Action<int, int> edge)
    {
        nodes.Clear();
        adj.Clear();

        CollectSceneLineRenderers(LineRendererBuffer);
        foreach (var lr in LineRendererBuffer)
        {
            if (lr == null || lr.positionCount < 2)
            {
                continue;
            }

            var n = lr.positionCount;
            for (var i = 0; i < n - 1; i++)
            {
                var u = findOrAdd(GetWorldPosition(lr, i));
                var v = findOrAdd(GetWorldPosition(lr, i + 1));
                if (u < 0 || v < 0)
                {
                    continue;
                }

                edge(u, v);
            }
        }

        AddMeshCableBoundsInRegion(regionMid, regionSq, findOrAdd, edge);
        AddCableTransformChainInRegion(regionMid, regionSq, findOrAdd, edge);
    }

    private static void AddMeshCableBoundsInRegion(
        Vector3 regionMid,
        float regionSq,
        System.Func<Vector3, int> findOrAdd,
        System.Action<int, int> edge)
    {
        var renderers = Resources.FindObjectsOfTypeAll<Renderer>();
        if (renderers == null)
        {
            return;
        }

        foreach (var r in renderers)
        {
            if (r == null || r is LineRenderer)
            {
                continue;
            }

            if (r is not MeshRenderer && r is not SkinnedMeshRenderer)
            {
                continue;
            }

            var go = r.gameObject;
            if (!go.scene.IsValid() || !go.scene.isLoaded)
            {
                continue;
            }

            if (!LooksLikeCableObjectName(go.name))
            {
                continue;
            }

            var b = r.bounds;
            if ((b.center - regionMid).sqrMagnitude > regionSq)
            {
                continue;
            }

            var ext = b.extents;
            Vector3 p0;
            Vector3 p1;
            if (ext.x >= ext.y && ext.x >= ext.z)
            {
                p0 = b.center - new Vector3(ext.x, 0f, 0f);
                p1 = b.center + new Vector3(ext.x, 0f, 0f);
            }
            else if (ext.y >= ext.z)
            {
                p0 = b.center - new Vector3(0f, ext.y, 0f);
                p1 = b.center + new Vector3(0f, ext.y, 0f);
            }
            else
            {
                p0 = b.center - new Vector3(0f, 0f, ext.z);
                p1 = b.center + new Vector3(0f, 0f, ext.z);
            }

            var ia = findOrAdd(p0);
            var ib = findOrAdd(p1);
            if (ia >= 0 && ib >= 0)
            {
                edge(ia, ib);
            }
        }
    }

    private static void AddCableTransformChainInRegion(
        Vector3 regionMid,
        float regionSq,
        System.Func<Vector3, int> findOrAdd,
        System.Action<int, int> edge)
    {
        var linkSq = CableTransformLinkMeters * CableTransformLinkMeters;
        var candidates = new List<Transform>(64);
        var allTr = Resources.FindObjectsOfTypeAll<Transform>();
        if (allTr == null)
        {
            return;
        }

        foreach (var t in allTr)
        {
            if (t == null)
            {
                continue;
            }

            var go = t.gameObject;
            if (!go.scene.IsValid() || !go.scene.isLoaded)
            {
                continue;
            }

            if (!LooksLikeCableObjectName(go.name))
            {
                continue;
            }

            if ((t.position - regionMid).sqrMagnitude > regionSq)
            {
                continue;
            }

            candidates.Add(t);
        }

        candidates.Sort((a, b) =>
            (a.position - regionMid).sqrMagnitude.CompareTo((b.position - regionMid).sqrMagnitude));
        while (candidates.Count > 96)
        {
            candidates.RemoveAt(candidates.Count - 1);
        }

        for (var i = 0; i < candidates.Count; i++)
        {
            for (var j = i + 1; j < candidates.Count; j++)
            {
                if ((candidates[i].position - candidates[j].position).sqrMagnitude > linkSq)
                {
                    continue;
                }

                var ia = findOrAdd(candidates[i].position);
                var ib = findOrAdd(candidates[j].position);
                if (ia >= 0 && ib >= 0)
                {
                    edge(ia, ib);
                }
            }
        }
    }

    private static void AddAnchorBridgesForAllRackDevices(
        List<Vector3> nodes,
        List<List<int>> adj,
        System.Func<Vector3, int> findOrAdd,
        System.Action<int, int> edge)
    {
        var buf = new List<Vector3>(40);
        var bridgeSq = AnchorBridgeMeters * AnchorBridgeMeters;

        var switches = Resources.FindObjectsOfTypeAll<NetworkSwitch>();
        if (switches != null)
        {
            foreach (var sw in switches)
            {
                if (sw == null || !sw.gameObject.scene.IsValid() || !sw.gameObject.scene.isLoaded)
                {
                    continue;
                }

                CollectAnchors(sw.transform, buf);
                AddAnchorBridgesToNearbyNodes(buf, nodes, findOrAdd, edge, bridgeSq, 10);
            }
        }

        var servers = Resources.FindObjectsOfTypeAll<Server>();
        if (servers != null)
        {
            foreach (var srv in servers)
            {
                if (srv == null || !srv.gameObject.scene.IsValid() || !srv.gameObject.scene.isLoaded)
                {
                    continue;
                }

                CollectAnchors(srv.transform, buf);
                AddAnchorBridgesToNearbyNodes(buf, nodes, findOrAdd, edge, bridgeSq, 10);
            }
        }
    }

    private static bool TryRouteOnGraph(
        List<Vector3> nodes,
        List<List<int>> adj,
        Transform from,
        Transform to,
        List<Vector3> segmentOut)
    {
        segmentOut.Clear();
        if (nodes.Count == 0 || from == null || to == null)
        {
            return false;
        }

        var src = from.position;
        var dst = to.position;

        var fromAnchors = new List<Vector3>(40);
        var toAnchors = new List<Vector3>(40);
        CollectAnchors(from, fromAnchors);
        CollectAnchors(to, toAnchors);

        var attachSq = CableAttachRadius * CableAttachRadius;
        var startIndices = new List<int>();
        var goalSet = new HashSet<int>();

        MinDistToAnchorsSq(nodes, fromAnchors, attachSq, startIndices, null);
        MinDistToAnchorsSq(nodes, toAnchors, attachSq, null, goalSet);

        if (startIndices.Count == 0)
        {
            AddKNearestNodeIndices(nodes, src, startIndices, FallbackNearestCount);
        }

        if (goalSet.Count == 0)
        {
            var tmp = new List<int>();
            AddKNearestNodeIndices(nodes, dst, tmp, FallbackNearestCount);
            foreach (var g in tmp)
            {
                goalSet.Add(g);
            }
        }

        if (startIndices.Count == 0 || goalSet.Count == 0)
        {
            return false;
        }

        var parent = new int[nodes.Count];
        for (var i = 0; i < parent.Length; i++)
        {
            parent[i] = -1;
        }

        var q = new Queue<int>();
        foreach (var s in startIndices)
        {
            if (parent[s] >= 0)
            {
                continue;
            }

            parent[s] = s;
            q.Enqueue(s);
        }

        var found = -1;
        while (q.Count > 0)
        {
            var u = q.Dequeue();
            if (goalSet.Contains(u))
            {
                found = u;
                break;
            }

            foreach (var v in adj[u])
            {
                if (parent[v] >= 0)
                {
                    continue;
                }

                parent[v] = u;
                q.Enqueue(v);
            }
        }

        if (found < 0)
        {
            return false;
        }

        var idxChain = new List<int>(32);
        for (var c = found;;)
        {
            idxChain.Add(c);
            if (parent[c] == c)
            {
                break;
            }

            c = parent[c];
        }

        idxChain.Reverse();

        segmentOut.Clear();
        AppendUnique(segmentOut, src);
        foreach (var idx in idxChain)
        {
            AppendUnique(segmentOut, nodes[idx]);
        }

        AppendUnique(segmentOut, dst);
        return segmentOut.Count >= 2;
    }

    private static void StraightSegment(Vector3 a, Vector3 b, List<Vector3> segmentOut)
    {
        segmentOut.Clear();
        segmentOut.Add(a);
        segmentOut.Add(b);
    }

    private static void MergeAppendPath(List<Vector3> acc, List<Vector3> seg)
    {
        if (seg == null || seg.Count == 0)
        {
            return;
        }

        for (var i = 0; i < seg.Count; i++)
        {
            if (i == 0 && acc.Count > 0 && (acc[^1] - seg[0]).sqrMagnitude < 0.04f)
            {
                continue;
            }

            AppendUnique(acc, seg[i]);
        }
    }

    private static void EnsureDistinctEndpoints(List<Vector3> path, Vector3 src, Vector3 dst)
    {
        if (path.Count < 2)
        {
            path.Clear();
            path.Add(src);
            path.Add((src - dst).sqrMagnitude > 1e-6f ? dst : src + Vector3.up * 0.2f);
            return;
        }

        if ((path[0] - path[^1]).sqrMagnitude < 1e-6f)
        {
            path.Clear();
            path.Add(src);
            path.Add(src + Vector3.up * 0.2f);
        }
    }

    private static void CollectAnchors(Transform root, List<Vector3> into)
    {
        into.Clear();
        if (root == null)
        {
            return;
        }

        into.Add(root.position);
        var children = root.GetComponentsInChildren<Transform>(true);
        var center = root.position;
        const float maxDistSq = 25f;
        for (var i = 0; i < children.Length && into.Count < 36; i++)
        {
            var tr = children[i];
            if (tr == root.transform)
            {
                continue;
            }

            if ((tr.position - center).sqrMagnitude > maxDistSq)
            {
                continue;
            }

            into.Add(tr.position);
        }
    }

    private static bool LooksLikeCableObjectName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        var n = name.ToLowerInvariant();
        return n.Contains("cable") || n.Contains("wire") || n.Contains("patch")
               || n.Contains("ethernet") || n.Contains("cord") || n.Contains("rj45")
               || n.Contains("fiber") || n.Contains("fibre") || n.Contains("pigtail")
               || n.Contains("connec") || n.Contains("plug") || n.Contains("jack")
               || n.Contains("strand") || n.Contains("lead") || n.Contains("utp")
               || n.Contains("stp") || (n.Contains("sfp") && n.Contains("cable"));
    }

    private static void CollectSceneLineRenderers(List<LineRenderer> list)
    {
        list.Clear();
        var all = Resources.FindObjectsOfTypeAll<LineRenderer>();
        if (all == null)
        {
            return;
        }

        foreach (var lr in all)
        {
            if (lr == null)
            {
                continue;
            }

            var go = lr.gameObject;
            if (!go.scene.IsValid() || !go.scene.isLoaded)
            {
                continue;
            }

            list.Add(lr);
        }
    }

    private static void AddAnchorBridgesToNearbyNodes(
        List<Vector3> anchors,
        List<Vector3> nodes,
        System.Func<Vector3, int> findOrAdd,
        System.Action<int, int> edge,
        float maxBridgeSq,
        int maxLinksPerAnchor)
    {
        if (anchors == null || anchors.Count == 0)
        {
            return;
        }

        foreach (var anchor in anchors)
        {
            var ai = findOrAdd(anchor);
            if (ai < 0)
            {
                continue;
            }

            var scored = new List<(float d, int idx)>(16);
            for (var i = 0; i < nodes.Count; i++)
            {
                if (i == ai)
                {
                    continue;
                }

                var d = (nodes[i] - anchor).sqrMagnitude;
                if (d > maxBridgeSq)
                {
                    continue;
                }

                scored.Add((d, i));
            }

            scored.Sort((a, b) => a.d.CompareTo(b.d));
            var n = Mathf.Min(maxLinksPerAnchor, scored.Count);
            for (var j = 0; j < n; j++)
            {
                edge(ai, scored[j].idx);
            }
        }
    }

    private static void MinDistToAnchorsSq(
        List<Vector3> nodes,
        List<Vector3> anchors,
        float maxDistSq,
        List<int> outIndices,
        HashSet<int> outSet)
    {
        if (anchors == null || anchors.Count == 0)
        {
            return;
        }

        for (var i = 0; i < nodes.Count; i++)
        {
            var m = float.MaxValue;
            foreach (var a in anchors)
            {
                var d = (nodes[i] - a).sqrMagnitude;
                if (d < m)
                {
                    m = d;
                }
            }

            if (m <= maxDistSq)
            {
                outIndices?.Add(i);
                outSet?.Add(i);
            }
        }
    }

    private static void AddKNearestNodeIndices(List<Vector3> nodes, Vector3 point, List<int> into, int k)
    {
        var scored = new List<(float d, int idx)>(nodes.Count);
        for (var i = 0; i < nodes.Count; i++)
        {
            scored.Add(((nodes[i] - point).sqrMagnitude, i));
        }

        scored.Sort((a, b) => a.d.CompareTo(b.d));
        var n = Mathf.Min(k, scored.Count);
        for (var j = 0; j < n; j++)
        {
            into.Add(scored[j].idx);
        }
    }

    private static Vector3 GetWorldPosition(LineRenderer lr, int index)
    {
        var p = lr.GetPosition(index);
        return lr.useWorldSpace ? p : lr.transform.TransformPoint(p);
    }

    private static void AppendUnique(List<Vector3> list, Vector3 v)
    {
        if (list.Count > 0 && (list[^1] - v).sqrMagnitude < 1e-6f)
        {
            return;
        }

        list.Add(v);
    }
}
