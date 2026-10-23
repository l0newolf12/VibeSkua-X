using System.Reflection;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using Skua.Core.Models.Items;
using Skua.Core.Options;
using Skua.Core.Services;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var host = new TestHost();
string optionsFile = Path.GetTempFileName();
try
{
    var enabled = new Option<bool>("enabled", "Enabled", "", false);
    var mode = new Option<TestMode>("mode", "Mode", "", TestMode.Farm);
    var transient = new Option<string>("session", "Session", "", "default", transient: true);
    var options = new OptionContainer(host) { OptionsFile = optionsFile };
    options.Options.AddRange([enabled, transient]);
    options.MultipleOptions["Combat"] = [mode];
    host.Edits = new Dictionary<IOption, string>
    {
        [enabled] = "True", [mode] = "Farm Boss", [transient] = "temporary"
    };
    options.Configure();
    Check(options.Get<bool>("enabled"), "Confirmed boolean option was not applied.");
    Check(options.Get<TestMode>("Combat", "mode") == TestMode.Farm_Boss, "Enum option did not normalize spaces.");
    Check(!File.ReadAllText(optionsFile).Contains("session"), "Transient option was persisted.");

    var reloaded = new OptionContainer(host) { OptionsFile = optionsFile };
    reloaded.Options.AddRange([enabled, transient]);
    reloaded.MultipleOptions["Combat"] = [mode];
    host.Edits = null; // Cancellation: the host does not call onConfirmed.
    reloaded.Configure();
    Check(reloaded.Get<bool>("enabled"), "Cancelled configuration lost the saved value.");
    Check(reloaded.Get<TestMode>("Combat", "mode") == TestMode.Farm_Boss, "Categorized option did not round-trip.");
    Console.WriteLine("PASS: option confirmation, cancellation, enums, persistence, and transient values.");
}
finally { File.Delete(optionsFile); }

var joined = new TaskCompletionSource<(string Map, string Cell, string Pad)>(TaskCreationOptions.RunContinuationsAsynchronously);
var jumped = new TaskCompletionSource<(string Cell, string Pad)>(TaskCreationOptions.RunContinuationsAsynchronously);
string currentMap = "battleon";
int mapItemCalls = 0;
IScriptMap map = Fake<IScriptMap>((method, arguments) =>
{
    if (method.Name == "get_Name") return currentMap;
    if (method.Name == "Join")
        joined.TrySetResult(((string)arguments![0]!, (string)arguments[1]!, (string)arguments[2]!));
    if (method.Name == "Jump")
        jumped.TrySetResult(((string)arguments![0]!, (string)arguments[1]!));
    if (method.Name == "GetMapItem") mapItemCalls++;
    return DefaultResult(method);
});
IScriptPlayer player = Fake<IScriptPlayer>((method, _) =>
    method.Name is "get_Playing" or "get_LoggedIn" ? true : DefaultResult(method));
var navigation = new MapService(map, player) { UsePrivateRoom = true, PrivateRoomNumber = 12345 };
navigation.Travel(new TravelDestination("battleontown", "Enter", "Spawn"));
Check(await joined.Task.WaitAsync(TimeSpan.FromSeconds(5)) == ("battleontown-12345", "Enter", "Spawn"),
    "Navigation did not preserve the private room, cell, and pad.");
currentMap = "tercessuinotlim";
navigation.Travel(new TravelDestination("tercessuinotlim", "m22", "Left"));
Check(await jumped.Task.WaitAsync(TimeSpan.FromSeconds(5)) == ("m22", "Left"),
    "Navigation within tercessuinotlim did not use Jump.");
Console.WriteLine("PASS: model-based navigation, private rooms, and special-map travel.");

using var provider = new ServiceCollection()
    .AddSingleton<IDialogService>(host)
    .AddSingleton(map)
    .BuildServiceProvider();
Ioc.Default.ConfigureServices(provider);
var grabber = new GrabberService(null!, null!, null!, null!, null!, null!, null!, map);
var progress = new TestProgress();
host.Input = null;
await grabber.GetMapItem([new MapItem(42, 0, "test.swf", "test")], progress, CancellationToken.None);
Check(mapItemCalls == 0 && progress.Messages.Contains("Cancelled."), "Cancelled input performed a game action.");
host.Input = "invalid";
await grabber.GetMapItem([new MapItem(42, 0, "test.swf", "test")], progress, CancellationToken.None);
Check(mapItemCalls == 0, "Invalid quantity performed a game action.");
host.Input = "3";
await grabber.GetMapItem([new MapItem(42, 0, "test.swf", "test")], progress, CancellationToken.None);
Check(mapItemCalls == 1, "Confirmed quantity did not perform the requested game action.");
Console.WriteLine("PASS: cancelled, invalid, and confirmed input requests.");
Console.WriteLine("All VibeSkua X foundation checks passed.");

static object? DefaultResult(MethodInfo method) =>
    method.ReturnType.IsValueType && method.ReturnType != typeof(void) ? Activator.CreateInstance(method.ReturnType) : null;
static T Fake<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
{
    T proxy = DispatchProxy.Create<T, TestProxy>();
    ((TestProxy)(object)proxy).Handler = handler;
    return proxy;
}

public enum TestMode { Farm, Farm_Boss }
public class TestProxy : DispatchProxy
{
    public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
    protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args);
}
internal sealed class TestProgress : IProgress<string>
{
    public List<string> Messages { get; } = [];
    public void Report(string value) => Messages.Add(value);
}
internal sealed class TestHost : IDialogService
{
    public IReadOnlyDictionary<IOption, string>? Edits { get; set; }
    public string? Input { get; set; }
    public void ConfigureOptions(IOptionContainer options, Action<IReadOnlyDictionary<IOption, string>> onConfirmed)
    {
        if (Edits is not null) onConfirmed(Edits);
    }
    public string? RequestInput(string title, string hint, bool numberOnly = false) => Input;
    public void ShowMessageBox(string message, string caption) { }
    public bool? ShowMessageBox(string message, string caption, bool yesAndNo) => null;
    public DialogResult ShowMessageBox(string message, string caption, params string[] buttons) => DialogResult.Cancelled;
}

