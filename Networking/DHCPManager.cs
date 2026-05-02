using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace DHCPSwitches;

public static class DHCPManager
{
    private static readonly HashSet<string> AssignedIPs = new();

    /// <summary>
    /// When true (default), empty <c>SetIP</c> from the game is auto-filled via Harmony (DHCP-style).
    /// Turn off from IPAM toolbar to stop background auto-assignment after clearing addresses.
    /// </summary>
    public static bool EmptyIpAutoFillEnabled { get; set; } = true;

    /// <summary>Skipped for the next empty <c>SetIP</c> when the mod intentionally clears an address.</summary>
    internal static bool SuppressEmptyIpAutoAssign { get; private set; }

    public static bool IsFlowPaused { get; private set; }

    /// <summary>Set when <see cref="SetServerIP"/> rejects or the game throws (shown in IPAM).</summary>
    public static string LastSetIpError { get; private set; }

    public static void ClearLastSetIpError() => LastSetIpError = null;

    /// <summary>
    /// One <see cref="UnityEngine.Object.FindObjectsOfType{Server}"/> per frame — used by Harmony empty-SetIP path
    /// so hundreds of calls in the same frame do not each rescan the scene.
    /// </summary>
    private static int _sceneServersFrame = -1;
    private static Server[] _sceneServersCache;

    public static void ClearCaches()
    {
        InvalidateSceneServerFrameCache();
        GameSubnetHelper.ClearCaches();
    }

    private static void InvalidateSceneServerFrameCache()
    {
        _sceneServersFrame = -1;
        _sceneServersCache = null;
    }

    internal static Server[] GetSceneServersForFrame()
    {
        var f = Time.frameCount;
        if (_sceneServersFrame != f)
        {
            _sceneServersFrame = f;
            _sceneServersCache = UnityEngine.Object.FindObjectsOfType<Server>();
        }

        return _sceneServersCache ?? Array.Empty<Server>();
    }

    /// <summary>IPAM customer-assign and batch actions can surface errors the same way as <see cref="SetServerIP"/>.</summary>
    internal static void SetLastIpamError(string message) => LastSetIpError = message;

    public static void ToggleFlow()
    {
        IsFlowPaused = !IsFlowPaused;
        ModLogging.Msg(IsFlowPaused ? "Flow paused." : "Flow running.");
        ModDebugLog.Bootstrap();
        ModDebugLog.WriteLine(
            IsFlowPaused
                ? "IPAM: sim flow PAUSED — AddAppPerformance is blocked here (no IOPS ticks through this gate; L3 checks are skipped)."
                : "IPAM: sim flow RUNNING — AddAppPerformance prefix will run (L3 enforcement applies when L3 is ON).");
    }

    public static void AssignAllServers()
    {
        if (!LicenseManager.IsDHCPUnlocked)
        {
            ModLogging.Warning("DHCP locked (toggle with Ctrl+D debug).");
            return;
        }

        ModDebugLog.EnterDhcpResolutionBatch();
        try
        {

        var servers = UnityEngine.Object.FindObjectsOfType<Server>();
        RebuildAssignedIpsFromScene(servers);

        var assigned = 0;
        foreach (var server in servers)
        {
            var ip = GetServerIP(server);
            if (!string.IsNullOrWhiteSpace(ip) && ip != "0.0.0.0")
            {
                continue;
            }

            var newIp = GetNextFreeIpForServer(server, servers);
            if (string.IsNullOrEmpty(newIp))
            {
                continue;
            }

            if (SetServerIP(server, newIp, skipUsableListCheck: true))
            {
                AssignedIPs.Add(newIp);
                assigned++;
            }
        }

        if (assigned > 0)
        {
            ModLogging.Msg($"DHCP: {assigned} IPs assigned.");
            IPAMOverlay.InvalidateDeviceCache();
        }

        }
        finally
        {
            ModDebugLog.LeaveDhcpResolutionBatch();
        }
    }

