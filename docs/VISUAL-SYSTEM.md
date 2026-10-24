# VibeSkua X visual system

This is the shared visual baseline for UI implementation. Use these named resources and rules instead of inferring styling from nearby controls or a conversation screenshot. It establishes the charcoal/cyan direction of the rough shell design; it does not make every pictured feature part of the current milestone.

## Authority and implementation

- [DesignTokens.axaml](../VibeSkuaX.App/Styles/DesignTokens.axaml) defines application colors, brushes, typography, spacing, and dimensions. Its values determine the running app. Update this guide when intentionally changing the baseline.
- [ShellStyles.axaml](../VibeSkuaX.App/Styles/ShellStyles.axaml) applies those resources to existing shell elements. [App.axaml](../VibeSkuaX.App/App.axaml) loads them after configuring FluentAvalonia.
- Prefer the controls, templates, interaction patterns, and focus adorners already provided by the pinned FluentAvalonia version. The app resources describe shell styling; they do not replace the library's complete control theme.
- Use `DynamicResource` for app brushes and `StaticResource` for fixed dimensions and typography. Reuse an existing semantic token before adding one. Keep new shared values here; view-specific initial window bounds or native hit-test roles can remain in their views.
- The in-conversation HTML mockup is an illustration, not an Avalonia dependency or a second source of implementation rules. Apply intentional design changes to the resources and this guide.

## Appearance and color

The baseline is **Dark**. FluentAvalonia uses the app's fixed cyan accent (`VsColorAccent`); system accent/theme following is explicitly disabled. Light/high-contrast palette switching is not implemented by this baseline. Adding another theme requires a complete, tested palette for these roles, rather than inheriting dark hex values or assuming the framework recolors custom chrome. Native Flash artwork/background remains separate from Avalonia styling.

All brush keys below start with `VsBrush`.

| Suffix | Value | Purpose |
| --- | --- | --- |
| `Canvas` | `#121214` | Main shell background |
| `TitleBar` | `#1A1A1D` | Title/account strip |
| `Toolbar` | `#232326` | Main command row |
| `Surface` | `#26262B` | Shell-owned raised surfaces |
| `Selected` | `#2A2A2F` | Selected tab or persistent subtle control fill |
| `Hover` | `#34343A` | Custom chrome hover feedback |
| `Pressed` | `#3B3B42` | Custom chrome pressed feedback |
| `Border` | `#35353C` | Thin structural borders |
| `TabDivider` | `#515159` | Short separators between inactive tabs |
| `TextPrimary` | `#E6E6EB` | Essential labels and headings |
| `TextSecondary` | `#A3A3AE` | Supporting text and quiet icons |
| `TextDisabled` | `#71717C` | Disabled affordances only |
| `Accent` | `VsColorAccent`, `#72D4F0` | Brand, selection indicator, emphasis |
| `OnAccent` | `#10202A` | Content on a filled cyan surface |
| `Success` | `#A3DBAD` | Running/online status |
| `Warning` | `#E8C675` | Caution requiring attention |
| `Danger` | `#EE9496` | Error/destructive meaning |
| `CloseHover` | `#C42B1C` | Native-style close-button hover fill |
| `ClosePressed` | `#A52519` | Close-button pressed fill |
| `OnClose` | `#FFFFFF` | Content on the red close-button fill |

Use cyan sparingly; keep most controls neutral. Green denotes running/online state, not decorative emphasis. Reserve error colors for their semantic purpose. New controls should use the matching FluentAvalonia component/state styling before adding app-specific overrides.

## Typography and icons

All sizes are device-independent pixels (DIPs), not physical screen pixels.

