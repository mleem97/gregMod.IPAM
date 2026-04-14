using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DHCPSwitches;

/// <summary>
/// Stable key for a rack device across game sessions (unlike <see cref="UnityEngine.Object.GetInstanceID"/>).
/// Uses scene name, hierarchy path (name + sibling index per level), and quantized world position.
/// </summary>
internal static class DeviceStableId
{
    private const int PositionDecimals = 2;

    internal static string ForNetworkSwitch(NetworkSwitch sw)
    {
        if (sw == null)
        {
            return "null";
        }

        var tr = sw.transform;
        var scene = tr.gameObject.scene;
        var scenePart = scene.IsValid() ? scene.name : "invalid_scene";
        var path = HierarchyPath(tr);
        var p = tr.position;
        var pos = $"{Quant(p.x)},{Quant(p.y)},{Quant(p.z)}";
        return $"{scenePart}|{path}|{pos}";
    }

    private static string Quant(float f) => Math.Round(f, PositionDecimals).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string HierarchyPath(Transform tr)
    {
        var stack = new List<(string name, int idx)>(8);
        for (var t = tr; t != null; t = t.parent)
        {
            stack.Add((t.name ?? "", t.GetSiblingIndex()));
        }

        var sb = new StringBuilder(64);
        for (var i = stack.Count - 1; i >= 0; i--)
        {
            if (i < stack.Count - 1)
            {
                sb.Append('/');
            }

            var (name, idx) = stack[i];
            sb.Append(name);
            sb.Append('@');
            sb.Append(idx);
        }

        return sb.ToString();
    }
}
