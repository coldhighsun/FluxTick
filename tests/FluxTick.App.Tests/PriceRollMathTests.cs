using FluxTick.App.Views;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for <see cref="PriceRollMath"/>.
/// </summary>
public class PriceRollMathTests
{
    /// <summary>
    /// Verifies a rising price rolls up: the old number leaves upwards and the new one comes from below.
    /// </summary>
    [Fact]
    public void Offsets_PriceRose_OldGoesUpNewComesFromBelow()
    {
        var (oldTo, newFrom) = PriceRollMath.Offsets(up: true, lineHeight: 20);

        oldTo.Should().BeNegative();
        newFrom.Should().BePositive();
    }

    /// <summary>
    /// Verifies a falling price rolls down, the other way round.
    /// </summary>
    [Fact]
    public void Offsets_PriceFell_OldGoesDownNewComesFromAbove()
    {
        var (oldTo, newFrom) = PriceRollMath.Offsets(up: false, lineHeight: 20);

        oldTo.Should().BePositive();
        newFrom.Should().BeNegative();
    }

    /// <summary>
    /// Verifies both numbers travel the same distance, which is a share of the height of the line and so grows with the
    /// size of the font.
    /// </summary>
    /// <param name="lineHeight">
    /// The height of the line of the price.
    /// </param>
    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(30)]
    public void Offsets_DistanceIsAShareOfTheLineHeight(double lineHeight)
    {
        var (oldTo, newFrom) = PriceRollMath.Offsets(up: true, lineHeight);

        Math.Abs(oldTo).Should().BeApproximately(lineHeight * PriceRollMath.Share, 1e-9);
        newFrom.Should().BeApproximately(-oldTo, 1e-9);
    }
}
