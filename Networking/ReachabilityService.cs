using System;
using System.Text;
using UnityEngine;

namespace DHCPSwitches;

/// <summary>
/// When <see cref="EnforcementEnabled"/> is true, blocks <see cref="CustomerBase.AddAppPerformance"/> unless:
/// the mod has L3 configuration, the customer's servers use RFC1918 addresses either in the mod default per-<c>customerID</c> /24
/// <b>or</b> on a subnet of a router that carries this customer's public contract WAN IP,
/// some router has that WAN address from the game's usable list for a non-private contract CIDR,
/// and a static route <b>or</b> connected L3 interface covers the mod default private LAN prefix,
/// <b>or</b> (when servers already have IPs) every such server sits on a connected subnet of a router that has this customer's WAN usable IP.
/// </summary>
public static class ReachabilityService
{
    public static bool EnforcementEnabled { get; set; }

    /// <summary>
    /// One full-scene scan per frame — <see cref="AllowCustomerAddAppPerformance"/> used to call
    /// <see cref="UnityEngine.Object.FindObjectsOfType{T}"/> many times per customer per tick (major hitch on power/cable changes).
    /// </summary>
    private static int _sceneDeviceCacheFrame = -1;

    private static Server[] _sceneServers = Array.Empty<Server>();
    private static NetworkSwitch[] _sceneSwitches = Array.Empty<NetworkSwitch>();

    private static void EnsureSceneDeviceCache()
    {
        var f = Time.frameCount;
        if (f == _sceneDeviceCacheFrame)
        {
            return;
        }

        _sceneDeviceCacheFrame = f;
        _sceneServers = UnityEngine.Object.FindObjectsOfType<Server>();
        _sceneSwitches = UnityEngine.Object.FindObjectsOfType<NetworkSwitch>();
    }

    /// <param name="denyReason">When false and enforcement is on, a short diagnostic for <c>DHCPSwitches-debug.log</c>.</param>
    public static bool AllowCustomerAddAppPerformance(CustomerBase customer, out string denyReason)
    {
        denyReason = null;
        var cid = customer != null ? customer.customerID : -1;
        if (!EnforcementEnabled || customer == null)
        {
            ModDebugLog.Trace("iops", $"customerID={cid} ALLOW (L3 enforcement off or customer null)");
            return true;
        }

        EnsureSceneDeviceCache();

        ModDebugLog.Trace("iops", $"customerID={cid} AddAppPerformance gate: enforcement ON");

        if (!ModHasL3Configuration())
        {
            denyReason =
                "NO_MOD_L3: No router has a no-shutdown interface with IP+mask, and no static routes. Configure at least one L3 interface or route.";
            ModDebugLog.Trace("iops", $"customerID={cid} DENY: {denyReason}");
            return false;
        }

        ModDebugLog.Trace("iops", $"customerID={cid} step OK: mod has L3 configuration");

        if (!RouterHasWanAddressOnCustomerPublicContract(customer))
        {
            denyReason =
                "NO_WAN_MATCH: No router interface IP is in the game's usable list for a non-RFC1918 contract CIDR on this customer. " +
                "Put the contract WAN IP (e.g. from keypad) on the correct Gi subinterface. " +
                DescribeCustomerWanMap(customer);
            ModDebugLog.Trace("iops", $"customerID={cid} DENY: {denyReason}");
            return false;
        }

        ModDebugLog.Trace("iops", $"customerID={cid} step OK: WAN/public contract IP on a router");

        if (!PrivateLanPathOk(customer, out var privateCidr, out var staticHit, out var connHit))
        {
            denyReason =
                $"PRIVATE_PATH: Mod default LAN {privateCidr ?? "?"} is not covered by static route or connected interface " +
                $"(staticCovers={staticHit}, connectedCovers={connHit}), and at least one server has an IP while not every addressed server " +
                "lies on a connected subnet of a router that has this customer's WAN contract IP. Align router LAN with mod /24 or move servers onto that WAN router's LAN.";
            ModDebugLog.Trace("iops", $"customerID={cid} DENY: {denyReason}");
            return false;
        }

        ModDebugLog.Trace(
            "iops",
            $"customerID={cid} step OK: private path (static={staticHit}, connected={connHit}, expected /24 {privateCidr ?? "?"})");

        foreach (var s in _sceneServers)
        {
            if (s == null || s.GetCustomerID() != cid)
            {
                continue;
            }

            var ip = DHCPManager.GetServerIP(s);
            if (string.IsNullOrWhiteSpace(ip) || ip == "0.0.0.0")
            {
                ModDebugLog.Trace("iops", $"customerID={cid} server skip (no IP): {s.name}");
                continue;
            }

            if (!RouteMath.TryParseIpv4(ip, out var ipUint))
            {
                denyReason = $"SERVER_IP_PARSE: Server \"{s.name}\" has unparsable IP \"{ip}\".";
                ModDebugLog.Trace("iops", $"customerID={cid} DENY: {denyReason}");
                return false;
            }

            if (!Ipv4Rfc1918.IsPrivateAddress(ipUint))
            {
                denyReason =
                    $"SERVER_NOT_RFC1918: Server \"{s.name}\" IP {ip} must be private (RFC1918) for this mod's IOPS gate.";
                ModDebugLog.Trace("iops", $"customerID={cid} DENY: {denyReason}");
                return false;
            }

            var onDefaultLan = CustomerPrivateSubnetRegistry.IpBelongsToCustomerPrivateLan(s, ip);
            var onWanRouterLan = ServerIpOnWanRouterConnectedLan(s, ipUint, customer);
            if (!onDefaultLan && !onWanRouterLan)
            {
                CustomerPrivateSubnetRegistry.TryGetPrivateLanCidrForServer(s, out var modCidr);
                denyReason =
                    $"SERVER_LAN_MISMATCH: Server \"{s.name}\" IP {ip} is not on mod private LAN " +
                    $"{(string.IsNullOrEmpty(modCidr) ? "?" : modCidr)} for this server (IpBelongsToCustomerPrivateLan=false) " +
                    $"and not on a connected subnet of a router that has this customer's WAN IP (wanRouterConnected=false). " +
                    "Check server customerID vs CustomerBase, DHCP assignment, and router inside-facing subnet.";
                ModDebugLog.Trace("iops", $"customerID={cid} DENY: {denyReason}");
                return false;
            }

            ModDebugLog.Trace(
                "iops",
                $"customerID={cid} server OK: {s.name} {ip} defaultPrivateLan={onDefaultLan} wanRouterConnected={onWanRouterLan}");
        }

        ModDebugLog.Trace("iops", $"customerID={cid} ALLOW AddAppPerformance (all checked servers passed)");
        return true;
    }

