using System.Dynamic;
using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Skua.Core.Interfaces;
using VibeSkuaX.Runtime.Windows.Flash;

namespace VibeSkuaX.Shell.Tests;

internal static class FlashChecks
{
    public static void Run()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            foreach (string culture in new[] { "en-US", "fr-FR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                Program.Check(FlashUtil.ToFlashXml(1.5) == "<number>1.5</number>", "Locale-dependent number serialization.");
                Program.Check((float)FlashXml.ConvertResult(XElement.Parse("<number>1.5</number>"), typeof(float))! == 1.5f, "Locale-dependent return conversion.");
                Program.Check((bool)FlashXml.ConvertResult(XElement.Parse("<true/>"), typeof(bool))!, "Native boolean return lost.");
                Program.Check((bool)FlashXml.ConvertResult(XElement.Parse("<string>true</string>"), typeof(bool))!, "Legacy boolean string return lost.");
                Program.Check(FlashXml.Read(XElement.Parse("<null/>")) is null && FlashXml.Read(XElement.Parse("<undefined/>")) is null, "Null/undefined decoding failed.");
                Program.Check((string)FlashXml.Read(FlashXml.Write("<&\"é>"))! == "<&\"é>", "String escaping failed.");
                Program.Check((string)FlashXml.ConvertResult(FlashXml.Write("{\"value\":\"a&b\"}"), typeof(string))! == "{\"value\":\"a&b\"}", "JSON return changed.");
                IDictionary<string, object?> data = new ExpandoObject();
                data["a\"&"] = new object?[] { 42, true, null, new object?[] { "nested", 1.5 } };
                var decoded = (IDictionary<string, object?>)FlashXml.Read(FlashXml.Write(data))!;
                var array = (object?[])decoded["a\"&"]!;
                Program.Check(array[0] is 42 && array[1] is true && array[2] is null && ((object?[])array[3]!)[0] is "nested", "Nested value round-trip failed.");
                var sparse = (object?[])FlashXml.Read(XElement.Parse("<array><property id='3'><number>7</number></property><property id='1'><false/></property></array>"))!;
                Program.Check(sparse.Length == 4 && sparse[0] is null && sparse[1] is false && sparse[2] is null && sparse[3] is 7, "Array indexes were not preserved.");
                foreach (string shape in new[] {
                    "<arguments><string>alpha</string><number>42</number></arguments>",
                    "<arguments><array><property id='0'><string>alpha</string></property><property id='1'><number>42</number></property></array></arguments>"
                })
                {
                    object[] arguments = FlashXml.ReadArguments(XElement.Parse("<invoke name='event'>" + shape + "</invoke>"));
                    Program.Check(arguments.Length == 2 && arguments[0] is "alpha" && arguments[1] is 42, "Callback arguments were flattened into text.");
                }
                Program.Check(FlashXml.ReadArguments(XElement.Parse("<invoke><arguments><array/></arguments></invoke>")).Length == 0, "Empty callback arguments changed.");
            }
        }
        finally { CultureInfo.CurrentCulture = original; }

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var source = new CancellationTokenSource();
                source.Cancel();
                var manager = DispatchProxy.Create<IScriptManager, CancelledManager>();
                ((CancelledManager)(object)manager).Source = source;
                using var bridge = new FlashUtil(new WeakReferenceMessenger(), new Lazy<IScriptManager>(() => manager));
                ExpectCancellation(() => bridge.Call<int>("isTrue"));
                ExpectCancellation(() => bridge.Call("isTrue"));
                ExpectCancellation(() => bridge.Call("isTrue", typeof(int)));
            }
            catch (Exception error) { failure = error; }
        }) { Name = "Script Thread", IsBackground = true };
        thread.Start();
        Program.Check(thread.Join(TimeSpan.FromSeconds(3)), "Cancelled polling script did not exit.");
        if (failure is not null) throw failure;
        // A session token also stops ordinary workers, without a thread-name convention.
        using (var provider = new ServiceCollection().BuildServiceProvider())
        using (var session = new ScriptSessionContext(provider))
        using (var source = new CancellationTokenSource())
        using (session.Enter(source.Token))
        using (var bridge = new FlashUtil(new WeakReferenceMessenger()))
        {
            source.Cancel();
            ExpectCancellation(() => bridge.Call<int>("isTrue"));
            ExpectCancellation(() => bridge.Call("isTrue"));
            ExpectCancellation(() => bridge.Call("isTrue", typeof(int)));
        }
        using var empty = new FlashUtil(new WeakReferenceMessenger());
        Program.Check(empty.Call<int>("missing") == 0, "Ordinary fallback behavior changed.");
        Console.WriteLine("PASS: Flash XML values/callbacks under two cultures; typed/raw cancellation and ordinary fallback.");
    }

    private static void ExpectCancellation(Action call)
    {
        try { call(); }
        catch (OperationCanceledException) { return; }
        throw new Exception("A cancelled Flash call returned a default instead of stopping the script.");
    }

    public class CancelledManager : DispatchProxy
    {
        public CancellationTokenSource Source { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
        {
            "get_ShouldExit" => true,
            "get_ScriptCts" => Source,
            _ => throw new NotSupportedException(method.Name)
        };
    }
}
