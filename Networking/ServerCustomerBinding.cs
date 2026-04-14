namespace DHCPSwitches;

/// <summary>Thin wrapper for resolving a server&apos;s <see cref="CustomerBase"/> from scene state.</summary>
public static class ServerCustomerBinding
{
    public static CustomerBase FindCustomerBaseForServer(Server server) => GameSubnetHelper.FindCustomerBaseForServer(server);
}
