using System.Collections.Generic;

namespace DHCPSwitches;

// File-local scratch lists — must not share one list between nested egress + next-hop resolution.
file static class EgressMatchScratch
{
    internal static readonly List<int> ConnectedDest = new(8);
    internal static readonly List<int> NextHopIface = new(8);
}

/// <summary>
/// Longest-prefix forwarding against mod <see cref="RouterRuntimeConfig"/> (connected + static) so ping uses the same egress as configured L3.
/// </summary>
internal static class RouterForwarding
{
    internal static bool TryGetInterfaceIndexForLocalIp(NetworkSwitch sw, string localIp, out int ifaceIndex)
    {
        ifaceIndex = -1;
        if (sw == null || string.IsNullOrWhiteSpace(localIp))
        {
            return false;
        }

        var t = localIp.Trim();
        var rc = DeviceConfigRegistry.GetOrCreateRouter(sw, NetworkDeviceClassifier.GetPortCount(sw));
        foreach (var iface in rc.Interfaces)
        {
            if (iface.Shutdown || string.IsNullOrWhiteSpace(iface.IpAddress))
            {
                continue;
            }

            if (string.Equals(iface.IpAddress.Trim(), t, System.StringComparison.OrdinalIgnoreCase))
            {
                ifaceIndex = iface.Index;
                return true;
            }
        }

        return false;
    }

    /// <summary>Longest prefix length among connected up interfaces that contain <paramref name="hostIp"/>.</summary>
    internal static bool GetLongestConnectedPrefixForHost(RouterRuntimeConfig rc, uint hostIp, out int bestPl)
    {
        bestPl = -1;
        if (rc?.Interfaces == null)
        {
            return false;
        }

        foreach (var iface in rc.Interfaces)
        {
            if (iface.Shutdown || string.IsNullOrWhiteSpace(iface.IpAddress) || string.IsNullOrWhiteSpace(iface.SubnetMask))
            {
                continue;
            }

            if (!RouteMath.TryParseIpv4(iface.IpAddress.Trim(), out var ifIp)
                || !RouteMath.TryParseSubnetMaskField(iface.SubnetMask.Trim(), out var maskU))
            {
                continue;
            }

            var pl = RouteMath.MaskToPrefixLength(maskU);
            if (pl < 0 || pl > 32)
            {
                continue;
            }

            var net = pl == 0 ? 0u : (ifIp & (0xFFFFFFFFu << (32 - pl)));
            if (!RouteMath.PrefixCovers(net, pl, hostIp))
            {
                continue;
            }

            if (pl > bestPl)
            {
                bestPl = pl;
            }
        }

        return bestPl >= 0;
    }

