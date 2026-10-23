using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AxShockwaveFlashObjects;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Messaging;
using VibeSkuaX.App.Controls;
using VibeSkuaX.App.Features.Shell;
using VibeSkuaX.App.Sessions;
using VibeSkuaX.Runtime.Windows.Flash;

namespace VibeSkuaX.Shell.Tests;

/// <summary>Opt-in checks against our own off-screen Windows/ActiveX window; no desktop input.</summary>
internal static class NativeSmoke
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
        var viewport = window.FindControl<FlashViewport>("GameViewport")!;
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
            await WaitForViewport(window, container, flash);
            var workArea = window.Screens.ScreenFromWindow(window)!.WorkingArea;
            GetClientRect(handle, out var maxClient);
            var origin = new NativePoint();
            ClientToScreen(handle, ref origin);
            Program.Check(origin.X == workArea.X && origin.Y == workArea.Y &&
                maxClient.Width == workArea.Width && maxClient.Height == workArea.Height,
                $"Maximized client does not fit the work area: {origin.X},{origin.Y} {maxClient.Width}x{maxClient.Height}; expected {workArea}.");
            await NativeClick(window, "MaximizeButton", 9);
            await Until(() => !IsZoomed(handle) && window.WindowState == WindowState.Normal, "Restore button did not restore.");
            await WaitForViewport(window, container, flash);
            Program.Check(WindowRect(handle).Equals(restoreBounds), "Restore did not preserve normal window bounds.");
            await NativeClick(window, "MinimizeButton", 8);
            await Until(() => IsIconic(handle) && window.WindowState == WindowState.Minimized, "Minimize button did not minimize.");
            window.WindowState = WindowState.Normal;
            await Until(() => !IsIconic(handle), "Window did not restore after minimizing.");
            await WaitForViewport(window, container, flash);
            Console.WriteLine("PASS: native title-bar double-click, caption button input, minimize/restore, and maximized work area.");
            CheckPartialInitializationCleanup();
            await NativeSessionChecks.Run(_artifacts);

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
            var earlyViewport = earlyClose.FindControl<FlashViewport>("GameViewport")!;
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
        await Until(() => viewport.Session.State == GameSessionState.Failed && window.FindControl<StackPanel>("GameErrorPanel")!.IsVisible,
            "Startup failure did not dispose the session and display the error.");
        Program.Check(!IsWindow(container) && viewport.ContainerHandle == IntPtr.Zero,
            "Startup failure left its WinForms container alive.");
        Program.Check(window.FindControl<TextBlock>("GameErrorMessage")!.Text!.Contains("bundled skua.swf is missing"),
            "The actual startup error was not shown.");
        Program.Check(window.FindControl<TextBlock>("StatusText")!.Text == "Game unavailable", "Failure status was not shown.");
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
        var viewport = window.FindControl<FlashViewport>("GameViewport")!;
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

    private static async Task CheckCaptionTargets(ShellWindow window)
    {
        await ChromeTarget(window, "TitleDragArea", 2);
        await ChromeTarget(window, "MinimizeButton", 8);
        await ChromeTarget(window, "MaximizeButton", 9);
        await ChromeTarget(window, "CloseButton", 20);
    }

    private static async Task<PixelPoint> ChromeTarget(ShellWindow window, string name, int role)
    {
        var control = window.FindControl<Control>(name)!;
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

    private static async Task NativeClick(ShellWindow window, string name, int role)
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
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr handle, IntPtr deviceContext, uint flags);
}
