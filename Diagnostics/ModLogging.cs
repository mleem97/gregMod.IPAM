using System;
using MelonLoader;

namespace DHCPSwitches;

internal static class ModLogging
{
    internal static MelonLogger.Instance Instance { get; set; }

    internal static void Msg(string message) => Instance?.Msg(message);

    internal static void Warning(string message) => Instance?.Warning(message);

    internal static void Error(string message) => Instance?.Error(message);

    internal static void Error(Exception ex) => Instance?.Error(ex?.ToString() ?? "Exception");
}
