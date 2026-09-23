using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GregModIPAM;

// Standalone-Input-Guard (ohne gregCore): Cursor + PlayerManager-Flags +
// PlayerInput-Deaktivierung (2s-gedrosselt). Wird nur benutzt wenn
// GregHost.HasCore == false; sonst steuert die zentrale Registry.
internal static class ModLocalGuard
{
    private static readonly List<PlayerInput> Suspended = new();
    private static bool _applied;
    private static bool _wantLock;
    private static float _nextRescanRealtime;

    internal static void SetLocked(bool locked)
    {
        _wantLock = locked;
        Refresh();
    }

    internal static void Tick()
    {
        Refresh();
    }

    private static void Refresh()
    {
        try
        {
            if (_wantLock && !_applied)
            {
                _applied = true;
                ForceCursor();
                SetPlayerManager(false);
                SuspendAll();
            }
            else if (_wantLock && _applied)
            {
                ForceCursor();
                SuspendNew();
            }
            else if (!_wantLock && _applied)
            {
                _applied = false;
                ResumeAll();
                SetPlayerManager(true);
                try
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
                catch { }
            }
        }
        catch { }
    }

    private static void ForceCursor()
    {
        try
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        catch { }
    }

    private static void SetPlayerManager(bool enabled)
    {
        try
        {
            var pm = Il2Cpp.PlayerManager.instance;
            if (pm == null) return;
            try { pm.enabledMouseMovement = enabled; } catch { }
            try { pm.enabledPlayerMovement = enabled; } catch { }
            try { pm.enabledRayLookInteract = enabled; } catch { }
        }
        catch { }
    }

    private static void SuspendAll()
    {
        Suspended.Clear();
        _nextRescanRealtime = 0f;
        SuspendNew();
    }

    private static void SuspendNew()
    {
        float now = 0f;
        try { now = Time.realtimeSinceStartup; } catch { return; }
        if (now < _nextRescanRealtime) return;
        _nextRescanRealtime = now + 2f;
        PlayerInput[] all = null;
        try { all = Resources.FindObjectsOfTypeAll<PlayerInput>(); } catch { }
        if (all == null) return;
        foreach (var pi in all)
        {
            if (pi == null || Suspended.Contains(pi)) continue;
            try
            {
                var go = pi.gameObject;
                if (go == null || !go.scene.IsValid() || !go.scene.isLoaded) continue;
            }
            catch { continue; }
            try
            {
                try
                {
                    var asset = pi.actions;
                    if (asset != null && asset.enabled) asset.Disable();
                }
                catch { }
                pi.DeactivateInput();
                Suspended.Add(pi);
            }
            catch (Exception ex)
            {
                ModLogging.Warning($"IPAM input lock: could not deactivate PlayerInput: {ex.Message}");
            }
        }
    }

    private static void ResumeAll()
    {
        foreach (var pi in Suspended)
        {
            if (pi == null) continue;
            try { pi.ActivateInput(); } catch { }
            try
            {
                InputActionAsset asset = null;
                try { asset = pi.actions; } catch { }
                if (asset != null && !asset.enabled) asset.Enable();
            }
            catch { }
        }
        Suspended.Clear();
    }
}
