using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

namespace DHCPSwitches;

/// <summary>
/// Debug log in the game install folder (directory containing the <c>_Data</c> folder). The file <c>DHCPSwitches-debug.log</c>
/// is replaced (truncated) at the start of each game launch; later lines append during that session.
/// Create an empty file <c>DHCPSwitches-trace.flag</c> in that same folder to enable verbose step traces
/// (<c>[trace:…]</c> lines) for ping routing, cable checks, and IOPS reachability.
/// <c>IOPS ALLOW</c> lines in this file omit per-server IP samples by default; create <c>DHCPSwitches-iops-allow-ips.flag</c> to restore the long sample (server=name=ip) on those lines.
/// IPAM UI diagnostics (IOPS toolbar, OnGUI mouse) go to <c>DHCPSwitches-ipam.log</c> when <c>DHCPSwitches-ipam.flag</c>
/// is present — see <see cref="IpamDebugLog"/>.
/// Batch DHCP from IPAM (e.g. &quot;DHCP all selected&quot;) always writes <c>dhcp-assign:</c> lines to this file; create
/// <c>DHCPSwitches-dhcp-assign.flag</c> beside <c>_Data</c> for extra subnet/reflection detail on those lines.
/// During explicit IPAM DHCP (assign all / batch / single), <c>dhcp-step:</c> lines record try-order construction and per-CIDR picks.
/// Create <c>DHCPSwitches-dhcp-trace.flag</c> for maximum detail (every string field scanned, every usable-IP skip reason).
/// IPAM perf: use the <b>Perf: on/off</b> button in the IPAM toolbar, or create <c>DHCPSwitches-ipam-perf.flag</c> beside <c>_Data</c>; throttled lines append to <c>DHCPSwitches-ipam-perf.log</c>.
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
    private static DateTime _ipamPerfFlagCacheUntilUtc;
    private static bool _ipamPerfFlagCached;
    private static bool _ipamPerfLogBannerWritten;

    /// <summary>When true (set from the IPAM toolbar), performance samples append to <c>DHCPSwitches-ipam-perf.log</c> without any flag file.</summary>
    public static bool IpamPerfRuntimeEnabled { get; set; }
    private static DateTime _iopsAllowIpsFlagCacheUntilUtc;
    private static bool _iopsAllowIpsFlagCached;
    private static DateTime _dhcpAssignVerboseFlagCacheUntilUtc;
    private static bool _dhcpAssignVerboseFlagCached;
    private static DateTime _dhcpStepTraceFlagCacheUntilUtc;
    private static bool _dhcpStepTraceFlagCached;
    private static int _dhcpResolutionBatchDepth;
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

    /// <summary>
    /// True when <see cref="IpamPerfRuntimeEnabled"/> is on or <c>DHCPSwitches-ipam-perf.flag</c> exists beside <c>_Data</c> (flag checked every ~2s).
    /// Throttled lines go to <c>DHCPSwitches-ipam-perf.log</c>.
    /// </summary>
    internal static bool IsIpamPerfLoggingEnabled
    {
        get
        {
            if (IpamPerfRuntimeEnabled)
            {
                return true;
            }

            var now = DateTime.UtcNow;
            if (now < _ipamPerfFlagCacheUntilUtc)
            {
                return _ipamPerfFlagCached;
            }

            _ipamPerfFlagCacheUntilUtc = now.AddSeconds(2);
            try
            {
                var dir = Path.GetDirectoryName(Application.dataPath);
                if (string.IsNullOrEmpty(dir))
                {
                    _ipamPerfFlagCached = false;
                    return false;
                }

                _ipamPerfFlagCached = File.Exists(Path.Combine(dir, "DHCPSwitches-ipam-perf.flag"));
            }
            catch
            {
                _ipamPerfFlagCached = false;
            }

            return _ipamPerfFlagCached;
        }
    }

    /// <summary>Full path to the IPAM performance append log (same folder as <c>DHCPSwitches-debug.log</c>).</summary>
    internal static string GetIpamPerfLogPath()
    {
        try
        {
            var dir = Path.GetDirectoryName(Application.dataPath);
            return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "DHCPSwitches-ipam-perf.log");
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Append-only; enable with <see cref="IsIpamPerfLoggingEnabled"/>.</summary>
    internal static void WriteIpamPerf(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || !IsIpamPerfLoggingEnabled)
        {
            return;
        }

        try
        {
            var dir = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(dir))
            {
                return;
            }

            var perfPath = Path.Combine(dir, "DHCPSwitches-ipam-perf.log");
            lock (Sync)
            {
                if (!_ipamPerfLogBannerWritten)
                {
                    _ipamPerfLogBannerWritten = true;
                    File.AppendAllText(
                        perfPath,
                        $"\r\n======== DHCPSwitches IPAM performance log {DateTime.UtcNow:u} ========\r\n");
                }

                File.AppendAllText(perfPath, $"{DateTime.UtcNow:HH:mm:ss.fff} {message}\r\n");
            }
        }
        catch
        {
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

    /// <summary>True when <c>DHCPSwitches-dhcp-assign.flag</c> exists beside the debug log (checked every ~2s).</summary>
    internal static bool IsDhcpAssignVerboseEnabled
    {
        get
        {
            var now = DateTime.UtcNow;
            if (now < _dhcpAssignVerboseFlagCacheUntilUtc)
            {
                return _dhcpAssignVerboseFlagCached;
            }

            Bootstrap();
            _dhcpAssignVerboseFlagCacheUntilUtc = now.AddSeconds(2);
            try
            {
                if (string.IsNullOrEmpty(_path))
                {
                    _dhcpAssignVerboseFlagCached = false;
                    return false;
                }

                var dir = Path.GetDirectoryName(_path);
                if (string.IsNullOrEmpty(dir))
                {
                    _dhcpAssignVerboseFlagCached = false;
                    return false;
                }

                _dhcpAssignVerboseFlagCached = File.Exists(Path.Combine(dir, "DHCPSwitches-dhcp-assign.flag"));
            }
            catch
            {
                _dhcpAssignVerboseFlagCached = false;
            }

            return _dhcpAssignVerboseFlagCached;
        }
    }

    /// <summary>Writes a <c>dhcp-assign:</c> line to <c>DHCPSwitches-debug.log</c> (creates/truncates log on first bootstrap of the session).</summary>
    internal static void WriteDhcpAssign(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        WriteLine($"dhcp-assign: {message}");
    }

    /// <summary>Increment while running explicit DHCP from IPAM so <see cref="WriteDhcpStep"/> emits mid-level resolution logs.</summary>
    internal static void EnterDhcpResolutionBatch()
    {
        Interlocked.Increment(ref _dhcpResolutionBatchDepth);
    }

    internal static void LeaveDhcpResolutionBatch()
    {
        Interlocked.Decrement(ref _dhcpResolutionBatchDepth);
    }

    /// <summary>True during <see cref="EnterDhcpResolutionBatch"/> scope.</summary>
    internal static bool IsDhcpResolutionStepLogging => _dhcpResolutionBatchDepth > 0;

    /// <summary>True when <c>DHCPSwitches-dhcp-trace.flag</c> exists beside the debug log (checked every ~2s).</summary>
    internal static bool IsDhcpStepTraceEnabled
    {
        get
        {
            var now = DateTime.UtcNow;
            if (now < _dhcpStepTraceFlagCacheUntilUtc)
            {
                return _dhcpStepTraceFlagCached;
            }

            Bootstrap();
            _dhcpStepTraceFlagCacheUntilUtc = now.AddSeconds(2);
            try
            {
                if (string.IsNullOrEmpty(_path))
                {
                    _dhcpStepTraceFlagCached = false;
                    return false;
                }

                var dir = Path.GetDirectoryName(_path);
                if (string.IsNullOrEmpty(dir))
                {
                    _dhcpStepTraceFlagCached = false;
                    return false;
                }

                _dhcpStepTraceFlagCached = File.Exists(Path.Combine(dir, "DHCPSwitches-dhcp-trace.flag"));
            }
            catch
            {
                _dhcpStepTraceFlagCached = false;
            }

            return _dhcpStepTraceFlagCached;
        }
    }

    /// <summary>Mid-level DHCP resolution (try-order phases, usable counts, pick result). Batch scope or trace flag.</summary>
    internal static void WriteDhcpStep(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        if (!IsDhcpResolutionStepLogging && !IsDhcpStepTraceEnabled)
        {
            return;
        }

        WriteLine($"dhcp-step: {message}");
    }

    /// <summary>Low-level DHCP (per field, per skipped IP). Requires <c>DHCPSwitches-dhcp-trace.flag</c>.</summary>
    internal static void WriteDhcpTrace(string message)
    {
        if (string.IsNullOrEmpty(message) || !IsDhcpStepTraceEnabled)
        {
            return;
        }

        WriteLine($"dhcp-trace: {message}");
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
