using Avalonia.Controls;
using Avalonia.Interactivity;

namespace VibeSkuaX.App.Features.Shell;

public partial class CloseGameWindow : Window
{
    public CloseGameWindow() : this("this game") { }

    internal CloseGameWindow(string label)
    {
        InitializeComponent();
        CloseGameName.Text = label;
        CloseMessage.Text = "This ends its game session. You will need to log in again to play.";
        Opened += (_, _) => CancelGameClose.Focus();
    }

    private void CancelClicked(object? sender, RoutedEventArgs e) => Close(false);
    private void ConfirmClicked(object? sender, RoutedEventArgs e) => Close(true);
}
