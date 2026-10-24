using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Controls.Primitives;
using CommunityToolkit.Mvvm.Input;
using VibeSkuaX.App.Controls;
using VibeSkuaX.App.Sessions;
using Button = Avalonia.Controls.Button;
using ToolTip = Avalonia.Controls.ToolTip;

namespace VibeSkuaX.App.Features.Shell;

public partial class ShellWindow : Window
{
    private readonly ShellViewModel _viewModel;
    private readonly Dictionary<GameTabViewModel, FlashViewport> _viewports = [];
    private bool _closed;
    private bool _preserveTabFocus;
    private bool _copyingDiagnostics;
    private bool _gameListSelectionMade;
    internal FAMenuFlyout GameListFlyout { get; } = new() { Placement = PlacementMode.BottomEdgeAlignedRight };
    internal CloseGameWindow? CloseConfirmation { get; private set; }
    internal FlashViewport? SelectedViewport => _viewModel.SelectedTab is { } tab ? _viewports.GetValueOrDefault(tab) : null;

    public ShellWindow() : this(new ShellViewModel()) { }

    internal ShellWindow(ShellViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        WindowChrome.MaximizeToggled += RestoreGameFocus;
        DataContext = _viewModel;
        AccountTabs.TemplateApplied += (_, e) =>
        {
            // The pinned control overwrites this minimum with a constrained DesiredSize
            // of zero during initial overflow. Keep the app's reserved drag space above
            // that local value, while leaving the library's width/scroll calculations intact.
            var grid = e.NameScope.Find<Grid>("TabContainerGrid");
            if (grid is { ColumnDefinitions.Count: 4 })
                grid.ColumnDefinitions[3].SetValue(ColumnDefinition.MinWidthProperty,
                    (double)this.FindResource("VsWidthTitleDrag")!, Avalonia.Data.BindingPriority.Animation);
        };
        GameListFlyout.Popup.ShouldUseOverlayLayer = false;
        GameListFlyout.FlyoutPresenterClasses.Add("vsx-game-list");
        GameListFlyout.Opening += (_, _) =>
        {
            _gameListSelectionMade = false;
            PopulateGameList();
        };
        GameListFlyout.Closed += (_, _) => RestoreFocusAfterGameList();
        GameListFlyout.Popup.AddHandler(KeyDownEvent, (_, _) => _preserveTabFocus = true, RoutingStrategies.Tunnel);
        GameListButton.Flyout = GameListFlyout;
        // Unlike TextBlock's peer, the pinned button peer does not report attached Name changes.
        GameListButton.PropertyChanged += (_, e) =>
        {
            if (e.Property == AutomationProperties.NameProperty)
                ControlAutomationPeer.CreatePeerForElement(GameListButton).RaisePropertyChangedEvent(
                    AutomationElementIdentifiers.NameProperty, e.OldValue, e.NewValue);
        };
        // The pinned library's handlers assume control items and realized containers.
        // Own the three existing shortcuts over the actual game collection instead.
        AccountTabs.KeyBindings.Clear();
        AccountTabs.KeyBindings.Add(new KeyBinding { Gesture = new(Key.Tab, KeyModifiers.Control), Command = new RelayCommand(() => CycleTab(1)) });
        AccountTabs.KeyBindings.Add(new KeyBinding { Gesture = new(Key.Tab, KeyModifiers.Control | KeyModifiers.Shift), Command = new RelayCommand(() => CycleTab(-1)) });
        AccountTabs.KeyBindings.Add(new KeyBinding { Gesture = new(Key.F4, KeyModifiers.Control), Command = new AsyncRelayCommand(CloseSelectedTabAsync) });
        foreach (var binding in AccountTabs.KeyBindings)
            GameListButton.KeyBindings.Add(new KeyBinding { Gesture = binding.Gesture, Command = binding.Command });
        AccountHeader.AddHandler(PointerPressedEvent, (_, _) => _preserveTabFocus = false, RoutingStrategies.Tunnel);
        AccountHeader.AddHandler(KeyDownEvent, (_, _) => _preserveTabFocus = true, RoutingStrategies.Tunnel);
        AccountHeader.GotFocus += (_, e) =>
        {
            if (e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional) _preserveTabFocus = true;
        };
        AccountTabs.SizeChanged += (_, _) => RevealSelectedTab();
        ((INotifyCollectionChanged)_viewModel.Tabs).CollectionChanged += TabsChanged;
        _viewModel.PropertyChanged += ModelChanged;
        SynchronizeViewports();
    }

