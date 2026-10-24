using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using VibeSkuaX.App.Features.Shell;
using VibeSkuaX.App.Sessions;

namespace VibeSkuaX.Shell.Tests;

internal static partial class NativeSmoke
{
    private static async Task CheckNativeTabDragging(ShellWindow window, FATabView tabs, params GameTabViewModel[] targets)
    {
        var model = (ShellViewModel)window.DataContext!;
        var owners = model.Tabs.ToArray();
        var handle = window.TryGetPlatformHandle()!.Handle;
        foreach (var tab in targets)
        {
            var item = (FATabViewItem)tabs.ContainerFromItem(tab);
            var root = item.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "LayoutRoot");
            var bounds = item.Bounds;
            var start = item.PointToScreen(new Point(20, item.Bounds.Height / 2));
            Program.Check(HitTest(handle, start.X, start.Y) == 1, "Tab drag did not start in the native client area.");
            var origin = new NativePoint();
            ClientToScreen(handle, ref origin);
            int x = start.X - origin.X, y = start.Y - origin.Y;
            SendMessage(handle, 0x0200 /* WM_MOUSEMOVE */, IntPtr.Zero, Pack(x, y));
            SendMessage(handle, 0x0201 /* WM_LBUTTONDOWN */, new IntPtr(1), Pack(x, y));
            SendMessage(handle, 0x0200, new IntPtr(1), Pack(x + (int)(40 * window.RenderScaling), y));
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.ApplicationIdle);
            Program.Check(root.Opacity == 1 && root.IsEffectivelyVisible && item.Bounds == bounds,
                "Native tab drag hid or resized the header.");
            SendMessage(handle, 0x0200, new IntPtr(1), Pack(x, y + (int)(80 * window.RenderScaling)));
            SendMessage(handle, 0x0202 /* WM_LBUTTONUP */, IntPtr.Zero, Pack(x, y + (int)(80 * window.RenderScaling)));
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.ApplicationIdle);
            Program.Check(root.Opacity == 1 && root.IsEffectivelyVisible && model.SelectedTab is not null &&
                owners.Contains(model.SelectedTab) && model.Tabs.SequenceEqual(owners),
                "Releasing a native tab drag outside the strip hid, removed, or reordered a tab.");
            await ClientClick(window, item);
            Program.Check(model.SelectedTab == tab && root.Opacity == 1, "Native tab stopped responding after a cancelled drag.");
        }
        Console.WriteLine("PASS: native dragging of selected/inactive tabs keeps headers visible and live games intact.");
    }

    private static async Task CheckKeyboardTabFocus(ShellWindow window, FATabView tabs, GameTabViewModel first,
        GameTabViewModel second, IntPtr firstFlash, IntPtr secondFlash)
    {
        var model = (ShellViewModel)window.DataContext!;
        var handle = window.TryGetPlatformHandle()!.Handle;
        var indicator = window.FindControl<FAProgressRing>("LoadingIndicator")!;
        Program.Check(!indicator.IsVisible && !indicator.IsActive, "Ready native game kept the loading animation active.");
        await ClientClick(window, tabs.ContainerFromItem(first));
        SetFocus(handle);
        tabs.ContainerFromItem(first).Focus(NavigationMethod.Tab);
        for (int i = 0; i < 4; i++)
        {
            bool forward = i % 2 == 0;
            var modifiers = forward ? KeyModifiers.Control : KeyModifiers.Control | KeyModifiers.Shift;
            var binding = tabs.KeyBindings.Single(k => k.Gesture.Key == Key.Tab && k.Gesture.KeyModifiers == modifiers);
            // Use the same binding the keyboard dispatcher invokes, without desktop keystrokes.
            binding.TryHandle(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Tab, KeyModifiers = modifiers });
            var expected = forward ? second : first;
            await Until(() => model.SelectedTab == expected && tabs.ContainerFromItem(expected)?.IsFocused == true,
                "Native game selection lost tab-strip keyboard focus.");
            Program.Check(GetFocus() == handle && GetFocus() != firstFlash && GetFocus() != secondFlash,
                "Keyboard cycling transferred native focus into Flash.");
        }
        await NativeClick(window, "MaximizeButton", 9);
        await Until(() => window.WindowState == WindowState.Maximized, "Keyboard-focus maximize did not complete.");
        Program.Check(tabs.ContainerFromItem(first).IsFocused && GetFocus() == handle, "Maximizing took focus from keyboard tab navigation.");
        await NativeClick(window, "MaximizeButton", 9);
        await Until(() => window.WindowState == WindowState.Normal, "Keyboard-focus restore did not complete.");
        Program.Check(tabs.ContainerFromItem(first).IsFocused && GetFocus() == handle, "Restoring took focus from keyboard tab navigation.");
        var closeBinding = tabs.KeyBindings.Single(k => k.Gesture.Key == Key.F4);
        closeBinding.TryHandle(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F4, KeyModifiers = KeyModifiers.Control });
        var dialog = window.CloseConfirmation ?? throw new Exception("Native keyboard close did not open confirmation.");
        await Until(() => dialog.IsVisible && dialog.TryGetPlatformHandle() is not null, "Keyboard confirmation did not become native.");
        Program.Check(dialog.FindControl<TextBlock>("CloseGameName")!.Text == first.Label, "Native confirmation names the wrong game.");
        CheckNativeIcons(dialog);
        await NativeClick(dialog, "CloseButton", 20);
        await Until(() => window.CloseConfirmation is null && tabs.ContainerFromItem(first)?.IsFocused == true,
            "Cancelling keyboard close did not restore tab focus.");
        Program.Check(GetFocus() == handle, "Keyboard confirmation cancellation restored Flash instead of native shell focus.");
        // Existing pointer and popup checks continue with the second game owning native focus.
        await ClientClick(window, tabs.ContainerFromItem(second));
        await Until(() => GetFocus() == secondFlash || IsChild(secondFlash, GetFocus()), "Pointer selection stopped returning focus to Flash.");
        File.WriteAllText(Path.Combine(_artifacts, "native-ui-regressions.json"), JsonSerializer.Serialize(new
        {
            RepeatedKeyboardCyclingPreservesTabFocus = true, KeyboardCloseUsesConfirmation = true,
            KeyboardCancelRestoresTabFocus = true, PointerSelectionRestoresFlashFocus = true,
            LoadingStopsWhenReady = true, WindowAndExecutableIcons = true
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS: repeated native-game keyboard switching/cancellation keep tab focus; pointer selection restores Flash focus; loading and icons are correct.");
    }

    private static async Task CheckRenamedTabUi()
    {
        var model = new ShellViewModel(() => new GameSession(new GameSessionOptions
        {
            SwfPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "InventoryHarness.swf")
        }));
        var window = new ShellWindow(model)
        {
            ShowActivated = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(-30000, -30000)
        };
        window.Show();
        try
        {
            var tab = model.SelectedTab!;
            await WaitReady(tab);
            var tabs = window.FindControl<FATabView>("AccountTabs")!;
            var close = tabs.ContainerFromItem(tab).GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CloseButton");
            var peer = ControlAutomationPeer.CreatePeerForElement(close);
            var status = window.FindControl<DropDownButton>("GameListButton")!;
            var statusPeer = ControlAutomationPeer.CreatePeerForElement(status);
            int announcements = 0;
            statusPeer.PropertyChanged += (_, e) =>
            {
                if (e.Property == AutomationElementIdentifiers.NameProperty) announcements++;
            };
            var runtime = tab.Session.Runtime!;
            // The fixture builds its plain game/avatar objects when this callback runs.
            runtime.Call("runInventoryTests");
            await Until(() => tab.Session.IsConnected == false &&
                ((string?)ToolTip.GetTip(status))?.Contains("Client: Game loaded\nConnection: Not connected") == true,
                "A loaded login screen did not distinguish client readiness from connection.");
            runtime.Call("setGameObject", "world.myAvatar.objData.strUsername", "");
            runtime.Call("setGameObject", "sfc", new Dictionary<string, object?> { ["isConnected"] = true });
            await Until(() => tab.Session.IsConnected == true && tab.Session.CharacterName is null &&
                ((string?)ToolTip.GetTip(status))?.Contains("Connection: Connected") == true && window.Title == "Game 1 - VibeSkua X",
                "Connection status incorrectly depends on character data being available.");
            runtime.Call("setGameObject", "world.myAvatar.objData.strUsername", "Fixture character");
            await Until(() => peer.GetName() == "Close Fixture character" && window.Title == "Fixture character - VibeSkua X" &&
                NativeTitle(window) == window.Title &&
                ((string?)ToolTip.GetTip(status))?.Contains("Connection: Connected") == true,
                "Renaming a connected character did not update close/status accessibility.");
            int afterRename = announcements;
            await Task.Delay(1100);
            Program.Check(announcements == afterRename, "Unchanged identity polls repeated live announcements.");
            runtime.Call("setGameObject", "sfc.isConnected", false);
            await Until(() => peer.GetName() == "Close Game 1" && window.Title == "Game 1 - VibeSkua X" &&
                NativeTitle(window) == window.Title &&
                ((string?)ToolTip.GetTip(status))?.Contains("Connection: Not connected") == true,
                "Logout left a stale close/status identity.");
            Program.Check(announcements == afterRename + 1, "Logout did not produce one updated status announcement.");
            Console.WriteLine("PASS: native Windows title and tooltip distinguish loaded/disconnected, connected without identity, named connection, and logout; unchanged polls do not repeat announcements.");
        }
        finally { window.Close(); }
    }

    private static void CheckNativeIcons(Window window)
    {
        var handle = window.TryGetPlatformHandle()!.Handle;
        var small = SendMessage(handle, 0x007F /* WM_GETICON */, IntPtr.Zero, IntPtr.Zero);
        var large = SendMessage(handle, 0x007F, new IntPtr(1), IntPtr.Zero);
        Program.Check(small != IntPtr.Zero && large != IntPtr.Zero, "Windows did not receive the small/large app icons.");
        CheckIconColors(small);
        CheckIconColors(large);
        string executable = Path.GetFullPath("VibeSkuaX.App/bin/Release/net10.0-windows/VibeSkuaX.exe");
        uint extracted = ExtractIconEx(executable, 0, out var exeLarge, out var exeSmall, 1);
        try
        {
            // Requesting both large and small icons returns two extracted handles.
            Program.Check(extracted > 0 && exeLarge != IntPtr.Zero && exeSmall != IntPtr.Zero,
                "The built app executable has no embedded icon.");
            CheckIconColors(exeLarge);
            CheckIconColors(exeSmall);
        }
        finally { DestroyIcon(exeLarge); DestroyIcon(exeSmall); }
    }

    private static void CheckIconColors(IntPtr handle)
    {
        using var icon = System.Drawing.Icon.FromHandle(handle);
        using var bitmap = icon.ToBitmap();
        bool cyan = false, dark = false;
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                cyan |= pixel.A > 200 && Math.Abs(pixel.R - 114) < 3 && Math.Abs(pixel.G - 212) < 3 && Math.Abs(pixel.B - 240) < 3;
                dark |= pixel.A > 200 && Math.Abs(pixel.R - 16) < 3 && Math.Abs(pixel.G - 32) < 3 && Math.Abs(pixel.B - 42) < 3;
            }
        Program.Check(cyan && dark, "Native icon does not contain the shared cyan badge and dark X.");
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, out IntPtr large, out IntPtr small, uint count);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
