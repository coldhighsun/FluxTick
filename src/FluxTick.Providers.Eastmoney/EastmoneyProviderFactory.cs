using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Eastmoney;

/// <summary>
/// Creates <see cref="EastmoneyProvider"/> instances.
/// </summary>
public sealed class EastmoneyProviderFactory : IMarketDataProviderFactory
{
    /// <summary>
    /// The provider id.
    /// </summary>
    public const string ProviderId = "eastmoney";

    /// <summary>
    /// The option key of the poll interval.
    /// </summary>
    public const string PollIntervalKey = ProviderOptionDefinition.PollIntervalKey;

    /// <summary>
    /// The option key of the quote endpoint.
    /// </summary>
    public const string QuoteUrlKey = "QuoteUrl";

    /// <summary>
    /// The option key of the stream endpoint.
    /// </summary>
    public const string StreamUrlKey = "StreamUrl";

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new(
        ProviderId,
        "Eastmoney",
        "A-shares, Hong Kong and US stocks, ETFs and indices from Eastmoney's public quote service: prices are pushed over one event stream per instrument, with the batch quote service as the first price and as a fallback. No API key required. Unofficial; data may be delayed. Symbols are written as market.code, for example 1.600519 (Shanghai), 0.000001 (Shenzhen), 116.00700 (Hong Kong) or 105.AAPL (US); symbol search fills them in.",
        [AssetClass.Equity, AssetClass.Index],
        ProviderUpdateMode.Streaming,
        Homepage: new Uri("https://quote.eastmoney.com/"),
        Options:
        [
            new ProviderOptionDefinition(
                PollIntervalKey,
                "Fallback poll interval",
                ProviderOptionType.Duration,
                "00:00:05",
                "How often prices are fetched for an instrument whose live stream is down. Nothing is fetched for a market that is shut.",
                Min: 2,
                Max: 300),
            new ProviderOptionDefinition(
                StreamUrlKey,
                "Stream endpoint",
                ProviderOptionType.String,
                "https://push2.eastmoney.com",
                "Used for the live price streams, one connection per instrument.",
                Advanced: true),
            new ProviderOptionDefinition(
                QuoteUrlKey,
                "Quote endpoint",
                ProviderOptionType.String,
                "https://push2delay.eastmoney.com",
                "Used for first prices and as a fallback. Symbol search and history use searchadapter.eastmoney.com and push2his.eastmoney.com.",
                Advanced: true),
        ],
        EnabledByDefault: false,
        Recommendation: 3);

    /// <inheritdoc />
    public IMarketDataProvider Create(ProviderCreationContext context)
    {
        var quoteUri = EndpointOptions.GetHttpUri(context.Options, Descriptor, QuoteUrlKey);
        var http = AppHttpClient.Create(decompress: true, logger: context.LoggerFactory.CreateLogger<EastmoneyProvider>());
        return new EastmoneyProvider(
            Descriptor,
            quoteUri,
            EndpointOptions.GetHttpUri(context.Options, Descriptor, StreamUrlKey),
            context.Options.GetDuration(PollIntervalKey),
            http,
            context.TimeProvider,
            context.LoggerFactory.CreateLogger<EastmoneyProvider>());
    }
}
