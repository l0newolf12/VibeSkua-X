using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Collections.Specialized;
using System.ComponentModel;
using FluentAvalonia.UI.Controls;
using VibeSkuaX.App.Controls;
using Button = Avalonia.Controls.Button;

namespace VibeSkuaX.App.Features.Shell;

public partial class ShellWindow : Window
{
    private readonly ShellViewModel _viewModel;
    private readonly Dictionary<GameTabViewModel, FlashViewport> _viewports = [];
    private bool _closed;
    internal CloseGameWindow? CloseConfirmation { get; private set; }
    internal FlashViewport? SelectedViewport => _viewModel.SelectedTab is { } tab ? _viewports.GetValueOrDefault(tab) : null;

    public ShellWindow() : this(new ShellViewModel()) { }

    internal ShellWindow(ShellViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        WindowChrome.MaximizeToggled += RestoreGameFocus;
        DataContext = _viewModel;
        ((INotifyCollectionChanged)_viewModel.Tabs).CollectionChanged += TabsChanged;
        _viewModel.PropertyChanged += ModelChanged;
        SynchronizeViewports();
    }

    private void TabsChanged(object? sender, NotifyCollectionChangedEventArgs e) => SynchronizeViewports();
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateVisibility();
        if (e.PropertyName == nameof(ShellViewModel.SelectedTab)) FocusSelectedGame();
    }
    private void TabChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameTabViewModel.Session)) SynchronizeViewports();
        else UpdateVisibility();
        if (sender == _viewModel.SelectedTab && e.PropertyName == nameof(GameTabViewModel.Status) &&
            _viewModel.SelectedTab is { IsLoading: false, HasError: false }) FocusSelectedGame();
    }

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
        if (!_closed && CloseConfirmation is null) SelectedViewport?.FocusGame();
    }, DispatcherPriority.Loaded);

    private async void TabCloseRequested(FATabView sender, FATabViewTabCloseRequestedEventArgs e)
    {
        if (e.Item is GameTabViewModel tab) await RequestCloseTabAsync(tab);
    }

    internal async Task RequestCloseTabAsync(GameTabViewModel tab)
    {
        if (_closed || CloseConfirmation is not null || !_viewModel.Tabs.Contains(tab)) return;
        var dialog = new CloseGameWindow(tab.Label);
        CloseConfirmation = dialog;
        try
        {
            if (await dialog.ShowDialog<bool>(this) && !_closed) _viewModel.CloseTab(tab);
        }
        finally
        {
            CloseConfirmation = null;
            FocusSelectedGame();
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        // Library template buttons keep their native keyboard behavior and explicit names.
        var add = AccountTabs.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "AddButton");
        if (add is not null) AutomationProperties.SetName(add, "Open game");
    }

    private void RestoreGameFocus(object? sender, EventArgs e) => SelectedViewport?.FocusGame();

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
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
