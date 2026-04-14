using UnityEngine;

namespace DHCPSwitches;

/// <summary>
/// IPAM-focused lines appended to <c>DHCPSwitches-ipam.log</c> in the game install folder (directory that contains the <c>_Data</c> folder).
/// </summary>
/// <remarks>
/// <para><b>Enable:</b> create an empty file named <c>DHCPSwitches-ipam.flag</c> in that same folder (next to the game executable). Remove the flag to stop writing.</para>
/// <para><b>File:</b> <c>DHCPSwitches-ipam.log</c> — same folder. Lines are UTC timestamps from <see cref="ModDebugLog.WriteIpam"/>.</para>
/// </remarks>
internal static class IpamDebugLog
{
    internal static void IopsToolbarScreenRectUpdated(Rect windowRect, Rect localRect, Rect screenRect)
    {
        ModDebugLog.WriteIpam(
            $"[IOPS rect] screen=({screenRect.x:F1},{screenRect.y:F1},{screenRect.width:F1},{screenRect.height:F1}) " +
            $"local=({localRect.x:F1},{localRect.y:F1},{localRect.width:F1},{localRect.height:F1}) " +
            $"window=({windowRect.x:F1},{windowRect.y:F1},{windowRect.width:F0}x{windowRect.height:F0})");
    }

    internal static void IopsOpenedViaImgui(int frame)
    {
        ModDebugLog.WriteIpam($"[IOPS] opened via IMGUI frame={frame}");
    }

    internal static void IopsOpenedViaInputFallback(int frame, Vector2 mouse, Rect screenRect)
    {
        ModDebugLog.WriteIpam(
            $"[IOPS] opened via Input.System fallback frame={frame} mouse=({mouse.x:F1},{mouse.y:F1}) " +
            $"hitRect=({screenRect.x:F1},{screenRect.y:F1},{screenRect.width:F1},{screenRect.height:F1})");
    }

    /// <summary>Left click while IPAM is open: hardware mouse vs window-local IOPS rect (preferred hit test).</summary>
    internal static void IopsHardwareProbe(
        Vector2 mouseBottomLeft,
        Vector2 pointerWindowLocal,
        Rect windowRect,
        Rect localRect,
        bool localRectEmpty,
        bool hit)
    {
        ModDebugLog.WriteIpam(
            $"[IOPS probe] mouseBL=({mouseBottomLeft.x:F1},{mouseBottomLeft.y:F1}) ptrLocal=({pointerWindowLocal.x:F1},{pointerWindowLocal.y:F1}) hit={hit} " +
            $"localRect=({localRect.x:F1},{localRect.y:F1},{localRect.width:F1},{localRect.height:F1}) " +
            $"window=({windowRect.x:F1},{windowRect.y:F1}) empty={localRectEmpty}");
    }

    internal static void OnGuiMouseDown(Rect windowRect, Vector2 mousePos)
    {
        ModDebugLog.WriteIpam(
            $"[OnGUI] MouseDown pos={mousePos} screen={Screen.width}x{Screen.height} windowRect=({windowRect.x:F1},{windowRect.y:F1},{windowRect.width:F0}x{windowRect.height:F0})");
    }

    internal static void IopsModalClosed(string reason)
    {
        ModDebugLog.WriteIpam($"[IOPS] modal closed: {reason}");
    }
}
