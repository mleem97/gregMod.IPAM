using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace GregModIPAM;

public class GregModIPAMMod : MelonMod
{
    public const string ModGuid = "com.gregmod.ipam";

    public const string DHCP_LICENSE_GUID = "dhcp-auto-assign-v1";
    public const string IPAM_LICENSE_GUID = "ipam-remote-view-v1";

    private const string PrefCategoryId = "gregMod.IPAM";
    private const string PrefCategoryName = "gregMod.IPAM";
    private const string PrefUiFontScaleKey = "IpamUiFontScale";

    private static MelonPreferences_Category _prefs;
    private static MelonPreferences_Entry<float> _prefUiFontScale;

    private static int _modSaveScopeSceneHandle = -1;

    public override void OnInitializeMelon()
    {
        try
        {
            ModLogging.Instance = LoggerInstance;
            ModDebugLog.Bootstrap();
            ModReleaseLog.Bootstrap();

            ModReleaseLog.Info("gregMod.IPAM initializing...");
            ModReleaseLog.Config("ModGuid", ModGuid);
            ModReleaseLog.Config("PrefCategoryId", PrefCategoryId);
            ModReleaseLog.Config("PrefCategoryName", PrefCategoryName);

            DeviceConfigRegistry.BootstrapLoadDisk();

            _prefs = MelonPreferences.CreateCategory(PrefCategoryId, PrefCategoryName);
            _prefUiFontScale = _prefs.CreateEntry(PrefUiFontScaleKey, 1f, "IPAM UI font scale");
            IPAMOverlay.UiFontScale = _prefUiFontScale.Value;
            IPAMOverlay.UiFontScaleChanged += OnUiFontScaleChanged;
            ModReleaseLog.Pref("UiFontScale", _prefUiFontScale.Value.ToString("F2"));

            ModReleaseLog.Info("Registering IL2CPP types...");
            ClassInjector.RegisterTypeInIl2Cpp<DHCPController>();
            ClassInjector.RegisterTypeInIl2Cpp<GregModIPAMBehaviour>();
            ModReleaseLog.Feature("IL2CPP Type Registration", "OK");

            var host = new GameObject("gregModIPAM_Host");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;
            host.AddComponent<GregModIPAMBehaviour>();
            ModReleaseLog.Info("GameObject 'gregModIPAM_Host' created");

            ModReleaseLog.Info("Applying Harmony patches...");
            var harmony = new HarmonyLib.Harmony(ModGuid);
            harmony.CreateClassProcessor(typeof(DHCPManager.ServerSetIpPatch)).Patch();
            ModReleaseLog.HarmonyPatch("DHCPManager.ServerSetIpPatch", true);
            harmony.CreateClassProcessor(typeof(DHCPManager.FlowPausePatch)).Patch();
            ModReleaseLog.HarmonyPatch("DHCPManager.FlowPausePatch", true);
            LegacyInputBlockPatches.TryApply(harmony);
            InputSystemUiCancelPatches.TryApply(harmony);
            InputSystemEscapeBlockPatches.TryApply(harmony);
            InputSystemMouseBlockPatches.TryApply(harmony);

            ModReleaseLog.Feature("DHCP Auto-Assign", LicenseManager.IsDHCPUnlocked ? "Unlocked" : "Locked");
            ModReleaseLog.Feature("IPAM Overlay", LicenseManager.IsIPAMUnlocked ? "Unlocked" : "Locked");
            ModReleaseLog.Feature("Shared Server Mode", "Available");
            ModReleaseLog.Feature("Network Health Score", "Available");

            LoggerInstance.Msg(
                "gregMod.IPAM loaded. P = IPAM, Ctrl+L = assign all servers, title bar DHCP/IPAM toggles.");
            ModReleaseLog.Info("gregMod.IPAM loaded successfully");

            if (!string.IsNullOrEmpty(ModDebugLog.DiagnosticLogPath))
            {
                LoggerInstance.Msg(
                    "gregMod.IPAM debug log: " + ModDebugLog.DiagnosticLogPath);
                ModReleaseLog.Info($"Debug log: {ModDebugLog.DiagnosticLogPath}");
            }

            ModReleaseLog.Info($"Release log: {ModReleaseLog.LogPath}");
            ModReleaseLog.Info("");
        }
        catch (System.Exception ex)
        {
            try
            {
                ModLogging.Instance = LoggerInstance;
                ModDebugLog.Bootstrap();
                ModDebugLog.WriteLine("OnInitializeMelon failed: " + ex);
                ModReleaseLog.Error("OnInitializeMelon failed", ex);
            }
            catch
            {
                // ignore secondary failures
            }

            LoggerInstance.Error(ex);
            throw;
        }
    }

