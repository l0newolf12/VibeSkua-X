using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Control = Avalonia.Controls.Control;
using UserControl = Avalonia.Controls.UserControl;

namespace VibeSkuaX.App.Controls;

/// <summary>Shared native caption behavior with an optional custom header such as account tabs.</summary>
public partial class WindowTitleBar : UserControl
{
    public static readonly StyledProperty<Control?> HeaderContentProperty =
        AvaloniaProperty.Register<WindowTitleBar, Control?>(nameof(HeaderContent));

    private Window? _window;

    public WindowTitleBar() => InitializeComponent();

    public Control? HeaderContent
    {
        get => GetValue(HeaderContentProperty);
        set => SetValue(HeaderContentProperty, value);
    }

    // A native game needs its own focus restoration after a window-state change.
    public event EventHandler? MaximizeToggled;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is not null) _window.PropertyChanged += WindowChanged;
        UpdateCaption();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_window is not null) _window.PropertyChanged -= WindowChanged;
        _window = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == HeaderContentProperty && TitleDragArea is not null)
            TitleDragArea.IsVisible = HeaderContent is null;
    }

    private void WindowChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.TitleProperty || e.Property == Window.WindowStateProperty ||
            e.Property == Window.CanMinimizeProperty || e.Property == Window.CanMaximizeProperty)
            UpdateCaption();
    }

    private void UpdateCaption()
    {
        if (_window is null) return;
        TitleText.Text = _window.Title;
        TitleDragArea.IsVisible = HeaderContent is null;
        MinimizeButton.IsVisible = _window.CanMinimize;
        MaximizeButton.IsVisible = _window.CanMaximize;
        bool maximized = _window.WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
        AutomationProperties.SetName(MaximizeButton, maximized ? "Restore" : "Maximize");
    }

    private void MinimizeClicked(object? sender, RoutedEventArgs e)
    {
        if (_window is { CanMinimize: true }) _window.WindowState = WindowState.Minimized;
    }

    private void MaximizeClicked(object? sender, RoutedEventArgs e)
    {
        if (_window is not { CanMaximize: true }) return;
        _window.WindowState = _window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        MaximizeToggled?.Invoke(this, EventArgs.Empty);
    }

    private void CloseClicked(object? sender, RoutedEventArgs e) => _window?.Close();
}
