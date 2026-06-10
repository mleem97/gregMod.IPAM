using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnityEngine;

namespace DHCPSwitches;

internal enum IpamPrefixParentMode
{
    /// <summary>Pick the tightest existing prefix that fully contains the new CIDR, else root.</summary>
    AutoPickContainedParent = 0,
    ForceRoot = 1,
    ExplicitParent = 2,
}

/// <summary>Persists user-defined IPAM prefixes (parent/child) and VLAN rows under UserData/DHCPSwitches/ipam_data.json.</summary>
internal static class IpamDataStore
{
    private const int FileVersion = 1;
    private const string SubDir = "DHCPSwitches";
    private const string FileName = "ipam_data.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static IpamPersistedRoot _root;
    private static bool _loaded;
    private static IpamPersistedRoot _deferEmptyRoot;

    private static IpamPersistedRoot NewEmptyRoot()
    {
        return new IpamPersistedRoot
        {
            Version = FileVersion,
            Prefixes = new List<IpamPrefixEntry>(),
            Vlans = new List<IpamVlanEntry>(),
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

    private static IpamPersistedRoot EnsureLoaded()
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

        var loadPath = GetPath();
        if (!File.Exists(loadPath))
        {
            return _root;
        }

        try
        {
            var json = File.ReadAllText(loadPath);
            var file = JsonSerializer.Deserialize<IpamPersistedRoot>(json, JsonOptions);
            if (file != null)
            {
                file.Prefixes ??= new List<IpamPrefixEntry>();
                file.Vlans ??= new List<IpamVlanEntry>();
                _root = file;
                _root.Version = FileVersion;
            }
        }
        catch (Exception ex)
        {
            ModLogging.Warning($"IPAM data load failed ({loadPath}): {ex.Message}");
        }

        return _root;
    }

    internal static void ResetForNewSaveSession()
    {
        _loaded = false;
        _root = null;
        _deferEmptyRoot = null;
    }

    internal static IReadOnlyList<IpamPrefixEntry> GetPrefixes()
    {
        return EnsureLoaded().Prefixes;
    }

    internal static bool TryGetPrefixByCidr(string cidr, out IpamPrefixEntry entry)
    {
        entry = null;
        var trimmed = (cidr ?? "").Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        foreach (var p in EnsureLoaded().Prefixes)
        {
            if (p == null)
            {
                continue;
            }

            if (string.Equals((p.Cidr ?? "").Trim(), trimmed, StringComparison.OrdinalIgnoreCase))
            {
                entry = p;
                return true;
            }
        }

        return false;
    }

    internal static IReadOnlyList<IpamVlanEntry> GetVlans()
    {
        return EnsureLoaded().Vlans;
    }

    internal static bool TryAddPrefix(string cidr, string name, string tenant, IpamPrefixParentMode mode, Guid? explicitParentId, out string error)
    {
        error = null;
        var trimmed = (cidr ?? "").Trim();
        if (!RouteMath.TryParseIpv4Cidr(trimmed, out _, out _))
        {
            error = "Invalid IPv4 CIDR (e.g. 10.1.0.0/24).";
            return false;
        }

        var root = EnsureLoaded();
        Guid? resolvedParent = null;

        switch (mode)
        {
            case IpamPrefixParentMode.ForceRoot:
                resolvedParent = null;
                break;
            case IpamPrefixParentMode.ExplicitParent:
                if (!explicitParentId.HasValue)
                {
                    error = "No parent selected.";
                    return false;
                }

                var parent = root.Prefixes.FirstOrDefault(p => p.Id == explicitParentId.Value.ToString("D"));
                if (parent == null)
                {
                    error = "Parent prefix not found.";
                    return false;
                }

                if (!RouteMath.IsStrictChildOf(trimmed, parent.Cidr.Trim()))
                {
                    error = RouteMath.ExplainStrictChildFailure(trimmed, parent.Cidr.Trim());
                    return false;
                }

                resolvedParent = explicitParentId;
                break;
            default:
            {
                var tight = FindTightestContainingParent(trimmed, root.Prefixes);
                resolvedParent = tight != null && Guid.TryParse(tight.Id, out var pg) ? pg : (Guid?)null;
                break;
            }
        }

        var parentIdStr = resolvedParent.HasValue ? resolvedParent.Value.ToString("D") : null;
        foreach (var sib in root.Prefixes)
        {
            if (sib == null)
            {
                continue;
            }

            if (!string.Equals(sib.ParentId ?? "", parentIdStr ?? "", StringComparison.Ordinal))
            {
                continue;
            }

            var sc = (sib.Cidr ?? "").Trim();
            if (RouteMath.Ipv4CidrRangesOverlap(trimmed, sc))
            {
                error = $"Overlaps existing prefix {sc}.";
                return false;
            }
        }

        var entry = new IpamPrefixEntry
        {
            Id = Guid.NewGuid().ToString("D"),
            ParentId = parentIdStr,
            Cidr = trimmed,
            Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
            Tenant = string.IsNullOrWhiteSpace(tenant) ? null : tenant.Trim(),
        };
        root.Prefixes.Add(entry);
        Save();
        return true;
    }

    /// <summary>Updates CIDR (must remain valid vs parent/siblings/children), name, and tenant.</summary>
    internal static bool TryUpdatePrefix(string id, string newCidr, string name, string tenant, out string error)
    {
        error = null;
        if (string.IsNullOrEmpty(id))
        {
            error = "Invalid prefix.";
            return false;
        }

        var trimmed = (newCidr ?? "").Trim();
        if (!RouteMath.TryParseIpv4Cidr(trimmed, out _, out _))
        {
            error = "Invalid IPv4 CIDR.";
            return false;
        }

        var root = EnsureLoaded();
        var entry = root.Prefixes.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));
        if (entry == null)
        {
            error = "Prefix not found.";
            return false;
        }

