# VibeSkua X

VibeSkua X is a new version of VibeSkua built on the original program's model
and core engine. The previous Avalonia views, view models, launcher, controls,
themes, and plugin windows have been removed so a new presentation layer can
be designed from a clean foundation.

This stage builds libraries. It has no application window or executable entry
point yet, and does not launch a game session on its own.

Project goals, the agreed architecture, the first milestone, and the rough UI reference are documented in [docs/PROJECT.md](docs/PROJECT.md).

## Retained foundation

| Project | Purpose |
| --- | --- |
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

## Build

Use the .NET 10 SDK on Windows:

    dotnet build VibeSkuaX.sln -c Release

BuildProject.bat runs the same build. QuestData.json, AdvancedSkills.json,
the existing SWF, and native runtime binaries are retained. Rebuilding the SWF
separately requires the Flex SDK and Skua.AS3/compile-as3.ps1.
The separate SWF formerly stored in the application folder is preserved as
VibeSkuaX.Runtime.Windows/Resources/legacy-client.swf for reference; builds use
Skua.AS3/skua/bin/skua.swf, as the original project did.

Run the foundation checks after building:

    dotnet run --project tests/VibeSkuaX.Foundation.Tests -c Release --no-build

These check option persistence and cancellation, model-based navigation, and
cancelled/invalid/confirmed input requests without starting the game.

## Boundaries for the new application

- The foundation contains no views or view models and has no Avalonia dependency.
- Dialog requests use plain option values and text; navigation uses TravelDestination.
- A new host must provide platform services, game hosting, dependency injection,
  user interaction, and application lifetime management before the engine can run.
- The Windows bridge still uses the existing ActiveX/WinForms runtime for Flash.
  It contains no application screens and reports initialization failures as exceptions.
- Daily Tracker's previous window and UI-only plugin entry point were removed.
- The old executable packaging scripts and release-publishing workflow were removed.
  No replacement updater/release channel is configured for VibeSkua X.
- Default application data is isolated under %APPDATA%\VibeSkuaX. Existing model
  formats and core migration behavior are retained.

RELEASE_NOTES.md records the original application's history.

## Credits

Based on [NinjaXz's VibeSkua](https://github.com/NinjaXz/VibeSkua),
[auqw/skua](https://github.com/auqw/skua), and
[ArrowDev's Avalonia port](https://github.com/ArrowDev123/Skua).
The retained ButlerV5 engine is credited to l0newolf12.
