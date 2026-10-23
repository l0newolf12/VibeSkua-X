using System.Collections.Concurrent;
using System.Reflection;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Input;
using Skua.Core.AppStartup;
using Microsoft.Extensions.DependencyInjection;
using Skua.Core.Interfaces;
using Skua.Core.Interfaces.Services;
using Skua.Core.Messaging;
using Skua.Core.Scripts;

internal static class EngineChecks
{
    public static async Task Run()
    {
        string directory = Path.GetFullPath("artifacts/engine-checks");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "Helper.cs"), """
            public static class SessionHelper
            {
                public static string Account() => global::CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetRequiredService<Skua.Core.Interfaces.IScriptInterface>().Player.Username;
                public static string Literal => "Ioc.Default";
            }
            """);
        string source = """
            //cs_include ./Helper.cs
            using CommunityToolkit.Mvvm.Messaging;
            using Skua.Core.Messaging;
            public class Script
            {
                public void ScriptMain(IScriptInterface bot)
                {
                    if (!object.ReferenceEquals(bot, IScriptInterface.Instance)) throw new Exception("Wrong static bot");
                    if (!object.ReferenceEquals(bot, Ioc.Default.GetRequiredService<IScriptInterface>())) throw new Exception("Wrong Ioc owner");
                    if (SessionHelper.Account() != bot.Player.Username || SessionHelper.Literal != "Ioc.Default") throw new Exception("Wrong include binding");
                    StrongReferenceMessenger.Default.Send<PacketMessage, int>(new PacketMessage(bot.Player.Username), (int)MessageChannels.GameEvents);
                    WeakReferenceMessenger.Default.Send(new PacketMessage(bot.Player.Username));
                    bot.Log("ready:" + bot.Player.Username);
                    while (true) bot.Sleep(10);
                }
            }
            """;
        string firstFile = Path.Combine(directory, "First.cs"), secondFile = Path.Combine(directory, "Second.cs");
        await File.WriteAllTextAsync(firstFile, source);
        await File.WriteAllTextAsync(secondFile, source);
        var firstState = new DependencyState("Alpha");
        var secondState = new DependencyState("Beta");
        await using var first = Build(firstState);
        await using var second = Build(secondState);
        var firstManager = first.GetRequiredService<IScriptManager>();
        var secondManager = second.GetRequiredService<IScriptManager>();
        var firstEvents = (ScriptEvent)first.GetRequiredService<IScriptEvent>();
        var secondEvents = (ScriptEvent)second.GetRequiredService<IScriptEvent>();
        _ = first.GetRequiredService<IScriptInterface>();
        _ = second.GetRequiredService<IScriptInterface>();
        string? hotkeyOwner = null;
        IRelayCommand firstCommand, secondCommand;
        using (first.Enter())
        {
            HotKeys.RegisterHotkey("SessionProbe", new RelayCommand(() => hotkeyOwner = IScriptInterface.Instance.Player.Username));
            firstCommand = HotKeys.GetRegistry()["SessionProbe"];
        }
        using (second.Enter())
        {
            HotKeys.RegisterHotkey("SessionProbe", new RelayCommand(() => hotkeyOwner = IScriptInterface.Instance.Player.Username));
            secondCommand = HotKeys.GetRegistry()["SessionProbe"];
            firstCommand.Execute(null);
            Check(hotkeyOwner == "Alpha" && ScriptSessionContext.Current == second.Context, "Hotkey execution lost its captured owner or failed to restore context.");
        }
        secondCommand.Execute(null);
        Check(hotkeyOwner == "Beta" && ScriptSessionContext.Current is null, "Hotkey callback leaked context outside its session.");
        firstEvents.PacketReceived += value => firstState.GamePackets.Enqueue(value);
        secondEvents.PacketReceived += value => secondState.GamePackets.Enqueue(value);
        first.Context.WeakMessenger.Register<PacketMessage>(firstState, (r, m) => ((DependencyState)r).WeakPackets.Enqueue(m.Packet));
        second.Context.WeakMessenger.Register<PacketMessage>(secondState, (r, m) => ((DependencyState)r).WeakPackets.Enqueue(m.Packet));
        int firstStarted = 0, secondStarted = 0, firstErrors = 0, secondErrors = 0;
        first.Context.Messenger.Register<ScriptStartedMessage, int>(firstState, (int)MessageChannels.ScriptStatus, (_, _) => Interlocked.Increment(ref firstStarted));
        second.Context.Messenger.Register<ScriptStartedMessage, int>(secondState, (int)MessageChannels.ScriptStatus, (_, _) => Interlocked.Increment(ref secondStarted));
        first.Context.Messenger.Register<ScriptErrorMessage, int>(firstState, (int)MessageChannels.ScriptStatus, (_, _) => Interlocked.Increment(ref firstErrors));
        second.Context.Messenger.Register<ScriptErrorMessage, int>(secondState, (int)MessageChannels.ScriptStatus, (_, _) => Interlocked.Increment(ref secondErrors));
        firstManager.SetLoadedScript(firstFile);
        secondManager.SetLoadedScript(secondFile);
        var starts = await Task.WhenAll(firstManager.StartScript(), secondManager.StartScript());
        foreach (var error in starts) if (error is not null) throw new Exception("Representative script failed to start.", error);
        await Until(() => firstState.Logs.Contains("ready:Alpha") && secondState.Logs.Contains("ready:Beta"), "Scripts did not reach their polling loops.");
        Check(firstStarted == 1 && secondStarted == 1 && firstErrors == 0 && secondErrors == 0, "Script lifecycle messages crossed sessions.");
        Check(firstState.GamePackets.SequenceEqual(["Alpha"]) && secondState.GamePackets.SequenceEqual(["Beta"]), "Game event owners crossed.");
        Check(firstState.WeakPackets.SequenceEqual(["Alpha"]) && secondState.WeakPackets.SequenceEqual(["Beta"]), "Weak messenger globals crossed sessions.");
        using (second.Enter()) Skua.Core.Compiler.RegisterLoaded("beta-sentinel", typeof(EngineChecks).Assembly);
        await firstManager.StopScript(false);
        await Until(() => !firstManager.ScriptRunning, "First script did not stop.");
        Check(secondManager.ScriptRunning && !secondManager.ShouldExit, "Stopping one script stopped the other.");
        using (second.Enter()) Check(Skua.Core.Compiler.IsLoaded("beta-sentinel"), "First engine unloaded the other's compiler registry.");
        await first.DisposeAsync();
        Check(!firstCommand.CanExecute(null), "A disposed engine's hotkey remained enabled.");
        Check(secondManager.ScriptRunning && !second.Context.IsDisposed, "Disposing one engine affected the other.");
        second.Context.Messenger.Send<PacketMessage, int>(new("after-dispose"), (int)MessageChannels.GameEvents);
        Check(secondState.GamePackets.Contains("after-dispose") && !firstState.GamePackets.Contains("after-dispose"), "Disposed engine still received another's events.");

        secondState.ThrowOnSkillStop = true;
        await secondManager.StopScript(false);
        secondState.ThrowOnSkillStop = false;
        Check(!secondManager.ScriptRunning, "A cleanup exception prevented the script from completing.");
        string failureFile = Path.Combine(directory, "Failure.cs");
        await File.WriteAllTextAsync(failureFile, "public class Script { public void ScriptMain(IScriptInterface bot) { throw new System.InvalidOperationException(\"expected Beta failure\"); } }");
        secondManager.SetLoadedScript(failureFile);
        var failureStart = await secondManager.StartScript();
        if (failureStart is not null) throw failureStart;
        await Until(() => !secondManager.ScriptRunning && secondErrors == 1, "Script failure did not finish in its own engine.");
        Check(firstErrors == 0 && firstStarted == 1, "Failure escaped into the disposed engine.");
        secondManager.SetLoadedScript(secondFile);
        var restart = await secondManager.StartScript();
        if (restart is not null) throw restart;
        await Until(() => secondState.Logs.Count(x => x == "ready:Beta") == 2, "The remaining engine could not restart.");
        await second.DisposeAsync();
        Check(!secondManager.ScriptRunning && second.Context.IsDisposed, "Disposing the running engine did not stop its script.");
        var startupState = new DependencyState("Gamma") { AutoStopGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var starting = Build(startupState);
        var startingManager = starting.GetRequiredService<IScriptManager>();
        startingManager.SetLoadedScript(firstFile);
        Task<Exception?> start = startingManager.StartScript();
        Check(startingManager.ScriptRunning && !start.IsCompleted, "Startup gate was not reached.");
        Task disposal = starting.DisposeAsync().AsTask();
        Check(!disposal.IsCompleted && !starting.Context.IsDisposed, "Disposal raced ahead of pending startup.");
        startupState.AutoStopGate.TrySetResult();
        Check(await start.WaitAsync(TimeSpan.FromSeconds(10)) is OperationCanceledException, "Closing during startup did not cancel the pending run.");
        await disposal.WaitAsync(TimeSpan.FromSeconds(10));
        Check(!startingManager.ScriptRunning && starting.Context.IsDisposed, "Startup cancellation did not finish cleanup.");
        Console.WriteLine("PASS: two real engine managers and bot facades compile/run source and includes; globals, messages, stop, failure, cache, restart, and disposal are isolated.");
    }

    private static EngineSession Build(DependencyState state) => new(services =>
    {
        // Fake only game/platform dependencies; exercise the real manager, bot, events,
        // waits, compiler, include resolver and their normal constructor graph.
        foreach (var type in typeof(IScriptInterface).Assembly.GetExportedTypes().Where(t => t.IsInterface && !t.ContainsGenericParameters))
            services.AddSingleton(type, _ => EngineDependencyProxy.Create(type, state));
        services.AddSingleton<IScriptManager, ScriptManager>();
        services.AddSingleton<IScriptInterface, ScriptInterface>();
        services.AddSingleton<IScriptEvent, ScriptEvent>();
        services.AddSingleton<IScriptWait, ScriptWait>();
        services.AddSingleton<IScriptPathResolver, ScriptPathResolver>();
    });

    private static async Task Until(Func<bool> condition, string message)
    {
        for (int i = 0; i < 1000; i++)
        {
            if (condition()) return;
            await Task.Delay(20);
        }
        throw new TimeoutException(message);
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}