    /// <summary>Assign DHCP from the game's contract usable lists (and fallbacks) to every listed server; skips servers that already have an IP.</summary>
    public static int AssignDhcpToServers(System.Collections.Generic.IEnumerable<Server> servers)
    {
        ModDebugLog.Bootstrap();

        if (servers == null)
        {
            ModDebugLog.WriteDhcpAssign("AssignDhcpToServers called with servers=null");
            return 0;
        }

        var selectedNonNull = 0;
        foreach (var x in servers)
        {
            if (x != null)
            {
                selectedNonNull++;
            }
        }

        var sceneArr = UnityEngine.Object.FindObjectsOfType<Server>();
        var sceneCount = sceneArr != null ? sceneArr.Length : 0;
        ModDebugLog.WriteDhcpAssign(
            $"AssignDhcpToServers begin selectedNonNull={selectedNonNull} sceneServerCount={sceneCount} dhcpUnlocked={LicenseManager.IsDHCPUnlocked}");

        if (!LicenseManager.IsDHCPUnlocked)
        {
            ModDebugLog.WriteDhcpAssign("aborted: DHCP feature locked (LicenseManager.IsDHCPUnlocked=false; use mod unlock / Ctrl+D per docs)");
            ModLogging.Warning("DHCP locked (toggle with Ctrl+D debug).");
            return 0;
        }

        ModDebugLog.EnterDhcpResolutionBatch();
        try
        {
        var allServers = sceneArr ?? UnityEngine.Object.FindObjectsOfType<Server>();
        RebuildAssignedIpsFromScene(allServers);
        ModDebugLog.WriteDhcpAssign($"after RebuildAssignedIpsFromScene in-use IP slots={AssignedIPs.Count}");

        var n = 0;
        foreach (var server in servers)
        {
            if (server == null)
            {
                continue;
            }

            var ip = GetServerIP(server);
            if (!string.IsNullOrWhiteSpace(ip) && ip != "0.0.0.0")
            {
                ModDebugLog.WriteDhcpAssign($"skip {FormatServerLogLabel(server)} (already has ip={ip})");
                continue;
            }

            var newIp = GetNextFreeIpForServer(server, allServers);
            if (string.IsNullOrEmpty(newIp))
            {
                ModDebugLog.WriteDhcpAssign(
                    $"no address {FormatServerLogLabel(server)} reason={ExplainDhcpMiss(server, allServers)}");
                continue;
            }

            if (!SetServerIP(server, newIp, skipUsableListCheck: true))
            {
                ModDebugLog.WriteDhcpAssign(
                    $"SetIP failed {FormatServerLogLabel(server)} candidate={newIp} LastSetIpError={LastSetIpError ?? "(null)"}");
                continue;
            }

            AssignedIPs.Add(newIp);
            n++;
            ModDebugLog.WriteDhcpAssign($"assigned {FormatServerLogLabel(server)} -> {newIp}");
        }

        ModDebugLog.WriteDhcpAssign($"AssignDhcpToServers end assignedCount={n}");

        if (n > 0)
        {
            ModLogging.Msg($"DHCP: {n} server(s) assigned.");
            IPAMOverlay.InvalidateDeviceCache();
        }

        return n;
        }
        finally
        {
            ModDebugLog.LeaveDhcpResolutionBatch();
        }
    }

    /// <summary>Assign one free usable address to a single server (toolbar / per-row DHCP auto).</summary>
    public static bool AssignDhcpToSingleServer(Server server)
    {
        if (!LicenseManager.IsDHCPUnlocked)
        {
            ModLogging.Warning("DHCP locked (toggle with Ctrl+D debug).");
            return false;
        }

        if (server == null)
        {
            return false;
        }

        ModDebugLog.EnterDhcpResolutionBatch();
        try
        {
        var allServers = UnityEngine.Object.FindObjectsOfType<Server>();
        RebuildAssignedIpsFromScene(allServers);
        var newIp = GetNextFreeIpForServer(server, allServers);
        if (string.IsNullOrEmpty(newIp))
        {
            if (IPAMOverlay.IsServerWithoutCustomerAssignment(server))
            {
                LastSetIpError = "DHCP failed: server is not on a customer contract yet (rack row or IPAM assign).";
            }
            else
            {
                LastSetIpError = "DHCP failed: no free address in the contract subnet usable lists for this server.";
            }

            ModLogging.Warning("DHCP: AssignDhcpToSingleServer found no free IP.");
            return false;
        }

        LastSetIpError = null;
        if (!SetServerIP(server, newIp, skipUsableListCheck: true))
        {
            return false;
        }

        IPAMOverlay.InvalidateDeviceCache();
        return true;
        }
        finally
        {
            ModDebugLog.LeaveDhcpResolutionBatch();
        }
    }

