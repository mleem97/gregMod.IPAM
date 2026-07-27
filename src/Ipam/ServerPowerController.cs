using System;
using System.Linq;
using Il2Cpp;

namespace GregModIPAM;

/// <summary>
/// Controls server power state via Game-API (Server.TurnOnCommonFunction / TurnOffCommonFunctions).
/// Power state is tracked in CablingDataStore (mod-eigen, nicht im Game-Savefile).
/// </summary>
internal static class ServerPowerController
{
    internal static bool TryFindRackMountForServer(int serverInstanceId, out string rackId, out RackMountRecord mount)
    {
        rackId = null;
        mount = null;
        if (serverInstanceId == 0)
        {
            return false;
        }

        try
        {
            var racks = RackDataStore.GetRacks();
            if (racks == null)
            {
                return false;
            }

            foreach (var rack in racks)
            {
                if (rack?.Mounts == null || string.IsNullOrEmpty(rack.Id))
                {
                    continue;
                }

                foreach (var m in rack.Mounts)
                {
                    if (m == null
                        || !string.Equals(m.DeviceType, RackDeviceTypes.Server, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (m.SceneInstanceId != serverInstanceId && m.ServerInstanceId != serverInstanceId)
                    {
                        continue;
                    }

                    rackId = rack.Id;
                    mount = m;
                    return true;
                }
            }
        }
        catch
        {
            // ignore lookup failures and fall back to non-rack behavior
        }

        return false;
    }

    internal static bool? TryGetTrackedServerPowerState(int serverInstanceId)
    {
        return TryFindRackMountForServer(serverInstanceId, out var rackId, out var mount)
            ? CablingDataStore.IsPoweredOn(rackId, mount.EntryId)
            : null;
    }

    internal static bool TryToggleServerByInstanceId(int serverInstanceId)
    {
        return TryFindRackMountForServer(serverInstanceId, out var rackId, out var mount)
            && TryTogglePower(rackId, mount);
    }

    /// <summary>Turns a server on via game API and updates mod state.</summary>
    internal static bool TryPowerOn(string rackId, RackMountRecord mount)
    {
        if (mount == null) return false;

        // Check if device has network connection (warning, not blocking)
        var connections = CablingDataStore.GetConnectionsForDevice(rackId, mount.EntryId);
        if (connections.Count == 0)
        {
            ModLogging.Warning($"Power-On: {mount.DisplayName} has no network connections.");
        }

        // Call game API
        var server = FindServerByInstanceId(mount.SceneInstanceId);
        if (server != null)
        {
            try
            {
                server.TurnOnCommonFunction();
                ModLogging.Msg($"Game Power-ON: {mount.DisplayName}");
            }
            catch (Exception ex)
            {
                ModLogging.Warning($"Game Power-ON failed for {mount.DisplayName}: {ex.Message}");
            }
        }

        // Update mod state (always, even if game API fails)
        CablingDataStore.SetPowerState(rackId, mount.EntryId, true);
        return true;
    }

    /// <summary>Turns a server off via game API and updates mod state.</summary>
    internal static bool TryPowerOff(string rackId, RackMountRecord mount)
    {
        if (mount == null) return false;

        var server = FindServerByInstanceId(mount.SceneInstanceId);
        if (server != null)
        {
            try
            {
                server.TurnOffCommonFunctions();
                ModLogging.Msg($"Game Power-OFF: {mount.DisplayName}");
            }
            catch (Exception ex)
            {
                ModLogging.Warning($"Game Power-OFF failed for {mount.DisplayName}: {ex.Message}");
            }
        }

        CablingDataStore.SetPowerState(rackId, mount.EntryId, false);
        return true;
    }

    /// <summary>Toggles power state.</summary>
    internal static bool TryTogglePower(string rackId, RackMountRecord mount)
    {
        if (mount == null) return false;

        var isOn = CablingDataStore.IsPoweredOn(rackId, mount.EntryId);
        return isOn ? TryPowerOff(rackId, mount) : TryPowerOn(rackId, mount);
    }

    /// <summary>Powers on all servers in a rack.</summary>
    internal static int PowerOnAll(string rackId)
    {
        var rack = RackDataStore.FindById(rackId);
        if (rack?.Mounts == null) return 0;

        var count = 0;
        foreach (var m in rack.Mounts)
        {
            if (string.Equals(m.DeviceType, RackDeviceTypes.Server, StringComparison.OrdinalIgnoreCase))
            {
                if (TryPowerOn(rackId, m)) count++;
            }
        }
        return count;
    }

    /// <summary>Powers off all servers in a rack.</summary>
    internal static int PowerOffAll(string rackId)
    {
        var rack = RackDataStore.FindById(rackId);
        if (rack?.Mounts == null) return 0;

        var count = 0;
        foreach (var m in rack.Mounts)
        {
            if (string.Equals(m.DeviceType, RackDeviceTypes.Server, StringComparison.OrdinalIgnoreCase))
            {
                if (TryPowerOff(rackId, m)) count++;
            }
        }
        return count;
    }

    // ── Helpers ──

    private static Server FindServerByInstanceId(int instanceId)
    {
        if (instanceId == 0) return null;
        try
        {
            var servers = UnityEngine.Object.FindObjectsOfType<Server>();
            if (servers == null) return null;
            foreach (var s in servers)
            {
                try { if (s.GetInstanceID() == instanceId) return s; } catch { }
            }
        }
        catch { }
        return null;
    }
}
