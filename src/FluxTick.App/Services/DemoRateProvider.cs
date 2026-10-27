using FluxTick.Core.Application;

namespace FluxTick.App.Services;

/// <summary>
/// Serves fixed exchange rates, for the offline demo mode.
/// </summary>
public sealed class DemoRateProvider : IExchangeRateProvider
{
    /// <summary>
    /// The rates against the US dollar.
    /// </summary>
    private static readonly Dictionary<string, decimal> Rates = new()
    {
        ["CNY"] = 7.2m,
        ["HKD"] = 7.8m,
        ["EUR"] = 0.92m,
        ["JPY"] = 150m,
        ["GBP"] = 0.79m,
    };

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, decimal>> GetRatesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, decimal>>(Rates);
}