    /// <summary>Best egress interface index for <paramref name="destIp"/> using connected then static (longest match), resolving next-hop on connected up interfaces.</summary>
    internal static bool TryGetEgressInterfaceIndex(NetworkSwitch sw, string destIp, out int ifaceIndex, out string detail)
    {
        ifaceIndex = -1;
        detail = null;
        if (sw == null || string.IsNullOrWhiteSpace(destIp))
        {
            return false;
        }

        if (!RouteMath.TryParseIpv4(destIp.Trim(), out var destU))
        {
            return false;
        }

        var rc = DeviceConfigRegistry.GetOrCreateRouter(sw, NetworkDeviceClassifier.GetPortCount(sw));

        var bestConnPl = -1;
        var connList = EgressMatchScratch.ConnectedDest;
        connList.Clear();
        foreach (var iface in rc.Interfaces)
        {
            if (iface.Shutdown || string.IsNullOrWhiteSpace(iface.IpAddress) || string.IsNullOrWhiteSpace(iface.SubnetMask))
            {
                continue;
            }

            if (!RouteMath.TryParseIpv4(iface.IpAddress.Trim(), out var ifIp)
                || !RouteMath.TryParseSubnetMaskField(iface.SubnetMask.Trim(), out var maskU))
            {
                continue;
            }

            var pl = RouteMath.MaskToPrefixLength(maskU);
            if (pl < 0 || pl > 32)
            {
                continue;
            }

            var net = pl == 0 ? 0u : (ifIp & (0xFFFFFFFFu << (32 - pl)));
            if (!RouteMath.PrefixCovers(net, pl, destU))
            {
                continue;
            }

            if (pl > bestConnPl)
            {
                bestConnPl = pl;
                connList.Clear();
                connList.Add(iface.Index);
            }
            else if (pl == bestConnPl)
            {
                connList.Add(iface.Index);
            }
        }

        var bestConnIdx = connList.Count == 1 ? connList[0] : -1;

        StaticRouteEntry bestSr = null;
        var bestSrLen = -1;
        foreach (var r in rc.StaticRoutes)
        {
            if (r == null || string.IsNullOrWhiteSpace(r.DestinationPrefix))
            {
                continue;
            }

            if (!RouteMath.TryParsePrefix(r.DestinationPrefix.Trim(), out var rnet, out var rlen))
            {
                continue;
            }

            if (rlen > 0 && !RouteMath.PrefixCovers(rnet, rlen, destU))
            {
                continue;
            }

            if (rlen > bestSrLen)
            {
                bestSrLen = rlen;
                bestSr = r;
            }
        }

        var staticWins = bestSr != null && bestSrLen > bestConnPl;
        if (ModDebugLog.IsTraceEnabled)
        {
            var connDesc = connList.Count == 0 ? "(none)" : string.Join(",", connList);
            ModDebugLog.Trace(
                "route",
                $"egress dest={destIp.Trim()} connectedPl={bestConnPl} candidates=[{connDesc}] staticBest={(bestSr == null ? "null" : $"{bestSr.DestinationPrefix} via {bestSr.NextHop} pl={bestSrLen}")} staticWins={staticWins}");
        }

        if (staticWins && bestSr != null && !string.IsNullOrWhiteSpace(bestSr.NextHop))
        {
            if (!RouteMath.TryParseIpv4(bestSr.NextHop.Trim(), out var nhU))
            {
                detail = "static route has invalid next-hop";
                ModDebugLog.Trace("route", $"egress FAIL: {detail}");
                return false;
            }

            if (TryConnectedEgressToAddress(rc, nhU, out var nhIdx))
            {
                ifaceIndex = nhIdx;
                detail = $"static via {bestSr.NextHop} -> Gi0/{nhIdx}";
                ModDebugLog.Trace("route", $"egress OK: {detail}");
                return true;
            }

            detail = "next-hop is not on a connected up interface";
            ModDebugLog.Trace("route", $"egress FAIL: {detail} (nh={bestSr.NextHop})");
            return false;
        }

        if (connList.Count > 1)
        {
            detail = "ambiguous: multiple interfaces match the same longest-prefix connected route";
            ModDebugLog.Trace("route", $"egress FAIL: {detail} pl={bestConnPl} ifaces=[{string.Join(",", connList)}]");
            return false;
        }

        if (bestConnIdx >= 0)
        {
            ifaceIndex = bestConnIdx;
            detail = $"connected Gi0/{bestConnIdx}";
            ModDebugLog.Trace("route", $"egress OK: {detail} (static not longer than connected; connected wins when same length)");
            return true;
        }

        detail = "no matching connected subnet or static route";
        ModDebugLog.Trace("route", $"egress FAIL: {detail}");
        return false;
    }

    private static bool TryConnectedEgressToAddress(RouterRuntimeConfig rc, uint address, out int ifaceIndex)
    {
        ifaceIndex = -1;
        var bestPl = -1;
        var list = EgressMatchScratch.NextHopIface;
        list.Clear();
        foreach (var iface in rc.Interfaces)
        {
            if (iface.Shutdown || string.IsNullOrWhiteSpace(iface.IpAddress) || string.IsNullOrWhiteSpace(iface.SubnetMask))
            {
                continue;
            }

            if (!RouteMath.TryParseIpv4(iface.IpAddress.Trim(), out var ifIp)
                || !RouteMath.TryParseSubnetMaskField(iface.SubnetMask.Trim(), out var maskU))
            {
                continue;
            }

            var pl = RouteMath.MaskToPrefixLength(maskU);
            if (pl < 0 || pl > 32)
            {
                continue;
            }

            var net = pl == 0 ? 0u : (ifIp & (0xFFFFFFFFu << (32 - pl)));
            if (!RouteMath.PrefixCovers(net, pl, address))
            {
                continue;
            }

            if (pl > bestPl)
            {
                bestPl = pl;
                list.Clear();
                list.Add(iface.Index);
            }
            else if (pl == bestPl)
            {
                list.Add(iface.Index);
            }
        }

        if (list.Count != 1)
        {
            return false;
        }

        ifaceIndex = list[0];
        return true;
    }

    /// <summary>Whether <paramref name="nextHopAddress"/> lies on a connected, up interface subnet (for static next-hop validation).</summary>
    internal static bool IsNextHopOnConnectedSubnet(RouterRuntimeConfig rc, uint nextHopAddress, out int ifaceIndex)
    {
        return TryConnectedEgressToAddress(rc, nextHopAddress, out ifaceIndex);
    }
}
