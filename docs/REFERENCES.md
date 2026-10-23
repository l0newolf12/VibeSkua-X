# Local reference guide

The `references/` directory holds user-supplied, Git-ignored research snapshots. Use the Avalonia-related sources, docs, and samples for UI work, and the decompiled game for source-level game information. Keep project instructions in [AGENTS.md](../AGENTS.md), architecture in [PROJECT.md](PROJECT.md), and build/check commands in [README.md](../README.md).

## Versions and contents

Application dependency versions are pinned in [Directory.Packages.props](../Directory.Packages.props). Currently these are Avalonia 12.1.1, FluentAvaloniaUI 3.1.0, and CommunityToolkit.Mvvm 8.4.0.

| Folder | Contents and version guidance |
| --- | --- |
| `references/Avalonia-Source/` | Framework source, platform implementations, samples, and upstream tests. `build/SharedVersion.props` declares 12.1.1. |
| `references/Avalonia-Docs/` | Prose under `docs/`, control documentation under `controls/`, and API snapshots under `api_versioned_docs/`. Select `version-12.1.1/` for the app's API version; general prose can describe newer behavior. |
| `references/Avalonia-Samples/` | Focused examples under `src/Avalonia.Samples/`, indexed by `README.adoc`. The inspected MVVM samples target Avalonia 12.1.4; check their package versions before adapting code. |
| `references/FluentAvalonia/` | FluentAvalonia library source. `src/Directory.Build.props` declares 3.1.0, matching the app's FluentAvaloniaUI package. Its own package configuration uses Avalonia 12.1.0; the app remains pinned to 12.1.1. |
| `references/decompiled game/` | Decompiled AQW client snapshot under `Game4000r5/`. ActionScript is under `Game4000r5/scripts/`; other folders contain exported assets and symbols. |

Reference folders may be absent in another checkout. Use matching upstream source or package metadata when necessary; do not treat their presence as a build prerequisite or change dependency versions to match an example.

## FluentAvalonia and game lookup

For FluentAvalonia controls and windowing behavior, start in `references/FluentAvalonia/src/FluentAvalonia/UI/`. Themes and resources are under `Styling/`; example usage is under `references/FluentAvalonia/samples/FAControlsGallery/`, with upstream tests under `tests/FluentAvaloniaTests/`.

For source-level game questions, search `references/decompiled game/Game4000r5/scripts/` for the relevant ActionScript class, method, or property. Trace that client behavior alongside the project's `Skua.AS3` integration and retained core APIs when changing game access. The decompilation describes the supplied client snapshot; server behavior and compatibility with another game version need separate evidence.

## Framework source map

Paths below are relative to `references/Avalonia-Source/`.

| Area | Starting points |
| --- | --- |
| Native control ownership and layout | `src/Avalonia.Controls/NativeControlHost.cs`; `src/Windows/Avalonia.Win32/Win32NativeControlHost.cs` |
| Window state, sizing, and decorations | `src/Avalonia.Controls/Window.cs`; `src/Avalonia.Controls/Chrome/WindowDecorationProperties.cs` |
| Windows resize borders and caption input | `src/Windows/Avalonia.Win32/WindowImpl.cs`, `WindowImpl.AppWndProc.cs`, and `WindowImpl.CustomCaptionProc.cs` |
| Chrome visual hit-testing | `src/Avalonia.Controls/PresentationSource/PresentationSource.cs` |
| Desktop shutdown behavior | `src/Avalonia.Controls/ApplicationLifetimes/ClassicDesktopStyleApplicationLifetime.cs` |
| Headless input, rendering, and test sessions | `src/Headless/Avalonia.Headless/HeadlessWindowExtensions.cs` and `HeadlessUnitTestSession.cs` |
| Native hosting examples | `samples/ControlCatalog.Desktop/NativeControls/Win/EmbedSample.Win.cs`; `samples/IntegrationTestApp/Embedding/` |
| Native window regression examples | `tests/Avalonia.IntegrationTests.Win32/ExtendClientAreaWindowTests.cs` and `StandardWindowTests.cs` |
| Lifetime and headless behavior tests | `tests/Avalonia.Controls.UnitTests/DesktopStyleApplicationLifetimeTests.cs`; `tests/Avalonia.Headless.UnitTests/` |

The source tree's `src/Avalonia.Themes.Fluent/` implements Avalonia's built-in Fluent theme. It is distinct from the FluentAvalonia library used by this app.

## Documentation and sample map

Useful pages in `references/Avalonia-Docs/`:

- `docs/app-development/native-interop.md`: native handles, hosting, and native-surface limitations.
- `docs/app-development/threading.md` and `docs/fundamentals/application-lifetimes.md`: dispatcher use, initialization, and desktop shutdown modes.
- `docs/data-binding/compiled-bindings.md`: typed XAML bindings and `x:DataType`.
- `docs/styling/styles.md`, `docs/styling/control-themes.md`, and `docs/app-development/resources.md`: styling, control templates, and shared resources.
- `docs/testing/setting-up-the-headless-platform.md`: simulated input, dispatcher work, and rendered frames.

Useful examples in `references/Avalonia-Samples/src/Avalonia.Samples/`:

- `MVVM/CommandSample/ViewModels/CommunityToolkitCommandsViewModel.cs`: toolkit observable properties and sync/async relay commands. The sample also contains a separate ReactiveUI implementation; use the toolkit approach for this project.
- `DataTemplates/`, `CustomControls/`, and `ViewInteraction/`: targeted examples to consult as those features are needed.
- `Testing/TestableApp.Headless.XUnit/` and `Testing/TestableApp.Headless.NUnit/`: upstream test patterns, not a requirement to replace this repository's existing test runner.

## Native-host constraints

`NativeControlHost` queues position updates after rendering and queues destruction when detached. Removing the viewport from a content host or tab can therefore end its native session. Preserve explicit ownership and test the intended behavior before expanding navigation or multi-account UI.

The hosted Flash HWND is not drawn into Avalonia's rendering surface. Ordinary Avalonia z-order, transparency, or transforms do not provide arbitrary composition over that HWND. Verify any UI intended to cover the game with native checks; a headless render omits Flash.

For the custom title bar, preserve the chrome configuration and lifecycle described in [PROJECT.md](PROJECT.md). Windows resize hit-testing depends on native frame styles; a clean-looking headless screenshot does not prove resizing works. Use upstream native tests to understand the platform behavior and this repository's native checks to verify the actual app.
