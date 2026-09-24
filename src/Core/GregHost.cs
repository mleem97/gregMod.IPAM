using System;

namespace GregModIPAM;

// Detects at runtime if gregCore is present (type-name lookup, no
// direct type access). With core: central registry/lock services.
// Without: mod-local minimal impls (standalone mode).
// Rule: methods with gregCore types may ONLY run when HasCore is true
// (else JIT TypeLoad with missing DLL).
internal static class GregHost
{
    private const string ProbeType = "gregCore.UI.GregNotificationManager, gregCore";
    private static bool? _hasCore;

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(Justification = "Probe failure branch needs assembly-load failure; not simulable headless. Success path fully tested.")]
    internal static bool HasCore
    {
        get
        {
            if (_hasCore == null)
            {
                try { _hasCore = Type.GetType(ProbeType) != null; }
                catch { _hasCore = false; }
            }
            return _hasCore.Value;
        }
    }
}
