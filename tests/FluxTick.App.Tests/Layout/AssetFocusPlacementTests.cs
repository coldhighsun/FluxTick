using Avalonia;
using FluxTick.App.Views;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for <see cref="AssetFocusPlacement"/>.
/// </summary>
public class AssetFocusPlacementTests
{
    /// <summary>
    /// The first monitor.
    /// </summary>
    private static readonly PixelRect Left = new(0, 0, 1920, 1080);

    /// <summary>
    /// The second monitor.
    /// </summary>
    private static readonly PixelRect Middle = new(1920, 0, 1920, 1080);

    /// <summary>
    /// The third monitor.
    /// </summary>
    private static readonly PixelRect Right = new(3840, 0, 1920, 1080);

    /// <summary>
    /// Verifies the preferred monitor is used while it is free.
    /// </summary>
    [Fact]
    public void PickScreen_PreferredIsFree_UsesPreferred()
    {
        var (screen, replace) = AssetFocusPlacement.PickScreen([Left, Middle, Right], Middle, [Left]);

        screen.Should().Be(Middle);
        replace.Should().BeFalse();
    }

    /// <summary>
    /// Verifies a free monitor is taken when the preferred one has a view already.
    /// </summary>
    [Fact]
    public void PickScreen_PreferredIsOccupied_UsesFirstFreeMonitor()
    {
        var (screen, replace) = AssetFocusPlacement.PickScreen([Left, Middle, Right], Left, [Left, Middle]);

        screen.Should().Be(Right);
        replace.Should().BeFalse();
    }

    /// <summary>
    /// Verifies the view on the preferred monitor is replaced when every monitor has one.
    /// </summary>
    [Fact]
    public void PickScreen_AllOccupied_ReplacesViewOnPreferred()
    {
        var (screen, replace) = AssetFocusPlacement.PickScreen([Left, Middle], Middle, [Left, Middle]);

        screen.Should().Be(Middle);
        replace.Should().BeTrue();
    }

    /// <summary>
    /// Verifies a single monitor with a view replaces it, so one monitor still shows one instrument.
    /// </summary>
    [Fact]
    public void PickScreen_SingleOccupiedMonitor_Replaces()
    {
        var (screen, replace) = AssetFocusPlacement.PickScreen([Left], Left, [Left]);

        screen.Should().Be(Left);
        replace.Should().BeTrue();
    }

    /// <summary>
    /// Verifies a drop on a free monitor goes there.
    /// </summary>
    [Fact]
    public void ResolveDrop_TargetIsFree_UsesTarget()
    {
        AssetFocusPlacement.ResolveDrop(Middle, Left, [Right]).Should().Be(Middle);
    }

    /// <summary>
    /// Verifies a drop on a monitor that another view fills goes back to the monitor the view came from.
    /// </summary>
    [Fact]
    public void ResolveDrop_TargetIsOccupied_ReturnsHome()
    {
        AssetFocusPlacement.ResolveDrop(Middle, Left, [Middle, Right]).Should().Be(Left);
    }

    /// <summary>
    /// Verifies a drop on the monitor the view is already on keeps it there.
    /// </summary>
    [Fact]
    public void ResolveDrop_TargetIsHome_UsesHome()
    {
        AssetFocusPlacement.ResolveDrop(Left, Left, [Middle]).Should().Be(Left);
    }
}