    private void TabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        GameListFlyout.Hide();
        GameListFlyout.Items.Clear();
        SynchronizeViewports();
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateVisibility();
        if (e.PropertyName == nameof(ShellViewModel.SelectedTab))
        {
            RefreshGameList();
            if (_preserveTabFocus) RevealSelectedTab();
            else FocusSelectedGame();
        }
        if (e.PropertyName is nameof(ShellViewModel.SelectedTab) or nameof(ShellViewModel.ErrorMessage))
        {
            CopyDiagnosticsButton.Content = "Copy details";
            ToolTip.SetTip(CopyDiagnosticsButton, null);
        }
    }
    private void TabChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameTabViewModel.Session)) SynchronizeViewports();
        else UpdateVisibility();
        if (e.PropertyName == nameof(GameTabViewModel.Label) && sender is GameTabViewModel renamed)
            UpdateTabCloseName(renamed);
        if (e.PropertyName == nameof(GameTabViewModel.AccessibleName)) RefreshGameList();
        if (sender == _viewModel.SelectedTab && e.PropertyName == nameof(GameTabViewModel.Status) &&
            _viewModel.SelectedTab is { HasError: true } failed)
            FocusRetryAfterFailure(failed, failed.Session);
        if (sender == _viewModel.SelectedTab && e.PropertyName == nameof(GameTabViewModel.Status) &&
            _viewModel.SelectedTab is { IsLoading: false, HasError: false } && !_preserveTabFocus) FocusSelectedGame();
    }

    private void PopulateGameList()
    {
        GameListFlyout.Items.Clear();
        foreach (var tab in _viewModel.Tabs)
        {
            var item = new FAToggleMenuFlyoutItem { Tag = tab };
            item.Click += (_, _) =>
            {
                if (!_viewModel.Tabs.Contains(tab)) return;
                _gameListSelectionMade = true;
                _viewModel.SelectedTab = tab;
                RevealSelectedTab();
            };
            GameListFlyout.Items.Add(item);
        }
        RefreshGameList();
    }

    private void RefreshGameList()
    {
        foreach (var item in GameListFlyout.Items.OfType<FAToggleMenuFlyoutItem>())
        {
            if (item.Tag is not GameTabViewModel tab) continue;
            item.Text = tab.MenuLabel;
            item.IsChecked = tab == _viewModel.SelectedTab;
            ToolTip.SetTip(item, tab.StatusToolTip);
            AutomationProperties.SetName(item, tab.AccessibleName);
        }
    }

    private void RestoreFocusAfterGameList()
    {
        bool selected = _gameListSelectionMade;
        bool keyboard = _preserveTabFocus;
        var tab = _viewModel.SelectedTab;
        Dispatcher.UIThread.Post(() =>
        {
            if (_closed || !IsActive || CloseConfirmation is not null || GameListFlyout.IsOpen || _viewModel.SelectedTab != tab) return;
            if (keyboard && !selected) GameListButton.Focus(NavigationMethod.Tab);
            else if (keyboard) RevealSelectedTab();
            else if (tab?.HasError == true) FocusRetry();
            else FocusSelectedGame();
        }, DispatcherPriority.Loaded);
    }

    private void FocusRetryAfterFailure(GameTabViewModel tab, GameSession session) => Dispatcher.UIThread.Post(() =>
    {
        if (!_closed && IsActive && CloseConfirmation is null && !GameListFlyout.IsOpen &&
            !AccountHeader.IsKeyboardFocusWithin && _viewModel.SelectedTab == tab && tab.Session == session && tab.HasError &&
            (session.HadFocusOnFailure || FocusManager?.GetFocusedElement() is null))
            FocusRetry();
    }, DispatcherPriority.Loaded);

    private void FocusRetry()
    {
        if (_closed || !IsActive || CloseConfirmation is not null || GameListFlyout.IsOpen || !_viewModel.HasError) return;
        // Avalonia's logical Focus does not move Win32 focus out of a native child.
        // The failed Flash control can leave that focus on another WinForms child.
        if (TryGetPlatformHandle() is { HandleDescriptor: "HWND" } handle) FocusNativeWindow(handle.Handle);
        RetryGameButton.Focus(NavigationMethod.Tab);
    }

    [DllImport("user32.dll", EntryPoint = "SetFocus")]
    private static extern IntPtr FocusNativeWindow(IntPtr window);

    private void SynchronizeViewports()
    {
        foreach (var (tab, viewport) in _viewports.ToArray())
        {
            if (_viewModel.Tabs.Contains(tab) && ReferenceEquals(tab.Session, viewport.Session)) continue;
            tab.PropertyChanged -= TabChanged;
            ShellLayout.Children.Remove(viewport);
            viewport.Dispose();
            _viewports.Remove(tab);
        }
        foreach (var tab in _viewModel.Tabs)
        {
            if (_viewports.ContainsKey(tab)) continue;
            var viewport = new FlashViewport(tab.Session);
            Grid.SetRow(viewport, 1);
            _viewports.Add(tab, viewport);
            tab.PropertyChanged += TabChanged;
            ShellLayout.Children.Add(viewport);
        }
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        foreach (var (tab, viewport) in _viewports)
            viewport.IsVisible = tab == _viewModel.SelectedTab && !tab.HasError;
    }

    private void FocusSelectedGame() => Dispatcher.UIThread.Post(() =>
    {
        if (!_closed && CloseConfirmation is null && !GameListFlyout.IsOpen && !_preserveTabFocus) SelectedViewport?.FocusGame();
    }, DispatcherPriority.Loaded);

    private void CycleTab(int direction)
    {
        _preserveTabFocus = true;
        int count = _viewModel.Tabs.Count;
        if (count == 0) return;
        int index = _viewModel.SelectedTab is { } selected ? _viewModel.Tabs.IndexOf(selected) : (direction > 0 ? -1 : 0);
        _viewModel.SelectedTab = _viewModel.Tabs[(index + direction + count) % count];
        RevealSelectedTab();
    }

    private Task CloseSelectedTabAsync()
    {
        _preserveTabFocus = true;
        return _viewModel.SelectedTab is { } tab ? RequestCloseTabAsync(tab) : Task.CompletedTask;
    }

    private void RevealSelectedTab() => Dispatcher.UIThread.Post(() =>
    {
        if (_closed || CloseConfirmation is not null || _viewModel.SelectedTab is not { } tab) return;
        AccountTabs.GetVisualDescendants().OfType<FATabViewListView>().FirstOrDefault()?.ScrollIntoView(tab);
        Dispatcher.UIThread.Post(() =>
        {
            if (!_closed && CloseConfirmation is null && _preserveTabFocus && _viewModel.SelectedTab == tab)
                AccountTabs.ContainerFromItem(tab)?.Focus(NavigationMethod.Tab);
        }, DispatcherPriority.Loaded);
    }, DispatcherPriority.Loaded);

    private void TabItemLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is FATabViewItem { Header: GameTabViewModel tab }) UpdateTabCloseName(tab);
    }

    private void UpdateTabCloseName(GameTabViewModel tab)
    {
        var close = AccountTabs.ContainerFromItem(tab)?.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "CloseButton");
        if (close is not null) AutomationProperties.SetName(close, $"Close {tab.Label}");
    }

    private async void TabCloseRequested(FATabView sender, FATabViewTabCloseRequestedEventArgs e)
    {
        if (e.Item is GameTabViewModel tab) await RequestCloseTabAsync(tab);
    }

    internal async Task RequestCloseTabAsync(GameTabViewModel tab)
    {
        if (_closed || CloseConfirmation is not null || !_viewModel.Tabs.Contains(tab)) return;
        // Owner reactivation may restore a tab with NavigationMethod.Tab. Remember
        // the caller's policy before modality changes focus, rather than reading it later.
        bool restoreTabFocus = _preserveTabFocus && AccountHeader.IsKeyboardFocusWithin;
        var dialog = new CloseGameWindow(tab.Label);
        CloseConfirmation = dialog;
        try
        {
            if (await dialog.ShowDialog<bool>(this) && !_closed) _viewModel.CloseTab(tab);
        }
        finally
        {
            CloseConfirmation = null;
            _preserveTabFocus = restoreTabFocus;
            if (!_closed)
            {
                if (_viewModel.HasNoTabs) OpenGameButton.Focus(NavigationMethod.Tab);
                else if (_preserveTabFocus) RevealSelectedTab();
                else if (_viewModel.HasError) Dispatcher.UIThread.Post(FocusRetry, DispatcherPriority.Loaded);
                else FocusSelectedGame();
            }
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        // Library template buttons keep their native keyboard behavior and explicit names.
        var add = AccountTabs.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "AddButton");
        if (add is not null)
        {
            AutomationProperties.SetName(add, "Open game");
            ToolTip.SetTip(add, "Open game");
        }
    }

    internal string BuildDiagnostics()
    {
        var tab = _viewModel.SelectedTab;
        return $"VibeSkua X {typeof(ShellWindow).Assembly.GetName().Version}{Environment.NewLine}" +
            $"Game: {tab?.Label ?? "None"}{Environment.NewLine}" +
            $"Client: {tab?.Session.GameFile ?? "Not loaded"}{Environment.NewLine}" +
            $"Status: {_viewModel.Status}{Environment.NewLine}{Environment.NewLine}{_viewModel.ErrorMessage}";
    }

    private async void CopyDiagnosticsClicked(object? sender, RoutedEventArgs e) => await CopyDiagnosticsAsync();

    internal async Task CopyDiagnosticsAsync()
    {
        if (_copyingDiagnostics || !_viewModel.HasError) return;
        _copyingDiagnostics = true;
        var tab = _viewModel.SelectedTab;
        var session = tab?.Session;
        string diagnostics = BuildDiagnostics();
        try
        {
            if (Clipboard is null) throw new InvalidOperationException("Clipboard is unavailable.");
            await Clipboard.SetTextAsync(diagnostics);
            if (!_closed && _viewModel.SelectedTab == tab && tab?.Session == session && tab?.HasError == true)
            {
                CopyDiagnosticsButton.Content = "Copied";
                ToolTip.SetTip(CopyDiagnosticsButton, null);
            }
        }
        catch (Exception)
        {
            if (!_closed && _viewModel.SelectedTab == tab && tab?.Session == session && tab?.HasError == true)
            {
                CopyDiagnosticsButton.Content = "Copy failed";
                ToolTip.SetTip(CopyDiagnosticsButton, "Select the error details and press Ctrl+C to copy them.");
            }
        }
        finally { _copyingDiagnostics = false; }
    }

    private void RestoreGameFocus(object? sender, EventArgs e)
    {
        if (!_preserveTabFocus || !AccountTabs.IsKeyboardFocusWithin) SelectedViewport?.FocusGame();
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        GameListFlyout.Hide();
        GameListFlyout.Items.Clear();
        WindowChrome.MaximizeToggled -= RestoreGameFocus;
        CloseConfirmation?.Close(false);
        ((INotifyCollectionChanged)_viewModel.Tabs).CollectionChanged -= TabsChanged;
        _viewModel.PropertyChanged -= ModelChanged;
        foreach (var (tab, viewport) in _viewports)
        {
            tab.PropertyChanged -= TabChanged;
            viewport.Dispose();
        }
        _viewports.Clear();
        _viewModel.Dispose();
        base.OnClosed(e);
    }
}