        if (!string.IsNullOrEmpty(entry.ParentId))
        {
            var parent = root.Prefixes.FirstOrDefault(p => string.Equals(p.Id, entry.ParentId, StringComparison.Ordinal));
            if (parent == null)
            {
                error = "Parent prefix missing.";
                return false;
            }

            var pc = (parent.Cidr ?? "").Trim();
            if (!RouteMath.IsStrictChildOf(trimmed, pc))
            {
                error = RouteMath.ExplainStrictChildFailure(trimmed, pc);
                return false;
            }
        }

        foreach (var ch in root.Prefixes)
        {
            if (ch == null || !string.Equals(ch.ParentId, entry.Id, StringComparison.Ordinal))
            {
                continue;
            }

            var cc = (ch.Cidr ?? "").Trim();
            if (!RouteMath.TryParseIpv4Cidr(cc, out _, out _))
            {
                continue;
            }

            if (!RouteMath.IsStrictChildOf(cc, trimmed))
            {
                error = $"Child prefix {cc} would fall outside {trimmed}. Resize or remove children first.";
                return false;
            }
        }

        foreach (var sib in root.Prefixes)
        {
            if (sib == null || string.Equals(sib.Id, entry.Id, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.Equals(sib.ParentId ?? "", entry.ParentId ?? "", StringComparison.Ordinal))
            {
                continue;
            }

            var sc = (sib.Cidr ?? "").Trim();
            if (RouteMath.Ipv4CidrRangesOverlap(trimmed, sc))
            {
                error = $"Overlaps sibling prefix {sc}.";
                return false;
            }
        }

        entry.Cidr = trimmed;
        entry.Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        entry.Tenant = string.IsNullOrWhiteSpace(tenant) ? null : tenant.Trim();
        Save();
        return true;
    }

