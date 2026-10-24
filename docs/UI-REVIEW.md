# UI review

Reviewed on 2026-10-23 against the current shell, its saved headless render and native title-bar capture, the rough design, and local Avalonia 12.1.1 / FluentAvalonia 3.1.0 sources. This review makes recommendations; it does not implement UI changes or expand milestone one.

The compact title bar and large game area already fit the design direction. Keep the working `BorderOnly`/extended-client-area configuration, native caption roles, direct WinForms hosting, CommunityToolkit.Mvvm, and compiled bindings. There is no demonstrated need to replace the window with `FAAppWindow` or introduce a navigation framework.

## Improvements to the current shell

### 1. Make failures actionable and accurately named — P2

**Current behavior:** `ShellWindow.axaml` always displays **The game could not start**, including a fatal runtime disconnect after the game was already Ready. The only error content is a wrapping `TextBlock`; there is no retry or copy action. A transient network failure therefore requires restarting the application, and a long technical error is difficult to share.

**Recommendation:** Present an appropriate title such as **Couldn't load the game** or **The game disconnected**, a concise explanation, **Retry**, and **Copy details**. Put technical details in a selectable, scrollable disclosure rather than the main explanation. Retrying should dispose the failed owner and create a fresh session; merely clearing `HasError` cannot restart the existing Failed session. Disable retry while a new attempt is starting.

Keep window minimize/maximize/close handlers in the view. Put retry availability and user-facing failure state in the view model using the existing Toolkit command approach. Carry failure category/state separately from message text instead of deriving behavior by parsing strings.

**References:** [current error UI](../VibeSkuaX.App/Features/Shell/ShellWindow.axaml), [session failure behavior](../VibeSkuaX.App/Sessions/GameSession.cs), [Toolkit commands sample](../references/Avalonia-Samples/src/Avalonia.Samples/MVVM/CommandSample/ViewModels/CommunityToolkitCommandsViewModel.cs), [FAInfoBar examples](../references/FluentAvalonia/samples/FAControlsGallery/Pages/FAControlsPages/InfoBarPage.axaml). An inline error panel is enough here; adopting FAInfoBar is optional.

### 2. Give loading a visible state — P2

**Current behavior:** Starting/loading is communicated only by 11-point text at the far right of the title bar. The rest of the shell can remain blank while the version and game downloads run, up to the startup deadline. The saved headless render demonstrates the shell layout, but omits Flash and cannot establish the live game's own loading artwork.

**Recommendation:** Add a small, temporary loading row with a progress indicator and a useful phase label. Use indeterminate progress until actual progress measurements exist. Bind the indicator's active state to loading and stop it when Ready or Failed. Expose `IsLoading` from session state rather than comparing the displayed status text.

Keep this feedback in shell space outside the Flash surface. An Avalonia panel drawn over the HWND is not a reliable loading overlay, and removing the host before startup could prevent the game from initializing. Preserve the host's attachment and lifetime while reporting progress.

**Reference:** [FAProgressRing implementation](../references/FluentAvalonia/src/FluentAvalonia/UI/Controls/ProgressRing/FAProgressRing.cs) provides `IsActive` and `IsIndeterminate` in the pinned version.

### 3. Replace scattered colors with a small theme palette — P2

**Current behavior:** The app requests Dark, while the shell hardcodes its backgrounds, text, borders, hover colors, and badge color directly in XAML. Those values do not respond to theme resources. Loading FluentAvalonia's theme alone does not make those custom colors adapt to Windows high contrast.

**Recommendation:** Keep the intended dark appearance, but centralize the small set of shell colors and spacing values. Use semantic dynamic resources for text, surfaces, borders, and interactive states, with explicit high-contrast treatment. Decide how system contrast preference overrides the default dark appearance; do not add a settings screen solely for this cleanup.

Use existing FluentAvalonia resources where their meaning matches, such as `TextFillColorPrimaryBrush`, `TextFillColorSecondaryBrush`, and `SolidBackgroundFillColorBaseBrush`. Preserve an intentional fixed Flash background separately from the surrounding shell theme.

**References:** [theme resources](../references/FluentAvalonia/src/FluentAvalonia/Styling/StylesV2/Fluentv2.axaml), [theme/contrast policy implementation](../references/FluentAvalonia/src/FluentAvalonia/Styling/Core/FluentAvaloniaTheme.axaml.cs), [Avalonia theme-variant guidance](../references/Avalonia-Docs/docs/styling/theme-variants.md).

### 4. Announce status changes and describe caption actions precisely — P2

**Current behavior:** Caption buttons already have accessible names and tooltips, which is good. However, the maximize button always says **Maximize or restore**, even when one specific action applies. Status/error text has no live-region setting. Existing mouse/layout tests do not establish keyboard or screen-reader behavior, particularly when focus moves into the native game.

**Recommendation:** Update the maximize/restore accessible name and tooltip with the icon. Mark meaningful loading/completion status as a polite live region, and deliberately announce a newly displayed failure. Preserve visible keyboard focus. Test Tab/Shift+Tab, Enter/Space, native system-menu behavior, and focus returning from an owned dialog while Flash is active. Do not promise global shortcuts until their behavior with native Flash focus is verified.

**References:** [accessibility guidance](../references/Avalonia-Docs/docs/app-development/accessibility.md), [pinned AutomationProperties API](../references/Avalonia-Source/src/Avalonia.Controls/Automation/AutomationProperties.cs), [TextBlock text-change notifications](../references/Avalonia-Source/src/Avalonia.Controls/Automation/Peers/TextBlockAutomationPeer.cs), [Windows live-region event handling](../references/Avalonia-Source/src/Windows/Avalonia.Win32.Automation/AutomationNode.cs).

