using Avalonia.Controls;
using Avalonia.Platform;
using VibeSkuaX.App.Sessions;

namespace VibeSkuaX.App.Controls;

/// <summary>Attaches a session to Avalonia layout; only explicit Dispose ends the game.</summary>
public sealed class FlashViewport : NativeControlHost, IDisposable
{
    private readonly GameSession _session;
    private bool _disposed;
    private bool _placeholder;

    public FlashViewport() : this(new GameSession()) { }
    internal FlashViewport(GameSessionOptions options) : this(new GameSession(options)) { }
    private FlashViewport(GameSession session)
    {
        _session = session;
        _session.StateChanged += OnStateChanged;
    }

    internal GameSession Session => _session;
    internal bool IsDisposed => _disposed;
    internal IntPtr ContainerHandle => _session.ContainerHandle;
    public event Action<string, string?>? StatusChanged;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // Failed sessions can still be navigated back to, to display their error.
        _placeholder = _session.State == GameSessionState.Failed;
        return _placeholder ? base.CreateNativeControlCore(parent) : new PlatformHandle(_session.EnsureControl(), "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (_placeholder) base.DestroyNativeControlCore(control);
        // Avalonia detaches the owned child before releasing its attachment. The
        // session retains that child until an explicit account/window close.
    }

    private void OnStateChanged(GameSessionState state, string? error) => StatusChanged?.Invoke(state switch
    {
        GameSessionState.Starting => "Starting game…",
        GameSessionState.Loading => "Loading game…",
        GameSessionState.Ready => "Game loaded",
        GameSessionState.Failed => "Game unavailable",
        _ => "Game closed"
    }, error);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.StateChanged -= OnStateChanged;
        _session.Dispose();
    }
}
