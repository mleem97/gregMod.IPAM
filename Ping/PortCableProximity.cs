using System.Collections.Generic;
using UnityEngine;

namespace DHCPSwitches;

/// <summary>
/// Samples scene cable geometry so we can infer "plugged" when the game does not expose port link flags.
/// </summary>
internal static class PortCableProximity
{
    private const float CacheSeconds = 0.22f;
    private const int MaxPoints = 12000;

    private static float _cacheUntil;
    private static readonly List<Vector3> Points = new(4096);

    internal static void InvalidateCache()
    {
        _cacheUntil = 0f;
    }

    internal static bool AnyVertexWithin(Vector3 world, float radiusMeters)
    {
        if (radiusMeters <= 0f)
        {
            return false;
        }

        RebuildIfStale();
        var r2 = radiusMeters * radiusMeters;
        for (var i = 0; i < Points.Count; i++)
        {
            if ((Points[i] - world).sqrMagnitude <= r2)
            {
                return true;
            }
        }

        return false;
    }

    private static void RebuildIfStale()
    {
        var t = Time.realtimeSinceStartup;
        if (t < _cacheUntil && Points.Count > 0)
        {
            return;
        }

        _cacheUntil = t + CacheSeconds;
        Points.Clear();

        var all = Resources.FindObjectsOfTypeAll<LineRenderer>();
        if (all == null)
        {
            return;
        }

        foreach (var lr in all)
        {
            if (lr == null || lr.positionCount < 2)
            {
                continue;
            }

            var go = lr.gameObject;
            if (!go.scene.IsValid() || !go.scene.isLoaded)
            {
                continue;
            }

            var n = lr.positionCount;
            var step = n > 400 ? Mathf.Max(1, n / 200) : 1;
            for (var i = 0; i < n && Points.Count < MaxPoints; i += step)
            {
                var p = lr.GetPosition(i);
                Points.Add(lr.useWorldSpace ? p : lr.transform.TransformPoint(p));
            }
        }
    }
}
