# VibeSkua X

VibeSkua X is a new version of VibeSkua built on the original program's model
and core engine. The previous Avalonia views, view models, launcher, controls,
themes, and plugin windows have been removed so a new presentation layer can
be designed from a clean foundation.

The Windows x64 desktop prototype has a custom title bar with account tabs and
multiple independent live games. It hosts each Flash ActiveX control through WinForms inside the Avalonia
window, using Avalonia's native-control hosting support. The user has confirmed
that it displays the game's login screen. Startup opens one game; **+** opens another.
Switch tabs to change games. Tab close asks for confirmation, failed games offer
**Retry**, and closing the last tab leaves an **Open game** action. Login is manual.
The top-right **Games** dropdown lists every open game, including hidden tabs. Its
tooltip separates client readiness and connection state; the Windows title follows
the selected character. Inactive windows use quieter chrome, and the empty state
explains the next step. Appropriate selected-game failures focus **Retry** without
interrupting tab/menu navigation, confirmations, or another active window.

Project goals, the agreed architecture, the first milestone, and the rough UI reference are documented in [docs/PROJECT.md](docs/PROJECT.md).

The UI's shared palette, dimensions, typography, control states, and implementation rules are defined in [docs/VISUAL-SYSTEM.md](docs/VISUAL-SYSTEM.md), backed by reusable resources in `VibeSkuaX.App/Styles/`.

## Retained foundation

| Project | Purpose |
| --- | --- |
| VibeSkuaX.App | Avalonia/FluentAvalonia shell, custom title bar, and in-process WinForms game viewport |
| Skua.Core.Models | Original game, account, settings, inventory, quest, skill, and equipment data |
| Skua.Core.Interfaces | Engine contracts and presentation-independent host interaction requests |
| Skua.Core | Script execution, combat, navigation, services, options, and plugin infrastructure |
| Skua.Core.Utils | Shared utilities |
| Skua.Core.Generators | Source generators for game/Flash bindings |
| Skua.AS3 | ActionScript integration source and the existing compiled SWF |
| VibeSkuaX.Runtime.Windows | Native Flash bridge and interop dependencies, extracted from the original client |
| Skua.Plugin.ButlerV5 | Butler automation engine, without its roster/settings windows or menu wiring |

The existing Skua.Core namespaces and assembly names are retained for script
compatibility. They do not determine the new product name.

## Build and run

Use the .NET 10 SDK on Windows:

    dotnet build VibeSkuaX.sln -c Release

Run the desktop prototype:

    dotnet run --project VibeSkuaX.App -c Release --no-build

Game hosting requires the 64-bit CleanFlash ActiveX runtime. The game loads
over the network. The shell displays an error if the Flash runtime cannot start.
Long errors can be scrolled/selected, and **Copy details** copies the full error
with app/game context. The title strip shows an indeterminate indicator while
the selected game loads. Shell tab navigation supports Ctrl+Tab,
Ctrl+Shift+Tab, and Ctrl+F4 while Avalonia controls have keyboard focus.

The checked-in Windows icon matches the cyan X title-bar badge and contains
16–256-pixel sizes. To regenerate the SVG/ICO from the existing Windows font:

    .\tools\Generate-AppIcon.ps1

Normal builds do not run this generator or require additional graphics tooling.

BuildProject.bat runs the same build. QuestData.json, AdvancedSkills.json,
the existing SWF, and native runtime binaries are retained. Rebuilding the SWF
separately requires the Flex SDK and Skua.AS3/compile-as3.ps1.
The separate SWF formerly stored in the application folder is preserved as
VibeSkuaX.Runtime.Windows/Resources/legacy-client.swf for reference; builds use
Skua.AS3/skua/bin/skua.swf, as the original project did.

Run the foundation checks after building:

    dotnet run --project tests/VibeSkuaX.Foundation.Tests -c Release --no-build

These check option persistence and cancellation, model-based navigation, and
cancelled/invalid/confirmed input requests without starting the game. Two real
engine managers also compile and run representative Skua source scripts and
includes concurrently. They check bot/global ownership, messages, compiler
registries, polling cancellation, failure, restart, cleanup exceptions, active
disposal, and disposal during startup. Game/platform dependencies are fakes.

