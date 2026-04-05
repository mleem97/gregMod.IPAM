using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace DHCPSwitches;

/// <summary>
/// Best-effort display names, EOL text, and lifecycle actions via reflection (game DLL varies by version).
/// </summary>
internal static class DeviceInventoryReflection
{
    private static readonly string[] DisplayNameMembers =
    {
        "displayName", "DisplayName", "serverName", "ServerName", "deviceName", "DeviceName",
        "label", "Label", "itemName", "ItemName", "objectName", "ObjectName", "friendlyName", "FriendlyName",
        "hostName", "HostName", "hostname", "Hostname", "rackName", "RackName", "rackLabel", "RackLabel",
        "serverTitle", "ServerTitle", "title", "Title", "networkName", "NetworkName",
    };

    private static readonly string[] EolStringMembers =
    {
        "eolDisplay", "EOLDisplay", "eolString", "EOLString", "endOfLifeDisplay", "EndOfLifeDisplay",
    };

    private static readonly string[] EolTimeMembers =
    {
        "timeUntilEOL", "TimeUntilEOL", "eolTime", "EOLTime", "timeToEOL", "TimeToEOL", "remainingEOL", "RemainingEOL",
    };

    private static readonly string[] EolSecondsMembers =
    {
        "eolSeconds", "EOLSeconds", "secondsUntilEOL", "SecondsUntilEOL",
        "timeUntilEOLSeconds", "TimeUntilEOLSeconds", "eolRemaining", "EOLRemaining",
        "remainingEolSeconds", "RemainingEolSeconds", "eolCountdown", "EOLCountdown",
    };

    private static readonly Dictionary<Type, (PropertyInfo[] Props, FieldInfo[] Fields)> EolMemberScanCache = new();

    private static readonly string[] TechnicianMethods =
    {
        "SendTechnician", "DispatchTechnician", "CallTechnician", "RequestTechnician", "SendRepair", "RequestRepair",
    };

    private static readonly string[] ClearAlarmMethods =
    {
        "ClearAlarms", "ClearAlarm", "ResetAlarms", "ResetAlarm", "AcknowledgeAlarms", "SilenceAlarms",
    };

    internal static string GetDisplayName(UnityEngine.Object o)
    {
        if (o == null)
        {
            return "";
        }

        if (TryReadStringMember(o, DisplayNameMembers, out var s) && !string.IsNullOrWhiteSpace(s))
        {
            return StripCloneSuffix(s.Trim());
        }

        var baseName = StripCloneSuffix(o.name ?? "");
        if (o is Server srv)
        {
            return AppendServerInstanceDisambiguator(baseName, srv);
        }

        return baseName;
    }

    /// <summary>Prefab name is shared by every clone (e.g. Server.Yellow1); append Unity instance id like switch names in-scene.</summary>
    private static string AppendServerInstanceDisambiguator(string baseName, Server srv)
    {
        var id = srv != null ? srv.GetInstanceID() : 0;
        if (string.IsNullOrEmpty(baseName))
        {
            return id.ToString();
        }

        return $"{baseName}_{id}";
    }

    internal static string StripCloneSuffix(string n)
    {
        if (string.IsNullOrEmpty(n))
        {
            return "";
        }

        const string spaced = " (Clone)";
        if (n.EndsWith(spaced, StringComparison.Ordinal))
        {
            return n[..^spaced.Length].TrimEnd();
        }

        const string tight = "(Clone)";
        if (n.EndsWith(tight, StringComparison.Ordinal))
        {
            return n[..^tight.Length].TrimEnd();
        }

        return n;
    }

    internal static bool TryGetEolDisplay(UnityEngine.Object o, out string text)
    {
        text = null;
        if (o == null)
        {
            return false;
        }

        if (TryReadStringMember(o, EolStringMembers, out var str) && !string.IsNullOrWhiteSpace(str))
        {
            text = str.Trim();
            return true;
        }

        if (TryReadTimeSpanMember(o, EolTimeMembers, out var ts))
        {
            text = FormatTimeRemaining(ts);
            return !string.IsNullOrEmpty(text);
        }

        if (TryReadSingleNumericSeconds(o, EolSecondsMembers, out var sec))
        {
            text = FormatSeconds(sec);
            return true;
        }

        if (TryReadIl2CppTimeSpanNamed(o, EolTimeMembers, out var tsIl))
        {
            text = FormatTimeRemaining(tsIl);
            return !string.IsNullOrEmpty(text);
        }

        if (TryGetEolFromMemberNameScan(o, out var scanned))
        {
            text = scanned;
            return true;
        }

        return false;
    }