    internal static bool TryUpdatePrefixMetadata(string id, string name, string tenant, out string error)
    {
        error = null;
        if (string.IsNullOrEmpty(id))
        {
            error = "Invalid prefix.";
            return false;
        }

        var root = EnsureLoaded();
        var entry = root.Prefixes.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));
        if (entry == null)
        {
            error = "Prefix not found.";
            return false;
        }

        entry.Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        entry.Tenant = string.IsNullOrWhiteSpace(tenant) ? null : tenant.Trim();
        Save();
        return true;
    }

    /// <summary>Among all prefixes, the one with the longest mask that still strictly contains <paramref name="newCidr"/>.</summary>
    private static IpamPrefixEntry FindTightestContainingParent(string newCidr, List<IpamPrefixEntry> all)
    {
        IpamPrefixEntry best = null;
        var bestLen = -1;
        foreach (var p in all)
        {
            if (p == null)
            {
                continue;
            }

            var pc = (p.Cidr ?? "").Trim();
            if (!RouteMath.TryParseIpv4Cidr(pc, out _, out var pLen))
            {
                continue;
            }

            if (!RouteMath.IsStrictChildOf(newCidr, pc))
            {
                continue;
            }

            if (pLen > bestLen)
            {
                bestLen = pLen;
                best = p;
            }
        }

        return best;
    }

    internal static bool TryDeletePrefix(Guid id, out string error)
    {
        error = null;
        var root = EnsureLoaded();
        var idStr = id.ToString("D");
        if (root.Prefixes.All(p => p.Id != idStr))
        {
            error = "Prefix not found.";
            return false;
        }

        var toRemove = new HashSet<string>(StringComparer.Ordinal);
        CollectSubtreeIds(root, idStr, toRemove);
        root.Prefixes.RemoveAll(p => toRemove.Contains(p.Id));
        Save();
        return true;
    }

    internal static bool TryDescribePrefixSubtree(
        string idStr,
        out IpamPrefixEntry entry,
        out int totalCount,
        out List<IpamPrefixEntry> descendants)
    {
        entry = null;
        totalCount = 0;
        descendants = new List<IpamPrefixEntry>();
        if (string.IsNullOrEmpty(idStr))
        {
            return false;
        }

        var root = EnsureLoaded();
        entry = root.Prefixes.FirstOrDefault(p => string.Equals(p.Id, idStr, StringComparison.Ordinal));
        if (entry == null)
        {
            return false;
        }

        var acc = new HashSet<string>(StringComparer.Ordinal);
        CollectSubtreeIds(root, idStr, acc);
        totalCount = acc.Count;
        foreach (var p in root.Prefixes)
        {
            if (p != null && acc.Contains(p.Id) && !string.Equals(p.Id, idStr, StringComparison.Ordinal))
            {
                descendants.Add(p);
            }
        }

        descendants.Sort((a, b) => string.CompareOrdinal((a.Cidr ?? "").Trim(), (b.Cidr ?? "").Trim()));
        return true;
    }

    private static void CollectSubtreeIds(IpamPersistedRoot root, string id, HashSet<string> acc)
    {
        acc.Add(id);
        foreach (var c in root.Prefixes)
        {
            if (c.ParentId != null && string.Equals(c.ParentId, id, StringComparison.Ordinal) && !acc.Contains(c.Id))
            {
                CollectSubtreeIds(root, c.Id, acc);
            }
        }
    }

    internal static bool TryAddVlan(int vlanId, string name, out string error)
    {
        error = null;
        if (vlanId < 1 || vlanId > 4094)
        {
            error = "VLAN ID must be between 1 and 4094.";
            return false;
        }

        var root = EnsureLoaded();
        if (root.Vlans.Any(v => v.VlanId == vlanId))
        {
            error = "That VLAN ID already exists.";
            return false;
        }

        root.Vlans.Add(new IpamVlanEntry
        {
            Id = Guid.NewGuid().ToString("D"),
            VlanId = vlanId,
            Name = string.IsNullOrWhiteSpace(name) ? $"VLAN {vlanId}" : name.Trim(),
        });
        Save();
        return true;
    }

    internal static bool TryDeleteVlan(Guid id, out string error)
    {
        error = null;
        var root = EnsureLoaded();
        var idStr = id.ToString("D");
        var n = root.Vlans.RemoveAll(v => v.Id == idStr);
        if (n == 0)
        {
            error = "VLAN not found.";
            return false;
        }

        Save();
        return true;
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
            ModLogging.Warning($"IPAM data save failed ({path}): {ex.Message}");
        }
    }
}

internal sealed class IpamPersistedRoot
{
    public int Version { get; set; }
    public List<IpamPrefixEntry> Prefixes { get; set; }
    public List<IpamVlanEntry> Vlans { get; set; }
}

internal sealed class IpamPrefixEntry
{
    public string Id { get; set; }
    public string ParentId { get; set; }
    public string Cidr { get; set; }
    public string Name { get; set; }
    public string Tenant { get; set; }
}

internal sealed class IpamVlanEntry
{
    public string Id { get; set; }
    public int VlanId { get; set; }
    public string Name { get; set; }
}
