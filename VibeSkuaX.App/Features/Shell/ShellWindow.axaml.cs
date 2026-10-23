using Avalonia.Controls;
using Avalonia.Interactivity;

namespace VibeSkuaX.App.Features.Shell;

public partial class ShellWindow : Window
{
    private readonly ShellViewModel _viewModel = new();

    public ShellWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        GameViewport.StatusChanged += _viewModel.SetStatus;
    }

    private void MinimizeClicked(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeClicked(object? sender, RoutedEventArgs e) => ToggleMaximize();
    private void CloseClicked(object? sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    protected override void OnPropertyChanged(Avalonia.AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty && MaximizeButton is not null)
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    protected override void OnClosed(EventArgs e)
    {
        GameViewport.StatusChanged -= _viewModel.SetStatus;
        GameViewport.Dispose();
        base.OnClosed(e);
    }
}
