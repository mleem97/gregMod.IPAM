using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DHCPSwitches;

/// <summary>
/// Debug log in the game install folder (directory containing the <c>_Data</c> folder). The file <c>DHCPSwitches-debug.log</c>
/// is replaced (truncated) at the start of each game launch; later lines append during that session.
/// Create an empty file <c>DHCPSwitches-setip.flag</c> beside the game <c>_Data</c> folder to dump the vanilla IP keypad UI hierarchy once when the keypad opens (see <c>SetIpKeypadDiagnostics</c>).
/// Create <c>DHCPSwitches-setip-dhcp.flag</c> there to append <c>setip-dhcp:</c> lines (spawn, destroy reason, border/canvas) while debugging the keypad DHCP button.
/// Create an empty file <c>DHCPSwitches-trace.flag</c> in that same folder to enable verbose step traces
/// (<c>[trace:…]</c> lines) for ping routing, cable checks, and IOPS reachability.
/// <c>IOPS ALLOW</c> lines in this file omit per-server IP samples by default; create <c>DHCPSwitches-iops-allow-ips.flag</c> to restore the long sample (server=name=ip) on those lines.
/// IPAM UI diagnostics (IOPS toolbar, OnGUI mouse) go to <c>DHCPSwitches-ipam.log</c> when <c>DHCPSwitches-ipam.flag</c>
/// is present — see <see cref="IpamDebugLog"/>.
/// </summary>
internal static class ModDebugLog
{
    private static readonly object Sync = new();
    private static string _path;
    private static string _ipamPath;
    private static bool _initTried;
    private static bool _ipamInitTried;
    private static bool _ipamSessionBannerWritten;
    private static DateTime _traceCacheUntilUtc;
    private static bool _traceCached;
    private static DateTime _ipamFlagCacheUntilUtc;
    private static bool _ipamFlagCached;
    private static DateTime _setIpFlagCacheUntilUtc;
    private static bool _setIpFlagCached;
    private static DateTime _setIpDhcpFlagCacheUntilUtc;
    private static bool _setIpDhcpFlagCached;
    private static DateTime _iopsAllowIpsFlagCacheUntilUtc;
    private static bool _iopsAllowIpsFlagCached;
    private static readonly Dictionary<int, (float Time, string Reason)> IopsDenyThrottle = new();
    private static readonly Dictionary<int, float> IopsAllowThrottle = new();

    /// <summary>Full path after <see cref="Bootstrap"/>; may be null if logging could not be initialized.</summary>
    internal static string DiagnosticLogPath => _path;

    /// <summary>Append-only IPAM diagnostics next to the game <c>_Data</c> folder when <c>DHCPSwitches-ipam.flag</c> exists there.</summary>
    internal static string IpamDiagnosticLogPath => _ipamPath;

    /// <summary>Checked every ~2s; create empty <c>DHCPSwitches-ipam.flag</c> beside the log to enable <see cref="WriteIpam"/>.</summary>
    internal static bool IsIpamFileLogEnabled
    {
        get
        {
            var now = DateTime.UtcNow;
            if (now < _ipamFlagCacheUntilUtc)
            {
                return _ipamFlagCached;
            }

            _ipamFlagCacheUntilUtc = now.AddSeconds(2);
            try
            {
                Bootstrap();
                if (string.IsNullOrEmpty(_path))
                {
                    _ipamFlagCached = false;
                    return false;
                }

                var dir = Path.GetDirectoryName(_path);
                if (string.IsNullOrEmpty(dir))
                {
                    _ipamFlagCached = false;
                    return false;
                }

                _ipamFlagCached = File.Exists(Path.Combine(dir, "DHCPSwitches-ipam.flag"));
                if (_ipamFlagCached)
                {
                    BootstrapIpamPath();
                }
            }
            catch
            {
                _ipamFlagCached = false;
            }

            return _ipamFlagCached;
        }
    }

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

