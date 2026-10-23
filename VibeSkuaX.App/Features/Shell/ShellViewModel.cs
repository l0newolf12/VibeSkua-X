using CommunityToolkit.Mvvm.ComponentModel;

namespace VibeSkuaX.App.Features.Shell;

public partial class ShellViewModel : ObservableObject
{
    public string Title => "VibeSkua X";

    [ObservableProperty]
    private string _status = "Starting game…";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _hasError;

    public void SetStatus(string status, string? error)
    {
        Status = status;
        ErrorMessage = error;
        HasError = error is not null;
    }
}
