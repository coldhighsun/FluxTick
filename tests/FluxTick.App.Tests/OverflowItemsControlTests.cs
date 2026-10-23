using FluxTick.App.Controls;
using static FluxTick.App.Tests.OverflowTestHelpers;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for the items control that mirrors the overflow state of its panel.
/// </summary>
public class OverflowItemsControlTests
{
    /// <summary>
    /// Verifies the panel is created with the spacing and reserved width of the control.
    /// </summary>
    [Fact]
    public void ItemsPanel_Built_UsesSpacingAndReservedWidthOfTheControl()
    {
        var control = new OverflowItemsControl { Spacing = 12, ReservedWidth = 32 };

        var panel = BuildPanel(control);

        panel.Spacing.Should().Be(12);
        panel.ReservedWidth.Should().Be(32);
    }

    /// <summary>
    /// Verifies changing the spacing or the reserved width after the panel exists reaches the panel.
    /// </summary>
    [Fact]
    public void SpacingAndReservedWidth_ChangedAfterPanelCreated_AreForwardedToPanel()
    {
        var control = new OverflowItemsControl { Spacing = 12, ReservedWidth = 32 };
        var panel = BuildPanel(control);

        control.Spacing = 4;
        control.ReservedWidth = 20;

        panel.Spacing.Should().Be(4);
        panel.ReservedWidth.Should().Be(20);
    }

    /// <summary>
    /// Verifies a changed reserved width is used for the next layout, not only stored.
    /// </summary>
    [Fact]
    public void ReservedWidth_IncreasedAfterLayout_HidesMoreItemsOnNextLayout()
    {
        var control = new OverflowItemsControl { Spacing = 10, ReservedWidth = 30 };
        var panel = BuildPanel(control);
        AddChildren(panel, 3);
        Layout(panel, 140);
        control.HiddenCount.Should().Be(2);

        // 140 minus a 100 pixel indicator and its 10 pixel gap leaves 30: not even one 50 pixel item fits.
        control.ReservedWidth = 100;
        Layout(panel, 140);

        control.HiddenCount.Should().Be(3);
    }

    /// <summary>
    /// Verifies the control follows the panel when items overflow and when they fit again.
    /// </summary>
    [Fact]
    public void HiddenCount_PanelOverflowsAndRecovers_ControlMirrorsIt()
    {
        var control = new OverflowItemsControl { Spacing = 10, ReservedWidth = 30 };
        var panel = BuildPanel(control);
        AddChildren(panel, 3);

        Layout(panel, 140);

        control.HiddenCount.Should().Be(2);
        control.HasHiddenItems.Should().BeTrue();

        Layout(panel, 200);

        control.HiddenCount.Should().Be(0);
        control.HasHiddenItems.Should().BeFalse();
    }

    /// <summary>
    /// Verifies the control reports a problem only when a flagged item was left out.
    /// </summary>
    [Fact]
    public void HiddenHasProblem_FlaggedItemsDoNotFit_ControlReportsIt()
    {
        var control = new OverflowItemsControl { Spacing = 10, ReservedWidth = 30 };
        var panel = BuildPanel(control);
        foreach (var child in AddChildren(panel, 3))
        {
            OverflowPanel.SetHasProblem(child, true);
        }

        Layout(panel, 140);

        control.HiddenHasProblem.Should().BeTrue();
    }

    /// <summary>
    /// Verifies a replaced panel resets the state and its predecessor no longer influences the control.
    /// </summary>
    [Fact]
    public void ItemsPanel_BuiltAgain_ResetsStateAndIgnoresThePreviousPanel()
    {
        var control = new OverflowItemsControl { Spacing = 10, ReservedWidth = 30 };
        var first = BuildPanel(control);
        AddChildren(first, 3);
        Layout(first, 140);
        control.HiddenCount.Should().Be(2);

        var second = BuildPanel(control);

        control.HiddenCount.Should().Be(0);
        control.HasHiddenItems.Should().BeFalse();
        Layout(first, 100);
        control.HiddenCount.Should().Be(0);
        AddChildren(second, 3);
        Layout(second, 140);
        control.HiddenCount.Should().Be(2);
    }

    /// <summary>
    /// Builds the panel of a control the way the items presenter does.
    /// </summary>
    /// <param name="control">
    /// The control.
    /// </param>
    /// <returns>
    /// The new panel.
    /// </returns>
    private static OverflowPanel BuildPanel(OverflowItemsControl control) =>
        control.ItemsPanel.Build().Should().BeOfType<OverflowPanel>().Subject;
}
