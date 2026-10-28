using FluxTick.Abstractions;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Common;

/// <summary>
/// Base class of the sources that log: it holds the logger and writes the failure of a status listener to it, which the
/// base class in the abstractions leaves to its derived classes because it has no logger.
/// </summary>
public abstract class LoggingProviderBase : MarketDataProviderBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LoggingProviderBase"/> class.
    /// </summary>
    /// <param name="descriptor">
    /// The provider descriptor.
    /// </param>
    /// <param name="timeProvider">
    /// The clock.
    /// </param>
    /// <param name="logger">
    /// The logger.
    /// </param>
    protected LoggingProviderBase(ProviderDescriptor descriptor, TimeProvider timeProvider, ILogger logger)
        : base(descriptor, timeProvider)
    {
        Logger = logger;
    }

    /// <summary>
    /// Gets the logger.
    /// </summary>
    protected ILogger Logger { get; }

    /// <summary>
    /// Publishes a parsed line for a subscribed instrument, under the symbol it was subscribed with.
    /// </summary>
    /// <param name="asset">
    /// The subscribed instrument.
    /// </param>
    /// <param name="quote">
    /// The parsed line.
    /// </param>
    protected void Publish(Asset asset, ParsedQuote quote) => Publish(asset, quote.Price, quote.PreviousClose, quote.Time);

    /// <summary>
    /// Publishes a parsed line for every subscribed instrument its key names.
    /// </summary>
    /// <param name="quote">
    /// The parsed line.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when an instrument with that key is subscribed.
    /// </returns>
    protected bool PublishToSubscribers(ParsedQuote quote) => PublishToSubscribers(quote.Key, quote.Price, quote.PreviousClose, quote.Time);

    /// <inheritdoc />
    protected override void OnStatusListenerFailed(Exception exception) =>
        Logger.LogWarning(exception, "A listener of the {Provider} status failed", Descriptor.DisplayName);
}
