using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace DHCPSwitches;

/// <summary>
/// When <c>DHCPSwitches-setip.flag</c> exists next to the game (see <see cref="ModDebugLog"/>), dumps the SetIP keypad hierarchy
/// once per opening to <c>DHCPSwitches-debug.log</c> so you can find the real GameObject path and component types for "Paste".
/// </summary>
internal static class SetIpKeypadDiagnostics
{
    private static bool _dumpedForCurrentOpen;

    internal static void MaybeDumpWhileKeypadOpen(SetIP setIp)
    {
        if (!ModDebugLog.IsSetIpKeypadInspectEnabled || setIp == null)
        {
            return;
        }

        // Match DHCP spawn visibility: keypad can be shown while SetIP.isActive stays false.
        if (setIp.gameObject == null || !setIp.gameObject.activeInHierarchy)
        {
            _dumpedForCurrentOpen = false;
            return;
        }

        if (_dumpedForCurrentOpen)
        {
            return;
        }

        _dumpedForCurrentOpen = true;
        ModDebugLog.Bootstrap();
        ModDebugLog.WriteLine("=== SetIP keypad inspect (DHCPSwitches-setip.flag) ===");
        DumpHideableButtonTextField(setIp);
        DumpRoots(setIp);
        ModDebugLog.WriteLine("=== end SetIP keypad inspect ===");
    }

    private static void DumpHideableButtonTextField(SetIP setIp)
    {
        var p = typeof(SetIP).GetProperty("hideableButtonText", BindingFlags.Public | BindingFlags.Instance);
        var h = p?.GetValue(setIp);
        if (h is not Component comp)
        {
            ModDebugLog.WriteLine("SetIP.hideableButtonText: null or not a Component");
            return;
        }

        var path = comp.transform != null ? BuildPath(comp.transform) : "(no transform)";
        var txt = TryReadAnyTextLikeString(comp);
        ModDebugLog.WriteLine($"SetIP.hideableButtonText: {comp.GetType().Name} path={path} text=\"{txt ?? ""}\"");
    }

    private static void DumpRoots(SetIP setIp)
    {
        var roots = new List<GameObject>(2);
        if (setIp.gameObject != null)
        {
            roots.Add(setIp.gameObject);
        }

        if (setIp.canvas != null && !ReferenceEquals(setIp.canvas, setIp.gameObject))
        {
            roots.Add(setIp.canvas);
        }

        var seen = new HashSet<int>();
        foreach (var root in roots)
        {
            ModDebugLog.WriteLine($"-- root: {root.name} (id={root.GetInstanceID()})");
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || !seen.Add(t.GetInstanceID()))
                {
                    continue;
                }

                TryLogTransformLine(t);
            }
        }
    }

    private static void TryLogTransformLine(Transform t)
    {
        var path = BuildPath(t);
        var sb = new StringBuilder();
        sb.Append(path);
        sb.Append(" | ");

        var btn = t.GetComponent<Button>();
        if (btn != null)
        {
            sb.Append("Button ");
        }

        var comps = t.GetComponents<Component>();
        foreach (var c in comps)
        {
            if (c == null)
            {
                continue;
            }

            var tn = c.GetType().Name;
            if (tn == "Transform" || tn == "RectTransform")
            {
                continue;
            }

            sb.Append(tn);
            sb.Append(':');

            var txt = TryReadAnyTextLikeString(c);
            if (txt != null)
            {
                sb.Append('\"');
                sb.Append(txt.Length > 80 ? txt[..80] + "…" : txt);
                sb.Append('\"');
            }

            sb.Append(';');
        }

        ModDebugLog.WriteLine(sb.ToString());
    }

    private static string TryReadAnyTextLikeString(Component c)
    {
        for (var bt = c.GetType(); bt != null && bt != typeof(Component) && bt != typeof(object); bt = bt.BaseType)
        {
            foreach (var p in bt.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (!string.Equals(p.Name, "text", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!p.CanRead)
                {
                    continue;
                }

                try
                {
                    var v = p.GetValue(c);
                    return v?.ToString();
                }
                catch
                {
                    // ignore
                }
            }
        }

        return null;
    }

    private static string BuildPath(Transform t)
    {
        if (t == null)
        {
            return "";
        }

        var stack = new Stack<string>();
        for (var x = t; x != null; x = x.parent)
        {
            stack.Push(x.name);
        }

        return string.Join("/", stack);
    }
}