public sealed class DependencyState(string name)
{
    public TaskCompletionSource? AutoStopGate { get; init; }
    public bool ThrowOnSkillStop { get; set; }
    public string Name { get; } = name;
    public ConcurrentQueue<string> Logs { get; } = new();
    public ConcurrentQueue<string> GamePackets { get; } = new();
    public ConcurrentQueue<string> WeakPackets { get; } = new();
}

public class EngineDependencyProxy : DispatchProxy
{
    private Type _type = null!;
    private DependencyState _state = null!;
    private readonly Dictionary<string, object?> _properties = new();
    public static object Create(Type type, DependencyState state)
    {
        object proxy = DispatchProxy.Create(type, typeof(EngineDependencyProxy));
        ((EngineDependencyProxy)proxy)._type = type;
        ((EngineDependencyProxy)proxy)._state = state;
        return proxy;
    }
    protected override object? Invoke(MethodInfo? method, object?[]? arguments)
    {
        string name = method!.Name;
        arguments ??= [];
        if (_type == typeof(IScriptSkill) && name == "Stop" && _state.ThrowOnSkillStop) throw new InvalidOperationException("Injected skill cleanup failure");
        if (_type == typeof(IScriptAuto) && name == "StopAsync" && _state.AutoStopGate is { } gate) return new ValueTask(gate.Task);
        if (_type == typeof(ILogService) && name == "ScriptLog") _state.Logs.Enqueue((string)arguments[0]!);
        if (_type == typeof(ISettingsService) && name == "Get" && arguments.Length == 2) return arguments[1];
        if (name == "get_Username") return _state.Name;
        if (name is "get_Playing" or "get_LoggedIn") return true;
        if (name.StartsWith("set_")) { _properties[name[4..]] = arguments[0]; return null; }
        if (name.StartsWith("get_"))
        {
            if (!_properties.TryGetValue(name[4..], out var value)) _properties[name[4..]] = value = Default(method.ReturnType);
            return value;
        }
        return Default(method.ReturnType);
    }
    private object? Default(Type type)
    {
        if (type == typeof(void)) return null;
        if (type == typeof(string)) return "";
        if (type == typeof(Task)) return Task.CompletedTask;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(type.GenericTypeArguments[0]).Invoke(null, [Default(type.GenericTypeArguments[0])]);
        if (type.IsValueType) return Activator.CreateInstance(type);
        if (type.IsArray) return Array.CreateInstance(type.GetElementType()!, 0);
        if (type.IsInterface) return Create(type, _state);
        if (type.GetConstructor(Type.EmptyTypes) is not null) return Activator.CreateInstance(type);
        return null;
    }
}
