# Quickstart — gregMod.IPAM

> gregMod.IPAM** extends **Data Center** with an in-game IPAM and network management layer. The focus is faster IP handling, better device visibility, DHCP/subnet

Repo: [https://github.com/mleem97/gregMod.IPAM](https://github.com/mleem97/gregMod.IPAM) · Version: `0.1.0` · Lizenz: Apache-2.0.

## 1. Klonen

```bash
git clone git@github.com:mleem97/gregMod.IPAM.git
cd gregMod.IPAM
```

## 2. Bauen / Starten

Je nach Tech-Stack **einen** Weg wählen:

```bash
# .NET
dotnet build -c Release
dotnet run --project src/

# Node / pnpm
pnpm install
pnpm build
pnpm start

# Python
python -m venv .venv && source .venv/bin/activate
pip install -r requirements.txt
python -m <modul>
```

## 3. Testen

```bash
dotnet test            # .NET
pnpm test              # Node
pytest                 # Python
```

Details stehen in [README.md](README.md) und [docs/INDEX.md](docs/INDEX.md).
Bei Problemen: Issue anlegen ([Issues](https://github.com/mleem97/gregMod.IPAM/issues)) oder [CONTRIBUTING.md](CONTRIBUTING.md) lesen.
