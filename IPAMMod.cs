using MelonLoader;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using greg.Core;
using greg.Sdk.Services;
using System.IO;
using greg.Mods.IPAM.UI;

[assembly: MelonInfo(typeof(greg.Mods.IPAM.IPAMMod), "gregMod.IPAM", "1.0.0.30-pre", "teamGreg (MLeeM97 & Mochimus)")]
[assembly: MelonGame("Waseku", "Data Center")]
[assembly: MelonAdditionalDependencies("gregCore")]

namespace greg.Mods.IPAM;

public class IPAMMod : MelonMod
{
    public static IPAMMod Instance;
    private static GameObject _ipamUI;
    private bool _uiVisible = false;

    public override void OnInitializeMelon()
    {
        Instance = this;
        MelonLogger.Msg("🌐 IPAM Expansion active – Managing Subnets & Flow.");

        // Services
        GregSaveService.OnBeforeSave += SaveConfig;
        
        // Subscribe to Mods menu event
        greg.Harmony.Patches.MainMenuPatch.OnModsMenuOpened += ToggleUI;

        // Initial scan on load
        MelonCoroutines.Start(InitialScan());
    }

    public override void OnUpdate()
    {
        // IPAM toggle: F9 (Optional legacy support)
        if (Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame)
        {
            ToggleUI();
        }
    }

    public void ToggleUI()
    {
        if (_ipamUI == null)
        {
            _ipamUI = IPAMUI.Create();
        }
        
        _uiVisible = !_uiVisible;
        _ipamUI.SetActive(_uiVisible);
        
        MelonLogger.Msg($"[IPAM] UI Toggled: {_uiVisible}");
    }


    private System.Collections.IEnumerator InitialScan()
    {
        yield return new WaitForSeconds(5f); // Wait for world to load
        MelonLogger.Msg("[IPAM] Running initial network flow analysis...");
        // Invoke analyzer
    }

    private void SaveConfig()
    {
        // GregSaveService.SetData("IPAM", "config", ...);
    }
}