                _path = Path.Combine(dir, "DHCPSwitches-debug.log");
                var banner =
                    $"======== DHCPSwitches debug session (new file each game launch) {DateTime.UtcNow:u} ========\r\n";
                File.WriteAllText(_path, banner);
            }
            catch
            {
                _path = null;
            }
        }
    }

    private static void BootstrapIpamPath()
    {
        lock (Sync)
        {
            if (_ipamInitTried)
            {
                return;
            }

            _ipamInitTried = true;
            try
            {
                var dir = Path.GetDirectoryName(Application.dataPath);
                if (string.IsNullOrEmpty(dir))
                {
                    return;
                }

                _ipamPath = Path.Combine(dir, "DHCPSwitches-ipam.log");
            }
            catch
            {
                _ipamPath = null;
            }
        }
    }

    /// <summary>Writes to <see cref="IpamDiagnosticLogPath"/> when <see cref="IsIpamFileLogEnabled"/> is true.</summary>
    internal static void WriteIpam(string message)
    {
        if (string.IsNullOrEmpty(message) || !IsIpamFileLogEnabled)
        {
            return;
        }

        BootstrapIpamPath();
        if (string.IsNullOrEmpty(_ipamPath))
        {
            return;
        }

        var line = $"{DateTime.UtcNow:HH:mm:ss.fff} {message}\r\n";
        lock (Sync)
        {
            try
            {
                if (!_ipamSessionBannerWritten)
                {
                    _ipamSessionBannerWritten = true;
                    File.AppendAllText(
                        _ipamPath,
                        $"\r\n======== DHCPSwitches IPAM debug {DateTime.UtcNow:u} ========\r\n");
                }

                File.AppendAllText(_ipamPath, line);
            }
            catch
            {
            }
        }
    }

    internal static void WriteLine(string message)
    {
        Bootstrap();
        if (string.IsNullOrEmpty(_path))
        {
            return;
        }

        var line = $"{DateTime.UtcNow:HH:mm:ss.fff} {message}\r\n";
        lock (Sync)
        {
            try
            {
                File.AppendAllText(_path, line);
            }
            catch
            {
                // ignore disk / permission errors
            }
        }
    }

    /// <summary>True when <c>DHCPSwitches-setip.flag</c> exists next to the main debug log (checked every ~2s).</summary>
    internal static bool IsSetIpKeypadInspectEnabled
    {
        get
        {
            var now = DateTime.UtcNow;
            if (now < _setIpFlagCacheUntilUtc)
            {
                return _setIpFlagCached;
            }

            Bootstrap();
            _setIpFlagCacheUntilUtc = now.AddSeconds(2);
            try
            {
                if (string.IsNullOrEmpty(_path))
                {
                    _setIpFlagCached = false;
                    return false;
                }

                var dir = Path.GetDirectoryName(_path);
                if (string.IsNullOrEmpty(dir))
                {
                    _setIpFlagCached = false;
                    return false;
                }

                _setIpFlagCached = File.Exists(Path.Combine(dir, "DHCPSwitches-setip.flag"));
            }
            catch
            {
                _setIpFlagCached = false;
            }

            return _setIpFlagCached;
        }
    }

    /// <summary>True when <c>DHCPSwitches-setip-dhcp.flag</c> exists beside <c>_Data</c> (checked every ~2s).</summary>
    internal static bool IsSetIpKeypadDhcpLogEnabled
    {
        get
        {
            var now = DateTime.UtcNow;
            if (now < _setIpDhcpFlagCacheUntilUtc)
            {
                return _setIpDhcpFlagCached;
            }

            Bootstrap();
            _setIpDhcpFlagCacheUntilUtc = now.AddSeconds(2);
            try
            {
                if (string.IsNullOrEmpty(_path))
                {
                    _setIpDhcpFlagCached = false;
                    return false;
                }

                var dir = Path.GetDirectoryName(_path);
                if (string.IsNullOrEmpty(dir))
                {
                    _setIpDhcpFlagCached = false;
                    return false;
                }

                _setIpDhcpFlagCached = File.Exists(Path.Combine(dir, "DHCPSwitches-setip-dhcp.flag"));
            }
            catch
            {
                _setIpDhcpFlagCached = false;
            }

            return _setIpDhcpFlagCached;
        }
    }

    /// <summary>
    /// When true, <see cref="WriteThrottledIopsAllow"/> includes per-server IP samples (noisy). Default false — counts only.
    /// </summary>
    internal static bool IsIopsAllowIpSamplesEnabled
    {
        get
        {
            var now = DateTime.UtcNow;
            if (now < _iopsAllowIpsFlagCacheUntilUtc)
            {
                return _iopsAllowIpsFlagCached;
            }

            Bootstrap();
            _iopsAllowIpsFlagCacheUntilUtc = now.AddSeconds(2);
            try
            {
                if (string.IsNullOrEmpty(_path))
                {
                    _iopsAllowIpsFlagCached = false;
                    return false;
                }

                var dir = Path.GetDirectoryName(_path);
                if (string.IsNullOrEmpty(dir))
                {
                    _iopsAllowIpsFlagCached = false;
                    return false;
                }

                _iopsAllowIpsFlagCached = File.Exists(Path.Combine(dir, "DHCPSwitches-iops-allow-ips.flag"));
            }
            catch
            {
                _iopsAllowIpsFlagCached = false;
            }

            return _iopsAllowIpsFlagCached;
        }
    }

    /// <summary>True when <c>DHCPSwitches-trace.flag</c> exists next to the main debug log (checked every ~2s).</summary>
    internal static bool IsTraceEnabled
    {
        get
        {
            var now = DateTime.UtcNow;
            if (now < _traceCacheUntilUtc)
            {
                return _traceCached;
            }

            Bootstrap();
            _traceCacheUntilUtc = now.AddSeconds(2);
            try
            {
                if (string.IsNullOrEmpty(_path))
                {
                    _traceCached = false;
                    return false;
                }

                var dir = Path.GetDirectoryName(_path);
                if (string.IsNullOrEmpty(dir))
                {
                    _traceCached = false;
                    return false;
                }

                _traceCached = File.Exists(Path.Combine(dir, "DHCPSwitches-trace.flag"));
            }
            catch
            {
                _traceCached = false;
            }

            return _traceCached;
        }
    }

    internal static void Trace(string component, string message)
    {
        if (!IsTraceEnabled)
        {
            return;
        }

        WriteLine($"[trace:{component}] {message}");
    }

    /// <summary>
    /// Always writes to <see cref="DiagnosticLogPath"/> when the mod blocks <c>AddAppPerformance</c>, throttled so ticks do not flood the disk.
    /// </summary>
    internal static void WriteThrottledIopsDeny(int customerId, string reason, float minIntervalSec = 8f)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return;
        }

        Bootstrap();
        if (string.IsNullOrEmpty(_path))
        {
            return;
        }

        var now = Time.realtimeSinceStartup;
        lock (Sync)
        {
            if (IopsDenyThrottle.TryGetValue(customerId, out var prev)
                && reason == prev.Reason
                && now - prev.Time < minIntervalSec)
            {
                return;
            }

            IopsDenyThrottle[customerId] = (now, reason);
        }

        WriteLine($"IOPS BLOCKED customerID={customerId}: {reason}");
    }

    /// <summary>Confirms the mod is not blocking IOPS (throttled).</summary>
    internal static void WriteThrottledIopsAllow(int customerId, string detail, float minIntervalSec = 40f)
    {
        Bootstrap();
        if (string.IsNullOrEmpty(_path))
        {
            return;
        }

        var now = Time.realtimeSinceStartup;
        lock (Sync)
        {
            if (IopsAllowThrottle.TryGetValue(customerId, out var t) && now - t < minIntervalSec)
            {
                return;
            }

            IopsAllowThrottle[customerId] = now;
        }

        WriteLine($"IOPS ALLOW customerID={customerId}: {detail}");
    }
}
