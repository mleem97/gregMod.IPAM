using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace DHCPSwitches;

public class DHCPSwitchesMod : MelonMod
{
    public const string ModGuid = "com.marvin.dhcpswitches";

    public const string DHCP_LICENSE_GUID = "dhcp-auto-assign-v1";
    public const string IPAM_LICENSE_GUID = "ipam-remote-view-v1";

    public override void OnInitializeMelon()
    {
        try
        {
            ModLogging.Instance = LoggerInstance;
            ModDebugLog.Bootstrap();
            DeviceConfigRegistry.BootstrapLoadDisk();

            ClassInjector.RegisterTypeInIl2Cpp<DHCPController>();
            ClassInjector.RegisterTypeInIl2Cpp<DHCPSwitchesBehaviour>();

            var host = new GameObject("DHCPSwitches_Host");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;
            host.AddComponent<DHCPSwitchesBehaviour>();

            var harmony = new HarmonyLib.Harmony(ModGuid);
            harmony.CreateClassProcessor(typeof(DHCPManager.ServerSetIpPatch)).Patch();
            harmony.CreateClassProcessor(typeof(DHCPManager.FlowPausePatch)).Patch();
            LegacyInputBlockPatches.TryApply(harmony);
            InputSystemUiCancelPatches.TryApply(harmony);

            LoggerInstance.Msg(
                "DHCP Switches & IPAM loaded. F1 = IPAM, Ctrl+L = assign all servers, title bar DHCP/IPAM toggles or Ctrl+D = lock (debug). Rack switch/router red menu = CLI, or IPAM → device → Open CLI.");
            if (!string.IsNullOrEmpty(ModDebugLog.DiagnosticLogPath))
            {
                LoggerInstance.Msg(
                    "DHCPSwitches debug log (replaced each game launch): " + ModDebugLog.DiagnosticLogPath);
            }

            if (ModDebugLog.IsIpamFileLogEnabled && !string.IsNullOrEmpty(ModDebugLog.IpamDiagnosticLogPath))
            {
                ModDebugLog.WriteIpam("Mod loaded (DHCPSwitches-ipam.flag is present).");
                LoggerInstance.Msg("DHCPSwitches IPAM diagnostic file: " + ModDebugLog.IpamDiagnosticLogPath);
            }
        }
        catch (System.Exception ex)
        {
            try
            {
                ModLogging.Instance = LoggerInstance;
                ModDebugLog.Bootstrap();
                ModDebugLog.WriteLine("OnInitializeMelon failed: " + ex);
            }
            catch
            {
                // ignore secondary failures
            }

            LoggerInstance.Error(ex);
            throw;
        }
    }

    public override void OnUpdate()
    {
        // Melon OnUpdate runs before most Unity behaviours — sync uGUI blocker early so pause menus do not eat the first click under IPAM.
        UiRaycastBlocker.SetBlocking(IPAMOverlay.IsVisible);

        // Run before default Unity script order so F1 / Ctrl+L are handled before many game scripts read the same keys.
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.f1Key.wasPressedThisFrame)
            {
                IPAMOverlay.NotifyF1ToggleHandledThisFrame();
                IPAMOverlay.IsVisible = !IPAMOverlay.IsVisible;
            }

            if (kb.leftCtrlKey.isPressed && kb.lKey.wasPressedThisFrame)
            {
                DHCPManager.AssignAllServers();
            }

            LicenseManager.HandleDebugUnlock();
        }

        if (!IPAMOverlay.IsVisible)
        {
            return;
        }

        // IPAM does not suspend PlayerInput
    }
}

/// <summary>
/// Handles per-frame input and IMGUI; IL2CPP MonoBehaviour must live in its own injected type.
/// </summary>
public class DHCPSwitchesBehaviour : MonoBehaviour
{
    internal static DHCPSwitchesBehaviour Instance { get; private set; }

    public DHCPSwitchesBehaviour(IntPtr ptr)
        : base(ptr)
    {
    }

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private int _lastSelectedServerCustomerId = -1;

    private void Update()
    {
        UiRaycastBlocker.SetBlocking(IPAMOverlay.IsVisible);

        if (IPAMOverlay.IsVisible)
        {
            // NEW: Check if the selected server was updated elsewhere (like a keypad)
            if (IPAMOverlay._selectedServer != null)
            {
                int currentActualId = IPAMOverlay._selectedServer.GetCustomerID();
                if (currentActualId != _lastSelectedServerCustomerId)
                {
                    _lastSelectedServerCustomerId = currentActualId;
                    IPAMOverlay.InvalidateCustomerCache();
                    IPAMOverlay.InvalidateDeviceCache(); // This forces a list reload
                }
            }

            IPAMOverlay.TickDeviceListCache();
            IPAMOverlay.TickInputSystemIopsToolbarClick();
            IPAMOverlay.TickIopsCalculatorInputSystem();
            IPAMOverlay.TickOctetInputSystem();
            IPAMOverlay.TickIpamPerfLog();
        }
    }

    private void LateUpdate()
    {
        IpamMenuOcclusion.Tick(IPAMOverlay.IsVisible);

        var setIp = SetIpKeypadDhcpButton.ResolveSetIPForTick();
        SetIpKeypadDhcpButton.Tick(setIp);
    }

    private void OnGUI()
    {
        IPAMOverlay.PumpImGuiInputRecovery();
        IPAMOverlay.Draw();
        if (!IPAMOverlay.IsVisible)
        {
            IPAMOverlay.PumpImGuiInputRecovery();
        }
    }
}