| Token | Value | Use |
| --- | --- | --- |
| `VsFontUi` | Segoe UI | Windows shell typography |
| `VsTextSmall` | 11 | Secondary status/metadata; do not shrink below this |
| `VsTextBody` | 12 | Compact shell labels and commands |
| `VsTextReading` | 14 | Longer messages and reading content |
| `VsTextHeading` | 22 | Main empty/error-state heading |
| `VsTextBrand` | 13 | Existing small X brand mark only |
| `VsIconCompact` | 14 | Dense toolbar icons |
| `VsIconStandard` | 16 | Normal command/menu icons |
| `VsIconCaption` | 10 | Existing Windows caption glyphs only |
| `VsFontCaptionIcons` | Segoe Fluent Icons, Segoe MDL2 Assets | Existing minimize/maximize/close implementation only |

Use regular weight for labels and body text, semibold for shell titles/headings, and bold only for the existing brand mark. Do not change font size or weight as click feedback. Use FluentAvalonia's existing icon controls/sources, such as `FASymbolIcon`/`FASymbolIconSource`, before introducing a different icon set. Keep icons from a coherent family with consistent sizing; the HTML mockup's icons are illustrative.

The existing text classes are `vsx-title`, `vsx-status`, `vsx-heading`, and `vsx-body`, applied within a `Window` with `Classes="vsx-shell"`. The existing `vsx-brand` class styles the badge and its text. Preserve meaningful accessible names for icon-only actions.

## Spacing, shape, and dimensions

The spacing scale is **4, 8, 12, 16, 24, 32** DIPs: `VsSpace1`, `VsSpace2`, `VsSpace3`, `VsSpace4`, `VsSpace6`, and `VsSpace8`. Use 8 for normal icon/text spacing, 12–16 for compact container insets, and 24–32 around larger content. Native geometry and optical icon alignment are deliberate exceptions to this scale.

| Token | Value | Use |
| --- | --- | --- |
| `VsRadiusControl` | 4 | App-specific compact control shape |
| `VsRadiusBrand` | 6 | Brand badge |
| `VsRadiusSurface` | 8 | Shell-owned popover/panel shape |
| `VsBorderHairline` | 1 on all sides | Thin app-surface border |
| `VsBorderBottom` | `0,0,0,1` | Horizontal row separator |
| `VsHeightTitleBar` | 40 | Current verified title-bar height |
| `VsWidthCaption` | 46 | Fixed width of each native caption button |
| `VsHeightToolbar` | 44 | Planned main toolbar row |
| `VsHeightControl` | 32 | Compact app-specific command height |
| `VsSizeBrand` | 22 | Current badge width/height |
| `VsSizeStatusDot` | 6 | Online/running indicator |
| `VsSizeProgress` | 16 | Loading indicator in the reserved title/status area |
| `VsWidthAuto` | 90 | Stable Auto control width |
| `VsWidthAccountTab` | 132 | Fixed account-tab width; overflow scrolls |
| `VsHeightTabDivider` | 14 | Short, vertically centered tab separator |
| `VsWidthTitleDrag` | 160 | Minimum reserved drag/status area after the add button |
| `VsWidthGameListButton` | 96 | Fixed Games dropdown width; height uses `VsHeightControl` |
| `VsWidthGameList` | 320 | Fixed open-games menu width; long labels wrap |
| `VsWidthConfirmation` | 420 | Owned tab-close confirmation width |
| `VsWidthDialogAction` | 112 | Fixed width for confirmation, retry, copy-details, and empty-state actions |
| `VsWidthMessage` | 520 | Maximum central message width |

Composite resources preserve consistent insets: `VsPaddingTitle` = `12,0,16,0`, `VsMarginAfterIcon` = `8,0,0,0`, and `VsMarginMessage` = `32`.

`VsPaddingAccountTab` = `8,3,4,3` applies to both selected and inactive tab headers. `VsBorderTabIndicator` = `0,0,0,2` reserves the same bottom-border space in every state; selection changes its brush to cyan. Tab text remains regular weight in every state. Both library tab-width limits use `VsWidthAccountTab`, retaining readable neutral labels and the library's scrolling behavior when tabs no longer fit.

