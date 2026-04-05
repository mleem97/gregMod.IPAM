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
            ModDebugLog.Bootstrap();
            ModLogging.Instance = LoggerInstance;
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
                LoggerInstance.Msg("DHCPSwitches diagnostic file: " + ModDebugLog.DiagnosticLogPath);
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
        UiRaycastBlocker.SetBlocking(IPAMOverlay.IsVisible || DeviceTerminalOverlay.IsVisible);

        // Run before default Unity script order so F1 / Ctrl+L are handled before many game scripts read the same keys.
        var kb = Keyboard.current;
        if (kb != null)
        {
            var cliOpen = DeviceTerminalOverlay.IsVisible;

            if (!cliOpen && kb.f1Key.wasPressedThisFrame)
            {
                IPAMOverlay.NotifyF1ToggleHandledThisFrame();
                IPAMOverlay.IsVisible = !IPAMOverlay.IsVisible;
            }

            if (!cliOpen && kb.leftCtrlKey.isPressed && kb.lKey.wasPressedThisFrame)
            {
                DHCPManager.AssignAllServers();
            }

            if (!cliOpen)
            {
                LicenseManager.HandleDebugUnlock();
            }

            if (!cliOpen)
            {
                RackSwitchCliHook.TryHandlePhysicalConsoleClick();
            }
        }

        var anyOverlay = IPAMOverlay.IsVisible || DeviceTerminalOverlay.IsVisible;
        if (!anyOverlay)
        {
            return;
        }

        // IPAM uses F1 (not I) and does not suspend PlayerInput — only the CLI needs full lock + legacy axis reset.
        if (!DeviceTerminalOverlay.IsVisible)
        {
            return;
        }

        DeviceTerminalOverlay.PumpInputSystemEscapeSinkForCli();
        GameInputSuppression.RefreshWhileActive();
        LegacyInputAxes.TryReset();
    }
}

/// <summary>
/// Handles per-frame input and IMGUI; IL2CPP MonoBehaviour must live in its own injected type.
/// </summary>
public class DHCPSwitchesBehaviour : MonoBehaviour
{
    internal static DHCPSwitchesBehaviour Instance { get; private set; }

    private static int s_pingSessionId;

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
        CancelPingVisuals();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>Stops continuous ping and destroys in-flight packet spheres when the CLI closes or a new ping starts.</summary>
    internal static void CancelPingVisuals()
    {
        s_pingSessionId++;
    }

    /// <summary>Spawns one sphere per echo (4 by default) or runs forever with <paramref name="continuous"/> until cancelled.</summary>
    internal static void BeginPingBurst(Vector3[] path, bool continuous)
    {
        if (path == null || path.Length < 2)
        {
            return;
        }

        var pathCopy = (Vector3[])path.Clone();
        CancelPingVisuals();
        var sessionId = s_pingSessionId;
        MelonCoroutines.Start(PingBurstEnumerator(sessionId, pathCopy, continuous));
    }

    private static IEnumerator PingBurstEnumerator(int sessionId, Vector3[] path, bool continuous)
    {
        const float travelTime = 0.26f;
        const float stagger = 0.055f;
        const int echoCount = 4;
        const float continuousInterval = 0.4f;

        if (!continuous)
        {
            for (var i = 0; i < echoCount; i++)
            {
                if (sessionId != s_pingSessionId)
                {
                    yield break;
                }

                MelonCoroutines.Start(SinglePingSphereEnumerator(sessionId, path, travelTime));
                if (i < echoCount - 1)
                {
                    yield return new WaitForSeconds(stagger);
                }
            }

            yield break;
        }

        while (sessionId == s_pingSessionId && DeviceTerminalOverlay.IsVisible)
        {
            MelonCoroutines.Start(SinglePingSphereEnumerator(sessionId, path, travelTime));
            yield return new WaitForSeconds(continuousInterval);
        }
    }

    private static IEnumerator SinglePingSphereEnumerator(int sessionId, Vector3[] path, float duration)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "ModPingPacket";
        go.transform.localScale = Vector3.one * 0.12f;
        var col = go.GetComponent<Collider>();
        if (col != null)
        {
            UnityEngine.Object.Destroy(col);
        }

        var r = go.GetComponent<Renderer>();
        if (r != null)
        {
            ApplyPingSphereMaterial(r);
        }

        var totalLen = PolylineLength(path);
        if (totalLen < 1e-4f)
        {
            totalLen = 1f;
        }

        var t = 0f;
        while (t < duration && sessionId == s_pingSessionId && DeviceTerminalOverlay.IsVisible)
        {
            t += Time.deltaTime;
            var u = Mathf.Clamp01(t / duration);
            go.transform.position = PointAlongPolyline(path, totalLen, u);
            yield return null;
        }

        if (go != null)
        {
            UnityEngine.Object.Destroy(go);
        }
    }

    private static void ApplyPingSphereMaterial(Renderer r)
    {
        var sh = Shader.Find("Unlit/Color")
                 ?? Shader.Find("Universal Render Pipeline/Unlit")
                 ?? Shader.Find("Sprites/Default")
                 ?? Shader.Find("Standard");
        if (sh != null)
        {
            r.material = new Material(sh);
        }

        r.material.color = new Color(0.2f, 1f, 0.35f, 1f);
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    private static float PolylineLength(Vector3[] p)
    {
        var sum = 0f;
        for (var i = 1; i < p.Length; i++)
        {
            sum += Vector3.Distance(p[i - 1], p[i]);
        }

        return sum;
    }

    private static Vector3 PointAlongPolyline(Vector3[] p, float totalLen, float u)
    {
        if (p.Length == 1)
        {
            return p[0];
        }

        var target = Mathf.Clamp01(u) * totalLen;
        var acc = 0f;
        for (var i = 1; i < p.Length; i++)
        {
            var seg = Vector3.Distance(p[i - 1], p[i]);
            if (acc + seg >= target || i == p.Length - 1)
            {
                var lt = seg > 1e-5f ? (target - acc) / seg : 1f;
                return Vector3.Lerp(p[i - 1], p[i], Mathf.Clamp01(lt));
            }

            acc += seg;
        }

        return p[^1];
    }

    private void Update()
    {
        if (DeviceTerminalOverlay.IsVisible)
        {
            DeviceTerminalOverlay.TickCliInput();
        }

        var anyOverlay = IPAMOverlay.IsVisible || DeviceTerminalOverlay.IsVisible;
        UiRaycastBlocker.SetBlocking(anyOverlay);
        GameInputSuppression.SetSuppressed(DeviceTerminalOverlay.IsVisible);

        if (IPAMOverlay.IsVisible)
        {
            IPAMOverlay.TickDeviceListCache();
            IPAMOverlay.TickInputSystemIopsToolbarClick();
            IPAMOverlay.TickIopsCalculatorInputSystem();
        }
    }

    private void LateUpdate()
    {
        var anyOverlay = IPAMOverlay.IsVisible || DeviceTerminalOverlay.IsVisible;
        IpamMenuOcclusion.Tick(anyOverlay);
    }

    private void OnGUI()
    {
        IPAMOverlay.PumpImGuiInputRecovery();
        IPAMOverlay.Draw();
        DeviceTerminalOverlay.Draw();
        if (!IPAMOverlay.IsVisible && !DeviceTerminalOverlay.IsVisible)
        {
            IPAMOverlay.PumpImGuiInputRecovery();
        }
    }
}
