using System;
using System.Collections.Generic;
using System.Linq;
using greg.Sdk.Services;
using greg.Mods.IPAM.Models;

namespace greg.Mods.IPAM.Core
{
    public static class ConflictDetector
    {
        public static List<IpConflict> RunAllPasses(List<IpLease> currentLeases, List<IpSubnet> subnets)
        {
            var conflicts = new List<IpConflict>();
            conflicts.AddRange(DetectDuplicates(currentLeases));
            conflicts.AddRange(DetectMismatches(currentLeases, subnets));
            return conflicts;
        }

        private static List<IpConflict> DetectDuplicates(List<IpLease> leases)
        {
            var conflicts = new List<IpConflict>();
            var ipMap = new Dictionary<string, List<IpLease>>();
            foreach (var lease in leases)
            {
                if (!ipMap.ContainsKey(lease.IpAddress)) ipMap[lease.IpAddress] = new List<IpLease>();
                ipMap[lease.IpAddress].Add(lease);
            }
            foreach (var kvp in ipMap)
            {
                if (kvp.Value.Count > 1)
                {
                    conflicts.Add(new IpConflict
                    {
                        IpAddress = kvp.Key,
                        Type = ConflictType.DuplicateIp,
                        Description = $"IP {kvp.Key} assigned to multiple servers.",
                        DetectedAt = DateTime.UtcNow
                    });
                }
            }
            return conflicts;
        }

        private static List<IpConflict> DetectMismatches(List<IpLease> leases, List<IpSubnet> subnets)
        {
            var conflicts = new List<IpConflict>();
            foreach (var lease in leases)
            {
                var subnet = subnets.FirstOrDefault(s => IsIpInCidr(lease.IpAddress, s.Cidr));
                if (subnet == null && !string.IsNullOrEmpty(lease.IpAddress))
                {
                    conflicts.Add(new IpConflict
                    {
                        IpAddress = lease.IpAddress,
                        Type = ConflictType.RogueDhcp,
                        Description = $"IP {lease.IpAddress} is rogue.",
                        DetectedAt = DateTime.UtcNow
                    });
                }
            }
            return conflicts;
        }

        private static bool IsIpInCidr(string ip, string cidr)
        {
            if (string.IsNullOrEmpty(cidr)) return false;
            var parts = cidr.Split('/')[0].Split('.');
            if (parts.Length < 3) return false;
            return ip.StartsWith($"{parts[0]}.{parts[1]}.{parts[2]}.");
        }
    }
}

