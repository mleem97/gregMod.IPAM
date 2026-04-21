using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace DHCPSwitches;

/// <summary>
/// Resolves per-application contract subnets from the game (<c>CustomerBase.subnetsPerApp</c> and related) and usable host lists.
/// Subnet values always come from game data or CIDR strings derived from it — no fixed mod subnet for contracts.
/// When map keys and UI lines are missing, may infer the map slot from System X / RISC / Mainframe / GPU cues vs subnet count.
/// </summary>
public static class GameSubnetHelper
{
    /// <summary>In-game catalog order (System X, RISC, Mainframe, GPU). Used only when inferring subnet slots from scene servers.</summary>
    private enum ServerHardwareFamily : int
    {
        SystemX = 0,
        Risc = 1,
        Mainframe = 2,
        Gpu = 3,
        Unknown = 99
    }

    private static readonly Dictionary<int, List<(object Key, string Cidr)>> SubnetsCacheByCustomerId = new();
    private static MethodInfo _cachedUsableIpMethod;
    private static object _cachedUsableIpTarget;

    /// <summary>Instance IDs of <see cref="Server"/> referenced by <see cref="AssetManagementDeviceLine.server"/> (rack / contract UI).</summary>
    private static readonly HashSet<int> ServerInstanceIdsOnAssetManagementDeviceLines = new();

    public static void ClearCaches()
    {
        SubnetsCacheByCustomerId.Clear();
        _cachedUsableIpMethod = null;
        _cachedUsableIpTarget = null;
        ServerInstanceIdsOnAssetManagementDeviceLines.Clear();
    }

