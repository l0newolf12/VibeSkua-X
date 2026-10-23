using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.VisualTree;
using VibeSkuaX.App.Controls;

namespace VibeSkuaX.Shell.Tests;

internal static class CaptionChecks
{
    internal static T? FindChrome<T>(this Window window, string name) where T : Control =>
        window.FindControl<WindowTitleBar>("WindowChrome")?.FindControl<T>(name);

    internal static Color HoverColor(Button button) => ((ISolidColorBrush)button.FindResource(
        button.Name == "CloseButton" ? "VsBrushCloseHover" : "VsBrushHover")!).Color;

    internal static void AssertBackground(Button button, bool hovered)
    {
        var presenter = button.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Name == "PART_ContentPresenter");
        var brush = (ISolidColorBrush)presenter.Background!;
        Program.Check(hovered ? brush.Color == HoverColor(button) && brush.Opacity == 1 : brush.Color.A == 0 || brush.Opacity == 0,
            $"{button.Name} has an intermediate/stale background: {brush.Color}, opacity {brush.Opacity}.");
    }

    internal static void RunHeadless(Window window)
    {
        var names = new[] { "CloseButton", "MaximizeButton", "MinimizeButton", "CloseButton" };
        var normal = ((ISolidColorBrush)window.FindChrome<Border>("TitleBar")!.Background!).Color;
        foreach (string name in names)
        {
            var button = window.FindChrome<Button>(name)!;
            if (!button.IsVisible) continue;
            window.MouseMove(button.TranslatePoint(new Point(button.Bounds.Width / 2, 15), window)!.Value);
            CheckRenderedColor(window, button, HoverColor(button));
            window.MouseMove(new Point(100, 100));
            CheckRenderedColor(window, button, normal);
        }
        foreach (string name in names.Distinct())
            Program.Check(ToolTip.GetTip(window.FindChrome<Button>(name)!) is null, "Caption still has a duplicate Avalonia tooltip.");
        Console.WriteLine("PASS: caption hover/leave pixels change immediately without gray/pink flashes; no duplicate Avalonia tooltips.");
    }

    private static void CheckRenderedColor(Window window, Button button, Color expected)
    {
        using var frame = window.CaptureRenderedFrame() ?? throw new Exception("No caption frame.");
        var point = button.TranslatePoint(new Point(7, 7), window)!.Value;
        using var pixels = frame.Lock();
        Program.Check(pixels.Format == PixelFormat.Bgra8888 || pixels.Format == PixelFormat.Rgba8888,
            $"Unexpected headless capture format: {pixels.Format}.");
        IntPtr address = pixels.Address + (int)Math.Round(point.Y * window.RenderScaling) * pixels.RowBytes + (int)Math.Round(point.X * window.RenderScaling) * 4;
        bool bgra = pixels.Format == PixelFormat.Bgra8888;
        Color actual = Color.FromArgb(Marshal.ReadByte(address, 3), Marshal.ReadByte(address, bgra ? 2 : 0),
            Marshal.ReadByte(address, 1), Marshal.ReadByte(address, bgra ? 0 : 2));
        Program.Check(actual == expected, $"{button.Name} rendered a transient/stale color: {actual}; expected {expected}.");
    }
}
