# Changelog

## v0.7.5

- Fixed inline modal input routing so popup controls receive IMGUI mouse clicks again
- Blocked click-through from modal buttons into the underlying inventory and detail views
- Restored server edit popup actions including customer selection, DHCP/IP actions, and close/apply/cancel flows
- Moved server edit popup controls to a dedicated IMGUI control-ID range to avoid collisions with main-window navigation

## v0.6.6

- Per-save data binding (`ModSaveScope`): IPAM prefixes, racks, and related JSON scoped to active playthrough
- Naming templates with bulk rename (`NamingTemplateEngine`, `NamingConventionStore`)
- Network health score (`NetworkHealthScore`)
- `IpamFreeSpace` and `IpamPrefixAvailability` helpers
- Verbose release log (`ipam.latest.log`)
- IPAM prefix tree, prefix wizard, and prefix delete confirmation (IPAMOverlay partials)
- Customers tab, racks tab, naming section, safe scroll fallback
- DHCP button on SetIP keypad (`SetIpKeypadDhcpButton`)
- Input system escape/mouse block patches

## v0.5.0

- IPAM overlay with dashboard, device, and IP views
- Device inventory via in-game reflection (`DeviceInventoryReflection`)
- IOPS calculator modal
- Customer server assignment modal
- IPv4/subnet helpers and private subnet logic (`Ipv4Rfc1918`, `CustomerPrivateSubnetRegistry`)
- DHCP scope management (Global / VLAN / Switch)
- Network device classifier and rack layout helpers
- Rack data store and rack type probe
- Persistent device configuration (`DeviceConfigRegistry`, `DeviceConfigModels`, `DeviceConfigPersistence`)
- Device stable IDs (`DeviceStableId`)
- License manager for DHCP and IPAM feature toggles
- Game technician dispatch (`GameTechnicianDispatch`)
- Game input suppression while overlays are open
- UI raycast blocking for IMGUI focus
- Legacy input and Input System Harmony patches
- Debug logging (`ModDebugLog`, `ModLogging`)
- IL2CPP injected MonoBehaviour (`GregModIPAMBehaviour`) for per-frame input/IMGUI

## v0.3.6

- Prefix picker overhaul: shows all registered prefixes plus free blocks
- Auto-create prefix when assigning servers from Available rows
- IP addresses tab with CIDR display
- Delete parent prefix confirmation dialog
- Column fit/resize for device and IP tables
- Racks tab with drag-only rack placement
- IOPS calculator (3U/7U server type toggle)
- Scene reload UI reset (textures, styles, scroll state)

## v0.3.0

- Initial IPAM overlay concept
- Basic device list and IP management
- DHCP auto-assign foundation
- Harmony patches for SetIP flow
