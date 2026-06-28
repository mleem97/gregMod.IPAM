using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnityEngine;

namespace GregModIPAM;

internal enum NamingCounterScope
{
    PerApply = 0,
    PerRack = 1,
    PerCustomer = 2,
    PerRow = 3,
    PerCol = 4,
}

internal enum NamingSortOrder
{
    RackU = 0,
    Grid = 1,
    Ip = 2,
    Selection = 3,
}

internal sealed class NamingConventionEntry
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Pattern { get; set; }
    public int SeqStart { get; set; } = 1;
    public int SeqStep { get; set; } = 1;
    public int SeqPad { get; set; }
    public string CounterScope { get; set; } = "perApply";
    public string SortOrder { get; set; } = "rackU";
    public int CustomerId { get; set; } = -1;
    public bool AutoApplyAfterAssign { get; set; }
    public string ManualRow { get; set; }
    public string ManualCol { get; set; }
}

internal static class NamingConventionStore
{
    private const int FileVersion = 1;
    private const string SubDir = "gregMod.IPAM";
    private const string FileName = "naming_data.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static NamingPersistedRoot _root;
    private static bool _loaded;
    private static NamingPersistedRoot _deferEmptyRoot;

    private static NamingPersistedRoot NewEmptyRoot()
    {
        return new NamingPersistedRoot
        {
            Version = FileVersion,
            Conventions = new List<NamingConventionEntry>(),
            Abbreviations = new Dictionary<string, string>(),
            Overrides = new Dictionary<string, string>(),
            CounterState = new Dictionary<string, int>(),
            CustomerDefaultConventionId = new Dictionary<string, string>(),
        };
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

    private static NamingPersistedRoot EnsureLoaded()
    {
        if (_loaded)
        {
            return _root;
        }

        if (!ModSaveScope.EnsureBindingChecked(out _))
        {
            _deferEmptyRoot ??= NewEmptyRoot();
            return _deferEmptyRoot;
        }

        _loaded = true;
        _root = NewEmptyRoot();
        _deferEmptyRoot = null;

        var path = GetPath();
        if (!File.Exists(path))
        {
            return _root;
        }

        try
        {
            var json = File.ReadAllText(path);
            var file = JsonSerializer.Deserialize<NamingPersistedRoot>(json, JsonOptions);
            if (file != null)
            {
                file.Conventions ??= new List<NamingConventionEntry>();
                file.Abbreviations ??= new Dictionary<string, string>();
                file.Overrides ??= new Dictionary<string, string>();
                file.CounterState ??= new Dictionary<string, int>();
                file.CustomerDefaultConventionId ??= new Dictionary<string, string>();
                _root = file;
                _root.Version = FileVersion;
            }
        }
        catch (Exception ex)
        {
            ModLogging.Warning($"Naming data load failed ({path}): {ex.Message}");
        }

        return _root;
    }

    internal static void ResetForNewSaveSession()
    {
        _loaded = false;
        _root = null;
        _deferEmptyRoot = null;
    }

    internal static IReadOnlyList<NamingConventionEntry> GetConventions()
    {
        return EnsureLoaded().Conventions;
    }

    internal static NamingConventionEntry TryGetConventionById(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        return EnsureLoaded().Conventions.FirstOrDefault(c =>
            c != null && string.Equals(c.Id, id, StringComparison.Ordinal));
    }

    internal static string TryGetOverrideName(UnityEngine.Object o)
    {
        if (o == null)
        {
            return null;
        }

        var key = GetOverrideKey(o);
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        return EnsureLoaded().Overrides.TryGetValue(key, out var v) ? v : null;
    }

    internal static void SetOverrideName(UnityEngine.Object o, string name)
    {
        if (o == null)
        {
            return;
        }

        var key = GetOverrideKey(o);
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        var root = EnsureLoaded();
        if (string.IsNullOrWhiteSpace(name))
        {
            root.Overrides.Remove(key);
        }
        else
        {
            root.Overrides[key] = name.Trim();
        }

        Save();
    }

    internal static string GetCustomerAbbreviation(int customerId, string fallbackFullName)
    {
        var root = EnsureLoaded();
        var key = customerId.ToString();
        if (root.Abbreviations.TryGetValue(key, out var abbr) && !string.IsNullOrWhiteSpace(abbr))
        {
            return abbr.Trim();
        }

        return DeriveAbbreviationFromName(fallbackFullName);
    }

    internal static void SetCustomerAbbreviation(int customerId, string abbr)
    {
        if (customerId < 0)
        {
            return;
        }

        var root = EnsureLoaded();
        var key = customerId.ToString();
        if (string.IsNullOrWhiteSpace(abbr))
        {
            root.Abbreviations.Remove(key);
        }
        else
        {
            root.Abbreviations[key] = abbr.Trim();
        }

        Save();
    }

    internal static string TryGetCustomerDefaultConventionId(int customerId)
    {
        if (customerId < 0)
        {
            return null;
        }

        var key = customerId.ToString();
        return EnsureLoaded().CustomerDefaultConventionId.TryGetValue(key, out var id) ? id : null;
    }

    internal static void SetCustomerDefaultConvention(int customerId, string conventionId, bool autoApplyAfterAssign)
    {
        if (customerId < 0)
        {
            return;
        }

        var root = EnsureLoaded();
        var key = customerId.ToString();
        if (string.IsNullOrEmpty(conventionId))
        {
            root.CustomerDefaultConventionId.Remove(key);
        }
        else
        {
            root.CustomerDefaultConventionId[key] = conventionId;
        }

        var conv = TryGetConventionById(conventionId);
        if (conv != null)
        {
            conv.CustomerId = customerId;
            conv.AutoApplyAfterAssign = autoApplyAfterAssign;
        }

        Save();
    }

    internal static bool TryDeleteConvention(string id, out string error)
    {
        error = null;
        if (string.IsNullOrEmpty(id))
        {
            error = "No convention selected.";
            return false;
        }

        var root = EnsureLoaded();
        var removed = root.Conventions.RemoveAll(c =>
            c != null && string.Equals(c.Id, id, StringComparison.Ordinal));
        if (removed == 0)
        {
            error = "Convention not found.";
            return false;
        }

        var staleDefaults = root.CustomerDefaultConventionId
            .Where(kv => string.Equals(kv.Value, id, StringComparison.Ordinal))
            .Select(kv => kv.Key)
            .ToList();
        foreach (var key in staleDefaults)
        {
            root.CustomerDefaultConventionId.Remove(key);
        }

        Save();
        return true;
    }

    internal static bool TryRenameConvention(string id, string newName, out string error)
    {
        error = null;
        newName = (newName ?? "").Trim();
        if (string.IsNullOrEmpty(id))
        {
            error = "No convention selected.";
            return false;
        }

        if (string.IsNullOrEmpty(newName))
        {
            error = "Enter a convention name.";
            return false;
        }

        var conv = TryGetConventionById(id);
        if (conv == null)
        {
            error = "Convention not found.";
            return false;
        }

        var dupe = EnsureLoaded().Conventions.Any(c =>
            c != null
            && !ReferenceEquals(c, conv)
            && !string.Equals(c.Id, id, StringComparison.Ordinal)
            && string.Equals(c.Name, newName, StringComparison.OrdinalIgnoreCase));
        if (dupe)
        {
            error = "A convention with that name already exists.";
            return false;
        }

        conv.Name = newName;
        Save();
        return true;
    }

    internal static bool TryUpdateConventionById(
        string id,
        string pattern,
        int seqStart,
        int seqStep,
        int seqPad,
        NamingCounterScope scope,
        NamingSortOrder sort,
        int customerId,
        string manualRow,
        string manualCol,
        out string error)
    {
        error = null;
        pattern = (pattern ?? "").Trim();
        if (string.IsNullOrEmpty(id))
        {
            error = "No convention selected.";
            return false;
        }

        if (string.IsNullOrEmpty(pattern))
        {
            error = "Enter a naming pattern.";
            return false;
        }

        var conv = TryGetConventionById(id);
        if (conv == null)
        {
            error = "Convention not found.";
            return false;
        }

        conv.Pattern = pattern;
        conv.SeqStart = seqStart;
        conv.SeqStep = seqStep;
        conv.SeqPad = seqPad;
        conv.CounterScope = ScopeToString(scope);
        conv.SortOrder = SortToString(sort);
        conv.CustomerId = customerId;
        conv.ManualRow = (manualRow ?? "").Trim();
        conv.ManualCol = (manualCol ?? "").Trim();
        Save();
        return true;
    }

    internal static bool TrySaveConvention(string name, string pattern, int seqStart, int seqStep, int seqPad,
        NamingCounterScope scope, NamingSortOrder sort, int customerId, string manualRow, string manualCol,
        out NamingConventionEntry entry, out string error)
    {
        entry = null;
        error = null;
        name = (name ?? "").Trim();
        pattern = (pattern ?? "").Trim();
        if (string.IsNullOrEmpty(name))
        {
            error = "Enter a convention name.";
            return false;
        }

        if (string.IsNullOrEmpty(pattern))
        {
            error = "Enter a naming pattern.";
            return false;
        }

        var root = EnsureLoaded();
        var existing = root.Conventions.FirstOrDefault(c =>
            c != null && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.Pattern = pattern;
            existing.SeqStart = seqStart;
            existing.SeqStep = seqStep;
            existing.SeqPad = seqPad;
            existing.CounterScope = ScopeToString(scope);
            existing.SortOrder = SortToString(sort);
            existing.CustomerId = customerId;
            existing.ManualRow = (manualRow ?? "").Trim();
            existing.ManualCol = (manualCol ?? "").Trim();
            entry = existing;
            Save();
            return true;
        }

        entry = new NamingConventionEntry
        {
            Id = Guid.NewGuid().ToString("D"),
            Name = name,
            Pattern = pattern,
            SeqStart = seqStart,
            SeqStep = seqStep,
            SeqPad = seqPad,
            CounterScope = ScopeToString(scope),
            SortOrder = SortToString(sort),
            CustomerId = customerId,
            ManualRow = (manualRow ?? "").Trim(),
            ManualCol = (manualCol ?? "").Trim(),
        };
        root.Conventions.Add(entry);
        Save();
        return true;
    }

    internal static int GetCounterValue(string stateKey, int defaultStart)
    {
        if (string.IsNullOrEmpty(stateKey))
        {
            return defaultStart;
        }

        return EnsureLoaded().CounterState.TryGetValue(stateKey, out var v) ? v : defaultStart;
    }

    internal static void SetCounterValue(string stateKey, int value)
    {
        if (string.IsNullOrEmpty(stateKey))
        {
            return;
        }

        EnsureLoaded().CounterState[stateKey] = value;
        Save();
    }

    internal static NamingCounterScope ParseScope(string s)
    {
        return (s ?? "").Trim().ToLowerInvariant() switch
        {
            "perrack" => NamingCounterScope.PerRack,
            "percustomer" => NamingCounterScope.PerCustomer,
            "perrow" => NamingCounterScope.PerRow,
            "percol" => NamingCounterScope.PerCol,
            _ => NamingCounterScope.PerApply,
        };
    }

    internal static NamingSortOrder ParseSort(string s)
    {
        return (s ?? "").Trim().ToLowerInvariant() switch
        {
            "grid" => NamingSortOrder.Grid,
            "ip" => NamingSortOrder.Ip,
            "selection" => NamingSortOrder.Selection,
            _ => NamingSortOrder.RackU,
        };
    }

    internal static string ScopeToString(NamingCounterScope scope) => scope switch
    {
        NamingCounterScope.PerRack => "perRack",
        NamingCounterScope.PerCustomer => "perCustomer",
        NamingCounterScope.PerRow => "perRow",
        NamingCounterScope.PerCol => "perCol",
        _ => "perApply",
    };

    internal static string SortToString(NamingSortOrder sort) => sort switch
    {
        NamingSortOrder.Grid => "grid",
        NamingSortOrder.Ip => "ip",
        NamingSortOrder.Selection => "selection",
        _ => "rackU",
    };

    internal static string DeriveAbbreviationFromName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return "";
        }

