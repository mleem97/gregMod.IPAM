using System;

namespace GregModIPAM;

/// <summary>
/// While IPAM is open, strips gameplay mouse reads unless code is inside <see cref="BeginHardwareMouseBypass"/>.
/// </summary>
internal static class IpamGameInputGate
{
    internal static int HardwareMouseBypassDepth { get; private set; }

    internal static bool ShouldStripGameMouse =>
        IPAMOverlay.IsVisible && HardwareMouseBypassDepth == 0;

    internal static IDisposable BeginHardwareMouseBypass()
    {
        HardwareMouseBypassDepth++;
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (HardwareMouseBypassDepth > 0)
            {
                HardwareMouseBypassDepth--;
            }
        }
    }
}