### 5. Finish caption styling and application identity — P3

**Current behavior:** The custom caption style explicitly defines normal and hover appearance. Pressed/disabled states otherwise come from the general button theme, and the title bar does not style inactive-window state. The cyan X is drawn inline; no `Window.Icon` or application icon is configured in the app project.

**Recommendation:** Give caption buttons explicit, coherent pressed and inactive-window appearances while retaining keyboard focus indicators. Use shared vector/icon assets when branding is settled, including the actual window/taskbar icon. Keep the current title-bar height and native resize behavior unless the design calls for a measured change.

Do not copy `CaptionButtonStyles.axaml` as a ready-made solution: in the supplied FluentAvalonia snapshot its caption theme is commented out under a TODO. The current small custom implementation is a reasonable base. Plain window operations do not need to be moved into a view model merely to remove code-behind.

**References:** [current caption styling](../VibeSkuaX.App/Features/Shell/ShellWindow.axaml), [general button states](../references/FluentAvalonia/src/FluentAvalonia/Styling/ControlThemes/BasicControls/ButtonStyles.axaml), [unfinished reference caption theme](../references/FluentAvalonia/src/FluentAvalonia/Styling/ControlThemes/BasicControls/CaptionButtonStyles.axaml).

## Applying the rough design later

These are implementation choices for the planned UI, not defects in the current single-account milestone.

### 6. Use FATabView for account tabs, with explicit session ownership

`FATabView` already provides selection, add/close affordances, width modes, and reordering. Start with ordinary account tabs; detachable windows can wait. Use a typed account collection and compiled templates, adapting the sample to CommunityToolkit.Mvvm.

Account/session ownership must outlive selected tab content. Selecting another tab must preserve its game, while an explicit account close disposes it. The current viewport's deferred-detach behavior has native coverage, but that does not automatically verify a future tab template or owner arrangement.

Follow the sample's `WindowDecorationProperties.ElementRole="User"` on interactive tab content, reserve space for caption buttons, and retain a real empty drag region. Test tab clicks, close buttons, reordering, and native dragging together so Windows does not treat tab interaction as a title-bar drag.

**References:** [tab-view gallery](../references/FluentAvalonia/samples/FAControlsGallery/Pages/FAControlsPages/TabViewPage.axaml), [title-bar tab example](../references/FluentAvalonia/samples/FAControlsGallery/Pages/FAControlsPages/TabViewWindowingSample.axaml), [native role API](../references/Avalonia-Source/src/Avalonia.Controls/Chrome/WindowDecorationProperties.cs).

### 7. Build a compact toolbar with overflow, checking menu behavior first

The draft's second row should preserve game space. Keep common actions visible and provide overflow when the window narrows. Use labeled controls for feature groups and convey Auto state through text as well as color. Avoid implementing unavailable features as decorative buttons.

The pinned library's `FACommandBar` offers primary/secondary commands and dynamic overflow. However, its gallery explicitly notes limitations around binding the command bar and keyboard navigation through submenus. Evaluate it for fixed top-level actions; use standard menus where grouped commands need reliable menu behavior. Verify actual 3.1.0 APIs: some separate sample text snippets still use older names such as `CommandBar`, while the live gallery uses `FACommandBar`.

Check the toolbar at minimum width and increased display scaling, with long labels and many accounts. Define overflow priority before compressing every control into an unreadable strip.

**References:** [live command-bar gallery and caveats](../references/FluentAvalonia/samples/FAControlsGallery/Pages/FAControlsPages/CommandBarPage.axaml), [overflow implementation](../references/FluentAvalonia/src/FluentAvalonia/UI/Controls/CommandBar/FACommandBar.cs).

### 8. Choose dialogs and flyouts that can cover native Flash

This is the most important reference-specific constraint for new tool windows. In this version, `FAContentDialog.ShowAsync` adds a host to Avalonia's `OverlayLayer`; its placement name does not make it a separate native window. It therefore must not be assumed to cover the running Flash HWND.

For modal tools over the game, use an owned window, or evaluate `FATaskDialog` with a Window `XamlRoot` and its default `showHosted: false` path. The latter creates its own window on desktop. For menus, retain native popup surfaces and verify the actual chosen flyout control. `ShouldUseOverlayLayer=false` is a request; confirm the popup uses a native root on Windows.

Existing native checks prove a generic native popup and owned dialog can overlap Flash. They do not prove every FluentAvalonia dialog/flyout works correctly. Add targeted native coverage when choosing the real control, including keyboard focus and restoration after closing it.

**References:** [FAContentDialog overlay hosting](../references/FluentAvalonia/src/FluentAvalonia/UI/Controls/ContentDialog/FAContentDialog.cs), [FATaskDialog window/overlay paths](../references/FluentAvalonia/src/FluentAvalonia/UI/Controls/TaskDialog/FATaskDialog.cs), [Avalonia popup selection](../references/Avalonia-Source/src/Avalonia.Controls/Primitives/Popup.cs).

## Suggested order and validation

Address failure recovery and loading feedback first, then theme resources and accessibility, then caption polish. Build the account tabs and toolbar when that feature work is authorized. Keep feedback panels small and preserve the draft's emphasis on the game viewport.

For implementation, extend the existing headless checks around loading/error bindings, retry availability, caption descriptions, and constrained layouts. Use targeted Windows/Flash checks for retry cleanup, native caption changes, focus restoration, actual tab switching, and chosen menus/dialogs. A headless screenshot omits Flash and cannot verify those native interactions.

This review inspected source and existing captures; it did not launch the client, change production code, run a new screen-reader evaluation, or establish coverage across monitor/DPI configurations.
