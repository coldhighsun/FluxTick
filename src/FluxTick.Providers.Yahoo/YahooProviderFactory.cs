using System.Net;
using FluxTick.Abstractions;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Yahoo;

/// <summary>
/// Creates <see cref="YahooProvider"/> instances.
/// </summary>
public sealed class YahooProviderFactory : IMarketDataProviderFactory
{
    /// <summary>
    /// The provider id.
    /// </summary>
    public const string ProviderId = "yahoo";

    /// <summary>
    /// The option key of the poll interval.
    /// </summary>
    public const string PollIntervalKey = "PollInterval";

    /// <summary>
    /// The option key of the base URL.
    /// </summary>
    public const string BaseUrlKey = "BaseUrl";

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new(
        ProviderId,
        "Yahoo Finance",
        "Stocks, ETFs, indices, forex and commodities by polling the unofficial Yahoo Finance API. Data may be delayed.",
        [AssetClass.Equity, AssetClass.Index, AssetClass.Forex, AssetClass.Commodity],
        ProviderUpdateMode.Polling,
        Homepage: new Uri("https://finance.yahoo.com/"),
        Options:
        [
            new ProviderOptionDefinition(
                PollIntervalKey,
                "Poll interval",
                ProviderOptionType.Duration,
                "00:00:05",
                "How often prices are refreshed. Polling too fast may get you rate limited.",
                Min: 2,
                Max: 300),
            new ProviderOptionDefinition(
                BaseUrlKey,
                "API endpoint",
                ProviderOptionType.String,
                "https://query1.finance.yahoo.com",
                "Alternative: https://query2.finance.yahoo.com"),
        ]);

    /// <inheritdoc />
    public IMarketDataProvider Create(ProviderCreationContext context)
    {
        var configured = context.Options.GetString(BaseUrlKey);
        var baseUri = Uri.TryCreate(configured, UriKind.Absolute, out var uri)
            ? uri
            : new Uri(Descriptor.OptionDefinitions.First(o => o.Key == BaseUrlKey).DefaultValue);

        var handler = new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");

        return new YahooProvider(
            Descriptor,
            baseUri,
            context.Options.GetDuration(PollIntervalKey),
            http,
            context.TimeProvider,
            context.LoggerFactory.CreateLogger<YahooProvider>());
    }
}
