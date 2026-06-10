using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace DHCPSwitches;

/// <summary>
/// Discovers physical racks via game types <see cref="Rack"/>, <see cref="RackPosition"/>, and <see cref="RackMount"/>.
/// Il2Cpp stubs often expose no compile-time members; we read them with reflection at runtime.
/// </summary>
internal static class GameRackSceneScanner
{
    private static readonly string[] GridRowIntHints =
    {
        "row", "Row", "rowIndex", "RowIndex", "rackRow", "RackRow", "gridRow", "GridRow",
        "y", "Y", "gridY", "GridY", "posY", "PosY",
    };

    private static readonly string[] GridColumnIntHints =
    {
        "column", "Column", "col", "Col", "columnIndex", "ColumnIndex", "rackColumn", "RackColumn",
        "gridColumn", "GridColumn", "x", "X", "gridX", "GridX", "posX", "PosX",
    };

    private static readonly string[] GridIndexHints =
    {
        "index", "Index", "rackIndex", "RackIndex", "positionIndex", "PositionIndex", "slotIndex", "SlotIndex",
    };

    private static readonly string[] GridRowStringHints =
    {
        "rowLetter", "RowLetter", "gridRowLetter", "GridRowLetter", "rowName", "RowName", "letter", "Letter",
    };

    private static readonly string[] MountServerHints =
    {
        "server", "Server", "mountedServer", "MountedServer", "targetServer", "TargetServer", "device", "Device",
    };

    private static readonly string[] MountStartUIntHints =
    {
        "startU", "StartU", "rackUnit", "RackUnit", "unit", "Unit", "slot", "Slot", "slotIndex", "SlotIndex",
        "position", "Position", "positionU", "PositionU", "uStart", "UStart", "rackUnitStart", "RackUnitStart",
    };

    private static bool _reflectionDumped;

    internal static List<RackLayoutHelper.RackInfo> BuildFromSceneRacks()
    {
        var list = new List<RackLayoutHelper.RackInfo>();
        Rack[] racks;
        try
        {
            racks = UnityEngine.Object.FindObjectsOfType<Rack>(true);
        }
        catch
        {
            return list;
        }

        if (racks == null || racks.Length == 0)
        {
            return list;
        }

        RackMount[] allMounts = null;
        RackPosition[] allPositions = null;
        try
        {
            allMounts = UnityEngine.Object.FindObjectsOfType<RackMount>(true);
            allPositions = UnityEngine.Object.FindObjectsOfType<RackPosition>(true);
        }
        catch
        {
            // Il2Cpp
        }

        if (!_reflectionDumped)
        {
            DumpReflectionSample(racks[0], allPositions?.FirstOrDefault(), allMounts?.FirstOrDefault());
            _reflectionDumped = true;
        }

        foreach (var rack in racks)
        {
            if (rack == null)
            {
                continue;
            }

            Transform tr;
            try
            {
                tr = rack.transform;
            }
            catch
            {
                continue;
            }

            var pos = ResolveRackPosition(rack, tr, allPositions);
            TryReadGameGrid(rack, pos, tr, out var rowIndex, out var column, out var posLabel);
            var stableKey = DeviceStableId.ForRackTransform(tr);
            var display = BuildDisplayName(tr, rowIndex, column, posLabel);
            var devices = CollectDevices(rack, tr, allMounts);

            list.Add(
                new RackLayoutHelper.RackInfo
                {
                    Key = $"game-rack:{stableKey}",
                    StableKey = stableKey,
                    DisplayName = display,
                    TotalU = RackDataStore.RackStandardHeightU,
                    AnchorPosition = tr.position,
                    GameRowIndex = rowIndex,
                    GameColumn = column,
                    GamePositionLabel = posLabel,
                    Devices = devices,
                });
        }

        return list;
    }

