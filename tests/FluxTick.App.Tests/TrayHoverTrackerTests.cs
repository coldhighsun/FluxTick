using FluxTick.App.Services;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for the decisions about opening and closing the tray popup.
/// </summary>
public class TrayHoverTrackerTests
{
    /// <summary>
    /// Verifies the popup opens only after the pointer rested on the icon for several samples in a row.
    /// </summary>
    [Fact]
    public void Update_OpensAfterEnoughSamplesOnTheIcon()
    {
        var tracker = new TrayHoverTracker();

        tracker.Update(true, false).Should().Be(TrayHoverTracker.Step.None);
        tracker.Update(true, false).Should().Be(TrayHoverTracker.Step.Open);
        tracker.IsOpen.Should().BeTrue();
    }

    /// <summary>
    /// Verifies a pointer that only passes over the icon does not open the popup.
    /// </summary>
    [Fact]
    public void Update_PassingOverTheIcon_DoesNotOpen()
    {
        var tracker = new TrayHoverTracker();

        tracker.Update(true, false);
        tracker.Update(false, false).Should().Be(TrayHoverTracker.Step.None);
        tracker.Update(true, false).Should().Be(TrayHoverTracker.Step.None);
        tracker.IsOpen.Should().BeFalse();
    }

    /// <summary>
    /// Verifies one sample away does not close the popup, as the pointer may be on its way to it, but several do.
    /// </summary>
    [Fact]
    public void Update_ClosesOnlyAfterSeveralSamplesAway()
    {
        var tracker = Open();

        tracker.Update(false, false).Should().Be(TrayHoverTracker.Step.None);
        tracker.Update(false, true).Should().Be(TrayHoverTracker.Step.None);
        tracker.Update(false, false).Should().Be(TrayHoverTracker.Step.None);
        tracker.Update(false, false).Should().Be(TrayHoverTracker.Step.Close);
        tracker.IsOpen.Should().BeFalse();
    }

    /// <summary>
    /// Verifies the pointer on the popup keeps it open.
    /// </summary>
    [Fact]
    public void Update_PointerOnThePopup_KeepsItOpen()
    {
        var tracker = Open();

        for (var i = 0; i < 10; i++)
        {
            tracker.Update(false, true).Should().Be(TrayHoverTracker.Step.None);
        }

        tracker.IsOpen.Should().BeTrue();
    }

    /// <summary>
    /// Verifies resetting closes the popup, tells whether it was open and starts counting afresh.
    /// </summary>
    [Fact]
    public void Reset_ClosesAndForgetsSamples()
    {
        var tracker = Open();

        tracker.Reset().Should().BeTrue();
        tracker.Reset().Should().BeFalse();
        tracker.Update(true, false).Should().Be(TrayHoverTracker.Step.None);
    }

    /// <summary>
    /// Verifies a dismissed popup stays closed while the pointer remains on the icon, and may open again after the pointer
    /// has left it once.
    /// </summary>
    [Fact]
    public void Dismiss_KeepsItClosedUntilThePointerLeavesTheIcon()
    {
        var tracker = Open();

        tracker.Dismiss().Should().BeTrue();
        for (var i = 0; i < 5; i++)
        {
            tracker.Update(true, false).Should().Be(TrayHoverTracker.Step.None);
        }

        tracker.IsOpen.Should().BeFalse();
        tracker.Update(false, false);
        tracker.Update(true, false);
        tracker.Update(true, false).Should().Be(TrayHoverTracker.Step.Open);
    }

    /// <summary>
    /// Creates a tracker whose popup is open.
    /// </summary>
    /// <returns>
    /// The tracker.
    /// </returns>
    private static TrayHoverTracker Open()
    {
        var tracker = new TrayHoverTracker();
        for (var i = 0; i < TrayHoverTracker.EnterSamples; i++)
        {
            tracker.Update(true, false);
        }

        return tracker;
    }
}
