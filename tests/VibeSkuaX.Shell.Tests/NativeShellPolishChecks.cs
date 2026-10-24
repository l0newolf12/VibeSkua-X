using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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

internal static partial class NativeSmoke
{
    private static async Task CheckNativeGameList(ShellWindow window, GameTabViewModel first, GameTabViewModel second,
        IntPtr firstFlash, IntPtr secondFlash)
    {
        var model = (ShellViewModel)window.DataContext!;
        var button = window.FindControl<DropDownButton>("GameListButton")!;
        var tabs = window.FindControl<FATabView>("AccountTabs")!;
        var handle = window.TryGetPlatformHandle()!.Handle;
        var previous = WindowRect(handle);
        double previousWidth = window.Width;
        window.Width = 720;
        await Until(() => window.ClientSize.Width == 720, "Game-list overflow fixture did not reach minimum width.");
        var workArea = (window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary ?? window.Screens.All.First()).WorkingArea;
        SetWindowPos(handle, IntPtr.Zero, workArea.X + 80, workArea.Y + 80, 0, 0, 0x0015);
        try
        {
            await ClientClick(window, button);
            await Until(() => window.GameListFlyout.IsOpen && TopLevel.GetTopLevel(window.GameListFlyout.Popup.Child!) is PopupRoot,
                "Open-games menu is not a native popup above Flash.");
            var popup = (PopupRoot)TopLevel.GetTopLevel(window.GameListFlyout.Popup.Child!)!;
            var popupHandle = popup.TryGetPlatformHandle()!.Handle;
            await Until(() => Overlaps(WindowRect(popupHandle), WindowRect(secondFlash)), "Actual game list does not overlap the native game.");
            Program.Check(IsAbove(popupHandle, handle), "Game-list popup is below its Flash owner.");
            var items = window.GameListFlyout.Items.Cast<FAToggleMenuFlyoutItem>().ToArray();
            Program.Check(items.Length == 2 && items.Single(i => i.IsChecked).Tag == second, "Native game list has stale owners or selection.");
            await ClientClick(popup, items.Single(i => i.Tag == first));
            await Until(() => model.SelectedTab == first && !window.GameListFlyout.IsOpen &&
                (GetFocus() == firstFlash || IsChild(firstFlash, GetFocus())), "Menu pointer selection did not return focus to the chosen game.");
            Program.Check(IsWindow(firstFlash) && IsWindow(secondFlash) && first.Session.Runtime!.Call<bool>("isTrue") &&
                second.Session.Runtime!.Call<bool>("isTrue"), "Menu selection destroyed a live game.");
            var header = tabs.ContainerFromItem(first);
            var viewport = tabs.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ScrollContentPresenter>().Single();
            var headerPosition = header.TranslatePoint(default, viewport)!.Value;
            Program.Check(headerPosition.X >= -.5 && headerPosition.X + header.Bounds.Width <= viewport.Bounds.Width + .5,
                "Pointer menu selection did not reveal its overflow tab header.");

            SetFocus(handle);
            button.Focus(NavigationMethod.Tab);
            NativeKey(handle, 0x20 /* Space */);
            await Until(() => window.GameListFlyout.IsOpen && window.GameListFlyout.Popup.Child!.IsKeyboardFocusWithin,
                "Keyboard Space did not open/focus the native game list.");
            popup = (PopupRoot)TopLevel.GetTopLevel(window.GameListFlyout.Popup.Child!)!;
            NativeKey(popup.TryGetPlatformHandle()!.Handle, 0x1B /* Escape */);
            await Until(() => !window.GameListFlyout.IsOpen && button.IsFocused && GetFocus() == handle,
                "Escape did not dismiss the list and restore the keyboard opener.");
            NativeKey(handle, 0x20);
            await Until(() => window.GameListFlyout.IsOpen && window.GameListFlyout.Popup.Child!.IsKeyboardFocusWithin,
                "Native game list did not reopen from the keyboard.");
            popup = (PopupRoot)TopLevel.GetTopLevel(window.GameListFlyout.Popup.Child!)!;
            var target = window.GameListFlyout.Items.Cast<FAToggleMenuFlyoutItem>().Single(i => i.Tag == second);
            target.Focus(NavigationMethod.Directional);
            NativeKey(popup.TryGetPlatformHandle()!.Handle, 0x0D /* Enter */);
            await Until(() => !window.GameListFlyout.IsOpen && model.SelectedTab == second &&
                tabs.ContainerFromItem(second)?.IsFocused == true && GetFocus() == handle,
                "Keyboard menu selection did not preserve shell navigation focus.");

            // Actual Windows activation must drive the shared inactive styles, including future dialogs.
            window.Activate();
            await Until(() => window.IsActive, "Could not activate the test shell for the inactive-chrome check.");
            var chrome = window.FindChrome<Border>("TitleBar")!;
            var bounds = chrome.Bounds;
            var dialog = new CloseGameWindow(second.Label);
            dialog.Show(window);
            try
            {
                dialog.Activate();
                await Until(() => dialog.IsActive && !window.IsActive, "Owned window did not deactivate the shell.");
                Program.Check(chrome.BorderBrush?.ToString() == "#ff26262b" && chrome.Bounds == bounds,
                    "Native deactivation did not apply the shared quiet chrome without resizing.");
                window.Activate();
                await Until(() => window.IsActive && !dialog.IsActive, "Shell reactivation did not deactivate the auxiliary window.");
                await Until(() => chrome.BorderBrush?.ToString() == "#ff35353c" &&
                    dialog.FindChrome<TextBlock>("TitleText")!.Foreground?.ToString() == "#ffa3a3ae",
                    $"Shared active/inactive chrome was not applied to both windows: border={chrome.BorderBrush}, dialog text={dialog.FindChrome<TextBlock>("TitleText")!.Foreground}.");
            }
            finally { dialog.Close(false); }
            // Return to the existing fixture's pointer/game focus policy.
            window.Width = previousWidth;
            await Until(() => window.ClientSize.Width == previousWidth, "Menu fixture did not restore the two-tab viewport.");
            await ClientClick(window, tabs.ContainerFromItem(first));
            await ClientClick(window, tabs.ContainerFromItem(second));
            await Until(() => GetFocus() == secondFlash || IsChild(secondFlash, GetFocus()), "Could not restore Flash focus after menu checks.");
            File.WriteAllText(Path.Combine(_artifacts, "native-game-list.json"), JsonSerializer.Serialize(new
            {
                NativePopupAboveFlash = true, PointerSelectionPreservesGames = true,
                EscapeRestoresOpener = true, KeyboardSelectionPreservesTabFocus = true,
                ActualActivationStylesSharedWindows = true
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PASS: actual game-list popup above Flash; pointer/keyboard selection, Escape focus, session survival, and shared native activation styling.");
        }
        finally
        {
            window.GameListFlyout.Hide();
            window.Width = previousWidth;
            SetWindowPos(handle, IntPtr.Zero, previous.Left, previous.Top, 0, 0, 0x0015);
        }
    }

    private static async Task CheckNativeRecoveryFocus()
    {
        var model = new ShellViewModel(() => new GameSession(new GameSessionOptions
        {
            SwfPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "InventoryHarness.swf")
        }));
        var window = new ShellWindow(model)
        {
            ShowInTaskbar = false, ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(-30000, -30000)
        };
        window.Show();
        try
        {
            var tab = model.SelectedTab!;
            await WaitReady(tab);
            window.Activate();
            await Until(() => window.IsActive, "Recovery focus fixture could not activate.");
            var close = window.RequestCloseTabAsync(tab);
            var dialog = window.CloseConfirmation!;
            await Until(() => dialog.IsActive && dialog.FindControl<Button>("CancelGameClose")!.IsFocused,
                "Recovery focus confirmation did not receive focus.");
            tab.Session.Messenger.Send(new FlashRuntimeFailedMessage(new Exception("Failure during confirmation")));
            await Until(() => tab.HasError, "Confirmation failure was not processed.");
            Program.Check(dialog.IsActive && dialog.FindControl<Button>("CancelGameClose")!.IsFocused &&
                !window.FindControl<Button>("RetryGameButton")!.IsFocused, "Failure stole focus from confirmation.");
            await NativeClick(dialog, "CloseButton", 20);
            await close;
            await Until(() => window.FindControl<Button>("RetryGameButton")!.IsFocused,
                "Cancelling game-oriented confirmation did not focus recovery for the failed game.");
            tab.RetryCommand.Execute(null);
            await WaitReady(tab);
            var other = new CloseGameWindow("Another window");
            other.Show(window);
            try
            {
                other.Activate();
                await Until(() => other.IsActive && !window.IsActive, "Other window did not receive focus.");
                tab.Session.Messenger.Send(new FlashRuntimeFailedMessage(new Exception("Inactive owner failure")));
                await Until(() => tab.HasError, "Inactive failure was not processed.");
                Program.Check(other.IsActive && other.FindControl<Button>("CancelGameClose")!.IsFocused,
                    "Failure reactivated the background shell or moved focus out of another window.");
            }
            finally { other.Close(false); }
            Console.WriteLine("PASS: failures preserve confirmation/other-window focus; returning to a failed game focuses Retry.");
        }
        finally { window.Close(); }
    }

    private static void NativeKey(IntPtr handle, int key)
    {
        SendMessage(handle, 0x0100 /* WM_KEYDOWN */, new IntPtr(key), new IntPtr(1));
        SendMessage(handle, 0x0101 /* WM_KEYUP */, new IntPtr(key), new IntPtr(unchecked((int)0xC0000001)));
    }

    private static string NativeTitle(Window window)
    {
        var title = new StringBuilder(512);
        GetWindowText(window.TryGetPlatformHandle()!.Handle, title, title.Capacity);
        return title.ToString();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle, StringBuilder text, int count);
}