    private static string BuildDisplayName(Transform tr, int? rowIndex, int? column, string posLabel)
    {
        if (rowIndex >= 0 && column is > 0 and <= 32)
        {
            return RackDataStore.FormatFloorGridLabel(rowIndex.Value, column.Value);
        }

        if (!string.IsNullOrWhiteSpace(posLabel))
        {
            return posLabel.Trim();
        }

        return string.IsNullOrWhiteSpace(tr?.name) ? "Game rack" : tr.name.Trim();
    }

    private static RackPosition ResolveRackPosition(Rack rack, Transform tr, RackPosition[] allPositions)
    {
        try
        {
            var onSelf = tr.GetComponent<RackPosition>();
            if (onSelf != null)
            {
                return onSelf;
            }

            var onParent = tr.GetComponentInParent<RackPosition>();
            if (onParent != null)
            {
                return onParent;
            }

            var onChild = tr.GetComponentInChildren<RackPosition>(true);
            if (onChild != null)
            {
                return onChild;
            }
        }
        catch
        {
            // Il2Cpp
        }

        if (allPositions == null)
        {
            return null;
        }

        RackPosition best = null;
        var bestDist = float.MaxValue;
        var p = tr.position;
        foreach (var candidate in allPositions)
        {
            if (candidate == null)
            {
                continue;
            }

            Transform ct;
            try
            {
                ct = candidate.transform;
            }
            catch
            {
                continue;
            }

            if (ct == tr || ct.IsChildOf(tr) || tr.IsChildOf(ct))
            {
                return candidate;
            }

            var d = (ct.position - p).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = candidate;
            }
        }

