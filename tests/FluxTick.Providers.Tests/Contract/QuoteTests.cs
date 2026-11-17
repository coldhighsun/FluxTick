using FluxTick.Abstractions;

namespace FluxTick.Providers.Tests;

/// <summary>
/// Tests for <see cref="Quote"/>.
/// </summary>
public class QuoteTests
{
    /// <summary>
    /// Verifies a quote built by hand, as the stock sources do, is read against the previous close.
    /// </summary>
    [Fact]
    public void Basis_ByDefault_IsPreviousClose()
    {
        var quote = new Quote(new AssetId("x", "AAPL"), 1m, 1m, DateTimeOffset.UnixEpoch);

        quote.Basis.Should().Be(ReferenceBasis.PreviousClose);
    }
}
