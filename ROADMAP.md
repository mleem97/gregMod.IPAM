# gregMod.IPAM Roadmap (Data Center Mod)

> **Status Legend:** `[x]` done | `[~]` partially done | `[ ]` not started | `[!]` known issue

---

## Feature Tracks (Epics)

### Epic A — IP Assignment UX

- [x] Mouse-wheel increment/decrement on last IPv4 octet. *(scroll handler in `DrawOctetEditor`, `IPAMOverlay.WindowUi.cs:3500`)*
- [ ] Smart paste flow (`192.168.0.` then scroll/append).
- [x] `Assign Next Free` action. *(`DHCPManager.GetNextFreeIpForServer()` in `Networking/DHCPManager.cs:658`)*
- [x] Collision prevention and subnet validation feedback. *(`DHCPManager.IsIpUsedByAnotherServer()` in `Networking/DHCPManager.cs:472`)*
- [x] Auto-detect customer prefix on assignment. *(shows "Contract subnet: x.x.x.x/nn" in octet editor)*

### Epic B — DHCP Scope Management

- [x] DHCP scopes configurable. *(`DhcpScopeEntry` in `Ipam/IpamDataStore.cs`, CRUD UI in `IPAMOverlay.IpamTabs.cs`)*
- [x] Clear precedence model. *(Global → VLAN → Switch priority in `IpamDataStore.FindScopeForServer()`)*
- [ ] Reservations and exclusion ranges.
- [x] Scope exhaustion warnings. *(color-coded utilization bars: green/yellow/red in `IPAMOverlay.IpamPrefixTree.cs`)*

### Epic C — VLAN & Management Plane

- [~] Port-based VLAN assignment on switches. *(config model exists: `SwitchPortConfig`, `IpamVlanEntry`, no in-game port assignment)*
- [ ] Management VLAN/network concept.
- [ ] Management ports for out-of-band style configuration access.
- [ ] Configuration apps on management PC.

### Epic D — Patch-Port Labeling

- [~] Label per patch panel port. *(`PatchLabel` field in `RackDataStore.cs:363`)*
- [x] Bulk naming templates. *(`NamingTemplateEngine` in `Ipam/NamingTemplateEngine.cs`)*
- [~] Labels visible in relevant UIs. *(rack UI shows labels in `IPAMOverlay.RacksTab.cs:826`)*

### Epic E — Shared Server Model

- [x] Server modes: `Dedicated` vs `Shared`. *(`ServerTenancyEntry` in `Ipam/IpamDataStore.cs`, toggle in server edit)*
- [x] Multi-tenant mapping of customer workloads. *(`TenantAllocation` with customer name, IP count)*
- [ ] Capacity/quota model for shared services.
- [ ] Basic isolation rules to avoid impossible mappings.

### Epic F — Redundancy & Advanced Networking

- [ ] Better redundancy gameplay:
  - [ ] device redundancy focus
  - [ ] path redundancy/LACP support
- [ ] Advanced concept target:
  - [ ] vPC/MLAG-like behavior for server multi-switch bonding

### Epic G — Gamified IPAM Layer

- [x] Subnet utilization, conflict feed, health score. *(`NetworkHealthScore` in `Networking/NetworkHealthScore.cs`, utilization bars in prefix tree)*
- [ ] Objective-style tasks for clean network operation.
- [ ] Reward/penalty hooks tied to network quality.

### Epic H — Optional Advanced Lab Bridge (Concept)

- [ ] Optional hard mode concept for future external lab alignment.
- [ ] Keep out of critical path for current releases.

### Epic I — Game Beta Features & IPAM Integration

- [ ] DMZ network support.
- [ ] Internet/WAN connectivity.
- [ ] Router feature integration.
- [ ] Firewall integration.
- [ ] IPAM icon in Computer/Shop UI.
- [ ] Deep game API hooks.
- [ ] Backward compatibility.

### Epic J — iPad-Style UI Border

- [ ] iPad frame border with status bar.
- [ ] Embedded image assets in DLL.

---

## Release Plan

### R1 — Foundation & UX `[DONE]` → **v0.4.0**