The add-button host stretches to the title row. `VsPaddingTabAdd` = `3,0,0,2` keeps the library's 3-DIP horizontal gap and reserves the same 2-DIP bottom indicator area as tab content. Dividers also reserve `VsBorderTabIndicator` as their margin. This aligns tab close buttons, dividers, and add on one center line without changing their interaction sizes.

Overflow scroll hosts also stretch. `VsPaddingTabScrollDecrease` = `8,0,3,2` and `VsPaddingTabScrollIncrease` = `3,0,8,2` retain the pinned template's horizontal gaps while reserving that same indicator area. Both arrows share the close/divider/add center line. Bind tab backgrounds, foregrounds, selection silhouettes/indicators, and dividers directly to dynamic app brush resources; static resource aliases do not track replacement of their source brush.

Do not force library controls to the app-specific height/radius when that breaks their template, content, or accessibility. Use the library's supported layout properties and check the real result. Let Windows/Avalonia own the window frame and outer shape.

## Interaction and state

1. **Stable geometry:** hover, pressed, checked/selected, loading, disabled, and label/icon changes must not resize a control or move adjacent controls. Reserve the largest content slot. Use color/opacity feedback; preserve padding, border thickness, icon slots, and text metrics. Responsive window resizing is a separate layout operation.
2. **FluentAvalonia first:** compose/style existing components and keep their keyboard/focus behavior. Use custom controls only for a verified gap in the pinned library.
3. **Shared visual language:** reuse these resources, semantic roles, and shared styles throughout the app. If a new role is needed, name and document it rather than copying a nearby literal value.

For the existing custom caption buttons, normal fill is transparent, hover uses `VsBrushHover`, and pressed uses `VsBrushPressed`; close uses its dedicated red fills. Disabled foreground uses `VsBrushTextDisabled`. Geometry is identical in every state. Native caption roles remain on the actual controls; focus indicators come from the retained FluentAvalonia button template.

Caption buttons are not keyboard-focus destinations. Clicking Maximize/Restore must preserve content/game focus, and Space afterwards must not reactivate the caption action. Keep native system-menu keyboard access and accessible button invocation available.

Windows supplies caption tooltips through the native roles. Do not add an Avalonia tooltip to those buttons: it duplicates the native tooltip and can appear below or away from it. Maximize/Restore's accessible name follows the window state. Caption fills change immediately; disable the inherited background brush transition on their content presenters to avoid gray/pink flashes when fading through transparent white. Other FluentAvalonia buttons retain their normal transitions.

Avalonia tooltips use control-relative **Bottom** placement with an 8-DIP gap (`VsSpace2`). Moving between controls anchors the new tooltip to its current owner, independent of the framework's cached pointer position. Keep the framework's normal initial delay and immediate switching between already-open tips. Request native popup surfaces (`ShouldUseOverlayLayer=False`) so tooltips can appear above Flash without taking game focus. Screen-edge adjustments remain owned by Avalonia. These rules do not attach tooltips to native caption buttons.

For the planned **Auto** control, keep the label **Auto**. Show a green 6-DIP dot while running; hide the dot while idle but retain its space. Keep button size and content positions unchanged. Expose Running/Idle through its accessible name and tooltip so state is available without relying on color. This is documented behavior for the future control; it is not connected to the app yet.

## Account tabs and toolbar

The game remains the dominant area, with a compact account/title row and one toolbar row as feature work proceeds. Do not add permanent side panels, extra status rows, or ornamental cards solely to fill space.

- Keep **+** immediately after the last account tab; the remaining empty title area is the drag region. Reserve native caption space at the right.
- Use short, centered **1-DIP-wide, 14-DIP-high** dividers between inactive tabs. Suppress dividers touching the selected or hovered tab. Show a divider before **+** when the last tab is inactive and not hovered. Follow the [tab-divider reference](design/tab-strip-reference.png), including these state details.
- Selected tabs use a distinct background and a cyan selection indicator.
- Do not place the removed Tools/Combat divider back in the main toolbar. Keep its commands in one visually consistent row, with deliberate overflow at constrained widths.
- Prefer `FATabView` for account tabs. Evaluate `FACommandBar`/standard menus against the pinned source's binding and keyboard limitations before choosing the toolbar implementation.
- Mark interactive title-area content with the appropriate native user role, and test it alongside dragging/caption behavior. Tab selection preserves the session; explicit account close owns disposal.

