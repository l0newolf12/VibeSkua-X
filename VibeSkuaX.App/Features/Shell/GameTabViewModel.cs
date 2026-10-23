using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VibeSkuaX.App.Sessions;

namespace VibeSkuaX.App.Features.Shell;

/// <summary>Owns a game for the lifetime of a tab, independently of native attachment.</summary>
public sealed partial class GameTabViewModel : ObservableObject, IDisposable
{
    private readonly Func<GameSession> _createSession;
    private readonly string _fallbackLabel;
    private bool _disposed;

    internal GameTabViewModel(string label, Func<GameSession> createSession)
    {
        _fallbackLabel = label;
        _createSession = createSession;
        Session = _createSession();
        Session.StateChanged += StateChanged;
        Session.CharacterNameChanged += IdentityChanged;
    }

    public string Label => Session.CharacterName ?? _fallbackLabel;
    internal GameSession Session { get; private set; }
    public bool HasError => Session.State == GameSessionState.Failed;
    public bool IsLoading => Session.State is GameSessionState.Starting or GameSessionState.Loading;
    public string? ErrorMessage => Session.Error;
    public string Status => Session.State switch
    {
        GameSessionState.Starting => "Starting game…",
        GameSessionState.Loading => "Loading game…",
        GameSessionState.Ready => "Game loaded",
        GameSessionState.Failed => "Game unavailable",
        _ => "Game closed"
    };
    public string AccessibleName => $"{Label}: {Status}";

    [RelayCommand(CanExecute = nameof(HasError))]
    private void Retry()
    {
        if (_disposed || !HasError) return;
        Session.StateChanged -= StateChanged;
        Session.CharacterNameChanged -= IdentityChanged;
        Session.Dispose();
        Session = _createSession();
        Session.StateChanged += StateChanged;
        Session.CharacterNameChanged += IdentityChanged;
        OnPropertyChanged(nameof(Session));
        IdentityChanged();
        StateChanged(Session.State, Session.Error);
    }

    private void IdentityChanged()
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(AccessibleName));
    }

    private void StateChanged(GameSessionState state, string? error)
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(AccessibleName));
        RetryCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Session.StateChanged -= StateChanged;
        Session.CharacterNameChanged -= IdentityChanged;
        Session.Dispose();
        StateChanged(Session.State, null);
    }
}
