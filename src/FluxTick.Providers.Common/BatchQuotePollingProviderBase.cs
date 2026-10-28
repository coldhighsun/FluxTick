using FluxTick.Abstractions;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Common;

/// <summary>
/// Base class for polling sources whose service answers many codes in one request (Tencent and Sina). It groups the
/// subscriptions by code, asks for them in batches, publishes each answered line under the symbol that was subscribed
/// and reports the codes the service did not answer for; derived classes only build the request and parse the answer.
/// </summary>
public abstract class BatchQuotePollingProviderBase : PollingProviderBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BatchQuotePollingProviderBase"/> class.
    /// </summary>
    /// <param name="descriptor">
    /// The provider descriptor.
    /// </param>
    /// <param name="interval">
    /// The poll interval.
    /// </param>
    /// <param name="timeProvider">
    /// The clock.
    /// </param>
    /// <param name="logger">
    /// The logger.
    /// </param>
    protected BatchQuotePollingProviderBase(ProviderDescriptor descriptor, TimeSpan interval, TimeProvider timeProvider, ILogger logger)
        : base(descriptor, interval, timeProvider, logger)
    {
    }

    /// <summary>
    /// Brings a subscribed symbol into the form the service expects, so a user may type it in another case.
    /// </summary>
    /// <param name="symbol">
    /// The symbol as stored or typed.
    /// </param>
    /// <returns>
    /// The code to request.
    /// </returns>
    protected abstract string ToRequestCode(string symbol);

    /// <summary>
    /// Requests one batch of codes and parses the answer.
    /// </summary>
    /// <param name="codes">
    /// The codes to request, at most one batch.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the request.
    /// </param>
    /// <returns>
    /// The lines the service answered with a price.
    /// </returns>
    protected abstract Task<IReadOnlyList<ParsedQuote>> RequestQuotesAsync(string[] codes, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches the given instruments in batches and publishes their quotes.
    /// </summary>
    /// <param name="assets">
    /// The instruments to fetch.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the cycle.
    /// </param>
    /// <returns>
    /// How the cycle went; the next cycle backs off when no batch succeeded.
    /// </returns>
    protected sealed override async Task<PollOutcome> PollOnceAsync(IReadOnlyCollection<Asset> assets, CancellationToken cancellationToken)
    {
        return await BatchQuoteFetcher.FetchAsync(
            assets,
            ToRequestCode,
            RequestQuotesAsync,
            Publish,
            Logger,
            Descriptor.DisplayName,
            cancellationToken).ConfigureAwait(false);
    }
}
