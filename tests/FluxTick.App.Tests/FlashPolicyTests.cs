using FluxTick.App.Views;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for <see cref="FlashPolicy"/>.
/// </summary>
public class FlashPolicyTests
{
    /// <summary>
    /// Verifies a flash starts only when it is turned on, the view can be seen and no flash of the view is playing.
    /// </summary>
    /// <param name="enabled">
    /// Whether the flash is turned on.
    /// </param>
    /// <param name="visible">
    /// Whether the view can be seen.
    /// </param>
    /// <param name="playing">
    /// Whether a flash is playing.
    /// </param>
    /// <param name="expected">
    /// Whether a flash should start.
    /// </param>
    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(false, false, true, false)]
    public void ShouldPlay_Combinations(bool enabled, bool visible, bool playing, bool expected)
    {
        FlashPolicy.ShouldPlay(enabled, visible, playing).Should().Be(expected);
    }
}
