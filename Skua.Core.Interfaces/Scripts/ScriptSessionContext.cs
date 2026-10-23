using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace Skua.Core.Interfaces;

/// <summary>Services and legacy bot access for one logical engine session.</summary>
public sealed class ScriptSessionContext : IServiceProvider, IDisposable
{
    private sealed record Frame(ScriptSessionContext Session, CancellationToken Cancellation);
    private static readonly AsyncLocal<Frame?> Active = new();
    private readonly IServiceProvider _provider;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _lifetimeToken;
    private bool _disposed;

    public ScriptSessionContext(IServiceProvider provider)
    {
        _provider = provider;
        _lifetimeToken = _lifetime.Token;
        ScopedIoc.ConfigureServices(this);
    }
    public static ScriptSessionContext? Current => Active.Value?.Session;
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public WeakReferenceMessenger WeakMessenger { get; } = new();
    public StrongReferenceMessenger Messenger { get; } = new();
    public Ioc ScopedIoc { get; } = new();
    public bool IsDisposed => _disposed;
    public IScriptInterface? Bot { get; set; }

    public IDisposable Enter(CancellationToken? cancellation = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Frame? previous = Active.Value;
        Active.Value = new(this, cancellation ?? (previous?.Session == this ? previous.Cancellation : _lifetimeToken));
        return new ExitScope(previous);
    }

    private sealed class ExitScope(Frame? previous) : IDisposable
    {
        public void Dispose() => Active.Value = previous;
    }

    public object? GetService(Type serviceType)
    {
        using var scope = Enter();
        return _provider.GetService(serviceType);
    }

    public static void ThrowIfCancellationRequested()
    {
        if (Active.Value is not { } frame) return;
        frame.Session._lifetimeToken.ThrowIfCancellationRequested();
        frame.Cancellation.ThrowIfCancellationRequested();
    }

    public void Cancel() => _lifetime.Cancel();
    public void Dispose()
    {
        if (_disposed) return;
        _lifetime.Cancel();
        WeakMessenger.Reset();
        Messenger.Reset();
        Bot = null;
        _disposed = true;
        _lifetime.Dispose();
    }
}

/// <summary>Compatibility targets for process-global APIs in compiled script source.</summary>
public static class ScriptSessionGlobals
{
    public static Ioc Ioc => ScriptSessionContext.Current?.ScopedIoc ?? CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default;
    public static StrongReferenceMessenger StrongMessenger => ScriptSessionContext.Current?.Messenger ?? StrongReferenceMessenger.Default;
    public static WeakReferenceMessenger WeakMessenger => ScriptSessionContext.Current?.WeakMessenger ?? WeakReferenceMessenger.Default;
}
