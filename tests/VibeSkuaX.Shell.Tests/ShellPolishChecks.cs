using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Messaging;
using FluentAvalonia.UI.Controls;
using Skua.Core.Messaging;
using VibeSkuaX.App.Features.Shell;

namespace VibeSkuaX.Shell.Tests;

internal static class ShellPolishChecks
{
    internal static async Task<int> RunHeadless(string artifacts)
    {
        var model = new ShellViewModel();
        for (int i = 0; i < 10; i++) model.OpenGameCommand.Execute(null);
        var window = new ShellWindow(model) { Width = 720, Height = 460 };
        window.Show();
        try
        {
            Layout();
            var tabs = window.FindControl<FATabView>("AccountTabs")!;
            var button = window.FindControl<DropDownButton>("GameListButton")!;
            var owners = model.Tabs.ToArray();
            var sessions = owners.Select(t => t.Session).ToArray();
            Program.Check(window.Title == "Game 11 - VibeSkua X" && button.IsEnabled && (string?)button.Content == "Games",
                "Selected game title or game-list action is missing.");
            Program.Check(window.FindControl<TextBlock>("StatusText") is null, "Old visible status text still occupies the game-list slot.");
            Program.Check(((string?)ToolTip.GetTip(button))?.Contains("Client: Starting game…\nConnection: Not checked") == true,
                "Tooltip conflates client readiness and connection before the first poll.");
            Rect buttonBounds = button.Bounds;
            var add = tabs.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "AddButton");
            var addPosition = add.TranslatePoint(default, window);
            var drag = window.FindControl<Border>("TitleDragArea")!;
            Program.Check(drag.Bounds.Width >= 160 && button.Bounds.Width == 96 && button.Bounds.Height == 32,
                "Game-list action consumed the reserved drag region or lost fixed geometry.");
            var menuPosition = button.TranslatePoint(default, window)!.Value;
            var captionPosition = window.FindChrome<Button>("MinimizeButton")!.TranslatePoint(default, window)!.Value;
            var dragPosition = drag.TranslatePoint(default, window)!.Value;
            Program.Check(dragPosition.X + drag.Bounds.Width <= menuPosition.X,
                $"Drag area overlaps the menu: drag={dragPosition}/{drag.Bounds}, menu={menuPosition}, add={addPosition}, tabs={tabs.Bounds}.");
            Program.Check(menuPosition.X >= 0 && menuPosition.X + button.Bounds.Width <= captionPosition.X &&
                menuPosition.Y >= 0 && menuPosition.Y + button.Bounds.Height <= 40,
                $"Game-list button is clipped or overlaps captions: menu={menuPosition}, caption={captionPosition}.");
            CheckInactiveStyles(window, tabs, button, add);

            // Open through the actual dropdown keyboard behavior, not a direct ShowAt.
            button.Focus(NavigationMethod.Tab);
            Press(window, PhysicalKey.Space);
            await NativeSmoke.Until(() => window.GameListFlyout.IsOpen, "Keyboard input did not open the game list.");
            Layout();
            var presenter = (FAMenuFlyoutPresenter)window.GameListFlyout.Popup.Child!;
            var items = window.GameListFlyout.Items.Cast<FAToggleMenuFlyoutItem>().ToArray();
            Program.Check(items.Length == 11 && items.Select(i => i.Tag).SequenceEqual(owners) &&
                items.Single(i => i.IsChecked).Tag == model.SelectedTab, "Game list omitted an overflow owner or marked the wrong selection.");
            Program.Check(presenter.Bounds.Width >= 320 && presenter.Bounds.Width <= 520, "Game list does not use shared menu bounds.");
            foreach (var item in items)
            {
                var tab = (GameTabViewModel)item.Tag!;
                Program.Check(item.Text == tab.MenuLabel && (string?)ToolTip.GetTip(item) == tab.StatusToolTip, "Menu text/status points to the wrong game.");
                var text = item.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "TextBlock");
                Program.Check(text.TextTrimming == Avalonia.Media.TextTrimming.None, "Game list truncates full names like the tab strip.");
            }
            var popup = TopLevel.GetTopLevel(presenter)!;
            using (var frame = popup.CaptureRenderedFrame()!) frame.Save(Path.Combine(artifacts, "game-list-menu.png"));
            items[0].Focus(NavigationMethod.Tab);
            Press(popup, PhysicalKey.ArrowDown);
            Program.Check(items[1].IsFocused, "Arrow keys cannot navigate the open-game list.");
            Press(popup, PhysicalKey.Enter);
            await NativeSmoke.Until(() => !window.GameListFlyout.IsOpen && model.SelectedTab == owners[1] &&
                tabs.ContainerFromItem(owners[1])?.IsFocused == true, "Keyboard selection did not reveal/focus an overflow tab.");
            Program.Check(window.Title == "Game 2 - VibeSkua X" && model.Tabs.Select(t => t.Session).SequenceEqual(sessions),
                "Menu selection did not update the title or replaced a session.");

