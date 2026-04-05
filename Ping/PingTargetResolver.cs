using UnityEngine;

namespace DHCPSwitches;

internal static class PingTargetResolver
{
    internal static bool TryResolveTransformForIpv4(string ip, NetworkSwitch excludeSource, out Transform target, out string label)
    {
        target = null;
        label = null;
        if (string.IsNullOrWhiteSpace(ip))
        {
            return false;
        }

        var trimmed = ip.Trim();

        if (excludeSource != null && NetworkDeviceClassifier.GetKind(excludeSource) == NetworkDeviceKind.Router)
        {
            var selfRc = DeviceConfigRegistry.GetOrCreateRouter(
                excludeSource,
                NetworkDeviceClassifier.GetPortCount(excludeSource));
            foreach (var iface in selfRc.Interfaces)
            {
                if (iface.Shutdown)
                {
                    continue;
                }

                if (string.Equals(iface.IpAddress?.Trim(), trimmed, System.StringComparison.OrdinalIgnoreCase))
                {
                    target = excludeSource.transform;
                    label = $"{selfRc.Hostname} {iface.Name}";
                    return true;
                }
            }
        }

        foreach (var s in UnityEngine.Object.FindObjectsOfType<Server>())
        {
            if (s == null)
            {
                continue;
            }

            var cur = DHCPManager.GetServerIP(s);
            if (string.Equals(cur, trimmed, System.StringComparison.OrdinalIgnoreCase))
            {
                target = s.transform;
                label = s.name;
                return true;
            }
        }

        if (GameSubnetHelper.TryResolveCustomerContractPingTarget(trimmed, out var custT, out var custLabel))
        {
            target = custT;
            label = custLabel;
            return true;
        }

        foreach (var sw in UnityEngine.Object.FindObjectsOfType<NetworkSwitch>())
        {
            if (sw == null || sw == excludeSource)
            {
                continue;
            }

            var kind = NetworkDeviceClassifier.GetKind(sw);
            if (kind != NetworkDeviceKind.Router)
            {
                continue;
            }

            var rc = DeviceConfigRegistry.GetOrCreateRouter(sw, NetworkDeviceClassifier.GetPortCount(sw));
            foreach (var iface in rc.Interfaces)
            {
                if (string.Equals(iface.IpAddress?.Trim(), trimmed, System.StringComparison.OrdinalIgnoreCase))
                {
                    target = sw.transform;
                    label = $"{rc.Hostname} {iface.Name}";
                    return true;
                }
            }
        }

        return false;
    }
}
