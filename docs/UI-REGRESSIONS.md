# UI regression notes

Record confirmed UI defects here so future changes preserve their fixes. Keep current styling rules in [VISUAL-SYSTEM.md](VISUAL-SYSTEM.md) and build/check commands in [README.md](../README.md).

## Final focus transitions — fixed 2026-10-24

The [final milestone-two review](MILESTONE-TWO-FINAL-REVIEW.md) reproduced three additional P2 defects. A click on the already-selected tab does not produce SelectedTabChanged, so the pointer-release path must restore game focus explicitly. Native Flash input also bypasses the Avalonia header: a cached keyboard-origin flag cannot prove that the tab strip still owns focus when it resizes. Reveal the selected item after resizing, but restore its focus only if the strip actually owned focus. Keep explicit keyboard cycling's delayed realization/focus behavior.

The Games button is a sibling of AccountTabs. Caption focus policy must check the complete AccountHeader so Maximize/Restore preserves keyboard focus on the button; a resize should not turn that into tab-header focus. `NativeUiRegressionChecks.CheckFinalFocusTransitions` verifies all three behaviors, including the later caption-focus consequence of a stale resize policy, against two live games. The new checks failed before the fixes.

## Games dropdown, connection tooltips, and recovery focus — 2026-10-24

Keep the Games dropdown outside FATabView's measured strip. Its fixed width and the separate 160-DIP drag minimum must fit before the captions, even when the first layout already contains many tabs. The pinned control derives its footer minimum from a constrained DesiredSize that can be zero during initial overflow; its later local assignments must not override the application's minimum. Assert global hit-area positions as well as local Bounds: a child can retain Width=160 while its zero-width parent centers it outside its allotted space.

Use the pinned FluentAvalonia menu and a native popup above Flash. The app creates ordinary menu items instead of using its experimental data-bound item path. Each item references the existing tab owner; refresh metadata in place while open and discard old items when the owner collection changes. Pointer/keyboard selection must reveal hidden headers and preserve session identity; Escape must preserve selection and return keyboard focus to the opener. Keep every game reachable and the menu disabled when empty. Use a fixed popup width and wrap full labels rather than truncating them like tabs.

