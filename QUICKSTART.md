# Quickstart — gregMod.IPAM

> gregMod.IPAM** extends **Data Center** with an in-game IPAM and network management layer. The focus is faster IP handling, better device visibility, DHCP/subnet

Repo: [https://github.com/mleem97/gregMod.IPAM](https://github.com/mleem97/gregMod.IPAM) · Version: `0.1.0` · License: Apache-2.0.

## 1. Clone

```bash
git clone git@github.com:mleem97/gregMod.IPAM.git
cd gregMod.IPAM
```

## 2. Build / Run

Choose **one** path depending on the tech stack:

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

## 3. Test

```bash
dotnet test            # .NET
pnpm test              # Node
pytest                 # Python
```

Details are in [README.md](README.md) and [docs/INDEX.md](docs/INDEX.md).
If you run into problems: open an issue ([Issues](https://github.com/mleem97/gregMod.IPAM/issues)) or read [CONTRIBUTING.md](CONTRIBUTING.md).
