using System;
using UnityEngine;

namespace DHCPSwitches;

/// <summary>
/// Uses the game's own technician entry points (same as Assembly-CSharp):
/// <list type="number">
/// <item><description><see cref="AssetManagementDeviceLine.ButtonSendTechnician"/> when a list row references the device</description></item>
/// <item><description><see cref="TechnicianManager.SendTechnician"/> via <see cref="TechnicianManager.instance"/> (or scene search) — this is the actual job queue</description></item>
/// <item><description><see cref="AssetManagement.SendTechnician"/> for the asset UI flow (confirm / price overlay)</description></item>
/// </list>
/// Reflection fallbacks for "send technician" are disabled in <see cref="DeviceInventoryReflection.TrySendTechnician"/> because they were invoking unrelated methods (EOL, replace, etc.).
/// </summary>
internal static class GameTechnicianDispatch
{
    /// <summary>
    /// <paramref name="device"/> must be a <see cref="Server"/> or <see cref="NetworkSwitch"/>.
    /// </summary>
    /// <returns><see langword="true"/> if a game API call completed without throwing.</returns>
    internal static bool TryDispatch(UnityEngine.Object device, out string detail)
    {
        detail = null;
        if (device == null)
        {
            return false;
        }

        var sw = device as NetworkSwitch;
        var srv = device as Server;
        if (sw == null && srv == null)
        {
            return false;
        }

        if (TryMatchingDeviceLineButton(sw, srv, requireActive: true, out detail))
        {
            return true;
        }

        if (TryMatchingDeviceLineButton(sw, srv, requireActive: false, out detail))
        {
            return true;
        }

        if (TryTechnicianManagerSendTechnician(sw, srv, out detail))
        {
            return true;
        }

        return TryAssetManagementSendTechnician(sw, srv, out detail);
    }

    private static bool TryMatchingDeviceLineButton(NetworkSwitch sw, Server srv, bool requireActive, out string detail)
    {
        detail = null;
        try
        {
            var lines = UnityEngine.Object.FindObjectsOfType<AssetManagementDeviceLine>(true);
            if (lines == null || lines.Length == 0)
            {
                return false;
            }

            foreach (var line in lines)
            {
                if (line == null)
                {
                    continue;
                }

                var matchServer = srv != null && ReferenceEquals(line.server, srv);
                var matchSwitch = sw != null && ReferenceEquals(line.networkSwitch, sw);
                if (!matchServer && !matchSwitch)
                {
                    continue;
                }

                if (requireActive && !line.isActiveAndEnabled)
                {
                    continue;
                }

                line.ButtonSendTechnician();
                detail = requireActive
                    ? "AssetManagementDeviceLine.ButtonSendTechnician() (active line)"
                    : "AssetManagementDeviceLine.ButtonSendTechnician() (matched reference)";
                return true;
            }
        }
        catch (Exception ex)
        {
            ModDebugLog.Bootstrap();
            ModDebugLog.WriteLine("GameTechnicianDispatch device line: " + ex);
        }

        return false;
    }

    private static bool TryTechnicianManagerSendTechnician(NetworkSwitch sw, Server srv, out string detail)
    {
        detail = null;
        try
        {
            var tm = TechnicianManager.instance;
            if (tm == null)
            {
                var arr = UnityEngine.Object.FindObjectsOfType<TechnicianManager>(true);
                if (arr != null)
                {
                    foreach (var x in arr)
                    {
                        if (x != null && x.isActiveAndEnabled)
                        {
                            tm = x;
                            break;
                        }
                    }

                    if (tm == null)
                    {
                        foreach (var x in arr)
                        {
                            if (x != null)
                            {
                                tm = x;
                                break;
                            }
                        }
                    }
                }
            }

            if (tm == null)
            {
                return false;
            }

            tm.SendTechnician(sw, srv);
            detail = "TechnicianManager.SendTechnician";
            return true;
        }
        catch (Exception ex)
        {
            ModDebugLog.Bootstrap();
            ModDebugLog.WriteLine("GameTechnicianDispatch TechnicianManager: " + ex);
        }

        return false;
    }

    private static bool TryAssetManagementSendTechnician(NetworkSwitch sw, Server srv, out string detail)
    {
        detail = null;
        AssetManagement[] managers;
        try
        {
            managers = UnityEngine.Object.FindObjectsOfType<AssetManagement>(true);
        }
        catch (Exception ex)
        {
            ModDebugLog.Bootstrap();
            ModDebugLog.WriteLine("GameTechnicianDispatch Find AssetManagement: " + ex);
            return false;
        }

        if (managers == null || managers.Length == 0)
        {
            return false;
        }

        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var am in managers)
            {
                if (am == null)
                {
                    continue;
                }

                if (pass == 0 && !am.isActiveAndEnabled)
                {
                    continue;
                }

                try
                {
                    am.SendTechnician(sw, srv);
                    detail = pass == 0
                        ? "AssetManagement.SendTechnician (active)"
                        : "AssetManagement.SendTechnician (fallback)";
                    return true;
                }
                catch (Exception ex)
                {
                    ModDebugLog.Bootstrap();
                    ModDebugLog.WriteLine($"GameTechnicianDispatch AssetManagement.SendTechnician: {ex.Message}");
                }
            }
        }

        return false;
    }
}
