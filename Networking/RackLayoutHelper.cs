using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DHCPSwitches;

/// <summary>
/// Scene rack layout derived from <see cref="AssetManagementDeviceLine"/> rows (customer rack / asset UI).
/// Unit positions and rack height are read via reflection when the game exposes them.
/// </summary>
public static class RackLayoutHelper
{
    public sealed class RackDeviceEntry
    {
        public Server Server;
        /// <summary>Bottom-origin RU index (1 = lowest slot). 0 if not reported by the game.</summary>
        public int StartU;
        public int HeightU;
        public string DisplayName;
    }

    public sealed class RackInfo
    {
        public string Key;
        public string DisplayName;
        public int TotalU;
        public List<RackDeviceEntry> Devices;
    }

    private static readonly Regex DigitBeforeURx = new(
        @"(\d+)\s*U\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly string[] RackStartUIntHints =
    {
        "rackUnitStart", "RackUnitStart", "unitStart", "UnitStart", "startUnit", "StartUnit",
        "rackPosition", "RackPosition", "positionU", "PositionU", "uStart", "UStart", "bottomUnit",
        "BottomUnit", "firstUnit", "FirstUnit", "rackSlot", "RackSlot", "slotIndex", "SlotIndex",
        "ruStart", "RuStart", "RUStart", "unitIndex", "UnitIndex", "linePosition", "LinePosition",
    };

    private static readonly string[] RackTotalUIntHints =
    {
        "totalUnits", "TotalUnits", "rackUnitsTotal", "RackUnitsTotal", "unitCount", "UnitCount",
        "sizeU", "SizeU", "heightU", "HeightU", "rackHeight", "RackHeight", "units", "Units",
        "totalU", "TotalU", "maxUnits", "MaxUnits",
    };

    /// <summary>Rebuilds rack snapshots from scene asset-management rows (O(n) scan).</summary>
    public static void BuildSceneRackLayout(out List<RackInfo> racks)
    {
        racks = new List<RackInfo>();
        GameSubnetHelper.RebuildAssetManagementDeviceLineServerCache();

        var byKey = new Dictionary<string, List<RackDeviceEntry>>(StringComparer.Ordinal);
        var rackMeta = new Dictionary<string, (string Name, int TotalU)>(StringComparer.Ordinal);

        try
        {
            var lines = UnityEngine.Object.FindObjectsOfType<AssetManagementDeviceLine>(true);
            if (lines == null)
            {
                return;
            }

            foreach (var line in lines)
            {
                if (line == null)
                {
                    continue;
                }

                Server srv = null;
                try
                {
                    srv = line.server;
                }
                catch
                {
                    srv = null;
                }

                if (srv == null)
                {
                    continue;
                }

                var key = InferRackKey(line);
                if (!rackMeta.TryGetValue(key, out var rm))
                {
                    var nm = InferRackDisplayName(line);
                    var tu = InferTotalU(line) ?? 42;
                    tu = Mathf.Clamp(tu, 1, 64);
                    rackMeta[key] = (nm, tu);
                }

                if (!byKey.TryGetValue(key, out var list))
                {
                    list = new List<RackDeviceEntry>();
                    byKey[key] = list;
                }

                var iid = SafeInstanceId(srv);
                if (list.Any(x => x.Server != null && SafeInstanceId(x.Server) == iid))
                {
                    continue;
                }

                var disp = DeviceInventoryReflection.GetDisplayName(srv);
                if (string.IsNullOrWhiteSpace(disp) && GameSubnetHelper.TryGetServerAssetLineConfiguredDisplayName(srv, out var rackNm))
                {
                    disp = rackNm;
                }

                if (string.IsNullOrWhiteSpace(disp))
                {
                    disp = "Server";
                }

                var h = InferServerHeightU(srv);
                var su = TryInferStartU(line, srv);

                list.Add(
                    new RackDeviceEntry
                    {
                        Server = srv,
                        StartU = su,
                        HeightU = h,
                        DisplayName = disp.Trim(),
                    });
            }
        }
        catch
        {
            // Il2Cpp / type mismatch
        }

        foreach (var kv in byKey)
        {
            if (!rackMeta.TryGetValue(kv.Key, out var m))
            {
                m = ("Rack", 42);
            }

            kv.Value.Sort(
                (a, b) =>
                {
                    var pa = a.StartU > 0 ? a.StartU : int.MaxValue;
                    var pb = b.StartU > 0 ? b.StartU : int.MaxValue;
                    var c = pa.CompareTo(pb);
                    return c != 0 ? c : string.CompareOrdinal(a.DisplayName ?? "", b.DisplayName ?? "");
                });

            racks.Add(
                new RackInfo
                {
                    Key = kv.Key,
                    DisplayName = m.Name,
                    TotalU = m.TotalU,
                    Devices = kv.Value,
                });
        }

        racks.Sort((a, b) => string.CompareOrdinal(a.DisplayName, b.DisplayName));
    }

    private static int SafeInstanceId(Server s)
    {
        try
        {
            return s != null ? s.GetInstanceID() : 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Rack unit height from catalog / reflection (typically 3 U or 7 U).</summary>
    public static int InferServerRackHeightU(Server srv)
    {
        return InferServerHeightU(srv);
    }

    private static int InferServerHeightU(Server srv)
    {
        var lab = DeviceInventoryReflection.GetServerFormFactorLabel(srv);
        if (TryParseDigitBeforeU(lab, out var u))
        {
            return Mathf.Clamp(u, 1, 32);
        }

        return 1;
    }

    private static bool TryParseDigitBeforeU(string s, out int u)
    {
        u = 1;
        if (string.IsNullOrWhiteSpace(s))
        {
            return false;
        }

        var m = DigitBeforeURx.Match(s.Trim());
        if (!m.Success)
        {
            return false;
        }

        return int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out u)
               && u > 0;
    }

    private static int TryInferStartU(AssetManagementDeviceLine line, Server srv)
    {
        if (TryReadFirstInt(line, RackStartUIntHints, out var u) && u > 0 && u <= 64)
        {
            return u;
        }

        if (TryReadFirstInt(srv, RackStartUIntHints, out var u2) && u2 > 0 && u2 <= 64)
        {
            return u2;
        }

        return 0;
    }

    private static int? InferTotalU(AssetManagementDeviceLine line)
    {
        Transform t = null;
        try
        {
            t = line.transform;
        }
        catch
        {
            t = null;
        }

        while (t != null)
        {
            if (TryReadFirstInt(t.gameObject, RackTotalUIntHints, out var n) && n > 0 && n <= 64)
            {
                return n;
            }

            t = t.parent;
        }

        return null;
    }

    private static string InferRackKey(AssetManagementDeviceLine line)
    {
        Transform t = null;
        try
        {
            t = line.transform;
        }
        catch
        {
            return "rack:unknown";
        }

        while (t != null)
        {
            var nm = t.name ?? "";
            if (nm.IndexOf("rack", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                try
                {
                    return $"rack:{nm}:{t.GetInstanceID()}";
                }
                catch
                {
                    return $"rack:{nm}";
                }
            }

            t = t.parent;
        }

        try
        {
            var root = line.transform.root;
            var rn = root != null ? root.name ?? "" : "";
            var id = root != null ? root.GetInstanceID() : 0;
            return $"scene:{rn}:{id}";
        }
        catch
        {
            try
            {
                return $"line:{line.GetInstanceID()}";
            }
            catch
            {
                return "rack:unknown";
            }
        }
    }

    private static string InferRackDisplayName(AssetManagementDeviceLine line)
    {
        Transform t = null;
        try
        {
            t = line.transform;
        }
        catch
        {
            return "Rack";
        }

        while (t != null)
        {
            var nm = t.name ?? "";
            if (nm.IndexOf("rack", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return string.IsNullOrWhiteSpace(nm) ? "Rack" : nm.Trim();
            }

            t = t.parent;
        }

        try
        {
            var root = line.transform.root;
            var rn = root != null ? root.name : "";
            return string.IsNullOrWhiteSpace(rn) ? "Rack" : rn.Trim();
        }
        catch
        {
            return "Rack";
        }
    }

    private static bool TryReadFirstInt(object o, string[] names, out int value)
    {
        value = 0;
        if (o == null || names == null)
        {
            return false;
        }

        foreach (var name in names)
        {
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            if (TryReadIntMember(o, name, out value) && value > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryReadIntMember(object o, string memberName, out int value)
    {
        value = 0;
        if (o == null || string.IsNullOrEmpty(memberName))
        {
            return false;
        }

        for (var bt = o.GetType(); bt != null && bt != typeof(object); bt = bt.BaseType)
        {
            try
            {
                var p = bt.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.CanRead && TryConvertToInt32(p.GetValue(o), out value))
                {
                    return true;
                }

                var f = bt.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null && TryConvertToInt32(f.GetValue(o), out value))
                {
                    return true;
                }
            }
            catch
            {
                // Il2Cpp
            }
        }

        return false;
    }

    private static bool TryConvertToInt32(object v, out int i)
    {
        i = 0;
        if (v == null)
        {
            return false;
        }

        switch (v)
        {
            case int x:
                i = x;
                return true;
            case long x:
                i = (int)x;
                return true;
            case short x:
                i = x;
                return true;
            case byte x:
                i = x;
                return true;
            case uint x:
                if (x <= int.MaxValue)
                {
                    i = (int)x;
                    return true;
                }

                return false;
            case float f:
                i = Mathf.RoundToInt(f);
                return true;
            case double d:
                i = (int)Math.Round(d);
                return true;
            default:
                return int.TryParse(v.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out i);
        }
    }
}
