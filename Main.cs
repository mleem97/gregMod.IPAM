using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using MelonLoader;
using UnityEngine.InputSystem;

[assembly: MelonInfo(typeof(DHCPSwitches.DHCPSwitchesMod), "DHCP Switches & IPAM", "1.0.0", "Marvin")]
[assembly: MelonGame("WASEKU", "Data Center")]

namespace DHCPSwitches
{
    public class DHCPSwitchesMod : MelonMod
    {
        public static DHCPSwitchesMod Instance { get; private set; }
        internal static Harmony ModHarmony { get; private set; }

        public const string DHCP_LICENSE_GUID = "dhcp-auto-assign-v1";
        public const string IPAM_LICENSE_GUID = "ipam-remote-view-v1";

        public override void OnInitializeMelon()
        {
            Instance = this;

            ClassInjector.RegisterTypeInIl2Cpp<DHCPController>();

            ModHarmony = new Harmony("com.marvin.dhcpswitches");
            ModHarmony.PatchAll();

            MelonLogger.Msg("DHCP Switches & IPAM geladen. I = IPAM, Strg+L = DHCP auf alle Server, Strg+D = Debug Unlock.");
        }

        public override void OnUpdate()
        {
            var kb = Keyboard.current;
            if (kb == null)
            {
                return;
            }

            if (kb.iKey.wasPressedThisFrame)
            {
                IPAMOverlay.IsVisible = !IPAMOverlay.IsVisible;
            }

            if (kb.leftCtrlKey.isPressed && kb.lKey.wasPressedThisFrame)
            {
                DHCPManager.AssignAllServers();
            }

            LicenseManager.HandleDebugUnlock();
        }

        public override void OnGUI()
        {
            IPAMOverlay.Draw();
        }
    }
}
