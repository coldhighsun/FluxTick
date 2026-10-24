namespace FluxTick.Abstractions;

/// <summary>
/// The connection state of a provider.
/// </summary>
public enum ProviderConnectionState
{
    /// <summary>
    /// The provider is idle, for example because nothing is subscribed.
    /// </summary>
    Idle,

    /// <summary>
    /// The provider is delivering data.
    /// </summary>
    Connected,

    /// <summary>
    /// The provider lost its source and is retrying.
    /// </summary>
    Reconnecting,

    /// <summary>
    /// The provider cannot obtain data and the cause needs attention.
    /// </summary>
    Failed,

    /// <summary>
    /// The provider is deliberately disconnected because nobody looks at the data.
    /// </summary>
    Paused,
}

/// <summary>
/// A provider's current state with an optional human readable detail.
/// </summary>
/// <param name="State">
/// The connection state.
/// </param>
/// <param name="Message">
/// Optional detail such as the last error.
/// </param>
public sealed record ProviderStatus(ProviderConnectionState State, string? Message = null);