- [x] Deliver Epic A (core IP UX) *(scroll octet, next-free, collision, auto-prefix)*
- [x] Deliver first slice of Epic B *(DHCP scopes Global/VLAN/Switch)*
- [x] Data model prep for VLAN scope extension *(IpamVlanEntry exists)*
- [x] Basic IPAM overlay improvements *(prefix tree, VLANs, racks, dashboard)*

**Exit Criteria**

- [x] IP entry friction reduced (scroll + next-free works reliably)
- [x] No duplicate IP assignment in supported flows
- [x] Existing DHCP behavior remains backward compatible

---

### R2 — VLAN-Aware DHCP & Labeling `[DONE]` → **v0.5.0**

- [x] Deliver full Epic B *(DHCP scopes with Global/VLAN/Switch hierarchy)*
- [~] Deliver Epic C *(config model exists, no in-game port assignment)*
- [x] Deliver Epic D *(rack labels + bulk naming templates)*

**Exit Criteria**

- [x] DHCP assignment respects scope hierarchy *(FindScopeForServer with priority)*
- [~] VLAN segmentation affects address assignment *(data model done, scope binding done)*
- [x] Labels persist and are retrievable in UI workflows

---

### R3 — Shared Infrastructure Gameplay `[DONE]` → **v0.6.0**

- [x] Deliver Epic E *(ServerTenancyEntry with Dedicated/Shared mode)*
- [x] Add compatibility checks with DHCP/VLAN rules
- [x] Introduce basic scoring hooks *(NetworkHealthScore)*

**Exit Criteria**

- [x] Shared server assignment is stable and understandable
- [x] No invalid customer routing from shared mappings
- [x] Player can inspect tenant allocation in UI

---

### R4 — Advanced Redundancy & IPAM Gamification `[NOT STARTED]` → **v0.7.0**

- [ ] Deliver Epic F (vPC/MLAG-inspired gameplay mechanics v1)
- [ ] Expand Epic G (health scoring, mission hooks, warning categories)

---

### R5 — Optional Advanced Mode Concepts `[NOT STARTED]` → **v0.8.0**

- [ ] Epic H as prototype/research track
- [ ] Connector architecture draft

---

### R6 — Game Beta Features & IPAM Integration `[NOT STARTED]` → **v0.9.0**

- [ ] DMZ/Internet/Router/Firewall support
- [ ] IPAM icon in Computer/Shop UI
- [ ] Deep game API hooks

---

### R7 — iPad-Style UI Border `[NOT STARTED]` → **v0.10.0**

- [ ] iPad frame with status bar
- [ ] Embedded assets in DLL

---

## Known Issues

- [!] **Lock Player Camera** — mouse delta stripping via `Vector2Control.ReadValue` patch may not block camera rotation on all Unity/IL2CPP builds. Needs verification on Unity 6000.4.12f1.

---

## Data Model Additions

- [x] `VlanDefinition` *(IpamVlanEntry: id, vlanId, name)*
- [x] `DhcpScope` *(DhcpScopeEntry: level, cidr, priority, vlanId, switchKey)*
- [ ] `ManagementProfile`: mgmt VLAN, managed ports, access flags
- [~] `PatchPortLabel` *(PatchLabel field in RackMountRecord)*
- [x] `ServerTenancy` *(ServerTenancyEntry: mode, maxTenants, TenantAllocation[])*
- [ ] `RedundancyGroup`: peer switches, channel rules, health state

---

## Implementation Order

### Now

1. [x] IP UX improvements (scroll octet, next free, auto-prefix)
2. [x] DHCP Scopes (Global/VLAN/Switch)
3. [x] Validation messages in overlay
4. [x] Shared tenancy model

### Next

1. [ ] Lock Player Camera fix (verify on Unity 6000.4.12f1)
2. [ ] Management network baseline
3. [ ] Reservations and exclusion ranges

### Later

1. [ ] vPC/MLAG-inspired mechanics
2. [ ] Advanced mode concept track
3. [ ] Game beta features (DMZ, Internet, Router, Firewall)
4. [ ] IPAM integration in Computer/Shop UI
5. [ ] iPad-style UI border with embedded assets
