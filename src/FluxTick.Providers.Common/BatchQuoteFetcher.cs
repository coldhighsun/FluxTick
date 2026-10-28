using FluxTick.Abstractions;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Common;

/// <summary>
/// Fetches the quotes of a batch service (Tencent and Sina) for subscribed instruments: it groups them by code, asks
/// for the codes in batches, publishes each answered line under the symbol that was subscribed and reports the codes the
/// service did not answer for. Both the polling and the REST fallback of streaming sources use it.
/// </summary>
public static class BatchQuoteFetcher
{
    /// <summary>
    /// The most codes asked for in one request, to keep the URL short.
    /// </summary>
    private const int BatchSize = 50;

    /// <summary>
    /// The problems of a round in which every code answered.
    /// </summary>
    private static readonly IReadOnlyDictionary<AssetId, string> NoProblems = new Dictionary<AssetId, string>();

    /// <summary>
    /// Fetches the given instruments in batches and publishes their quotes.
    /// </summary>
    /// <param name="assets">
    /// The instruments to fetch.
    /// </param>
    /// <param name="toRequestCode">
    /// Brings a subscribed symbol into the form the service expects.
    /// </param>
    /// <param name="request">
    /// Requests one batch of codes and parses the answer.
    /// </param>
    /// <param name="publish">
    /// Publishes a parsed line for a subscribed instrument.
    /// </param>
    /// <param name="logger">
    /// The logger.
    /// </param>
    /// <param name="providerName">
    /// The name of the source, for the log.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the round.
    /// </param>
    /// <returns>
    /// How the round went; the next round backs off when no batch succeeded.
    /// </returns>
    public static async Task<PollOutcome> FetchAsync(
        IReadOnlyCollection<Asset> assets,
        Func<string, string> toRequestCode,
        Func<string[], CancellationToken, Task<IReadOnlyList<ParsedQuote>>> request,
        Action<Asset, ParsedQuote> publish,
        ILogger logger,
        string providerName,
        CancellationToken cancellationToken)
    {
        // A symbol may have been typed in another case than the service answers with; several subscriptions can
        // share one code. Quotes are published under the symbol that was subscribed.
        var byCode = new Dictionary<string, List<Asset>>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in assets)
        {
            var code = toRequestCode(asset.Symbol);
            if (!byCode.TryGetValue(code, out var list))
            {
                byCode[code] = list = [];
            }

            list.Add(asset);
        }

        var succeeded = 0;
        var failed = 0;
        string? lastError = null;
        Dictionary<AssetId, string>? problems = null;
        foreach (var batch in byCode.Keys.Chunk(BatchSize))
        {
            try
            {
                var answered = await FetchBatchAsync(batch, byCode, request, publish, cancellationToken).ConfigureAwait(false);
                succeeded++;
                foreach (var code in batch.Where(c => !answered.Contains(c)))
                {
                    lastError = $"No data for {byCode[code][0].Symbol}";
                    foreach (var asset in byCode[code])
                    {
                        (problems ??= [])[asset.Id] = $"No data for {asset.Symbol}";
                    }
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                failed++;
                lastError = ex.Message;
                foreach (var asset in batch.SelectMany(code => byCode[code]))
                {
                    (problems ??= [])[asset.Id] = ex.Message;
                }

                logger.LogWarning(ex, "{Provider} request failed", providerName);
            }
        }

        return new PollOutcome(succeeded, failed, lastError, BackOff: succeeded == 0, problems ?? NoProblems);
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
    /// <param name="request">
    /// Requests one batch of codes and parses the answer.
    /// </param>
    /// <param name="publish">
    /// Publishes a parsed line for a subscribed instrument.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the request.
    /// </param>
    /// <returns>
    /// The codes the service answered for.
    /// </returns>
    private static async Task<HashSet<string>> FetchBatchAsync(
        string[] codes,
        Dictionary<string, List<Asset>> byCode,
        Func<string[], CancellationToken, Task<IReadOnlyList<ParsedQuote>>> request,
        Action<Asset, ParsedQuote> publish,
        CancellationToken cancellationToken)
    {
        var answered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var quote in await request(codes, cancellationToken).ConfigureAwait(false))
        {
            if (!byCode.TryGetValue(quote.Key, out var subscribed))
            {
                continue;
            }

            answered.Add(quote.Key);
            foreach (var asset in subscribed)
            {
                publish(asset, quote);
            }
        }

        return answered;
    }
}
