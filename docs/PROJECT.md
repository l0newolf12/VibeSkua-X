# VibeSkua X project brief

## Project purpose

VibeSkua X is a new successor to VibeSkua, rebuilt from the bottom up using the original models and retained core engine as its foundation. The original presentation layer was deliberately removed so a new interface and application structure can be built.

The priorities are a better UI, clear ownership and cleanup of resources, reliable application and account lifecycles, stability, performance, multi-account control, and an improved scripting workflow. The user has supplied a rough base UI design; use the reference below to guide the new shell.

## Script compatibility

Complete compatibility with existing Skua scripts is the primary product requirement. Compatibility is a goal to validate, not a claim that the current foundation already achieves it.

Preserve script-facing namespaces, assembly identities, public APIs, entry points, compilation and include behavior, script options, and execution semantics wherever existing scripts depend on them. Internal cleanup must account for compatibility. Use representative existing scripts to validate relevant changes, and discuss any required compatibility break before implementing it.

## UI technology

The new UI will use Avalonia with the [FluentAvalonia](https://github.com/amwx/FluentAvalonia) library. FluentAvalonia belongs in the presentation layer; the models and core engine must remain independent of it.

## Agreed architecture

The user approved a small MVVM desktop application organized by feature, with a separate game-host process for each account:

- `VibeSkuaX.App`: the desktop shell, navigation, account management, session controls, and view models. Keep related views and view models together under `Features/<Feature>`, starting with `Shell`; add account, session, and scripting features as needed. Shared controls, styles, and UI services belong in this project.
- `VibeSkuaX.ClientHost`: one account's game session and scripting engine. Each host owns its game runtime, script execution, timers, subscriptions, and cancellation. Preserve compatibility with the existing engine's process-wide state by isolating accounts in separate processes.
- `VibeSkuaX.Runtime.Windows`: the retained Windows Flash bridge and native integration, used by the host.
- Existing `Skua.Core.*` projects: the retained models, script APIs, engine, services, utilities, and generators. Reuse existing models rather than duplicating them in the presentation layer.
- `Skua.AS3`: ActionScript integration. `tests`: focused behavioral, lifecycle, and script compatibility checks.

The desktop application owns host processes and communication connections. Closing an account requests orderly shutdown, waits for completion within a defined timeout, and terminates the host if needed. Views bind to session state; they do not own game sessions. Keep host communication small and explicit, extending it only as implemented features require.

These are the intended boundaries. The new App and ClientHost projects have not been implemented yet.

## UI design reference

The user's rough shell draft is saved at [design/client-shell-rough-draft.png](design/client-shell-rough-draft.png). This is a directional reference, not a final design or an exact pixel specification.

- A dark, compact desktop shell with account tabs in the top/title-bar area, account status indicators, tab close controls, and an add-account/tab action.
- A horizontal toolbar immediately below the tabs. The draft groups are Scripts, Options, Tools & Helpers, Combat, Bank, Diagnostics, and Plugins, with Auto status/control, Jump, and quick actions toward the right.
- A large game viewport occupies the remaining main area. The draft's centered viewport text is a placeholder for the hosted game.
- Account names and displayed states in the image are examples, not real account data or hardcoded defaults.
- Spacing, typography, colors, icons, exact controls, and interaction details remain open to refinement with the user. The pictured features describe the eventual direction and do not expand the first milestone's scope.

## Current stage and first milestone

The repository currently contains the retained foundation, not a runnable desktop application. The old views, view models, launcher, shared UI, and plugin windows have been removed. The Windows Flash bridge and native dependencies are retained in `VibeSkuaX.Runtime.Windows`; ActionScript integration remains in `Skua.AS3`.

The first usable milestone is to launch the new application, load the game for one account, and support clean session/application shutdown. Establish that lifecycle before expanding to multiple accounts and richer scripting workflows.

See [README.md](../README.md) for build commands and the current foundation checks.

## Repository

The repository origin is https://github.com/l0newolf12/VibeSkua-X.git.
