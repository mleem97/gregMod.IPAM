# gregMod.IPAM Roadmap (Data Center Mod)

> **Status Legend:** `[x]` done | `[~]` partially done | `[ ]` not started

## Scope & Vision

Build a **gamified IPAM and network operations layer** for Data Center with:

- improved in-game IP assignment UX
- DHCP scopes
- shared multi-tenant server model
- optional advanced mode concepts

---

## Product Goals

1. Reduce manual network configuration overhead.
2. Increase realism (without overwhelming normal-mode players).
3. Improve troubleshooting depth and progression gameplay.
4. Keep features modular and unlockable via in-game progression.

---

## Guiding Principles

- **Normal Mode first**: all core features must work without external tools.
- **Advanced features as opt-in**: no forced complexity for casual users.
- **Fail-safe behavior**: invalid config should degrade gracefully with clear UI hints.
- **Game-first UX**: fast actions, minimal typing, meaningful feedback loops.

---

## Feature Tracks (Epics)

### Epic A — IP Assignment UX

- [~] Mouse-wheel increment/decrement on last IPv4 octet in server IP input. *(implemented as +/- buttons and keyboard input in `IPAMOverlay.WindowUi.cs:3418`)*
- [ ] Smart paste flow (`192.168.0.` then scroll/append).
- [x] `Assign Next Free` action. *(`DHCPManager.GetNextFreeIpForServer()` in `Networking/DHCPManager.cs:658`)*
- [x] Collision prevention and subnet validation feedback. *(`DHCPManager.IsIpUsedByAnotherServer()` in `Networking/DHCPManager.cs:472`, `RouteMath.Ipv4CidrRangesOverlap()` in `Networking/RouteMath.cs:357`)*

### Epic B — DHCP Scope Management

- [ ] DHCP scopes configurable
- [~] Clear precedence model. *(try-order in `GameSubnetHelper.BuildDhcpCidrTryOrder()`, no explicit VLAN/Switch/Global hierarchy)*
- [ ] Reservations and exclusion ranges. *(only gateway .1 reservation in `RouteMath.cs:87`)*
- [~] Scope exhaustion warnings. *(null return + log in `DHCPManager.cs:721`, no proactive "scope X% full" warnings)*

### Epic C — VLAN & Management Plane

- [~] Port-based VLAN assignment on switches. *(config model exists: `SwitchPortConfig` in `Config/DeviceConfigModels.cs:38`, `IpamVlanEntry` in `Ipam/IpamDataStore.cs:532`, no in-game port assignment)*
- [ ] Management VLAN/network concept.
- [ ] Management ports for out-of-band style configuration access.
- [ ] Configuration apps on management PC.

### Epic D — Patch-Port Labeling

- [~] Label per patch panel port. *(`PatchLabel` field in `RackDataStore.cs:363`, no separate `PatchPortLabel` model)*
- [x] Bulk naming templates. *(`NamingTemplateEngine` in `Ipam/NamingTemplateEngine.cs`, `NamingConventionStore` in `Ipam/NamingConventionStore.cs`)*
- [~] Labels visible in relevant UIs (rack/patch/network map where applicable). *(rack UI shows labels in `IPAMOverlay.RacksTab.cs:826`)*

### Epic E — Shared Server Model

- [ ] Server modes: `Dedicated` vs `Shared`.
- [ ] Multi-tenant mapping of customer workloads.
- [ ] Capacity/quota model for shared services.
- [ ] Basic isolation rules to avoid impossible mappings.

### Epic F — Redundancy & Advanced Networking

- [ ] Better redundancy gameplay:
  - [ ] device redundancy focus
  - [ ] path redundancy/LACP support
- [ ] Advanced concept target:
  - [ ] vPC/MLAG-like behavior for server multi-switch bonding

### Epic G — Gamified IPAM Layer

- [~] Subnet utilization, conflict feed, health score. *(utilization in `IPAMOverlay.IpamTabs.cs:540`, free-space in `IpamFreeSpace.cs`, overlap detection in `IpamDataStore.TryAddPrefix:218` — no health score)*
- [ ] Objective-style tasks for clean network operation.
- [ ] Reward/penalty hooks tied to network quality.

### Epic H — Optional Advanced Lab Bridge (Concept)

- [ ] Optional hard mode concept for future external lab alignment (GNS3/EVE-NG style bridge concept).
- [ ] Keep out of critical path for current releases.

### Epic I — Game Beta Features & IPAM Integration

- [ ] DMZ network support — DMZ subnet handling, firewall rules, port forwarding.
- [ ] Internet/WAN connectivity — external network simulation, ISP handoff, public IP assignment.
- [ ] Router feature integration — NAT, routing tables, static/dynamic routes, gateway management.
- [ ] Firewall integration — ACL rules, zone-based policies, DMZ-to-LAN filtering.
- [ ] IPAM icon in Computer/Shop UI — embed IPAM launcher in the in-game computer/shop interface.
- [ ] Deep game API hooks — hook into new game networking APIs as they ship in beta updates.
- [ ] Backward compatibility — graceful degradation when beta features are not present.

