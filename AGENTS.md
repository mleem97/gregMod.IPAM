# AGENTS.md — Notes for AI agents (gregMod.IPAM)

Repo: https://github.com/mleem97/gregMod.IPAM · License: Apache-2.0 · Version: see `VERSION`.

MelonMod for Data Center. IP address management (IPAM) with in-game
networking hooks and an embedded web UI.

## Duties

1. **Read first:** `README.md`, `docs/INDEX.md`, `docs/ARCHITECTURE.md`, `docs/NETWORKING_HOOKS.md` — only then make changes.
2. **Do not commit secrets** (keys, tokens, `.env`). Use keys only via environment variables.
3. **Preserve history:** no `push --force`, no history rewrite without instruction.
4. **Verify changes:** before reporting done, build the mod (`dotnet build gregMod.IPAM.csproj -c Release` or `./build.sh IPAM` from `ModRepositories/`).
5. **Keep docs in sync:** for new features update `README.md` + `docs/` + `CHANGELOG.md` (Unreleased).
6. **Conventions:** Conventional Commits (`feat:`, `fix:`, `docs:`, `chore:` …), one logical change per commit.
7. **When unsure:** stop and ask instead of guessing — especially for deletes, migrations, CI.

## Build and references

- Target: `net6.0`, x64. Game: Data Center (`MelonGame("Waseku", "Data Center")`).
- `references/` holds absolute symlinks into the Steam Data Center install.
  Never commit `references/*.dll`, `bin/`, or `obj/`.
- After a fresh clone, run `../tools/sync-melon-assemblies.sh`.
- Deploy only with `./build.sh IPAM --deploy`.

## Hard rules

- Networking hooks are documented in `docs/NETWORKING_HOOKS.md` — keep that
  file accurate whenever hook targets change.
- **Never** touch gregCore types outside a soft-probe/JIT-split bridge — the
  mod must load without `gregCore.dll`.
- Embedded web UI binds to loopback only by default; never expose it
  unconditionally and never log secrets or tokens.
- Defensive `try/catch` in every per-frame/network path; no per-frame reflection.

## Layout

- `src/Config/`, `src/Core/`, `src/Ipam/`, `src/Networking/`, `src/Patches/` — core logic.
- `src/Web/` — embedded UI. `src/Input/`, `src/Diagnostics/` — support.
