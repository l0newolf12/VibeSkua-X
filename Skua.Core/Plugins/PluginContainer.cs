using CommunityToolkit.Mvvm.DependencyInjection;
using Skua.Core.Interfaces;
using Skua.Core.Models;

namespace Skua.Core.Plugins;

public class PluginContainer : IPluginContainer
{
    private readonly SessionServices _session = new();
    public IOptionContainer OptionContainer { get; }
    public ISkuaPlugin Plugin { get; }
    public string OptionsFile => Path.Combine(ClientFileSources.SkuaPluginsDIR, "options", $"{Plugin.OptionsStorage}.cfg");

    public PluginContainer(ISkuaPlugin plugin)
    {
        OptionContainer = _session.GetRequiredService<IOptionContainer>();
        Plugin = plugin;
        if (plugin.Options is not null)
            OptionContainer.Options.AddRange(plugin.Options);
        OptionContainer.OptionsFile = OptionsFile;
    }

    public override string ToString()
    {
        return Plugin.Name;
    }
}
