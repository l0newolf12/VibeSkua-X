using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Messaging;
using VibeSkuaX.App.Controls;
using VibeSkuaX.App.Sessions;

namespace VibeSkuaX.Shell.Tests;

internal static class NativeSessionChecks
{
    public static async Task Run(string artifacts)
    {
        var passed = new List<string>();
        await using var server = new GameServer();
        foreach (var test in new[] { ("unavailable", "version request"), ("malformed", "version response"), ("missing-field", "version response"), ("missing-game", "game download"), ("slow", "startup deadline") })
        {
            var (window, viewport) = Open(new GameSessionOptions
            {
                VersionEndpoint = server.BaseUrl + test.Item1,
                GameBaseUrl = server.BaseUrl,
                StartupTimeout = TimeSpan.FromSeconds(test.Item1 == "slow" ? 1 : 8)
            });
            IntPtr container = viewport.ContainerHandle;
            try
            {
                await WaitState(viewport, GameSessionState.Failed);
                Program.Check(viewport.Session.Error!.Contains(test.Item2), $"Wrong load failure for {test.Item1}: {viewport.Session.Error}");
                Program.Check(container != IntPtr.Zero && !NativeSmoke.IsWindow(container) && viewport.Session.Runtime is null,
                    "Failed startup left a native runtime alive.");
                viewport.Session.ProcessCallback("loaded", []);
                Program.Check(viewport.Session.State == GameSessionState.Failed, "Late completion revived a failed session.");
                window.Content = null;
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.ApplicationIdle);
                window.Content = viewport;
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.ApplicationIdle);
                Program.Check(viewport.Session.State == GameSessionState.Failed && viewport.Session.Runtime is null,
                    "Reattaching a failed session restarted it or accessed a disposed game.");
                passed.Add(test.Item1);
            }
            finally { window.Close(); }
        }

        var pending = Open(new GameSessionOptions { VersionEndpoint = server.BaseUrl + "pending", GameBaseUrl = server.BaseUrl });
        IntPtr pendingContainer = pending.Viewport.ContainerHandle;
        await server.PendingRequest.Task.WaitAsync(TimeSpan.FromSeconds(8));
        pending.Window.Close();
        Program.Check(pending.Viewport.Session.State == GameSessionState.Disposed && !NativeSmoke.IsWindow(pendingContainer),
            "Closing while loading did not dispose the session.");
        server.ReleasePending.TrySetResult();
        pending.Viewport.Session.ProcessCallback("loaded", []);
        await Task.Delay(100);
        Program.Check(pending.Viewport.Session.State == GameSessionState.Disposed, "Late response revived a closed session.");
        passed.Add("close-during-load");

        var fixture = Open(new GameSessionOptions { SwfPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "InventoryHarness.swf") });
        try
        {
            await WaitState(fixture.Viewport, GameSessionState.Ready);
            var runtime = fixture.Viewport.Session.Runtime!;
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                Program.Check(runtime.Call<bool>("nativeTrue") && runtime.Call<float>("nativeNumber") == 1.5f,
                    "Real native Flash return conversion failed.");
                Program.Check(runtime.Call("nativeNull", typeof(object)) is null, "Native null return changed.");
            }
            finally { CultureInfo.CurrentCulture = original; }
            using var result = JsonDocument.Parse(runtime.Call("runInventoryTests"));
            foreach (var check in result.RootElement.EnumerateArray())
                Program.Check(check.GetProperty("pass").GetBoolean(), "AS3 inventory contract failed: " + check.GetProperty("name").GetString());
            File.WriteAllText(Path.Combine(artifacts, "inventory-contract.json"), result.RootElement.GetRawText());
            // The fixture owns plain client objects: exercise the production identity
            // poller without connecting to a server or supplying account credentials.
            runtime.Call("setGameObject", "sfc", new Dictionary<string, object?> { ["isConnected"] = true });
            runtime.Call("setGameObject", "world.myAvatar.objData.strUsername", "Fixture character");
            await NativeSmoke.Until(() => fixture.Viewport.Session.CharacterName == "Fixture character",
                "Connected character data did not update the session identity.");
            runtime.Call("setGameObject", "sfc.isConnected", false);
            await NativeSmoke.Until(() => fixture.Viewport.Session.CharacterName is null,
                "Logging out did not clear the session identity.");
            passed.Add("character-identity-and-logout");
            IntPtr child = fixture.Viewport.ContainerHandle;
            fixture.Viewport.Session.Messenger.Send(new FlashRuntimeFailedMessage(new COMException("Injected native disconnect", unchecked((int)0x80010108))));
            await WaitState(fixture.Viewport, GameSessionState.Failed);
            Program.Check(!NativeSmoke.IsWindow(child), "Fatal native runtime failure did not release its container.");
            passed.Add("inventory-snapshot-and-native-values");
            passed.Add("fatal-disconnect");
        }
        finally { fixture.Window.Close(); }
        File.WriteAllText(Path.Combine(artifacts, "native-session-checks.json"), JsonSerializer.Serialize(passed));
        Console.WriteLine("PASS: real AS3 network/JSON/SWF failures, startup deadline, close-during-load, late callbacks, snapshot inventory, and native values.");
    }

    private static (Window Window, FlashViewport Viewport) Open(GameSessionOptions options)
    {
        var viewport = new FlashViewport(options);
        var window = new Window { Width = 800, Height = 600, ShowInTaskbar = false, ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(-30000, -30000), Content = viewport };
        window.Closed += (_, _) => viewport.Dispose();
        window.Show();
        return (window, viewport);
    }

    private static async Task WaitState(FlashViewport viewport, GameSessionState expected)
    {
        for (int i = 0; i < 500; i++)
        {
            if (viewport.Session.State == expected) return;
            if (viewport.Session.State == GameSessionState.Failed && expected != GameSessionState.Failed)
                throw new Exception(viewport.Session.Error);
            await Task.Delay(20);
        }
        throw new TimeoutException($"Session remained {viewport.Session.State}; expected {expected}.");
    }

    // A loopback-only HTTP fixture avoids external outage simulation or machine-wide network changes.
    private sealed class GameServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _accept;
        private readonly List<Task> _requests = [];
        public TaskCompletionSource PendingRequest { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleasePending { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string BaseUrl { get; }
        public GameServer()
        {
            _listener.Start();
            BaseUrl = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
            _accept = Accept();
        }
        private async Task Accept()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    _requests.Add(Respond(client));
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        }
        private async Task Respond(TcpClient client)
        {
            using (client)
            try
            {
                var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, leaveOpen: true);
                string request = await reader.ReadLineAsync(_stop.Token) ?? "";
                string path = request.Split(' ').ElementAtOrDefault(1)?.Split('?')[0] ?? "";
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync(_stop.Token))) { }
                string body = "missing";
                int status = 404;
                if (path == "/crossdomain.xml") { status = 200; body = "<cross-domain-policy><allow-access-from domain='*'/></cross-domain-policy>"; }
                if (path == "/malformed") { status = 200; body = "{invalid"; }
                if (path == "/missing-field") { status = 200; body = "{}"; }
                if (path == "/missing-game") { status = 200; body = "{\"sFile\":\"missing.swf\"}"; }
                if (path == "/slow") await Task.Delay(3000, _stop.Token);
                if (path == "/pending")
                {
                    PendingRequest.TrySetResult();
                    await ReleasePending.Task.WaitAsync(_stop.Token);
                    status = 200; body = "{\"sFile\":\"missing.swf\"}";
                }
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                byte[] headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {(status == 200 ? "OK" : "Not Found")}\r\nContent-Length: {bytes.Length}\r\nContent-Type: text/plain\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(headers, _stop.Token);
                await stream.WriteAsync(bytes, _stop.Token);
            }
            catch (IOException) { }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        }
        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            await _accept;
            await Task.WhenAll(_requests);
            _stop.Dispose();
        }
    }
}
