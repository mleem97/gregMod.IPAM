using System.Collections.Generic;
using UnityEngine;

namespace greg.Mods.IPAM;

public class SubnetManager
{
    public Dictionary<int, string> CustomerSubnets { get; private set; } = new();
    
    // Bypass: CustomerBase/NetworkSwitch types are currently unavailable in Assembly-CSharp references.
    public void AutoAssign(object customer)
    {
    }

    private void AssignToNearbySwitches(object customer, int baseOctet)
    {
    }
}

