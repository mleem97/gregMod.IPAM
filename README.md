# gregMod.IPAM — Network Management

> IP Address Management and subnet automation for your data center network.

**Repository:** [github.com/mleem97/gregMod.IPAM](https://github.com/mleem97/gregMod.IPAM)  
**Author:** TeamGreg Modding (mleem97 & mochimus) | **License:** Apache 2.0 | **Framework:** MelonLoader

---

## Overview

**gregMod.IPAM** is a gamified networking system and IPAM layer for **Data Center**. It expands network management depth while keeping the experience practical and fun.

## Features
- Auto-subnetting (/24, /22, /16 blocks)
- VLAN editor and assignment
- DeepFlow network status integration
- Customer-to-subnet mapping
- DHCP scope management
- Shared server / multi-tenant gameplay concepts

---

## Installation

1. Install **MelonLoader** (v0.6+)
2. Place `gregMod.IPAM.dll` into `Game/Mods/`
3. Start the game and press **F1**

## Dependencies

- None (standalone mod)

---

## Tech Stack

- **Game:** Data Center
- **Runtime:** MelonLoader (`0.7.2+` target)
- **Language:** C# / .NET 6
- **Interop:** Il2CppInterop
- **Patching:** Harmony

---

## Repository Structure

- **`Core/`** — MelonLoader entry (`Main.cs`, `MelonModInfo.cs`, …)
- **`Networking/`** — DHCP, subnets, device helpers
- **`Ipam/`** — IPAM overlay (`IPAMOverlay.cs`), `LicenseManager`
- **`Config/`** — Device configuration persistence
- **`Patches/`** — Harmony patches for input/UI
- **`Diagnostics/`** — Debug logging utilities
- **`ROADMAP.md`** — phased implementation roadmap
- **`docs/SOURCE_LAYOUT.md`** — folder-by-folder map of all C# sources

---

## Building from Source

```bash
git clone https://github.com/mleem97/gregMod.IPAM.git
cd gregMod.IPAM
dotnet build -c Release
```

Release DLL: `bin/Release/net6.0/gregMod.IPAM.dll` (also packaged under `dist/`).

---

## Contributors & Thanks

### Code & Development
- **mleem97** — Lead Developer
- **mochimus** — Co-Developer

### Discord Community
**Thanks to:**
- **Noootry**
- **TheSlickers**
- **Jarvis**
- **Kirei**
- **TeamWaseku** (ModernSamurai, GamerFrankstar, Ultra, Zyn)

### Testing
- **Joniii11** ([GitHub](https://github.com/Joniii11))
- **Baker**, **Sharpy1o1**, **MachineFreak**

### Sponsors
- **@tobiasreichel** — Haupt-Sponsor
- **SQ8** — Infrastructure Hosting

---
*gregMod.IPAM — Powered by the Community!*