    internal static bool AppearsPastOrCriticalEol(UnityEngine.Object o)
    {
        if (o == null)
        {
            return false;
        }

        if (TryReadTimeSpanMember(o, EolTimeMembers, out var ts))
        {
            return ts <= TimeSpan.Zero;
        }

        if (TryReadSingleNumericSeconds(o, EolSecondsMembers, out var sec))
        {
            return sec <= 0f;
        }

        if (TryReadStringMember(o, EolStringMembers, out var s) && !string.IsNullOrWhiteSpace(s))
        {
            var t = s.Trim();
            if (t.StartsWith("0:", StringComparison.Ordinal) || t.StartsWith("-", StringComparison.Ordinal))
            {
                return true;
            }

            if (t.Contains("EOL", StringComparison.OrdinalIgnoreCase) && t.Contains("0:00", StringComparison.Ordinal))
            {
                return true;
            }
        }

        if (TryReadIl2CppTimeSpanNamed(o, EolTimeMembers, out var tsIl))
        {
            return tsIl <= TimeSpan.Zero;
        }

        if (TryGetEolFromMemberNameScan(o, out var disp))
        {
            return IsExpiredEolDisplayText(disp);
        }

        return false;
    }

    private static bool IsExpiredEolDisplayText(string disp)
    {
        if (string.IsNullOrEmpty(disp))
        {
            return false;
        }

        var t = disp.Trim();
        return string.Equals(t, "EOL", StringComparison.OrdinalIgnoreCase)
               || t.StartsWith("0:", StringComparison.Ordinal)
               || t.StartsWith("-", StringComparison.Ordinal);
    }

    internal static bool TrySendTechnician(UnityEngine.Object o) => TryInvokeNoArgs(o, TechnicianMethods);

    internal static bool TryClearAlarms(UnityEngine.Object o) => TryInvokeNoArgs(o, ClearAlarmMethods);

    private static bool TryInvokeNoArgs(UnityEngine.Object o, string[] methodNames)
    {
        if (o == null)
        {
            return false;
        }

        var t = o.GetType();
        for (var bt = t; bt != null; bt = bt.BaseType)
        {
            foreach (var name in methodNames)
            {
                try
                {
                    var m = bt.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                    if (m == null)
                    {
                        continue;
                    }

                    m.Invoke(o, null);
                    return true;
                }
                catch
                {
                    // try next
                }
            }
        }

        return false;
    }

    private static bool TryReadStringMember(object o, string[] names, out string value)
    {
        value = null;
        var t = o.GetType();
        for (var bt = t; bt != null; bt = bt.BaseType)
        {
            foreach (var name in names)
            {
                try
                {
                    var p = bt.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (p?.GetValue(o) is string s)
                    {
                        value = s;
                        return true;
                    }

                    var f = bt.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (f?.GetValue(o) is string s2)
                    {
                        value = s2;
                        return true;
                    }
                }
                catch
                {
                    // Il2Cpp
                }
            }
        }

        return false;
    }

    private static bool TryReadTimeSpanMember(object o, string[] names, out TimeSpan ts)
    {
        ts = default;
        var t = o.GetType();
        for (var bt = t; bt != null; bt = bt.BaseType)
        {
            foreach (var name in names)
            {
                try
                {
                    var p = bt.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var pv = p?.GetValue(o);
                    if (pv != null && TryConvertToSystemTimeSpan(pv, out ts))
                    {
                        return true;
                    }

                    var fld = bt.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var fv = fld?.GetValue(o);
                    if (fv != null && TryConvertToSystemTimeSpan(fv, out ts))
                    {
                        return true;
                    }
                }
                catch
                {
                    // ignore
                }
            }
        }

        return false;
    }

    private static bool TryReadSingleNumericSeconds(object o, string[] names, out float seconds)
    {
        seconds = 0f;
        var t = o.GetType();
        for (var bt = t; bt != null; bt = bt.BaseType)
        {
            foreach (var name in names)
            {
                try
                {
                    var p = bt.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (TryConvertToFloat(p?.GetValue(o), out seconds))
                    {
                        return true;
                    }

                    var f = bt.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (TryConvertToFloat(f?.GetValue(o), out seconds))
                    {
                        return true;
                    }
                }
                catch
                {
                    // ignore
                }
            }
        }

        return false;
    }

    private static bool TryConvertToFloat(object v, out float f)
    {
        f = 0f;
        if (v == null)
        {
            return false;
        }

        switch (v)
        {
            case float x:
                f = x;
                return true;
            case double x:
                f = (float)x;
                return true;
            case int x:
                f = x;
                return true;
            case long x:
                f = x;
                return true;
            default:
                return false;
        }
    }

