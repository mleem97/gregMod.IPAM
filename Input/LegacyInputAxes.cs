using System.Reflection;

namespace DHCPSwitches;

/// <summary>Clears legacy Unity Input Manager button/axis state so games using <c>Input.GetKey</c> stop seeing keys held for the CLI.</summary>
internal static class LegacyInputAxes
{
    private static MethodInfo _resetAxes;

    internal static void TryReset()
    {
        try
        {
            _resetAxes ??= Type.GetType("UnityEngine.Input, UnityEngine.InputLegacyModule")
                ?.GetMethod("ResetInputAxes", BindingFlags.Public | BindingFlags.Static);
            _resetAxes?.Invoke(null, null);
        }
        catch
        {
            // Input legacy module not loaded or API missing — ignore.
        }
    }
}
