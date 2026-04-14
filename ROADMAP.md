# DHCPSwitches Roadmap (Data Center Mod)

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

- Mouse-wheel increment/decrement on last IPv4 octet in server IP input.
- Smart paste flow (`192.168.0.` then scroll/append).
- `Assign Next Free` action.
- Collision prevention and subnet validation feedback.

### Epic B — DHCP Scope Management

- DHCP scopes configurable
- Clear precedence model.
- Reservations and exclusion ranges.
- Scope exhaustion warnings.

### Epic C — VLAN & Management Plane

- Port-based VLAN assignment on switches.
- Management VLAN/network concept.
- Management ports for out-of-band style configuration access.
- Configuration apps on management PC.

### Epic D — Patch-Port Labeling

- Label per patch panel port.
- Bulk naming templates.
- Labels visible in relevant UIs (rack/patch/network map where applicable).

### Epic E — Shared Server Model

- Server modes: `Dedicated` vs `Shared`.
- Multi-tenant mapping of customer workloads.
- Capacity/quota model for shared services.
- Basic isolation rules to avoid impossible mappings.

### Epic F — Redundancy & Advanced Networking

- Better redundancy gameplay:
  - device redundancy focus
  - path redundancy/LACP support
- Advanced concept target:
  - vPC/MLAG-like behavior for server multi-switch bonding

### Epic G — Gamified IPAM Layer

- Subnet utilization, conflict feed, health score.
- Objective-style tasks for clean network operation.
- Reward/penalty hooks tied to network quality.

### Epic H — Optional Advanced Lab Bridge (Concept)

- Optional hard mode concept for future external lab alignment (GNS3/EVE-NG style bridge concept).
- Keep out of critical path for current releases.

---

## Release Plan

## R1 — Foundation & UX (High Impact, Low Risk)

**Target:** establish immediate quality-of-life and stable technical foundation.

- Deliver Epic A (core IP UX)
- Deliver first slice of Epic B (global + switch scope)
- Data model prep for VLAN scope extension
- Basic IPAM overlay improvements (warnings/messages)

**Exit Criteria**

- IP entry friction reduced (scroll + next-free works reliably)
- No duplicate IP assignment in supported flows
- Existing DHCP behavior remains backward compatible

---

## R2 — VLAN-Aware DHCP & Labeling

**Target:** introduce segmented addressing and topology clarity.

- Deliver full Epic B (including VLAN scopes)
- Deliver Epic C (port-based VLAN basics + management network base)
- Deliver Epic D (patch-port labels v1)

**Exit Criteria**

- DHCP assignment respects scope hierarchy
- VLAN segmentation affects address assignment as expected
- Labels persist and are retrievable in UI workflows

---

## R3 — Shared Infrastructure Gameplay

**Target:** remove strict 1-server-per-customer limitation.

- Deliver Epic E (shared server model v1)
- Add compatibility checks with DHCP/VLAN rules
- Introduce basic scoring hooks (Epic G starter)

**Exit Criteria**

- Shared server assignment is stable and understandable
- No invalid customer routing from shared mappings
- Player can inspect tenant allocation in UI

---

## R4 — Advanced Redundancy & IPAM Gamification

**Target:** deepen high-level network gameplay.

- Deliver Epic F (vPC/MLAG-inspired gameplay mechanics v1)
- Expand Epic G (health scoring, mission hooks, warning categories)

**Exit Criteria**

- Redundancy states are visible and actionable
- Failure scenarios produce deterministic, teachable outcomes

---

## R5 — Optional Advanced Mode Concepts

**Target:** optional hard-mode direction without blocking mainline development.

- Epic H as prototype/research track
- Connector architecture draft, no hard dependency for main build

**Exit Criteria**

- Main mod unaffected when advanced mode is disabled
- Clear feasibility report for future implementation

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

- `VlanDefinition`: id, name, cidr/mask, gateway
- `DhcpScope`: level (VLAN/Switch/Global), rangeStart/rangeEnd, exclusions, reservations
- `ManagementProfile`: mgmt VLAN, managed ports, access flags
- `PatchPortLabel`: panelId, portId, label, metadata
- `ServerTenancy`: serverId, mode, tenants[], quotas
- `RedundancyGroup`: peer switches, channel rules, health state

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

## Now

1. IP UX improvements (`scroll octet`, `next free`).
2. DHCP precedence engine (global + switch).
3. Validation messages in overlay.

## Next

1. VLAN scope support.
2. Patch-port labeling.
3. Management network baseline.

## Later

1. Shared tenancy model.
2. vPC/MLAG-inspired mechanics.
3. Advanced mode concept track.

---

## First Sprint Proposal (Execution-Ready)

### Sprint Goal

Ship **IP assignment QoL + deterministic next-free allocation** with safe validation.

### Sprint Backlog

1. Implement last-octet wheel adjustment handler.
2. Add next-free IP allocator function with scope awareness.
3. Add duplicate/subnet validation and UI feedback.
4. Add config toggles for new UX behavior.
5. Add regression tests for existing assignment paths.

### Definition of Done

- Feature works on all supported server IP input flows.
- No regressions in existing DHCP auto-assign behavior.
- Clear in-game feedback for invalid or exhausted conditions.
- Documentation updated for controls and expected behavior.

---

## Notes for Planning Sessions

- Keep each sprint focused on one major gameplay benefit.
- Prefer vertical slices (data + logic + UI + persistence) over isolated backend work.
- Treat advanced-mode topics as separate track unless core milestones are green.
