using FluxTick.Abstractions;
using FluxTick.Core.Application;

namespace FluxTick.Core.Tests;

/// <summary>
/// Tests for how holdings are kept in the settings.
/// </summary>
public class HoldingSettingsTests
{
    /// <summary>
    /// Verifies that a holding survives a settings round trip.
    /// </summary>
    [Fact]
    public void Clone_Holding_IsPreserved()
    {
        var settings = new AppSettings();
        settings.Watchlist.Add(new WatchlistEntry { ProviderId = "p", Symbol = "S", CostPrice = 1.5m, Quantity = 20m, HoldingCurrency = "HKD" });

        var clone = settings.Clone().Watchlist[0];

        clone.CostPrice.Should().Be(1.5m);
        clone.Quantity.Should().Be(20m);
        clone.HoldingCurrency.Should().Be("HKD");
        clone.HasHolding.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that settings written before holdings existed still load.
    /// </summary>
    [Fact]
    public void Deserialize_OldEntry_HasNoHolding()
    {
        var settings = new AppSettings();
        settings.Watchlist.Add(new WatchlistEntry { ProviderId = "p", Symbol = "S" });

        var clone = settings.Clone().Watchlist[0];

        clone.HasHolding.Should().BeFalse();
        clone.CostPrice.Should().BeNull();
    }

    /// <summary>
    /// Verifies that a half-filled holding is cleared and a currency code is normalized.
    /// </summary>
    [Fact]
    public void Normalize_InvalidHolding_IsClearedAndCurrencyNormalized()
    {
        var settings = new AppSettings();
        settings.Watchlist.Add(new WatchlistEntry { ProviderId = "p", Symbol = "A", CostPrice = 5m, HoldingCurrency = "usd" });
        settings.Watchlist.Add(new WatchlistEntry { ProviderId = "p", Symbol = "B", CostPrice = 5m, Quantity = 2m, HoldingCurrency = "hkd" });
        settings.General.BaseCurrency = "cny";

        var changed = settings.Normalize();

        changed.Should().BeTrue();
        settings.Watchlist[0].CostPrice.Should().BeNull();
        settings.Watchlist[0].HoldingCurrency.Should().BeNull();
        settings.Watchlist[1].HoldingCurrency.Should().Be("HKD");
        settings.General.BaseCurrency.Should().Be("CNY");
    }

    /// <summary>
    /// Verifies that a currency the app has no rates for is dropped from the settings.
    /// </summary>
    [Fact]
    public void Normalize_UnsupportedCurrency_IsDropped()
    {
        var settings = new AppSettings();
        settings.Watchlist.Add(new WatchlistEntry { ProviderId = "p", Symbol = "S", CostPrice = 1m, Quantity = 1m, HoldingCurrency = "XXX" });
        settings.General.BaseCurrency = "twd";

        settings.Normalize().Should().BeTrue();

        settings.Watchlist[0].HoldingCurrency.Should().BeNull();
        settings.Watchlist[0].HasHolding.Should().BeTrue();
        settings.General.BaseCurrency.Should().BeNull();
    }

    /// <summary>
    /// Verifies that a holding moves with the instrument.
    /// </summary>
    [Fact]
    public void CopyHoldingFrom_CopiesAllHoldingFields()
    {
        var from = new WatchlistEntry { CostPrice = 3m, Quantity = 4m, HoldingCurrency = "EUR" };
        var to = WatchlistEntry.From(new Asset("p", "S", "S", AssetClass.Equity));

        to.CopyHoldingFrom(from);

        to.CostPrice.Should().Be(3m);
        to.Quantity.Should().Be(4m);
        to.HoldingCurrency.Should().Be("EUR");
    }
}