### Epic J — iPad-Style UI Border

- [ ] iPad frame border — rounded corners, metallic/dark bezel, subtle shadow/glow.
- [ ] Status bar — real-time clock, battery icon, WiFi signal, device name.
- [ ] Embedded image assets — icons/graphics compiled into DLL as embedded resources.
- [ ] Home indicator bar — bottom swipe indicator (visual only).
- [ ] Realistic screen bezel — inner shadow, slight screen curvature effect.
- [ ] Optional iPad wallpaper background behind IPAM content.

---

## Release Plan

## R1 — Foundation & UX (High Impact, Low Risk) `[IN PROGRESS]` → **v0.4.0**

**Target:** establish immediate quality-of-life and stable technical foundation.

- [~] Deliver Epic A (core IP UX) *(scroll octet = +/- buttons, next-free = done, collision = done)*
- [~] Deliver first slice of Epic B (global + switch scope) *(precedence model partial, no configurable scopes)*
- [x] Data model prep for VLAN scope extension *(IpamVlanEntry exists)*
- [x] Basic IPAM overlay improvements (warnings/messages) *(IPAMOverlay with prefix tree, VLANs, racks)*

**Exit Criteria**

- [~] IP entry friction reduced (scroll + next-free works reliably) *(next-free works, scroll as +/- buttons)*
- [x] No duplicate IP assignment in supported flows
- [x] Existing DHCP behavior remains backward compatible

---

## R2 — VLAN-Aware DHCP & Labeling `[NOT STARTED]` → **v0.5.0**

**Target:** introduce segmented addressing and topology clarity.

- [ ] Deliver full Epic B (including VLAN scopes)
- [~] Deliver Epic C (port-based VLAN basics + management network base) *(config model exists, no in-game assignment)*
- [~] Deliver Epic D (patch-port labels v1) *(labels in rack, bulk naming done)*

**Exit Criteria**

- [ ] DHCP assignment respects scope hierarchy
- [ ] VLAN segmentation affects address assignment as expected
- [~] Labels persist and are retrievable in UI workflows *(rack labels yes, no separate PatchPortLabel model)*

---

## R3 — Shared Infrastructure Gameplay `[DONE]` → **v0.6.0**

**Target:** remove strict 1-server-per-customer limitation.

- [x] Deliver Epic E (shared server model v1) *(ServerTenancyEntry with Dedicated/Shared mode)*
- [x] Add compatibility checks with DHCP/VLAN rules *(tenant allocation validation)*
- [x] Introduce basic scoring hooks (Epic G starter) *(NetworkHealthScore with deductions)*

**Exit Criteria**

- [x] Shared server assignment is stable and understandable *(Dedicated/Shared toggle in server edit)*
- [x] No invalid customer routing from shared mappings *(max tenants, duplicate check)*
- [x] Player can inspect tenant allocation in UI *(tenant list with remove button)*

---

## R4 — Advanced Redundancy & IPAM Gamification `[NOT STARTED]` → **v0.7.0**

**Target:** deepen high-level network gameplay.

- [ ] Deliver Epic F (vPC/MLAG-inspired gameplay mechanics v1)
- [ ] Expand Epic G (health scoring, mission hooks, warning categories)

**Exit Criteria**

- [ ] Redundancy states are visible and actionable
- [ ] Failure scenarios produce deterministic, teachable outcomes

---

## R5 — Optional Advanced Mode Concepts `[NOT STARTED]` → **v0.8.0**

**Target:** optional hard-mode direction without blocking mainline development.

- [ ] Epic H as prototype/research track
- [ ] Connector architecture draft, no hard dependency for main build

**Exit Criteria**

- [ ] Main mod unaffected when advanced mode is disabled
- [ ] Clear feasibility report for future implementation

---

## R6 — Game Beta Features & IPAM Integration `[NOT STARTED]` → **v0.9.0**

**Target:** integrate new game beta features and embed IPAM into the game's computer/shop UI.

- [ ] DMZ network support — DMZ subnet handling, firewall rules, port forwarding concepts
- [ ] Internet/WAN connectivity — external network simulation, ISP handoff, public IP assignment
- [ ] Router feature integration — NAT, routing tables, static/dynamic routes, gateway management
- [ ] Firewall integration — ACL rules, zone-based policies, DMZ-to-LAN filtering
- [ ] IPAM icon in Computer/Shop UI — embed IPAM launcher as clickable icon in the in-game computer/shop interface
- [ ] Deep game API hooks — hook into new game networking APIs as they ship in beta updates
- [ ] Backward compatibility — graceful degradation when beta features are not present in game build

**Exit Criteria**

- [ ] DMZ/Internet/Router/Firewall features functional when game exposes the APIs
- [ ] IPAM accessible from computer/shop without F1 hotkey
- [ ] No crashes when beta APIs are missing (fail-safe detection)
- [ ] User feedback on integration quality

---

## R7 — iPad-Style UI Border `[NOT STARTED]` → **v0.10.0**

**Target:** wrap the IPAM overlay in a realistic iPad-style frame with status bar, embedded assets, and polished visuals.

