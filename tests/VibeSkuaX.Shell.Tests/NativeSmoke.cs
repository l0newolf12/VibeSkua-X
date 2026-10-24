using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AxShockwaveFlashObjects;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Messaging;
using VibeSkuaX.App.Controls;
using VibeSkuaX.App.Features.Shell;
using VibeSkuaX.App.Sessions;
using VibeSkuaX.Runtime.Windows.Flash;
using FluentAvalonia.UI.Controls;

namespace VibeSkuaX.Shell.Tests;

/// <summary>Opt-in checks against our own off-screen Windows/ActiveX window; no desktop input.</summary>
internal static partial class NativeSmoke
{
    private static string _artifacts = "";
    private static Exception? _failure;
    private static bool _missingSwf;

    public static int Run(string artifacts, bool missingSwf)
    {
        _artifacts = artifacts;
        _missingSwf = missingSwf;
        // Use the production App's real desktop lifetime, including OnMainWindowClose shutdown.
        int result = AppBuilder.Configure<NativeTestApp>().UsePlatformDetect()
            .StartWithClassicDesktopLifetime([]);
        if (_failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(_failure).Throw();
        Program.Check(result == 0, $"Desktop lifetime returned {result}.");
        Console.WriteLine("PASS: closing the main window stopped the production desktop lifetime.");
        return result;
    }

    public sealed class NativeTestApp : App.App
    {
        public override void OnFrameworkInitializationCompleted()
        {
            base.OnFrameworkInitializationCompleted();
            var window = (ShellWindow)((IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!).MainWindow!;
            window.ShowInTaskbar = false;
            window.ShowActivated = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = new PixelPoint(-30000, -30000);
            Dispatcher.UIThread.Post(async () => await CheckWindow(window));
        }
    }

    private static async Task CheckWindow(ShellWindow window)
    {
        var viewport = window.SelectedViewport!;
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Status(string status, string? error)
        {
            if (error is not null) loaded.TrySetException(new Exception(error));
            else if (status == "Game loaded") loaded.TrySetResult();
        }
        viewport.StatusChanged += Status;
        if (viewport.Session.State == GameSessionState.Ready) loaded.TrySetResult();
        else if (viewport.Session.State == GameSessionState.Failed) loaded.TrySetException(new Exception(viewport.Session.Error));
        try
        {
            IntPtr handle = window.TryGetPlatformHandle()!.Handle;
            if (_missingSwf)
            {
                Program.Check(!File.Exists(Path.Combine(AppContext.BaseDirectory, "skua.swf")), "Failure fixture still has a SWF.");
                await CheckStartupFailure(window, viewport, handle, loaded.Task);
                return;
            }
            CheckResizeHitTargets(handle);
            await CheckCaptionTargets(window);
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(35));
            Program.Check(!EoLHook.IsHooked, "Flash startup left the system-time hook installed.");
            Console.WriteLine("PASS: real Flash loaded the game.");
            Program.Check(!string.IsNullOrWhiteSpace(viewport.Session.GameFile), "Loaded game file/version was not reported.");
            IntPtr container = viewport.ContainerHandle;
            IntPtr flash = FindWindowEx(container, IntPtr.Zero, null, null);
            Program.Check(container != IntPtr.Zero && flash != IntPtr.Zero, "WinForms/ActiveX child is missing.");
            int topInset = CheckTopFrame(handle);
            await WaitForViewport(window, container, flash);
            SaveChromeCapture(handle, window.RenderScaling);
            await CheckCaptionHover(window);

            int operationErrors = 0;
            viewport.Session.OperationFailed += _ => operationErrors++;
            Program.Check(viewport.Session.Runtime!.Call<int>("isTrue") == 0, "Invalid typed call changed fallback behavior.");
            await Until(() => operationErrors == 1, "Recoverable call error was not reported.");
            Program.Check(viewport.Session.State == GameSessionState.Ready && IsWindow(flash) &&
                viewport.Session.Runtime.Call<bool>("isTrue"), "A recoverable error destroyed the running game.");

            var parent = (Panel)viewport.Parent!;
            int index = parent.Children.IndexOf(viewport);
            var runtime = viewport.Session.Runtime;
            parent.Children.Remove(viewport);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Program.Check(IsWindow(container) && IsWindow(flash), "Deferred native detachment ended the game session.");
            parent.Children.Insert(index, viewport);
            await WaitForViewport(window, container, flash);
            Program.Check(ReferenceEquals(runtime, viewport.Session.Runtime) && viewport.Session.State == GameSessionState.Ready,
                "Reattaching replaced or restarted the session.");
            Console.WriteLine("PASS: recoverable call error and deferred detach/reattach preserve the same usable Flash session.");
            await CheckNativeOverlays(window, viewport, flash);

            foreach (var size in new[] { new Size(840, 560), new Size(1280, 800), new Size(760, 500) })
            {
                // Resize through Win32, so WM_SIZE/layout/native-host synchronization all execute.
                var before = WindowRect(handle);
                GetClientRect(handle, out var client);
                int width = (int)Math.Round(size.Width * window.RenderScaling);
                int height = (int)Math.Round(size.Height * window.RenderScaling);
                Program.Check(SetWindowPos(handle, IntPtr.Zero, before.Left, before.Top,
                    width + before.Width - client.Width, height + before.Height - client.Height, 0x0014),
                    "Native resize failed.");
                await Until(() => Math.Abs(window.ClientSize.Width - size.Width) < 1 &&
                    Math.Abs(window.ClientSize.Height - size.Height) < 1, "Avalonia did not receive the native resize.");
                await WaitForViewport(window, container, flash);
                CheckResizeHitTargets(handle);
            }
            CheckMinimumSize(window, handle);
            Program.Check(SetWindowPos(handle, IntPtr.Zero, -30000, -30000, 100, 100, 0x0014), "Minimum-size resize failed.");
            await WaitForViewport(window, container, flash);
            Program.Check(window.ClientSize.Width >= window.MinWidth && window.ClientSize.Height >= window.MinHeight,
                "A native resize shrank the content below the shell's minimum size.");
            Console.WriteLine("PASS: native resize at three sizes, minimum tracking bounds, and Flash fill/same session.");

            var restoreBounds = WindowRect(handle);
            var captionPoint = window.FindControl<Border>("TitleDragArea")!.PointToScreen(new Point(200, 20));
            SendMessage(handle, 0x00A3 /* WM_NCLBUTTONDBLCLK */, new IntPtr(2), Pack(captionPoint.X, captionPoint.Y));
            await Until(() => IsZoomed(handle) && window.WindowState == WindowState.Maximized, "Title-bar double-click did not maximize.");
            Program.Check(AutomationProperties.GetName(window.FindChrome<Button>("MaximizeButton")!) == "Restore",
                "Maximized caption has the wrong accessible action name.");
            await WaitForViewport(window, container, flash);
            var workArea = window.Screens.ScreenFromWindow(window)!.WorkingArea;
            GetClientRect(handle, out var maxClient);
            var origin = new NativePoint();
            ClientToScreen(handle, ref origin);
            Program.Check(origin.X == workArea.X && origin.Y == workArea.Y &&
                maxClient.Width == workArea.Width && maxClient.Height == workArea.Height,
                $"Maximized client does not fit the work area: {origin.X},{origin.Y} {maxClient.Width}x{maxClient.Height}; expected {workArea}.");
            SetFocus(flash);
            IntPtr gameFocus = GetFocus();
            Program.Check(gameFocus == flash || IsChild(flash, gameFocus), "Could not focus the real Flash surface.");
            await NativeClick(window, "MaximizeButton", 9);
            await Until(() => !IsZoomed(handle) && window.WindowState == WindowState.Normal, "Restore button did not restore.");
            Program.Check(AutomationProperties.GetName(window.FindChrome<Button>("MaximizeButton")!) == "Maximize",
                "Restored caption has the wrong accessible action name.");
            await WaitForViewport(window, container, flash);
            Program.Check(GetFocus() == gameFocus, "Restoring with the caption button took native focus from Flash.");
            await NativeClick(window, "MaximizeButton", 9);
            await Until(() => IsZoomed(handle), "Focus-check maximize did not maximize.");
            Program.Check(GetFocus() == gameFocus, "Maximizing with the caption button took native focus from Flash.");
            await NativeClick(window, "MaximizeButton", 9);
            await Until(() => !IsZoomed(handle), "Focus-check restore did not restore.");
            await WaitForViewport(window, container, flash);
            Program.Check(GetFocus() == gameFocus, "Repeated caption actions took native focus from Flash.");
            Console.WriteLine("PASS: maximize/restore caption clicks preserve native Flash keyboard focus.");
            Program.Check(WindowRect(handle).Equals(restoreBounds), "Restore did not preserve normal window bounds.");
            await NativeClick(window, "MinimizeButton", 8);
            await Until(() => IsIconic(handle) && window.WindowState == WindowState.Minimized, "Minimize button did not minimize.");
            window.WindowState = WindowState.Normal;
            await Until(() => !IsIconic(handle), "Window did not restore after minimizing.");
            await WaitForViewport(window, container, flash);
            Console.WriteLine("PASS: native title-bar double-click, caption button input, minimize/restore, and maximized work area.");
            CheckPartialInitializationCleanup();
            await CheckAccountTabs(window, viewport);
            await NativeSessionChecks.Run(_artifacts);
            await CheckRenamedTabUi();
            await CheckNativeRecoveryFocus();

            // Cancel an actual native close request; it must leave the live game intact.
            EventHandler<WindowClosingEventArgs> cancel = (_, e) => e.Cancel = true;
            window.Closing += cancel;
            SendMessage(handle, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
            Program.Check(window.IsVisible && !viewport.IsDisposed && IsWindow(flash), "Cancelled native close destroyed the game.");
            window.Closing -= cancel;

            // Closing before the deferred Flash startup must also be safe.
            var earlyClose = new ShellWindow { ShowInTaskbar = false, ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(-30000, -30000) };
            earlyClose.Show();
            var earlyViewport = earlyClose.SelectedViewport!;
            IntPtr earlyChild = earlyViewport.ContainerHandle;
            Program.Check(earlyChild != IntPtr.Zero, "Early-close fixture did not create its native container.");
            earlyClose.Close();
            await Task.Delay(50);
            Program.Check(earlyViewport.IsDisposed && !IsWindow(earlyChild), "Early close left its container alive.");

            await NativeClick(window, "CloseButton", 20);
            Program.Check(!window.IsVisible && viewport.IsDisposed, "Close did not dispose the session.");
            Program.Check(!IsWindow(handle) && !IsWindow(container) && !IsWindow(flash), "Native handles remain after close.");
            viewport.Dispose();
            File.WriteAllText(Path.Combine(_artifacts, "native-result.json"), JsonSerializer.Serialize(new
            {
                GameLoaded = true, ResizeHitTargets = 8, NativeResizeSizes = 3, MinimumTrackingBounds = true,
                GameFile = viewport.Session.GameFile, RecoverableCallPreservesGame = true,
                DeferredDetachPreservesGame = true, NativePopupAndOwnedDialog = true,
                RenderScaling = window.RenderScaling,
                CaptionHitTargets = true, NativeCaptionInput = true, MaximizedFitsWorkArea = true,
                ImmediateCaptionHover = true, NativeCaptionTooltipsOnly = true,
                TopFrameInsetPixels = topInset, FlashFillsViewport = true, FlashSessionPreserved = true,
                CancelledClosePreservesGame = true, EarlyCloseDisposes = true,
                PartialInitializationCleanup = true, StartupHookReleased = !EoLHook.IsHooked,
                WindowDestroyed = !IsWindow(handle), WinFormsContainerDestroyed = !IsWindow(container),
                ActiveXWindowDestroyed = !IsWindow(flash), ViewportDisposed = viewport.IsDisposed
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PASS: cancelled close, early close, and main close destroyed all owned native handles.");
        }
        catch (Exception exception) { _failure = exception; }
        finally
        {
            viewport.StatusChanged -= Status;
            window.Close();
        }
    }

    private static async Task CheckAccountTabs(ShellWindow window, FlashViewport firstViewport)
    {
        // The resize checks leave the shell at minimum width. Use its default width
        // for two fully visible native tab targets; the game-list check covers overflow.
        window.Width = 1100;
        await Until(() => window.ClientSize.Width == 1100, "Account fixture did not restore the default width.");
        var model = (ShellViewModel)window.DataContext!;
        var tabs = window.FindControl<FATabView>("AccountTabs")!;
        var first = model.SelectedTab!;
        var firstRuntime = first.Session.Runtime!;
        IntPtr firstContainer = firstViewport.ContainerHandle;
        IntPtr firstFlash = FindWindowEx(firstContainer, IntPtr.Zero, null, null);
        var add = tabs.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "AddButton");
        await ClientClick(window, add);
        var second = model.SelectedTab!;
        Program.Check(second != first && model.Tabs.Count == 2, "Native add button did not create/select a game.");
        await WaitReady(second);
        var secondViewport = window.SelectedViewport!;
        var secondRuntime = second.Session.Runtime!;
        IntPtr secondContainer = secondViewport.ContainerHandle;
        IntPtr secondFlash = FindWindowEx(secondContainer, IntPtr.Zero, null, null);
        Program.Check(firstContainer != secondContainer && firstFlash != secondFlash && firstRuntime != secondRuntime,
            "Two tabs share a native container, Flash control, or runtime.");
        // mcLogin is a dynamic MovieClip in the supplied client snapshot. A test-only
        // marker avoids login fields, which the game's frame scripts initialize later.
        const string field = "mcLogin.vsxSessionCheck";
        await Until(() => int.TryParse(firstRuntime.Call("getGameObject", "mcLogin.currentFrame"), out int a) && a > 0 &&
            int.TryParse(secondRuntime.Call("getGameObject", "mcLogin.currentFrame"), out int b) && b > 0,
            "Loaded clients did not construct their login clips.");
        try
        {
            firstRuntime.Call("setGameObject", field, "session-one-check");
            secondRuntime.Call("setGameObject", field, "session-two-check");
            for (int i = 0; i < 4; i++)
            {
                var selected = i % 2 == 0 ? first : second;
                var active = i % 2 == 0 ? firstViewport : secondViewport;
                IntPtr activeFlash = i % 2 == 0 ? firstFlash : secondFlash;
                IntPtr inactiveContainer = i % 2 == 0 ? secondContainer : firstContainer;
                await ClientClick(window, tabs.ContainerFromItem(selected));
                await Until(() => model.SelectedTab == selected && IsWindowVisible(active.ContainerHandle) && !IsWindowVisible(inactiveContainer),
                    $"Native tab selection did not show only the selected game: switch={i}, width={window.ClientSize.Width}, requested={selected.Label}, selected={model.SelectedTab?.Label}, header={tabs.ContainerFromItem(selected)?.Bounds}.");
                await WaitForViewport(window, active.ContainerHandle, activeFlash);
                await Until(() => GetFocus() == activeFlash || IsChild(activeFlash, GetFocus()), "Tab selection did not focus the selected Flash game.");
                Program.Check(ReferenceEquals(firstRuntime, first.Session.Runtime) && ReferenceEquals(secondRuntime, second.Session.Runtime) &&
                    firstViewport.ContainerHandle == firstContainer && secondViewport.ContainerHandle == secondContainer &&
                    IsWindow(firstFlash) && IsWindow(secondFlash), "Tab switching replaced or destroyed a running game.");
                string firstMarker = firstRuntime.Call("getGameObject", field);
                string secondMarker = secondRuntime.Call("getGameObject", field);
                Program.Check(JsonSerializer.Deserialize<string>(firstMarker) == "session-one-check" &&
                    JsonSerializer.Deserialize<string>(secondMarker) == "session-two-check",
                    $"Switching lost test marker state at switch {i}: {firstMarker} / {secondMarker}.");
            }
            await CheckNativeTabDragging(window, tabs, second, first);
            Program.Check(firstViewport.ContainerHandle == firstContainer && secondViewport.ContainerHandle == secondContainer &&
                IsWindow(firstFlash) && IsWindow(secondFlash) && ReferenceEquals(firstRuntime, first.Session.Runtime) &&
                ReferenceEquals(secondRuntime, second.Session.Runtime) &&
                JsonSerializer.Deserialize<string>(firstRuntime.Call("getGameObject", field)) == "session-one-check" &&
                JsonSerializer.Deserialize<string>(secondRuntime.Call("getGameObject", field)) == "session-two-check",
                "Dragging a tab replaced a live game or lost its state.");
        }
        finally
        {
            firstRuntime.Call("setGameObject", field, null!);
            secondRuntime.Call("setGameObject", field, null!);
        }

        await CheckKeyboardTabFocus(window, tabs, first, second, firstFlash, secondFlash);
        await CheckNativeGameList(window, first, second, firstFlash, secondFlash);
        CheckNativeIcons(window);
        await CheckTabToolTips(window, tabs, secondFlash);

        Task cancelled = window.RequestCloseTabAsync(second);
        var dialog = window.CloseConfirmation!;
        await Until(() => dialog.IsVisible && dialog.TryGetPlatformHandle() is not null, "Close confirmation did not open.");
        IntPtr dialogHandle = dialog.TryGetPlatformHandle()!.Handle;
        IntPtr ownerHandle = window.TryGetPlatformHandle()!.Handle;
        // CenterOwner may clamp an off-screen dialog to a monitor; move only our own test window over the game.
        var gameBounds = WindowRect(secondFlash);
        SetWindowPos(dialogHandle, IntPtr.Zero, gameBounds.Left + 40, gameBounds.Top + 40, 0, 0, 0x0015);
        Program.Check(dialogHandle != ownerHandle && IsAbove(dialogHandle, ownerHandle) && Overlaps(WindowRect(dialogHandle), WindowRect(secondFlash)),
            "Actual close confirmation does not cover the native Flash surface.");
        Program.Check(dialog.WindowDecorations == WindowDecorations.BorderOnly && dialog.ExtendClientAreaToDecorationsHint &&
            !dialog.FindChrome<Button>("MinimizeButton")!.IsVisible && !dialog.FindChrome<Button>("MaximizeButton")!.IsVisible,
            "Confirmation did not use the shared close-only native chrome.");
        CheckTopFrame(dialogHandle);
        var dragPoint = await ChromeTarget(dialog, "TitleDragArea", 2);
        SendMessage(dialogHandle, 0x00A3 /* WM_NCLBUTTONDBLCLK */, new IntPtr(2), Pack(dragPoint.X, dragPoint.Y));
        Program.Check(!IsZoomed(dialogHandle), "Double-click maximized a fixed confirmation dialog.");
        await CheckCaptionHover(dialog);
        await NativeClick(dialog, "CloseButton", 20);
        await cancelled;
        Program.Check(model.Tabs.Count == 2 && IsWindow(firstFlash) && IsWindow(secondFlash), "Cancelled confirmation destroyed a live game.");
        await Until(() => GetFocus() == secondFlash || IsChild(secondFlash, GetFocus()), "Cancelling confirmation did not restore game focus.");

        second.Session.Messenger.Send(new FlashRuntimeFailedMessage(new COMException("Tab isolation check")));
        await Until(() => second.HasError, "Injected tab failure was not reported.");
        await Until(() => window.FindControl<Button>("RetryGameButton")!.IsFocused && GetFocus() == ownerHandle,
            $"Failure did not focus Retry: active={window.IsActive}, captured={second.Session.HadFocusOnFailure}, focused={window.FocusManager?.GetFocusedElement()}, native={GetFocus()}, expected={ownerHandle}, header={tabs.IsKeyboardFocusWithin}.");
        Program.Check(!IsWindow(secondContainer) && !IsWindow(secondFlash) && IsWindow(firstFlash) && firstRuntime.Call<bool>("isTrue"),
            "Failure did not clean up just the affected native game.");
        var failed = second.Session;
        NativeKey(ownerHandle, 0x20 /* Space activates the focused Retry button */);
        await Until(() => second.Session != failed, "Keyboard activation of the focused Retry action did not restart the game.");
        await WaitReady(second);
        var replacement = window.SelectedViewport!;
        IntPtr replacementContainer = replacement.ContainerHandle;
        IntPtr replacementFlash = FindWindowEx(replacementContainer, IntPtr.Zero, null, null);
        Program.Check(failed.State == GameSessionState.Disposed && second.Session != failed && second.Session.Runtime != secondRuntime &&
            IsWindow(replacementFlash) && IsWindow(firstFlash) && ReferenceEquals(firstRuntime, first.Session.Runtime),
            "Retry replaced the wrong session or failed to start a fresh game.");
        Task confirmed = window.RequestCloseTabAsync(second);
        window.CloseConfirmation!.FindControl<Button>("ConfirmGameClose")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await confirmed;
        await Until(() => model.SelectedTab == first && IsWindowVisible(firstContainer), "Closing the selected tab did not restore the remaining game.");
        await WaitForViewport(window, firstContainer, firstFlash);
        Program.Check(!IsWindow(replacementContainer) && !IsWindow(replacementFlash) && replacement.IsDisposed && firstRuntime.Call<bool>("isTrue"),
            "Confirmed tab close affected the surviving game or leaked its replacement.");
        // Leave a second live session for the existing cancelled-close and final application-shutdown checks.
        model.OpenGameCommand.Execute(null);
        await WaitReady(model.SelectedTab!);
        var background = window.SelectedViewport!;
        IntPtr backgroundContainer = background.ContainerHandle;
        IntPtr backgroundFlash = FindWindowEx(backgroundContainer, IntPtr.Zero, null, null);
        model.SelectedTab = first;
        await WaitForViewport(window, firstContainer, firstFlash);
        window.Closed += (_, _) => Program.Check(background.IsDisposed && !IsWindow(backgroundContainer) && !IsWindow(backgroundFlash),
            "Application close leaked an inactive live game.");
        File.WriteAllText(Path.Combine(_artifacts, "native-account-tabs.json"), JsonSerializer.Serialize(new
        {
            TwoRealGames = true, SwitchesPreserveHandlesRuntimeAndState = true, InactiveCallsIsolated = true,
            OnlySelectedGameVisible = true, NativeTabAndAddInput = true, SelectedFlashKeyboardFocus = true,
            ConfirmationAboveFlash = true, CancelPreservesGamesAndFocus = true, FailureAndRetryIsolated = true,
            ToolTipsFollowCurrentTabAboveFlashWithoutTakingFocus = true,
            ConfirmedCloseDisposesOnlyItsGame = true, BackgroundCleanupCheckedOnApplicationClose = true,
            FirstGameFile = first.Session.GameFile, SecondGameFile = second.Session.GameFile
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS: two real games preserve state/handles across native tab input; focus, confirmation, failure, retry, and close are isolated.");
    }

    private static async Task CheckTabToolTips(ShellWindow window, FATabView tabs, IntPtr flash)
    {
        Control[] owners = [tabs.ContainerFromIndex(0), tabs.ContainerFromIndex(1)];
        object?[] originalTips = owners.Select(ToolTip.GetTip).ToArray();
        var tips = new[] { new ToolTip { Content = "First game" }, new ToolTip { Content = "Second game" } };
        IntPtr handle = window.TryGetPlatformHandle()!.Handle;
        var previous = WindowRect(handle);
        var workArea = (window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary ?? window.Screens.All.First()).WorkingArea;
        IntPtr focus = GetFocus();
        // Keep the test-owned window within a monitor for this check: off-screen
        // owners would intentionally clamp both tooltips to the same screen edge.
        SetWindowPos(handle, IntPtr.Zero, workArea.X + 80, workArea.Y + 80, 0, 0, 0x0015);
        try
        {
            for (int i = 0; i < owners.Length; i++) ToolTip.SetTip(owners[i], tips[i]);
            ToolTip.SetShowDelay(owners[0], 0);
            PixelPoint? firstPosition = null;
            for (int i = 0; i < owners.Length; i++)
            {
                var owner = owners[i];
                var screen = owner.PointToScreen(new Point(20, 20));
                var origin = new NativePoint();
                ClientToScreen(handle, ref origin);
                SendMessage(handle, 0x0200 /* WM_MOUSEMOVE */, IntPtr.Zero, Pack(screen.X - origin.X, screen.Y - origin.Y));
                var tip = tips[i];
                await Until(() => ToolTip.GetIsOpen(owner) && TopLevel.GetTopLevel(tip) is PopupRoot,
                    "Tab tooltip did not open a native popup.");
                await Until(() =>
                {
                    var expected = owner.PointToScreen(new Point(owner.Bounds.Width / 2, owner.Bounds.Height));
                    var actual = tip.PointToScreen(new Point(tip.Bounds.Width / 2, 0));
                    return Math.Abs(actual.X - expected.X) <= 1 &&
                        Math.Abs(actual.Y - expected.Y - 8 * window.RenderScaling) <= 1;
                }, "Native tooltip did not anchor beneath its current tab.");
                var root = (PopupRoot)TopLevel.GetTopLevel(tip)!;
                IntPtr popup = root.TryGetPlatformHandle()!.Handle;
                Program.Check(IsAbove(popup, handle) && Overlaps(WindowRect(popup), WindowRect(flash)),
                    "Tab tooltip is not above the native Flash surface.");
                Program.Check(GetFocus() == focus, "Showing or switching tab tooltips stole game keyboard focus.");
                var position = tip.PointToScreen(default);
                if (i == 0) firstPosition = position;
                else Program.Check(!ToolTip.GetIsOpen(owners[0]) && position.X > firstPosition!.Value.X + 80,
                    "Tooltip content switched at its previous owner's position.");
            }
            Console.WriteLine("PASS: native tooltips move to each current tab above Flash without taking game focus.");
        }
        finally
        {
            for (int i = 0; i < owners.Length; i++)
            {
                ToolTip.SetIsOpen(owners[i], false);
                ToolTip.SetTip(owners[i], originalTips[i]);
            }
            owners[0].ClearValue(ToolTip.ShowDelayProperty);
            SetWindowPos(handle, IntPtr.Zero, previous.Left, previous.Top, 0, 0, 0x0015);
        }
    }

    private static async Task WaitReady(GameTabViewModel tab)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(35);
        while (tab.Session.State != GameSessionState.Ready)
        {
            if (tab.HasError) throw new Exception(tab.ErrorMessage);
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Another real game did not load within 35 seconds.");
            await Task.Delay(20);
        }
    }

    private static async Task ClientClick(TopLevel window, Control control)
    {
        PixelPoint screen = default;
        await Until(() =>
        {
            screen = control.PointToScreen(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2));
            return control.Bounds.Width > 0 && HitTest(window.TryGetPlatformHandle()!.Handle, screen.X, screen.Y) == 1;
        }, "Tab/add control is not a native client input target.");
        var origin = new NativePoint();
        IntPtr handle = window.TryGetPlatformHandle()!.Handle;
        ClientToScreen(handle, ref origin);
        var position = Pack(screen.X - origin.X, screen.Y - origin.Y);
        SendMessage(handle, 0x0200, IntPtr.Zero, position);
        SendMessage(handle, 0x0201, new IntPtr(1), position);
        SendMessage(handle, 0x0202, IntPtr.Zero, position);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static async Task CheckNativeOverlays(ShellWindow owner, FlashViewport viewport, IntPtr flash)
    {
        // Native popups/owned windows have their own surface; an in-window overlay cannot cover Flash.
        var popup = new Popup
        {
            PlacementTarget = viewport, Placement = PlacementMode.Center,
            PlacementConstraintAdjustment = PopupPositionerConstraintAdjustment.None,
            ShouldUseOverlayLayer = false,
            Child = new Border { Width = 240, Height = 100, Background = Avalonia.Media.Brushes.DarkSlateGray }
        };
        var parent = (Panel)viewport.Parent!;
        parent.Children.Add(popup);
        IntPtr ownerHandle = owner.TryGetPlatformHandle()!.Handle;
        try
        {
            popup.IsOpen = true;
            await Until(() => TopLevel.GetTopLevel(popup.Child) is PopupRoot, "Popup did not create a native root.");
            var root = (PopupRoot)TopLevel.GetTopLevel(popup.Child)!;
            IntPtr popupHandle = root.TryGetPlatformHandle()!.Handle;
            Program.Check(!popup.IsUsingOverlayLayer && popupHandle != ownerHandle && IsAbove(popupHandle, ownerHandle),
                "Popup was not above the Flash owner's native surface.");
            Program.Check(Overlaps(WindowRect(popupHandle), WindowRect(flash)), "Popup did not overlap real Flash.");
        }
        finally { popup.IsOpen = false; parent.Children.Remove(popup); }

        var bounds = WindowRect(flash);
        var dialog = new Window
        {
            Width = 300, Height = 150, ShowInTaskbar = false, ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Position = new PixelPoint(bounds.Left + 40, bounds.Top + 40),
            Content = new TextBlock { Text = "Native dialog integration check" }
        };
        Task closed = dialog.ShowDialog(owner);
        try
        {
            await Until(() => dialog.IsVisible, "Owned dialog did not open.");
            IntPtr dialogHandle = dialog.TryGetPlatformHandle()!.Handle;
            Program.Check(GetWindow(dialogHandle, 4 /* GW_OWNER */) == ownerHandle && IsAbove(dialogHandle, ownerHandle),
                "Owned dialog was not above the Flash owner's native surface.");
            Program.Check(Overlaps(WindowRect(dialogHandle), WindowRect(flash)) && IsWindow(flash),
                "Owned dialog did not overlap the live Flash surface.");
        }
        finally { dialog.Close(); await closed; }
        Program.Check(viewport.Session.Runtime!.Call<bool>("isTrue"), "Closing native UI invalidated Flash.");
        Console.WriteLine("PASS: native popup and owned dialog overlap real Flash above its owner; closing them preserves the runtime.");
    }

    private static bool Overlaps(NativeRect a, NativeRect b) => a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom && b.Top < a.Bottom;
    private static bool IsAbove(IntPtr target, IntPtr owner)
    {
        for (IntPtr current = GetWindow(owner, 3 /* GW_HWNDPREV */); current != IntPtr.Zero; current = GetWindow(current, 3))
            if (current == target) return true;
        return false;
    }

    private static void CheckPartialInitializationCleanup()
    {
        using var panel = new System.Windows.Forms.Panel();
        panel.CreateControl();
        var messenger = new WeakReferenceMessenger();
        var recipient = new object();
        AxShockwaveFlash? control = null;
        messenger.Register<FlashChangedMessage<AxShockwaveFlash>>(recipient, (_, message) =>
        {
            control = message.Flash;
            throw new InvalidOperationException("Injected initialization failure");
        });
        using var runtime = new FlashUtil(messenger);
        try
        {
            runtime.InitializeFlash(panel);
            throw new Exception("Injected startup failure was swallowed.");
        }
        catch (InvalidOperationException error) when (error.InnerException?.Message == "Injected initialization failure") { }
        Program.Check(control is { IsDisposed: true } && panel.Controls.Count == 0,
            "Partial initialization did not release the owned ActiveX control.");
        Program.Check(!EoLHook.IsHooked, "Partial initialization left the system-time hook installed.");
        runtime.Dispose();
        Console.WriteLine("PASS: partial Flash initialization releases the control and startup hook; disposal is repeat-safe.");
    }

    private static async Task CheckStartupFailure(ShellWindow window, FlashViewport viewport, IntPtr handle, Task loaded)
    {
        IntPtr container = viewport.ContainerHandle;
        try
        {
            await loaded.WaitAsync(TimeSpan.FromSeconds(10));
            throw new InvalidOperationException("A missing SWF unexpectedly loaded the game.");
        }
        catch (Exception error) when (error.Message.Contains("bundled skua.swf is missing")) { }
        await Until(() => viewport.Session.State == GameSessionState.Failed && window.FindControl<Grid>("GameErrorPanel")!.IsVisible,
            "Startup failure did not dispose the session and display the error.");
        Program.Check(!IsWindow(container) && viewport.ContainerHandle == IntPtr.Zero,
            "Startup failure left its WinForms container alive.");
        Program.Check(window.FindControl<TextBlock>("GameErrorMessage")!.Text!.Contains("bundled skua.swf is missing"),
            "The actual startup error was not shown.");
        Program.Check(((string?)ToolTip.GetTip(window.FindControl<DropDownButton>("GameListButton")!))?.Contains("Client: Game unavailable") == true, "Failure status tooltip was not updated.");
        window.Width = 900;
        window.Height = 600;
        await Task.Delay(50);
        await NativeClick(window, "CloseButton", 20);
        Program.Check(!IsWindow(handle), "Failed-startup window did not close.");
        File.WriteAllText(Path.Combine(_artifacts, "native-startup-failure.json"), JsonSerializer.Serialize(new
        {
            MissingSwfReported = true, ErrorVisible = true, ContainerDestroyed = !IsWindow(container),
            ViewportDisposed = viewport.IsDisposed, WindowDestroyed = !IsWindow(handle)
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS: missing-SWF startup error was displayed; container and main window closed cleanly.");
    }

    private static void SaveChromeCapture(IntPtr handle, double scale)
    {
        var rect = WindowRect(handle);
        using var bitmap = new System.Drawing.Bitmap(rect.Width, rect.Height);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            IntPtr dc = graphics.GetHdc();
            try { Program.Check(PrintWindow(handle, dc, 2), "Native title-bar capture failed."); }
            finally { graphics.ReleaseHdc(dc); }
        }
        // Capture only window chrome; never save the game's remembered account fields.
        using var chrome = bitmap.Clone(new System.Drawing.Rectangle(0, 0, rect.Width, (int)Math.Ceiling(41 * scale)), bitmap.PixelFormat);
        chrome.Save(Path.Combine(_artifacts, "native-titlebar.png"), System.Drawing.Imaging.ImageFormat.Png);
    }

    private static int CheckTopFrame(IntPtr handle)
    {
        var rect = WindowRect(handle);
        var origin = new NativePoint();
        Program.Check(ClientToScreen(handle, ref origin), "Cannot read client origin.");
        int inset = origin.Y - rect.Top;
        int border = GetSystemMetricsForDpi(6 /* SM_CYBORDER */, GetDpiForWindow(handle));
        Program.Check(inset >= 0 && inset <= border,
            $"Top frame reserves {inset}px; only the {border}px Windows border should remain.");
        return inset;
    }

    private static async Task WaitForViewport(ShellWindow window, IntPtr container, IntPtr flash)
    {
        var viewport = window.SelectedViewport!;
        await Until(() =>
        {
            window.UpdateLayout();
            if (!IsWindow(container) || !IsWindow(flash)) return false;
            var expected = viewport.PointToScreen(default);
            var actual = WindowRect(container);
            var activeX = WindowRect(flash);
            return actual.Left == expected.X && actual.Top == expected.Y &&
                Math.Abs(actual.Width - viewport.Bounds.Width * window.RenderScaling) <= 1 &&
                Math.Abs(actual.Height - viewport.Bounds.Height * window.RenderScaling) <= 1 &&
                actual.Equals(activeX);
        }, "WinForms/ActiveX bounds did not follow the viewport.");
        Program.Check(viewport.ContainerHandle == container && !viewport.IsDisposed, "Resize replaced/disposed the game session.");
        Program.Check(viewport.Bounds.Y == 40 && viewport.Bounds.Height > 0, "Title bar overlaps or hides the game.");
    }

    private static void CheckMinimumSize(ShellWindow window, IntPtr handle)
    {
        var info = new MinMaxInfo();
        IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf<MinMaxInfo>());
        try
        {
            Marshal.StructureToPtr(info, memory, false);
            SendMessage(handle, 0x0024 /* WM_GETMINMAXINFO */, IntPtr.Zero, memory);
            info = Marshal.PtrToStructure<MinMaxInfo>(memory);
            var frame = WindowRect(handle);
            GetClientRect(handle, out var client);
            Program.Check(info.MinTrackSize.X >= window.MinWidth * window.RenderScaling + frame.Width - client.Width &&
                info.MinTrackSize.Y >= window.MinHeight * window.RenderScaling + frame.Height - client.Height,
                "Windows resize tracking does not enforce the shell's minimum size.");
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    private static async Task CheckCaptionHover(Window window)
    {
        IntPtr handle = window.TryGetPlatformHandle()!.Handle;
        (string Name, int Role)[] targets = [("CloseButton", 20), ("MaximizeButton", 9), ("MinimizeButton", 8), ("CloseButton", 20)];
        foreach (var target in targets)
        {
            var button = window.FindChrome<Button>(target.Name)!;
            if (!button.IsVisible) continue;
            var point = await ChromeTarget(window, target.Name, target.Role);
            SendMessage(handle, 0x00A0 /* WM_NCMOUSEMOVE */, new IntPtr(target.Role), Pack(point.X, point.Y));
            Program.Check(button.IsPointerOver, $"Native hover did not reach {target.Name}.");
            CaptionChecks.AssertBackground(button, true);
            Program.Check(ToolTip.GetTip(button) is null, "Caption has a duplicate Avalonia tooltip.");
            SendMessage(handle, 0x02A2 /* WM_NCMOUSELEAVE */, IntPtr.Zero, IntPtr.Zero);
            Program.Check(!button.IsPointerOver, $"Native leave did not clear {target.Name}.");
            CaptionChecks.AssertBackground(button, false);
        }
        Console.WriteLine("PASS: native caption hover/leave updates immediately; only native caption tooltips remain.");
    }

    private static async Task CheckCaptionTargets(ShellWindow window)
    {
        await ChromeTarget(window, "TitleDragArea", 2);
        await ChromeTarget(window, "MinimizeButton", 8);
        await ChromeTarget(window, "MaximizeButton", 9);
        await ChromeTarget(window, "CloseButton", 20);
    }

    private static async Task<PixelPoint> ChromeTarget(Window window, string name, int role)
    {
        var control = window.FindControl<Control>(name) ?? window.FindChrome<Control>(name)!;
        PixelPoint point = default;
        // Avalonia's chrome hit test uses GetVisualAt: wait for the committed input scene,
        // including the updated positions after maximize/restore, rather than a fixed sleep.
        await Until(() =>
        {
            point = control.PointToScreen(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2));
            return HitTest(window.TryGetPlatformHandle()!.Handle, point.X, point.Y) == role;
        }, $"{name} is not recognized as native window chrome.");
        return point;
    }

    private static async Task NativeClick(Window window, string name, int role)
    {
        var point = await ChromeTarget(window, name, role);
        IntPtr handle = window.TryGetPlatformHandle()!.Handle;
        SendMessage(handle, 0x00A1 /* WM_NCLBUTTONDOWN */, new IntPtr(role), Pack(point.X, point.Y));
        SendMessage(handle, 0x00A2 /* WM_NCLBUTTONUP */, new IntPtr(role), Pack(point.X, point.Y));
    }

    private static void CheckResizeHitTargets(IntPtr handle)
    {
        var r = WindowRect(handle);
        int x = (r.Left + r.Right) / 2, y = (r.Top + r.Bottom) / 2;
        (string Name, int X, int Y, int Expected)[] targets =
        [
            ("left", r.Left + 1, y, 10), ("right", r.Right - 2, y, 11),
            ("top", x, r.Top + 1, 12), ("bottom", x, r.Bottom - 2, 15),
            ("top-left", r.Left + 1, r.Top + 1, 13), ("top-right", r.Right - 2, r.Top + 1, 14),
            ("bottom-left", r.Left + 1, r.Bottom - 2, 16), ("bottom-right", r.Right - 2, r.Bottom - 2, 17)
        ];
        foreach (var target in targets)
        {
            int result = HitTest(handle, target.X, target.Y);
            Program.Check(result == target.Expected,
                $"The {target.Name} edge cannot resize: native hit-test returned {result}, expected {target.Expected}.");
        }
    }

    internal static async Task Until(Func<bool> condition, string message)
    {
        for (int i = 0; i < 100; i++)
        {
            if (condition()) return;
            await Task.Delay(20);
        }
        throw new InvalidOperationException(message);
    }

    private static int HitTest(IntPtr handle, int x, int y) =>
        (int)SendMessage(handle, 0x0084 /* WM_NCHITTEST */, IntPtr.Zero, Pack(x, y));
    private static IntPtr Pack(int x, int y) => new(unchecked((y << 16) | (x & 0xffff)));
    private static NativeRect WindowRect(IntPtr handle)
    {
        Program.Check(GetWindowRect(handle, out var rect), "Cannot read native window bounds.");
        return rect;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo { public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr handle, out NativeRect rect);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr handle, ref NativePoint point);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(IntPtr handle);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr handle);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr handle);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr handle);
    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll")]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? windowName);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr handle, uint command);
    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr handle);
    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsChild(IntPtr parent, IntPtr child);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr handle, IntPtr deviceContext, uint flags);
}
