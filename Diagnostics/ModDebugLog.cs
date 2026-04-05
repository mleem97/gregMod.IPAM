using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DHCPSwitches;

/// <summary>
/// Append-only log in the game install folder (directory containing the <c>_Data</c> folder) for field debugging.
/// Create an empty file <c>DHCPSwitches-trace.flag</c> in that same folder to enable verbose step traces
/// (<c>[trace:…]</c> lines) for ping routing, cable checks, and IOPS reachability.
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
                File.AppendAllText(
                    _path,
                    $"\r\n======== DHCPSwitches debug {DateTime.UtcNow:u} ========\r\n");
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
