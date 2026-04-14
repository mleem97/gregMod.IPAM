using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DHCPSwitches;

/// <summary>
/// While the CLI is open, disables <see cref="PlayerInput"/> devices so letter keys do not trigger in-game actions.
/// Falls back silently if the game does not use <see cref="PlayerInput"/>.
/// </summary>
internal static class GameInputSuppression
{
    private static readonly List<PlayerInput> Suspended = new();
    private static readonly HashSet<InputActionAsset> DisabledAssets = new();
    private static bool _active;

    internal static void SetSuppressed(bool suppress)
    {
        if (suppress == _active)
        {
            return;
        }

        _active = suppress;
        if (suppress)
        {
            Suspended.Clear();
            DisabledAssets.Clear();
            var all = Resources.FindObjectsOfTypeAll<PlayerInput>();
            if (all == null)
            {
                return;
            }

            foreach (var pi in all)
            {
                if (pi == null)
                {
                    continue;
                }

                var go = pi.gameObject;
                if (go == null || !go.scene.IsValid() || !go.scene.isLoaded)
                {
                    continue;
                }

                try
                {
                    TryDisableActions(pi);
                    pi.DeactivateInput();
                    Suspended.Add(pi);
                }
                catch (System.Exception ex)
                {
                    ModLogging.Warning($"CLI input lock: could not deactivate PlayerInput: {ex.Message}");
                }
            }
        }
        else
        {
            foreach (var pi in Suspended)
            {
                if (pi == null)
                {
                    continue;
                }

                try
                {
                    pi.ActivateInput();
                }
                catch (System.Exception ex)
                {
                    ModLogging.Warning($"CLI input lock: could not reactivate PlayerInput: {ex.Message}");
                }
            }

            foreach (var asset in DisabledAssets)
            {
                if (asset == null)
                {
                    continue;
                }

                try
                {
                    asset.Enable();
                }
                catch (System.Exception ex)
                {
                    ModLogging.Warning($"CLI input lock: could not re-enable InputActionAsset: {ex.Message}");
                }
            }

            Suspended.Clear();
            DisabledAssets.Clear();
        }
    }

    /// <summary>Re-scan for new <see cref="PlayerInput"/> instances spawned while the CLI stayed open.</summary>
    internal static void RefreshWhileActive()
    {
        if (!_active)
        {
            return;
        }

        var all = Resources.FindObjectsOfTypeAll<PlayerInput>();
        if (all == null)
        {
            return;
        }

        foreach (var pi in all)
        {
            if (pi == null || Suspended.Contains(pi))
            {
                continue;
            }

            var go = pi.gameObject;
            if (go == null || !go.scene.IsValid() || !go.scene.isLoaded)
            {
                continue;
            }

            try
            {
                TryDisableActions(pi);
                pi.DeactivateInput();
                Suspended.Add(pi);
            }
            catch (System.Exception ex)
            {
                ModLogging.Warning($"CLI input lock: refresh could not deactivate PlayerInput: {ex.Message}");
            }
        }
    }

    private static void TryDisableActions(PlayerInput pi)
    {
        try
        {
            var asset = pi.actions;
            if (asset == null || !asset.enabled)
            {
                return;
            }

            asset.Disable();
            DisabledAssets.Add(asset);
        }
        catch
        {
            // actions API missing or incompatible — ignore
        }
    }
}