    /// <summary>Short summary of <see cref="Server"/> objects tied to <paramref name="customerId"/> for debug logs.</summary>
    internal static string SummarizeServersForCustomer(int customerId)
    {
        EnsureSceneDeviceCache();
        var match = 0;
        var withIp = 0;
        var sb = new StringBuilder();
        var sampleCap = 6;
        foreach (var s in _sceneServers)
        {
            if (s == null || s.GetCustomerID() != customerId)
            {
                continue;
            }

            match++;
            var ip = DHCPManager.GetServerIP(s);
            var ok = !string.IsNullOrWhiteSpace(ip) && ip != "0.0.0.0";
            if (ok)
            {
                withIp++;
            }

            if (sampleCap <= 0 || sb.Length >= 480)
            {
                continue;
            }

            sb.Append(ok ? $"{s.name}={ip}; " : $"{s.name}=noIP; ");
            sampleCap--;
        }

        return match == 0
            ? $"no Server with GetCustomerID()=={customerId} (game may not link this customer to any server)"
            : $"{match} server(s), {withIp} with assigned IP; sample: {sb}";
    }

    private static string DescribeCustomerWanMap(CustomerBase customer)
    {
        var map = customer?.subnetsPerApp;
        if (map == null)
        {
            return "subnetsPerApp=null.";
        }

        var pub = 0;
        var priv = 0;
        foreach (var key in map.Keys)
        {
            var cidr = map[key];
            if (string.IsNullOrWhiteSpace(cidr))
            {
                continue;
            }

            if (Ipv4Rfc1918.IsPrivateCidr(cidr))
            {
                priv++;
            }
            else
            {
                pub++;
            }
        }

        return $"subnetsPerApp: {map.Count} row(s), non-RFC1918 (public contract)={pub}, RFC1918={priv}.";
    }