Avalonia 12.1.1 exposes Window.IsActive, not an automatic :active pseudo-class. Test the shared inactive style through actual native window activation. The button automation peer also does not forward attached Name changes, so explicitly forward those events for the single polite Games live region. Unchanged identity polls must not repeat announcements. Connection comes from the existing AS3 isLoggedIn bridge (the client's sfc.isConnected), independently of whether character data is available; it is not proof of a fully loaded character.

Capture native game focus before destroying its control. Logical Retry.Focus alone can leave Windows focus on another WinForms child; focus the active shell HWND before its Retry action. Respect confirmations, active tab/menu navigation, and other windows, and guard delayed restoration against close/retry/selection changes. Native checks send Space to the focused Retry button and verify that only its session restarts.

`ShellPolishChecks.cs` and `NativeShellPolishChecks.cs` cover these cases. Native fixture polling also verifies native Windows title updates and loaded/disconnected, connected-without-identity, named connection, and logout tooltips. Existing chrome, resize, tooltip, script, session survival, and cleanup checks remain.

## Tab headers disappear while dragging — fixed 2026-10-24

FluentAvalonia 3.1.0's `FATabViewItem` enters `:dragging` after a pressed pointer crosses its drag threshold, even when its parent has both `CanDragTabs` and `CanReorderTabs` disabled. The item theme then sets `LayoutRoot.Opacity` to zero for a drag preview, while the list correctly refuses to start dragging. Releasing outside the item can leave this cosmetic state uncleared. The game and tab owner remain alive; only the header disappears.

The shared account-tab style keeps that template root opaque during `:dragging` only when both operations are disabled. Preserve the existing template, normal input, and session ownership. If reordering/tear-out is implemented later, the property-qualified override stops applying when either operation is enabled; validate that feature against the pinned library rather than removing its preview behavior globally.

`UiRegressionChecks.cs` reproduces the original zero-opacity failure with real pointer input. It checks selected/inactive headers, overflow, release outside the strip, stable geometry/order/session ownership, and subsequent tab selection. Native checks repeat the drag with Windows messages and verify that two Flash handles, runtime instances, and temporary client-state markers survive.

## Multi-game review follow-up — fixed 2026-10-23

The nine findings in [UI-INTERACTION-REVIEW.md](UI-INTERACTION-REVIEW.md) now have permanent regression coverage.

- The pinned FATabView shortcuts assumed nonempty collections, control-valued items, and realized containers. Keep the app-owned collection-based handlers for Ctrl+Tab/Ctrl+Shift+Tab/Ctrl+F4. The empty/single-tab checks run in a child process with a deadline so a loop regression cannot hang the main runner.
- Preserve the distinction between keyboard tab navigation and pointer selection. Keyboard cycling/confirmation restores tab-strip focus, including across caption maximize/restore; pointer selection and game-oriented confirmation return to Flash. Capture the confirmation caller's policy before opening the dialog: Windows can restore a tab with NavigationMethod.Tab while reactivating the owner, which must not overwrite that original policy. Native checks verify both policies with two real clients.
- Give each realized tab-close button an explicit action name and refresh it when the game label changes. Do not assume the pinned library's commented-out localization code names it. Check actual automation peers after overflow realization and native identity polling.
- Use one polite status live region with the current game label/state. Avoid duplicate live regions on full error details and repeated events from unchanged identity polls. Name events are covered; end-to-end screen-reader speech still needs manual validation.
- Keep recovery actions outside the scrollable diagnostic region. At minimum window size, long errors must not place Retry or Copy details below the client area. Details remain selectable; selected text uses the on-accent foreground for readable contrast. Copy feedback never changes button geometry.
- Stretch both overflow arrow hosts and reserve the same bottom indicator area as close/divider/add. Include their centers in overflow layout checks. Keep the status slot fixed-width so label/state changes do not move the add button; re-reveal the selected tab when the strip resizes.
- Bind tab brushes directly to the shared dynamic resources. Static brush aliases can retain old values after replacement; check distinct colors in selected/hover/pressed states and restore the originals afterwards.

`UiRegressionChecks.cs` covers the headless cases, copy content, confirmation keyboard actions, loading state, and icon frames. `NativeUiRegressionChecks.cs` covers live-game focus, rename/logout metadata, and native window/executable icons. Existing resize, session survival, native popup, caption, and missing-SWF checks remain. New controls use shared tokens/styles, and native integrations retain their original STA ownership.

## Caption buttons — fixed 2026-10-23

These were interaction and styling defects at the boundary between FluentAvalonia controls and native Windows window chrome. The user confirmed the visible tooltip and hover fixes.

| Defect | Cause | Prevention |
| --- | --- | --- |
| Two tooltips appeared over a caption button. | Native caption roles already provide Windows tooltips; the buttons also had Avalonia `ToolTip.Tip` values. | Keep the native roles and let Windows supply caption tooltips. Do not add a second tooltip to these buttons. |
| The extra dark tooltip appeared too low or away from the button. | The additional Avalonia tooltip used separate popup placement from Windows' native tooltip. | Remove that duplicate popup rather than adding placement offsets. Windows retains ownership of the remaining tooltip. |
| Hovering/leaving caption buttons produced gray/pink flashes or lingering fills. | The inherited FluentAvalonia background brush transition interpolated between a solid hover color and transparent white, producing unwanted intermediate colors. | Disable background transitions on caption content presenters only. Preserve immediate hover/pressed/normal feedback and leave other controls' theme transitions intact. |
| Maximize/Restore could take keyboard focus from the game; Space could activate the caption action again. | Caption buttons inherited ordinary button focus behavior. Making them non-focusable alone did not preserve native Flash focus through maximize/restore. | Keep caption buttons non-focusable and explicitly return focus to the ready game after Maximize/Restore. Preserve accessible invocation and native system-menu access. |

### Regression coverage

- `tests/VibeSkuaX.Shell.Tests/CaptionChecks.cs` checks rendered pixels immediately after hover and leave, and checks that no Avalonia caption tooltips are attached. The pixel test reproduced an incorrect lingering color before the transition fix and passed afterwards.
- `tests/VibeSkuaX.Shell.Tests/Program.cs` checks caption geometry and content focus, including Space after Maximize/Restore.
- `tests/VibeSkuaX.Shell.Tests/NativeSmoke.cs` sends native hover/leave and caption input to its own off-screen window. It checks immediate fills, native hit targets, Maximize/Restore accessible names, real Flash keyboard focus, resizing, and session survival.

Run the headless shell checks and native Windows/Flash checks after changing caption styles, templates, roles, focus handling, or framework versions. Native checks do not move the physical pointer or establish tooltip placement across every monitor/DPI arrangement.

### Lessons for future UI work

Check which layer already owns an interaction before adding another implementation. Inspect inherited template animations as well as local styles; correct final colors can still have incorrect intermediate frames. Verify keyboard focus after window actions against the real native game surface, since headless content cannot reproduce all native focus behavior.

## Account-tab dividers (2026-10-23)

FluentAvalonia 3.1.0 sets `TabSeparator.Opacity` locally when tabs load or selection changes. Its own pointer-over template style cannot override that value, so a hovered inactive tab can keep its trailing divider visible. Keep the library's neighbor-opacity behavior and hide the hovered/selected tab's own divider through `IsVisible` in the shared styles. This preserves the pinned template and does not change tab geometry. `TabChecks.cs` covers selected/hovered suppression, the final inactive divider before add, its 1-by-14-DIP size, and stable tab/add positions.

## Tab action alignment and tooltip placement (2026-10-23)

The library's default add-button host was bottom-aligned with bottom padding, placing its center at Y=25 while tab close buttons centered at Y=19. Stretch the existing host and reserve the same bottom indicator area as tab content; reserve that area for dividers too. `TabChecks.cs` reproduces the original 6-DIP difference and checks that close, divider, and add now share a center line.

Avalonia's default pointer-relative tooltip placement uses the top level's cached last pointer position. Immediate tooltip switching can show the new control's content near the previous position. Use the standard control-relative Bottom placement and shared 8-DIP offset throughout the app. `ToolTipChecks.cs` exercises immediate switching and leave behavior headlessly. Native checks verify actual tooltip popup roots reposition beneath each current tab, overlap Flash above its owner, and preserve native game focus. They temporarily position only their own test window within a monitor so screen-edge clamping does not collapse off-screen tooltip positions together. Caption buttons continue to use Windows tooltips alone.

## Consistent auxiliary-window chrome (2026-10-23)

The close-game confirmation initially used Windows' standard light title bar while the shell used app-owned dark chrome. Both now compose `WindowTitleBar`, with the native frame settings centralized in `Window.vsx-shell`. New app windows must use the shared control/style pair. The title bar follows window title/state/capabilities and releases window event subscriptions on detach. Keep game-specific focus restoration in the shell through `MaximizeToggled`; ordinary dialogs have no native-game dependency.

Headless checks cover default title presentation and updates, capability changes, content placement, shared hover pixels, maximize/restore behavior, and confirmation cancellation through its caption close. Native checks retain shell resize/caption coverage and check the actual confirmation's drag/close roles, top frame, hover/leave feedback, disabled maximize behavior on double-click, and close cancellation while two real games survive.
