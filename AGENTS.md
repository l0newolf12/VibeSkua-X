# VibeSkua X project brief

## Project purpose

VibeSkua X is a new successor to VibeSkua, rebuilt from the bottom up using the original models and retained core engine as its foundation

## Working instructions

Prefer the simplest correct solution within the requested scope. Avoid speculative features and unnecessary infrastructure. Introduce helpers or abstractions when they clearly improve clarity or correctness. Remove duplication when doing so keeps the code easier to understand. Validate in proportion to the change.

All commits must be authored solely by the user. Use the user's Git author identity; never use an AI or agent identity or add AI/agent co-author attribution. If the user's identity is unavailable, ask for it before committing.

Use Conventional Commits for commit messages: `type(optional-scope): description`, for example `feat(core): add loadout validation` or `fix: handle missing settings`.

Work on and push to `main` unless the user explicitly requests a different branch.

Read [docs/PROJECT.md](docs/PROJECT.md) for project goals, agreed architecture, the current milestone, and the rough UI design reference. Keep project descriptions and design decisions there; keep this file focused on working instructions.

Preserve existing Skua script compatibility. Validate relevant script-facing changes with representative scripts and discuss any required compatibility break before implementing it.

Use [README.md](README.md) for build and verification commands. Run checks appropriate to the change.
