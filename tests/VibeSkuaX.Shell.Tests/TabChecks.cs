using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Messaging;
using FluentAvalonia.UI.Controls;
using Skua.Core.Messaging;
using VibeSkuaX.App.Features.Shell;
using VibeSkuaX.App.Sessions;

namespace VibeSkuaX.Shell.Tests;

internal static class TabChecks
{
    internal static async Task<int> RunHeadless(string artifacts)
    {
        var window = new ShellWindow();
        try
        {
            window.Show();
            Layout();
            var model = (ShellViewModel)window.DataContext!;
            var tabs = window.FindControl<FATabView>("AccountTabs")!;
            Program.Check(model.Tabs.Count == 1 && model.SelectedTab == model.Tabs[0], "Startup must select one game.");
            var first = model.SelectedTab!;
            var firstViewport = window.SelectedViewport!;
            var add = tabs.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "AddButton");
            Click(window, add);
            var second = model.SelectedTab!;
            var secondViewport = window.SelectedViewport!;
            Program.Check(model.Tabs.Count == 2 && first.Session != second.Session && !firstViewport.IsVisible && secondViewport.IsVisible,
                "Adding did not create/select an independent game.");
            Click(window, tabs.ContainerFromItem(first));
            Program.Check(model.SelectedTab == first && window.SelectedViewport == firstViewport && !secondViewport.IsVisible,
                "Tab input did not preserve/select the original viewport.");
            CheckGeometry(window, tabs, add);
            model.SelectedTab = second;
            Layout();
            first.Session.Messenger.Send(new FlashRuntimeFailedMessage(new Exception("Background failure")));
            Layout();
            Program.Check(first.HasError && !model.HasError && secondViewport.IsVisible, "Background failure affected the selected game.");
            model.SelectedTab = first;
            Layout();
            Program.Check(model.HasError && !firstViewport.IsVisible && first.RetryCommand.CanExecute(null), "Failed tab is not recoverable.");
            var old = first.Session;
            Click(window, window.FindControl<Button>("RetryGameButton")!);
            Program.Check(first.Session != old && old.State == GameSessionState.Disposed && firstViewport.IsDisposed &&
                !first.RetryCommand.CanExecute(null) && !secondViewport.IsDisposed && window.SelectedViewport!.IsVisible,
                "Retry did not replace just the failed owner/viewport.");
            var retried = window.SelectedViewport!;
            old.ProcessCallback("loaded", []);
            Program.Check(first.IsLoading, "Late callback revived the old session after retry.");

            // The library selects the tab when its close affordance receives pointer input.
            var secondItem = tabs.ContainerFromItem(second);
            var close = secondItem.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CloseButton");
            Click(window, close);
            var confirmation = window.CloseConfirmation ?? throw new Exception("Tab close skipped confirmation.");
            Program.Check(model.Tabs.Count == 2 && !secondViewport.IsDisposed, "Opening confirmation ended a session.");
            confirmation.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            await NativeSmoke.Until(() => window.CloseConfirmation is null, "Escape did not cancel tab close.");
            Program.Check(model.Tabs.Count == 2 && model.SelectedTab == second && !secondViewport.IsDisposed,
                "Cancelled tab close disposed or removed a game.");
            model.SelectedTab = first;
            await Confirm(window, second);
            Program.Check(model.Tabs.Count == 1 && secondViewport.IsDisposed && model.SelectedTab == first && !retried.IsDisposed,
                "Closing an inactive tab affected the selected game.");
            await Confirm(window, first);
            Layout();
            Program.Check(model.HasNoTabs && window.IsVisible && retried.IsDisposed && window.SelectedViewport is null &&
                window.FindControl<StackPanel>("NoGamesPanel")!.IsVisible, "Final tab close did not show the empty state.");
            Click(window, window.FindControl<Button>("OpenGameButton")!);
            Program.Check(model.Tabs.Count == 1 && model.SelectedTab!.Label == "Game 3", "Empty-state action did not start a new game.");

            // The adjacent-tab policy and application cleanup include background games.
            var third = model.SelectedTab!;
            model.OpenGameCommand.Execute(null);
            var fourth = model.SelectedTab!;
            model.OpenGameCommand.Execute(null);
            var fifth = model.SelectedTab!;
            model.SelectedTab = fourth;
            await Confirm(window, fourth);
            Program.Check(model.SelectedTab == fifth, "Closing the selected middle tab did not select the next game.");
            await Confirm(window, fifth);
            Program.Check(model.SelectedTab == third, "Closing the last selected tab did not select the previous game.");
            for (int i = 0; i < 10; i++) model.OpenGameCommand.Execute(null);
            window.Width = window.MinWidth;
            Layout();
            CheckReservedChrome(window, add);
            using (var frame = window.CaptureRenderedFrame()!) frame.Save(Path.Combine(artifacts, "tabs-overflow.png"));
            window.Width = 1100;
            Layout();
            var sessions = model.Tabs.Select(t => t.Session).ToArray();
            var pending = window.RequestCloseTabAsync(model.SelectedTab!);
            window.Close();
            await pending;
            Program.Check(sessions.All(s => s.State == GameSessionState.Disposed) && model.HasNoTabs,
                "Application shutdown with confirmation open left a session alive.");
            Console.WriteLine("PASS: account tabs, state isolation, retry, confirmation/Escape, adjacent selection, empty state, overflow, and all-session shutdown.");
            return 0;
        }
        finally { window.Close(); }
    }

    private static async Task Confirm(ShellWindow window, GameTabViewModel tab)
    {
        var request = window.RequestCloseTabAsync(tab);
        var dialog = window.CloseConfirmation ?? throw new Exception("Missing close confirmation.");
        Layout();
        dialog.FindControl<Button>("ConfirmGameClose")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await request;
        Layout();
    }

    private static void CheckGeometry(ShellWindow window, FATabView tabs, Button add)
    {
        var first = (FATabViewItem)tabs.ContainerFromIndex(0);
        var second = (FATabViewItem)tabs.ContainerFromIndex(1);
        Rect firstBounds = first.Bounds, secondBounds = second.Bounds;
        var addPosition = add.TranslatePoint(default, window);
        window.MouseMove(second.TranslatePoint(new Point(20, 20), window)!.Value);
        Layout();
        Program.Check(first.Bounds == firstBounds && second.Bounds == secondBounds && add.TranslatePoint(default, window) == addPosition,
            "Hover changed tab/add geometry.");
        Program.Check((!first.TabSeparator!.IsVisible || first.TabSeparator.Opacity == 0) &&
            (!second.TabSeparator!.IsVisible || second.TabSeparator.Opacity == 0), "Dividers touching selected/hovered tabs remain visible.");
        window.MouseMove(new Point(300, 200));
        tabs.SelectedIndex = 1;
        Layout();
        Program.Check(first.Bounds == firstBounds && second.Bounds == secondBounds && add.TranslatePoint(default, window) == addPosition,
            "Selection changed tab/add geometry.");
        tabs.SelectedIndex = 0;
        Layout();
        var divider = (Border)second.TabSeparator!;
        Program.Check(divider.Opacity == 1 && divider.Bounds.Width == 1 && divider.Bounds.Height == 14,
            "Last inactive tab must show its short divider before add.");
        var plusCenter = add.TranslatePoint(new Point(add.Bounds.Width / 2, add.Bounds.Height / 2), window)!.Value;
        foreach (var item in new[] { first, second })
        {
            var close = item.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CloseButton");
            var closeCenter = close.TranslatePoint(new Point(close.Bounds.Width / 2, close.Bounds.Height / 2), window)!.Value;
            Program.Check(Math.Abs(plusCenter.Y - closeCenter.Y) <= 0.5,
                $"Tab close and add are not vertically aligned: close={closeCenter.Y}, add={plusCenter.Y}.");
        }
        var dividerCenter = divider.TranslatePoint(new Point(0, divider.Bounds.Height / 2), window)!.Value;
        Program.Check(Math.Abs(plusCenter.Y - dividerCenter.Y) <= 0.5, "Tab divider and add are not vertically aligned.");
        var end = second.TranslatePoint(new Point(second.Bounds.Width, 0), window)!.Value;
        var plus = add.TranslatePoint(default, window)!.Value;
        Program.Check(plus.X >= end.X && plus.X - end.X < 16, "Add button is not immediately after the last tab.");
        CheckReservedChrome(window, add);
    }

    private static void CheckReservedChrome(ShellWindow window, Button add)
    {
        var drag = window.FindControl<Border>("TitleDragArea")!;
        var caption = window.FindChrome<Button>("MinimizeButton")!;
        var addRight = add.TranslatePoint(new Point(add.Bounds.Width, 0), window)!.Value.X;
        var dragLeft = drag.TranslatePoint(default, window)!.Value.X;
        var captionLeft = caption.TranslatePoint(default, window)!.Value.X;
        Program.Check(drag.Bounds.Width >= 160 && addRight <= dragLeft && dragLeft + drag.Bounds.Width <= captionLeft + 1,
            "Tab overflow consumed the drag region or caption buttons.");
    }

    private static void Click(Window window, Control control)
    {
        Layout();
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Layout();
    }

    private static void Layout() => Dispatcher.UIThread.RunJobs();
}
