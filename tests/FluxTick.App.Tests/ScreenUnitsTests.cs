using FluxTick.App.Services;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for the unit that window positions and screens are measured in.
/// </summary>
public class ScreenUnitsTests
{
    /// <summary>
    /// Verifies macOS, which reports positions and screens in points, needs no scale, and other systems use the scaling.
    /// </summary>
    /// <param name="scaling">
    /// The scaling of the screen.
    /// </param>
    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void PerPixel_Scaling_DependsOnPlatform(double scaling)
    {
        var expected = OperatingSystem.IsMacOS() ? 1 : scaling;

        ScreenUnits.PerPixel(scaling).Should().Be(expected);
    }
}
