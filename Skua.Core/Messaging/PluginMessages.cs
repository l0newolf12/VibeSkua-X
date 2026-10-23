using Skua.Core.Interfaces;

namespace Skua.Core.Messaging;

public sealed record PluginLoadedMessage(IPluginContainer Container);
public sealed record PluginUnloadedMessage(IPluginContainer Container);
