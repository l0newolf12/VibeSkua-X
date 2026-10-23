using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using VibeSkuaX.App.Features.Shell;
using VibeSkuaX.App.Sessions;

namespace VibeSkuaX.Shell.Tests;

internal static class SharedChromeChecks
{
    internal static async Task<int> RunHeadless(string artifacts)
    {
        var owner = new ShellWindow();
        try
        {
            owner.Show();
            var model = (ShellViewModel)owner.DataContext!;
            var session = model.SelectedTab!.Session;
            Task closed = owner.RequestCloseTabAsync(model.SelectedTab);
            var dialog = owner.CloseConfirmation!;
            Dispatcher.UIThread.RunJobs();
            Program.Check(dialog.WindowDecorations == WindowDecorations.BorderOnly && dialog.ExtendClientAreaToDecorationsHint,
                "Shared window style did not enable the custom native frame.");
            Program.Check(dialog.FindChrome<Border>("TitleBar")!.Bounds.Height == 40 &&
                dialog.FindControl<TextBlock>("CloseMessage")!.TranslatePoint(default, dialog)!.Value.Y >= 40,
                "Shared title bar overlaps dialog content or has an inconsistent height.");
            Program.Check(!dialog.FindChrome<Button>("MinimizeButton")!.IsVisible &&
                !dialog.FindChrome<Button>("MaximizeButton")!.IsVisible && dialog.FindChrome<Button>("CloseButton")!.IsVisible,
                "Fixed confirmation dialog must show only its close caption action.");
            Program.Check(dialog.FindChrome<TextBlock>("TitleText")!.Text == dialog.Title, "Default header did not display the window title.");
            dialog.Title = "Updated dialog title";
            Program.Check(dialog.FindChrome<TextBlock>("TitleText")!.Text == dialog.Title, "Shared title did not follow window title changes.");
            dialog.Title = "Close game?";
            CaptionChecks.RunHeadless(dialog);

            // Future ordinary windows use these same capabilities and caption actions.
            dialog.CanResize = true;
            dialog.CanMinimize = true;
            dialog.CanMaximize = true;
            Dispatcher.UIThread.RunJobs();
            Program.Check(dialog.FindChrome<Button>("MinimizeButton")!.IsVisible && dialog.FindChrome<Button>("MaximizeButton")!.IsVisible,
                "Caption actions did not follow window capability changes.");
            Click(dialog, "MaximizeButton");
            Program.Check(dialog.WindowState == WindowState.Maximized, "Shared caption did not maximize an enabled window.");
            Click(dialog, "MaximizeButton");
            Program.Check(dialog.WindowState == WindowState.Normal, "Shared caption did not restore an enabled window.");
            dialog.CanResize = false;
            dialog.CanMinimize = false;
            dialog.CanMaximize = false;
            Dispatcher.UIThread.RunJobs();
            using (var frame = dialog.CaptureRenderedFrame()!) frame.Save(Path.Combine(artifacts, "close-game-dialog.png"));
            Click(dialog, "CloseButton");
            await closed;
            Program.Check(owner.IsVisible && model.Tabs.Count == 1 && session.State != GameSessionState.Disposed,
                "Dialog caption close ended its owner's game instead of cancelling.");
            Console.WriteLine("PASS: shared dialog chrome, title/capability updates, caption actions, content layout, and safe caption-close cancellation.");
            return 0;
        }
        finally { owner.Close(); }
    }

    private static void Click(Window window, string name)
    {
        var button = window.FindChrome<Button>(name)!;
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }
}
