using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using VibeSkuaX.App.Features.Shell;

namespace VibeSkuaX.Shell.Tests;

internal static class ToolTipChecks
{
    internal static async Task<int> RunHeadless()
    {
        var model = new ShellViewModel();
        var window = new ShellWindow(model);
        try
        {
            model.OpenGameCommand.Execute(null);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var tabs = window.FindControl<FATabView>("AccountTabs")!;
            var first = tabs.ContainerFromIndex(0);
            var second = tabs.ContainerFromIndex(1);
            var firstTip = new ToolTip { Content = "First tooltip" };
            var secondTip = new ToolTip { Content = "Second tooltip" };
            ToolTip.SetTip(first, firstTip);
            ToolTip.SetTip(second, secondTip);
            ToolTip.SetShowDelay(first, 0);
            window.MouseMove(first.TranslatePoint(new Point(20, 20), window)!.Value);
            await NativeSmoke.Until(() => ToolTip.GetIsOpen(first), "First tooltip did not open.");
            await CheckPlacement(first, firstTip);
            // Keep the default delay on the second control: the tooltip service should
            // switch immediately from the already-open first tip to its new owner.
            window.MouseMove(second.TranslatePoint(new Point(20, 20), window)!.Value);
            await NativeSmoke.Until(() => ToolTip.GetIsOpen(second), "Tooltip did not switch controls.");
            Program.Check(!ToolTip.GetIsOpen(first), "Previous tooltip stayed open after moving to another control.");
            await CheckPlacement(second, secondTip);
            Program.Check(secondTip.PointToScreen(default).X > first.PointToScreen(default).X + first.Bounds.Width / 2,
                "Second tooltip retained the previous control's position.");
            window.MouseMove(new Point(400, 200));
            await NativeSmoke.Until(() => !ToolTip.GetIsOpen(second), "Tooltip stayed open after leaving its owner.");
            Console.WriteLine("PASS: tooltips anchor to each current control, switch immediately, and close on leave.");
            return 0;
        }
        finally { window.Close(); }
    }

    private static async Task CheckPlacement(Control owner, ToolTip tip)
    {
        await NativeSmoke.Until(() =>
        {
            Dispatcher.UIThread.RunJobs();
            // The headless platform uses an overlay host even when native popups
            // are requested; native coverage separately checks the real HWND.
            if (TopLevel.GetTopLevel(tip) is null || tip.Bounds.Width <= 0) return false;
            var bottom = owner.PointToScreen(new Point(owner.Bounds.Width / 2, owner.Bounds.Height));
            var actual = tip.PointToScreen(new Point(tip.Bounds.Width / 2, 0));
            return Math.Abs(actual.X - bottom.X) <= 1 && Math.Abs(actual.Y - bottom.Y - 8) <= 1;
        }, "Tooltip did not open beneath its current owner with the shared gap.");
    }
}
