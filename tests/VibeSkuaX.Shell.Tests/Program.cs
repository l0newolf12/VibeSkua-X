using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
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
            if (!args.Contains("--native-worker")) As3BuildChecks.Run();
            if (args.Contains("--native-worker")) return NativeSmoke.Run(artifacts, args.Contains("--missing-swf"));
            if (args.Contains("--native")) return RunNativeProcess(artifacts);
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
        if (!process.WaitForExit(70000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
            throw new TimeoutException("Native checks or application shutdown exceeded 70 seconds.");
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
                var viewport = window.FindControl<FlashViewport>("GameViewport")!;
                Check(title.TranslatePoint(default, window)!.Value.Y == 0, "Title bar does not start at the top edge.");
                Check(window.FindControl<Border>("TitleBar")!.Bounds.Height == 40, "Unexpected title-bar height.");
                Check(viewport.Bounds.Y == 40, "Game viewport is not immediately below the title bar.");
                Check(viewport.ContainerHandle == IntPtr.Zero, "Headless checks must not create ActiveX controls.");
                using var frame = window.CaptureRenderedFrame() ?? throw new Exception("No headless frame was rendered.");
                frame.Save(Path.Combine(artifacts, "shell.png"));
                Console.WriteLine("PASS: borderless layout; saved a headless shell render.");

                Click(window, "MaximizeButton");
                Check(window.WindowState == WindowState.Maximized, "Maximize button did not maximize.");
                Check((string?)window.FindControl<Button>("MaximizeButton")!.Content == "\uE923", "Restore icon was not updated.");
                Click(window, "MaximizeButton");
                Check(window.WindowState == WindowState.Normal, "Restore button did not restore.");
                Click(window, "MinimizeButton");
                Check(window.WindowState == WindowState.Minimized, "Minimize button did not minimize.");
                window.WindowState = WindowState.Normal;
                Console.WriteLine("PASS: mouse-driven maximize, restore, and minimize controls.");

                var viewModel = (ShellViewModel)window.DataContext!;
                viewModel.SetStatus("Game unavailable", "Test runtime failure");
                Dispatcher.UIThread.RunJobs();
                Check(!viewport.IsVisible, "Viewport remained visible after a runtime error.");
                Check(window.FindControl<StackPanel>("GameErrorPanel")!.IsVisible, "Runtime error was not shown.");
                Check(window.FindControl<TextBlock>("GameErrorMessage")!.Text == "Test runtime failure", "Error binding did not update.");
                Check(window.FindControl<TextBlock>("StatusText")!.Text == "Game unavailable", "Title-bar status did not update.");
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
    }

    private static void Click(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        var button = window.FindControl<Button>(name) ?? throw new Exception("Button not found: " + name);
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