    private static string FormatServerLogLabel(Server server)
    {
        if (server == null)
        {
            return "Server<null>";
        }

        try
        {
            var nm = server.name;
            return string.IsNullOrEmpty(nm)
                ? $"Server(instanceId={server.GetInstanceID()})"
                : $"{nm} instanceId={server.GetInstanceID()}";
        }
        catch
        {
            return $"Server(instanceId={server.GetInstanceID()})";
        }
    }

    /// <summary>Why <see cref="GetNextFreeIpForServer"/> returned null (for <c>dhcp-assign:</c> log lines).</summary>
    private static string ExplainDhcpMiss(Server server, Server[] allServers)
    {
        if (server == null)
        {
            return "server is null";
        }

        int customerId;
        try
        {
            customerId = server.GetCustomerID();
        }
        catch (Exception ex)
        {
            return $"GetCustomerID threw: {ex.GetType().Name}: {ex.Message}";
        }

        var cb = GameSubnetHelper.FindCustomerBaseForServer(server);
        if (ModDebugLog.IsDhcpAssignVerboseEnabled && cb != null)
        {
            ModDebugLog.WriteDhcpAssign($"verbose subnets: {GameSubnetHelper.FormatSubnetsDiagnostic(cb)}");
        }

        var tryOrder = GameSubnetHelper.BuildDhcpCidrTryOrder(server, cb, allServers, logSteps: false);
        if (tryOrder == null || tryOrder.Count == 0)
        {
            if (IPAMOverlay.IsServerWithoutCustomerAssignment(server))
            {
                return "server not on customer contract (no AssetManagementDeviceLine + no real IPv4, or GetCustomerID()<0); no CIDR try list";
            }

            if (cb == null)
            {
                return $"no CustomerBase in scene for customerId={customerId} and no CIDR on Server/DeviceLine";
            }

            return ModDebugLog.IsDhcpAssignVerboseEnabled
                ? $"no try CIDRs after Server/DeviceLine + CustomerBase. {GameSubnetHelper.FormatSubnetsDiagnostic(cb)}"
                : "0 CIDRs (CustomerBase map empty and no x.x.x.x/nn on Server or AssetManagementDeviceLine); add DHCPSwitches-dhcp-assign.flag";
        }

        var sb = new StringBuilder();
        sb.Append("tried CIDRs ");
        for (var i = 0; i < tryOrder.Count; i++)
        {
            var cidr = tryOrder[i];
            if (string.IsNullOrWhiteSpace(cidr))
            {
                continue;
            }

            var usable = GameSubnetHelper.GetUsableIpsForSubnet(cidr, logDetail: false);
            var len = usable?.Length ?? 0;
            if (i > 0)
            {
                sb.Append("; ");
            }

            sb.Append(cidr);
            sb.Append(" usableCount=");
            sb.Append(len);
        }

        sb.Append(" — no unused usable IP (all taken, .1 skipped, or lists empty)");
        return sb.ToString();
    }

    private static void RebuildAssignedIpsFromScene(IEnumerable<Server> servers)
    {
        AssignedIPs.Clear();
        if (servers == null)
        {
            return;
        }

        foreach (var s in servers)
        {
            if (s == null)
            {
                continue;
            }

            var existingIp = GetServerIP(s);
            if (!string.IsNullOrWhiteSpace(existingIp) && existingIp != "0.0.0.0")
            {
                AssignedIPs.Add(existingIp);
            }
        }
    }

    private static bool IsUnsetIp(string ip)
    {
        return string.IsNullOrWhiteSpace(ip) || ip == "0.0.0.0";
    }

    internal static string GetServerIP(Server server)
    {
        if (server == null)
        {
            return string.Empty;
        }

        return server.IP ?? string.Empty;
    }

