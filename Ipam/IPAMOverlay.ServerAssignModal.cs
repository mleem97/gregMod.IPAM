using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace DHCPSwitches;

// Inline "Assign + address" inside server edit popup (9005): Contract+DHCP or IPAM prefix pick + search.

public static partial class IPAMOverlay
{
    private readonly struct InlinePrefixPickOption
    {
        public readonly string PickKey;
        public readonly string Label;

        public InlinePrefixPickOption(string pickKey, string label)
        {
            PickKey = pickKey;
            Label = label;
        }
    }

    private static void StartInlineCustomerAssign(CustomerBase cb)
    {
        _inlineAssignCustomer = cb;
        _inlineAssignMode = 0;
        _inlineIpamPrefixPickKey = "";
        _inlineIpamPrefixSearchBuf = "";
        _inlineAssignError = "";
        _inlineIpamPrefixListScroll = Vector2.zero;
        _ipamFormFieldFocus = IpamFormFocusNone;
        _customerDropdownOpen = false;
    }

    private static void ClearInlineCustomerAssign()
    {
        _inlineAssignCustomer = null;
        _inlineAssignError = "";
        _inlineIpamPrefixSearchBuf = "";
        _inlineIpamPrefixPickKey = "";
        if (_ipamFormFieldFocus == IpamFormFocusInlinePrefixSearch)
        {
            _ipamFormFieldFocus = IpamFormFocusNone;
        }
    }

    private static void ApplyInlineCustomerAssign()
    {
        _inlineAssignError = "";
        var cb = _inlineAssignCustomer;
        if (cb == null)
        {
            _inlineAssignError = "No customer selected.";
            return;
        }

        CollectSelectedServersIntoScratch();
        var servers = new List<Server>();
        foreach (var s in SelectedServersScratch)
        {
            if (s != null)
            {
                servers.Add(s);
            }
        }

        if (servers.Count == 0)
        {
            _inlineAssignError = "No servers in selection.";
            return;
        }

        DHCPManager.ClearLastSetIpError();

        switch (_inlineAssignMode)
        {
            case 0:
                SelectedServersScratch.Clear();
                SelectedServersScratch.AddRange(servers);
                ApplyCustomerAssignToSelection(cb);
                ClearInlineCustomerAssign();
                return;

            case 1:
                if (!TryResolveInlineIpamPrefixPickCidr(out var cidr, out var pickErr))
                {
                    _inlineAssignError = pickErr;
                    return;
                }

                if (!RouteMath.TryParseIpv4Cidr(cidr, out _, out _))
                {
                    _inlineAssignError = "Selected prefix CIDR is invalid.";
                    return;
                }

                if ((_inlineIpamPrefixPickKey ?? "").StartsWith("free:", StringComparison.Ordinal)
                    && !TryEnsureIpamPrefixForFreeBlock(cidr, cb, out pickErr))
                {
                    _inlineAssignError = pickErr;
                    return;
                }

                foreach (var s in servers)
                {
                    if (!TrySetServerCustomer(s, cb))
                    {
                        _inlineAssignError = "Could not set customer on one or more servers.";
                        return;
                    }

                    if (!DHCPManager.TryPickUnusedIpFromSubnet(cidr, s, out var pick2) || string.IsNullOrEmpty(pick2))
                    {
                        _inlineAssignError =
                            $"No free usable IPv4 in {cidr} for {DeviceInventoryReflection.GetDisplayName(s)}.";
                        return;
                    }

                    DHCPManager.SetServerIP(s, pick2, skipUsableListCheck: true);
                }

                DHCPManager.ClearLastSetIpError();
                InvalidateDeviceCache();
                BeginImGuiInputRecoveryBurst();
                UpdateAnchorServerForDetail();
                if (servers.Count == 1)
                {
                    LoadOctetsFromIp(DHCPManager.GetServerIP(servers[0]));
                }

                ClearInlineCustomerAssign();
                return;

            default:
                _inlineAssignError = "Unknown mode.";
                return;
        }
    }

    private static bool TryResolveInlineIpamPrefixPickCidr(out string cidr, out string error)
    {
        cidr = null;
        error = null;
        var key = (_inlineIpamPrefixPickKey ?? "").Trim();
        if (string.IsNullOrEmpty(key))
        {
            error = "Choose a prefix or Available free block.";
            return false;
        }

        if (key.StartsWith("free:", StringComparison.Ordinal))
        {
            cidr = key.Substring("free:".Length).Trim();
            if (string.IsNullOrEmpty(cidr))
            {
                error = "Choose a prefix or Available free block.";
                return false;
            }

            return true;
        }

        if (key.StartsWith("id:", StringComparison.Ordinal))
        {
            var id = key.Substring("id:".Length).Trim();
            var entry = IpamDataStore.GetPrefixes()
                .FirstOrDefault(p => p != null && string.Equals(p.Id, id, StringComparison.Ordinal));
            cidr = (entry?.Cidr ?? "").Trim();
            if (string.IsNullOrEmpty(cidr))
            {
                error = "Choose a prefix or Available free block.";
                return false;
            }

            return true;
        }

        error = "Choose a prefix or Available free block.";
        return false;
    }

