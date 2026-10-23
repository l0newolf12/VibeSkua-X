# VibeSkua X

VibeSkua X is a new version of VibeSkua built on the original program's model
and core engine. The previous Avalonia views, view models, launcher, controls,
themes, and plugin windows have been removed so a new presentation layer can
be designed from a clean foundation.

The Windows x64 desktop prototype now has a custom title bar and a single game
viewport. It hosts the Flash ActiveX control through WinForms inside the Avalonia
window, using Avalonia's native-control hosting support. The user has confirmed
that it displays the game's login screen.

Project goals, the agreed architecture, the first milestone, and the rough UI reference are documented in [docs/PROJECT.md](docs/PROJECT.md).

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

Run the shell checks without a visible window or Flash installation:

    dotnet run --project tests/VibeSkuaX.Shell.Tests -c Release --no-build

These load the actual shell with Avalonia's headless platform, exercise the
caption buttons with simulated mouse input, check layout/error bindings and
close/disposal behavior, and save a render to artifacts/shell-tests/shell.png.
The native Flash viewport is not rendered by the headless platform. These checks
also exercise the actual Flash XML codec under en-US and fr-FR, callback argument
shapes, typed/raw cancellation, and ordinary call fallbacks. Source/binary hashes
detect stale bundled AS3 and inventory fixture SWFs. Run commands from the repository root.

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
and actual application exit. Each worker process has a 70-second deadline.
The loaded game file/version is recorded. The same runtime must survive an
isolated call error and deferred detach/reattach. Native popups and owned dialogs
are checked above the real Flash surface. A loopback HTTP fixture exercises
unavailable version data, malformed JSON, unavailable SWFs, the startup deadline,
and close while loading. Late callbacks must not revive failed/closed sessions.
An AS3 fixture runs the production inventory bridge against the supplied
Game4000r5 InvCat class, with stack, inventory-pool, and bank-swap cases.

A second worker runs from a temporary copy of the build without skua.swf to
verify the real startup error and cleanup; the copy is removed afterwards.
Results are saved to artifacts/shell-tests/native-result.json and
native-startup-failure.json, with additional native-session-checks.json and
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
- The first milestone uses one in-process game session. Multi-account hosting
  and the scripting workflow are future work; the earlier separate host-process
  proposal is deferred.
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
