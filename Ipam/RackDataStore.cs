using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnityEngine;

namespace DHCPSwitches;

/// <summary>Persists user-defined racks and device mounts under UserData/DHCPSwitches/rack_data.json.</summary>
internal static class RackDataStore
{
    /// <summary>All racks in this mod use a standard 47 U cabinet.</summary>
    internal const int RackStandardHeightU = 47;

    private const int FileVersion = 1;
    private const string SubDir = "DHCPSwitches";
    private const string FileName = "rack_data.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static RackPersistedRoot _root;
    private static bool _loaded;

    private static string GetPath()
    {
        try
        {
            var dataPath = Application.dataPath;
            if (!string.IsNullOrEmpty(dataPath))
            {
                var rootDir = Path.GetDirectoryName(dataPath);
                if (!string.IsNullOrEmpty(rootDir))
                {
                    return Path.Combine(rootDir, "UserData", SubDir, FileName);
                }
            }
        }
        catch
        {
            // fall through
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, SubDir, FileName);
    }

    private static void NormalizeAfterLoad()
    {
        var root = EnsureLoaded();
        foreach (var r in root.Racks)
        {
            if (r == null)
            {
                continue;
            }

            r.TotalU = RackStandardHeightU;
            if (r.Mounts == null)
            {
                continue;
            }

            foreach (var m in r.Mounts)
            {
                if (m == null)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(m.EntryId))
                {
                    m.EntryId = Guid.NewGuid().ToString("D");
                }

                if (string.IsNullOrEmpty(m.DeviceType))
                {
                    m.DeviceType = m.ServerInstanceId != 0 ? RackDeviceTypes.Server : RackDeviceTypes.PatchPanel;
                }

                if (string.Equals(m.DeviceType, RackDeviceTypes.Server, StringComparison.OrdinalIgnoreCase))
                {
                    if (m.SceneInstanceId == 0 && m.ServerInstanceId != 0)
                    {
                        m.SceneInstanceId = m.ServerInstanceId;
                    }

                    m.ServerInstanceId = m.SceneInstanceId;
                }

                if (m.HeightU <= 0)
                {
                    m.HeightU = RackMountHeights.HeightForType(m.DeviceType);
                }
            }
        }
    }

    private static RackPersistedRoot EnsureLoaded()
    {
        if (_loaded)
        {
            return _root;
        }

        _loaded = true;
        _root = new RackPersistedRoot { Version = FileVersion, Racks = new List<RackDefinition>() };
        var path = GetPath();
        if (!File.Exists(path))
        {
            return _root;
        }

        try
        {
            var json = File.ReadAllText(path);
            var file = JsonSerializer.Deserialize<RackPersistedRoot>(json, JsonOptions);
            if (file != null)
            {
                file.Racks ??= new List<RackDefinition>();
                foreach (var r in file.Racks)
                {
                    r.Mounts ??= new List<RackMountRecord>();
                }

                _root = file;
                _root.Version = FileVersion;
                NormalizeAfterLoad();
            }
        }
        catch (Exception ex)
        {
            ModLogging.Warning($"Rack data load failed ({path}): {ex.Message}");
        }

        return _root;
    }

    internal static IReadOnlyList<RackDefinition> GetRacks()
    {
        return EnsureLoaded().Racks;
    }

    internal static RackDefinition FindById(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        return EnsureLoaded().Racks.FirstOrDefault(r => r != null && string.Equals(r.Id, id, StringComparison.Ordinal));
    }

    internal static bool TryAddRack(string displayName, out string id, out string error)
    {
        error = null;
        id = null;
        var name = (displayName ?? "").Trim();
        if (string.IsNullOrEmpty(name))
        {
            error = "Enter a rack name.";
            return false;
        }

        var rack = new RackDefinition
        {
            Id = Guid.NewGuid().ToString("D"),
            DisplayName = name,
            TotalU = RackStandardHeightU,
            Mounts = new List<RackMountRecord>(),
        };
        EnsureLoaded().Racks.Add(rack);
        id = rack.Id;
        Save();
        return true;
    }

    internal static bool TryUpdateRackName(string id, string displayName, out string error)
    {
        error = null;
        var rack = FindById(id);
        if (rack == null)
        {
            error = "Rack not found.";
            return false;
        }

        var name = (displayName ?? "").Trim();
        if (string.IsNullOrEmpty(name))
        {
            error = "Enter a rack name.";
            return false;
        }

        rack.DisplayName = name;
        rack.TotalU = RackStandardHeightU;
        Save();
        return true;
    }

    internal static bool TryDeleteRack(string id)
    {
        var root = EnsureLoaded();
        var n = root.Racks.RemoveAll(r => r != null && string.Equals(r.Id, id, StringComparison.Ordinal));
        if (n > 0)
        {
            Save();
            return true;
        }

        return false;
    }

    /// <summary>Adds a mounted device. <paramref name="sceneInstanceId"/> is server or switch id; 0 for patch panel only.</summary>
    internal static bool TryAddRackMount(
        string rackId,
        string deviceType,
        int sceneInstanceId,
        string patchLabel,
        int startU,
        int serverHeightU,
        out string error)
    {
        error = null;
        var rack = FindById(rackId);
        if (rack == null)
        {
            error = "Rack not found.";
            return false;
        }

        rack.TotalU = RackStandardHeightU;
        var ru = RackStandardHeightU;
        var dt = (deviceType ?? "").Trim();
        if (string.IsNullOrEmpty(dt))
        {
            dt = RackDeviceTypes.Server;
        }

        var h = RackMountHeights.HeightForMount(dt, serverHeightU);
        if (startU < 1 || startU > ru)
        {
            error = "Start U is out of range.";
            return false;
        }

        if (startU + h - 1 > ru)
        {
            error = "Device extends past the rack height.";
            return false;
        }

        if (string.Equals(dt, RackDeviceTypes.PatchPanel, StringComparison.OrdinalIgnoreCase))
        {
            sceneInstanceId = 0;
        }
        else if (sceneInstanceId == 0)
        {
            error = "Invalid device.";
            return false;
        }

        if (!MountOccupancyAvailable(rack, startU, h, null, out error))
        {
            return false;
        }

        if (!SceneDeviceNotDuplicatedElsewhere(rackId, dt, sceneInstanceId, out error))
        {
            return false;
        }

        var entry = Guid.NewGuid().ToString("D");
        var rec = new RackMountRecord
        {
            EntryId = entry,
            DeviceType = dt,
            SceneInstanceId = sceneInstanceId,
            ServerInstanceId = string.Equals(dt, RackDeviceTypes.Server, StringComparison.OrdinalIgnoreCase) ? sceneInstanceId : 0,
            PatchLabel = string.Equals(dt, RackDeviceTypes.PatchPanel, StringComparison.OrdinalIgnoreCase)
                ? (string.IsNullOrWhiteSpace(patchLabel) ? "Patch panel" : patchLabel.Trim())
                : null,
            StartU = startU,
            HeightU = h,
        };
        rack.Mounts.Add(rec);
        Save();
        return true;
    }

    private static bool SceneDeviceNotDuplicatedElsewhere(string currentRackId, string deviceType, int sceneInstanceId, out string error)
    {
        error = null;
        if (sceneInstanceId == 0)
        {
            return true;
        }

        foreach (var other in EnsureLoaded().Racks)
        {
            if (other?.Mounts == null)
            {
                continue;
            }

            foreach (var m in other.Mounts)
            {
                if (m.SceneInstanceId != sceneInstanceId)
                {
                    continue;
                }

                var sameKind =
                    string.Equals(deviceType, RackDeviceTypes.Server, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(m.DeviceType, RackDeviceTypes.Server, StringComparison.OrdinalIgnoreCase)
                    || IsSwitchFamily(deviceType) && IsSwitchFamily(m.DeviceType);

                if (!sameKind)
                {
                    continue;
                }

                if (!string.Equals(other.Id, currentRackId, StringComparison.Ordinal))
                {
                    error = $"That device is already on rack \"{other.DisplayName}\".";
                    return false;
                }

                error = "Device is already on this rack.";
                return false;
            }
        }

        return true;
    }

    private static bool IsSwitchFamily(string dt)
    {
        return string.Equals(dt, RackDeviceTypes.Switch, StringComparison.OrdinalIgnoreCase)
               || string.Equals(dt, RackDeviceTypes.Router, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MountOccupancyAvailable(RackDefinition rack, int startU, int heightU, string excludeEntryId, out string error)
    {
        error = null;
        var rangeLo = startU;
        var rangeHi = startU + heightU - 1;
        foreach (var m in rack.Mounts)
        {
            if (!string.IsNullOrEmpty(excludeEntryId) && string.Equals(m.EntryId, excludeEntryId, StringComparison.Ordinal))
            {
                continue;
            }

            var mh = Mathf.Max(1, m.HeightU);
            var lo = m.StartU;
            var hi = m.StartU + mh - 1;
            if (rangeHi >= lo && rangeLo <= hi)
            {
                error = "Overlaps another device in this rack.";
                return false;
            }
        }

        return true;
    }

    internal static bool TryRemoveMount(string rackId, string entryId)
    {
        var rack = FindById(rackId);
        if (rack?.Mounts == null || string.IsNullOrEmpty(entryId))
        {
            return false;
        }

        var n = rack.Mounts.RemoveAll(m => m != null && string.Equals(m.EntryId, entryId, StringComparison.Ordinal));
        if (n > 0)
        {
            Save();
            return true;
        }

        return false;
    }

    internal static bool TryImportDiscoveredRack(string discoveredKey, string displayName, RackLayoutHelper.RackInfo info, out string newRackId, out string error)
    {
        error = null;
        newRackId = null;
        if (string.IsNullOrEmpty(discoveredKey) || info == null)
        {
            error = "Nothing to import.";
            return false;
        }

        var name = (displayName ?? "").Trim();
        if (string.IsNullOrEmpty(name))
        {
            error = "Enter a name for this rack.";
            return false;
        }

        if (EnsureLoaded().Racks.Any(r => string.Equals(r.DiscoveredSourceKey ?? "", discoveredKey, StringComparison.Ordinal)))
        {
            error = "This scene rack is already saved.";
            return false;
        }

        var rack = new RackDefinition
        {
            Id = Guid.NewGuid().ToString("D"),
            DisplayName = name,
            TotalU = RackStandardHeightU,
            DiscoveredSourceKey = discoveredKey,
            Mounts = new List<RackMountRecord>(),
        };

        foreach (var d in info.Devices)
        {
            if (d?.Server == null)
            {
                continue;
            }

            int iid;
            try
            {
                iid = d.Server.GetInstanceID();
            }
            catch
            {
                continue;
            }

            var h = Mathf.Max(1, d.HeightU);
            var su = d.StartU > 0 ? d.StartU : 1;
            if (su + h - 1 > RackStandardHeightU)
            {
                continue;
            }

            if (rack.Mounts.Any(m => m.SceneInstanceId == iid && string.Equals(m.DeviceType, RackDeviceTypes.Server, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            string errM = null;
            if (!MountOccupancyAvailable(rack, su, h, null, out errM))
            {
                continue;
            }

            rack.Mounts.Add(
                new RackMountRecord
                {
                    EntryId = Guid.NewGuid().ToString("D"),
                    DeviceType = RackDeviceTypes.Server,
                    SceneInstanceId = iid,
                    ServerInstanceId = iid,
                    StartU = su,
                    HeightU = h,
                });
        }

        EnsureLoaded().Racks.Add(rack);
        newRackId = rack.Id;
        Save();
        return true;
    }

    private static void Save()
    {
        var path = GetPath();
        try
        {
            foreach (var r in EnsureLoaded().Racks)
            {
                if (r != null)
                {
                    r.TotalU = RackStandardHeightU;
                }
            }

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(EnsureLoaded(), JsonOptions);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            ModLogging.Warning($"Rack data save failed ({path}): {ex.Message}");
        }
    }
}

internal static class RackDeviceTypes
{
    internal const string Server = "Server";
    internal const string Switch = "Switch";
    internal const string Router = "Router";
    internal const string PatchPanel = "PatchPanel";
}

internal static class RackMountHeights
{
    internal static int HeightForType(string deviceType)
    {
        if (string.Equals(deviceType, RackDeviceTypes.Switch, StringComparison.OrdinalIgnoreCase)
            || string.Equals(deviceType, RackDeviceTypes.Router, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (string.Equals(deviceType, RackDeviceTypes.PatchPanel, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 3;
    }

    internal static int HeightForMount(string deviceType, int serverInferredU)
    {
        if (string.Equals(deviceType, RackDeviceTypes.Server, StringComparison.OrdinalIgnoreCase))
        {
            var u = Mathf.Clamp(serverInferredU, 1, 32);
            return u == 7 ? 7 : 3;
        }

        return HeightForType(deviceType);
    }
}

internal sealed class RackPersistedRoot
{
    public int Version { get; set; }
    public List<RackDefinition> Racks { get; set; }
}

internal sealed class RackDefinition
{
    public string Id { get; set; }
    public string DisplayName { get; set; }
    public int TotalU { get; set; }
    public string DiscoveredSourceKey { get; set; }
    public List<RackMountRecord> Mounts { get; set; }
}

internal sealed class RackMountRecord
{
    public string EntryId { get; set; }
    /// <summary>Server, Switch, Router, PatchPanel.</summary>
    public string DeviceType { get; set; }
    public int SceneInstanceId { get; set; }
    /// <summary>Legacy JSON field — kept in sync for servers.</summary>
    public int ServerInstanceId { get; set; }
    public string PatchLabel { get; set; }
    public int StartU { get; set; }
    public int HeightU { get; set; }
}
