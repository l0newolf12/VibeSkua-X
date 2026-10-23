using Skua.Core.Interfaces;
using System;
using System.Xml.Linq;

namespace VibeSkuaX.Runtime.Windows.Flash;

using AxShockwaveFlashObjects;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Flash;
using Skua.Core.Messaging;
using Skua.Core.Utils;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading;
using System.Windows.Forms;

public class FlashUtil : IFlashUtil
{
    public FlashUtil(IMessenger messenger)
    {
        _messenger = messenger;
    }

    public FlashUtil(IMessenger messenger, Lazy<IScriptManager> manager) : this(messenger)
    {
        _lazyManager = manager;
    }

    private readonly IMessenger _messenger;
    private readonly Lazy<IScriptManager>? _lazyManager;
    private AxShockwaveFlash? Flash;

    public event FlashCallHandler? FlashCall;

    private static byte[]? _cachedSwf;

    public static void PreloadSwf()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "skua.swf");
        if (_cachedSwf == null && File.Exists(path))
        {
            _cachedSwf = File.ReadAllBytes(path);
        }
    }

    public void InitializeFlash()
    {
        InitializeFlash(null);
    }

    /// <summary>Creates the ActiveX control directly inside an owned WinForms container.</summary>
    public void InitializeFlash(Control? container)
        => InitializeFlash(container, Path.Combine(AppContext.BaseDirectory, "skua.swf"));

    public void InitializeFlash(Control? container, string swfPath)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("Flash must be initialized on the STA UI thread.");

        if (!File.Exists(swfPath))
        {
            throw new FileNotFoundException("Missing skua.swf. Build the AS3 client before initializing VibeSkua X.", swfPath);
        }

        try
        {
            if (!EoLHook.IsHooked)
                EoLHook.Hook();

            CleanupOldFlash();

            AxShockwaveFlash flash = new();
            Flash = flash;
            flash.BeginInit();
            flash.Name = "flash";
            flash.Dock = DockStyle.Fill;
            flash.TabIndex = 0;
            flash.FlashCall += CallHandler;

            container?.Controls.Add(flash);
            _messenger.Send<FlashChangedMessage<AxShockwaveFlash>>(new(flash));
            flash.EndInit();
            flash.WMode = "window";
            flash.ScaleMode = 0;
            flash.CtlScale = "ShowAll";
            byte[] swf = swfPath == Path.Combine(AppContext.BaseDirectory, "skua.swf")
                ? _cachedSwf ?? File.ReadAllBytes(swfPath) : File.ReadAllBytes(swfPath);
            using MemoryStream stream = new();
            using BinaryWriter writer = new(stream);
            writer.Write(8 + swf.Length);
            writer.Write(1432769894);
            writer.Write(swf.Length);
            writer.Write(swf);
            writer.Seek(0, SeekOrigin.Begin);
            flash.OcxState = new AxHost.State(stream, 1, false, null);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Flash initialization error: {ex}");
            CleanupOldFlash();
            throw new InvalidOperationException("Could not initialize the Flash runtime. Check the CleanFlash installation.", ex);
        }
        finally
        {
            EoLHook.Unhook();
        }
    }

    private void CleanupOldFlash()
    {
        if (Flash == null) return;

        Flash.FlashCall -= CallHandler;

        try
        {
            Flash.Stop();
        }
        catch { /* ignored */ }

        object? ocx = null;
        try
        {
            ocx = Flash.GetOcx();
        }
        catch { /* ignored */ }

        Flash.Dispose();
        Flash = null;

        if (ocx != null)
        {
            try
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(ocx);
            }
            catch { /* ignored */ }
        }
    }

    private void CallHandler(object sender, _IShockwaveFlashEvents_FlashCallEvent e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(e?.request))
                return;

            XElement el = XElement.Parse(e.request);
            string? function = el.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(function))
                return;

            object[] args = FlashXml.ReadArguments(el);
            FlashCall?.Invoke(function, args);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Flash CallHandler exception: {ex.Message}");
        }
    }

    public string Call(string function, params object[] args)
    {
        return Call<string>(function, args);
    }

    public T Call<T>(string function, params object[] args)
    {
        try
        {
            object? o = Call(function, typeof(T), args);
            return o is T value ? value : GetDefaultValue<T>();
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return GetDefaultValue<T>();
        }
    }

    private static T GetDefaultValue<T>()
    {
        object? defaultValue = DefaultProvider.GetDefault<T>(typeof(T));
        return defaultValue is T typed ? typed : default!;
    }

    public object Call(string function, Type type, params object[] args)
    {
        ScriptSessionContext.ThrowIfCancellationRequested();
        if (Thread.CurrentThread.Name == "Script Thread" && _lazyManager?.Value.ShouldExit == true)
            _lazyManager.Value.ScriptCts?.Token.ThrowIfCancellationRequested();

        try
        {
            if (Flash is null)
                return default!;

            var req = new XElement("invoke", new XAttribute("name", function), new XAttribute("returntype", "xml"));
            if (args.Length > 0) req.Add(new XElement("arguments", args.Select(FlashXml.Write)));

            string? result = null;
            const int maxRetries = 2;
            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var control = Flash;
                    result = control.InvokeRequired
                        ? (string)control.Invoke(() => control.CallFunction(req.ToString(SaveOptions.DisableFormatting)))
                        : control.CallFunction(req.ToString(SaveOptions.DisableFormatting));
                    break;
                }
                catch (COMException comEx) when (attempt < maxRetries && IsTransientComError(comEx.HResult))
                {
                    Thread.Sleep(10 * (attempt + 1));
                }
            }

            if (string.IsNullOrWhiteSpace(result))
                return default!;

            XElement el = XElement.Parse(result);
            return FlashXml.ConvertResult(el, type)!;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e)
        {
            if (function != "isKicked")
                _messenger.Send<FlashErrorMessage>(new(e, function, args));
            var cause = e.GetBaseException();
            if (cause is COMException && cause.HResult is unchecked((int)0x80010108) or unchecked((int)0x800401FD))
                _messenger.Send(new FlashRuntimeFailedMessage(cause));
            return default!;
        }
    }

    private static bool IsTransientComError(int hresult)
    {
        return hresult == unchecked((int)0x8001010A) || hresult == unchecked((int)0x80010001);
    }

    public static string ToFlashXml(object o) => FlashXml.Write(o).ToString(SaveOptions.DisableFormatting);

    public object FromFlashXml(XElement el) => FlashXml.Read(el)!;
    public IFlashObject<T> CreateFlashObject<T>(string path)
    {
        return new FlashObject<T>(Call<int>("lnkCreate", path), this);
    }

    public void Dispose()
    {
        try
        {
            CleanupOldFlash();
        }
        catch { /* ignored */ }
        finally
        {
            EoLHook.Unhook();
        }
    }
}
