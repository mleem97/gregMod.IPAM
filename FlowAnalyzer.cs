using System.Collections.Generic;
using UnityEngine;

namespace greg.Mods.IPAM;

public enum DeepFlowStatus { Active, Idle, Isolated, Broken, Offline }

public static class FlowAnalyzer
{
    // Bypass: NetworkSwitch, NetworkMap, CustomerBase types are currently unavailable in Assembly-CSharp references.
    public static DeepFlowStatus AnalyzeSwitch(object sw)
    {
        return DeepFlowStatus.Active;
    }
}

