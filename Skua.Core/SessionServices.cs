using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Skua.Core.Interfaces;

namespace Skua.Core;

/// <summary>Captures session ownership at construction, including for later UI callbacks.</summary>
internal sealed class SessionServices : IServiceProvider
{
    public ScriptSessionContext? Context { get; } = ScriptSessionContext.Current;
    public IMessenger Weak => Context?.WeakMessenger ?? WeakReferenceMessenger.Default;
    public StrongReferenceMessenger Strong => Context?.Messenger ?? StrongReferenceMessenger.Default;
    public static IMessenger CurrentWeak => ScriptSessionContext.Current?.WeakMessenger ?? WeakReferenceMessenger.Default;
    public object? GetService(Type type) => Context is { } context ? context.GetService(type) : Ioc.Default.GetService(type);
    public T? GetService<T>() where T : class => (T?)GetService(typeof(T));
    public T GetRequiredService<T>() where T : notnull => ServiceProviderServiceExtensions.GetRequiredService<T>(this);
    public IDisposable? Enter(CancellationToken? token = null) => Context?.Enter(token);
}
