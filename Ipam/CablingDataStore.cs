using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnityEngine;

namespace GregModIPAM;

/// <summary>
/// Persists cabling connections and device power states per rack
/// in UserData/gregMod.IPAM/cabling_data.json.
/// Separate from game save — never corrupts the savefile.
/// </summary>
internal static class CablingDataStore
{
    private const int FileVersion = 1;
    private const string SubDir = "gregMod.IPAM";
    private const string FileName = "cabling_data.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static CablingRoot _root;
    private static bool _loaded;

    // ── Lifecycle ──

    internal static void ResetForNewSaveSession()
    {
        _loaded = false;
        _root = null;
    }

    private static CablingRoot EnsureLoaded()
    {
        if (_loaded) return _root;

        if (!ModSaveScope.EnsureBindingChecked(out _))
        {
            _root ??= NewEmptyRoot();
            return _root;
        }

        _loaded = true;
        _root = NewEmptyRoot();

        var path = GetPath();
        if (!File.Exists(path)) return _root;

        try
        {
            var json = File.ReadAllText(path);
            var file = JsonSerializer.Deserialize<CablingRoot>(json, JsonOptions);
            if (file != null)
            {
                file.RackCablings ??= new List<RackCabling>();
                foreach (var rc in file.RackCablings)
                {
                    rc.Connections ??= new List<CableConnection>();
                    rc.PowerStates ??= new List<DevicePowerState>();
                }
                _root = file;
                _root.Version = FileVersion;
            }
        }
        catch (Exception ex)
        {
            ModLogging.Warning($"Cabling data load failed ({path}): {ex.Message}");
        }

        return _root;
    }

    // ── Rack Cabling ──

    internal static RackCabling GetOrCreateRackCabling(string rackId)
    {
        var root = EnsureLoaded();
        var existing = root.RackCablings.FirstOrDefault(rc =>
            string.Equals(rc.RackId, rackId, StringComparison.Ordinal));
        if (existing != null) return existing;

        var newCabling = new RackCabling
        {
            RackId = rackId,
            Mode = CablingMode.None,
            Connections = new List<CableConnection>(),
            PowerStates = new List<DevicePowerState>(),
        };
        root.RackCablings.Add(newCabling);
        return newCabling;
    }

    internal static CablingMode GetCablingMode(string rackId)
    {
        return GetOrCreateRackCabling(rackId).Mode;
    }

    internal static void SetCablingMode(string rackId, CablingMode mode)
    {
        GetOrCreateRackCabling(rackId).Mode = mode;
        Save();
    }

    // ── Cable Connections ──

    internal static void AddConnection(string rackId, CableConnection conn)
    {
        GetOrCreateRackCabling(rackId).Connections.Add(conn);
        Save();
    }

    internal static void ClearConnections(string rackId)
    {
        GetOrCreateRackCabling(rackId).Connections.Clear();
        Save();
    }

    internal static List<CableConnection> GetConnections(string rackId)
    {
        return GetOrCreateRackCabling(rackId).Connections;
    }

    internal static List<CableConnection> GetConnectionsForDevice(string rackId, string entryId)
    {
        return GetOrCreateRackCabling(rackId).Connections
            .Where(c => string.Equals(c.SourceEntryId, entryId, StringComparison.Ordinal)
                     || string.Equals(c.TargetEntryId, entryId, StringComparison.Ordinal))
            .ToList();
    }

    internal static int CountPortsUsed(string rackId, string entryId)
    {
        return GetConnectionsForDevice(rackId, entryId).Count;
    }

    // ── Power States ──

    internal static bool IsPoweredOn(string rackId, string entryId)
    {
        var rc = GetOrCreateRackCabling(rackId);
        var ps = rc.PowerStates.FirstOrDefault(p =>
            string.Equals(p.EntryId, entryId, StringComparison.Ordinal));
        return ps?.IsPoweredOn ?? false;
    }

    internal static void SetPowerState(string rackId, string entryId, bool isOn)
    {
        var rc = GetOrCreateRackCabling(rackId);
        var ps = rc.PowerStates.FirstOrDefault(p =>
            string.Equals(p.EntryId, entryId, StringComparison.Ordinal));
        if (ps != null)
        {
            ps.IsPoweredOn = isOn;
            ps.LastChangedAt = DateTime.UtcNow;
        }
        else
        {
            rc.PowerStates.Add(new DevicePowerState
            {
                EntryId = entryId,
                IsPoweredOn = isOn,
                LastChangedAt = DateTime.UtcNow,
            });
        }
        Save();
    }

    internal static int CountPoweredOn(string rackId)
    {
        return GetOrCreateRackCabling(rackId).PowerStates.Count(p => p.IsPoweredOn);
    }

    internal static int CountPoweredOff(string rackId)
    {
        return GetOrCreateRackCabling(rackId).PowerStates.Count(p => !p.IsPoweredOn);
    }

    // ── Persistence ──

    internal static void Save()
    {
        var path = GetPath();
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(EnsureLoaded(), JsonOptions);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            ModLogging.Warning($"Cabling data save failed ({path}): {ex.Message}");
        }
    }

    private static string GetPath()
    {
        try
        {
            var dataPath = Application.dataPath;
            if (!string.IsNullOrEmpty(dataPath))
            {
                var rootDir = Path.GetDirectoryName(dataPath);
                if (!string.IsNullOrEmpty(rootDir))
                    return Path.Combine(rootDir, "UserData", SubDir, FileName);
            }
        }
        catch { }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, SubDir, FileName);
    }

    private static CablingRoot NewEmptyRoot()
    {
        return new CablingRoot { Version = FileVersion, RackCablings = new List<RackCabling>() };
    }
}
