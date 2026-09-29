# Changelog — gregMod.IPAM

Format: [Keep a Changelog](https://keepachangelog.com/de/1.0.0/). Version: see [`VERSION`](VERSION).

## [Unreleased]

### Fixed

- Save loss on relog ("log back in, everything done is gone"): mod UserData
  (IPAM prefixes, racks, naming, cabling) is now persistent and never
  auto-wiped. The old save-scope hash (scene + device counts + money) changed
  on normal progress, so reloading the SAME save looked like a "new game" and
  deleted all JSONs. `ModSaveScope` now only keeps a diagnostic marker;
  `TryResetAllModData()` is the single explicit reset path. Stores also merge
   pre-load writes instead of clobbering the on-disk file with an empty root.
- Crash-safe writes: all mod JSONs (IPAM, racks, naming, cabling, device
  configs, save binding) now write atomic (tmp + move, `.bak` kept) and read
  back from the backup when the main file is torn — a crash mid-write no
  longer silently resets data to empty.
- Router/switch runtime configs are actually persisted now
  (`DeviceConfigRegistry.TrySaveAllToDisk` previously had no caller):
  autosave every 5 minutes plus on application quit.
- Deterministic DHCP assignment order (stable device key, then name):
  batch assignment no longer follows undefined `FindObjectsOfType` order, so
  empty-IP servers keep getting the same addresses across relogs.
- Per-save namespacing: mod JSON is stored under
  `UserData/gregMod.IPAM/saves/<save>/` when a save is loaded, so saves stop
  sharing one dataset. Loading falls back to the global legacy files (soft
  migration — globals are never moved or deleted).
- Title bar "Reset data" button (two-click confirm): the single explicit path
  to delete this save's IPAM data.
- Stale references after reload: server tenancies and rack mounts now store a
  stable device key (scene + hierarchy path + position) next to the session-local
  instance ID. Lookups match by stable key (path-only fallback for moved devices)
  and heal the stored instance ID, so assignments survive log back in. Cabling
  and power paths resolve mounts through the same helper.

- Toggle hotkey (default P) no longer fires while typing into a text
  field (labeling): `IPAMOverlay.IsTextInputActive()` covers IMGUI
  focus, the mod's own form/octet fields, uGUI text inputs and virtual
  keyboards.
- Closing the overlay no longer leaves the previous screen stuck:
  cursor lock/visibility is snapshotted on open and restored on close,
  the pre-open uGUI selection is restored when nothing else took
  focus, and close no longer clears the game's EventSystem selection.

## [0.8.1] — 2026-09-24

### Changed

- F1-hub wiring via GregMenuBinding.BindToggle.
- English strings and WebUI.

## [0.8.0] — 2026-09-24

### Added

- Bulk actions via checkbox (Devices, IP, and Customers lists): bulk bar on selection (assign DHCP, technician, deselect). Sorting still via clickable column headers (▲▼).
- WebUI: Racks tab (build mounts incl. cheat option, list racks/templates, apply template); API: `racks/mounts`, `racks/install`, `racks/list`, `racks/templates`, `racks/apply-template`.
- Technician dispatch: "Tech" button on broken servers + bulk panel ("Send to all broken devices", queue status) — uses the vanilla dispatch paths.
- Router management (Devices → Routers): view, add, remove subnets/routes, reapply, same-ASN sync.
- Firewall management (Devices → Firewall): view, add, remove rules, cluster sync/broadcast, traffic test.
- Hard assignment locks: network (.0) and broadcast address (.255) are never assigned (all paths) + `ExcludeIps` pref for custom exclusions.
- React web UI in the background (127.0.0.1:8177, prefs `WebEnabled`/`WebPort`): dashboard, server control (DHCP/IP/rename/power), scopes CRUD, logs; JSON API + main-thread queue; frontend in `web/` (Vite+React+TS), deploy via `scripts/deploy-web.sh`.
- Server selection: Ctrl+A (Cmd+A) selects all displayed servers (Devices, IP, and Customers lists, current page); Shift-click range now starts at the server last selected via Ctrl.
- Naming: "Reset saved seq counter" button (reset persisted counters).
- `ExcludeGateway` pref (default on) + toggle in DHCP Scopes: DHCP never assigns .1 on /24 or shorter, including in private networks.
- Configurable toggle hotkey (`ToggleKey` pref, default P), key HUD entry and opener for the F1 hub (only with gregCore).
- Unified open-source layout (README, docs, badges) following the gregCore template.

### Fixed

- Racks page scales with UI font scale (front view, table layout, cached styles are rebuilt on scale change).
- Opening/closing no longer freezes for seconds: reflection lookups cached (once per type/name instead of per device), full EOL snapshot only on empty cache.

## [0.1.0] — 2026-09-22

- Initial standardized baseline.