    /// <param name="skipUsableListCheck">True when the address was chosen from the game's usable list (DHCP path).</param>
    /// <param name="suppressAutoAssignOnEmpty">True when clearing the address from IPAM so Harmony does not immediately assign a new IP.</param>
    internal static bool SetServerIP(
        Server server,
        string ip,
        bool skipUsableListCheck = false,
        bool suppressAutoAssignOnEmpty = false)
    {
        LastSetIpError = null;

        if (server == null)
        {
            return false;
        }

        if (!skipUsableListCheck
            && !string.IsNullOrWhiteSpace(ip)
            && ip != "0.0.0.0"
            && !GameSubnetHelper.IsIpAllowedForServer(server, ip))
        {
            LastSetIpError =
                "That IP is not in this app's usable range for the contract. Use an address from the in-game IP keypad (hint list) or DHCP auto.";
            ModLogging.Warning($"SetIP blocked: '{ip}' is not in the usable list for this server's contract subnet.");
            return false;
        }

        var prevIp = GetServerIP(server);
        var needSuppress = suppressAutoAssignOnEmpty && IsUnsetIp(ip);
        try
        {
            if (needSuppress)
            {
                SuppressEmptyIpAutoAssign = true;
            }

            server.SetIP(ip);
            if (!string.IsNullOrWhiteSpace(prevIp) && prevIp != "0.0.0.0")
            {
                AssignedIPs.Remove(prevIp);
            }

            return true;
        }
        catch (Exception ex)
        {
            LastSetIpError = "The game rejected this address; see MelonLoader's log (e.g. MelonLoader/Latest.log) for details.";
            ModLogging.Error(ex);
            return false;
        }
        finally
        {
            if (needSuppress)
            {
                SuppressEmptyIpAutoAssign = false;
            }
        }
    }

    /// <summary>Skips x.x.x.1 — same convention as the in-game hint (gateway is usually .1 on contract subnets).</summary>
    private static bool IsTypicalGatewayLastOctet(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            return false;
        }

        var parts = ip.Trim().Split('.');
        if (parts.Length != 4 || !int.TryParse(parts[3], out var last))
        {
            return false;
        }

