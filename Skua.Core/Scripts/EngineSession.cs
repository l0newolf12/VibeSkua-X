using Microsoft.Extensions.DependencyInjection;
using Skua.Core.AppStartup;
using Skua.Core.Interfaces;

namespace Skua.Core.Scripts;

/// <summary>Owns an independent service graph, bot facade, and messaging boundary.</summary>
public sealed class EngineSession : IDisposable, IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private bool _disposed;
    public ScriptSessionContext Context { get; }
    public IServiceProvider Services => Context;

    public EngineSession(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddCommonServices(isolated: true);
        services.AddTransient<Compiler>(AppStartup.Services.CreateCompiler);
        configure(services);
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        Context = _provider.GetRequiredService<ScriptSessionContext>();
    }

    public IDisposable Enter(CancellationToken? token = null) => Context.Enter(token);
    public T GetRequiredService<T>() where T : notnull => Services.GetRequiredService<T>();

    public void Dispose()
    {
        if (_disposed) return;
        if (Context.Bot?.Manager is IScriptManager manager) manager.StopScript(false).AsTask().GetAwaiter().GetResult();
        Context.Cancel();
        using (Context.Enter()) _provider.Dispose();
        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        if (Context.Bot?.Manager is IScriptManager manager) await manager.StopScript(false);
        Context.Cancel();
        using (Context.Enter()) await _provider.DisposeAsync();
        _disposed = true;
    }
}
