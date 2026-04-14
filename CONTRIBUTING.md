# Contributing to DHCPSwitches

Thanks for your interest in improving `DHCPSwitches`.

---

## Ground Rules

- Be respectful and constructive.
- Keep changes focused and atomic.
- Follow project roadmap priorities in `ROADMAP.md`.
- Use **Conventional Commits**.

---

## Development Workflow

1. Fork and create a feature branch:
   - `feat/<short-topic>`
   - `fix/<short-topic>`
   - `docs/<short-topic>`
2. Implement the change with minimal scope.
3. Build locally and validate behavior.
4. Open a Pull Request using the PR template.

---

## Commit Message Format

Use Conventional Commits:

- `feat: add next-free IP allocator`
- `fix: prevent duplicate IP assignment on auto-assign`
- `docs: update roadmap milestones`
- `chore: align project metadata`

Recommended structure:

```text
<type>(optional-scope): short summary
```

Types used in this repo:

- `feat`, `fix`, `docs`, `refactor`, `test`, `chore`

---

## Build references (MelonLoader)

- Set `DataCenterGameDir` in `Directory.Build.props` (or pass `-p:DataCenterGameDir=...` to `dotnet build`) to your **Data Center** install folder.
- The project resolves game stubs from `MelonLoader\Il2CppAssemblies` when present (after running the game once with MelonLoader). If that folder is missing, it falls back to `BepInEx\interop` for local development.
- MelonLoader binaries are referenced from `MelonLoader\net6` (`MelonLoader.dll`, `0Harmony.dll`, `Il2CppInterop.Runtime.dll`). If your install uses another TFM folder, adjust `MelonLoaderNetDir` in `Directory.Build.props`.
- `Core/MelonModInfo.cs` uses a **universal** `[MelonGame()]` so the mod is not filtered by company/product name. To restrict loading to Data Center only, replace it with `[MelonGame("Company", "Product")]` using the values from `MelonLoader/Latest.log` at startup.

## Coding Guidelines

- Keep compatibility with current MelonLoader + IL2CPP interop patterns.
- Prefer clear, modular logic over large monolithic methods.
- Avoid introducing new dependencies unless strictly required.
- Preserve existing gameplay behavior unless the PR explicitly targets behavior changes.

---

## Documentation Guidelines

- Documentation files must be written in **English**.
- Use consistent Markdown structure with clear headings and separators.
- Update docs when behavior, setup, or UX changes.

---

## Pull Request Checklist

Before submitting, ensure:

- [ ] Build succeeds locally
- [ ] Changes are scoped and explained
- [ ] Docs are updated (if relevant)
- [ ] Commit messages follow Conventional Commits
- [ ] No unrelated refactors mixed in

---

## Reporting Bugs / Requesting Features

Use GitHub Issues and include:

- Game version
- Mod version / branch
- Repro steps
- Expected behavior
- Actual behavior
- Logs/screenshots if applicable
