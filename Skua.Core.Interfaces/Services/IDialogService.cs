using Skua.Core.Models;

namespace Skua.Core.Interfaces;

/// <summary>Host interaction requests without dependencies on presentation classes.</summary>
public interface IDialogService
{
    /// <summary>Requests option values and calls back only when the host confirms them.</summary>
    void ConfigureOptions(IOptionContainer optionContainer, Action<IReadOnlyDictionary<IOption, string>> onConfirmed);

    void ShowMessageBox(string message, string caption);
    bool? ShowMessageBox(string message, string caption, bool yesAndNo);
    DialogResult ShowMessageBox(string message, string caption, params string[] buttons);

    /// <summary>Returns the entered text, or null when the host cancels the request.</summary>
    string? RequestInput(string title, string hint, bool numberOnly = false);
}
