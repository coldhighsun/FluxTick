using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Tencent;

/// <summary>
/// Creates <see cref="TencentProvider"/> instances.
/// </summary>
public sealed class TencentProviderFactory : IMarketDataProviderFactory
{
    /// <summary>
    /// The provider id.
    /// </summary>
    public const string ProviderId = "tencent";

    /// <summary>
    /// The option key of the poll interval.
    /// </summary>
    public const string PollIntervalKey = ProviderOptionDefinition.PollIntervalKey;

    /// <summary>
    /// The option key of the quote endpoint.
    /// </summary>
    public const string QuoteUrlKey = "QuoteUrl";

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new(
        ProviderId,
        "Tencent Finance",
        "A-shares, Hong Kong and US stocks, ETFs and indices by polling Tencent's public quote service. No API key required. Unofficial; data may be delayed. Minute history covers A-shares and Hong Kong only.",
        [AssetClass.Equity, AssetClass.Index],
        ProviderUpdateMode.Polling,
        Homepage: new Uri("https://gu.qq.com/"),
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
                "https://qt.gtimg.cn",
                "Used for prices. Symbol search and history use smartbox.gtimg.cn and web.ifzq.gtimg.cn."),
        ],
        EnabledByDefault: false);

    /// <inheritdoc />
    public IMarketDataProvider Create(ProviderCreationContext context)
    {
        var quoteUri = EndpointOptions.GetHttpUri(context.Options, Descriptor, QuoteUrlKey);
        var http = AppHttpClient.Create(decompress: true);
        return new TencentProvider(
            Descriptor,
            quoteUri,
            context.Options.GetDuration(PollIntervalKey),
            http,
            context.TimeProvider,
            context.LoggerFactory.CreateLogger<TencentProvider>());
    }
}
