using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.TwelveData;

/// <summary>
/// Creates <see cref="TwelveDataProvider"/> instances.
/// </summary>
public sealed class TwelveDataProviderFactory : IMarketDataProviderFactory
{
    /// <summary>
    /// The provider id.
    /// </summary>
    public const string ProviderId = "twelvedata";

    /// <summary>
    /// The key of the API key option.
    /// </summary>
    public const string ApiKeyKey = "ApiKey";

    /// <summary>
    /// The key of the poll interval option.
    /// </summary>
    public const string PollIntervalKey = ProviderOptionDefinition.PollIntervalKey;

    /// <summary>
    /// The key of the API endpoint option.
    /// </summary>
    public const string BaseUrlKey = "BaseUrl";

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new(
        ProviderId,
        "Twelve Data",
        "Stocks and ETFs worldwide from the Twelve Data API, by polling. Needs an API key; a free key allows 8 credits a minute and 800 a day, and a request costs one credit per symbol, so the default interval suits about 8 symbols. Free prices may be delayed.",
        [AssetClass.Equity, AssetClass.Index],
        ProviderUpdateMode.Polling,
        Homepage: new Uri("https://twelvedata.com/"),
        Options:
        [
            new ProviderOptionDefinition(
                ApiKeyKey,
                "API key",
                ProviderOptionType.Secret,
                string.Empty,
                "Create a free key at twelvedata.com. A free key allows 8 credits a minute and 800 a day; every symbol of every refresh, and every chart request, costs a credit.",
                Required: true),
            new ProviderOptionDefinition(
                PollIntervalKey,
                "Poll interval",
                ProviderOptionType.Duration,
                "00:01:00",
                "How often prices are refreshed. With the free key, keep (symbols x 60 seconds / 8) below this value to stay within the per-minute limit.",
                Min: 5,
                Max: 3600),
            new ProviderOptionDefinition(
                BaseUrlKey,
                "API endpoint",
                ProviderOptionType.String,
                "https://api.twelvedata.com",
                "Used for prices, history and symbol search.",
                Advanced: true),
        ],
        EnabledByDefault: false);

    /// <inheritdoc />
    public IMarketDataProvider Create(ProviderCreationContext context) =>
        new TwelveDataProvider(
            Descriptor,
            EndpointOptions.GetHttpUri(context.Options, Descriptor, BaseUrlKey),
            context.Options.GetString(ApiKeyKey).Trim(),
            context.Options.GetDuration(PollIntervalKey),
            AppHttpClient.Create(decompress: true, logger: context.LoggerFactory.CreateLogger<TwelveDataProvider>()),
            context.TimeProvider,
            context.LoggerFactory.CreateLogger<TwelveDataProvider>());
}
