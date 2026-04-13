using System;
using System.Collections.Generic;
using System.Linq;
using greg.Sdk.Services;
using greg.Mods.IPAM.Models;
using MelonLoader;
using UnityEngine;

namespace greg.Mods.IPAM.Core
{
    public class IpamEngine
    {
        private static IpamEngine _instance;
        public static IpamEngine Instance => _instance ??= new IpamEngine();

        public List<IpSubnet> Subnets { get; private set; } = new List<IpSubnet>();
        public List<IpLease> Leases { get; private set; } = new List<IpLease>();
        public List<IpConflict> Conflicts { get; private set; } = new List<IpConflict>();

        private IpamEngine() { }

        public void Initialize()
        {
            LoadData();
            RefreshNetworkState();
            MelonLogger.Msg("[IPAM] Engine initialized internally in gregCore.");
        }

        public void RefreshNetworkState()
        {
            var servers = GregServerDiscoveryService.ScanAll();
            var newLeases = new List<IpLease>();

            foreach (var sv in servers)
            {
                if (!string.IsNullOrWhiteSpace(sv.Ip) && sv.Ip != "0.0.0.0")
                {
                    newLeases.Add(new IpLease
                    {
                        IpAddress = sv.Ip,
                        ServerId = sv.ServerId,
                        CustomerId = sv.CustomerId,
                        LeaseTime = DateTime.UtcNow,
                        IsStatic = true
                    });
                }
            }

            Leases = newLeases;
            Conflicts = ConflictDetector.RunAllPasses(Leases, Subnets);

            if (Conflicts.Count > 0)
            {
                GregNotificationService.ShowBanner($"{Conflicts.Count} IP Conflicts found!", Color.red);
            }
        }

        public void SaveData()
        {
            GregPersistenceService.Save("gregCore", "ipam_subnets", Subnets);
        }

        public void LoadData()
        {
            Subnets = GregPersistenceService.Load<List<IpSubnet>>("gregCore", "ipam_subnets", new List<IpSubnet>());
        }
    }
}