            button.Focus(NavigationMethod.Tab);
            Press(window, PhysicalKey.Space);
            Layout();
            presenter = (FAMenuFlyoutPresenter)window.GameListFlyout.Popup.Child!;
            var background = owners[2];
            background.Session.Messenger.Send(new FlashRuntimeFailedMessage(new Exception("Background menu status check")));
            Layout();
            var failedItem = window.GameListFlyout.Items.Cast<FAToggleMenuFlyoutItem>().Single(i => i.Tag == background);
            Program.Check(failedItem.Text.Contains("Game unavailable") && window.GameListFlyout.IsOpen && model.SelectedTab == owners[1],
                "Background failure closed the menu, changed selection, or left stale menu status.");
            Press(TopLevel.GetTopLevel(presenter)!, PhysicalKey.Escape);
            Program.Check(!window.GameListFlyout.IsOpen && model.SelectedTab == owners[1], "Escape changed selection or failed to dismiss the list.");

            // A selected failure must not take focus from active tab navigation.
            var selected = model.SelectedTab!;
            var selectedHeader = tabs.ContainerFromItem(selected);
            selectedHeader.Focus(NavigationMethod.Tab);
            selected.Session.Messenger.Send(new FlashRuntimeFailedMessage(new Exception("Keyboard focus check")));
            Layout();
            Program.Check(selectedHeader.IsFocused && !window.FindControl<Button>("RetryGameButton")!.IsFocused,
                "A failure stole focus from keyboard tab navigation.");
            Program.Check(button.Bounds == buttonBounds && add.TranslatePoint(default, window) == addPosition,
                "Menu, status, or selection feedback moved chrome actions.");
            foreach (var owner in owners) model.CloseTab(owner);
            Layout();
            Program.Check(window.Title == "VibeSkua X" && !button.IsEnabled && model.HasNoTabs &&
                window.FindControl<TextBlock>("EmptyStateHelp") is { IsEffectivelyVisible: true, Text: "Open a game, then log in. Use + to open another." },
                "Empty state lacks guidance, a neutral title, or a disabled empty game list.");
            using (var frame = window.CaptureRenderedFrame()!) frame.Save(Path.Combine(artifacts, "empty-state-help.png"));
            model.OpenGameCommand.Execute(null);
            Layout();
            Program.Check(button.IsEnabled && window.Title == "Game 12 - VibeSkua X" &&
                ((string?)ToolTip.GetTip(button))?.StartsWith("Open games (1)") == true, "Opening from empty left stale menu metadata.");
            Console.WriteLine("PASS: game-list pointer/keyboard composition, all overflow owners, live status, preserved sessions, titles, inactive chrome, empty guidance, and nonintrusive failure focus.");
            return 0;
        }
        finally { window.Close(); }
    }

    private static void CheckInactiveStyles(ShellWindow window, FATabView tabs, Button menu, Button add)
    {
        var header = (FATabViewItem)tabs.ContainerFromItem(((ShellViewModel)window.DataContext!).SelectedTab!);
        var text = header.GetVisualDescendants().OfType<ContentPresenter>().Single(c => c.Name == "ContentPresenter" && c.TemplatedParent == header);
        var title = window.FindChrome<Border>("TitleBar")!;
        var bounds = new[] { header.Bounds, menu.Bounds, add.Bounds, title.Bounds };
        bool wasActive = window.IsActive;
        bool showActivated = window.ShowActivated;
        try
        {
            window.Activate();
            Layout();
            Program.Check(text.Foreground?.ToString() == "#ffe6e6eb" && title.BorderBrush?.ToString() == "#ff35353c", "Active chrome lost its baseline colors.");
            window.Hide();
            Layout();
            window.ShowActivated = false;
            window.Show();
            Layout();
            Program.Check(text.Foreground?.ToString() == "#ffa3a3ae" && title.BorderBrush?.ToString() == "#ff26262b", "Inactive chrome has no restrained shared visual change.");
            Program.Check(new[] { header.Bounds, menu.Bounds, add.Bounds, title.Bounds }.SequenceEqual(bounds), "Activation styling changed control geometry.");
        }
        finally
        {
            window.ShowActivated = showActivated;
            if (wasActive) window.Activate();
            Layout();
        }
    }

    private static void Press(TopLevel target, PhysicalKey key)
    {
        target.KeyPressQwerty(key, RawInputModifiers.None);
        target.KeyReleaseQwerty(key, RawInputModifiers.None);
        Layout();
    }

    private static void Layout() => Dispatcher.UIThread.RunJobs();
}
