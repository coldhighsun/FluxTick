using Avalonia;
using FluxTick.App.Services;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for <see cref="ToastStackMath"/>.
/// </summary>
public class ToastStackMathTests
{
    /// <summary>
    /// The work area of the test screen.
    /// </summary>
    private static readonly PixelRect WorkArea = new(0, 0, 1000, 800);

    /// <summary>
    /// Verifies the newest notification sits in the bottom right corner and older ones stack above it.
    /// </summary>
    [Fact]
    public void Place_StacksUpwardsFromTheBottomRight()
    {
        var positions = ToastStackMath.Place(WorkArea, [new PixelSize(300, 80), new PixelSize(300, 100)]);

        positions[0].Should().Be(new PixelPoint(1000 - ToastStackMath.Margin - 300, 800 - ToastStackMath.Margin - 80));
        positions[1].Y.Should().Be(positions[0].Y - ToastStackMath.Gap - 100);
        positions[1].X.Should().Be(positions[0].X);
    }

    /// <summary>
    /// Verifies a notification never leaves the work area at the top.
    /// </summary>
    [Fact]
    public void Place_TooManyForTheScreen_StaysInsideTheWorkArea()
    {
        var positions = ToastStackMath.Place(new PixelRect(0, 0, 500, 200), [new PixelSize(300, 150), new PixelSize(300, 150)]);

        positions.Should().OnlyContain(p => p.Y >= 0);
    }
}