The foundation runner prints exception details and exits with code 1 on failure.
Use `-- --verify-failure-exit` with its run command to intentionally exercise this
error-reporting path; that probe should exit with code 1 without a Windows crash dialog.

Run the shell checks without a visible window or Flash installation:

    dotnet run --project tests/VibeSkuaX.Shell.Tests -c Release --no-build

These load the actual shell with Avalonia's headless platform, exercise the
caption buttons with simulated mouse input, check layout/error bindings and
close/disposal behavior, and save a render to artifacts/shell-tests/shell.png.
They also verify shared brush updates and stable caption geometry across hover,
pressed, maximize/restore, and disabled states. Tab checks cover add/select,
stable geometry and dividers, overflow at minimum width, per-game failure/retry,
close confirmation/Escape, adjacent selection, empty state, and all-session cleanup.
They also check close/divider/add center-line alignment and tooltip placement when
moving directly between controls, including closing the old tooltip on leave.
The shell and owned confirmation share `Controls/WindowTitleBar`. Checks cover its
default title, title/capability updates, dialog content layout, and safe caption close.
Rendered pixels check immediate caption hover/leave feedback without gray/pink
flashes. Caption buttons retain only Windows' native tooltips.
The native Flash viewport is not rendered by the headless platform. These checks
also exercise the actual Flash XML codec under en-US and fr-FR, callback argument
shapes, typed/raw cancellation, and ordinary call fallbacks. Source/binary hashes
detect stale bundled AS3 and inventory fixture SWFs. Run commands from the repository root.

UI regression checks also run empty/single-tab shortcuts in a child process with
a 15-second timeout, cycle through unrealized overflow tabs in both directions,
exercise Ctrl+F4 and keyboard confirmation, inspect accessible close names and
polite live-region Name events, and replace/restore tab brushes in active states.
Long diagnostics must scroll while Retry and Copy details stay inside the minimum
window size. Headless clipboard checks verify the full diagnostic text and stable
Copied feedback; they do not alter the Windows clipboard. Loading activity,
confirmation emphasis, shared icon frames, and overflow-arrow alignment are covered.
Pressed tab headers must remain visible when dragged, including overflow and release
outside the strip; subsequent selection, tab order, geometry, and session ownership are checked.
Additional checks cover the Games menu's eleven-owner list, keyboard selection and
Escape, live status, full names, empty guidance, titles, actual inactive styling,
and minimum-width separation of tabs, drag area, dropdown, and captions.

For a real Windows/Flash lifecycle check, with CleanFlash installed and network
access available:

    dotnet run --project tests/VibeSkuaX.Shell.Tests -c Release --no-build -- --native

This uses the production desktop lifetime with an off-screen shell and waits
up to 35 seconds for the real game to load. It checks all eight native resize
targets, title-bar/caption hit targets, native resizing and minimum sizes,
double-click maximize, caption input, minimize/restore, maximized work-area
bounds, and the WinForms/ActiveX bounds after each resize. The same game
controls must survive those changes. It also checks cancelled close, close
before deferred startup, partial Flash initialization failure, handle cleanup,
and actual application exit. Each worker process has a 180-second deadline,
allowing several independent game loads and a retry.
The loaded game file/version is recorded. The same runtime must survive an
isolated call error and deferred detach/reattach. Native popups and owned dialogs
are checked above the real Flash surface. A loopback HTTP fixture exercises
unavailable version data, malformed JSON, unavailable SWFs, the startup deadline,
and close while loading. Late callbacks must not revive failed/closed sessions.
Two real clients must preserve their handles, runtime, and temporary client-state
markers across native tab clicks, with only the selected game visible and focused.
Dragging selected/inactive headers and releasing outside must preserve their visibility,
selection response, both native game handles/runtimes, and those state markers.
The actual Games popup must appear above Flash; pointer/keyboard selection and
Escape must restore the appropriate focus. Native checks exercise activation of
shell/auxiliary windows, Windows title updates, distinct readiness/connection
tooltips, and keyboard activation of Retry after a focused-game failure. Failure
during confirmation or while another window is active must preserve that focus.
The actual tab-close confirmation must appear above Flash. Failure, retry, and
confirmed close must leave the other game usable; application shutdown must also
destroy an inactive live game. Fixture data checks character names and logout.
Native tooltip checks verify placement beneath each current tab above Flash and
preserved game focus. They briefly move only the test-owned window onto a monitor
to avoid off-screen popup clamping, then restore its previous position.
The actual confirmation's shared title bar is checked for native drag/close roles,
immediate caption feedback, fixed-window behavior, and cancellation through Close.
Native regression checks additionally preserve tab-strip focus through repeated
keyboard game switches and confirmation cancellation, then verify pointer selection
returns focus to Flash. Real fixture identity polls update close/status names without
repeated announcements. Windows small/large window icons and the built executable's
embedded icons are checked against the badge colors. Results include
artifacts/shell-tests/native-ui-regressions.json.
These checks do not log into accounts; real login continuity needs user verification.
An AS3 fixture runs the production inventory bridge against the supplied
Game4000r5 InvCat class, with stack, inventory-pool, and bank-swap cases.

