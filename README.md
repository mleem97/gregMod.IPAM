# gregMod.IPAM

> IP Address Management, DHCP workflows, and network tooling for **Data Center** — built for the **gregFramework** ecosystem.

[![Discord](https://img.shields.io/discord/1392073682133848075?style=for-the-badge&logo=discord&logoColor=white&label=Discord)](https://discord.gg/greg)
[![gregFramework](https://img.shields.io/badge/gregFramework-Website-blue?style=for-the-badge)](https://gregframework.eu)
[![License](https://img.shields.io/badge/License-Apache%202.0-green?style=for-the-badge)](./LICENSE)
[![Version](https://img.shields.io/badge/Version-0.7.6-orange?style=for-the-badge)](./ROADMAP.md)
[![GameVersion](https://img.shields.io/badge/Game%20Version-1.1.0-yellow?style=for-the-badge)]()
[![Unity](https://img.shields.io/badge/Unity-6000.4.12f1-black?style=for-the-badge&logo=unity&logoColor=white)]()

## Links

- **Website:** [gregframework.eu](https://gregframework.eu)
- **Discord / Support:** [discord.gg/greg](https://discord.gg/greg)
- **Repository:** [github.com/mleem97/gregMod.IPAM](https://github.com/mleem97/gregMod.IPAM)
- **Roadmap:** [`ROADMAP.md`](./ROADMAP.md)

## Overview

**gregMod.IPAM** extends **Data Center** with an in-game IPAM and network management layer. The focus is faster IP handling, better device visibility, DHCP/subnet workflows, and a foundation for more advanced network features.

The project is designed as **normal-mode first**: core features should be usable directly in-game without external tools or unnecessary complexity.

## Features

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

1. Install **MelonLoader** (v0.7.2+) for **Data Center**.
2. Copy the release DLL into the mod folder:

   ```text
   Game/Mods/gregMod.IPAM.dll
   ```

3. Start the game.
4. Open the IPAM overlay in-game. Default hotkey: **P**.

## Keyboard Shortcuts

| Key | Action |
|-----|--------|
| **P** | Open IPAM overlay (takes mouse focus) |
| **Escape** | Close IPAM + open game pause menu |
| **Ctrl+L** | Assign DHCP to all servers |
| **Mouse wheel** | Increment/decrement IP octet |
| **.** or **,** | Advance to next octet |
| **Backspace** | Delete last digit in active octet |
| **0–9** | Type digits into active octet |
| **Ctrl+Click** | Multi-select rows |
| **Shift+Click** | Range-select rows |
| **Tab** | Cycle through form fields |

## Dependencies

- **MelonLoader** (v0.7.2+)

### Build only

- **Il2CppInterop**
- **Harmony**
- Unity / game interop assemblies from the local Data Center installation

## Build from Source

Requirements:

- .NET 6 SDK
- local Data Center / MelonLoader installation

> **Note:** This mod was built on Linux using Proton-GE 10-34. The `.csproj` uses MSBuild variables (`$(MelonLoaderNetDir)`, `$(GameInteropDir)`) to locate game and MelonLoader DLLs. When building on a different system, either set these variables or adjust the `<HintPath>` entries to point to your local MelonLoader and game interop assemblies.

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

- **`src/Core/`** — MelonLoader entry point, mod info, global helpers
- **`src/Networking/`** — DHCP, subnets, reachability, device and customer logic
- **`src/Config/`** — device configuration, registry, and persistence
- **`src/Ipam/`** — IPAM overlay, windows, tables, lifecycle, and UI logic
- **`src/Patches/`** — Harmony patches
- **`src/Input/`** — input suppression while overlays are open
- **`src/Diagnostics/`** — debug and Melon logging
- **`docs/SOURCE_LAYOUT.md`** — detailed source layout

## Community & Support

Questions, feedback, testing, and modding coordination happen on the greg Discord:

- [discord.gg/greg](https://discord.gg/greg)

## Credits

| Role | Contributor |
|------|-------------|
| **Codebase** | [mleem97](https://github.com/mleem97) ([TeamGreg Modding](https://github.com/teamGregModding)) |
| **Major Features** | [mleem97](https://github.com/mleem97), [mochimus](https://github.com/mochimus) ([TeamGreg Modding](https://github.com/teamGregModding)) |

## Contributing

Contributions are welcome. Useful starting points:

- report bugs or regressions as issues
- provide reproducible test cases for network / IPAM flows
- discuss roadmap items
- keep pull requests small and easy to review

## License

This project is licensed under the **Apache License 2.0**. See [`LICENSE`](./LICENSE).

## 🚀 Join the gregFramework Team!

### macOS Support

A native macOS version of Data Center already exists. At the moment, however, there is no implementation path available for macOS support in this mod, and I do not have access to an Apple device for development or testing. I am actively looking for contributors who can help make macOS support possible. See “Join the gregFramework Team” below.

Building the ultimate modding framework for Data Center is a massive undertaking. gregFramework is currently maintained by a passionate core team of three, and we are looking for fellow creators to help us scale this mission!

**Your place in the team:** We won't throw you into the deep end. Depending on your individual strengths and skills, we will match you with the right areas of the project so you can contribute exactly where you have the most fun.

**🌍 Language Requirement:** A solid grasp of written English is required (without relying on machine translation). Being comfortable speaking English in voice chats is a huge plus, but we completely respect those who prefer to stick to text!

**We are looking for motivated volunteers to join our crew across several roles:**

- 💻 **Code Wizards** (C#, Rust, Lua, TS, GO) — Build and expand the core framework and mod packages
- 🎨 **Asset Creators** (3D Models, hardware assets) — Bring the framework to life visually
- 📚 **Technical Writers** — Craft wiki entries, maintain documentation, and write user guides
- 🎮 **Alpha Testers** — Hunt down bugs, stress-test the framework, and provide critical feedback
- ⚙️ **System Guardians** — Maintain our Linux servers, Docker containers, and infrastructure
- 🤝 **Community Managers** — Foster our Discord community, gather feedback, and keep the energy high

Interested in joining the project? Everyone is absolutely welcome! Send us an email at **apply@gregframework.eu**, shoot a quick DM, or drop a message on [Discord](https://discord.gg/greg).

---

**gregFramework — powered by the community.**
