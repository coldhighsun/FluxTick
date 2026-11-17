using FluxTick.App.Views;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for <see cref="RowAnimationPolicy"/>.
/// </summary>
public class RowAnimationPolicyTests
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
        RowAnimationPolicy.ShouldPlay(enabled, visible, playing).Should().Be(expected);
    }

    /// <summary>
    /// Verifies the flash of an alert only needs a view that can be seen: it does not depend on the setting of the flash
    /// on price changes, and a new alert starts over while an earlier one is still playing.
    /// </summary>
    /// <param name="visible">
    /// Whether the view can be seen.
    /// </param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ShouldPlayAlert_FollowsVisibility(bool visible)
    {
        RowAnimationPolicy.ShouldPlayAlert(visible).Should().Be(visible);
    }
}
