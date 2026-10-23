namespace Skua.Core.Interfaces;

/// <summary>Requests a named tool from a host; presentation belongs to the host.</summary>
public interface IWindowService
{
    void ShowManagedWindow(string key);
}
