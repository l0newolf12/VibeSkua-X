# VibeSkua X project brief

## Project purpose

VibeSkua X is a new successor to VibeSkua, rebuilt from the bottom up using the original models and retained core engine as its foundation. The original presentation layer was deliberately removed so a new interface and application structure can be built.

The priorities are a better UI, clear ownership and cleanup of resources, reliable application and account lifecycles, stability, performance, multi-account control, and an improved scripting workflow. The user has supplied a rough base UI design; use the reference below to guide the new shell.

## Script compatibility

Complete compatibility with existing Skua scripts is the primary product requirement. Compatibility is a goal to validate, not a claim that the current foundation already achieves it.

Preserve script-facing namespaces, assembly identities, public APIs, entry points, compilation and include behavior, script options, and execution semantics wherever existing scripts depend on them. Internal cleanup must account for compatibility. Use representative existing scripts to validate relevant changes, and discuss any required compatibility break before implementing it.

## UI technology

The UI uses Avalonia with the [FluentAvalonia](https://github.com/amwx/FluentAvalonia) library. FluentAvalonia belongs in the presentation layer; the models and core engine remain independent of it. The first milestone hosts Flash through WinForms directly inside the Avalonia window.

## Agreed architecture

The application uses a small MVVM desktop shell organized by feature. For milestone one, the shell and its single game session run in the same process:

- `VibeSkuaX.App`: the desktop shell, navigation, account management, session controls, and view models. Keep related views and view models together under `Features/<Feature>`, starting with `Shell`; add account, session, and scripting features as needed. Shared controls, styles, and UI services belong in this project.
- `VibeSkuaX.App/Controls/FlashViewport`: an Avalonia native-control host backed by `Sessions/GameSession`, which owns a WinForms container and a single Flash runtime on the STA UI thread. Avalonia manages attachment and sizing; deferred view detachment preserves the session.
- `VibeSkuaX.Runtime.Windows`: the retained Windows Flash bridge and native integration. It creates the ActiveX control directly in the supplied WinForms container and reports initialization failures to the shell.
- Existing `Skua.Core.*` projects: the retained models, script APIs, engine, services, utilities, and generators. Reuse existing models rather than duplicating them in the presentation layer.
- `Skua.AS3`: ActionScript integration. `tests`: focused behavioral, lifecycle, and script compatibility checks.

The shell disposes its viewport/session after the window actually closes, so a cancelled close preserves the game. The session unsubscribes from Flash callbacks, unregisters its messages, and disposes its runtime and container. Its states are Starting, Loading, Ready, Failed, and Disposed. Startup has a cancellable 45-second deadline; asynchronous version/download/initialization failures are distinct from recoverable game-call errors. Late startup callbacks cannot revive a failed or disposed session. Native attachment uses Avalonia's framework support; there is no application-authored cross-process HWND reparenting or window-position synchronization.

Custom window chrome uses `WindowDecorations="BorderOnly"` with `ExtendClientAreaToDecorationsHint="True"`. Together these retain Windows' resize frame while extending content into the top area. Do not remove decorations entirely to hide a frame artifact: that also removes native resizing. Avalonia's `WindowDecorationProperties.ElementRole` marks the drag area and caption buttons, allowing native title-bar behavior. Windows' normal thin border remains.

The earlier separate `VibeSkuaX.ClientHost` process proposal is deferred. The chosen foundation for multiple engines in one process is `Skua.Core/Scripts/EngineSession`: each owner builds its own service provider, bot facade, messengers, compiler registries, and hotkey commands. Resolve services through the owner's `Services`/`GetRequiredService` methods so construction captures the correct session. Use `Enter()` for host callbacks, and await `DisposeAsync()` to stop a running or starting script before disposing its services.

Existing `ScriptMain(IScriptInterface)` and `IScriptInterface.Instance` APIs remain. The compiler binds source-level Toolkit `Ioc.Default` and messenger `Default` references to the current session, including compiled source includes; strings and unrelated types are untouched. The original no-argument `AddCommonServices()` still provides legacy single-engine composition. It must not be used to create multiple independent engines.

Two real engines have passed concurrent source/include execution, message routing, cancellation, failure, restart, cache, and disposal checks with fake game/platform dependencies. This is service and lifecycle isolation, not a security boundary or a guarantee about arbitrary scripts, third-party static state, or precompiled DLLs that directly use process globals. Non-cooperative script code cannot be safely killed in process; stop reports a timeout rather than silently disposing its live service graph. Account tabs, multiple live games, and the shell's scripting controls remain future work.

## UI design reference

The user's rough shell draft is saved at [design/client-shell-rough-draft.png](design/client-shell-rough-draft.png). This is a directional reference, not a final design or an exact pixel specification.

- A dark, compact desktop shell with account tabs in the top/title-bar area, account status indicators, tab close controls, and an add-account/tab action.
- A horizontal toolbar immediately below the tabs. The draft groups are Scripts, Options, Tools & Helpers, Combat, Bank, Diagnostics, and Plugins, with Auto status/control, Jump, and quick actions toward the right.
- A large game viewport occupies the remaining main area. The draft's centered viewport text is a placeholder for the hosted game.
- Account names and displayed states in the image are examples, not real account data or hardcoded defaults.
- Spacing, typography, colors, icons, exact controls, and interaction details remain open to refinement with the user. The pictured features describe the eventual direction and do not expand the first milestone's scope.

## Current stage and first milestone

The repository now contains a runnable Windows x64 desktop prototype alongside the retained foundation. Its shell has a custom title bar with drag, minimize, maximize/restore, and close controls, and a single game viewport. The old presentation layer remains removed; ActionScript integration remains in `Skua.AS3`.

Milestone one is a simple custom-title-bar UI that loads the game for one account using direct WinForms Flash hosting and supports clean session/application shutdown. Establish that lifecycle before expanding to multiple accounts and richer scripting workflows.

The user has confirmed that the shell runs and Flash renders the game's login screen. Automated headless tests exercise the real shell's layout, caption buttons, status/error bindings, cancelled close, and disposal. Opt-in Windows checks use the production desktop lifetime and real Flash. They check all eight resize hit targets, caption hit-testing and input, native resizing, minimum sizes, double-click maximize, minimize/restore, maximized work-area bounds, and WinForms/ActiveX sizing without replacing the game session. They verify the normal thin top border, partial initialization cleanup, early and cancelled close, native-handle destruction, and process exit. An isolated missing-SWF launch verifies the actual error UI and cleanup. Captures include a headless shell render and native title bar.

Native checks also exercise deferred detach/reattach without restarting the game, recoverable versus fatal failures, asynchronous startup errors, close while loading, and category-aware inventory fixtures using the supplied game's original `InvCat` class. They verify native popup and owned-dialog windows overlap Flash above its owner, then confirm the runtime remains usable. Use native popups or owned windows for UI covering Flash; ordinary in-window Avalonia overlays do not compose over its native surface.

Native checks use messages to their own window; they do not move the desktop pointer or validate every monitor/DPI arrangement. Headless layout alone cannot prove native resize behavior. Script execution and multi-account controls have not yet been connected to the new shell.

See [FOUNDATION-REVIEW.md](FOUNDATION-REVIEW.md) for the seven original findings, their implemented resolutions, verification evidence, and remaining validation limits.

See [UI-REVIEW.md](UI-REVIEW.md) for the source-based review of current shell usability and recommendations for applying the rough tabs/toolbar design with the pinned framework versions.

See [README.md](../README.md) for build commands and the current foundation checks.

## Repository

The repository origin is https://github.com/l0newolf12/VibeSkua-X.git.
