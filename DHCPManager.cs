using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace DHCPSwitches;

public static class DHCPManager
{
    private const string SUBNET_BASE = "192.168.1.";
    private const int POOL_START = 10;
    private const int POOL_END = 254;

    private static readonly HashSet<string> AssignedIPs = new();

    public static bool IsFlowPaused { get; private set; }

    public static void ToggleFlow()
    {
        IsFlowPaused = !IsFlowPaused;
        MelonLogger.Msg(IsFlowPaused ? "Flow pausiert." : "Flow läuft.");
    }

    public static void AssignAllServers()
    {
        if (!LicenseManager.IsDHCPUnlocked)
        {
            MelonLogger.Warning("DHCP nicht freigeschaltet.");
            return;
        }

        var servers = UnityEngine.Object.FindObjectsOfType<Server>();
        AssignedIPs.Clear();

        foreach (var s in servers)
        {
            var existingIp = GetServerIP(s);
            if (!string.IsNullOrWhiteSpace(existingIp) && existingIp != "0.0.0.0")
            {
                AssignedIPs.Add(existingIp);
            }
        }

        var assigned = 0;
        foreach (var server in servers)
        {
            var ip = GetServerIP(server);
            if (!string.IsNullOrWhiteSpace(ip) && ip != "0.0.0.0")
            {
                continue;
            }

            var newIp = GetNextFreeIP();
            if (string.IsNullOrEmpty(newIp))
            {
                break;
            }

            if (SetServerIP(server, newIp))
            {
                AssignedIPs.Add(newIp);
                assigned++;
            }
        }

        MelonLogger.Msg($"DHCP: {assigned} IPs vergeben.");
    }

    internal static string GetServerIP(Server server)
    {
        var type = server.GetType();

        var field = AccessTools.Field(type, "ipAddress")
            ?? AccessTools.Field(type, "ip")
            ?? AccessTools.Field(type, "currentIP");
        if (field != null)
        {
            return field.GetValue(server)?.ToString() ?? string.Empty;
        }

        var prop = AccessTools.Property(type, "ipAddress")
            ?? AccessTools.Property(type, "ip")
            ?? AccessTools.Property(type, "currentIP");
        if (prop != null)
        {
            return prop.GetValue(server)?.ToString() ?? string.Empty;
        }

        return string.Empty;
    }

    internal static bool SetServerIP(Server server, string ip)
    {
        var type = server.GetType();

        var method = AccessTools.Method(type, "SetIP", new[] { typeof(string) })
            ?? AccessTools.Method(type, "AssignIP", new[] { typeof(string) })
            ?? AccessTools.Method(type, "SetAddress", new[] { typeof(string) });

        if (method == null)
        {
            return false;
        }

        method.Invoke(server, new object[] { ip });
        return true;
    }

    private static string GetNextFreeIP()
    {
        for (var i = POOL_START; i <= POOL_END; i++)
        {
            var candidate = SUBNET_BASE + i;
            if (!AssignedIPs.Contains(candidate))
            {
                return candidate;
            }
        }

        MelonLogger.Warning("DHCP: IP-Pool erschöpft.");
        return null;
    }

    [HarmonyPatch]
    public static class ServerSetIpPatch
    {
        public static MethodBase? TargetMethod()
        {
            var serverType = AccessTools.TypeByName("Server") ?? typeof(Server);
            return AccessTools.Method(serverType, "SetIP", new[] { typeof(string) })
                ?? AccessTools.Method(serverType, "AssignIP", new[] { typeof(string) })
                ?? AccessTools.Method(serverType, "SetAddress", new[] { typeof(string) });
        }

        public static void Prefix(object __instance, ref string ip)
        {
            if (!LicenseManager.IsDHCPUnlocked)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(ip) && ip != "0.0.0.0")
            {
                return;
            }

            var autoIp = GetNextFreeIP();
            if (string.IsNullOrEmpty(autoIp))
            {
                return;
            }

            ip = autoIp;
            AssignedIPs.Add(autoIp);
            MelonLogger.Msg($"DHCP Auto-Assign: {__instance} -> {autoIp}");
        }

        public static void Postfix(string ip)
        {
            if (!string.IsNullOrWhiteSpace(ip) && ip != "0.0.0.0")
            {
                AssignedIPs.Add(ip);
            }
        }
    }

    [HarmonyPatch]
    public static class FlowPausePatch
    {
        public static MethodBase? TargetMethod()
        {
            var customerType = AccessTools.TypeByName("CustomerBase");
            if (customerType == null)
            {
        return string.Empty;
            }

            return AccessTools.Method(customerType, "AddAppPerformance");
        }

        public static bool Prefix() => !IsFlowPaused;
    }
}

public class DHCPController : MonoBehaviour
{
    public DHCPController(System.IntPtr ptr)
        : base(ptr)
    {
    }

    private float _checkInterval = 5f;
    private float _timer;

    public void Update()
    {
        _timer += Time.deltaTime;
        if (_timer < _checkInterval)
        {
            return;
        }

        _timer = 0f;
        if (LicenseManager.IsDHCPUnlocked)
        {
            DHCPManager.AssignAllServers();
        }
    }
}
