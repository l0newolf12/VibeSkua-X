# VibeSkua X project brief

## Project purpose

VibeSkua X is a new successor to VibeSkua, rebuilt from the bottom up using the original models and retained core engine as its foundation

## Working instructions

Prefer the simplest correct solution within the requested scope. Avoid speculative features and unnecessary infrastructure. Introduce helpers or abstractions when they clearly improve clarity or correctness. Remove duplication when doing so keeps the code easier to understand. Validate in proportion to the change.

Avoid em dashes in application text and project documentation. Use plain punctuation instead.

All commits must be authored solely by the user. Use the user's Git author identity; never use an AI or agent identity or add AI/agent co-author attribution. If the user's identity is unavailable, ask for it before committing.

Use Conventional Commits for commit messages: `type(optional-scope): description`, for example `feat(core): add loadout validation` or `fix: handle missing settings`.

Work on and push to `main` unless the user explicitly requests a different branch.

Read [docs/PROJECT.md](docs/PROJECT.md) for project goals, agreed architecture, the current milestone, and the rough UI design reference. Keep project descriptions and design decisions there; keep this file focused on working instructions.

Preserve existing Skua script compatibility. Validate relevant script-facing changes with representative scripts and discuss any required compatibility break before implementing it.

Use [README.md](README.md) for build and verification commands. Run checks appropriate to the change.

Prefer automated headless tests for shell behavior and targeted native checks for Windows/Flash integration over desktop automation. Run the native checks for changes to window chrome; headless layout tests cannot verify native resizing.

## UI development and reference sources

Keep interactive controls the same size across hover, pressed, selected, loading, and other state changes. Changing a label or icon must not change the control's width or height or shift neighboring controls. Reserve room for the largest state content; responsive resizing is separate from interaction feedback.

Prefer existing FluentAvalonia controls and themes over custom implementations. Style or compose those controls to match the agreed design, using standard Avalonia controls where needed. Introduce a custom control only for a concrete requirement that the available controls cannot satisfy; check the pinned source and samples first.

Use a shared visual system for colors, spacing, typography, icon sizes, corner radii, and control states. Define reusable values in shared theme/style resources and apply them consistently across the app. Keep variations intentional and tied to a control's role.

Read [docs/VISUAL-SYSTEM.md](docs/VISUAL-SYSTEM.md) before UI implementation. Use `VibeSkuaX.App/Styles/DesignTokens.axaml` and `ShellStyles.axaml` as the shared resource/style definitions; update the guide and resources together when intentionally changing the visual baseline. Do not infer new values from surrounding elements or conversation mockups.

For UI work, consult `references/Avalonia-Source/`, `references/Avalonia-Docs/`, `references/Avalonia-Samples/`, and `references/FluentAvalonia/` before guessing at framework behavior or introducing a platform workaround. For game-related work requiring source-level information, consult `references/decompiled game/` and trace the relevant client code. Treat decompiled output as a game-version snapshot; distinguish client behavior from assumptions about server behavior.

Use [docs/REFERENCES.md](docs/REFERENCES.md) to locate relevant files. Reference snapshots are Git-ignored research material; keep them separate from application code and build dependencies. Search them explicitly with `rg --no-ignore`, scoped to the relevant folder.

Use `Directory.Packages.props` as the authority for dependency versions. Check source, docs, and sample APIs against those versions before adopting them; see the reference guide for snapshot versions. Distinguish Avalonia's built-in Fluent theme from the FluentAvalonia library used by this app.

Keep the existing CommunityToolkit.Mvvm approach for view models and commands, and use typed compiled bindings with `x:DataType`. Adapt samples to this project's stack instead of adding ReactiveUI or changing themes just to reproduce an example.

Keep WinForms/ActiveX creation and disposal on the STA UI thread, and marshal background access to Avalonia controls through their UI dispatcher. Account for deferred layout, native-host sizing, and destruction; use bounded readiness checks in native tests.

Let `NativeControlHost` manage native attachment and sizing. Detaching a host can destroy its game control; verify session ownership and survival when changing navigation or account tabs. Native Flash occupies a separate native surface: verify menus, dialogs, and overlays against the real hosted control rather than assuming Avalonia z-order will cover it.
