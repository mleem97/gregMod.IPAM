# Changelog — gregMod.IPAM

Format: [Keep a Changelog](https://keepachangelog.com/de/1.0.0/). Version: siehe [`VERSION`](VERSION).

## [Unreleased]

### Added

- Router-Verwaltung (Devices → Routers): Subnetze/Routen ansehen, hinzufügen, entfernen, Reapply, Same-ASN-Sync.
- Firewall-Verwaltung (Devices → Firewall): Regeln ansehen, hinzufügen, entfernen, Cluster-Sync/Broadcast, Traffic-Test.
- Harte Vergabe-Sperren: Netzwerk- (.0) und Broadcast-Adresse (.255) werden nie vergeben (alle Pfade) + `ExcludeIps`-Pref für eigene Ausschlüsse.
- React-WebUI im Hintergrund (127.0.0.1:8177, Prefs `WebEnabled`/`WebPort`): Dashboard, Server-Steuerung (DHCP/IP/Rename/Power), Scopes-CRUD, Logs; JSON-API + Main-Thread-Queue; Frontend in `web/` (Vite+React+TS), Deploy via `scripts/deploy-web.sh`.
- Server-Auswahl: Ctrl+A (Cmd+A) wählt alle angezeigten Server (Devices-, IP- und Kunden-Liste, aktuelle Seite); Shift-Klick-Bereich startet jetzt am zuletzt per Ctrl gewählten Server.
- Naming: „Reset saved seq counter“-Button (persistierte Zähler zurücksetzen).
- `ExcludeGateway`-Pref (Default an) + Toggle in DHCP Scopes: DHCP vergibt nie .1 auf /24 oder kürzer, auch in privaten Netzen.
- Konfigurierbarer Toggle-Hotkey (`ToggleKey`-Pref, Default P), Tasten-HUD-Eintrag und Oeffner fuers F1-Hub (nur mit gregCore).
- Einheitliches Open-Source-Layout (README, Docs, Badges) nach gregCore-Vorbild.

### Fixed

- Racks-Seite skaliert mit UI-Font-Scale (Front-View, Tabellen-Layout, gecachte Styles werden bei Skalenwechsel neu aufgebaut).
- Öffnen/Schließen friert nicht mehr sekundenlang: Reflection-Lookups gecacht (pro Typ/Name einmal statt pro Gerät), voller EOL-Snapshot nur noch bei leerem Cache.

## [0.1.0] — 2026-09-22

- Initialer standardisierter Stand.
