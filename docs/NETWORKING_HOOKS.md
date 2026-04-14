# Networking hooks (Data Center + DHCPSwitches)

## Local decompile (reference sources)

Decompiled C# for exploration (not referenced by the mod build) lives next to this repo:

- `D:\Data-Center Mods\Game-hooks\Assembly-CSharp\` — main game logic (`InputController`, `NetworkSwitch`, `CustomerBase`, …).
- `D:\Data-Center Mods\Game-hooks\Assembly-CSharp-firstpass\` — plugins compiled first (e.g. LeanTween); usually not needed for DHCP/IPAM hooks.

The mod still links **`Assembly-CSharp.dll`** from MelonLoader’s interop folder via `Directory.Build.props` / `GameInteropDir`.

## Reverse-engineering summary

- **Assembly:** game scripts live in `Assembly-CSharp.dll` (MelonLoader `Il2CppAssemblies` after codegen).
- **`NetworkSwitch`:** used by the mod via Il2Cpp stubs; **model / SKU strings are not compiled into this repo**. At runtime, the mod treats devices as switches.
- **`CustomerBase.AddAppPerformance`:** already patched by [`DHCPManager.FlowPausePatch`](../Networking/DHCPManager.cs) (flow pause). This is treated as the **primary choke point** for “application traffic / performance” ticks toward customers.
- **Server context:** `AddAppPerformance` is currently patched with a **parameterless** `Prefix`; game signature appears to take **no parameters** (or only implicit `this`). Therefore, the patch uses the `CustomerBase.customerID` of `__instance`.

## Patch strategy

| Patch | Target | Behavior |
|-------|--------|----------|
| `FlowPausePatch` | `CustomerBase.AddAppPerformance` | Returns `false` when flow paused. |

## Failure modes

- If the game adds overloads of `AddAppPerformance`, `AccessTools.Method(customerType, "AddAppPerformance")` may become ambiguous — switch to `DeclaredMethod` with explicit parameter types.
- If performance is driven by additional code paths (e.g. per-server methods), add Harmony targets alongside [`DHCPManager.FlowPausePatch`](../Networking/DHCPManager.cs) after identifying symbols in ILSpy.

## Input: IPAM hotkey vs game Inventory

- In **`InputController`** (generated / decompiled), **`UIActions.Inventory`** is an `InputAction` the game uses for inventory UI.
- IPAM toggles on **`Keyboard.current.f1Key`** (`DHCPSwitchesBehaviour.Update` in [`Main.cs`](../Core/Main.cs)). **`PlayerInput` is only suspended while the device CLI is open** (`GameInputSuppression`); IPAM stays overlay-only so gameplay / pause menus are not forced closed by the old **I**-key + inventory conflict.

## Future work

- Physical topology (switch port ↔ server) from game types, if exposed, to tighten VLAN binding and DHCP scope selection.
