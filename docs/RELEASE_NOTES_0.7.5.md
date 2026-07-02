# Release 0.7.5

Changes since **0.7.0** with a focused fix pass for overlay modal input.

## Overlay / modal input

- Fixed inline modal input routing so controls inside popup panels receive IMGUI mouse events again.
- Backdrop clicks are now consumed only outside the modal panel, instead of swallowing clicks meant for popup content.
- The inventory body pauses while inline modals are open, preventing click-through into the underlying window.
- Server edit popup controls now use a dedicated IMGUI control-ID range to avoid collisions with main navigation controls.

## Fixed in practice

- Customer selection inside the server edit popup is clickable again.
- `Contract+DHCP`, `IPAM prefix`, and `Naming` mode buttons respond correctly.
- `Apply`, `Cancel`, and `Close` work again inside the popup.
- DHCP and IPv4 actions in the server edit popup no longer trigger controls behind the modal.

## Install

Copy `bin/Release/net6.0/gregMod.IPAM.dll` into your game **Mods** folder.
