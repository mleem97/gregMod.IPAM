using System;
using System.Collections.Generic;
using UnityEngine;

namespace DHCPSwitches;

/// <summary>
/// Screen-space Overlay canvases (pause, system/settings) are drawn after IMGUI, so they paint on top of IPAM.
/// While our overlay is open, temporarily disable matching game canvases and restore when closed.
/// </summary>
internal static class IpamMenuOcclusion
{
    private const string OurBlockerName = "DHCPSwitches_IPAMClickBlocker";

    /// <summary>Substring matches on canvas or root name (case-insensitive). Keep specific to avoid hiding HUD.</summary>
    private static readonly string[] CanvasNameHints =
    {
        "PauseMenu", "Pause_", "_Pause", "Paused", "GamePause", "InGameMenu", "EscapeMenu",
        "SystemMenu", "System_", "_System", "OptionsMenu", "SettingsMenu", "MenuPause",
    };

    private static readonly List<(Canvas Canvas, bool WasEnabled)> Suppressed = new();
    private static float _nextScanTime;

    internal static void Tick(bool anyModOverlayVisible)
    {
        if (!anyModOverlayVisible)
        {
            RestoreAll();
            return;
        }

        if (Time.unscaledTime < _nextScanTime)
        {
            return;
        }

        _nextScanTime = Time.unscaledTime + 0.2f;

        try
        {
            var all = Resources.FindObjectsOfTypeAll<Canvas>();
            if (all == null)
            {
                return;
            }

            foreach (var c in all)
            {
                if (c == null)
                {
                    continue;
                }

                var go = c.gameObject;
                if (go == null || go.name.IndexOf(OurBlockerName, StringComparison.Ordinal) >= 0)
                {
                    continue;
                }

                if (!go.scene.IsValid() || !go.scene.isLoaded)
                {
                    continue;
                }

                if (c.renderMode != RenderMode.ScreenSpaceOverlay)
                {
                    continue;
                }

                if (!c.isActiveAndEnabled)
                {
                    continue;
                }

                if (!NameLooksLikeFullscreenMenu(c))
                {
                    continue;
                }

                if (AlreadyTracking(c))
                {
                    continue;
                }

                Suppressed.Add((c, c.enabled));
                c.enabled = false;
            }
        }
        catch (Exception ex)
        {
            ModLogging.Warning($"IpamMenuOcclusion scan: {ex.Message}");
        }
    }

    private static bool AlreadyTracking(Canvas c)
    {
        for (var i = 0; i < Suppressed.Count; i++)
        {
            if (Suppressed[i].Canvas == c)
            {
                return true;
            }
        }

        return false;
    }

    private static bool NameLooksLikeFullscreenMenu(Canvas c)
    {
        var n = c.gameObject.name ?? "";
        var root = c.transform != null && c.transform.root != null ? c.transform.root.gameObject.name ?? "" : "";
        if (n.IndexOf("Particle", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return false;
        }

        if (string.Equals(n, "System", StringComparison.OrdinalIgnoreCase)
            || string.Equals(root, "System", StringComparison.OrdinalIgnoreCase)
            || string.Equals(n, "Pause", StringComparison.OrdinalIgnoreCase)
            || string.Equals(root, "Pause", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var hint in CanvasNameHints)
        {
            if (n.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (root.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static void RestoreAll()
    {
        var n = Suppressed.Count;
        for (var i = 0; i < Suppressed.Count; i++)
        {
            var (cv, was) = Suppressed[i];
            if (cv != null)
            {
                cv.enabled = was;
            }
        }

        Suppressed.Clear();
        _nextScanTime = 0f;
        if (n > 0)
        {
            ModDebugLog.WriteIpam($"IpamMenuOcclusion.RestoreAll canvases={n}");
        }
    }
}