    /// <summary>Rebuilds the set of servers currently tied to an asset-management row (O(n) scene scan).</summary>
    public static void RebuildAssetManagementDeviceLineServerCache()
    {
        ServerInstanceIdsOnAssetManagementDeviceLines.Clear();
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

                Server lineServer = null;
                try
                {
                    lineServer = line.server;
                }
                catch
                {
                    lineServer = null;
                }

                if (lineServer == null)
                {
                    continue;
                }

                try
                {
                    ServerInstanceIdsOnAssetManagementDeviceLines.Add(lineServer.GetInstanceID());
                }
                catch
                {
                    // Il2Cpp
                }
            }
        }
        catch
        {
            // type or field mismatch across game versions
        }
    }

    public static bool IsServerReferencedByAssetManagementDeviceLine(Server server)
    {
        if (server == null)
        {
            return false;
        }

        try
        {
            return ServerInstanceIdsOnAssetManagementDeviceLines.Contains(server.GetInstanceID());
        }
        catch
        {
            return false;
        }
    }

    public static CustomerBase FindCustomerBaseForServer(Server server)
    {
        if (server == null)
        {
            return null;
        }

        var cid = server.GetCustomerID();
        if (cid < 0)
        {
            return null;
        }

        foreach (var cb in UnityEngine.Object.FindObjectsOfType<CustomerBase>())
        {
            if (cb == null)
            {
                continue;
            }

            try
            {
                if (cb.customerID == cid)
                {
                    return cb;
                }
            }
            catch
            {
                // Il2Cpp: property may throw if object is stale
            }
        }

        return null;
    }

    /// <summary>
    /// CIDR strings to try for DHCP. When a matching <see cref="AssetManagementDeviceLine"/> exposes a subnet, that is
    /// authoritative (same row as the rack UI) — other apps&apos; subnets on the same customer are not mixed in.
    /// Otherwise falls back to <see cref="CustomerBase"/> map, then server string fields.
    /// </summary>
    /// <param name="logSteps">When true, writes <c>dhcp-step:</c> / <c>dhcp-trace:</c> lines (batch DHCP or dhcp-trace flag).</param>
    public static List<string> BuildDhcpCidrTryOrder(Server server, CustomerBase cb, Server[] allServers, bool logSteps = false)
    {
        if (logSteps)
        {
            var cid = -999;
            try
            {
                cid = server != null ? server.GetCustomerID() : -1;
            }
            catch
            {
                cid = -999;
            }

            var cbId = -999;
            try
            {
                cbId = cb != null ? cb.customerID : -1;
            }
            catch
            {
                cbId = -999;
            }

            ModDebugLog.WriteDhcpStep(
                $"BuildDhcpCidrTryOrder enter server={FormatServerBrief(server)} GetCustomerID={cid} CustomerBase.customerID={cbId}");
        }

        var fromUiLine = new List<string>();
        if (server != null)
        {
            TryAppendCidrsFromMatchingAssetManagementDeviceLines(server, fromUiLine, stopAfterFirstMatchingLine: true, logSteps);
        }

        if (fromUiLine.Count > 0)
        {
            if (logSteps)
            {
                ModDebugLog.WriteDhcpStep(
                    $"BuildDhcpCidrTryOrder phase=DeviceLineOnly authoritative count={fromUiLine.Count} [{string.Join(", ", fromUiLine)}] (no CustomerBase merge)");
            }

            return fromUiLine;
        }

        if (logSteps)
        {
            ModDebugLog.WriteDhcpStep("BuildDhcpCidrTryOrder phase=DeviceLine yielded 0 CIDRs; trying Server string fields");
        }

        var fromServerStrings = new List<string>();
        if (server != null)
        {
            TryAppendCidrsFromServerStringFields(server, fromServerStrings, logSteps);
        }

        if (fromServerStrings.Count > 0)
        {
            if (logSteps)
            {
                ModDebugLog.WriteDhcpStep(
                    $"BuildDhcpCidrTryOrder phase=ServerStrings count={fromServerStrings.Count} [{string.Join(", ", fromServerStrings)}]");
            }

            return fromServerStrings;
        }

        var ordered = new List<string>();
        if (cb == null)
        {
            if (logSteps)
            {
                ModDebugLog.WriteDhcpStep("BuildDhcpCidrTryOrder phase=CustomerBase skipped (cb=null); final count=0");
            }

            return ordered;
        }

        var entries = GetOrBuildSubnetsList(cb);
        if (entries == null || entries.Count == 0)
        {
            if (logSteps)
            {
                ModDebugLog.WriteDhcpStep(
                    $"BuildDhcpCidrTryOrder phase=CustomerBase map empty entries=0 (see dhcp-assign.flag + FormatSubnetsDiagnostic)");
            }

            return ordered;
        }

        if (logSteps)
        {
            ModDebugLog.WriteDhcpStep($"BuildDhcpCidrTryOrder phase=CustomerBase entries={entries.Count}");
        }

        var matchIdx = TryMatchServerToSubnetIndex(server, cb, entries, allServers);
        if (logSteps)
        {
            ModDebugLog.WriteDhcpStep($"BuildDhcpCidrTryOrder CustomerBase TryMatchServerToSubnetIndex -> matchIdx={matchIdx}");
        }

        if (matchIdx >= 0 && matchIdx < entries.Count)
        {
            var cidr = entries[matchIdx].Cidr;
            if (!string.IsNullOrWhiteSpace(cidr))
            {
                AddUniqueNormalizedCidr(ordered, cidr);
            }
        }

        for (var i = 0; i < entries.Count; i++)
        {
            if (i == matchIdx)
            {
                continue;
            }

            var c = entries[i].Cidr;
            if (string.IsNullOrWhiteSpace(c))
            {
                continue;
            }

            AddUniqueNormalizedCidr(ordered, c);
        }

        if (logSteps)
        {
            ModDebugLog.WriteDhcpStep(
                $"BuildDhcpCidrTryOrder phase=CustomerBase final count={ordered.Count} [{string.Join(", ", ordered)}]");
        }

        return ordered;
    }

    private static string FormatServerBrief(Server s)
    {
        if (s == null)
        {
            return "null";
        }

        try
        {
            var n = s.name;
            return string.IsNullOrEmpty(n) ? $"Server iid={s.GetInstanceID()}" : $"{n} iid={s.GetInstanceID()}";
        }
        catch
        {
            return $"Server iid={s.GetInstanceID()}";
        }
    }

    private static void AddUniqueNormalizedCidr(List<string> ordered, string rawCidr)
    {
        var n = NormalizeCidr(rawCidr);
        if (string.IsNullOrWhiteSpace(n) || !RouteMath.TryParseIpv4Cidr(n, out _, out _))
        {
            return;
        }

        if (!ordered.Contains(n))
        {
            ordered.Add(n);
        }
    }

    private static void TryAppendCidrsFromServerStringFields(Server server, List<string> ordered, bool logSteps)
    {
        if (server == null)
        {
            return;
        }

        try
        {
            var found = new List<string>();
            var label = FormatServerBrief(server);
            ScanAllStringFieldsForIpv4Cidr(server, found, logSteps ? $"Server[{label}]" : null);
            foreach (var c in found)
            {
                AddUniqueNormalizedCidr(ordered, c);
            }

            if (logSteps)
            {
                ModDebugLog.WriteDhcpStep($"TryAppendCidrsFromServerStringFields found={found.Count} [{string.Join(", ", found)}]");
            }
        }
        catch
        {
            // Il2Cpp reflection
        }
    }

    /// <summary>Game UI lines (e.g. asset / rack row) often hold the subnet string while <see cref="CustomerBase"/> map is empty.</summary>
    /// <param name="stopAfterFirstMatchingLine">When true, use only the first line that references this server and yields a CIDR (avoids duplicate rows / cross-app bleed).</param>
    private static void TryAppendCidrsFromMatchingAssetManagementDeviceLines(
        Server server,
        List<string> ordered,
        bool stopAfterFirstMatchingLine = false,
        bool logSteps = false)
    {
        if (server == null)
        {
            return;
        }

        try
        {
            var lines = UnityEngine.Object.FindObjectsOfType<AssetManagementDeviceLine>(true);
            var nLines = lines != null ? lines.Length : 0;
            if (logSteps)
            {
                ModDebugLog.WriteDhcpStep(
                    $"DeviceLine scan: AssetManagementDeviceLine instances in scene={nLines} targetServer={FormatServerBrief(server)}");
            }

            if (lines == null || lines.Length == 0)
            {
                return;
            }

            var lineIndex = -1;
            foreach (var line in lines)
            {
                lineIndex++;
                if (line == null)
                {
                    continue;
                }

                Server lineServer = null;
                try
                {
                    lineServer = line.server;
                }
                catch
                {
                    lineServer = null;
                }

                var lineId = -1;
                try
                {
                    lineId = line.GetInstanceID();
                }
                catch
                {
                    lineId = -1;
                }

                var refMatch = lineServer != null && ReferenceEquals(lineServer, server);
                if (logSteps)
                {
                    var lsBrief = lineServer == null ? "null" : FormatServerBrief(lineServer);
                    ModDebugLog.WriteDhcpStep(
                        $"  line[{lineIndex}] lineGoId={lineId} line.server refMatch={refMatch} line.server={lsBrief}");
                }

                if (!refMatch)
                {
                    continue;
                }

                var found = new List<string>();
                var hostLabel = $"AssetManagementDeviceLine[iid={lineId}]";
                ScanAllStringFieldsForIpv4Cidr(line, found, logSteps ? hostLabel : null);
                if (found.Count > 1)
                {
                    var one = ResolveSingleAuthoritativeCidrFromLine(line, found, logSteps);
                    found.Clear();
                    if (!string.IsNullOrEmpty(one))
                    {
                        found.Add(one);
                    }
                }

                foreach (var c in found)
                {
                    AddUniqueNormalizedCidr(ordered, c);
                }

                if (logSteps)
                {
                    ModDebugLog.WriteDhcpStep(
                        $"  line[{lineIndex}] MATCHED server: extracted CIDR count={found.Count} [{string.Join(", ", found)}] stopAfterFirst={stopAfterFirstMatchingLine}");
                }

                if (stopAfterFirstMatchingLine && found.Count > 0)
                {
                    return;
                }
            }
        }
        catch
        {
            // type or field mismatch across game versions
        }
    }

    private static void ScanAllStringFieldsForIpv4Cidr(object host, List<string> sink, string traceHostLabel)
    {
        if (host == null || sink == null)
        {
            return;
        }

        var t = host.GetType();
        const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var fi in t.GetFields(inst))
        {
            if (fi.FieldType != typeof(string))
            {
                continue;
            }

            string s;
            try
            {
                s = fi.GetValue(host) as string;
            }
            catch
            {
                continue;
            }

            if (TryExtractCidrFromString(s, out var cidr))
            {
                sink.Add(cidr);
                if (!string.IsNullOrEmpty(traceHostLabel) && ModDebugLog.IsDhcpStepTraceEnabled)
                {
                    ModDebugLog.WriteDhcpTrace($"{traceHostLabel} field {fi.Name} = \"{s}\" -> use {cidr}");
                }
            }
            else if (!string.IsNullOrEmpty(traceHostLabel) && ModDebugLog.IsDhcpStepTraceEnabled && !string.IsNullOrEmpty(s)
                     && (s.IndexOf('/') >= 0 || s.IndexOf('.') >= 0))
            {
                ModDebugLog.WriteDhcpTrace($"{traceHostLabel} field {fi.Name} = \"{s}\" (not parsed as IPv4 CIDR)");
            }
        }

        foreach (var pi in t.GetProperties(inst))
        {
            if (pi.PropertyType != typeof(string) || !pi.CanRead)
            {
                continue;
            }

            string s;
            try
            {
                s = pi.GetValue(host, null) as string;
            }
            catch
            {
                continue;
            }

            if (TryExtractCidrFromString(s, out var cidr))
            {
                sink.Add(cidr);
                if (!string.IsNullOrEmpty(traceHostLabel) && ModDebugLog.IsDhcpStepTraceEnabled)
                {
                    ModDebugLog.WriteDhcpTrace($"{traceHostLabel} prop {pi.Name} = \"{s}\" -> use {cidr}");
                }
            }
            else if (!string.IsNullOrEmpty(traceHostLabel) && ModDebugLog.IsDhcpStepTraceEnabled && !string.IsNullOrEmpty(s)
                     && (s.IndexOf('/') >= 0 || s.IndexOf('.') >= 0))
            {
                ModDebugLog.WriteDhcpTrace($"{traceHostLabel} prop {pi.Name} = \"{s}\" (not parsed as IPv4 CIDR)");
            }
        }
    }

    /// <summary>When string scan finds multiple CIDRs on one line (e.g. UI + internal copy), prefer a member named like <c>subnet</c>.</summary>
    private static string ResolveSingleAuthoritativeCidrFromLine(AssetManagementDeviceLine line, List<string> candidates, bool logSteps)
    {
        if (line == null || candidates == null || candidates.Count == 0)
        {
            return null;
        }

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        var t = line.GetType();
        const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var name in new[]
                 {
                     "subnet", "Subnet", "subnetText", "SubnetText", "subnetCidr", "SubnetCidr",
                     "networkCidr", "NetworkCidr", "cidr", "Cidr", "ipSubnet", "IpSubnet"
                 })
        {
            var f = t.GetField(name, inst);
            if (f != null && f.FieldType == typeof(string))
            {
                try
                {
                    if (TryExtractCidrFromString(f.GetValue(line) as string, out var c) && candidates.Contains(c))
                    {
                        if (logSteps)
                        {
                            ModDebugLog.WriteDhcpStep($"  narrowed multi-CIDR line using field {name} -> {c}");
                        }

                        return c;
                    }
                }
                catch
                {
                    // ignore
                }
            }

            var p = t.GetProperty(name, inst);
            if (p != null && p.PropertyType == typeof(string) && p.CanRead)
            {
                try
                {
                    if (TryExtractCidrFromString(p.GetValue(line, null) as string, out var c2) && candidates.Contains(c2))
                    {
                        if (logSteps)
                        {
                            ModDebugLog.WriteDhcpStep($"  narrowed multi-CIDR line using property {name} -> {c2}");
                        }

                        return c2;
                    }
                }
                catch
                {
                    // ignore
                }
            }
        }

        if (logSteps)
        {
            ModDebugLog.WriteDhcpStep(
                $"  narrowed multi-CIDR line: no subnet-named member matched; using first of {candidates.Count} -> {candidates[0]}");
        }

        return candidates[0];
    }

    private static bool TryExtractCidrFromString(string s, out string cidr)
    {
        cidr = null;
        if (string.IsNullOrWhiteSpace(s))
        {
            return false;
        }

        s = s.Trim();
        if (s.IndexOf('/') < 0)
        {
            return false;
        }

        if (!RouteMath.TryParseIpv4Cidr(s, out _, out _))
        {
            return false;
        }

        cidr = s;
        return true;
    }

    public static Il2CppStringArray GetUsableIpsForSubnet(string cidr, bool logDetail = false)
    {
        if (string.IsNullOrWhiteSpace(cidr))
        {
            if (logDetail)
            {
                ModDebugLog.WriteDhcpStep("GetUsableIpsForSubnet: cidr null/empty");
            }

            return null;
        }

        if (logDetail)
        {
            ModDebugLog.WriteDhcpStep($"GetUsableIpsForSubnet: probing game API for cidr={cidr}");
        }

        var fromGame = TryInvokeGameGetUsableIpsForSubnet(cidr);
        if (fromGame != null && fromGame.Length > 0)
        {
            if (logDetail)
            {
                ModDebugLog.WriteDhcpStep($"GetUsableIpsForSubnet: source=GameAPI cidr={cidr} length={fromGame.Length}");
            }

            return fromGame;
        }

        if (logDetail)
        {
            ModDebugLog.WriteDhcpStep($"GetUsableIpsForSubnet: game API empty; generating via RouteMath for cidr={cidr}");
        }

        var generated = new List<string>();
        foreach (var ip in RouteMath.EnumerateDhcpCandidates(cidr, skipTypicalGatewayLastOctet: true))
        {
            generated.Add(ip);
        }

        if (generated.Count == 0)
        {
            if (logDetail)
            {
                ModDebugLog.WriteDhcpStep($"GetUsableIpsForSubnet: RouteMath yielded 0 hosts for cidr={cidr}");
            }

            return null;
        }

        if (logDetail)
        {
            ModDebugLog.WriteDhcpStep($"GetUsableIpsForSubnet: source=RouteMath cidr={cidr} length={generated.Count}");
        }

        return new Il2CppStringArray(generated.ToArray());
    }

    public static bool IsIpAllowedForServer(Server server, string ip)
    {
        if (server == null || string.IsNullOrWhiteSpace(ip) || ip == "0.0.0.0")
        {
            return false;
        }

        var cb = FindCustomerBaseForServer(server);
        if (cb == null)
        {
            return false;
        }

        var tryOrder = BuildDhcpCidrTryOrder(server, cb, null, logSteps: false);
        foreach (var cidr in tryOrder)
        {
            var usable = GetUsableIpsForSubnet(cidr, logDetail: false);
            if (usable == null)
            {
                continue;
            }

            for (var i = 0; i < usable.Length; i++)
            {
                var u = usable[i];
                if (string.Equals(u, ip, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Human-readable contract subnet map for <see cref="ModDebugLog.IsDhcpAssignVerboseEnabled"/> DHCP diagnostics.</summary>
    internal static string FormatSubnetsDiagnostic(CustomerBase cb)
    {
        if (cb == null)
        {
            return "CustomerBase=null";
        }

        var cid = -1;
        try
        {
            cid = cb.customerID;
        }
        catch
        {
            cid = -1;
        }

        var list = TryReadSubnetsPerApp(cb);
        if (list == null || list.Count == 0)
        {
            return $"customerID={cid} subnetEntries=0 (no subnetsPerApp field, Il2Cpp map not IDictionary, or empty)";
        }

        var parts = new List<string>(list.Count);
        foreach (var e in list)
        {
            parts.Add($"{e.Key}->{e.Cidr}");
        }

        return $"customerID={cid} subnetEntries={list.Count} map=[{string.Join(", ", parts)}]";
    }

    private static List<(object Key, string Cidr)> GetOrBuildSubnetsList(CustomerBase cb)
    {
        if (cb == null)
        {
            return null;
        }

        int customerId;
        try
        {
            customerId = cb.customerID;
        }
        catch
        {
            return null;
        }

        if (SubnetsCacheByCustomerId.TryGetValue(customerId, out var cached))
        {
            return cached;
        }

        var built = TryReadSubnetsPerApp(cb);
        SubnetsCacheByCustomerId[customerId] = built ?? new List<(object Key, string Cidr)>();
        return SubnetsCacheByCustomerId[customerId];
    }

    private static List<(object Key, string Cidr)> TryReadSubnetsPerApp(CustomerBase cb)
    {
        var result = new List<(object Key, string Cidr)>();
        if (cb == null)
        {
            return result;
        }

        var t = cb.GetType();
        foreach (var name in new[]
                 {
                     "subnetsPerApp", "SubnetsPerApp", "subnetsByApp", "SubnetsByApp",
                     "appSubnets", "AppSubnets", "subnets", "Subnets", "contractSubnets", "ContractSubnets"
                 })
        {
            var member = (MemberInfo)t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                         ?? t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (member == null)
            {
                continue;
            }

            object raw = null;
            try
            {
                raw = member is FieldInfo fi ? fi.GetValue(cb) : ((PropertyInfo)member).GetValue(cb);
            }
            catch
            {
                continue;
            }

            if (raw == null)
            {
                continue;
            }

            if (TryEnumerateSubnetMap(raw, result))
            {
                ModDebugLog.Trace("dhcp", $"subnets map from {member.Name}: {result.Count} entries (customer {cb.customerID})");
                return result;
            }
        }

        if (TryReadSubnetsFromNestedContractLike(cb, result))
        {
            ModDebugLog.Trace("dhcp", $"subnets from nested contract-like object: {result.Count} (customer {cb.customerID})");
            return result;
        }

        TryReadSubnetsFromMembersMatching(cb, result, static n => n.IndexOf("subnet", StringComparison.OrdinalIgnoreCase) >= 0);
        if (result.Count > 0)
        {
            ModDebugLog.Trace("dhcp", $"subnets from *subnet* member scan: {result.Count} (customer {cb.customerID})");
            return result;
        }

        TryReadSubnetsFromMembersMatching(cb, result, static n => n.IndexOf("network", StringComparison.OrdinalIgnoreCase) >= 0);
        if (result.Count > 0)
        {
            ModDebugLog.Trace("dhcp", $"subnets from *network* member scan: {result.Count} (customer {cb.customerID})");
            return result;
        }

        return result;
    }

    /// <summary>Some builds store subnet maps on a nested contract object, not on <see cref="CustomerBase"/> directly.</summary>
    private static bool TryReadSubnetsFromNestedContractLike(CustomerBase cb, List<(object Key, string Cidr)> result)
    {
        if (cb == null)
        {
            return false;
        }

        var start = result.Count;
        var t = cb.GetType();
        foreach (var nestName in new[]
                 {
                     "contract", "Contract", "activeContract", "ActiveContract",
                     "customerContract", "CustomerContract", "currentContract", "CurrentContract"
                 })
        {
            object nested = null;
            var nf = t.GetField(nestName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (nf != null)
            {
                try
                {
                    nested = nf.GetValue(cb);
                }
                catch
                {
                    nested = null;
                }
            }

            if (nested == null)
            {
                var np = t.GetProperty(nestName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (np != null && np.CanRead)
                {
                    try
                    {
                        nested = np.GetValue(cb);
                    }
                    catch
                    {
                        nested = null;
                    }
                }
            }

            if (nested == null)
            {
                continue;
            }

            TryReadSubnetsFromObject(nested, result);
            if (result.Count > start)
            {
                return true;
            }
        }

        return false;
    }

    private static void TryReadSubnetsFromObject(object host, List<(object Key, string Cidr)> result)
    {
        if (host == null)
        {
            return;
        }

        var t = host.GetType();
        foreach (var name in new[]
                 {
                     "subnetsPerApp", "SubnetsPerApp", "subnetsByApp", "SubnetsByApp",
                     "appSubnets", "AppSubnets", "subnets", "Subnets", "contractSubnets", "ContractSubnets"
                 })
        {
            var member = (MemberInfo)t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                         ?? t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (member == null)
            {
                continue;
            }

            object raw = null;
            try
            {
                raw = member is FieldInfo fi ? fi.GetValue(host) : ((PropertyInfo)member).GetValue(host);
            }
            catch
            {
                continue;
            }

            if (raw == null)
            {
                continue;
            }

            if (TryEnumerateSubnetMap(raw, result))
            {
                return;
            }
        }
    }

    private static void TryReadSubnetsFromMembersMatching(CustomerBase cb, List<(object Key, string Cidr)> result, Func<string, bool> nameMatch)
    {
        if (cb == null)
        {
            return;
        }

        var t = cb.GetType();
        foreach (var fi in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!nameMatch(fi.Name))
            {
                continue;
            }

            object raw = null;
            try
            {
                raw = fi.GetValue(cb);
            }
            catch
            {
                continue;
            }

            if (raw == null)
            {
                continue;
            }

            if (TryEnumerateSubnetMap(raw, result))
            {
                return;
            }
        }

        foreach (var pi in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!pi.CanRead || !nameMatch(pi.Name))
            {
                continue;
            }

            object raw = null;
            try
            {
                raw = pi.GetValue(cb);
            }
            catch
            {
                continue;
            }

            if (raw == null)
            {
                continue;
            }

            if (TryEnumerateSubnetMap(raw, result))
            {
                return;
            }
        }
    }

    private static bool TryEnumerateSubnetMap(object raw, List<(object Key, string Cidr)> result)
    {
        if (raw == null)
        {
            return false;
        }

        if (TryEnumerateDictionaryKeysValues(raw, result))
        {
            return true;
        }

        var before = result.Count;

        if (TryEnumerateIList(raw, result))
        {
            return result.Count > before;
        }

        if (TryEnumerateReflectiveIndexedList(raw, result))
        {
            return result.Count > before;
        }

        if (raw is IDictionary dict)
        {
            foreach (DictionaryEntry e in dict)
            {
                var cidr = CoerceToCidrString(e.Value);
                if (!string.IsNullOrWhiteSpace(cidr))
                {
                    result.Add((e.Key, cidr));
                }
            }

            return result.Count > before;
        }

        if (raw is IEnumerable enumerable && raw is not string)
        {
            var idx = 0;
            foreach (var item in enumerable)
            {
                if (item == null)
                {
                    idx++;
                    continue;
                }

                var cidr = CoerceToCidrString(item);
                if (!string.IsNullOrWhiteSpace(cidr))
                {
                    result.Add((idx, cidr));
                    idx++;
                    continue;
                }

                var kvCidr = TryCoerceKeyValueCidr(item);
                if (!string.IsNullOrWhiteSpace(kvCidr.Cidr))
                {
                    result.Add((kvCidr.Key ?? idx, kvCidr.Cidr));
                }

                idx++;
            }

            return result.Count > before;
        }

        return false;
    }

    /// <summary>Il2Cpp <c>Dictionary</c> often does not cast to <see cref="IDictionary"/>; use <c>Keys</c>/<c>Values</c> instead.</summary>
    private static bool TryEnumerateDictionaryKeysValues(object raw, List<(object Key, string Cidr)> result)
    {
        if (raw == null)
        {
            return false;
        }

        var type = raw.GetType();
        var keysProp = type.GetProperty("Keys", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                         ?? type.GetProperty("keys", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var valsProp = type.GetProperty("Values", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                         ?? type.GetProperty("values", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (keysProp == null || valsProp == null || !keysProp.CanRead || !valsProp.CanRead)
        {
            return false;
        }

        object keysObj;
        object valsObj;
        try
        {
            keysObj = keysProp.GetValue(raw);
            valsObj = valsProp.GetValue(raw);
        }
        catch
        {
            return false;
        }

        if (keysObj is not IEnumerable keysEn || valsObj is not IEnumerable valsEn)
        {
            return false;
        }

        var startCount = result.Count;
        var ke = keysEn.GetEnumerator();
        var ve = valsEn.GetEnumerator();
        try
        {
            while (ke.MoveNext())
            {
                if (!ve.MoveNext())
                {
                    break;
                }

                var cidr = CoerceToCidrString(ve.Current);
                if (!string.IsNullOrWhiteSpace(cidr))
                {
                    result.Add((ke.Current, cidr));
                }
            }
        }
        finally
        {
            (ke as IDisposable)?.Dispose();
            (ve as IDisposable)?.Dispose();
        }

        return result.Count > startCount;
    }

    private static bool TryEnumerateIList(object raw, List<(object Key, string Cidr)> result)
    {
        if (raw is not IList ilist)
        {
            return false;
        }

        var start = result.Count;
        int n;
        try
        {
            n = ilist.Count;
        }
        catch
        {
            return false;
        }

        for (var i = 0; i < n; i++)
        {
            object item;
            try
            {
                item = ilist[i];
            }
            catch
            {
                continue;
            }

            if (item == null)
            {
                continue;
            }

            var cidr = CoerceToCidrString(item);
            if (!string.IsNullOrWhiteSpace(cidr))
            {
                result.Add((i, cidr));
                continue;
            }

            var kvCidr = TryCoerceKeyValueCidr(item);
            if (!string.IsNullOrWhiteSpace(kvCidr.Cidr))
            {
                result.Add((kvCidr.Key ?? i, kvCidr.Cidr));
            }
        }

        return result.Count > start;
    }

    /// <summary>Il2Cpp <c>List</c> sometimes does not implement <see cref="IList"/> on the managed wrapper.</summary>
    private static bool TryEnumerateReflectiveIndexedList(object raw, List<(object Key, string Cidr)> result)
    {
        if (raw == null)
        {
            return false;
        }

        var t = raw.GetType();
        var countProp = t.GetProperty("Count", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (countProp == null || !countProp.CanRead)
        {
            return false;
        }

        int n;
        try
        {
            n = Convert.ToInt32(countProp.GetValue(raw), CultureInfo.InvariantCulture);
        }
        catch
        {
            return false;
        }

        if (n <= 0)
        {
            return false;
        }

        var itemProp = t.GetProperty("Item", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, typeof(object), new[] { typeof(int) }, null);
        MethodInfo getItem = null;
        if (itemProp == null || !itemProp.CanRead)
        {
            getItem = t.GetMethod("get_Item", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int) }, null);
            if (getItem == null)
            {
                return false;
            }
        }

        var start = result.Count;
        for (var i = 0; i < n; i++)
        {
            object item;
            try
            {
                item = itemProp != null
                    ? itemProp.GetValue(raw, new object[] { i })
                    : getItem.Invoke(raw, new object[] { i });
            }
            catch
            {
                continue;
            }

            if (item == null)
            {
                continue;
            }

            var cidr = CoerceToCidrString(item);
            if (!string.IsNullOrWhiteSpace(cidr))
            {
                result.Add((i, cidr));
                continue;
            }

            var kvCidr = TryCoerceKeyValueCidr(item);
            if (!string.IsNullOrWhiteSpace(kvCidr.Cidr))
            {
                result.Add((kvCidr.Key ?? i, kvCidr.Cidr));
            }
        }

        return result.Count > start;
    }

    private static (object Key, string Cidr) TryCoerceKeyValueCidr(object item)
    {
        var type = item.GetType();
        object key = null;
        string cidr = null;
        foreach (var kn in new[] { "Key", "key", "app", "App", "application", "Application", "id", "Id", "index", "Index" })
        {
            var f = type.GetField(kn, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null)
            {
                try
                {
                    key ??= f.GetValue(item);
                }
                catch
                {
                    // ignore
                }
            }

            var p = type.GetProperty(kn, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && p.CanRead)
            {
                try
                {
                    key ??= p.GetValue(item);
                }
                catch
                {
                    // ignore
                }
            }
        }

        foreach (var vn in new[] { "Value", "value", "cidr", "Cidr", "subnet", "Subnet", "network", "Network", "ipRange", "IpRange" })
        {
            var f = type.GetField(vn, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null)
            {
                try
                {
                    cidr ??= CoerceToCidrString(f.GetValue(item));
                }
                catch
                {
                    // ignore
                }
            }

            var p = type.GetProperty(vn, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && p.CanRead)
            {
                try
                {
                    cidr ??= CoerceToCidrString(p.GetValue(item));
                }
                catch
                {
                    // ignore
                }
            }
        }

        return (key, cidr);
    }

    private static string CoerceToCidrString(object value)
    {
        if (value == null)
        {
            return null;
        }

        if (value is string s)
        {
            return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        }

        return value.ToString();
    }

    private static string NormalizeCidr(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return raw;
        }

        var t = raw.Trim();
        if (t.IndexOf('/') >= 0 && RouteMath.TryParseIpv4Cidr(t, out _, out _))
        {
            return t;
        }

        if (RouteMath.TryParseIpv4Cidr(t + "/24", out _, out _))
        {
            return t + "/24";
        }

        if (RouteMath.TryParseIpv4Cidr(t + "/32", out _, out _))
        {
            return t + "/32";
        }

        var four = TryParseAsNetworkDotZero(t);
        return string.IsNullOrEmpty(four) ? t : four + "/24";
    }

    /// <summary>Accepts <c>a.b.c.d</c>, <c>a.b.c.0</c>, <c>a.b.c.</c>, or <c>a.b.c</c> and returns <c>a.b.c.0</c>.</summary>
    private static string TryParseAsNetworkDotZero(string trimmed)
    {
        if (trimmed.EndsWith("/24", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(0, trimmed.Length - 3).Trim();
        }

        if (trimmed.EndsWith(".", StringComparison.Ordinal))
        {
            trimmed = trimmed.TrimEnd('.');
        }

        if (trimmed.EndsWith(".0", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(0, trimmed.Length - 2);
        }

        var parts = trimmed.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        if (!byte.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            || !byte.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            || !byte.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return null;
        }

        return string.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2}.0", parts[0], parts[1], parts[2]);
    }

    private static int TryMatchServerToSubnetIndex(
        Server server,
        CustomerBase cb,
        IReadOnlyList<(object Key, string Cidr)> entries,
        Server[] allServers)
    {
        if (server == null || entries == null || entries.Count == 0)
        {
            return -1;
        }

        foreach (var fieldName in new[]
                 {
                     "appID", "AppID", "appId", "AppId", "applicationID", "ApplicationID", "applicationId", "ApplicationId",
                     "contractAppIndex", "ContractAppIndex", "appIndex", "AppIndex", "serverAppIndex", "ServerAppIndex",
                     "hostedAppId", "HostedAppId", "applicationIndex", "ApplicationIndex"
                 })
        {
            var t = server.GetType();
            var f = t.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object v = null;
            if (f != null)
            {
                try
                {
                    v = f.GetValue(server);
                }
                catch
                {
                    // ignore
                }
            }

            if (v == null)
            {
                var p = t.GetProperty(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.CanRead)
                {
                    try
                    {
                        v = p.GetValue(server);
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }

            if (v == null)
            {
                continue;
            }

            for (var i = 0; i < entries.Count; i++)
            {
                if (KeysLooselyEqual(entries[i].Key, v))
                {
                    return i;
                }
            }
        }

        var familyIdx = TryMatchSubnetIndexByHardwareFamilyBijection(server, cb, entries, allServers);
        if (familyIdx >= 0)
        {
            if (ModDebugLog.IsDhcpResolutionStepLogging || ModDebugLog.IsDhcpStepTraceEnabled)
            {
                ModDebugLog.WriteDhcpStep(
                    $"TryMatchServerToSubnetIndex: hardware-family bijection index={familyIdx} (catalog order × subnet prefix sort; needs distinct families == subnet count)");
            }

            return familyIdx;
        }

        var catalogSlotIdx = TryMatchSubnetIndexByCatalogSlotWhenFewerFamiliesThanEntries(server, cb, entries, allServers);
        if (catalogSlotIdx >= 0)
        {
            if (ModDebugLog.IsDhcpResolutionStepLogging || ModDebugLog.IsDhcpStepTraceEnabled)
            {
                ModDebugLog.WriteDhcpStep(
                    $"TryMatchServerToSubnetIndex: catalog slot (partial contract) index={catalogSlotIdx} (fewer distinct server families than subnet rows; X=0,Risc=1,MF=2,Gpu=3; avoids instanceId % subnetCount)");
            }

            return catalogSlotIdx;
        }

        return InferSubnetIndexByServerOrdering(server, cb, entries, allServers);
    }

    /// <summary>
    /// Bijection requires <c>distinct hardware families on scene == subnetsPerApp row count</c>. When the contract has extra
    /// rows (e.g. Mainframe slot) but only X+RISC are deployed, counts differ and bijection fails — then <c>instanceId % N</c>
    /// mixes Yellow and Blue across subnets. Map each server by its own family&apos;s catalog slot (X=0, Risc=1, Mainframe=2,
    /// Gpu=3) when every detected family on that customer fits in row count. Skipped when <c>present.Count &gt;= entries.Count</c>
    /// (full bijection already ran). Fails if any on-scene family&apos;s slot is &gt;= N (too many types for this contract).
    /// </summary>
    private static int TryMatchSubnetIndexByCatalogSlotWhenFewerFamiliesThanEntries(
        Server server,
        CustomerBase cb,
        IReadOnlyList<(object Key, string Cidr)> entries,
        Server[] allServers)
    {
        if (server == null || cb == null || entries == null || entries.Count < 2)
        {
            return -1;
        }

        int cid;
        try
        {
            cid = cb.customerID;
        }
        catch
        {
            return -1;
        }

        var peers = CollectServersForCustomer(cid, allServers, server);
        var present = new HashSet<ServerHardwareFamily>();
        foreach (var p in peers)
        {
            if (p == null)
            {
                continue;
            }

            var f = TryDetectServerHardwareFamily(p);
            if (f != ServerHardwareFamily.Unknown)
            {
                present.Add(f);
            }
        }

        if (present.Count == 0 || present.Count >= entries.Count)
        {
            return -1;
        }

        foreach (var f in present)
        {
            if ((int)f >= entries.Count)
            {
                return -1;
            }
        }

        var selfF = TryDetectServerHardwareFamily(server);
        if (selfF == ServerHardwareFamily.Unknown)
        {
            return -1;
        }

        var slot = (int)selfF;
        if (slot < 0 || slot >= entries.Count)
        {
            return -1;
        }

        return slot;
    }

    /// <summary>
    /// When app ids and device lines are missing, infer which <c>subnetsPerApp</c> entry belongs to this server by
    /// classifying hardware family (System X, RISC, Mainframe, GPU) from names / reflection, collecting distinct families
    /// on that customer&apos;s servers, and matching count to subnet entries. Families are ordered by catalog enum.
    /// Subnet row order: if every entry shares the same IPv4 prefix length, preserve game map enumeration order (so two /27s
    /// are not reordered by numeric network, which inverts X vs RISC). Otherwise sort by ascending prefix then network.
    /// </summary>
    private static int TryMatchSubnetIndexByHardwareFamilyBijection(
        Server server,
        CustomerBase cb,
        IReadOnlyList<(object Key, string Cidr)> entries,
        Server[] allServers)
    {
        if (server == null || cb == null || entries == null || entries.Count == 0)
        {
            return -1;
        }

        int cid;
        try
        {
            cid = cb.customerID;
        }
        catch
        {
            return -1;
        }

        if (entries.Count == 1)
        {
            return 0;
        }

        var selfFamily = TryDetectServerHardwareFamily(server);
        if (selfFamily == ServerHardwareFamily.Unknown)
        {
            return -1;
        }

        var peers = CollectServersForCustomer(cid, allServers, server);
        var presentFamilies = new HashSet<ServerHardwareFamily>();
        foreach (var p in peers)
        {
            if (p == null)
            {
                continue;
            }

            var f = TryDetectServerHardwareFamily(p);
            if (f != ServerHardwareFamily.Unknown)
            {
                presentFamilies.Add(f);
            }
        }

        if (presentFamilies.Count != entries.Count)
        {
            return -1;
        }

        var orderedFamilies = new List<ServerHardwareFamily>(presentFamilies);
        orderedFamilies.Sort((a, b) => ((int)a).CompareTo((int)b));

        var orderedSubnetIdx = BuildSubnetIndexOrderForBijection(entries);

        var rank = orderedFamilies.IndexOf(selfFamily);
        if (rank < 0 || rank >= orderedSubnetIdx.Count)
        {
            return -1;
        }

        return orderedSubnetIdx[rank];
    }

    private static List<Server> CollectServersForCustomer(int customerId, Server[] allServers, Server fallbackIfEmpty)
    {
        var peers = new List<Server>();
        var scan = allServers ?? UnityEngine.Object.FindObjectsOfType<Server>();
        if (scan != null)
        {
            foreach (var s in scan)
            {
                if (s == null)
                {
                    continue;
                }

                try
                {
                    if (s.GetCustomerID() == customerId)
                    {
                        peers.Add(s);
                    }
                }
                catch
                {
                    // ignore
                }
            }
        }

        if (peers.Count == 0 && fallbackIfEmpty != null)
        {
            peers.Add(fallbackIfEmpty);
        }

        return peers;
    }

    private static ServerHardwareFamily TryDetectServerHardwareFamily(Server server)
    {
        if (server == null)
        {
            return ServerHardwareFamily.Unknown;
        }

        string name;
        try
        {
            name = server.name ?? string.Empty;
        }
        catch
        {
            return ServerHardwareFamily.Unknown;
        }

        var fromName = ClassifyHardwareFamilyFromString(name);
        if (fromName != ServerHardwareFamily.Unknown)
        {
            return fromName;
        }

        return ClassifyHardwareFamilyFromServerReflection(server);
    }

    private static ServerHardwareFamily ClassifyHardwareFamilyFromString(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return ServerHardwareFamily.Unknown;
        }

        var lower = text.ToLowerInvariant();
        if (lower.Contains("mainframe"))
        {
            return ServerHardwareFamily.Mainframe;
        }

        if (lower.Contains("gpu"))
        {
            return ServerHardwareFamily.Gpu;
        }

        if (lower.Contains("risc"))
        {
            return ServerHardwareFamily.Risc;
        }

        if (lower.Contains("systemx") || lower.Contains("system x"))
        {
            return ServerHardwareFamily.SystemX;
        }

        if (lower.Contains("purple"))
        {
            return ServerHardwareFamily.Mainframe;
        }

        if (lower.Contains("green"))
        {
            return ServerHardwareFamily.Gpu;
        }

        if (lower.Contains("yellow"))
        {
            return ServerHardwareFamily.SystemX;
        }

        if (lower.Contains("blue"))
        {
            return ServerHardwareFamily.Risc;
        }

        return ServerHardwareFamily.Unknown;
    }

    private static ServerHardwareFamily ClassifyHardwareFamilyFromServerReflection(Server server)
    {
        if (server == null)
        {
            return ServerHardwareFamily.Unknown;
        }

        var t = server.GetType();
        const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var name in new[]
                 {
                     "serverType", "ServerType", "hardwareType", "HardwareType", "serverLine", "ServerLine",
                     "productLine", "ProductLine", "lineType", "LineType", "modelName", "ModelName", "displayName", "DisplayName"
                 })
        {
            object v = null;
            var f = t.GetField(name, inst);
            if (f != null)
            {
                try
                {
                    v = f.GetValue(server);
                }
                catch
                {
                    // ignore
                }
            }

            if (v == null)
            {
                var p = t.GetProperty(name, inst);
                if (p != null && p.CanRead)
                {
                    try
                    {
                        v = p.GetValue(server, null);
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }

            if (v == null)
            {
                continue;
            }

            var s = v as string ?? v.ToString();
            var fam = ClassifyHardwareFamilyFromString(s);
            if (fam != ServerHardwareFamily.Unknown)
            {
                return fam;
            }
        }

        foreach (var fi in t.GetFields(inst))
        {
            if (fi.FieldType != typeof(string))
            {
                continue;
            }

            string s;
            try
            {
                s = fi.GetValue(server) as string;
            }
            catch
            {
                continue;
            }

            var fam = ClassifyHardwareFamilyFromString(s);
            if (fam != ServerHardwareFamily.Unknown)
            {
                return fam;
            }
        }

        foreach (var pi in t.GetProperties(inst))
        {
            if (pi.PropertyType != typeof(string) || !pi.CanRead)
            {
                continue;
            }

            string s;
            try
            {
                s = pi.GetValue(server, null) as string;
            }
            catch
            {
                continue;
            }

            var fam = ClassifyHardwareFamilyFromString(s);
            if (fam != ServerHardwareFamily.Unknown)
            {
                return fam;
            }
        }

        return ServerHardwareFamily.Unknown;
    }

    /// <summary>
    /// Subnet indices in game <c>subnetsPerApp</c> order, unless prefix lengths differ — then sort by prefix then network
    /// so /27 vs /28 contracts still line up with larger blocks first.
    /// </summary>
    private static List<int> BuildSubnetIndexOrderForBijection(IReadOnlyList<(object Key, string Cidr)> entries)
    {
        var idx = new List<int>(entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            idx.Add(i);
        }

        if (AllEntriesShareSameIpv4PrefixLength(entries))
        {
            return idx;
        }

        idx.Sort((ia, ib) => CompareEntriesBySubnetPrefixThenNetwork(entries[ia], entries[ib]));
        return idx;
    }

    private static bool AllEntriesShareSameIpv4PrefixLength(IReadOnlyList<(object Key, string Cidr)> entries)
    {
        if (entries == null || entries.Count <= 1)
        {
            return true;
        }

        int? firstPrefix = null;
        foreach (var e in entries)
        {
            var c = NormalizeCidr(e.Cidr);
            if (!RouteMath.TryParseIpv4Cidr(c, out _, out var p))
            {
                return false;
            }

            if (firstPrefix == null)
            {
                firstPrefix = p;
            }
            else if (firstPrefix.Value != p)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Ascending prefix length (/27 before /28); same prefix → ascending network address.</summary>
    private static int CompareEntriesBySubnetPrefixThenNetwork((object Key, string Cidr) a, (object Key, string Cidr) b)
    {
        var ca = NormalizeCidr(a.Cidr);
        var cb = NormalizeCidr(b.Cidr);
        if (!RouteMath.TryParseIpv4Cidr(ca, out var na, out var pa))
        {
            return 0;
        }

        if (!RouteMath.TryParseIpv4Cidr(cb, out var nb, out var pb))
        {
            return 0;
        }

        var c = pa.CompareTo(pb);
        return c != 0 ? c : na.CompareTo(nb);
    }

    private static bool KeysLooselyEqual(object mapKey, object serverVal)
    {
        if (mapKey == null || serverVal == null)
        {
            return false;
        }

        if (mapKey.Equals(serverVal))
        {
            return true;
        }

        try
        {
            if (Convert.ToInt64(mapKey, CultureInfo.InvariantCulture) == Convert.ToInt64(serverVal, CultureInfo.InvariantCulture))
            {
                return true;
            }
        }
        catch
        {
            // ignore
        }

        var a = mapKey.ToString();
        var b = serverVal.ToString();
        return !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Last resort when app id and prefab-name (Blue/Yellow) heuristics do not apply: order servers by instance id mod subnet count.
    /// This wrongly alternates subnets for multiple clones of the same app type — prefer <see cref="TryMatchSubnetIndexByHardwareFamilyBijection"/>
    /// or <see cref="TryMatchSubnetIndexByCatalogSlotWhenFewerFamiliesThanEntries"/> when subnet rows outnumber on-scene families.
    /// </summary>
    private static int InferSubnetIndexByServerOrdering(
        Server server,
        CustomerBase cb,
        IReadOnlyList<(object Key, string Cidr)> entries,
        Server[] allServers)
    {
        if (cb == null || entries.Count == 0)
        {
            return -1;
        }

        int cid;
        try
        {
            cid = cb.customerID;
        }
        catch
        {
            return -1;
        }

        var peers = CollectServersForCustomer(cid, allServers, server);

        peers.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));
        var selfId = server.GetInstanceID();
        var idx = peers.FindIndex(s => s != null && s.GetInstanceID() == selfId);
        if (idx < 0)
        {
            return -1;
        }

        if (entries.Count == 1)
        {
            return 0;
        }

        return idx % entries.Count;
    }

    private static Il2CppStringArray TryInvokeGameGetUsableIpsForSubnet(string cidr)
    {
        if (_cachedUsableIpMethod != null)
        {
            try
            {
                var arr = InvokeUsableMethod(_cachedUsableIpMethod, _cachedUsableIpTarget, cidr);
                if (arr != null && arr.Length > 0)
                {
                    return arr;
                }
            }
            catch
            {
                _cachedUsableIpMethod = null;
                _cachedUsableIpTarget = null;
            }
        }

        foreach (var (declType, isStatic) in UsableMethodProbeOrder())
        {
            var flags = (isStatic ? BindingFlags.Static : BindingFlags.Instance) | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var methodName in new[] { "GetUsableIPsFromSubnet", "GetUsableIpsFromSubnet", "GetUsableIPsForSubnet", "GetUsableIpsForSubnet" })
            {
                var m = declType.GetMethod(methodName, flags, null, new[] { typeof(string) }, null);
                if (m == null)
                {
                    continue;
                }

                object target = null;
                if (!isStatic)
                {
                    target = TryFindSingletonForType(declType);
                    if (target == null)
                    {
                        continue;
                    }
                }

                try
                {
                    var arr = InvokeUsableMethod(m, target, cidr);
                    if (arr != null && arr.Length > 0)
                    {
                        _cachedUsableIpMethod = m;
                        _cachedUsableIpTarget = target;
                        ModDebugLog.Trace("dhcp", $"usable IPs from {declType.Name}.{methodName} len={arr.Length}");
                        return arr;
                    }
                }
                catch
                {
                    // try next
                }
            }
        }

        return null;
    }

    private static IEnumerable<(Type DeclType, bool Static)> UsableMethodProbeOrder()
    {
        yield return (typeof(SetIP), false);
        yield return (typeof(Server), true);
        yield return (typeof(Server), false);
        yield return (typeof(CustomerBase), true);
        yield return (typeof(CustomerBase), false);
        yield return (typeof(MainGameManager), true);
        yield return (typeof(MainGameManager), false);
    }

    private static object TryFindSingletonForType(Type declType)
    {
        try
        {
            var instProp = declType.GetProperty("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                           ?? declType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (instProp != null)
            {
                return instProp.GetValue(null);
            }
        }
        catch
        {
            // ignore
        }

        try
        {
            UnityEngine.Object[] arr = declType == typeof(SetIP)
                ? UnityEngine.Object.FindObjectsOfType<SetIP>(true)
                : declType == typeof(Server)
                    ? UnityEngine.Object.FindObjectsOfType<Server>()
                    : declType == typeof(CustomerBase)
                        ? UnityEngine.Object.FindObjectsOfType<CustomerBase>()
                        : declType == typeof(MainGameManager)
                            ? UnityEngine.Object.FindObjectsOfType<MainGameManager>()
                            : null;
            if (arr != null && arr.Length > 0)
            {
                return arr[0];
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static Il2CppStringArray InvokeUsableMethod(MethodInfo method, object target, string cidr)
    {
        var r = method.Invoke(target, new object[] { cidr });
        if (r == null)
        {
            return null;
        }

        if (r is Il2CppStringArray il2)
        {
            return il2;
        }

        if (r is string[] sa)
        {
            return sa.Length == 0 ? null : new Il2CppStringArray(sa);
        }

        if (r is IList list && list.Count > 0)
        {
            var tmp = new string[list.Count];
            for (var i = 0; i < list.Count; i++)
            {
                tmp[i] = list[i]?.ToString() ?? string.Empty;
            }

            return new Il2CppStringArray(tmp);
        }

        return null;
    }
}
