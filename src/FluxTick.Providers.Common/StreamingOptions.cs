using FluxTick.Abstractions;

namespace FluxTick.Providers.Common;

/// <summary>
/// Option definitions shared by the sources that stream and use REST for what the stream does not carry, so that they
/// are declared once.
/// </summary>
public static class StreamingOptions
{
    /// <summary>
    /// Gets the option that sets how long a live stream, or one instrument on it, may stay quiet during trading hours before
    /// the source polls its REST API as well. The value is read with <c>GetDuration</c> under <see cref="ProviderOptionDefinition.QuietPollIntervalKey"/>
    /// and passed to <see cref="RestBackedWebSocketProviderBase"/>.
    /// </summary>
    public static ProviderOptionDefinition QuietPollInterval { get; } = new(
        ProviderOptionDefinition.QuietPollIntervalKey,
        "Quiet-stream poll interval",
        ProviderOptionType.Duration,
        "00:05:00",
        "While the live stream is connected, how long it, or a single instrument on it, may deliver no prices before they are topped up from the API during trading hours. Not used while the stream is down (see the fallback poll interval) or while the market is not open.",
        Min: 10,
        Max: 3600);
}
