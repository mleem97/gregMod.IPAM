# gregMod.IPAM

> IP Address Management, DHCP workflows, and network tooling for **Data Center** — built for the **gregFramework** ecosystem.

[![Discord](https://img.shields.io/badge/Discord-Join-5865F2?style=for-the-badge&logo=discord&logoColor=white)](https://discord.gg/greg)
[![gregFramework](https://img.shields.io/badge/gregFramework-Website-blue?style=for-the-badge)](https://gregframework.eu)
[![License](https://img.shields.io/badge/License-Apache%202.0-green?style=for-the-badge)](./LICENSE)
[![Version](https://img.shields.io/badge/Version-0.5.0-orange?style=for-the-badge)](./ROADMAP.md)
[![GameVersion](https://img.shields.io/badge/Game%20Version-1.1-yellow?style=for-the-badge)]()
[![Unity](https://img.shields.io/badge/Unity-6000.5-black?style=for-the-badge&logo=unity&logoColor=white)]()

## Links

- **Website:** [gregframework.eu](https://gregframework.eu)
- **Discord / Support:** [discord.gg/greg](https://discord.gg/greg)
- **Repository:** [github.com/mleem97/gregMod.IPAM](https://github.com/mleem97/gregMod.IPAM)
- **Roadmap:** [`ROADMAP.md`](./ROADMAP.md)

## Overview

**gregMod.IPAM** extends **Data Center** with an in-game IPAM and network management layer. The focus is faster IP handling, better device visibility, DHCP/subnet workflows, and a foundation for more advanced network features.

The project is designed as **normal-mode first**: core features should be usable directly in-game without external tools or unnecessary complexity.

## Current Features

- IPAM overlay with dashboard, device, and IP views
- Device inventory via in-game reflection
- IPv4/subnet helpers and private subnet logic
- DHCP scope management (Global / VLAN / Switch)
- Routing, ping, and reachability helpers
- Cisco-like CLI / terminal concept for network devices
- Persistent device configuration
- Rack management with live slot status
- Naming templates with bulk rename
- Debug logging for mod and network diagnostics

## Planned Focus Areas

See [`ROADMAP.md`](./ROADMAP.md) for details. Main development tracks include:

- improved IP assignment UX
- DHCP scope management
- VLAN and management-plane foundations
- patch-port labeling
- shared-server / multi-tenant concepts
- redundancy and advanced networking features
- gamified IPAM objectives, health scores, and conflict reporting
- iPad-style UI border

## Installation

1. Install **MelonLoader** for **Data Center**.
2. Copy the release DLL into the mod folder:

   ```text
   Game/Mods/gregMod.IPAM.dll
   ```

3. Start the game.
4. Open the IPAM overlay in-game. Default hotkey: **F1**.

## Keyboard Shortcuts

| Key | Action |
|-----|--------|
| **F1** | Toggle IPAM overlay (takes mouse focus) |
| **Escape** | Close IPAM + open game pause menu |
| **P** | Close IPAM only (no pause menu) |
| **Ctrl+L** | Assign DHCP to all servers |
| **Mouse wheel** | Increment/decrement IP octet |
| **Ctrl+Click** | Multi-select rows |
| **Shift+Click** | Range-select rows |

## Dependencies

Runtime / mod setup requirements:

- **MelonLoader**
- **Il2CppInterop**
- **Harmony**
- Unity / game interop assemblies from the local Data Center installation

## Build from Source

Requirements:

- .NET 6 SDK
- local Data Center / MelonLoader installation
- available interop assemblies

Build:

```bash
git clone https://github.com/mleem97/gregMod.IPAM.git
cd gregMod.IPAM
dotnet build -c Release
```

Release output:

```text
bin/Release/net6.0/gregMod.IPAM.dll
```

## Project Structure

- **`Core/`** — MelonLoader entry point, mod info, global helpers
- **`Networking/`** — DHCP, subnets, reachability, device and customer logic
- **`Config/`** — device configuration, registry, and persistence
- **`Ipam/`** — IPAM overlay, windows, tables, lifecycle, and UI logic
- **`Patches/`** — Harmony patches
- **`Input/`** — input suppression while overlays are open
- **`Diagnostics/`** — debug and Melon logging
- **`docs/SOURCE_LAYOUT.md`** — detailed source layout

## Community & Support

Questions, feedback, testing, and modding coordination happen on the greg Discord:

- [discord.gg/greg](https://discord.gg/greg)

## Sponsors & Thanks

- **[@tobiasreichel](https://github.com/tobiasreichel)** — main sponsor

## Contributing

Contributions are welcome. Useful starting points:

- report bugs or regressions as issues
- provide reproducible test cases for network / IPAM flows
- discuss roadmap items
- keep pull requests small and easy to review

## License

This project is licensed under the **Apache License 2.0**. See [`LICENSE`](./LICENSE).

---

**gregFramework — powered by the community.**
