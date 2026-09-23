# Changelog — gregMod.IPAM

Format: [Keep a Changelog](https://keepachangelog.com/de/1.0.0/). Version: siehe [`VERSION`](VERSION).

## [Unreleased]

### Added

- `ExcludeGateway`-Pref (Default an) + Toggle in DHCP Scopes: DHCP vergibt nie .1 auf /24 oder kürzer, auch in privaten Netzen.
- Konfigurierbarer Toggle-Hotkey (`ToggleKey`-Pref, Default P), Tasten-HUD-Eintrag und Oeffner fuers F1-Hub (nur mit gregCore).
- Einheitliches Open-Source-Layout (README, Docs, Badges) nach gregCore-Vorbild.

### Fixed

- Racks-Seite skaliert mit UI-Font-Scale (Front-View, Tabellen-Layout, gecachte Styles werden bei Skalenwechsel neu aufgebaut).

## [0.1.0] — 2026-09-22

- Initialer standardisierter Stand.
