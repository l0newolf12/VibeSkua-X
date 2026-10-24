using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Messaging;
using FluentAvalonia.UI.Controls;
using Skua.Core.Messaging;
using VibeSkuaX.App.Features.Shell;

namespace VibeSkuaX.Shell.Tests;

internal static class UiRegressionChecks
{
    internal static void RunShortcutWorker()
    {
        using var process = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--empty-tab-shortcuts")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        }) ?? throw new Exception("Could not start shortcut regression worker.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
            throw new TimeoutException("Empty/single-tab keyboard navigation hung the isolated worker.");
        }
        Program.Check(process.ExitCode == 0, errors.GetAwaiter().GetResult());
        Console.Write(output.GetAwaiter().GetResult());
    }

    internal static int EmptyShortcuts()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessAppBuilder));
        session.Dispatch(() =>
        {
            var window = new ShellWindow();
            try
            {
                window.Show();
                Layout();
                var model = (ShellViewModel)window.DataContext!;
                var tabs = window.FindControl<FATabView>("AccountTabs")!;
                tabs.ContainerFromIndex(0).Focus(NavigationMethod.Tab);
                for (int i = 0; i < 3; i++)
                {
                    Key(window, PhysicalKey.Tab, RawInputModifiers.Control);
                    Key(window, PhysicalKey.Tab, RawInputModifiers.Control | RawInputModifiers.Shift);
                    Program.Check(model.SelectedTab == model.Tabs[0], "Single-tab cycling changed selection.");
                }
                model.CloseTab(model.SelectedTab!);
                Layout();
                var add = tabs.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "AddButton");
                add.Focus(NavigationMethod.Tab);
                for (int i = 0; i < 3; i++)
                {
                    Key(window, PhysicalKey.Tab, RawInputModifiers.Control);
                    Key(window, PhysicalKey.Tab, RawInputModifiers.Control | RawInputModifiers.Shift);
                    Key(window, PhysicalKey.F4, RawInputModifiers.Control);
                }
                Program.Check(model.HasNoTabs && window.IsVisible && window.CloseConfirmation is null, "Empty shortcuts changed the empty state.");
                Console.WriteLine("PASS: zero/one-tab shortcuts in both directions finish in an isolated process.");
            }
            finally { window.Close(); }
        }, CancellationToken.None).GetAwaiter().GetResult();
        return 0;
    }

    internal static async Task<int> RunHeadless(string artifacts)
    {
        var window = new ShellWindow();
        try
        {
            window.Show();
            Layout();
            var model = (ShellViewModel)window.DataContext!;
            var tabs = window.FindControl<FATabView>("AccountTabs")!;
            var status = window.FindControl<DropDownButton>("GameListButton")!;
            var statusPeer = ControlAutomationPeer.CreatePeerForElement(status);
            var announcements = new List<string>();
            statusPeer.PropertyChanged += (_, e) =>
            {
                if (e.Property == AutomationElementIdentifiers.NameProperty) announcements.Add(e.NewValue?.ToString() ?? "");
            };
            Program.Check(statusPeer.GetLiveSetting() == AutomationLiveSetting.Polite, "Status is not a polite live region.");
            Program.Check(ControlAutomationPeer.CreatePeerForElement(window.FindControl<SelectableTextBlock>("GameErrorMessage")!).GetLiveSetting() == AutomationLiveSetting.Off,
                "Error details would duplicate the status live announcement.");
            var indicator = window.FindControl<FAProgressRing>("LoadingIndicator")!;
            Program.Check(indicator.IsVisible && indicator.IsActive && indicator.IsIndeterminate, "Startup has no active indeterminate loading indicator.");
            var add = tabs.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "AddButton");
            Program.Check(AutomationProperties.GetName(add) == "Open game" && (string?)ToolTip.GetTip(add) == "Open game", "Add action wording is inconsistent.");
            CheckIcon(window);

            model.OpenGameCommand.Execute(null);
            Layout();
            CheckTabDragging(window, model, tabs);
            for (int i = 0; i < 9; i++) model.OpenGameCommand.Execute(null);
            window.Width = 720;
            Layout();
            tabs.ContainerFromItem(model.SelectedTab!).Focus(NavigationMethod.Tab);
            for (int i = 0; i < model.Tabs.Count; i++)
            {
                Key(window, PhysicalKey.Tab, RawInputModifiers.Control);
                var expected = model.Tabs[i];
                await NativeSmoke.Until(() => model.SelectedTab == expected && tabs.ContainerFromItem(expected)?.IsFocused == true,
                    "Keyboard cycling skipped an unrealized tab or lost tab-strip focus.");
                CheckCloseName(tabs, expected);
                Program.Check(SelectedIsVisible(tabs, expected), "Keyboard-selected tab is clipped by overflow.");
            }
            Key(window, PhysicalKey.Tab, RawInputModifiers.Control);
            await NativeSmoke.Until(() => model.SelectedTab == model.Tabs[0] && tabs.ContainerFromItem(model.Tabs[0])?.IsFocused == true,
                "Forward wrap failed.");
            Key(window, PhysicalKey.Tab, RawInputModifiers.Control | RawInputModifiers.Shift);
            await NativeSmoke.Until(() => model.SelectedTab == model.Tabs[^1] && tabs.ContainerFromItem(model.Tabs[^1])?.IsFocused == true,
                "Reverse wrap failed.");
            CheckOverflowAlignment(window, tabs, add);
            CheckTabDragging(window, model, tabs);
            using (var frame = window.CaptureRenderedFrame()!) frame.Save(Path.Combine(artifacts, "tabs-overflow-fixed.png"));

            var retained = model.SelectedTab!;
            Key(window, PhysicalKey.F4, RawInputModifiers.Control);
            var dialog = window.CloseConfirmation ?? throw new Exception("Ctrl+F4 did not open confirmation for a bound game.");
            Layout();
            Program.Check(dialog.FindControl<TextBlock>("CloseGameName")!.Text == retained.Label &&
                dialog.FindControl<TextBlock>("CloseGameName")!.FontWeight == FontWeight.SemiBold, "Confirmation target is not prominent.");
            Program.Check(dialog.Icon is not null, "Confirmation does not share the app icon.");
            Program.Check(dialog.FindControl<Button>("CancelGameClose")!.IsFocused, "Confirmation did not default to Cancel.");
            Key(dialog, PhysicalKey.Tab);
            Program.Check(dialog.FindControl<Button>("ConfirmGameClose")!.IsFocused, "Tab did not reach Close game.");
            Key(dialog, PhysicalKey.Tab, RawInputModifiers.Shift);
            Program.Check(dialog.FindControl<Button>("CancelGameClose")!.IsFocused, "Shift+Tab did not return to Cancel.");
            Key(dialog, PhysicalKey.Enter, releaseTo: window);
            await NativeSmoke.Until(() => window.CloseConfirmation is null, "Default Enter did not cancel close.");
            Program.Check(model.Tabs.Contains(retained), "Default Enter ended a game.");
            await NativeSmoke.Until(() => tabs.ContainerFromItem(retained)?.IsFocused == true, "Keyboard cancellation lost tab-strip focus.");
            Key(window, PhysicalKey.F4, RawInputModifiers.Control);
            dialog = window.CloseConfirmation!;
            Layout();
            dialog.FindControl<Button>("ConfirmGameClose")!.Focus();
            Key(dialog, PhysicalKey.Space, releaseTo: window);
            await NativeSmoke.Until(() => !model.Tabs.Contains(retained) && window.CloseConfirmation is null, "Keyboard confirmation did not close just its game.");

            window.Width = 1100;
            Layout();
            CheckBrushes(window, model, tabs);
            window.Width = 720;
            window.Height = 460;
            Layout();
            var addBeforeFailure = add.TranslatePoint(default, window);
            var dragBeforeFailure = window.FindControl<Border>("TitleDragArea")!.Bounds;
            await NativeSmoke.Until(() => SelectedIsVisible(tabs, model.SelectedTab!), "Resizing left the selected game outside the tab viewport.");
            string error = string.Join("\n", Enumerable.Range(1, 18).Select(i => $"Diagnostic {i}: The requested game resource could not be downloaded. Check the network connection and retry."));
            model.SelectedTab!.Session.Messenger.Send(new FlashRuntimeFailedMessage(new Exception(error)));
            Layout();
            Program.Check(add.TranslatePoint(default, window) == addBeforeFailure && window.FindControl<Border>("TitleDragArea")!.Bounds == dragBeforeFailure,
                "Loading/failure status changes shifted tab actions or the reserved drag region.");
            Program.Check(!indicator.IsVisible && !indicator.IsActive, "Loading animation stayed active after failure.");
            Program.Check(announcements.Any(a => a.Contains("Game unavailable")), "Failure did not produce a live-region Name event.");
            var retry = window.FindControl<Button>("RetryGameButton")!;
            var copy = window.FindControl<Button>("CopyDiagnosticsButton")!;
            foreach (var action in new[] { retry, copy })
            {
                var p = action.TranslatePoint(default, window)!.Value;
                Program.Check(p.Y >= 40 && p.Y + action.Bounds.Height <= window.ClientSize.Height, "Long error hid a recovery action.");
            }
            var scroll = window.FindControl<ScrollViewer>("ErrorDetailsScroll")!;
            Program.Check(scroll.Extent.Height > scroll.Viewport.Height, "Long diagnostics have no scrollable viewport.");
            scroll.Offset = new Vector(0, scroll.Extent.Height);
            Layout();
            Program.Check(scroll.Offset.Y > 0, "Error details cannot scroll.");
            var details = window.FindControl<SelectableTextBlock>("GameErrorMessage")!;
            details.SelectAll();
            Program.Check(details.SelectedText.Contains(error), "Error details cannot be selected.");
            Program.Check(details.SelectionForegroundBrush?.ToString() == "#ff10202a" && details.SelectionBrush?.ToString() == "#ff72d4f0",
                "Selected diagnostics do not use readable on-accent text.");
            Rect copyBounds = copy.Bounds;
            string diagnostics = window.BuildDiagnostics();
            await window.CopyDiagnosticsAsync();
            Program.Check(await window.Clipboard!.TryGetTextAsync() == diagnostics && diagnostics.Contains(error) && diagnostics.Contains(model.SelectedTab.Label),
                "Copied diagnostics lost context or full error text.");
            Program.Check(copy.Bounds == copyBounds && (string?)copy.Content == "Copied", "Copy feedback changed action geometry or was absent.");
            using (var frame = window.CaptureRenderedFrame()!) frame.Save(Path.Combine(artifacts, "long-error-fixed.png"));
            model.SelectedTab.RetryCommand.Execute(null);
            Layout();
            Program.Check(indicator.IsVisible && indicator.IsActive, "Retry did not restart loading feedback.");
            Console.WriteLine("PASS: bound/overflow keyboard navigation and close, accessible close names/live events, long errors/copy, loading, stable actions, and shared icon.");
            return 0;
        }
        finally { window.Close(); }
    }

    private static void CheckIcon(Window window)
    {
        Program.Check(window.Icon is not null, "Window has no app icon.");
        using var resource = AssetLoader.Open(new Uri("avares://VibeSkuaX/Assets/App.ico"));
        using var reader = new BinaryReader(resource);
        Program.Check(reader.ReadUInt16() == 0 && reader.ReadUInt16() == 1 && reader.ReadUInt16() == 9, "App icon is not a multi-size ICO.");
        var widths = new List<int>();
        for (int i = 0; i < 9; i++)
        {
            byte width = reader.ReadByte();
            widths.Add(width == 0 ? 256 : width);
            reader.ReadBytes(15);
        }
        Program.Check(widths.SequenceEqual(new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 }), "Icon lacks taskbar/DPI sizes.");
    }

    private static void CheckTabDragging(ShellWindow window, ShellViewModel model, FATabView tabs)
    {
        var owners = model.Tabs.ToArray();
        var sessions = owners.Select(t => t.Session).ToArray();
        // Exercise both an already selected header and an inactive neighbor.
        var targets = owners.Where(t => SelectedIsVisible(tabs, t)).OrderByDescending(t => t == model.SelectedTab).ToArray();
        Program.Check(targets.Length > 0, "Drag regression needs a fully visible tab header.");
        foreach (var tab in targets.Take(2))
        {
            var item = (FATabViewItem)tabs.ContainerFromItem(tab);
            var root = item.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "LayoutRoot");
            var bounds = item.Bounds;
            var start = item.TranslatePoint(new Point(20, item.Bounds.Height / 2), window)!.Value;
            window.MouseMove(start);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Vector(40, 0), RawInputModifiers.LeftMouseButton);
            Layout();
            Program.Check(root.Opacity == 1 && root.IsEffectivelyVisible, "Dragging a disabled-drag tab makes its header disappear.");
            Program.Check(item.Bounds == bounds, "Dragging changed tab geometry.");
            // Release outside the header, where the library may not clear its drag pseudo-state.
            var outside = new Point(start.X, 100);
            window.MouseMove(outside, RawInputModifiers.LeftMouseButton);
            window.MouseUp(outside, MouseButton.Left);
            Layout();
            Program.Check(root.Opacity == 1 && root.IsEffectivelyVisible, "Tab stayed hidden after releasing outside the strip.");
            Program.Check(model.SelectedTab is not null && owners.Contains(model.SelectedTab) && model.Tabs.SequenceEqual(owners) &&
                model.Tabs.Select(t => t.Session).SequenceEqual(sessions), "Dragging removed/reordered a tab or changed session ownership.");
            var click = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;
            window.MouseMove(click);
            window.MouseDown(click, MouseButton.Left);
            window.MouseUp(click, MouseButton.Left);
            Layout();
            Program.Check(model.SelectedTab == tab && root.Opacity == 1, "Tab is not selectable/visible after a cancelled drag.");
        }
        Console.WriteLine("PASS: selected/inactive tab dragging and release outside preserve headers, geometry, order, sessions, and subsequent selection.");
    }

    private static void CheckCloseName(FATabView tabs, GameTabViewModel tab)
    {
        var close = tabs.ContainerFromItem(tab).GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CloseButton");
        Program.Check(ControlAutomationPeer.CreatePeerForElement(close).GetName() == "Close " + tab.Label, "Tab close is named with a glyph or a stale game label.");
    }

    private static bool SelectedIsVisible(FATabView tabs, GameTabViewModel tab)
    {
        var item = tabs.ContainerFromItem(tab);
        var viewport = tabs.GetVisualDescendants().OfType<ScrollContentPresenter>().Single();
        var position = item?.TranslatePoint(default, viewport);
        return position is { } point && point.X >= -.5 && point.X + item!.Bounds.Width <= viewport.Bounds.Width + .5;
    }

    private static void CheckOverflowAlignment(Window window, FATabView tabs, Button add)
    {
        double center = add.TranslatePoint(new Point(add.Bounds.Width / 2, add.Bounds.Height / 2), window)!.Value.Y;
        foreach (var arrow in tabs.GetVisualDescendants().OfType<Button>().Where(b => b.Name is "ScrollIncreaseButton" or "ScrollDecreaseButton"))
        {
            double y = arrow.TranslatePoint(new Point(arrow.Bounds.Width / 2, arrow.Bounds.Height / 2), window)!.Value.Y;
            Program.Check(arrow.IsEffectivelyVisible && Math.Abs(y - center) <= .5, $"Overflow arrow is misaligned: {arrow.Name}={y}, add={center}.");
        }
    }

    private static void CheckBrushes(ShellWindow window, ShellViewModel model, FATabView tabs)
    {
        window.Activate();
        // Keep two tabs realized while replacing the shared resources, including active states.
        model.SelectedTab = model.Tabs[^1];
        Layout();
        var selected = (FATabViewItem)tabs.ContainerFromItem(model.SelectedTab);
        var neighbor = (FATabViewItem)tabs.ContainerFromIndex(model.Tabs.Count - 2);
        var replacements = new Dictionary<string, IBrush>
        {
            ["VsBrushSelected"] = new SolidColorBrush(Colors.Lime),
            ["VsBrushHover"] = new SolidColorBrush(Colors.Orange),
            ["VsBrushPressed"] = new SolidColorBrush(Colors.Blue),
            ["VsBrushTextPrimary"] = new SolidColorBrush(Colors.Yellow),
            ["VsBrushTabDivider"] = new SolidColorBrush(Colors.Magenta),
            ["VsBrushAccent"] = new SolidColorBrush(Colors.Red)
        };
        try
        {
            foreach (var (key, brush) in replacements) Application.Current!.Resources[key] = brush;
            Layout();
            var border = selected.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "TabContainerBorder");
            Program.Check(ReferenceEquals(border.Background, replacements["VsBrushSelected"]), "Selected tab did not follow a shared brush replacement.");
            Program.Check(ReferenceEquals(border.BorderBrush, replacements["VsBrushAccent"]), "Selection indicator did not follow the shared accent.");
            Program.Check(ReferenceEquals(selected.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(p => p.Name == "SelectedBackgroundPath").Fill, replacements["VsBrushSelected"]),
                "Selected tab silhouette retained a stale brush.");
            Program.Check(ReferenceEquals(selected.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Name == "ContentPresenter" && p.TemplatedParent == selected).Foreground, replacements["VsBrushTextPrimary"]),
                "Tab text did not follow the shared foreground.");
            var separator = neighbor.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "TabSeparator");
            Program.Check(ReferenceEquals(separator.Background, replacements["VsBrushTabDivider"]), "Divider did not follow the shared brush.");
            window.MouseMove(neighbor.TranslatePoint(new Point(20, 20), window)!.Value);
            Layout();
            var neighborBorder = neighbor.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "TabContainerBorder");
            Program.Check(ReferenceEquals(neighborBorder.Background, replacements["VsBrushHover"]), "Hovered tab did not follow the shared brush.");
            ((IPseudoClasses)neighbor.Classes).Set(":pressed", true);
            Layout();
            Program.Check(ReferenceEquals(neighborBorder.Background, replacements["VsBrushPressed"]), "Pressed tab did not follow the shared brush.");
            ((IPseudoClasses)neighbor.Classes).Set(":pressed", false);
        }
        finally
        {
            foreach (string key in replacements.Keys) Application.Current!.Resources.Remove(key);
            window.MouseMove(new Point(500, 200));
            Layout();
        }
        var restored = selected.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "TabContainerBorder");
        Program.Check(restored.Background?.ToString() == "#ff2a2a2f", "Removing shared overrides did not restore tab styling.");
    }

    private static void Layout() => Dispatcher.UIThread.RunJobs();

    private static void Key(Window window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None, Window? releaseTo = null)
    {
        window.KeyPressQwerty(key, modifiers);
        (window.IsVisible ? window : releaseTo!).KeyReleaseQwerty(key, modifiers);
        Layout();
    }
}