    /// <summary>
    /// Accept servers addressed on a subnet of a router that has this customer's public (contract) WAN IP,
    /// even when that subnet is not the mod's default <see cref="CustomerPrivateSubnetRegistry"/> /24 for this <c>customerID</c>.
    /// </summary>
    private static bool ServerIpOnWanRouterConnectedLan(Server server, uint ipUint, CustomerBase customer)
    {
        if (server == null || customer == null || server.GetCustomerID() != customer.customerID)
        {
            return false;
        }

        foreach (var sw in _sceneSwitches)
        {
            if (sw == null || NetworkDeviceClassifier.GetKind(sw) != NetworkDeviceKind.Router)
            {
                continue;
            }

            if (!SwitchHasWanIpOnCustomerPublicContract(sw, customer))
            {
                continue;
            }

            if (ConnectedInterfaceCoversHostIp(sw, ipUint))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SwitchHasWanIpOnCustomerPublicContract(NetworkSwitch sw, CustomerBase customer)
    {
        var map = customer?.subnetsPerApp;
        if (map == null)
        {
            return false;
        }

        var cfg = DeviceConfigRegistry.GetOrCreateRouter(sw, NetworkDeviceClassifier.GetPortCount(sw));
        foreach (var key in map.Keys)
        {
            var cidr = map[key];
            if (string.IsNullOrWhiteSpace(cidr) || Ipv4Rfc1918.IsPrivateCidr(cidr))
            {
                continue;
            }

            var usable = GameSubnetHelper.GetUsableIpsForSubnet(cidr);
            if (usable == null || usable.Length == 0)
            {
                continue;
            }

            foreach (var iface in cfg.Interfaces)
            {
                var a = iface.IpAddress?.Trim();
                if (string.IsNullOrWhiteSpace(a))
                {
                    continue;
                }

                for (var i = 0; i < usable.Length; i++)
                {
                    var u = usable[i];
                    if (string.IsNullOrWhiteSpace(u))
                    {
                        continue;
                    }

                    if (string.Equals(a, u.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool ConnectedInterfaceCoversHostIp(NetworkSwitch sw, uint hostIp)
    {
        var cfg = DeviceConfigRegistry.GetOrCreateRouter(sw, NetworkDeviceClassifier.GetPortCount(sw));
        foreach (var iface in cfg.Interfaces)
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

            var ifaceNet = pl == 0 ? 0u : (ifIp & (0xFFFFFFFFu << (32 - pl)));
            if (RouteMath.PrefixCovers(ifaceNet, pl, hostIp))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ModHasL3Configuration()
    {
        foreach (var sw in _sceneSwitches)
        {
            if (sw == null || NetworkDeviceClassifier.GetKind(sw) != NetworkDeviceKind.Router)
            {
                continue;
            }

            var cfg = DeviceConfigRegistry.GetOrCreateRouter(sw, NetworkDeviceClassifier.GetPortCount(sw));
            if (cfg.StaticRoutes.Count > 0)
            {
                return true;
            }

            foreach (var i in cfg.Interfaces)
            {
                if (!i.Shutdown && !string.IsNullOrWhiteSpace(i.IpAddress) && !string.IsNullOrWhiteSpace(i.SubnetMask))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>At least one router interface IP appears in the game's usable list for a non–RFC1918 contract CIDR on this customer.</summary>
    private static bool RouterHasWanAddressOnCustomerPublicContract(CustomerBase customer)
    {
        foreach (var sw in _sceneSwitches)
        {
            if (sw == null || NetworkDeviceClassifier.GetKind(sw) != NetworkDeviceKind.Router)
            {
                continue;
            }

            if (SwitchHasWanIpOnCustomerPublicContract(sw, customer))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True if the mod's default /24 for this customer is routed on some router, or (when any server has an IP) every addressed
    /// server lies on a connected subnet of a WAN router for this customer — so labs using 172.16.0.0/24 for every ID still work.
    /// </summary>
    private static bool PrivateLanPathOk(
        CustomerBase customer,
        out string privateCidr,
        out bool staticCovers,
        out bool connectedCovers)
    {
        privateCidr = null;
        staticCovers = false;
        connectedCovers = false;
        if (customer == null)
        {
            return false;
        }

        if (StaticRouteCoversCustomerPrivateLan(customer.customerID, out privateCidr, out staticCovers, out connectedCovers))
        {
            return true;
        }

        var cid = customer.customerID;
        if (!AnyServerWithAssignedIp(cid))
        {
            return true;
        }

        if (AllAssignedServersOnWanRouterConnectedSubnets(customer))
        {
            ModDebugLog.Trace(
                "iops",
                $"customerID={cid} private path: OK via server IPs on WAN-router LANs (mod /24 {privateCidr ?? "?"} not covered by connected/static)");
            return true;
        }

        return false;
    }

    private static bool AnyServerWithAssignedIp(int customerId)
    {
        foreach (var s in _sceneServers)
        {
            if (s == null || s.GetCustomerID() != customerId)
            {
                continue;
            }

            var ip = DHCPManager.GetServerIP(s);
            if (!string.IsNullOrWhiteSpace(ip) && ip != "0.0.0.0")
            {
                return true;
            }
        }

        return false;
    }

    private static bool AllAssignedServersOnWanRouterConnectedSubnets(CustomerBase customer)
    {
        if (customer == null)
        {
            return false;
        }

        var cid = customer.customerID;
        foreach (var s in _sceneServers)
        {
            if (s == null || s.GetCustomerID() != cid)
            {
                continue;
            }

            var ip = DHCPManager.GetServerIP(s);
            if (string.IsNullOrWhiteSpace(ip) || ip == "0.0.0.0")
            {
                continue;
            }

            if (!RouteMath.TryParseIpv4(ip, out var ipUint))
            {
                return false;
            }

            if (!Ipv4Rfc1918.IsPrivateAddress(ipUint))
            {
                return false;
            }

            if (!ServerIpOnWanRouterConnectedLan(s, ipUint, customer))
            {
                return false;
            }
        }

        return true;
    }

    private static bool StaticRouteCoversCustomerPrivateLan(
        int customerId,
        out string privateCidr,
        out bool staticCovers,
        out bool connectedCovers)
    {
        privateCidr = null;
        staticCovers = false;
        connectedCovers = false;
        if (!CustomerPrivateSubnetRegistry.TryGetPrivateLanCidrForCustomerId(customerId, out var cidr))
        {
            return false;
        }

        privateCidr = cidr;
        if (!RouteMath.TryParsePrefix(cidr, out var net, out var len))
        {
            return false;
        }

        staticCovers = StaticRouteCoversNetwork(net, len);
        connectedCovers = ConnectedRouterCoversNetwork(net);
        return staticCovers || connectedCovers;
    }

    private static bool StaticRouteCoversNetwork(uint destinationNetwork, int destinationPrefixLen)
    {
        foreach (var sw in _sceneSwitches)
        {
            if (sw == null || NetworkDeviceClassifier.GetKind(sw) != NetworkDeviceKind.Router)
            {
                continue;
            }

            var cfg = DeviceConfigRegistry.GetOrCreateRouter(sw, NetworkDeviceClassifier.GetPortCount(sw));
            foreach (var route in cfg.StaticRoutes)
            {
                if (!RouteMath.TryParsePrefix(route.DestinationPrefix, out var rnet, out var rlen))
                {
                    continue;
                }

                if (rlen == 0)
                {
                    return true;
                }

                if (RouteMath.PrefixCovers(rnet, rlen, destinationNetwork))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// No-shutdown L3 interfaces imply a connected route (avoids requiring a redundant <c>ip route</c> for the LAN).
    /// </summary>
    private static bool ConnectedRouterCoversNetwork(uint destinationNetwork)
    {
        foreach (var sw in _sceneSwitches)
        {
            if (sw == null || NetworkDeviceClassifier.GetKind(sw) != NetworkDeviceKind.Router)
            {
                continue;
            }

            var cfg = DeviceConfigRegistry.GetOrCreateRouter(sw, NetworkDeviceClassifier.GetPortCount(sw));
            foreach (var iface in cfg.Interfaces)
            {
                if (iface.Shutdown || string.IsNullOrWhiteSpace(iface.IpAddress) || string.IsNullOrWhiteSpace(iface.SubnetMask))
                {
                    continue;
                }

                if (!RouteMath.TryParseIpv4(iface.IpAddress.Trim(), out var ipU)
                    || !RouteMath.TryParseSubnetMaskField(iface.SubnetMask.Trim(), out var maskU))
                {
                    continue;
                }

                var pl = RouteMath.MaskToPrefixLength(maskU);
                if (pl < 0 || pl > 32)
                {
                    continue;
                }

                var ifaceNet = pl == 0 ? 0u : (ipU & (0xFFFFFFFFu << (32 - pl)));
                if (RouteMath.PrefixCovers(ifaceNet, pl, destinationNetwork))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
