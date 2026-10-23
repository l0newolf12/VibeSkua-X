using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using VibeSkuaX.App.Features.Shell;

namespace VibeSkuaX.App;

public partial class App : Avalonia.Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
            desktop.MainWindow = new ShellWindow();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
