using System;
using System.Collections.Generic;
using System.Linq;
using greg.Sdk.Services;
using greg.Mods.IPAM.Models;
using MelonLoader;

namespace greg.Mods.IPAM.Core
{
    public class DhcpEngine
    {
        private static DhcpEngine _instance;
        public static DhcpEngine Instance => _instance ??= new DhcpEngine();

        private DhcpEngine() { }

        public bool AssignIp(string serverId, string subnetCidr)
        {
            var engine = IpamEngine.Instance;
            var subnet = engine.Subnets.FirstOrDefault(s => s.Cidr == subnetCidr);
            if (subnet == null) return false;

            var server = GregServerDiscoveryService.GetById(serverId);
            if (server == null) return false;

            string availableIp = FindAvailableIp(subnet);
            if (availableIp == null)
            {
                GregNotificationService.ShowToast($"No available IPs in {subnetCidr}", ToastType.Error, 5f);
                return false;
            }

            if (GregIpService.SetIp(server.Instance, availableIp))
            {
                engine.RefreshNetworkState();
                return true;
            }

            return false;
        }

        private string FindAvailableIp(IpSubnet subnet)
        {
            var usedIps = new HashSet<string>(IpamEngine.Instance.Leases.Select(l => l.IpAddress));
            foreach (var pool in subnet.Pools)
            {
                var ips = GenerateIpRange(pool.StartIp, pool.EndIp);
                foreach (var ip in ips)
                {
                    if (!usedIps.Contains(ip)) return ip;
                }
            }
            return null;
        }

        private List<string> GenerateIpRange(string startIp, string endIp)
        {
            var result = new List<string>();
            try
            {
                var startParts = startIp.Split('.').Select(int.Parse).ToArray();
                var endParts = endIp.Split('.').Select(int.Parse).ToArray();
                if (startParts.Length != 4 || endParts.Length != 4) return result;

                for (int i = startParts[3]; i <= endParts[3]; i++)
                {
                    result.Add($"{startParts[0]}.{startParts[1]}.{startParts[2]}.{i}");
                }
            }
            catch { }
            return result;
        }
    }
}

