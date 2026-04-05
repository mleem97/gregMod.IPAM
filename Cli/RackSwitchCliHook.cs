using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace DHCPSwitches;

/// <summary>
/// Opens <see cref="DeviceTerminalOverlay"/> when the player clicks the in-world console/menu control on a rack
/// <see cref="NetworkSwitch"/> (e.g. vertical red “⋯” button). Uses collider name heuristics so random chassis clicks do not open the CLI.
/// </summary>
internal static class RackSwitchCliHook
{
    /// <summary>Collider name substrings (case-insensitive). Tune via log hints if your build uses different names.</summary>
    private static readonly string[] OpenCliNameHints =
    {
        "red",
        "menu",
        "more",
        "option",
        "side",
        "context",
        "submenu",
        "sub_menu",
        "vertical",
        "three",
        "dots",
        "ellipsis",
        "extra",
        "panel",
        "settings",
        "gear",
        "cog",
        "console",
        "mgmt",
        "manage",
        "ui",
        "btn",
        "button",
    };

    private static readonly string[] ExcludeNameHints =
    {
        "power",
        "led",
        "fan",
        "vent",
        "mesh",
        "rail",
        "handle",
    };

    private static readonly HashSet<string> LoggedUnknownColliders = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxUnknownColliderLogs = 32;

    internal static void TryHandlePhysicalConsoleClick()
    {
        if (IPAMOverlay.IsVisible || DeviceTerminalOverlay.IsVisible)
        {
            return;
        }

        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
        {
            return;
        }

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(-1))
        {
            return;
        }

        var cam = Camera.main;
        if (cam == null)
        {
            return;
        }

        var ray = cam.ScreenPointToRay(mouse.position.ReadValue());
        const float maxDist = 18f;
        if (!Physics.Raycast(ray, out var hit, maxDist, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
        {
            return;
        }

        var col = hit.collider;
        if (col == null)
        {
            return;
        }

        var sw = col.GetComponentInParent<NetworkSwitch>();
        if (sw == null)
        {
            return;
        }

        var goName = col.gameObject.name ?? string.Empty;
        if (ShouldExcludeColliderName(goName))
        {
            return;
        }

        if (!ShouldOpenCliForColliderName(goName))
        {
            MaybeLogUnknownColliderName(goName);
            return;
        }

        DeviceTerminalOverlay.OpenFor(sw);
    }

    private static bool ShouldExcludeColliderName(string name)
    {
        foreach (var ex in ExcludeNameHints)
        {
            if (name.IndexOf(ex, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ShouldOpenCliForColliderName(string name)
    {
        foreach (var h in OpenCliNameHints)
        {
            if (name.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static void MaybeLogUnknownColliderName(string goName)
    {
        if (LoggedUnknownColliders.Count >= MaxUnknownColliderLogs || string.IsNullOrEmpty(goName))
        {
            return;
        }

        if (!LoggedUnknownColliders.Add(goName))
        {
            return;
        }

        ModLogging.Msg(
            $"Rack CLI: clicked switch collider “{goName}” (no CLI hint match). If this is the menu/red button, add a substring to {nameof(OpenCliNameHints)} in {nameof(RackSwitchCliHook)}.");
    }
}
