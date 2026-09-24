# AGENTS.md — Notes for AI agents (gregMod.IPAM)

Repo: [https://github.com/mleem97/gregMod.IPAM](https://github.com/mleem97/gregMod.IPAM) · License: Apache-2.0 · Version: see `VERSION`.

## Duties

1. **Read first:** `README.md`, `docs/INDEX.md`, `CONTRIBUTING.md` — only then make changes.
2. **Do not commit secrets** (keys, tokens, `.env`). Use keys only via environment variables.
3. **Preserve history:** no `push --force`, no history rewrite without instruction.
4. **Back up changes:** before reporting done, build/test whatever the repo offers (`QUICKSTART.md`).
5. **Keep docs in sync:** for new features update `README.md` + `docs/` + `CHANGELOG.md` (Unreleased).
6. **Conventions:** Conventional Commits (`feat:`, `fix:`, `docs:`, `chore:` …), one logical change per commit.
7. **When unsure:** stop and ask instead of guessing — especially for deletes, migrations, CI.

## Layout

See [README.md](README.md) → Repository Layout. Central entry points: `docs/INDEX.md`, `scripts/`, `tests/`.