A second worker runs from a temporary copy of the build without skua.swf to
verify the real startup error and cleanup; the copy is removed afterwards.
Results are saved to artifacts/shell-tests/native-result.json and
native-startup-failure.json, with additional native-account-tabs.json, native-session-checks.json and
inventory-contract.json results. Native chrome is captured to native-titlebar.png
without capturing account fields. Windows retains its normal thin border;
the custom title bar fills the extended client area without the old blank strip.
CI runs the headless checks; native checks require Windows/CleanFlash and are
opt-in. These checks use messages sent to the test's own window, without
moving the desktop pointer or automating other applications.

After changing AS3 source, rebuild the bundled bridge and its hash manifest:

    .\Skua.AS3\compile-as3.ps1 -MxmlcPath '<Flex SDK>\bin\mxmlc.bat'

After changing the inventory bridge or fixture, rebuild the test SWF too:

    .\tests\VibeSkuaX.Shell.Tests\Fixtures\build.ps1 -MxmlcPath '<Flex SDK>\bin\mxmlc.bat'

The fixture rebuild uses `references/decompiled game/Game4000r5/scripts/InvCat.as`
(or `-GameScriptsPath`) and records its hash. References/Flex are not required for
ordinary .NET builds or CI: the fixture SWF is checked in. Rebuild the .NET solution
after either SWF changes to refresh the application/test outputs. Test fixtures
establish client behavior for this snapshot, not server acceptance of inventory actions.

## Boundaries for the new application

- UI views and view models live in VibeSkuaX.App. The retained models and core
  engine have no Avalonia dependency.
- Dialog requests use plain option values and text; navigation uses TravelDestination.
- Each tab owns one in-process game session; selecting it changes visibility/focus.
  The scripting workflow is future work; the earlier separate host-process proposal
  is deferred.
- The Windows bridge still uses the existing ActiveX/WinForms runtime for Flash.
  It contains no application screens and reports initialization failures as exceptions.
- Daily Tracker's previous window and UI-only plugin entry point were removed.
- The old executable packaging scripts and release-publishing workflow were removed.
  No replacement updater/release channel is configured for VibeSkua X.
- Default application data is isolated under %APPDATA%\VibeSkuaX. Existing model
  formats and core migration behavior are retained.

Headless checks cover shell layout and bindings. Native checks cover Windows
chrome hit-testing, messages, geometry, and Flash lifetime. They do not drive
physical pointer gestures or establish coverage across every monitor/DPI setup.

RELEASE_NOTES.md records the original application's history.

## Credits

Based on [NinjaXz's VibeSkua](https://github.com/NinjaXz/VibeSkua),
[auqw/skua](https://github.com/auqw/skua), and
[ArrowDev's Avalonia port](https://github.com/ArrowDev123/Skua).
The retained ButlerV5 engine is credited to l0newolf12.