Account tabs are implemented with the pinned `FATabView` template and shared style overrides. Its add button stays after the tab strip, and its footer reserves the drag/status region. Loaded games use a cyan dot, starting/loading games a secondary-text dot, and failed games a danger dot; accessible tab names and tooltips include textual status. Loaded means client readiness. Labels begin with neutral game numbers and update to the connected character name; label changes truncate within the existing tab width. Confirmation uses an owned native window with Cancel as the initial/default action and Escape to cancel. Retry and Open game reuse the fixed action size.

The add action's tooltip and accessible name both say **Open game**. Each tab-close button has an explicit **Close {game label}** accessible name that follows identity changes and newly realized tabs. Ctrl+Tab, Ctrl+Shift+Tab, and Ctrl+F4 are handled over the app's collection, including zero/one tabs and unrealized overflow items. Keyboard navigation retains tab-strip focus; pointer selection returns to the game. Dismissing confirmation restores the originating tab/game focus policy; closing the final game focuses Open game. These are shell shortcuts, not global hotkeys while Flash owns native keyboard input.

The tab footer remains a stretchy native drag region with a real 160-DIP minimum and a reserved 16-DIP `FAProgressRing` slot at its right edge. The pinned FATabView can overwrite its footer column minimum with a zero constrained DesiredSize during initial overflow; the shell enforces the shared minimum above that local assignment when its template is applied. Do not replace the library template or let an overflowing footer cover tab/add/menu input.

A separate, fixed 96-by-32-DIP **Games** dropdown sits immediately before the caption controls, with the standard 8-DIP left margin. It replaces the old visible selected-game status. Keep it outside FATabView's measured strip so library width calculations cannot push it beneath captions. Its FluentAvalonia `FAMenuFlyout` uses a native popup above Flash, a fixed 320-DIP width, wrapping full labels, textual client status, and a checkmark for the current game. It lists every owner, including tabs outside the viewport. Selecting a row reveals the existing tab without replacing its session. With no games the dropdown is disabled; the central empty state explains opening a game and using +.

The Games button tooltip and each tab/menu-item tooltip separate **Client** readiness from **Connection** state. Ready means **Game loaded**; connection is **Not checked** until polled, then **Connected** or **Not connected**. A connection does not imply character data is ready. The button's accessible name includes count, selected label, readiness, and connection, and is the single polite live region. The pinned button peer needs explicit Name-change forwarding; error details do not duplicate announcements, and unchanged polls do not repeat them. The loading ring remains indeterminate and stops on readiness/failure without moving controls.

The Windows title follows the selected character/game label (`Game 1 - VibeSkua X` before identity is available), with `VibeSkua X` when empty. Shared `Window.vsx-shell[IsActive=False]` styles soften the title-bar border to `VsBrushSurface` and title/tab text to `VsBrushTextSecondary`; active windows retain the baseline colors. Use the actual IsActive property, not an unsupported :active pseudo-class. Keep geometry, selection indicators, status colors, and caption hover behavior unchanged.

Use plain punctuation in application text: the Windows title separates the label and product with ` - `, and game-menu rows use the same plain separator. Em dashes remain acceptable in project documentation. Clicking an already-selected tab must still restore game focus. Resizing reveals the selected header but restores its focus only when the strip actually owned focus; a stale keyboard-origin flag is insufficient after native Flash input. Caption actions preserve keyboard focus anywhere in AccountHeader, including the Games opener.

