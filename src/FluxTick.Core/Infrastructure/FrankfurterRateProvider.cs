using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FluxTick.Core.Application;

namespace FluxTick.Core.Infrastructure;

/// <summary>
/// Fetches exchange rates from the Frankfurter service, which republishes the reference rates of the European Central
/// Bank.
/// </summary>
/// <param name="http">
/// The client to send requests with. The provider owns it and disposes it.
/// </param>
public sealed class FrankfurterRateProvider(HttpClient http) : IExchangeRateProvider, IDisposable
{
    /// <summary>
    /// The address of the latest rates against the pivot currency.
    /// </summary>
    public static readonly Uri LatestUri = new($"https://api.frankfurter.dev/v1/latest?base={CurrencyCodes.Pivot}");

    /// <inheritdoc/>
    public void Dispose() => http.Dispose();

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, decimal>> GetRatesAsync(CancellationToken cancellationToken)
    {
        var response = await http.GetFromJsonAsync<LatestResponse>(LatestUri, cancellationToken).ConfigureAwait(false);
        return response?.Rates is { Count: > 0 } rates
            ? rates
            : throw new ExchangeRateException("The rate service returned no rates.");
    }

    /// <summary>
    /// The shape of the service's answer.
    /// </summary>
    private sealed class LatestResponse
    {
        /// <summary>
        /// Gets or sets the rates keyed by currency code.
        /// </summary>
        [JsonPropertyName("rates")]
        public Dictionary<string, decimal>? Rates { get; set; }
    }
}
