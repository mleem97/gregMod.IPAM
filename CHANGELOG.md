# Changelog — gregMod.IPAM

Format: [Keep a Changelog](https://keepachangelog.com/de/1.0.0/). Version: see [`VERSION`](VERSION).

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
