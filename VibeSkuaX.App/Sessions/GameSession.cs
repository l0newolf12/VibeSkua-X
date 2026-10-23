using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Messaging;
using VibeSkuaX.Runtime.Windows.Flash;
using WinForms = System.Windows.Forms;

namespace VibeSkuaX.App.Sessions;

internal enum GameSessionState { Starting, Loading, Ready, Failed, Disposed }

internal sealed record GameSessionOptions
{
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(45);
    public string? VersionEndpoint { get; init; }
    public string? GameBaseUrl { get; init; }
    public string SwfPath { get; init; } = Path.Combine(AppContext.BaseDirectory, "skua.swf");
}

/// <summary>Owns one game independently of the viewport's native attachment.</summary>
internal sealed class GameSession : IDisposable
{
    private readonly GameSessionOptions _options;
    private readonly WeakReferenceMessenger _messenger = new();
    private readonly CancellationTokenSource _startup = new();
    private WinForms.Panel? _container;
    private FlashUtil? _runtime;

    public GameSession(GameSessionOptions? options = null)
    {
        _options = options ?? new();
        if (_options.StartupTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options));
        _messenger.Register<GameSession, FlashErrorMessage>(this, (session, message) =>
            Dispatcher.UIThread.Post(() =>
            {
                if (session.State is GameSessionState.Disposed or GameSessionState.Failed) return;
                Trace.WriteLine($"Flash operation {message.Function} failed: {message.Exception}");
                session.OperationFailed?.Invoke(message);
            }));
        _messenger.Register<GameSession, FlashRuntimeFailedMessage>(this, (session, message) =>
            Dispatcher.UIThread.Post(() => session.Fail("The Flash runtime disconnected. " + message.Exception.Message)));
    }

    public GameSessionState State { get; private set; } = GameSessionState.Starting;
    public string? Error { get; private set; }
    public string? GameFile { get; private set; }
    public IntPtr ContainerHandle => _container is { IsHandleCreated: true } ? _container.Handle : IntPtr.Zero;
    internal FlashUtil? Runtime => _runtime;
    internal IMessenger Messenger => _messenger;
    public event Action<GameSessionState, string?>? StateChanged;
    public event Action<FlashErrorMessage>? OperationFailed;

    public IntPtr EnsureControl()
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(State == GameSessionState.Disposed, this);
        if (State == GameSessionState.Failed) throw new InvalidOperationException("The game session has failed.");
        if (_container is null)
        {
            _container = new WinForms.Panel { BackColor = System.Drawing.Color.FromArgb(19, 19, 19) };
            _container.CreateControl();
            Dispatcher.UIThread.Post(Start, DispatcherPriority.Loaded);
        }
        return _container.Handle;
    }

    private void Start()
    {
        if (State != GameSessionState.Starting || _container is null) return;
        SetState(GameSessionState.Loading);
        _ = WatchStartup(_startup.Token);
        try
        {
            _runtime = new FlashUtil(_messenger);
            _runtime.FlashCall += OnFlashCall;
            _runtime.InitializeFlash(_container, _options.SwfPath);
        }
        catch (FileNotFoundException)
        {
            Fail("The bundled skua.swf is missing. Rebuild VibeSkua X or restore its application files.");
        }
        catch (Exception exception)
        {
            Fail("Flash could not start. Make sure the 64-bit CleanFlash ActiveX runtime is installed. " + exception.GetBaseException().Message);
        }
    }

    private async Task WatchStartup(CancellationToken token)
    {
        try
        {
            await Task.Delay(_options.StartupTimeout, token);
            Fail("The game did not finish loading before the startup deadline. Check the network connection and try again.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private void OnFlashCall(string function, params object[] arguments) =>
        Dispatcher.UIThread.Post(() => ProcessCallback(function, arguments));

    internal void ProcessCallback(string function, object[] arguments)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (State != GameSessionState.Loading) return;
        switch (function)
        {
            case "requestLoadGame":
                _runtime?.Call("loadClient", _options.VersionEndpoint!, _options.GameBaseUrl!);
                break;
            case "loaded":
                GameFile = arguments.ElementAtOrDefault(0)?.ToString();
                _startup.Cancel();
                SetState(GameSessionState.Ready);
                break;
            case "gameLoadFailed":
                string phase = arguments.ElementAtOrDefault(0)?.ToString() ?? "game load";
                string reason = arguments.ElementAtOrDefault(1)?.ToString() ?? "Unknown loading error.";
                Fail($"The {phase} failed. {reason}");
                break;
        }
    }

    private void Fail(string error)
    {
        if (State is GameSessionState.Disposed or GameSessionState.Failed) return;
        ReleaseRuntime();
        SetState(GameSessionState.Failed, error);
    }

    private void SetState(GameSessionState state, string? error = null)
    {
        State = state;
        Error = error;
        StateChanged?.Invoke(state, error);
    }

    private void ReleaseRuntime()
    {
        _startup.Cancel();
        if (_runtime is not null)
        {
            _runtime.FlashCall -= OnFlashCall;
            _runtime.Dispose();
            _runtime = null;
        }
        _container?.Dispose();
        _container = null;
    }

    public void Dispose()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (State == GameSessionState.Disposed) return;
        _messenger.UnregisterAll(this);
        ReleaseRuntime();
        _startup.Dispose();
        SetState(GameSessionState.Disposed);
    }
}
