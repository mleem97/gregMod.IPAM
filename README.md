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

See [docs/INDEX.md](docs/INDEX.md) for the complete documentation.

## Compatibility

| Platform | Status |
|---|---|
| Windows x64 | Supported |
| Linux x64 | Supported |

## Features

- See [docs/INDEX.md](docs/INDEX.md) and [QUICKSTART.md](QUICKSTART.md)

## WebUI (React, Port 8177)

From mod start, a web server runs in the background (only 127.0.0.1, prefs
`WebEnabled`/`WebPort`): http://127.0.0.1:8177/

- Dashboard (status, overlay open/close, DHCP assign-all), server table
  (filter, DHCP/IP/rename/power per server), DHCP scopes (CRUD), logs
- API under `/api/*` (JSON): `status`, `servers`, `dhcp/assign-all`,
  `dhcp/assign-one`, `server/ip`, `server/rename`, `server/power`,
  `scopes` (GET/POST/DELETE), `logs`, `overlay`
- Game access runs via a main-thread queue (8s timeout), otherwise 503
- Develop the frontend: `cd web && npm install && npm run dev`
- Deploy the frontend: `npm run build` in `web/`, then
  `scripts/deploy-web.sh` (copies `web/dist` to
  `UserData/gregMod.IPAM/web/`)

## Installation

See [QUICKSTART.md](QUICKSTART.md).

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

See [`docs/INDEX.md`](docs/INDEX.md).

## Credits

| Role | Contributor |
|---|---|
| **Codebase** | [mleem97](https://github.com/mleem97) |

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

Apache-2.0 — see [`LICENSE`](LICENSE).

## 🚀 Join the gregFramework Team!

Do you enjoy building mods, tools, or docs? Get in touch: **apply@gregframework.eu** or via
[Discord](https://discord.gg/greg) — Code, Assets, Docs, Testing, Infra, Community.

---

**gregFramework — powered by the community.**

