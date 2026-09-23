# gregMod.IPAM

> gregMod.IPAM** extends **Data Center** with an in-game IPAM and network management layer. The focus is faster IP handling, better device visibility, DHCP/subnet

![License](https://img.shields.io/github/license/mleem97/gregMod.IPAM?style=for-the-badge) ![Last commit](https://img.shields.io/github/last-commit/mleem97/gregMod.IPAM?style=for-the-badge) ![Repo size](https://img.shields.io/github/repo-size/mleem97/gregMod.IPAM?style=for-the-badge) ![Stars](https://img.shields.io/github/stars/mleem97/gregMod.IPAM?style=for-the-badge)

## Links

- **Steam Workshop:** [My Workshop (Data Center)](https://steamcommunity.com/id/frikadelle3000/myworkshopfiles/?appid=4170200)
- **Repository:** [https://github.com/mleem97/gregMod.IPAM](https://github.com/mleem97/gregMod.IPAM)
- **Issues:** [https://github.com/mleem97/gregMod.IPAM/issues](https://github.com/mleem97/gregMod.IPAM/issues)
- **Releases:** [https://github.com/mleem97/gregMod.IPAM/releases](https://github.com/mleem97/gregMod.IPAM/releases)

## Overview

**gregMod.IPAM** — gregMod.IPAM** extends **Data Center** with an in-game IPAM and network management layer. The focus is faster IP handling, better device visibility, DHCP/subnet

Siehe [docs/INDEX.md](docs/INDEX.md) für die komplette Dokumentation.

## Compatibility

| Plattform | Status |
|---|---|
| Windows x64 | Supported |
| Linux x64 | Supported |

## Features

- Siehe [docs/INDEX.md](docs/INDEX.md) und [QUICKSTART.md](QUICKSTART.md)

## WebUI (React, Port 8177)

Ab Mod-Start läuft im Hintergrund ein Webserver (nur 127.0.0.1, Prefs
`WebEnabled`/`WebPort`): http://127.0.0.1:8177/

- Dashboard (Status, Overlay auf/zu, DHCP assign-all), Server-Tabelle
  (Filter, DHCP/IP/Rename/Power pro Server), DHCP-Scopes (CRUD), Logs
- API unter `/api/*` (JSON): `status`, `servers`, `dhcp/assign-all`,
  `dhcp/assign-one`, `server/ip`, `server/rename`, `server/power`,
  `scopes` (GET/POST/DELETE), `logs`, `overlay`
- Game-Zugriffe laufen über eine Main-Thread-Queue (8s-Timeout), sonst 503
- Frontend entwickeln: `cd web && npm install && npm run dev`
- Frontend deployen: `npm run build` in `web/`, dann
  `scripts/deploy-web.sh` (kopiert `web/dist` nach
  `UserData/gregMod.IPAM/web/`)

## Installation

Siehe [QUICKSTART.md](QUICKSTART.md).

## Build from Source

```bash
git clone git@github.com:mleem97/gregMod.IPAM.git
cd gregMod.IPAM
```

Details: [QUICKSTART.md](QUICKSTART.md), [CONTRIBUTING.md](CONTRIBUTING.md).

## Repository Layout

```
├── README.md            # Diese Datei
├── QUICKSTART.md        # Schnellstart
├── CHANGELOG.md         # Changelog (Keep a Changelog)
├── CONTRIBUTING.md      # Mitmachen
├── SECURITY.md          # Sicherheitsmeldungen
├── CODE_OF_CONDUCT.md   # Verhaltenskodex
├── AGENTS.md            # Hinweise für KI-Agenten
├── LICENSE              # Apache-2.0
├── VERSION              # Single Source of Truth für die Version
├── docs/                # Dokumentation ([Index](docs/INDEX.md))
├── scripts/             # Build-/Hilfsskripte
├── tests/               # Tests
├── references/          # Referenzen
├── sponsors/            # Sponsoren
└── examples/            # Beispiele
```

## API Documentation

Siehe [`docs/INDEX.md`](docs/INDEX.md).

## Credits

| Rolle | Contributor |
|---|---|
| **Codebase** | [mleem97](https://github.com/mleem97) |

## Contributing

Siehe [CONTRIBUTING.md](CONTRIBUTING.md).

## License

Apache-2.0 — siehe [`LICENSE`](LICENSE).

## 🚀 Join the gregFramework Team!

Baust du gerne Mods, Tools oder Docs? Melde dich: **apply@gregframework.eu** oder via
[Discord](https://discord.gg/greg) — Code, Assets, Docs, Testing, Infra, Community.

---

**gregFramework — powered by the community.**