    private static string FormatTimeRemaining(TimeSpan ts)
    {
        if (ts < TimeSpan.Zero)
        {
            return "EOL";
        }

        if (ts.TotalDays >= 1d)
        {
            return $"{(int)ts.TotalDays}d {ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        return ts.Hours > 0
            ? $"{ts.Hours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    private static string FormatSeconds(float sec)
    {
        if (sec <= 0f)
        {
            return "EOL";
        }

        var ts = TimeSpan.FromSeconds(sec);
        return FormatTimeRemaining(ts);
    }

    private static bool TryReadIl2CppTimeSpanNamed(object o, string[] names, out TimeSpan ts)
    {
        ts = default;
        if (o == null)
        {
            return false;
        }

        var t = o.GetType();
        for (var bt = t; bt != null; bt = bt.BaseType)
        {
            foreach (var name in names)
            {
                try
                {
                    var p = bt.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var pv = p?.GetValue(o);
                    if (pv != null && TryConvertToSystemTimeSpan(pv, out ts))
                    {
                        return true;
                    }

                    var f = bt.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var fv = f?.GetValue(o);
                    if (fv != null && TryConvertToSystemTimeSpan(fv, out ts))
                    {
                        return true;
                    }
                }
                catch
                {
                    // Il2Cpp
                }
            }
        }

        return false;
    }

    private static bool TryConvertToSystemTimeSpan(object pv, out TimeSpan ts)
    {
        ts = default;
        if (pv == null)
        {
            return false;
        }

        switch (pv)
        {
            case TimeSpan tsm:
                ts = tsm;
                return true;
            case float f:
                ts = TimeSpan.FromSeconds(f);
                return true;
            case double d:
                ts = TimeSpan.FromSeconds(d);
                return true;
        }

        var vt = pv.GetType();
        var fn = vt.FullName ?? "";
        if (fn.IndexOf("TimeSpan", StringComparison.Ordinal) < 0)
        {
            return false;
        }

        foreach (var pn in new[] { "TotalSeconds", "totalSeconds", "TotalMilliseconds", "totalMilliseconds" })
        {
            try
            {
                var tp = vt.GetProperty(
                    pn,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var v = tp?.GetValue(pv);
                if (v is double d)
                {
                    ts = pn.IndexOf("Millisecond", StringComparison.OrdinalIgnoreCase) >= 0
                        ? TimeSpan.FromMilliseconds(d)
                        : TimeSpan.FromSeconds(d);
                    return true;
                }

                if (v is float fl)
                {
                    ts = pn.IndexOf("Millisecond", StringComparison.OrdinalIgnoreCase) >= 0
                        ? TimeSpan.FromMilliseconds(fl)
                        : TimeSpan.FromSeconds(fl);
                    return true;
                }

                if (v is long lg)
                {
                    ts = pn.IndexOf("Tick", StringComparison.OrdinalIgnoreCase) >= 0
                        ? TimeSpan.FromTicks(lg)
                        : TimeSpan.FromSeconds(lg);
                    return true;
                }

                if (v is int ig)
                {
                    ts = TimeSpan.FromSeconds(ig);
                    return true;
                }
            }
            catch
            {
                // ignore
            }
        }

        return false;
    }

    private static void EnsureEolMemberScan(Type rootType)
    {
        if (rootType == null || EolMemberScanCache.ContainsKey(rootType))
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var props = new List<PropertyInfo>();
        var fields = new List<FieldInfo>();
        for (var bt = rootType; bt != null && bt != typeof(object); bt = bt.BaseType)
        {
            foreach (var p in bt.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (p.GetIndexParameters().Length != 0 || !p.CanRead)
                {
                    continue;
                }

                var nm = p.Name;
                if (nm.IndexOf("eol", StringComparison.OrdinalIgnoreCase) < 0
                    && nm.IndexOf("endoflife", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if (!seen.Add("P:" + nm))
                {
                    continue;
                }

                props.Add(p);
            }

            foreach (var f in bt.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var nm = f.Name;
                if (nm.IndexOf("eol", StringComparison.OrdinalIgnoreCase) < 0
                    && nm.IndexOf("endoflife", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if (!seen.Add("F:" + nm))
                {
                    continue;
                }

                fields.Add(f);
            }
        }

        EolMemberScanCache[rootType] = (props.ToArray(), fields.ToArray());
    }

    private static bool TryGetEolFromMemberNameScan(object o, out string text)
    {
        text = null;
        if (o == null)
        {
            return false;
        }

        var rt = o.GetType();
        EnsureEolMemberScan(rt);
        if (!EolMemberScanCache.TryGetValue(rt, out var pack))
        {
            return false;
        }

        foreach (var p in pack.Props)
        {
            try
            {
                var v = p.GetValue(o);
                if (TryFormatEolCandidateValue(v, out text))
                {
                    return true;
                }
            }
            catch
            {
                // Il2Cpp
            }
        }

        foreach (var f in pack.Fields)
        {
            try
            {
                var v = f.GetValue(o);
                if (TryFormatEolCandidateValue(v, out text))
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

    private static bool TryFormatEolCandidateValue(object v, out string text)
    {
        text = null;
        if (v == null)
        {
            return false;
        }

        switch (v)
        {
            case string s:
                if (string.IsNullOrWhiteSpace(s))
                {
                    return false;
                }

                text = s.Trim();
                return true;
            case TimeSpan ts:
                text = FormatTimeRemaining(ts);
                return !string.IsNullOrEmpty(text);
        }

        if (TryConvertToSystemTimeSpan(v, out var ts2))
        {
            text = FormatTimeRemaining(ts2);
            return !string.IsNullOrEmpty(text);
        }

        if (TryConvertToFloat(v, out var sec))
        {
            text = FormatSeconds(sec);
            return true;
        }

        return false;
    }
}
