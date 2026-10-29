using System.Collections.Concurrent;
using FluxTick.Abstractions;

namespace FluxTick.Providers.Common;

/// <summary>
/// Remembers the latest quote of every instrument of a source and repeats it for instruments whose market is shut. A
/// source does not ask for the price of a shut market, so nothing arrives for it; whoever reads the quotes takes that
/// silence for a source that went quiet. A repeated quote, with the trade time it had, says the source is still there.
/// </summary>
/// <param name="isSubscribed">
/// Tells whether an instrument is subscribed at this moment, so that a quote that races with the removal of its instrument
/// cannot bring the instrument back.
/// </param>
internal sealed class QuoteRepeater(Func<AssetId, bool> isSubscribed)
{
    /// <summary>
    /// Whether the calling thread is publishing a repeated quote, so that the hook that follows what is published can tell
    /// a repeat from news. The hook runs on the publishing thread.
    /// </summary>
    [ThreadStatic]
    private static bool _repeating;

    /// <summary>
    /// The latest quote of every instrument that has published one.
    /// </summary>
    private readonly ConcurrentDictionary<AssetId, Quote> _latest = new();

    /// <summary>
    /// Gets a value indicating whether the calling thread is publishing a repeated quote, which says nothing new about the
    /// instrument and must not count as a quote that arrived.
    /// </summary>
    public static bool IsRepeating => _repeating;

    /// <summary>
    /// Remembers a quote that was published. A repeated quote is not news and leaves what is remembered as it is, so that a
    /// repeat that races with a newer quote cannot bring the older one back. The quote of an instrument that was removed in
    /// the meantime is not kept, as it would be repeated, as old as it is, if the instrument were added again.
    /// </summary>
    /// <param name="quote">
    /// The quote.
    /// </param>
    public void Remember(Quote quote)
    {
        if (_repeating)
        {
            return;
        }

        _latest[quote.AssetId] = quote;
        if (!isSubscribed(quote.AssetId))
        {
            _latest.TryRemove(quote.AssetId, out _);
        }
    }

    /// <summary>
    /// Forgets the quote of an instrument that is no longer subscribed.
    /// </summary>
    /// <param name="id">
    /// The instrument.
    /// </param>
    public void Forget(AssetId id) => _latest.TryRemove(id, out _);

    /// <summary>
    /// Splits the subscriptions into those to ask the source for and those whose market is shut, which are not asked for
    /// and are to be repeated.
    /// </summary>
    /// <param name="subscribed">
    /// The subscribed instruments.
    /// </param>
    /// <param name="isOutsideTrading">
    /// Tells whether the market of an instrument is known not to be trading; it is asked once for each instrument.
    /// </param>
    /// <param name="askUnpriced">
    /// Whether a shut instrument that has no quote yet is asked for anyway, until it has one.
    /// </param>
    /// <param name="shut">
    /// The instruments whose market is shut.
    /// </param>
    /// <returns>
    /// The instruments to ask the source for.
    /// </returns>
    public Asset[] Split(IEnumerable<Asset> subscribed, Func<Asset, bool> isOutsideTrading, bool askUnpriced, out Asset[] shut)
    {
        List<Asset> open = [];
        List<Asset> closed = [];
        foreach (var asset in subscribed)
        {
            var isShut = isOutsideTrading(asset) && (!askUnpriced || _latest.ContainsKey(asset.Id));
            (isShut ? closed : open).Add(asset);
        }

        shut = [.. closed];
        return [.. open];
    }

    /// <summary>
    /// Publishes again the latest quote of every instrument that is shut.
    /// </summary>
    /// <param name="shut">
    /// The instruments whose market is shut.
    /// </param>
    /// <param name="publish">
    /// Publishes a quote.
    /// </param>
    public void Repeat(IEnumerable<Asset> shut, Action<Quote> publish)
    {
        _repeating = true;
        try
        {
            foreach (var asset in shut)
            {
                if (_latest.TryGetValue(asset.Id, out var quote))
                {
                    publish(quote);
                }
            }
        }
        finally
        {
            _repeating = false;
        }
    }
}
