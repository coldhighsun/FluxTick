using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Sina;

/// <summary>
/// Creates <see cref="SinaProvider"/> instances.
/// </summary>
public sealed class SinaProviderFactory : IMarketDataProviderFactory
{
    /// <summary>
    /// The provider id.
    /// </summary>
    public const string ProviderId = "sina";

    /// <summary>
    /// The option key of the poll interval.
    /// </summary>
    public const string PollIntervalKey = "PollInterval";

    /// <summary>
    /// The option key of the quote endpoint.
    /// </summary>
    public const string QuoteUrlKey = "QuoteUrl";

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new(
        ProviderId,
        "Sina Finance",
        "A-share stocks, ETFs and indices (Shanghai, Shenzhen, Beijing) by polling Sina's public quote service. Symbols look like sh600000 or sz000001, the same as Tencent's. Instruments stay with the source they were added from, so to use Sina add them again under it. No API key required. Unofficial; data may be delayed or rate limited.",
        [AssetClass.Equity, AssetClass.Index],
        ProviderUpdateMode.Polling,
        Homepage: new Uri("https://finance.sina.com.cn/"),
        Options:
        [
            new ProviderOptionDefinition(
                PollIntervalKey,
                "Poll interval",
                ProviderOptionType.Duration,
                "00:00:05",
                "How often prices are refreshed.",
                Min: 2,
                Max: 300),
            new ProviderOptionDefinition(
                QuoteUrlKey,
                "Quote endpoint",
                ProviderOptionType.String,
                "https://hq.sinajs.cn",
                "Used for prices. Symbol search uses suggest3.sinajs.cn."),
        ],
        EnabledByDefault: false);

    /// <inheritdoc />
    public IMarketDataProvider Create(ProviderCreationContext context)
    {
        var quoteUri = EndpointOptions.GetHttpUri(context.Options, Descriptor, QuoteUrlKey);
        var http = AppHttpClient.Create(decompress: true);
        return new SinaProvider(
            Descriptor,
            quoteUri,
            context.Options.GetDuration(PollIntervalKey),
            http,
            context.TimeProvider,
            context.LoggerFactory.CreateLogger<SinaProvider>());
    }
}
