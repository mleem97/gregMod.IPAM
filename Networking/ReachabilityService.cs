namespace DHCPSwitches;

/// <summary>
/// IOPS / flow gating vs L3 reachability. Extend when the mod enforces routing between customers;
/// default allows simulation so <see cref="DHCPManager.FlowPausePatch"/> only pauses on user request.
/// </summary>
public static class ReachabilityService
{
    public static bool AllowCustomerAddAppPerformance(CustomerBase customer, out string denyReason)
    {
        denyReason = null;
        return true;
    }

    public static string SummarizeServersForCustomer(int customerId)
    {
        return customerId >= 0 ? $"customer={customerId}" : "customer=?";
    }
}