    private static void OnUiFontScaleChanged(float newScale)
    {
        if (_prefUiFontScale == null)
        {
            return;
        }

        _prefUiFontScale.Value = newScale;
        MelonPreferences.Save();
    }

    public override void OnUpdate()
    {
        // Melon OnUpdate runs before most Unity behaviours — sync uGUI blocker early so pause menus do not eat the first click under IPAM.
        UiRaycastBlocker.SetBlocking(IPAMOverlay.IsVisible);

        // Keep input suppression active for a short window after overlay closes via Escape
        // so the game does not see the same Escape press and open the pause menu.
        if (!IPAMOverlay.IsVisible && IPAMOverlay.IsInEscapeCooldown())
        {
            GameInputSuppression.SetSuppressed(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (!IPAMOverlay.IsVisible && GameInputSuppression.IsActive)
        {
            GameInputSuppression.SetSuppressed(false);
        }

        // Run before default Unity script order so keys are handled before many game scripts read the same keys.
        var kb = Keyboard.current;
        if (kb != null)
        {
            // P toggles IPAM — but NOT while pause menu is active
            if (kb.pKey.wasPressedThisFrame && !IPAMOverlay.IsVisible && !IPAMOverlay.IsPauseMenuActive())
            {
                IPAMOverlay.IsVisible = true;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            if (kb.leftCtrlKey.isPressed && kb.lKey.wasPressedThisFrame)
            {
                DHCPManager.AssignAllServers();
            }
        }

        if (!IPAMOverlay.IsVisible)
        {
            return;
        }

        // Force cursor visible and unlocked every frame while IPAM is open
        // (game may override cursor state in its own Update/LateUpdate).
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        IPAMOverlay.TickIpamGameInputSuppression();
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        ModReleaseLog.SceneLoaded(sceneName, buildIndex);
        TryNotifyModSaveScopeSceneChange();
    }

    internal static void TryNotifyModSaveScopeSceneChange()
    {
        try
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                return;
            }

            var handle = scene.handle;
            if (handle == _modSaveScopeSceneHandle)
            {
                return;
            }

            _modSaveScopeSceneHandle = handle;
            ModSaveScope.NotifySceneLoaded();
        }
        catch
        {
            // Il2Cpp: scene handle can be invalid during boot or unload transitions.
        }
    }
}

/// <summary>
/// Handles per-frame input and IMGUI; IL2CPP MonoBehaviour must live in its own injected type.
/// </summary>
public class GregModIPAMBehaviour : MonoBehaviour
{
    internal static GregModIPAMBehaviour Instance { get; private set; }

    public GregModIPAMBehaviour(IntPtr ptr)
        : base(ptr)
    {
    }

    private int _deferredInitialSceneSyncFrames = 1;

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
        if (_deferredInitialSceneSyncFrames > 0)
        {
            _deferredInitialSceneSyncFrames--;
            if (_deferredInitialSceneSyncFrames == 0)
            {
                GregModIPAMMod.TryNotifyModSaveScopeSceneChange();
            }
        }

        ModSaveScope.TickCapture();
        UiRaycastBlocker.SetBlocking(IPAMOverlay.IsVisible);

        if (IPAMOverlay.IsVisible)
        {
            IPAMOverlay.TickIpamEscapeEdgeDetection();

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
            IPAMOverlay.TickInputSystemWindowResize();
            IPAMOverlay.TickInputSystemIopsToolbarClick();
            IPAMOverlay.TickIopsCalculatorInputSystem();
            IPAMOverlay.TickCustomersAddServerWizardInput();
            IPAMOverlay.TickIpamFormInputSystem();
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