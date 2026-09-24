using System;

namespace GregModIPAM;

// Erkennt zur Laufzeit, ob gregCore vorhanden ist (Typname-Lookup, kein
// direkter Typzugriff). Mit Core: zentrale Registry/Lock-Services.
// Without: mod-local minimal implementations (standalone mode).
// Regel: Methoden mit gregCore-Typen duerfen NUR bei HasCore aufgerufen
// werden (sonst JIT-TypeLoad bei fehlender DLL).
internal static class GregHost
{
    private const string ProbeType = "gregCore.UI.GregNotificationManager, gregCore";
    private static bool? _hasCore;

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
