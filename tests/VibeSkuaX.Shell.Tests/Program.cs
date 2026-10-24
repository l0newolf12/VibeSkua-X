using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Messaging;
using VibeSkuaX.App.Controls;
using VibeSkuaX.App.Features.Shell;

namespace VibeSkuaX.Shell.Tests;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            string artifacts = Path.GetFullPath("artifacts/shell-tests");
            Directory.CreateDirectory(artifacts);
            if (args.Contains("--empty-tab-shortcuts")) return UiRegressionChecks.EmptyShortcuts();
            if (!args.Contains("--native-worker")) As3BuildChecks.Run();
            if (args.Contains("--native-worker")) return NativeSmoke.Run(artifacts, args.Contains("--missing-swf"));
            if (args.Contains("--native")) return RunNativeProcess(artifacts);
            UiRegressionChecks.RunShortcutWorker();
            FlashChecks.Run();
            RunHeadless(artifacts).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static int RunNativeProcess(string artifacts)
    {
        File.Delete(Path.Combine(artifacts, "native-result.json"));
        File.Delete(Path.Combine(artifacts, "native-startup-failure.json"));
        File.Delete(Path.Combine(artifacts, "native-titlebar.png"));
        File.Delete(Path.Combine(artifacts, "native-account-tabs.json"));
        RunWorker(Environment.ProcessPath!, "--native-worker");

        // Isolate the missing-file case: never rename files in a developer's build output.
        string fixture = Path.Combine(artifacts, "missing-swf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        try
        {
            foreach (string file in Directory.EnumerateFiles(AppContext.BaseDirectory, "*", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(file).Equals("skua.swf", StringComparison.OrdinalIgnoreCase)) continue;
                string destination = Path.Combine(fixture, Path.GetRelativePath(AppContext.BaseDirectory, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
            RunWorker(Path.Combine(fixture, Path.GetFileName(Environment.ProcessPath!)), "--native-worker --missing-swf");
        }
        finally { Directory.Delete(fixture, recursive: true); }
        return 0;
    }

    private static void RunWorker(string executable, string arguments)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            executable, arguments)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        }) ?? throw new Exception("Could not start the native test process.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(180000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
            throw new TimeoutException("Native checks or application shutdown exceeded 180 seconds.");
        }
        Console.Write(output.GetAwaiter().GetResult());
        Console.Error.Write(errors.GetAwaiter().GetResult());
        Check(process.ExitCode == 0, $"Native test process exited with {process.ExitCode}.");
        Console.WriteLine("PASS: native test process exited within the deadline.");
    }

    private static async Task RunHeadless(string artifacts)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessAppBuilder));
        await session.Dispatch(() =>
        {
            var window = new ShellWindow();
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var title = window.FindControl<Border>("TitleDragArea")!;
                var viewport = window.SelectedViewport!;
                Check(title.TranslatePoint(default, window)!.Value.Y == 0, "Title bar does not start at the top edge.");
                Check(window.FindChrome<Border>("TitleBar")!.Bounds.Height == 40, "Unexpected title-bar height.");
                Check(viewport.Bounds.Y == 40, "Game viewport is not immediately below the title bar.");
                Check(viewport.ContainerHandle == IntPtr.Zero, "Headless checks must not create ActiveX controls.");
                using var frame = window.CaptureRenderedFrame() ?? throw new Exception("No headless frame was rendered.");
                frame.Save(Path.Combine(artifacts, "shell.png"));
                Console.WriteLine("PASS: borderless layout; saved a headless shell render.");
                CaptionChecks.RunHeadless(window);
                CheckSharedStyles(window);
                CheckCaptionFocus(window);

                Click(window, "MaximizeButton");
                Check(window.WindowState == WindowState.Maximized, "Maximize button did not maximize.");
                Check((string?)window.FindChrome<Button>("MaximizeButton")!.Content == "\uE923", "Restore icon was not updated.");
                Click(window, "MaximizeButton");
                Check(window.WindowState == WindowState.Normal, "Restore button did not restore.");
                Click(window, "MinimizeButton");
                Check(window.WindowState == WindowState.Minimized, "Minimize button did not minimize.");
                window.WindowState = WindowState.Normal;
                Console.WriteLine("PASS: mouse-driven maximize, restore, and minimize controls.");

                var viewModel = (ShellViewModel)window.DataContext!;
                viewModel.SelectedTab!.Session.Messenger.Send(new FlashRuntimeFailedMessage(new Exception("Test runtime failure")));
                Dispatcher.UIThread.RunJobs();
                Check(!viewport.IsVisible, "Viewport remained visible after a runtime error.");
                Check(window.FindControl<Grid>("GameErrorPanel")!.IsVisible, "Runtime error was not shown.");
                Check(window.FindControl<TextBlock>("GameErrorMessage")!.Text!.Contains("Test runtime failure"), "Error binding did not update.");
                Check(((string?)ToolTip.GetTip(window.FindControl<DropDownButton>("GameListButton")!))?.Contains("Client: Game unavailable") == true, "Game-list status tooltip did not update.");
                Console.WriteLine("PASS: runtime error and status bindings.");

                EventHandler<WindowClosingEventArgs> cancel = (_, e) => e.Cancel = true;
                window.Closing += cancel;
                Click(window, "CloseButton");
                Check(window.IsVisible && !viewport.IsDisposed, "Cancelled close disposed the session.");
                window.Closing -= cancel;
                Click(window, "CloseButton");
                Check(!window.IsVisible && viewport.IsDisposed, "Close button did not close and dispose the viewport.");
                viewport.Dispose();
                Console.WriteLine("PASS: cancelled close, actual close, and repeat-safe disposal.");
            }
            finally { window.Close(); }
        }, CancellationToken.None);
        await session.Dispatch(() => TabChecks.RunHeadless(artifacts), CancellationToken.None);
        await session.Dispatch(() => ToolTipChecks.RunHeadless(), CancellationToken.None);
        await session.Dispatch(() => SharedChromeChecks.RunHeadless(artifacts), CancellationToken.None);
        await session.Dispatch(() => UiRegressionChecks.RunHeadless(artifacts), CancellationToken.None);
        await session.Dispatch(() => ShellPolishChecks.RunHeadless(artifacts), CancellationToken.None);
    }

    private static void CheckSharedStyles(ShellWindow window)
    {
        var titleBar = window.FindChrome<Border>("TitleBar")!;
        IBrush? original = titleBar.Background;
        var replacement = new SolidColorBrush(Color.Parse("#304050"));
        try
        {
            Application.Current!.Resources["VsBrushTitleBar"] = replacement;
            Dispatcher.UIThread.RunJobs();
            Check(ReferenceEquals(titleBar.Background, replacement), "Shared brush changes did not reach the rendered shell.");
        }
        finally
        {
            Application.Current!.Resources.Remove("VsBrushTitleBar");
            Dispatcher.UIThread.RunJobs();
        }
        Check(ReferenceEquals(titleBar.Background, original), "Removing an override did not restore the shared brush.");

        Button[] buttons = [window.FindChrome<Button>("MinimizeButton")!, window.FindChrome<Button>("MaximizeButton")!, window.FindChrome<Button>("CloseButton")!];
        Rect[] bounds = buttons.Select(button => button.Bounds).ToArray();
        void CheckGeometry()
        {
            for (int i = 0; i < buttons.Length; i++)
                Check(buttons[i].Bounds == bounds[i], "Caption state changed its size or shifted neighboring controls.");
        }
        Button target = buttons[1];
        var point = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        Dispatcher.UIThread.RunJobs();
        CheckGeometry();
        window.MouseDown(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        CheckGeometry();
        window.MouseUp(point, MouseButton.Left);
        window.WindowState = WindowState.Normal;
        Dispatcher.UIThread.RunJobs();
        CheckGeometry();
        target.IsEnabled = false;
        Dispatcher.UIThread.RunJobs();
        CheckGeometry();
        target.IsEnabled = true;
        window.MouseMove(new Point(0, 100));
        Dispatcher.UIThread.RunJobs();
        Console.WriteLine("PASS: shared brush updates/restoration and stable caption geometry across interaction states.");
    }

    private static void CheckCaptionFocus(ShellWindow window)
    {
        var panel = (Panel)window.Content!;
        var editor = new TextBox();
        Grid.SetRow(editor, 1);
        panel.Children.Add(editor);
        try
        {
            Dispatcher.UIThread.RunJobs();
            Check(editor.Focus(), "Could not focus content for the caption regression check.");
            foreach (string name in new[] { "MinimizeButton", "MaximizeButton", "CloseButton" })
                Check(!window.FindChrome<Button>(name)!.Focus(), "Caption button accepted keyboard focus.");
            Click(window, "MaximizeButton");
            Check(editor.IsFocused, "Maximizing moved keyboard focus out of the content.");
            Click(window, "MaximizeButton");
            Check(editor.IsFocused, "Restoring moved keyboard focus out of the content.");
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Check(window.WindowState == WindowState.Normal && editor.IsFocused, "Space reactivated a caption button after clicking it.");
        }
        finally { panel.Children.Remove(editor); }
        Console.WriteLine("PASS: caption buttons cannot take focus; maximize/restore and Space preserve content focus.");
    }

    private static void Click(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        var button = window.FindChrome<Button>(name) ?? throw new Exception("Button not found: " + name);
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)
            ?? throw new Exception("Button is not attached: " + name);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    internal static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

public static class HeadlessAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .UseSkia();
}
