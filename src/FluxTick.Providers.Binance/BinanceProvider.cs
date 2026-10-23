using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Binance;

/// <summary>
/// Streams 24h mini ticker updates (roughly once per second per symbol) from Binance and reconnects with
/// exponential backoff when the connection drops.
/// </summary>
public sealed class BinanceProvider : CryptoExchangeProvider
{
    /// <summary>
    /// Quote currencies, longest first, used to split a symbol into a readable pair.
    /// </summary>
    private static readonly string[] QuoteAssets = ["FDUSD", "USDT", "USDC", "BUSD", "TUSD", "BTC", "ETH", "BNB", "EUR", "TRY", "BRL", "GBP"];

    /// <summary>
    /// The next control message id.
    /// </summary>
    private int _requestId;

    /// <summary>
    /// Initializes a new instance of the <see cref="BinanceProvider"/> class.
    /// </summary>
    /// <param name="descriptor">
    /// The provider descriptor.
    /// </param>
    /// <param name="endpoints">
    /// The Binance addresses.
    /// </param>
    /// <param name="http">
    /// The HTTP client for REST calls; the provider takes ownership.
    /// </param>
    /// <param name="timeProvider">
    /// The clock.
    /// </param>
    /// <param name="logger">
    /// The logger.
    /// </param>
    internal BinanceProvider(
        ProviderDescriptor descriptor,
        ExchangeEndpoints endpoints,
        HttpClient http,
        TimeProvider timeProvider,
        ILogger logger)
        : base(descriptor, endpoints, http, timeProvider, logger)
    {
    }

    /// <inheritdoc />
    protected override string SymbolsRequest => "/api/v3/ticker/price";

    /// <inheritdoc />
    protected override string HistoryRequest(Asset asset) =>
        $"/api/v3/klines?symbol={Uri.EscapeDataString(asset.Symbol)}&interval=1m&limit=60";

    /// <inheritdoc />
    protected override IReadOnlyList<PricePoint> ParseHistory(string json) => BinanceMessages.ParseKlines(json);

    /// <inheritdoc />
    protected override IReadOnlyList<ListedSymbol> ParseSymbols(string json) =>
        BinanceMessages.ParseSymbols(json).Select(s => new ListedSymbol(s, DisplayNameOf(s))).ToArray();

    /// <inheritdoc />
    protected override IEnumerable<string> BuildSubscribeMessages(IReadOnlyCollection<Asset> assets) =>
        [BinanceMessages.ControlMessage("SUBSCRIBE", assets.Select(a => a.Symbol), NextId())];

    /// <inheritdoc />
    protected override IEnumerable<string> BuildUnsubscribeMessages(IReadOnlyCollection<Asset> assets) =>
        [BinanceMessages.ControlMessage("UNSUBSCRIBE", assets.Select(a => a.Symbol), NextId())];

    /// <inheritdoc />
    protected override void HandleMessage(string text)
    {
        if (BinanceMessages.TryParseMiniTicker(text, Descriptor.Id, out var quote))
        {
            Publish(quote);
        }
    }

    /// <summary>
    /// Builds a readable pair name such as <c>BTC/USDT</c> from a Binance symbol.
    /// </summary>
    /// <param name="symbol">
    /// The Binance symbol.
    /// </param>
    /// <returns>
    /// The readable name, or the symbol itself when the quote currency is unknown.
    /// </returns>
    internal static string DisplayNameOf(string symbol)
    {
        foreach (var quote in QuoteAssets)
        {
            if (symbol.Length > quote.Length && symbol.EndsWith(quote, StringComparison.Ordinal))
            {
                return $"{symbol[..^quote.Length]}/{quote}";
            }
        }

        return symbol;
    }

    /// <summary>
    /// Gets the next control message id.
    /// </summary>
    /// <returns>
    /// A unique id.
    /// </returns>
    private int NextId() => Interlocked.Increment(ref _requestId);
}
