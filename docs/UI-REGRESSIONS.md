# UI regression notes

Record confirmed UI defects here so future changes preserve their fixes. Keep current styling rules in [VISUAL-SYSTEM.md](VISUAL-SYSTEM.md) and build/check commands in [README.md](../README.md).

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