        var parts = fullName.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return "";
        }

        if (parts.Length == 1)
        {
            var one = parts[0];
            return one.Length <= 6 ? one.ToUpperInvariant() : one[..6].ToUpperInvariant();
        }

        var sb = new System.Text.StringBuilder(parts.Length);
        foreach (var p in parts)
        {
            if (p.Length > 0)
            {
                sb.Append(char.ToUpperInvariant(p[0]));
            }
        }

        var result = sb.ToString();
        return result.Length > 8 ? result[..8] : result;
    }

    private static string GetOverrideKey(UnityEngine.Object o)
    {
        return o switch
        {
            Server srv => DeviceStableId.ForServer(srv),
            NetworkSwitch sw => DeviceStableId.ForNetworkSwitch(sw),
            _ => null,
        };
    }

    private static void Save()
    {
        var path = GetPath();
        try
        {
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
            ModLogging.Warning($"Naming data save failed ({path}): {ex.Message}");
        }
    }
}

internal sealed class NamingPersistedRoot
{
    public int Version { get; set; }
    public List<NamingConventionEntry> Conventions { get; set; }
    public Dictionary<string, string> Abbreviations { get; set; }
    public Dictionary<string, string> Overrides { get; set; }
    public Dictionary<string, int> CounterState { get; set; }
    public Dictionary<string, string> CustomerDefaultConventionId { get; set; }
}