        return last == 1;
    }

    private static bool IsIpUsedByAnotherServer(Server self, string ip, Server[] allServers)
    {
        if (string.IsNullOrWhiteSpace(ip) || allServers == null)
        {
            return false;
        }

        foreach (var s in allServers)
        {
            if (s == null || s == self)
            {
                continue;
            }

            var cur = GetServerIP(s);
            if (cur == ip)
            {
                return true;
            }
        }

        return false;
    }

    private static string PickFromUsableArray(
        Il2CppStringArray usable,
        Server server,
        Server[] allServers,
        string cidrForLog,
        bool logStep,
        bool logEachReject)
    {
        if (usable == null)
        {
            if (logStep)
            {
                ModDebugLog.WriteDhcpStep($"PickFromUsableArray cidr={cidrForLog}: usable array is null");
            }

            return null;
        }

        var nEmpty = 0;
        var nInAssigned = 0;
        var nOtherServer = 0;
        var nGatewaySkip = 0;

        for (var i = 0; i < usable.Length; i++)
        {
            var candidate = usable[i];
            if (string.IsNullOrWhiteSpace(candidate))
            {
                nEmpty++;
                if (logEachReject)
                {
                    ModDebugLog.WriteDhcpTrace($"PickFromUsableArray cidr={cidrForLog} idx={i}: empty candidate");
                }

                continue;
            }

            if (AssignedIPs.Contains(candidate))
            {
                nInAssigned++;
                if (logEachReject)
                {
                    ModDebugLog.WriteDhcpTrace($"PickFromUsableArray cidr={cidrForLog} idx={i}: skip {candidate} (in mod AssignedIPs pool)");
                }

                continue;
            }

            if (IsIpUsedByAnotherServer(server, candidate, allServers))
            {
                nOtherServer++;
                if (logEachReject)
                {
                    ModDebugLog.WriteDhcpTrace($"PickFromUsableArray cidr={cidrForLog} idx={i}: skip {candidate} (used by another Server in scene)");
                }

                continue;
            }

            if (IsTypicalGatewayLastOctet(candidate))
            {
                nGatewaySkip++;
                if (logEachReject)
                {
                    ModDebugLog.WriteDhcpTrace($"PickFromUsableArray cidr={cidrForLog} idx={i}: skip {candidate} (typical gateway .1 rule)");
                }

                continue;
            }

            if (logStep)
            {
                ModDebugLog.WriteDhcpStep(
                    $"PickFromUsableArray cidr={cidrForLog}: chose {candidate} idx={i} stats empty={nEmpty} inAssignedPool={nInAssigned} otherServer={nOtherServer} gatewaySkip={nGatewaySkip} totalLen={usable.Length}");
            }

            return candidate;
        }

        if (logStep)
        {
            ModDebugLog.WriteDhcpStep(
                $"PickFromUsableArray cidr={cidrForLog}: no candidate totalLen={usable.Length} empty={nEmpty} inAssignedPool={nInAssigned} otherServer={nOtherServer} gatewaySkip={nGatewaySkip}");
        }

        return null;
    }

    /// <summary>
    /// Picks the first usable IPv4 in <paramref name="cidr"/> that passes the same DHCP UI rules as batch assign
    /// (assigned pool, other servers, typical .1 gateway skip).
    /// </summary>
    public static bool TryPickUnusedIpFromSubnet(string cidr, Server server, out string ip)
    {
        ip = null;
        if (string.IsNullOrWhiteSpace(cidr) || server == null)
        {
            return false;
        }

        var trimmed = cidr.Trim();
        var allServers = UnityEngine.Object.FindObjectsOfType<Server>();
        var usable = GameSubnetHelper.GetUsableIpsForSubnet(trimmed, logDetail: false);
        var picked = PickFromUsableArray(usable, server, allServers, trimmed, logStep: false, logEachReject: false);
        if (string.IsNullOrEmpty(picked))
        {
            return false;
        }

        ip = picked;
        return true;
    }

    private static string PickFromPrivateLan(string privateCidr, Server server, Server[] allServers)
    {
        foreach (var candidate in CustomerPrivateSubnetRegistry.EnumerateDhcpCandidates(privateCidr))
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (AssignedIPs.Contains(candidate))
            {
                continue;
            }

            if (IsIpUsedByAnotherServer(server, candidate, allServers))
            {
                continue;
            }

            return candidate;
        }

        return null;
    }

    private static string GetNextFreeIpForServer(Server server, Server[] allServersCache = null)
    {
        if (server == null)
        {
            return null;
        }

        var allServers = allServersCache ?? UnityEngine.Object.FindObjectsOfType<Server>();

        var logDetail = ModDebugLog.IsDhcpResolutionStepLogging || ModDebugLog.IsDhcpStepTraceEnabled;
        var logEachReject = ModDebugLog.IsDhcpStepTraceEnabled;

        if (logDetail)
        {
            ModDebugLog.WriteDhcpStep($"--- GetNextFreeIpForServer {FormatServerLogLabel(server)} ---");
        }

        var cb = GameSubnetHelper.FindCustomerBaseForServer(server);
        if (logDetail)
        {
            ModDebugLog.WriteDhcpStep(
                $"FindCustomerBaseForServer: {(cb == null ? "null" : "found")} (scene CustomerBase match for this server GetCustomerID)");
        }

        var tryOrder = GameSubnetHelper.BuildDhcpCidrTryOrder(server, cb, allServers, logSteps: logDetail);
        if (tryOrder.Count > 0)
        {
            if (logDetail)
            {
                ModDebugLog.WriteDhcpStep(
                    $"GetNextFreeIpForServer: tryOrder count={tryOrder.Count} order=[{string.Join(", ", tryOrder)}]");
            }

            foreach (var cidr in tryOrder)
            {
                if (string.IsNullOrWhiteSpace(cidr))
                {
                    continue;
                }

                if (logDetail)
                {
                    ModDebugLog.WriteDhcpStep($"GetNextFreeIpForServer: trying cidr={cidr}");
                }

                var usable = GameSubnetHelper.GetUsableIpsForSubnet(cidr, logDetail: logDetail);
                var picked = PickFromUsableArray(usable, server, allServers, cidr, logDetail, logEachReject);
                if (!string.IsNullOrEmpty(picked))
                {
                    if (logDetail)
                    {
                        ModDebugLog.WriteDhcpStep($"GetNextFreeIpForServer: SUCCESS ip={picked} from cidr={cidr}");
                    }

                    return picked;
                }
            }

            if (logDetail)
            {
                ModDebugLog.WriteDhcpStep("GetNextFreeIpForServer: exhausted all CIDRs in tryOrder without a pick");
            }

            ModLogging.Warning(
                "DHCP: no free address in any contract subnet list for this server (all may be in use or lists empty).");
            return null;
        }

        if (logDetail)
        {
            ModDebugLog.WriteDhcpStep("GetNextFreeIpForServer: tryOrder count=0; fallbacks private LAN / mod CIDR");
        }

        if (CustomerPrivateSubnetRegistry.TryGetPrivateLanCidrForServer(server, out var privateCidr))
        {
            var fromPrivate = PickFromPrivateLan(privateCidr, server, allServers);
            if (!string.IsNullOrEmpty(fromPrivate))
            {
                return fromPrivate;
            }
        }

        if (DeviceConfigRegistry.TryGetPreferredDhcpCidrForServer(server, out var modCidr))
        {
            var usableMod = GameSubnetHelper.GetUsableIpsForSubnet(modCidr, logDetail: logDetail);
            var pickedMod = PickFromUsableArray(usableMod, server, allServers, modCidr, logDetail, logEachReject);
            if (!string.IsNullOrEmpty(pickedMod))
            {
                return pickedMod;
            }
        }

        return null;
    }

    [HarmonyPatch]
    public static class ServerSetIpPatch
    {
        public static MethodBase TargetMethod()
        {
            // Use typeof(Server) — avoid AccessTools.TypeByName, which scans every loaded assembly and spams
            // ReflectionTypeLoadException on IL2CPP Unity modules Harmony cannot load.
            var serverType = typeof(Server);
            return AccessTools.Method(serverType, "SetIP", new[] { typeof(string) })
                ?? AccessTools.Method(serverType, "AssignIP", new[] { typeof(string) })
                ?? AccessTools.Method(serverType, "SetAddress", new[] { typeof(string) });
        }

        public static void Prefix(object __instance, ref string _ip)
        {
            if (!LicenseManager.IsDHCPUnlocked)
            {
                return;
            }

            if (!EmptyIpAutoFillEnabled || SuppressEmptyIpAutoAssign)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(_ip) && _ip != "0.0.0.0")
            {
                return;
            }

            if (__instance is not Server server)
            {
                return;
            }

            // Physical / in-game "clear IP" passes empty or 0.0.0.0 while server.IP is still the old address.
            // Without this, we immediately DHCP again and the clear never sticks.
            var prevIp = GetServerIP(server);
            if (!IsUnsetIp(prevIp))
            {
                return;
            }

            var autoIp = GetNextFreeIpForServer(server, GetSceneServersForFrame());
            if (string.IsNullOrEmpty(autoIp))
            {
                return;
            }

            _ip = autoIp;
            AssignedIPs.Add(autoIp);
        }

        public static void Postfix(string _ip)
        {
            if (!string.IsNullOrWhiteSpace(_ip) && _ip != "0.0.0.0")
            {
                AssignedIPs.Add(_ip);
            }
        }
    }

    [HarmonyPatch]
    public static class FlowPausePatch
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(CustomerBase), "AddAppPerformance");
        }

        public static bool Prefix(CustomerBase __instance)
        {
            if (IsFlowPaused)
            {
                var cid = __instance != null ? __instance.customerID : -1;
                ModDebugLog.Trace("iops", "AddAppPerformance Prefix: blocked (IsFlowPaused)");
                ModDebugLog.WriteThrottledIopsDeny(
                    cid,
                    "FLOW_PAUSED: IPAM flow is paused — click Resume in IPAM so AddAppPerformance runs.");
                return false;
            }

            if (!ReachabilityService.AllowCustomerAddAppPerformance(__instance, out var denyReason))
            {
                var cid = __instance != null ? __instance.customerID : -1;
                ModDebugLog.Trace("iops", $"AddAppPerformance Prefix: blocked (ReachabilityService) {denyReason}");
                ModDebugLog.WriteThrottledIopsDeny(cid, denyReason ?? "REACHABILITY: blocked (unknown reason)");
                return false;
            }

            // Hot path: do not lock/throttle-log every tick (AddAppPerformance can run very frequently).
            return true;
        }
    }
}

/// <summary>
/// Registered for Il2Cpp; periodic auto-assign was removed to avoid log spam and redundant work.
/// </summary>
public class DHCPController : MonoBehaviour
{
    public DHCPController(System.IntPtr ptr)
        : base(ptr)
    {
    }
}
