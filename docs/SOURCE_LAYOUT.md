# Source layout

All compilation units use the root namespace **`GregModIPAM`** (see `gregMod.IPAM.csproj`). Folders are for navigation only; no `GregModIPAM.SubFolder` namespaces.

## Folder overview

| Folder | Role |
|--------|------|
| **`Core/`** | MelonLoader entry (`MelonMod`, `MelonModInfo`), injected `MonoBehaviour`, save scope, global usings, nullable shims. |
| **`Networking/`** | DHCP + Harmony on `SetIP`, flow gate, game subnet/customer caches, IPv4 helpers, server-customer binding, reachability, route math, rack helpers, network health. |
| **`Config/`** | Per-device config model, registry, disk persistence, stable device IDs. |
| **`Ipam/`** | IPAM IMGUI overlay (`IPAMOverlay` partials), device display reflection, menu occlusion, license toggles, naming engine, nav icons, data stores. |
| **`Patches/`** | Standalone Harmony patches (legacy input block, UI cancel / Input System, escape/mouse block, SetIP keypad DHCP button). |
| **`Input/`** | Suspend game `PlayerInput` while overlays are open, legacy axis reset, IMGUI raycast blocking. |
| **`Diagnostics/`** | File + Melon logging helpers (debug log, release log). |

## File map

### Core/
| File | Role |
|------|------|
| `Main.cs` | MelonLoader entry point (`GregModIPAMMod`), IL2CPP type registration, Harmony patching, keyboard shortcuts (P, Ctrl+L), injected `GregModIPAMBehaviour` MonoBehaviour. |
| `MelonModInfo.cs` | Assembly-level MelonInfo/MelonGame attributes. |
| `ModSaveScope.cs` | Per-save data binding: scopes IPAM/rack JSON to active playthrough via SHA256 hash of scene + device counts + money. Wipes mod UserData on new game. |
| `Il2CppGlobalUsings.cs` | Global using directives for Il2Cpp interop. |
| `NullableAttributes.cs` | Polyfill nullable attributes for .NET 6. |

### Networking/
| File | Role |
|------|------|
| `DHCPManager.cs` | DHCP auto-assign: Harmony patches on `SetIP` (server) and flow pause gate. Assigns IPs from scope. |
| `GameSubnetHelper.cs` | Reads game's subnet state via reflection. |
| `CustomerPrivateSubnetRegistry.cs` | Tracks customer → private subnet mappings. |
| `Ipv4Rfc1918.cs` | RFC 1918 private range helpers. |
| `RouteMath.cs` | Subnet/route calculation utilities. |
| `NetworkDeviceClassifier.cs` | Classifies devices (server, switch, router) via reflection. |
| `ServerCustomerBinding.cs` | Assigns `customerID` to servers via reflection. |
| `ReachabilityService.cs` | Network reachability checks. |
| `GameRackSceneScanner.cs` | Scans scene for rack objects. |
| `RackLayoutHelper.cs` | Rack slot layout utilities. |
| `RackTypeProbe.cs` | Probes rack type (server/switch/patch). |
| `NetworkHealthScore.cs` | Computes network health metrics. |
| `IpamFreeSpace.cs` | Free IP space calculation within prefixes. |
| `IpamPrefixAvailability.cs` | Prefix availability checking. |

### Config/
| File | Role |
|------|------|
| `DeviceConfigModels.cs` | Data models for per-device router/switch config. |
| `DeviceConfigRegistry.cs` | In-memory registry + disk bootstrap load. |
| `DeviceConfigPersistence.cs` | JSON read/write for device config files. |
| `DeviceStableId.cs` | Stable device ID generation (survives scene reloads). |

### Ipam/
| File | Role |
|------|------|
| `IPAMOverlay.cs` | Hub: all static fields, layout constants, `IsVisible`, `Draw()` (modal blocker → main window → IOPS window). |
| `IPAMOverlay.ImGui.cs` | Procedural textures, `GUIStyle` setup, toolbar buttons, toast text. |
| `IPAMOverlay.InventoryTable.cs` | EOL display snapshot, column weights/auto-fit, sort + table row/header IMGUI. |
| `IPAMOverlay.Lifecycle.cs` | `TickDeviceListCache`, Input System IOPS toolbar click + digit pump, `InvalidateDeviceCache`, IMGUI recovery. |
| `IPAMOverlay.IopsModal.cs` | Standalone IOPS `GUI.Window`, IMGUI `KeyDown` pump. |
| `IPAMOverlay.IpamFormInput.cs` | IPAM form field input handling. |
| `IPAMOverlay.IpamPrefixDeleteConfirm.cs` | Delete parent prefix confirmation dialog. |
| `IPAMOverlay.IpamPrefixTree.cs` | Prefix tree / picker UI. |
| `IPAMOverlay.IpamPrefixWizard.cs` | New prefix creation wizard. |
| `IPAMOverlay.IpamTabs.cs` | Tab navigation (Dashboard, Devices, IPs, etc.). |
| `IPAMOverlay.WindowUi.cs` | `DrawWindow`, nav sections, dashboard/device/IP views, selection + detail panel, octet editor. |
| `IPAMOverlay.CustomersTab.cs` | Customers management tab. |
| `IPAMOverlay.RacksTab.cs` | Racks tab with drag-only rack placement. |
| `IPAMOverlay.NamingSection.cs` | Device naming section with template engine. |
| `IPAMOverlay.SafeScroll.cs` | Manual scroll fallback when Unity's `GUI.BeginScrollView` is broken on IL2CPP. |
| `IPAMOverlay.ServerAssignModal.cs` | Customer server assignment modal. |
| `DeviceInventoryReflection.cs` | Reads device inventory via reflection. |
| `GameTechnicianDispatch.cs` | Calls game `AssetManagement` / device line technician API. |
| `IpamMenuOcclusion.cs` | Hides game menus while IPAM overlay is open. |
| `LicenseManager.cs` | Feature toggle for DHCP and IPAM licenses. |
| `NamingConventionStore.cs` | Persistent naming convention data. |
| `NamingTemplateEngine.cs` | Template-based bulk rename engine. |
| `NavIcons.cs` | Navigation icon textures. |
| `IpamDataStore.cs` | In-memory IPAM prefix/assignment data. |
| `RackDataStore.cs` | In-memory rack placement data. |