- [ ] iPad frame border — rounded corners, metallic/dark bezel, subtle shadow/glow
- [ ] Status bar — real-time clock, battery icon, WiFi signal, device name (like iOS status bar)
- [ ] Embedded image assets — icons/graphics compiled into DLL as embedded resources
- [ ] Home indicator bar — bottom swipe indicator (visual only)
- [ ] Realistic screen bezel — inner shadow, slight screen curvature effect
- [ ] Optional: iPad wallpaper background behind IPAM content
- [ ] Performance — minimal overhead, assets loaded once at startup

**Exit Criteria**

- [ ] IPAM visually resembles an iPad app when open
- [ ] Status bar shows real-time data (clock updates each frame)
- [ ] No external files required — all assets in DLL
- [ ] Smooth 60fps rendering, no stutter from asset loading

---

## Technical Architecture Milestones

1. **Configuration domain model**
   - VLANs, scopes, reservations, labels, server tenancy.
2. **Validation layer**
   - subnet overlap, gateway mismatch, duplicate leases, tenant capacity.
3. **UI interaction layer**
   - management PC apps + overlays + quick actions.
4. **Persistence layer**
   - save/load all new entities reliably.
5. **Simulation integration**
   - connect game network state to DHCP/IPAM logic.

---

## Data Model Additions (Planned)

- [~] `VlanDefinition`: id, name, cidr/mask, gateway *(partially: `IpamVlanEntry` in `Ipam/IpamDataStore.cs:532` has id, vlanId, name — cidr/mask/gateway missing)*
- [ ] `DhcpScope`: level (VLAN/Switch/Global), rangeStart/rangeEnd, exclusions, reservations
- [ ] `ManagementProfile`: mgmt VLAN, managed ports, access flags
- [~] `PatchPortLabel`: panelId, portId, label, metadata *(partially: `PatchLabel` field in `RackDataStore.cs:363`)*
- [ ] `ServerTenancy`: serverId, mode, tenants[], quotas
- [ ] `RedundancyGroup`: peer switches, channel rules, health state

---

## Risk Register

1. **Complexity creep** in UI and rules.
   - Mitigation: phased feature flags, progressive unlocks.
2. **State desync** between network simulation and DHCP/IPAM cache.
   - Mitigation: authoritative refresh points and reconciliation jobs.
3. **Save compatibility issues** after schema expansion.
   - Mitigation: versioned save migrations.
4. **Performance overhead** with large deployments.
   - Mitigation: interval-based updates and cached lookups.

---

## Test Strategy

- Unit-level logic tests (scope precedence, next-free selection, conflict detection).
- Integration tests (server creation, IP assignment, scope changes, save/load).
- Scenario tests:
  - exhausted scope
  - duplicate reservation
  - VLAN mismatch
  - switch failure in redundant topology
- Regression checks for existing DHCP assignment flow.

---

## KPI / Success Metrics

- Reduced manual IP edit actions per deployment.
- Reduced duplicate-IP incidents.
- Increased successful first-try network deployments.
- Positive user feedback on clarity (labels, overlays, scope visibility).

---

## Implementation Order (Now / Next / Later)

## Now `[MOSTLY DONE]`

1. [~] IP UX improvements (`scroll octet` as +/- buttons, `next free` done).
2. [~] DHCP precedence engine (global + switch). *(try-order exists, explicit scope hierarchy done)*
3. [x] Validation messages in overlay.
4. [x] DHCP Scopes (Global/VLAN/Switch).

## Next

1. [~] VLAN scope support. *(data model done, scope binding done)*
2. [~] Patch-port labeling. *(rack labels + bulk naming done)*
3. [ ] Management network baseline.
4. [ ] Shared tenancy model.

## Later

1. [ ] vPC/MLAG-inspired mechanics.
2. [ ] Advanced mode concept track.
3. [ ] Game beta features (DMZ, Internet, Router, Firewall).
4. [ ] IPAM integration in Computer/Shop UI.
5. [ ] iPad-style UI border with embedded assets.

---

## First Sprint Proposal (Execution-Ready) `[COMPLETED]`

### Sprint Goal

Ship **IP assignment QoL + deterministic next-free allocation** with safe validation.

### Sprint Backlog

1. [~] Implement last-octet wheel adjustment handler. *(implemented as +/- buttons instead of wheel)*
2. [x] Add next-free IP allocator function with scope awareness.
3. [x] Add duplicate/subnet validation and UI feedback.
4. [x] Add config toggles for new UX behavior.
5. [ ] Add regression tests for existing assignment paths.

### Definition of Done

- [x] Feature works on all supported server IP input flows.
- [x] No regressions in existing DHCP auto-assign behavior.
- [x] Clear in-game feedback for invalid or exhausted conditions.
- [ ] Documentation updated for controls and expected behavior.

---

## Notes for Planning Sessions

- Keep each sprint focused on one major gameplay benefit.
- Prefer vertical slices (data + logic + UI + persistence) over isolated backend work.
- Treat advanced-mode topics as separate track unless core milestones are green.
