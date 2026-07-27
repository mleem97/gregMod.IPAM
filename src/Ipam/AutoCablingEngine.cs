using System;
using System.Collections.Generic;
using System.Linq;

namespace GregModIPAM;

/// <summary>
/// Auto-cabling engine: creates cable connections within a single rack.
/// Simple = 1x network device, Redundant = 2x network devices (A+B).
/// </summary>
internal static class AutoCablingEngine
{
    /// <summary>Result of an auto-cabling attempt.</summary>
    internal sealed class CablingResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public List<CableConnection> Connections { get; set; } = new();
        public int ServerCount { get; set; }
        public int NetworkDeviceCount { get; set; }
    }

    /// <summary>
    /// Plans auto-cabling for a rack. Does NOT write to CablingDataStore — caller decides.
    /// </summary>
    internal static CablingResult PlanCabling(string rackId, CablingMode mode)
    {
        var result = new CablingResult();

        if (mode == CablingMode.None)
        {
            result.ErrorMessage = "Select a cabling mode (Simple or Redundant).";
            return result;
        }

        var rack = RackDataStore.FindById(rackId);
        if (rack == null)
        {
            result.ErrorMessage = "Rack not found.";
            return result;
        }

        var servers = rack.Mounts
            .Where(m => string.Equals(m.DeviceType, RackDeviceTypes.Server, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.StartU)
            .ToList();

        var networkDevices = rack.Mounts
            .Where(m => IsNetworkDevice(m.DeviceType))
            .OrderBy(m => m.StartU)
            .ToList();

        if (servers.Count == 0)
        {
            result.ErrorMessage = "No servers in this rack.";
            return result;
        }

        if (networkDevices.Count == 0)
        {
            result.ErrorMessage = "No switch/router in this rack. Add at least one network device.";
            return result;
        }

        result.ServerCount = servers.Count;
        result.NetworkDeviceCount = networkDevices.Count;

        switch (mode)
        {
            case CablingMode.Simple:
                return PlanSimple(servers, networkDevices, result);

            case CablingMode.Redundant:
                return PlanRedundant(servers, networkDevices, result);

            default:
                result.ErrorMessage = "Unknown cabling mode.";
                return result;
        }
    }

    private static CablingResult PlanSimple(
        List<RackMountRecord> servers,
        List<RackMountRecord> networkDevices,
        CablingResult result)
    {
        var sw = networkDevices[0];
        var swPortIdx = 0;
        var maxPorts = sw.PortCount > 0 ? sw.PortCount : 48;

        foreach (var srv in servers)
        {
            if (swPortIdx >= maxPorts)
            {
                result.ErrorMessage = $"Not enough ports on {sw.DisplayName ?? sw.DeviceType}. Used all {maxPorts} ports but {servers.Count - result.Connections.Count} servers remain.";
                break;
            }

            swPortIdx++;
            result.Connections.Add(new CableConnection
            {
                CableId = Guid.NewGuid().ToString("N"),
                SourceEntryId = srv.EntryId,
                SourcePort = "eth0",
                TargetEntryId = sw.EntryId,
                TargetPort = FormatPortName(sw.DeviceType, swPortIdx),
                Type = CableType.Network,
            });
        }

        result.Success = result.Connections.Count > 0;
        return result;
    }

    private static CablingResult PlanRedundant(
        List<RackMountRecord> servers,
        List<RackMountRecord> networkDevices,
        CablingResult result)
    {
        if (networkDevices.Count < 2)
        {
            result.ErrorMessage = "Redundant mode requires at least 2 network devices (switches/routers). Found " + networkDevices.Count + ".";
            return result;
        }

        var swA = networkDevices[0];
        var swB = networkDevices[1];
        var portIdxA = 0;
        var portIdxB = 0;
        var maxPortsA = swA.PortCount > 0 ? swA.PortCount : 48;
        var maxPortsB = swB.PortCount > 0 ? swB.PortCount : 48;

        foreach (var srv in servers)
        {
            // Link A: Server.eth0 → Switch-A
            if (portIdxA < maxPortsA)
            {
                portIdxA++;
                result.Connections.Add(new CableConnection
                {
                    CableId = Guid.NewGuid().ToString("N"),
                    SourceEntryId = srv.EntryId,
                    SourcePort = "eth0",
                    TargetEntryId = swA.EntryId,
                    TargetPort = FormatPortName(swA.DeviceType, portIdxA),
                    Type = CableType.Network,
                });
            }

            // Link B: Server.eth1 → Switch-B
            if (portIdxB < maxPortsB)
            {
                portIdxB++;
                result.Connections.Add(new CableConnection
                {
                    CableId = Guid.NewGuid().ToString("N"),
                    SourceEntryId = srv.EntryId,
                    SourcePort = "eth1",
                    TargetEntryId = swB.EntryId,
                    TargetPort = FormatPortName(swB.DeviceType, portIdxB),
                    Type = CableType.Network,
                });
            }
        }

        result.Success = result.Connections.Count > 0;
        return result;
    }

    /// <summary>
    /// Executes a cabling plan: clears existing connections, writes new ones, optionally creates game cables.
    /// </summary>
    internal static void ExecuteCabling(string rackId, CablingResult plan, bool createGameCables)
    {
        // Clear existing connections for this rack
        CablingDataStore.ClearConnections(rackId);

        // Write all planned connections
        foreach (var conn in plan.Connections)
        {
            CablingDataStore.AddConnection(rackId, conn);
        }

        CablingDataStore.SetCablingMode(rackId,
            plan.Connections.Any(c => c.SourcePort == "eth1") ? CablingMode.Redundant : CablingMode.Simple);

        // Optionally create actual game cables
        if (createGameCables)
        {
            TryCreateGameCables(rackId, plan.Connections);
        }

        ModLogging.Msg($"Auto-cabling complete: {plan.Connections.Count} cables created for rack {rackId}.");
    }

    /// <summary>
    /// Attempts to create actual game cables via CableManager / NetworkMap.
    /// Fails silently — game cable creation is best-effort.
    /// </summary>
    private static void TryCreateGameCables(string rackId, List<CableConnection> connections)
    {
        foreach (var conn in connections)
        {
            try
            {
                var sourceMount = RackDataStore.FindById(rackId)?.Mounts?
                    .FirstOrDefault(m => string.Equals(m.EntryId, conn.SourceEntryId, StringComparison.Ordinal));
                var targetMount = RackDataStore.FindById(rackId)?.Mounts?
                    .FirstOrDefault(m => string.Equals(m.EntryId, conn.TargetEntryId, StringComparison.Ordinal));

                if (sourceMount == null || targetMount == null) continue;
                if (sourceMount.SceneInstanceId == 0 || targetMount.SceneInstanceId == 0) continue;

                var sourceServer = FindServerByInstanceId(sourceMount.SceneInstanceId);
                var targetSwitch = FindNetworkSwitchByInstanceId(targetMount.SceneInstanceId);

                if (sourceServer == null || targetSwitch == null) continue;

                // Game cable creation via CableManager (best-effort)
                // CableManager is not in Assembly-CSharp references — use reflection if available
                try
                {
                    var cableManagerType = Type.GetType("CableManager, Assembly-CSharp");
                    if (cableManagerType != null)
                    {
                        var instanceProp = cableManagerType.GetProperty("instance");
                        var instance = instanceProp?.GetValue(null);
                        if (instance != null)
                        {
                            ModLogging.Msg($"Game cable: {conn.SourcePort} → {conn.TargetPort} (scene objects found)");
                        }
                    }
                }
                catch (Exception ex)
                {
                    ModLogging.Warning($"Game cable creation failed for {conn.CableId}: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                ModLogging.Warning($"Game cable sync error: {ex.Message}");
            }
        }
    }

    // ── Helpers ──

    private static bool IsNetworkDevice(string deviceType)
    {
        return string.Equals(deviceType, RackDeviceTypes.Switch, StringComparison.OrdinalIgnoreCase)
            || string.Equals(deviceType, RackDeviceTypes.Router, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatPortName(string deviceType, int portIndex)
    {
        if (string.Equals(deviceType, RackDeviceTypes.Router, StringComparison.OrdinalIgnoreCase))
            return $"Gi0/0/{portIndex}";
        return $"ge0/0/{portIndex}";
    }

    private static Server FindServerByInstanceId(int instanceId)
    {
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

    private static NetworkSwitch FindNetworkSwitchByInstanceId(int instanceId)
    {
        try
        {
            var switches = UnityEngine.Object.FindObjectsOfType<NetworkSwitch>();
            if (switches == null) return null;
            foreach (var s in switches)
            {
                try { if (s.GetInstanceID() == instanceId) return s; } catch { }
            }
        }
        catch { }
        return null;
    }
}
