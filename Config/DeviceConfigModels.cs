using System;
using System.Collections.Generic;

namespace DHCPSwitches;

[Serializable]
public sealed class RouterInterfaceConfig
{
    public string Name = "Gi0/0";
    public int Index;
    public bool Shutdown = true;
    public int? AccessVlan;
    public int? NativeVlan;
    public bool Trunk;
    public string AllowedVlanRaw = "all";
    public string IpAddress = "";
    public string SubnetMask = "";
}

[Serializable]
public sealed class StaticRouteEntry
{
    public string DestinationPrefix = "";
    public int PrefixLength;
    public string NextHop = "";
    public string ViaInterface = "";
}

[Serializable]
public sealed class RouterRuntimeConfig
{
    public string Hostname = "Router";
    public List<RouterInterfaceConfig> Interfaces = new();
    public List<StaticRouteEntry> StaticRoutes = new();
}

[Serializable]
public sealed class SwitchPortConfig
{
    public int PortIndex;
    public string Mode = "access";
    public int AccessVlan = 1;
    public bool Trunk;
    public string AllowedVlanRaw = "all";
}

[Serializable]
public sealed class SwitchVlanEntry
{
    public int Id;
    public string Name = "";
}

[Serializable]
public sealed class SwitchRuntimeConfig
{
    public string Hostname = "Switch";
    public List<SwitchVlanEntry> Vlans = new();
    public List<SwitchPortConfig> Ports = new();
}
