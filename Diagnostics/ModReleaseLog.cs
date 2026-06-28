using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace GregModIPAM;

/// <summary>
/// Writes a verbose human-readable release log (<c>ipam.latest.log</c>) next to MelonLoader's <c>Latest.log</c>.
/// Recreated on each game launch. Contains version, environment, feature state, and all runtime events.
/// </summary>
internal static class ModReleaseLog
{
    private static string _path;
    private static bool _initTried;
    private static readonly object Sync = new();

    internal static string LogPath => _path;

    internal static void Bootstrap()
    {
        lock (Sync)
        {
            if (_initTried)
            {
                return;
            }

            _initTried = true;
            try
            {
                var dir = Path.GetDirectoryName(Application.dataPath);
                if (string.IsNullOrEmpty(dir))
                {
                    return;
                }

                _path = Path.Combine(dir, "ipam.latest.log");
                WriteHeader();
            }
            catch
            {
                _path = null;
            }
        }
    }

    private static void WriteHeader()
    {
        var asm = Assembly.GetExecutingAssembly();
        var version = asm.GetName().Version?.ToString() ?? "unknown";
        var buildDate = File.GetLastWriteTimeUtc(asm.Location);

        Append($"============================================================");
        Append($"  gregMod.IPAM — Verbose Release Log");
        Append($"============================================================");
        Append($"");
        Append($"Version:       {version}");
        Append($"Build Date:    {buildDate:yyyy-MM-dd HH:mm:ss} UTC");
        Append($"Log Started:   {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        Append($"");
        Append($"------------------------------------------------------------");
        Append($"  Environment");
        Append($"------------------------------------------------------------");
        Append($"OS:            {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
        Append($".NET:          {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        Append($"Unity:         {Application.unityVersion}");
        Append($"Game:          {Application.productName} {Application.version}");
        Append($"Data Path:     {Application.dataPath}");
        Append($"Platform:      {Application.platform}");
        Append($"Screen:        {Screen.width}x{Screen.height} @ {Screen.currentResolution.refreshRateRatio}Hz");
        Append($"System Memory: {SystemInfo.systemMemorySize} MB");
        Append($"GPU:           {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsMemorySize} MB)");
        Append($"CPU:           {SystemInfo.processorType} ({SystemInfo.processorCount} cores)");
        Append($"");
        Append($"------------------------------------------------------------");
        Append($"  Mod Info");
        Append($"------------------------------------------------------------");
        Append($"Mod GUID:      com.gregmod.ipam");
        Append($"Mod Name:      gregMod.IPAM");
        Append($"Author:        TeamGreg Modding (mleem97 & mochimus)");
        Append($"License:       Apache 2.0");
        Append($"Namespace:     GregModIPAM");
        Append($"Assembly:      {Path.GetFileName(asm.Location)}");
        Append($"Assembly Path: {asm.Location}");
        Append($"Assembly Size: {new FileInfo(asm.Location).Length} bytes");
        Append($"");
        Append($"============================================================");
        Append($"");
    }

    // ── General ──
    internal static void Info(string message) { Append($"[INFO]  {Ts()} {message}"); }
    internal static void Warning(string message) { Append($"[WARN]  {Ts()} {message}"); }
    internal static void Error(string message) { Append($"[ERROR] {Ts()} {message}"); }

    internal static void Error(string message, Exception ex)
    {
        Append($"[ERROR] {Ts()} {message}");
        if (ex != null)
        {
            Append($"        Exception: {ex.GetType().FullName}");
            Append($"        Message:   {ex.Message}");
            if (ex.InnerException != null)
            {
                Append($"        Inner:     {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
            }
            if (!string.IsNullOrEmpty(ex.StackTrace))
            {
                Append($"        StackTrace:");
                foreach (var line in ex.StackTrace.Split('\n'))
                {
                    Append($"          {line.Trim()}");
                }
            }
        }
    }

    // ── Feature / Config ──
    internal static void Feature(string feature, string state) { Append($"[FEAT]  {Ts()} {feature,-40} {state}"); }
    internal static void Config(string key, string value) { Append($"[CFG]   {Ts()} {key,-40} {value}"); }
    internal static void Pref(string key, string value) { Append($"[PREF]  {Ts()} {key,-40} {value}"); }

    // ── DLL / Dependencies ──
    internal static void DllLoad(string dllName, bool found, string path = null)
    {
        var status = found ? "OK" : "MISSING";
        var detail = path != null ? $" ({path})" : "";
        Append($"[DLL]   {Ts()} {dllName,-40} {status}{detail}");
    }

    internal static void DllCheckSummary(int found, int missing)
    {
        Append($"[DLL]   {Ts()} Dependency check complete: {found} found, {missing} missing");
        Append($"");
    }

    // ── Scene ──
    internal static void SceneLoaded(string sceneName, int buildIndex)
    {
        Append($"[SCENE] {Ts()} Loaded: \"{sceneName}\" (buildIndex={buildIndex})");
    }

    internal static void SceneUnloaded(string sceneName)
    {
        Append($"[SCENE] {Ts()} Unloaded: \"{sceneName}\"");
    }

    // ── Harmony Patches ──
    internal static void HarmonyPatch(string patchName, bool success, string detail = null)
    {
        var status = success ? "OK" : "FAILED";
        var extra = detail != null ? $" — {detail}" : "";
        Append($"[PATCH] {Ts()} {patchName,-40} {status}{extra}");
    }

    internal static void HarmonyPatchSummary(int success, int failed)
    {
        Append($"[PATCH] {Ts()} Harmony patching complete: {success} succeeded, {failed} failed");
        Append($"");
    }

    // ── DHCP ──
    internal static void DhcpEvent(string message) { Append($"[DHCP]  {Ts()} {message}"); }

    internal static void DhcpAssign(string serverLabel, string ip, string cidr, bool success, string error = null)
    {
        var status = success ? "OK" : "FAIL";
        Append($"[DHCP]  {Ts()} Assign {status}: {serverLabel} -> {ip} (cidr={cidr})");
        if (!success && error != null)
        {
            Append($"        Reason: {error}");
        }
    }

    internal static void DhcpScopeResolution(string serverLabel, string matchedScope, string cidr)
    {
        Append($"[DHCP]  {Ts()} Scope match: {serverLabel} -> scope=\"{matchedScope}\" cidr={cidr}");
    }

    internal static void DhcpNoFree(string serverLabel, string tryOrder)
    {
        Append($"[DHCP]  {Ts()} No free IP for {serverLabel} (tryOrder=[{tryOrder}])");
    }

    // ── IPAM ──
    internal static void IpamEvent(string message) { Append($"[IPAM]  {Ts()} {message}"); }

    internal static void IpamOpen()
    {
        Append($"[IPAM]  {Ts()} Overlay opened (frame={Time.frameCount})");
    }

    internal static void IpamClose()
    {
        Append($"[IPAM]  {Ts()} Overlay closed (frame={Time.frameCount})");
    }

    internal static void IpamNav(string section)
    {
        Append($"[IPAM]  {Ts()} Navigation: {section}");
    }

    internal static void IpamServerSelect(string serverLabel, string ip)
    {
        Append($"[IPAM]  {Ts()} Server selected: {serverLabel} ip={ip}");
    }

    internal static void IpamPrefixAction(string action, string cidr, string name = null)
    {
        var extra = name != null ? $" name=\"{name}\"" : "";
        Append($"[IPAM]  {Ts()} Prefix {action}: {cidr}{extra}");
    }

    internal static void IpamVlanAction(string action, int vlanId, string name = null)
    {
        var extra = name != null ? $" name=\"{name}\"" : "";
        Append($"[IPAM]  {Ts()} VLAN {action}: id={vlanId}{extra}");
    }

    internal static void IpamScopeAction(string action, string scopeName, string level, string cidr)
    {
        Append($"[IPAM]  {Ts()} DHCP Scope {action}: \"{scopeName}\" level={level} cidr={cidr}");
    }

    internal static void IpamTenancyAction(string action, string serverLabel, string mode, int tenantCount)
    {
        Append($"[IPAM]  {Ts()} Tenancy {action}: {serverLabel} mode={mode} tenants={tenantCount}");
    }

    internal static void IpamToast(string message)
    {
        Append($"[IPAM]  {Ts()} Toast: {message}");
    }

    // ── Rack ──
    internal static void RackEvent(string message) { Append($"[RACK]  {Ts()} {message}"); }

    internal static void RackMount(string rackName, string deviceName, int startU, int heightU)
    {
        Append($"[RACK]  {Ts()} Mount: \"{deviceName}\" -> \"{rackName}\" U{startU}+{heightU}");
    }

    internal static void RackUnmount(string rackName, string deviceName)
    {
        Append($"[RACK]  {Ts()} Unmount: \"{deviceName}\" from \"{rackName}\"");
    }

    // ── Naming ──
    internal static void NamingEvent(string message) { Append($"[NAME]  {Ts()} {message}"); }

    internal static void NamingApply(string pattern, int count)
    {
        Append($"[NAME]  {Ts()} Applied pattern=\"{pattern}\" to {count} devices");
    }

    // ── Input ──
    internal static void InputEvent(string message) { Append($"[INPUT] {Ts()} {message}"); }

    internal static void InputKey(string key, string action)
    {
        Append($"[INPUT] {Ts()} Key {key}: {action}");
    }

    // ── Performance ──
    internal static void PerfMs(string label, double ms)
    {
        if (ms > 5.0) // only log slow frames
        {
            Append($"[PERF]  {Ts()} {label}: {ms:F2}ms");
        }
    }

    // ── Save/Load ──
    internal static void SaveEvent(string path, bool success, string detail = null)
    {
        var status = success ? "OK" : "FAIL";
        var extra = detail != null ? $" — {detail}" : "";
        Append($"[SAVE]  {Ts()} {status}: {path}{extra}");
    }

    internal static void LoadEvent(string path, bool success, string detail = null)
    {
        var status = success ? "OK" : "FAIL";
        var extra = detail != null ? $" — {detail}" : "";
        Append($"[LOAD]  {Ts()} {status}: {path}{extra}");
    }

    // ── Helpers ──
    private static string Ts() => $"{DateTime.UtcNow:HH:mm:ss.fff}";

    private static void Append(string line)
    {
        if (string.IsNullOrEmpty(_path))
        {
            return;
        }

        try
        {
            File.AppendAllText(_path, line + Environment.NewLine);
        }
        catch
        {
            // ignore write failures
        }
    }
}