    private static bool TryEnsureIpamPrefixForFreeBlock(string cidr, CustomerBase cb, out string error)
    {
        error = null;
        var trimmed = (cidr ?? "").Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            error = "Invalid prefix CIDR.";
            return false;
        }

        var exists = IpamDataStore.GetPrefixes().Any(p =>
            p != null && string.Equals((p.Cidr ?? "").Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
        if (exists)
        {
            return true;
        }

        var label = DeriveInlinePrefixLabel(cb);
        if (!IpamDataStore.TryAddPrefix(
                trimmed,
                label,
                label,
                IpamPrefixParentMode.AutoPickContainedParent,
                null,
                out error))
        {
            return false;
        }

        RecomputeContentHeight();
        return true;
    }

    private static string DeriveInlinePrefixLabel(CustomerBase cb)
    {
        var cn = GetCustomerName(cb);
        if (string.IsNullOrWhiteSpace(cn))
        {
            return null;
        }

        return cn.Length > 24 ? cn.Substring(0, 24).Trim() : cn.Trim();
    }

    private static List<InlinePrefixPickOption> BuildInlinePrefixPickOptions(string rawQuery)
    {
        var q = (rawQuery ?? "").Trim();
        var ql = q.ToLowerInvariant();
        var all = IpamDataStore.GetPrefixes();
        var existingCidrs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in all)
        {
            if (p == null)
            {
                continue;
            }

            var ec = (p.Cidr ?? "").Trim();
            if (!string.IsNullOrEmpty(ec))
            {
                existingCidrs.Add(ec);
            }
        }

        var sortable = new List<(ulong Key, InlinePrefixPickOption Opt)>();
        var seenFree = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in all)
        {
            if (p == null)
            {
                continue;
            }

            var cidr = (p.Cidr ?? "").Trim();
            if (string.IsNullOrEmpty(cidr))
            {
                continue;
            }

            var kids = all
                .Where(c => c != null && string.Equals(c.ParentId, p.Id, StringComparison.Ordinal))
                .ToList();
            var hasChildren = kids.Count > 0;
            var cap = RouteMath.CountDhcpUsableHosts(cidr);
            var used = hasChildren
                ? CountAssignedServersWithIpInCidr(cidr)
                : CountAssignedServersExclusiveToPrefix(p, all);
            var free = Math.Max(0, cap - used);
            var name = (p.Name ?? "").Trim();
            var namePart = string.IsNullOrEmpty(name) ? "" : $" ({name})";
            var status = hasChildren
                ? $"{used}/{cap} used · folder"
                : $"{free} free · {used}/{cap} used";
            var prefixLabel = $"{cidr}{namePart}  —  {status}";
            sortable.Add((
                SortKeyForIpv4Cidr(cidr),
                new InlinePrefixPickOption("id:" + (p.Id ?? ""), prefixLabel)));

            // Free gaps only under folder prefixes — leaf rows already represent that CIDR.
            if (hasChildren
                && IpamFreeSpace.TryEnumerateMaximalFreeCidrs(cidr, kids, out var freeCidrs))
            {
                foreach (var fc in freeCidrs)
                {
                    var fcTrim = (fc ?? "").Trim();
                    if (string.IsNullOrEmpty(fcTrim)
                        || !seenFree.Add(fcTrim)
                        || existingCidrs.Contains(fcTrim))
                    {
                        continue;
                    }

                    var fcCap = RouteMath.CountDhcpUsableHosts(fcTrim);
                    var fcUsed = CountAssignedServersWithIpInCidr(fcTrim);
                    var fcFree = Math.Max(0, fcCap - fcUsed);
                    if (fcFree <= 0)
                    {
                        continue;
                    }

                    var freeLabel = $"Available · {fcTrim}  —  {fcFree} free";
                    sortable.Add((SortKeyForIpv4Cidr(fcTrim), new InlinePrefixPickOption("free:" + fcTrim, freeLabel)));
                }
            }
        }

        sortable.Sort((a, b) => a.Key.CompareTo(b.Key));

        var opts = new List<InlinePrefixPickOption>(sortable.Count);
        foreach (var (_, opt) in sortable)
        {
            if (string.IsNullOrEmpty(q))
            {
                opts.Add(opt);
                continue;
            }

            if (opt.Label.IndexOf(ql, StringComparison.OrdinalIgnoreCase) >= 0
                || opt.PickKey.IndexOf(ql, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                opts.Add(opt);
            }
        }

        return opts;
    }

    private static ulong SortKeyForIpv4Cidr(string cidr)
    {
        if (!RouteMath.TryParseIpv4Cidr((cidr ?? "").Trim(), out var net, out var len))
        {
            return ulong.MaxValue;
        }

        return ((ulong)net << 8) | (uint)len;
    }

    /// <summary>IPv4 with the most specific containing IPAM prefix CIDR, e.g. <c>172.30.15.2 (172.30.15.0/24)</c>.</summary>
    private static string FormatServerIpWithContainingPrefix(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip) || ip == "0.0.0.0")
        {
            return "—";
        }

        var trimmed = ip.Trim();
        if (TryGetMostSpecificContainingPrefix(trimmed, IpamDataStore.GetPrefixes(), out var winner)
            && winner != null
            && !string.IsNullOrWhiteSpace(winner.Cidr))
        {
            return $"{trimmed}  ({winner.Cidr.Trim()})";
        }

        return trimmed;
    }
}
