using System;
using System.Collections.Generic;

namespace GregModIPAM;

// ──────────────────────────────────────────────
//  Cabling & Power Models
// ──────────────────────────────────────────────

internal sealed class CablingRoot
{
    public int Version { get; set; } = 1;
    public List<RackCabling> RackCablings { get; set; } = new();
}

internal sealed class RackCabling
{
    public string RackId { get; set; } = "";
    public CablingMode Mode { get; set; }
    public List<CableConnection> Connections { get; set; } = new();
    public List<DevicePowerState> PowerStates { get; set; } = new();
}

internal sealed class CableConnection
{
    public string CableId { get; set; } = "";
    public string SourceEntryId { get; set; } = "";
    public string SourcePort { get; set; } = "";
    public string TargetEntryId { get; set; } = "";
    public string TargetPort { get; set; } = "";
    public CableType Type { get; set; }
}

internal sealed class DevicePowerState
{
    public string EntryId { get; set; } = "";
    public bool IsPoweredOn { get; set; }
    public DateTime LastChangedAt { get; set; }
}

// ──────────────────────────────────────────────
//  Enums
// ──────────────────────────────────────────────

internal enum CablingMode
{
    None,
    Simple,
    Redundant
}

internal enum CableType
{
    Network,
    Power,
    Fiber,
    SFP
}

internal static class CableTypeLabels
{
    internal static string Label(CableType t) => t switch
    {
        CableType.Network => "Cat6",
        CableType.Power => "Power",
        CableType.Fiber => "Fiber",
        CableType.SFP => "SFP+",
        _ => "?",
    };
}
