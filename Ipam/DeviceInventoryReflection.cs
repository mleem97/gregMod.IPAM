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

    /// <summary>Exact names only for technician — broad heuristics were invoking unrelated &quot;service&quot; / EOL helpers.</summary>
    private static readonly string[] TechnicianMethods =
    {
        "SendTechnician", "DispatchTechnician", "CallTechnician", "RequestTechnician",
        "SendRepair", "RequestRepair", "OrderTechnician", "HireTechnician", "SpawnTechnician", "DispatchRepair",
        "StartTechnicianDispatch", "BeginTechnicianVisit", "TriggerTechnician", "RequestOnsiteTechnician",
        "DispatchOnsiteRepair", "SpawnRepairTechnician", "StartPhysicalRepair", "BeginRepairVisit",
        "StartRepair", "RequestMaintenance", "CallMaintenance", "SummonRepair", "RepairDevice",
        "FixDevice", "RequestService", "CallForService", "OrderRepair",
    };

    private static readonly string[] ClearAlarmMethods =
    {
        "ClearAlarms", "ClearAlarm", "ResetAlarms", "ResetAlarm", "AcknowledgeAlarms", "SilenceAlarms",
        "DismissAlarms", "AckAlarms", "ClearAllAlarms", "SilenceAllAlarms", "ResetAllAlarms",
        "ClearFaults", "ResetFaults", "AcknowledgeAlarm", "DismissAlarm", "SilenceAlarm",
        "ClearIssues", "DismissIssues", "ClearProblems", "ResetErrors", "ClearNotifications",
        "DismissAllAlarms", "ClearDeviceAlarms",
        "ClearWarnings", "DismissWarnings", "ClearAlerts", "RemoveAlarms", "ResolveAlarms",
        "MuteAlarms", "SuppressAlarms", "ClearIncident", "ClearIncidents",
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

    internal static bool TrySendTechnician(UnityEngine.Object o)
    {
        if (o == null)
        {
            return false;
        }

        if (GameTechnicianDispatch.TryDispatch(o, out var gameDetail))
        {
            LogLifecycleInvoke("Send technician", gameDetail);
            return true;
        }

        ModLogging.Warning(
            "[DHCPSwitches IPAM] Send technician: no game dispatch path succeeded (device line → TechnicianManager → AssetManagement). "
            + "Check MelonLoader/Latest.log next to the game, and DHCPSwitches-debug.log in the folder above *_Data (if created).");

        return false;
    }

    internal static bool TryClearAlarms(UnityEngine.Object o)
    {
        if (o == null)
        {
            return false;
        }

        // Try every plausible component on the device — the first match may be a no-op stub while a child owns real alarm state.
        var anyDeviceInvoke = false;
        foreach (var target in EnumerateAlarmTargets(o))
        {
            var allowHeuristic = target is Server or NetworkSwitch
                || (target is Component c && PlausibleAlarmSubsystemType(c.GetType()));
            if (TryInvokeLifecycleOnTarget(
                    target,
                    ClearAlarmMethods,
                    allowNameHeuristic: allowHeuristic,
                    nameHeuristic: IsLikelyClearAlarmMethodName,
                    logLabel: "Clear alarms"))
            {
                anyDeviceInvoke = true;
            }
        }

        if (anyDeviceInvoke)
        {
            return true;
        }

        return TryInvokeOnSceneFacadesOneArg(o, ClearAlarmMethods, "Clear alarms");
    }

    private static void LogLifecycleInvoke(string label, string detail)
    {
        ModLogging.Msg(
            $"[DHCPSwitches IPAM] {label}: {detail} — if the visible effect is wrong, the game may use a different API; check the game assembly for this type.");
    }

    private static bool TryInvokeLifecycleOnTarget(
        UnityEngine.Object o,
        string[] explicitNames,
        bool allowNameHeuristic,
        Func<string, bool> nameHeuristic,
        string logLabel)
    {
        if (TryAccessToolsInvokeNoArgs(o, explicitNames, out var dAccess))
        {
            LogLifecycleInvoke(logLabel, dAccess);
            return true;
        }

        if (TryInvokeNoArgsByNameList(o, explicitNames, out var d0))
        {
            LogLifecycleInvoke(logLabel, d0);
            return true;
        }

        if (TryAccessToolsInvokeSingleBool(o, explicitNames, out var dAccessB))
        {
            LogLifecycleInvoke(logLabel, dAccessB);
            return true;
        }

        if (TryInvokeSingleBoolByNameList(o, explicitNames, out var d1))
        {
            LogLifecycleInvoke(logLabel, d1);
            return true;
        }

        if (allowNameHeuristic
            && nameHeuristic != null
            && TryInvokeFirstZeroArgMethodMatching(o, nameHeuristic, out var d2))
        {
            LogLifecycleInvoke(logLabel, d2 + " [name heuristic]");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Walks the instance type chain and matches declared methods only — avoids Harmony <c>AccessTools.Method</c>,
    /// which logs a console error on every miss (very noisy during IPAM technician / alarm probes).
    /// </summary>
    private static MethodInfo TryResolveInstanceMethodOnHierarchy(Type startType, string methodName, Type[] parameterTypes)
    {
        if (startType == null || string.IsNullOrEmpty(methodName) || parameterTypes == null)
        {
            return null;
        }

        for (var t = startType; t != null && t != typeof(object); t = t.BaseType)
        {
            try
            {
                foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (m.IsStatic || !string.Equals(m.Name, methodName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var ps = m.GetParameters();
                    if (ps.Length != parameterTypes.Length)
                    {
                        continue;
                    }

                    var sigOk = true;
                    for (var i = 0; i < ps.Length; i++)
                    {
                        var pt = ps[i].ParameterType;
                        var exp = parameterTypes[i];
                        if (exp == typeof(bool) && IsBoolLikeParameterType(pt))
                        {
                            continue;
                        }

                        if (pt != exp)
                        {
                            sigOk = false;
                            break;
                        }
                    }

                    if (sigOk)
                    {
                        return m;
                    }
                }
            }
            catch
            {
                // Il2Cpp
            }
        }

        return null;
    }

    private static bool IsBoolLikeParameterType(Type pt)
    {
        if (pt == null)
        {
            return false;
        }

        if (pt == typeof(bool))
        {
            return true;
        }

        return string.Equals(pt.Name, "Boolean", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Instance method with one parameter that accepts <paramref name="device"/> (or <see cref="GameObject"/> for a <see cref="Component"/>).</summary>
    private static MethodInfo TryFindInstanceMethodOneArgAcceptsDevice(Type startType, string methodName, UnityEngine.Object device)
    {
        if (startType == null || device == null)
        {
            return null;
        }

        for (var t = startType; t != null && t != typeof(object); t = t.BaseType)
        {
            try
            {
                foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (m.IsStatic || !string.Equals(m.Name, methodName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var ps = m.GetParameters();
                    if (ps.Length != 1)
                    {
                        continue;
                    }

                    var pt = ps[0].ParameterType;
                    if (pt.IsInstanceOfType(device))
                    {
                        return m;
                    }

                    if (device is Component dc && dc.gameObject != null && pt == typeof(GameObject))
                    {
                        return m;
                    }
                }
            }
            catch
            {
                // Il2Cpp
            }
        }

        return null;
    }

    private static bool TryAccessToolsInvokeNoArgs(UnityEngine.Object o, string[] methodNames, out string detail)
    {
        detail = null;
        if (o == null || methodNames == null)
        {
            return false;
        }

        foreach (var name in methodNames)
        {
            var m = TryResolveInstanceMethodOnHierarchy(o.GetType(), name, Type.EmptyTypes);
            if (m == null)
            {
                continue;
            }

            try
            {
                m.Invoke(o, null);
                detail = $"{m.DeclaringType?.Name ?? "?"}.{m.Name}()";
                return true;
            }
            catch
            {
                // try next name
            }
        }

        return false;
    }

    private static bool TryAccessToolsInvokeSingleBool(UnityEngine.Object o, string[] methodNames, out string detail)
    {
        detail = null;
        if (o == null || methodNames == null)
        {
            return false;
        }

        foreach (var name in methodNames)
        {
            var m = TryResolveInstanceMethodOnHierarchy(o.GetType(), name, new[] { typeof(bool) });
            if (m == null)
            {
                continue;
            }

            foreach (var arg in new[] { true, false })
            {
                try
                {
                    m.Invoke(o, new object[] { arg });
                    detail = $"{m.DeclaringType?.Name ?? "?"}.{m.Name}({arg.ToString().ToLowerInvariant()})";
                    return true;
                }
                catch
                {
                    // try other bool
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Game may expose <c>ClearAlarms(Server s)</c> / <c>SendTechnician(Server s)</c> on a scene singleton rather than on the device.
    /// </summary>
    private static bool TryInvokeOnSceneFacadesOneArg(UnityEngine.Object device, string[] methodNames, string logLabel)
    {
        if (device == null || methodNames == null)
        {
            return false;
        }

        MonoBehaviour[] arr;
        try
        {
            arr = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true);
        }
        catch
        {
            return false;
        }

        for (var i = 0; i < arr.Length; i++)
        {
            var mb = arr[i];
            if (mb == null || ReferenceEquals(mb, device))
            {
                continue;
            }

            var bt = mb.GetType();
            if (!IsInterestingGameFacadeType(bt))
            {
                continue;
            }

            if (TryInvokeFirstMatchingOneArg(mb, device, methodNames, out var sig))
            {
                LogLifecycleInvoke(logLabel, $"{bt.Name}.{sig} (scene)");
                return true;
            }
        }

        return false;
    }

    private static bool IsInterestingGameFacadeType(Type t)
    {
        var ns = t.Namespace ?? "";
        if (ns.StartsWith("UnityEngine", StringComparison.Ordinal)
            || ns.StartsWith("TMPro", StringComparison.Ordinal)
            || ns.StartsWith("MelonLoader", StringComparison.Ordinal))
        {
            return false;
        }

        if (ns.StartsWith("DHCPSwitches", StringComparison.Ordinal))
        {
            return false;
        }

        var n = t.Name;
        return n.IndexOf("Alarm", StringComparison.OrdinalIgnoreCase) >= 0
               || n.IndexOf("Fault", StringComparison.OrdinalIgnoreCase) >= 0
               || n.IndexOf("Technician", StringComparison.OrdinalIgnoreCase) >= 0
               || n.IndexOf("Repair", StringComparison.OrdinalIgnoreCase) >= 0
               || n.IndexOf("Operational", StringComparison.OrdinalIgnoreCase) >= 0
               || n.IndexOf("Coordinator", StringComparison.OrdinalIgnoreCase) >= 0
               || n.IndexOf("Director", StringComparison.OrdinalIgnoreCase) >= 0
               || n.IndexOf("Facility", StringComparison.OrdinalIgnoreCase) >= 0
               || n.IndexOf("Rack", StringComparison.OrdinalIgnoreCase) >= 0
               || (n.IndexOf("Device", StringComparison.OrdinalIgnoreCase) >= 0
                   && n.IndexOf("Manager", StringComparison.OrdinalIgnoreCase) >= 0)
               || (n.IndexOf("Server", StringComparison.OrdinalIgnoreCase) >= 0
                   && (n.IndexOf("Manager", StringComparison.OrdinalIgnoreCase) >= 0
                       || n.IndexOf("Service", StringComparison.OrdinalIgnoreCase) >= 0))
               || n.IndexOf("Inventory", StringComparison.OrdinalIgnoreCase) >= 0
               || n.IndexOf("Notification", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>Map selected device to a facade method's single parameter (e.g. <see cref="GameObject"/> vs the device reference type).</summary>
    private static object TryResolveFacadeInvokeArgument(UnityEngine.Object device, Type paramType)
    {
        if (paramType != null && paramType.IsInstanceOfType(device))
        {
            return device;
        }

        if (device is Component c && c.gameObject != null && paramType == typeof(GameObject))
        {
            return c.gameObject;
        }

        return null;
    }

    private static bool TryInvokeFirstMatchingOneArg(
        UnityEngine.Object target,
        UnityEngine.Object device,
        string[] methodNames,
        out string detail)
    {
        detail = null;
        if (target == null || device == null)
        {
            return false;
        }

        try
        {
            foreach (var m in target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.IsStatic || m.IsSpecialName)
                {
                    continue;
                }

                var ps = m.GetParameters();
                if (ps.Length != 1)
                {
                    continue;
                }

                var arg0 = TryResolveFacadeInvokeArgument(device, ps[0].ParameterType);
                if (arg0 == null)
                {
                    continue;
                }

                foreach (var name in methodNames)
                {
                    if (!string.Equals(m.Name, name, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    try
                    {
                        m.Invoke(target, new object[] { arg0 });
                        detail = $"{m.Name}({arg0.GetType().Name})";
                        return true;
                    }
                    catch
                    {
                        // next name
                    }
                }
            }

            foreach (var m in target.GetType().GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!m.IsStatic || m.IsSpecialName)
                {
                    continue;
                }

                var ps = m.GetParameters();
                if (ps.Length != 1)
                {
                    continue;
                }

                var arg0 = TryResolveFacadeInvokeArgument(device, ps[0].ParameterType);
                if (arg0 == null)
                {
                    continue;
                }

                foreach (var name in methodNames)
                {
                    if (!string.Equals(m.Name, name, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    try
                    {
                        m.Invoke(null, new object[] { arg0 });
                        detail = $"{m.Name}({arg0.GetType().Name}) static";
                        return true;
                    }
                    catch
                    {
                        // next name
                    }
                }
            }

            foreach (var name in methodNames)
            {
                var am = TryFindInstanceMethodOneArgAcceptsDevice(target.GetType(), name, device);
                if (am == null)
                {
                    continue;
                }

                try
                {
                    var ps = am.GetParameters();
                    object arg = device;
                    if (ps.Length == 1
                        && ps[0].ParameterType == typeof(GameObject)
                        && device is Component dc
                        && dc.gameObject != null)
                    {
                        arg = dc.gameObject;
                    }

                    am.Invoke(target, new object[] { arg });
                    detail = $"{am.Name}({arg.GetType().Name})";
                    return true;
                }
                catch
                {
                    // next name
                }
            }
        }
        catch
        {
            // Il2Cpp
        }

        return false;
    }

    private static bool IsUnityUiOrChrome(Component comp)
    {
        if (comp is RectTransform)
        {
            return true;
        }

        var ns = comp.GetType().Namespace ?? "";
        if (ns.StartsWith("UnityEngine.UI", StringComparison.Ordinal)
            || ns.StartsWith("TMPro", StringComparison.Ordinal)
            || ns.StartsWith("UnityEngine.EventSystems", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    /// <summary>Extra components under the device that may own alarm state (not the root <see cref="Server"/> type name).</summary>
    private static bool PlausibleAlarmSubsystemType(Type t)
    {
        var n = t.Name;
        if (n.IndexOf("Alarm", StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("Fault", StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("Alert", StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("Notification", StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("Warning", StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("Issue", StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("Health", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        // "Status" alone matches many unrelated UI/helpers; require another device/network cue.
        if (n.IndexOf("Status", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return n.IndexOf("Server", StringComparison.OrdinalIgnoreCase) >= 0
                   || n.IndexOf("Device", StringComparison.OrdinalIgnoreCase) >= 0
                   || n.IndexOf("Network", StringComparison.OrdinalIgnoreCase) >= 0
                   || n.IndexOf("Rack", StringComparison.OrdinalIgnoreCase) >= 0
                   || n.IndexOf("Health", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        return false;
    }

    /// <summary>
    /// Avoid EOL/timer-only behaviours for sibling sweep — those often reset countdown without spawning a character.
    /// </summary>
    private static bool PlausibleTechnicianSubsystemType(Type t)
    {
        var n = t.Name;
        if (n.IndexOf("EOL", StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("Eol", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return false;
        }

        return n.IndexOf("Technician", StringComparison.OrdinalIgnoreCase) >= 0
               || n.IndexOf("Repair", StringComparison.OrdinalIgnoreCase) >= 0
               || n.IndexOf("Maintenance", StringComparison.OrdinalIgnoreCase) >= 0
               || n.IndexOf("Dispatch", StringComparison.OrdinalIgnoreCase) >= 0
               || n.IndexOf("WorkOrder", StringComparison.OrdinalIgnoreCase) >= 0
               || n.IndexOf("Onsite", StringComparison.OrdinalIgnoreCase) >= 0
               || n.IndexOf("Usable", StringComparison.OrdinalIgnoreCase) >= 0
               || n.IndexOf("Interact", StringComparison.OrdinalIgnoreCase) >= 0
               || (n.IndexOf("Visit", StringComparison.OrdinalIgnoreCase) >= 0
                   && n.IndexOf("Technician", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static IEnumerable<UnityEngine.Object> EnumerateAlarmTargets(UnityEngine.Object o)
    {
        if (o == null)
        {
            yield break;
        }

        var seen = new HashSet<int>();
        if (!seen.Add(o.GetInstanceID()))
        {
            yield break;
        }

        yield return o;

        if (o is not Component c || c.gameObject == null)
        {
            yield break;
        }

        foreach (var comp in c.gameObject.GetComponents<Component>())
        {
            if (comp == null || IsUnityUiOrChrome(comp))
            {
                continue;
            }

            if (!PlausibleAlarmSubsystemType(comp.GetType()))
            {
                continue;
            }

            if (!seen.Add(comp.GetInstanceID()))
            {
                continue;
            }

            yield return comp;
        }

        foreach (var comp in c.gameObject.GetComponentsInChildren<Component>(true))
        {
            if (comp == null || IsUnityUiOrChrome(comp))
            {
                continue;
            }

            if (!seen.Add(comp.GetInstanceID()))
            {
                continue;
            }

            if (PlausibleAlarmSubsystemType(comp.GetType()))
            {
                yield return comp;
            }
        }
    }

    private static IEnumerable<UnityEngine.Object> EnumerateTechnicianTargets(UnityEngine.Object o)
    {
        if (o == null)
        {
            yield break;
        }

        var seen = new HashSet<int>();
        if (!seen.Add(o.GetInstanceID()))
        {
            yield break;
        }

        yield return o;

        if (o is not Component c || c.gameObject == null)
        {
            yield break;
        }

        foreach (var comp in c.gameObject.GetComponents<Component>())
        {
            if (comp == null || IsUnityUiOrChrome(comp))
            {
                continue;
            }

            if (!PlausibleTechnicianSubsystemType(comp.GetType()))
            {
                continue;
            }

            if (!seen.Add(comp.GetInstanceID()))
            {
                continue;
            }

            yield return comp;
        }

        foreach (var comp in c.gameObject.GetComponentsInChildren<Component>(true))
        {
            if (comp == null || IsUnityUiOrChrome(comp))
            {
                continue;
            }

            if (!seen.Add(comp.GetInstanceID()))
            {
                continue;
            }

            if (PlausibleTechnicianSubsystemType(comp.GetType()))
            {
                yield return comp;
            }
        }
    }

    private static bool TryInvokeNoArgsByNameList(UnityEngine.Object o, string[] methodNames, out string detail)
    {
        detail = null;
        if (o == null || methodNames == null || methodNames.Length == 0)
        {
            return false;
        }

        try
        {
            foreach (var m in o.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.IsStatic || m.IsSpecialName || m.GetParameters().Length != 0)
                {
                    continue;
                }

                foreach (var name in methodNames)
                {
                    if (!string.Equals(m.Name, name, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    try
                    {
                        m.Invoke(o, null);
                        detail = $"{o.GetType().Name}.{m.Name}()";
                        return true;
                    }
                    catch
                    {
                        // wrong overload / Il2Cpp — try next candidate
                    }
                }
            }
        }
        catch
        {
            // Il2Cpp
        }

        return false;
    }

    private static bool TryInvokeSingleBoolByNameList(UnityEngine.Object o, string[] methodNames, out string detail)
    {
        detail = null;
        if (o == null || methodNames == null || methodNames.Length == 0)
        {
            return false;
        }

        try
        {
            foreach (var m in o.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.IsStatic || m.IsSpecialName)
                {
                    continue;
                }

                var ps = m.GetParameters();
                if (ps.Length != 1)
                {
                    continue;
                }

                var pt = ps[0].ParameterType;
                var isBoolParam = pt == typeof(bool)
                                  || string.Equals(pt.Name, "Boolean", StringComparison.OrdinalIgnoreCase);
                if (!isBoolParam)
                {
                    continue;
                }

                foreach (var name in methodNames)
                {
                    if (!string.Equals(m.Name, name, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    foreach (var arg in new[] { true, false })
                    {
                        try
                        {
                            m.Invoke(o, new object[] { arg });
                            detail = $"{o.GetType().Name}.{m.Name}({arg.ToString().ToLowerInvariant()})";
                            return true;
                        }
                        catch
                        {
                            // try other bool / next method
                        }
                    }
                }
            }
        }
        catch
        {
            // Il2Cpp
        }

        return false;
    }

    private static bool TryInvokeFirstZeroArgMethodMatching(UnityEngine.Object o, Func<string, bool> nameOk, out string detail)
    {
        detail = null;
        if (o == null || nameOk == null)
        {
            return false;
        }

        try
        {
            foreach (var m in o.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.IsStatic || m.IsSpecialName || m.GetParameters().Length != 0)
                {
                    continue;
                }

                if (!nameOk(m.Name))
                {
                    continue;
                }

                try
                {
                    m.Invoke(o, null);
                    detail = $"{o.GetType().Name}.{m.Name}()";
                    return true;
                }
                catch
                {
                    // try next method on same component
                }
            }
        }
        catch
        {
            // Il2Cpp
        }

        return false;
    }

    private static bool IsLikelyClearAlarmMethodName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        var aboutIssue = name.IndexOf("Alarm", StringComparison.OrdinalIgnoreCase) >= 0
                         || name.IndexOf("Fault", StringComparison.OrdinalIgnoreCase) >= 0
                         || name.IndexOf("Alert", StringComparison.OrdinalIgnoreCase) >= 0
                         || name.IndexOf("Issue", StringComparison.OrdinalIgnoreCase) >= 0
                         || name.IndexOf("Problem", StringComparison.OrdinalIgnoreCase) >= 0
                         || name.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0
                         || name.IndexOf("Warning", StringComparison.OrdinalIgnoreCase) >= 0;
        if (!aboutIssue)
        {
            return false;
        }

        return name.IndexOf("Clear", StringComparison.OrdinalIgnoreCase) >= 0
               || name.IndexOf("Ack", StringComparison.OrdinalIgnoreCase) >= 0
               || name.IndexOf("Silence", StringComparison.OrdinalIgnoreCase) >= 0
               || name.IndexOf("Reset", StringComparison.OrdinalIgnoreCase) >= 0
               || name.IndexOf("Dismiss", StringComparison.OrdinalIgnoreCase) >= 0
               || name.IndexOf("Resolve", StringComparison.OrdinalIgnoreCase) >= 0;
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
