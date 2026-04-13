using System;
using System.Collections.Generic;

namespace greg.Mods.IPAM.Models
{
    public class IpSubnet
    {
        public string Cidr { get; set; }
        public int VlanId { get; set; }
        public string Description { get; set; }
        public List<IpPool> Pools { get; set; } = new List<IpPool>();
    }

    public class IpPool
    {
        public string StartIp { get; set; }
        public string EndIp { get; set; }
        public string PoolName { get; set; }
    }

    public class IpLease
    {
        public string IpAddress { get; set; }
        public string ServerId { get; set; }
        public int CustomerId { get; set; }
        public DateTime LeaseTime { get; set; }
        public bool IsStatic { get; set; }
    }

    public enum ConflictType
    {
        DuplicateIp,
        VlanMismatch,
        SubnetMismatch,
        CustomerMismatch,
        RogueDhcp,
        GhostLease
    }

    public class IpConflict
    {
        public string IpAddress { get; set; }
        public ConflictType Type { get; set; }
        public string Description { get; set; }
        public string AffectedServer1 { get; set; }
        public string AffectedServer2 { get; set; }
        public DateTime DetectedAt { get; set; }
    }
}

