using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Massive;

/// <summary>
/// Creates <see cref="MassiveProvider"/> instances.
/// </summary>
public sealed class MassiveProviderFactory : IMarketDataProviderFactory
{
    /// <summary>
    /// The provider id.
    /// </summary>
    public const string ProviderId = "massive";

    /// <summary>
    /// The key of the API key option.
    /// </summary>
    public const string ApiKeyKey = "ApiKey";

    /// <summary>
    /// The key of the poll interval option.
    /// </summary>
    public const string PollIntervalKey = ProviderOptionDefinition.PollIntervalKey;

    /// <summary>
    /// The key of the option that spaces the requests of one refresh.
    /// </summary>
    public const string RequestSpacingKey = "RequestSpacing";

    /// <summary>
    /// The key of the API endpoint option.
    /// </summary>
    public const string BaseUrlKey = "BaseUrl";

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new(
        ProviderId,
        "Massive",
        "US stocks and ETFs from the Massive (formerly Polygon.io) API, by polling. Needs an API key. The free plan has no WebSocket and no snapshot: prices are the delayed one-minute bars, limited to 5 requests a minute (one or two per symbol), so it suits a few symbols and is not live. A paid plan adds a stream.",
        [AssetClass.Equity],
        ProviderUpdateMode.Polling,
        Homepage: new Uri("https://massive.com/"),
        Options:
        [
            new ProviderOptionDefinition(
                ApiKeyKey,
                "API key",
                ProviderOptionType.Secret,
                string.Empty,
                "Create a free key at massive.com. The free plan allows 5 requests a minute and has no WebSocket.",
                Required: true),
            new ProviderOptionDefinition(
                PollIntervalKey,
                "Poll interval",
                ProviderOptionType.Duration,
                "00:05:00",
                "How often prices are refreshed. With the free plan, keep (symbols x 13 seconds) below this value.",
                Min: 5,
                Max: 3600),
            new ProviderOptionDefinition(
                RequestSpacingKey,
                "Request spacing",
                ProviderOptionType.Duration,
                "00:00:13",
                "The wait between the requests of one refresh, one request per symbol. The free plan allows 5 requests a minute; lower this on a paid plan.",
                Min: 1,
                Max: 120,
                Advanced: true),
            new ProviderOptionDefinition(
                BaseUrlKey,
                "API endpoint",
                ProviderOptionType.String,
                "https://api.massive.com",
                "Used for prices, history and symbol search.",
                Advanced: true),
        ],
        EnabledByDefault: false);

    /// <inheritdoc />
    public IMarketDataProvider Create(ProviderCreationContext context) =>
        new MassiveProvider(
            Descriptor,
            EndpointOptions.GetHttpUri(context.Options, Descriptor, BaseUrlKey),
            context.Options.GetString(ApiKeyKey).Trim(),
            context.Options.GetDuration(PollIntervalKey),
            context.Options.GetDuration(RequestSpacingKey),
            AppHttpClient.Create(decompress: true, logger: context.LoggerFactory.CreateLogger<MassiveProvider>()),
            context.TimeProvider,
            context.LoggerFactory.CreateLogger<MassiveProvider>());
}
