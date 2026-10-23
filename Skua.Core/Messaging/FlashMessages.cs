using System.ComponentModel;

namespace Skua.Core.Messaging;

public sealed record FlashChangedMessage<T>(T Flash) where T : IComponent;

public sealed record FlashErrorMessage(Exception Exception, string Function, params object[] Args);

/// <summary>The native runtime has disconnected and can no longer serve calls.</summary>
public sealed record FlashRuntimeFailedMessage(Exception Exception);