        return bestDist < 4f ? best : null;
    }

    private static void TryReadGameGrid(
        Rack rack,
        RackPosition pos,
        Transform tr,
        out int? rowIndex,
        out int? column,
        out string posLabel)
    {
        rowIndex = null;
        column = null;
        posLabel = null;
        var sources = new object[] { pos, rack, tr?.gameObject };
        foreach (var src in sources)
        {
            if (src == null)
            {
                continue;
            }

            if (!column.HasValue && TryReadFirstInt(src, GridColumnIntHints, out var col) && col is > 0 and <= 64)
            {
                column = col;
            }

            if (!rowIndex.HasValue)
            {
                if (TryReadFirstInt(src, GridRowIntHints, out var rowInt))
                {
                    if (rowInt is >= 0 and <= 15)
                    {
                        rowIndex = rowInt;
                    }
                    else if (rowInt is >= 1 and <= 16)
                    {
                        rowIndex = rowInt - 1;
                    }
                }
                else if (TryReadRowLetter(src, out var letterRow))
                {
                    rowIndex = letterRow;
                }
            }

            if (string.IsNullOrEmpty(posLabel) && TryReadFirstInt(src, GridIndexHints, out var idx))
            {
                posLabel = $"#{idx}";
            }
        }

        if (rowIndex.HasValue && column.HasValue)
        {
            posLabel = RackDataStore.FormatFloorGridLabel(rowIndex.Value, column.Value);
        }
        else if (!string.IsNullOrEmpty(posLabel))
        {
            // keep index label
        }
        else if (tr != null)
        {
            posLabel = tr.name;
        }
    }

    private static bool TryReadRowLetter(object o, out int rowIndex)
    {
        rowIndex = -1;
        foreach (var name in GridRowStringHints)
        {
            if (!TryReadStringMember(o, name, out var s) || string.IsNullOrWhiteSpace(s))
            {
                continue;
            }

            var c = char.ToUpperInvariant(s.Trim()[0]);
            if (c is >= 'A' and <= 'P')
            {
                rowIndex = c - 'A';
                return true;
            }
        }

        return false;
    }

    private static List<RackLayoutHelper.RackDeviceEntry> CollectDevices(Rack rack, Transform rackTr, RackMount[] allMounts)
    {
        var devices = new List<RackLayoutHelper.RackDeviceEntry>();
        var seen = new HashSet<int>();

        if (allMounts != null)
        {
            foreach (var mount in allMounts)
            {
                if (mount == null)
                {
                    continue;
                }

                Transform mt;
                try
                {
                    mt = mount.transform;
                }
                catch
                {
                    continue;
                }

                if (!IsUnderRack(mt, rackTr))
                {
                    continue;
                }

                if (!TryReadServerFromMount(mount, out var srv) || srv == null)
                {
                    continue;
                }

                if (!TryAddDevice(devices, seen, srv, mount, null))
                {
                    continue;
                }
            }
        }

        try
        {
            foreach (var srv in rackTr.GetComponentsInChildren<Server>(true))
            {
                TryAddDevice(devices, seen, srv, null, rackTr);
            }
        }
        catch
        {
            // Il2Cpp
        }

        devices.Sort(
            (a, b) =>
            {
                var pa = a.StartU > 0 ? a.StartU : int.MaxValue;
                var pb = b.StartU > 0 ? b.StartU : int.MaxValue;
                var c = pa.CompareTo(pb);
                return c != 0 ? c : string.CompareOrdinal(a.DisplayName ?? "", b.DisplayName ?? "");
            });
        return devices;
    }

    private static bool IsUnderRack(Transform t, Transform rackTr)
    {
        if (t == null || rackTr == null)
        {
            return false;
        }

        return t == rackTr || t.IsChildOf(rackTr) || rackTr.IsChildOf(t);
    }

    private static bool TryReadServerFromMount(RackMount mount, out Server srv)
    {
        srv = null;
        if (mount == null)
        {
            return false;
        }

        foreach (var name in MountServerHints)
        {
            if (!TryReadObjectMember(mount, name, out var obj) || obj == null)
            {
                continue;
            }

            if (obj is Server s)
            {
                srv = s;
                return true;
            }

            if (obj is Component c && TryGetServerFromComponent(c, out srv))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetServerFromComponent(Component c, out Server srv)
    {
        srv = null;
        if (c == null)
        {
            return false;
        }

        if (c is Server s)
        {
            srv = s;
            return true;
        }

        try
        {
            srv = c.GetComponent<Server>() ?? c.GetComponentInChildren<Server>(true);
            return srv != null;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryAddDevice(
        List<RackLayoutHelper.RackDeviceEntry> devices,
        HashSet<int> seen,
        Server srv,
        RackMount mount,
        Transform rackTr)
    {
        if (srv == null)
        {
            return false;
        }

        int iid;
        try
        {
            iid = srv.GetInstanceID();
        }
        catch
        {
            return false;
        }

        if (!seen.Add(iid))
        {
            return false;
        }

        var configured = GameSubnetHelper.TryGetServerAssetLineConfiguredDisplayName(srv, out var cfgNm)
            ? cfgNm.Trim()
            : null;
        var disp = DeviceInventoryReflection.GetDisplayName(srv);
        if (string.IsNullOrWhiteSpace(disp) && !string.IsNullOrWhiteSpace(configured))
        {
            disp = configured;
        }

        var startU = 0;
        if (mount != null && TryReadFirstInt(mount, MountStartUIntHints, out var su))
        {
            startU = su;
        }
        else if (rackTr != null)
        {
            startU = InferStartUFromLocalY(srv.transform, rackTr);
        }

        devices.Add(
            new RackLayoutHelper.RackDeviceEntry
            {
                Server = srv,
                StartU = startU,
                HeightU = RackLayoutHelper.InferServerRackHeightU(srv),
                DisplayName = string.IsNullOrWhiteSpace(disp) ? "Server" : disp.Trim(),
                ConfiguredName = configured,
                StableId = DeviceStableId.ForServer(srv),
            });
        return true;
    }

    private static int InferStartUFromLocalY(Transform srvTr, Transform rackTr)
    {
        try
        {
            var local = rackTr.InverseTransformPoint(srvTr.position);
            var u = Mathf.RoundToInt(local.y) + 1;
            if (u >= 1 && u <= 64)
            {
                return u;
            }
        }
        catch
        {
            // Il2Cpp
        }

        return 0;
    }

    private static void DumpReflectionSample(Rack rack, RackPosition pos, RackMount mount)
    {
        try
        {
            ModDebugLog.WriteLine("[DHCPSwitches Racks] Game rack reflection sample:");
            DumpTypeMembers(rack, "Rack");
            DumpTypeMembers(pos, "RackPosition");
            DumpTypeMembers(mount, "RackMount");
        }
        catch
        {
            // debug only
        }
    }

    private static void DumpTypeMembers(object o, string label)
    {
        if (o == null)
        {
            ModDebugLog.WriteLine($"  {label}: (none in scene)");
            return;
        }

        const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        ModDebugLog.WriteLine($"  {label} ({o.GetType().Name}):");
        foreach (var p in o.GetType().GetProperties(bf))
        {
            object v = null;
            try
            {
                v = p.GetValue(o);
            }
            catch
            {
                v = "?";
            }

            ModDebugLog.WriteLine($"    prop {p.PropertyType.Name} {p.Name} = {FormatDumpValue(v)}");
        }

        foreach (var f in o.GetType().GetFields(bf))
        {
            object v = null;
            try
            {
                v = f.GetValue(o);
            }
            catch
            {
                v = "?";
            }

            ModDebugLog.WriteLine($"    field {f.FieldType.Name} {f.Name} = {FormatDumpValue(v)}");
        }
    }

    private static string FormatDumpValue(object v)
    {
        if (v == null)
        {
            return "null";
        }

        if (v is string s)
        {
            return s;
        }

        if (v is UnityEngine.Object uo)
        {
            try
            {
                return uo.name ?? uo.GetType().Name;
            }
            catch
            {
                return uo.GetType().Name;
            }
        }

        if (v is IEnumerable en && v is not string)
        {
            var n = 0;
            foreach (var _ in en)
            {
                n++;
                if (n > 5)
                {
                    return $"IEnumerable({n}+)";
                }
            }

            return $"IEnumerable({n})";
        }

        return Convert.ToString(v, CultureInfo.InvariantCulture) ?? v.GetType().Name;
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
            if (TryReadIntMember(o, name, out value))
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

    private static bool TryReadStringMember(object o, string memberName, out string value)
    {
        value = null;
        if (o == null || string.IsNullOrEmpty(memberName))
        {
            return false;
        }

        for (var bt = o.GetType(); bt != null && bt != typeof(object); bt = bt.BaseType)
        {
            try
            {
                var p = bt.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.CanRead)
                {
                    var v = p.GetValue(o);
                    if (v != null)
                    {
                        value = v.ToString();
                        return !string.IsNullOrWhiteSpace(value);
                    }
                }

                var f = bt.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null)
                {
                    var v = f.GetValue(o);
                    if (v != null)
                    {
                        value = v.ToString();
                        return !string.IsNullOrWhiteSpace(value);
                    }
                }
            }
            catch
            {
                // Il2Cpp
            }
        }

        return false;
    }

    private static bool TryReadObjectMember(object o, string memberName, out object value)
    {
        value = null;
        if (o == null || string.IsNullOrEmpty(memberName))
        {
            return false;
        }

        for (var bt = o.GetType(); bt != null && bt != typeof(object); bt = bt.BaseType)
        {
            try
            {
                var p = bt.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.CanRead)
                {
                    value = p.GetValue(o);
                    return value != null;
                }

                var f = bt.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null)
                {
                    value = f.GetValue(o);
                    return value != null;
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
            case uint x when x <= int.MaxValue:
                i = (int)x;
                return true;
            case float f:
                i = Mathf.RoundToInt(f);
                return true;
            case double d:
                i = (int)Math.Round(d);
                return true;
            case char c when c >= 'A' && c <= 'P':
                i = char.ToUpperInvariant(c) - 'A';
                return true;
            case char c2 when c2 >= 'a' && c2 <= 'p':
                i = char.ToUpperInvariant(c2) - 'A';
                return true;
            default:
                return int.TryParse(v.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out i);
        }
    }
}
