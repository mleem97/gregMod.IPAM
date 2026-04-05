# C# source layout

All compilation units use the root namespace **`DHCPSwitches`** (see `DHCPSwitches.csproj`). Folders are for navigation only; no `DHCPSwitches.SubFolder` namespaces.

| Folder | Role |
|--------|------|
| **`Core/`** | MelonLoader entry (`MelonMod`, `MelonModInfo`), injected `MonoBehaviour`, global usings, nullable shims. |
| **`Networking/`** | DHCP + Harmony on `SetIP`, flow/`AddAppPerformance` gate, reachability (L3 enforce), game subnet/customer caches, private LAN registry, IPv4 helpers, router vs switch classification, `ServerCustomerBinding` (assign `customerID` via reflection). |
| **`Routing/`** | Forwarding table resolution, static vs connected routes, L3 validation, switch-port / cable probes used by ping and routing. |
| **`Cli/`** | Cisco-style terminal UI, autocomplete, rack hook to open CLI, terminal overlay + input pump. |
| **`Config/`** | Per-device router/switch config model, registry, disk persistence, stable device IDs. |
| **`Ipam/`** | IPAM IMGUI overlay, license toggles used by toolbar features. |
| **`Ping/`** | Ping target resolution, hop paths / visuals helpers, port–cable proximity scoring. |
| **`Patches/`** | Standalone Harmony patches (legacy input block, UI cancel / Input System). |
| **`Input/`** | Suspend game `PlayerInput` while overlays are open, legacy axis reset, IMGUI raycast blocking. |
| **`Diagnostics/`** | File + Melon logging helpers (`DHCPSwitches-debug.log`, trace flag). |

## Quick map (file → folder)

- **Core:** `Main.cs`, `MelonModInfo.cs`, `Il2CppGlobalUsings.cs`, `NullableAttributes.cs`
- **Networking:** `DHCPManager.cs`, `ReachabilityService.cs`, `GameSubnetHelper.cs`, `CustomerPrivateSubnetRegistry.cs`, `Ipv4Rfc1918.cs`, `RouteMath.cs`, `NetworkDeviceClassifier.cs`, `ServerCustomerBinding.cs`
- **Routing:** `RouterForwarding.cs`, `RouterL3Validation.cs`, `SwitchPortHardwareProbe.cs`
- **Cli:** `CiscoLikeCli.cs`, `CliAutocomplete.cs`, `DeviceTerminalOverlay.cs`, `RackSwitchCliHook.cs`
- **Config:** `DeviceConfigRegistry.cs`, `DeviceConfigModels.cs`, `DeviceConfigPersistence.cs`, `DeviceStableId.cs`
- **Ipam:** `IPAMOverlay.cs`, `LicenseManager.cs`
- **Ping:** `PingPacketPaths.cs`, `PingTargetResolver.cs`, `PortCableProximity.cs`
- **Patches:** `LegacyInputBlockPatches.cs`, `InputSystemUiCancelPatches.cs`
- **Input:** `GameInputSuppression.cs`, `LegacyInputAxes.cs`, `UiRaycastBlocker.cs`
- **Diagnostics:** `ModDebugLog.cs`, `ModLogging.cs`

The project file **`DHCPSwitches.csproj`** stays at the repository root; SDK-style includes pick up all `*.cs` under the project directory (excluding `bin/` and `obj/`).

**`StreamingAssets.Mods/`** — template `config.json` (+ README) for the game’s passive shop pipeline; copy into `Data Center_Data/StreamingAssets/Mods/` and add `model.obj` / textures from your install (not redistributed here).
