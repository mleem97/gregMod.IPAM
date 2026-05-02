using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace DHCPSwitches;

// Inline "Assign + address" inside server edit popup (9005): Contract+DHCP or IPAM prefix pick + search.

public static partial class IPAMOverlay
{
    private static void StartInlineCustomerAssign(CustomerBase cb)
    {
        _inlineAssignCustomer = cb;
        _inlineAssignMode = 0;
        _inlineIpamPrefixPickId = "";
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
        _inlineIpamPrefixPickId = "";
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
                var prefixes = IpamDataStore.GetPrefixes();
                var entry = prefixes.FirstOrDefault(p =>
                    p != null && string.Equals(p.Id, _inlineIpamPrefixPickId, StringComparison.Ordinal));
                if (entry == null || string.IsNullOrWhiteSpace(entry.Cidr))
                {
                    _inlineAssignError = "Choose an IPAM prefix row.";
                    return;
                }

                var cidr = entry.Cidr.Trim();
                if (!RouteMath.TryParseIpv4Cidr(cidr, out _, out _))
                {
                    _inlineAssignError = "Selected prefix CIDR is invalid.";
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

    private static bool IsLeafIpamPrefix(IpamPrefixEntry p, IReadOnlyList<IpamPrefixEntry> all)
    {
        if (p == null)
        {
            return false;
        }

        return !all.Any(c => c != null && string.Equals(c.ParentId, p.Id, StringComparison.Ordinal));
    }

    private static List<IpamPrefixEntry> GetFilteredIpamPrefixesForInlineSearch(string rawQuery)
    {
        var q = (rawQuery ?? "").Trim();
        var all = IpamDataStore.GetPrefixes();
        if (string.IsNullOrEmpty(q))
        {
            return all.Where(p => p != null && IsLeafIpamPrefix(p, all)).OrderBy(NetworkSortKeyIpam).ToList();
        }

        var ql = q.ToLowerInvariant();
        var filtered = new List<IpamPrefixEntry>();
        foreach (var p in all)
        {
            if (p == null || !IsLeafIpamPrefix(p, all))
            {
                continue;
            }

            var cidr = (p.Cidr ?? "").Trim();
            var name = (p.Name ?? "").Trim();
            var tenant = (p.Tenant ?? "").Trim();
            var id = (p.Id ?? "").Trim();
            if (cidr.IndexOf(ql, StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf(ql, StringComparison.OrdinalIgnoreCase) >= 0
                || tenant.IndexOf(ql, StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf(ql, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                filtered.Add(p);
            }
        }

        return filtered.OrderBy(NetworkSortKeyIpam).ToList();
    }

    private static ulong NetworkSortKeyIpam(IpamPrefixEntry p)
    {
        if (p == null || !RouteMath.TryParseIpv4Cidr((p.Cidr ?? "").Trim(), out var net, out var len))
        {
            return ulong.MaxValue;
        }

        return ((ulong)net << 8) | (uint)len;
    }
}
