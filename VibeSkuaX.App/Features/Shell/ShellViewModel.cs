using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VibeSkuaX.App.Sessions;

namespace VibeSkuaX.App.Features.Shell;

public partial class ShellViewModel : ObservableObject, IDisposable
{
    private readonly ObservableCollection<GameTabViewModel> _tabs = [];
    private readonly Func<GameSession> _createSession;
    private int _nextGame;
    private bool _disposed;

    public ShellViewModel() : this(() => new GameSession()) { }

    internal ShellViewModel(Func<GameSession> createSession)
    {
        _createSession = createSession;
        Tabs = new(_tabs);
        OpenGame();
    }

    public string Title => SelectedTab is { } tab ? $"{tab.Label} - VibeSkua X" : "VibeSkua X";
    public ReadOnlyObservableCollection<GameTabViewModel> Tabs { get; }
    public bool HasNoTabs => _tabs.Count == 0;
    public string Status => SelectedTab?.Status ?? "No games open";
    public string StatusDescription => SelectedTab?.AccessibleName ?? Status;
    public string GameListDescription => $"Open games ({_tabs.Count}). {StatusDescription}";
    public string GameListToolTip => $"Open games ({_tabs.Count})\n{SelectedTab?.StatusToolTip ?? "No games open"}";
    public bool HasTabs => _tabs.Count > 0;
    public bool IsLoading => SelectedTab?.IsLoading == true;
    public string? ErrorMessage => SelectedTab?.ErrorMessage;
    public bool HasError => SelectedTab?.HasError == true;

    [ObservableProperty]
    private GameTabViewModel? _selectedTab;

    partial void OnSelectedTabChanging(GameTabViewModel? value)
    {
        if (SelectedTab is not null) SelectedTab.PropertyChanged -= SelectedStateChanged;
    }

    partial void OnSelectedTabChanged(GameTabViewModel? value)
    {
        if (value is not null) value.PropertyChanged += SelectedStateChanged;
        NotifySelectedState();
    }

    private void SelectedStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GameTabViewModel.AccessibleName)) NotifySelectedState();
    }

    private void NotifySelectedState()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(StatusDescription));
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(GameListDescription));
        OnPropertyChanged(nameof(GameListToolTip));
    }

    [RelayCommand]
    private void OpenGame()
    {
        if (_disposed) return;
        var tab = new GameTabViewModel($"Game {++_nextGame}", _createSession);
        _tabs.Add(tab);
        SelectedTab = tab;
        OnPropertyChanged(nameof(HasNoTabs));
        OnPropertyChanged(nameof(HasTabs));
    }

    // Called only after the view has obtained confirmation for this exact tab.
    internal void CloseTab(GameTabViewModel tab)
    {
        int index = _tabs.IndexOf(tab);
        if (index < 0 || _disposed) return;
        if (SelectedTab == tab)
            SelectedTab = _tabs.Count == 1 ? null : _tabs[index + 1 < _tabs.Count ? index + 1 : index - 1];
        _tabs.RemoveAt(index);
        tab.Dispose();
        OnPropertyChanged(nameof(HasNoTabs));
        OnPropertyChanged(nameof(HasTabs));
        NotifySelectedState();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SelectedTab = null;
        foreach (var tab in _tabs) tab.Dispose();
        _tabs.Clear();
        OnPropertyChanged(nameof(HasNoTabs));
        OnPropertyChanged(nameof(HasTabs));
    }
}
