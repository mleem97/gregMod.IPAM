# DHCP-IPAM — Network Management

> IP Address Management and subnet automation for your data center network.

**Repository:** [github.com/Mochimus/DHCP-IPAM](https://github.com/Mochimus/DHCP-IPAM)  
**Author:** Mochimus | **License:** MIT | **Framework:** [gregCore](https://git.datacentermods.com/teamGreg/gregCore)

---

## Overview

**DHCP Switches & IPAM** is a gamified networking system and IPAM layer for **Data Center**. It expands network management depth while keeping the experience practical and fun.

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
2. Place `DHCP and IPAM.dll` into `Game/Mods/`
3. Start the game and press **F6**

## Dependencies

- `gregCore.dll`

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
- **`ROADMAP.md`** — phased implementation roadmap
- **`docs/SOURCE_LAYOUT.md`** — folder-by-folder map of all C# sources

---

## Building from Source

```bash
git clone https://github.com/Mochimus/DHCP-IPAM.git
cd DHCP-IPAM
dotnet build -c Release
```

Release DLL: `bin/Release/net6.0/DHCP and IPAM.dll` (also packaged under `dist/`).

---

## Contributors & Thanks

### Discord Community
**Thanks to:**
- **Noootry**
- **TheSlickers**
- **Jarvis**
- **Kirei**
- **TeamWaseku** (ModernSamurai, GamerFrankstar, Ultra, Zyn)

### Code & Testing
- **Joniii11** ([GitHub](https://github.com/Joniii11)) & **mochimus**
- **Baker**, **Sharpy1o1**, **MachineFreak**

### Sponsors
- **@tobiasreichel** - Haupt-Sponsor
- **SQ8** - Infrastructure Hosting

---
*gregFramework - Powered by the Community!*
