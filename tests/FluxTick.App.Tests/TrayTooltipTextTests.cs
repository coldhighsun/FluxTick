using FluxTick.App.Services;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for the tooltip of the tray icon.
/// </summary>
public class TrayTooltipTextTests
{
    /// <summary>
    /// Verifies that the tooltip of the tray icon shows the total, falls back to the slogan and never exceeds the limit
    /// of the system.
    /// </summary>
    [Fact]
    public void Compose_TrayTooltip_ShowsTotalOrSloganWithinTheLimit()
    {
        TrayTooltipText.Compose("Total +1.00", "Slogan").Should().Be("FluxTick — Total +1.00");
        TrayTooltipText.Compose("", "Slogan").Should().Be("FluxTick — Slogan");
        TrayTooltipText.Compose(null, "Slogan").Should().Be("FluxTick — Slogan");

        var cut = TrayTooltipText.Compose(new string('x', 300), "Slogan");

        cut.Length.Should().Be(TrayTooltipText.MaxLength);
        cut.Should().EndWith("…");
    }
}
