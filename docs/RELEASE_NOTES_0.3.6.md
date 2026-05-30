# Release 0.3.6

Changes since **0.3.5** (Racks tab, prefix wizard).

## Save / session stability

- **Per-save data binding** (`ModSaveScope`): IPAM prefixes, racks, and related JSON are scoped to the active playthrough so a new game does not inherit data from a deleted save.
- **Scene reload UI reset**: After loading a save or changing scenes, the IPAM overlay rebuilds textures/styles, resets scroll state, and closes stale popups (fixes transparent overlay, overlapping text, and misaligned tables after restart).
- **Safe scroll fallback** (`SafeScroll`): Manual scroll implementation when Unity’s `GUI.BeginScrollView` is broken on current IL2CPP builds.

## IPAM — prefixes & assignments

- **Prefix picker overhaul**: Shows all registered prefixes plus **Available** free blocks under folder prefixes (not just leaf rows).
- **Auto-create prefix** when assigning servers from an **Available · x.x.x.x/N** row so the prefix appears in the Prefixes list.
- **IP addresses tab** shows IPv4 with containing CIDR, e.g. `10.10.0.7  (10.10.0.0/24)`.
- **Delete parent prefix confirmation**: Deleting a folder prefix that has children opens a confirmation dialog listing child subnets before removing the subtree. Leaf prefixes still delete immediately.

## IPAM — tables & columns

- **Fit columns** now sizes against the actual table width (excluding the gear column) and distributes space proportionally across all columns.
- **IPv4 and EOL** columns no longer stay truncated while Device/Customer absorb all slack.
- **Column resize grips** (teal bars): wider hit targets, no longer blocked by sortable header clicks, unique control IDs per table on the Devices tab.

## Racks tab

- **Drag-only rack placement** for servers, switches, routers, and patch panels (removed **Add to rack** button).
- **Dedicated drag row** below each pick list fixes drop-zone offset inside nested scroll areas.
- Improved contrast for drag rows, rack diagram, and drop preview.
- Fixed accidental pick deselect when clicking inside the list viewport.

## IOPS calculator

- Single **3 U / 7 U** server type toggle (no mixed-size optimization).
- Output: `N × {size} servers`, total rack U, and delivered IOPS.

## Install

Copy `dist/gregMod.IPAM.dll` (or `bin/Release/net6.0/DHCPSwitches.dll`) into your game **Mods** folder alongside `gregCore.dll`.