After a selected game fails, focus Retry if the game owned native focus or the active shell has no other focused control. Preserve tab/menu navigation, confirmation focus, and other active windows. Return Windows keyboard focus to the shell before focusing Retry; Avalonia logical focus alone cannot move it out of WinForms. Keyboard menu selection returns to the chosen header; Escape returns to the Games opener, while pointer selection returns to the game or its recovery action.

The close confirmation gives the current game label a separate `vsx-dialog-game` line: reading size 14, semibold, primary foreground. Its explanatory text and safe default Cancel behavior remain. Error UI uses a bounded Grid with scrollable/selectable details and a visible action row. Retry and Copy details use the same fixed 112-by-32-DIP action size; Copied/Copy failed feedback stays inside that size. Selected diagnostic text uses `VsBrushAccent` behind `VsBrushOnAccent` for readable contrast.

The library updates adjacent separator opacity locally. Shared styles additionally hide each selected/hovered tab's own separator through visibility, because template opacity setters cannot override the library's local value. Tests cover both separator rules and stable tab/add positions. The toolbar remains future work.

## Using and changing the system

### Shared window title bar

Use `Controls/WindowTitleBar` for all new app windows. The `vsx-shell` window class supplies the common palette and `BorderOnly`/extended-client-area native frame configuration. Put the title bar in an Auto-height first row and window content below it. It displays the containing window's `Title` and brand mark, with caption actions following `CanMinimize` and `CanMaximize`. Fixed modal dialogs set both capabilities false and keep only Close. A title-bar close uses the window's normal close path, including closing-event cancellation and default dialog result.

The same window style supplies `Assets/App.ico`; the executable embeds that icon too. Its cyan rounded badge and dark Segoe UI bold X match the existing title-bar mark. Checked-in ICO frames cover 16, 20, 24, 32, 40, 48, 64, 128, and 256 pixels. `Assets/App.svg` records the vector source; `tools/Generate-AppIcon.ps1` reads the established dimensions/colors/font from `DesignTokens.axaml` and regenerates both using the installed Windows font. Normal builds consume the checked-in files.

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:controls="using:VibeSkuaX.App.Controls"
        Classes="vsx-shell" Title="Window title"
        Width="{StaticResource VsWidthConfirmation}" SizeToContent="Height"
        CanResize="False" CanMinimize="False" CanMaximize="False">
  <Grid RowDefinitions="Auto,*">
    <controls:WindowTitleBar Name="WindowChrome" />
    <StackPanel Grid.Row="1" Margin="{StaticResource VsMarginMessage}">
      <!-- Window content -->
    </StackPanel>
  </Grid>
</Window>
```

Ordinary resizable windows keep their normal capability defaults. Use `WindowTitleBar.HeaderContent` for a custom header such as the shell's account tabs; that slot replaces only the default title/brand area and preserves the shared caption controls. Mark custom header interactions and drag regions with the native User/TitleBar roles, as the shell does. A host with native content can handle `MaximizeToggled` to restore that content's focus. Keep the existing shared caption dimensions, immediate hover/pressed fills, non-focusable buttons, and native-only caption tooltips; do not recreate them in each window.

Consult [UI-REGRESSIONS.md](UI-REGRESSIONS.md) when changing affected controls. It records the caption tooltip, hover-animation, and focus defects and the checks that protect their fixes.

Apply existing semantic styles where available; use resources for new compositions:

```xml
<StackPanel Spacing="{StaticResource VsSpace3}">
  <TextBlock Classes="vsx-heading" Text="A meaningful heading" />
  <TextBlock Classes="vsx-body" Text="Supporting content" TextWrapping="Wrap" />
</StackPanel>
```

For every intentional shared change, update the relevant resource/style and this guide together. Reuse pinned FluentAvalonia templates instead of copying them into the app. Preserve stable control measurements, accessibility, compiled bindings, and native session ownership. Validate shell appearance/layout headlessly; use targeted Windows/Flash checks for changes to chrome or UI intended to cover the native game. The new shared resources do not make in-window overlays render above Flash.
