using FluxTick.Abstractions;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Common;

/// <summary>
/// A price line as a batch quote service answers it.
/// </summary>
/// <param name="Key">
/// The code the line answers; matched to the requested codes without regard to case.
/// </param>
/// <param name="Price">
/// The last price.
/// </param>
/// <param name="PreviousClose">
/// The previous close, or <see langword="null"/> when unknown.
/// </param>
/// <param name="Time">
/// The quote time, or <see langword="null"/> when the line carries none that can be read.
/// </param>
public sealed record ParsedQuote(string Key, decimal Price, decimal? PreviousClose, DateTimeOffset? Time);

/// <summary>
/// Base class for polling sources whose service answers many codes in one request (Tencent and Sina). It groups the
/// subscriptions by code, asks for them in batches, publishes each answered line under the symbol that was subscribed
/// and reports the codes the service did not answer for; derived classes only build the request and parse the answer.
/// </summary>
public abstract class BatchQuotePollingProviderBase : PollingProviderBase
{
    /// <summary>
    /// The most codes asked for in one request, to keep the URL short.
    /// </summary>
    private const int BatchSize = 50;

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
        // A symbol may have been typed in another case than the service answers with; several subscriptions can
        // share one code. Quotes are published under the symbol that was subscribed.
        var byCode = new Dictionary<string, List<Asset>>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in assets)
        {
            var code = ToRequestCode(asset.Symbol);
            if (!byCode.TryGetValue(code, out var list))
            {
                byCode[code] = list = [];
            }

            list.Add(asset);
        }

        var succeeded = 0;
        var failed = 0;
        string? lastError = null;
        foreach (var batch in byCode.Keys.Chunk(BatchSize))
        {
            try
            {
                var answered = await FetchAsync(batch, byCode, cancellationToken).ConfigureAwait(false);
                succeeded++;
                foreach (var code in batch.Where(c => !answered.Contains(c)))
                {
                    lastError = $"No data for {byCode[code][0].Symbol}";
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                failed++;
                lastError = ex.Message;
                Logger.LogWarning(ex, "{Provider} request failed", Descriptor.DisplayName);
            }
        }

        return new PollOutcome(succeeded, failed, lastError, BackOff: succeeded == 0);
    }

    /// <summary>
    /// Requests one batch of codes and publishes the quotes of the answer.
    /// </summary>
    /// <param name="codes">
    /// The codes to request.
    /// </param>
    /// <param name="byCode">
    /// The subscribed instruments by code.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the request.
    /// </param>
    /// <returns>
    /// The codes the service answered for.
    /// </returns>
    private async Task<HashSet<string>> FetchAsync(
        string[] codes,
        Dictionary<string, List<Asset>> byCode,
        CancellationToken cancellationToken)
    {
        var answered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var quote in await RequestQuotesAsync(codes, cancellationToken).ConfigureAwait(false))
        {
            if (!byCode.TryGetValue(quote.Key, out var subscribed))
            {
                continue;
            }

            answered.Add(quote.Key);
            foreach (var asset in subscribed)
            {
                Publish(new Quote(asset.Id, quote.Price, quote.PreviousClose, quote.Time ?? TimeProvider.GetUtcNow()) { TimeIsEstimated = quote.Time is null });
            }
        }

        return answered;
    }
}
