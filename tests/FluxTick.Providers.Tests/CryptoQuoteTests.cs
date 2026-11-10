using FluxTick.Abstractions;
using FluxTick.Providers.Common;

namespace FluxTick.Providers.Tests;

/// <summary>
/// Tests for <see cref="CryptoQuote"/>.
/// </summary>
public class CryptoQuoteTests
{
    /// <summary>
    /// Verifies the quote carries what was passed in and is read against a rolling 24h price.
    /// </summary>
    [Fact]
    public void Create_WithValues_ReturnsRolling24HoursQuote()
    {
        var id = new AssetId("x", "BTC-USD");
        var time = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var quote = CryptoQuote.Create(id, 110m, 100m, time);

        quote.AssetId.Should().Be(id);
        quote.Price.Should().Be(110m);
        quote.Reference.Should().Be(100m);
        quote.Timestamp.Should().Be(time);
        quote.TimeIsEstimated.Should().BeFalse();
        quote.Basis.Should().Be(ReferenceBasis.Rolling24Hours);
    }

    /// <summary>
    /// Verifies an estimated time is flagged.
    /// </summary>
    [Fact]
    public void Create_WithEstimatedTime_FlagsIt()
    {
        var quote = CryptoQuote.Create(new AssetId("x", "BTC-USD"), 1m, null, DateTimeOffset.UnixEpoch, timeIsEstimated: true);

        quote.TimeIsEstimated.Should().BeTrue();
    }
}