### Patches/
| File | Role |
|------|------|
| `LegacyInputBlockPatches.cs` | Blocks legacy input axes while IPAM is open. |
| `InputSystemUiCancelPatches.cs` | Blocks Input System UI cancel while IPAM is open. |
| `InputSystemEscapeBlockPatches.cs` | Manages Escape key: close IPAM + open pause menu. |
| `InputSystemMouseBlockPatches.cs` | Blocks mouse input propagation while IPAM is open. |
| `SetIpKeypadDhcpButton.cs` | Adds DHCP button to SetIP keypad UI. |

### Input/
| File | Role |
|------|------|
| `GameInputSuppression.cs` | Suspends game `PlayerInput` actions while overlays are open. |
| `IpamGameInputGate.cs` | Input gate for IPAM-specific game input blocking. |
| `LegacyInputAxes.cs` | Resets legacy input axes to zero. |
| `UiRaycastBlocker.cs` | Blocks IMGUI raycasts so game UI doesn't eat clicks under IPAM. |

### Diagnostics/
| File | Role |
|------|------|
| `ModDebugLog.cs` | Writes debug log (`gregModIPAM-debug.log`) next to MelonLoader's `Latest.log`. |
| `ModLogging.cs` | Thin wrapper around MelonLogger for consistent mod logging. |
| `IpamDebugLog.cs` | IPAM-specific debug log with trace flag. |
| `ModReleaseLog.cs` | Verbose release log (`ipam.latest.log`) with environment info and categorized events. |

## `IPAMOverlay` partial map

| File | Responsibility |
|------|----------------|
| `IPAMOverlay.cs` | All static fields / layout constants; `IsVisible`; `Draw()` (modal blocker → main window → IOPS window order). |
| `IPAMOverlay.ImGui.cs` | Procedural textures, `GUIStyle` setup, `ImguiButtonOnce`, octet / IOPS toolbar buttons, toast text. |
| `IPAMOverlay.InventoryTable.cs` | EOL display snapshot (`_eolDisplayByInstanceId`), column weights / auto-fit, sort + table row/header IMGUI. |
| `IPAMOverlay.Lifecycle.cs` | `TickDeviceListCache`, Input System IOPS toolbar click + digit pump, `InvalidateDeviceCache`, IMGUI recovery, `FilterAlive`. |
| `IPAMOverlay.IopsModal.cs` | Standalone IOPS `GUI.Window`, IMGUI `KeyDown` pump when no keyboard device, IPAM debug mouse line. |
| `IPAMOverlay.WindowUi.cs` | `DrawWindow`, nav sections, dashboard / devices / IP views, selection + detail panel, octet editor. |
| `IPAMOverlay.IpamTabs.cs` | Tab navigation between Dashboard, Devices, IPs, Customers, Racks views. |
| `IPAMOverlay.CustomersTab.cs` | Customers management tab with server assignment. |
| `IPAMOverlay.RacksTab.cs` | Racks tab with drag-only rack placement for servers, switches, routers, patch panels. |
| `IPAMOverlay.NamingSection.cs` | Device naming section with template-based bulk rename. |
| `IPAMOverlay.SafeScroll.cs` | Manual scroll fallback for broken Unity `GUI.BeginScrollView` on IL2CPP. |
| `IPAMOverlay.ServerAssignModal.cs` | Modal dialog for assigning servers to customers. |
| `IPAMOverlay.IpamPrefixTree.cs` | Prefix tree / picker UI for IPAM prefix management. |
| `IPAMOverlay.IpamPrefixWizard.cs` | Wizard for creating new IPAM prefixes. |
| `IPAMOverlay.IpamPrefixDeleteConfirm.cs` | Confirmation dialog for deleting parent prefixes with children. |
| `IPAMOverlay.IpamFormInput.cs` | IPAM form field input handling (tab cycling, field navigation). |

The project file **`gregMod.IPAM.csproj`** stays at the repository root; SDK-style includes pick up all `*.cs` under the project directory (excluding `bin/` and `obj/`).

**`StreamingAssets.Mods/`** — template `config.json` (+ README) for the game's passive shop pipeline; copy into `Data Center_Data/StreamingAssets/Mods/` and add `model.obj` / textures from your install (not redistributed here).
