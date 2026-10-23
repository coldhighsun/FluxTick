using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using FluxTick.App.Controls;
using static FluxTick.App.Tests.OverflowTestHelpers;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for the panel that hides the footer chips that do not fit.
/// </summary>
public class OverflowPanelTests
{
    /// <summary>
    /// Verifies every child is arranged in order and nothing is reported hidden when all of them fit.
    /// </summary>
    [Fact]
    public void Arrange_AllFit_ArrangesInOrderWithoutHiding()
    {
        var (panel, children) = CreatePanel(3);

        Layout(panel, 200);

        panel.HiddenCount.Should().Be(0);
        panel.HiddenHasProblem.Should().BeFalse();
        children.Select(c => c.Bounds.X).Should().Equal(0, 60, 120);
        panel.DesiredSize.Width.Should().Be(170);
    }

    /// <summary>
    /// Verifies children that do not fit are counted and parked outside the panel's bounds.
    /// </summary>
    [Fact]
    public void Arrange_NotAllFit_HidesTailBeyondTheRightEdge()
    {
        var (panel, children) = CreatePanel(3);

        Layout(panel, 140);

        // 140 minus the 30 pixel indicator and its 10 pixel gap leaves 100: one chip of 50 fits, a second would need 110.
        panel.HiddenCount.Should().Be(2);
        children[0].Bounds.X.Should().Be(0);
        children[1].Bounds.X.Should().BeGreaterThan(140);
        children[2].Bounds.X.Should().BeGreaterThan(140);
    }

    /// <summary>
    /// Verifies a flagged child is moved to the left of healthy ones.
    /// </summary>
    [Fact]
    public void Arrange_LastChildFlagged_MovesItToTheFront()
    {
        var (panel, children) = CreatePanel(3);
        OverflowPanel.SetHasProblem(children[2], true);

        Layout(panel, 200);

        children[2].Bounds.X.Should().Be(0);
        children[0].Bounds.X.Should().Be(60);
        children[1].Bounds.X.Should().Be(120);
    }

    /// <summary>
    /// Verifies a flagged child stays visible and the healthy ones are hidden first.
    /// </summary>
    [Fact]
    public void Arrange_FlaggedChildAndNotAllFit_KeepsFlaggedVisibleAndReportsNoHiddenProblem()
    {
        var (panel, children) = CreatePanel(3);
        OverflowPanel.SetHasProblem(children[2], true);

        Layout(panel, 140);

        children[2].Bounds.X.Should().Be(0);
        panel.HiddenCount.Should().Be(2);
        panel.HiddenHasProblem.Should().BeFalse();
    }

    /// <summary>
    /// Verifies the hidden problem flag is raised once flagged children no longer fit either.
    /// </summary>
    [Fact]
    public void Measure_FlaggedChildrenDoNotFit_ReportsHiddenProblem()
    {
        var (panel, children) = CreatePanel(3);
        foreach (var child in children)
        {
            OverflowPanel.SetHasProblem(child, true);
        }

        Layout(panel, 140);

        panel.HiddenCount.Should().Be(2);
        panel.HiddenHasProblem.Should().BeTrue();
    }

    /// <summary>
    /// Verifies a flag flipping after layout invalidates the panel so the new order is applied.
    /// </summary>
    [Fact]
    public void HasProblem_ChangedAfterLayout_InvalidatesPanelMeasure()
    {
        var (panel, children) = CreatePanel(3);
        Layout(panel, 200);
        panel.IsMeasureValid.Should().BeTrue();

        OverflowPanel.SetHasProblem(children[2], true);

        panel.IsMeasureValid.Should().BeFalse();
        Layout(panel, 200);
        children[2].Bounds.X.Should().Be(0);
    }

    /// <summary>
    /// Verifies the flag is also found on the content of the presenter an items control wraps each item in.
    /// </summary>
    [Fact]
    public void Measure_FlagOnPresenterContent_IsRespected()
    {
        var plain = new Border { Width = 50, Height = 10 };
        var flagged = new Border { Width = 50, Height = 10 };
        OverflowPanel.SetHasProblem(flagged, true);
        var presenter = new ContentPresenter { Content = flagged };
        presenter.UpdateChild();
        var panel = new OverflowPanel { Spacing = 10, ReservedWidth = 30 };
        panel.Children.Add(plain);
        panel.Children.Add(presenter);

        Layout(panel, 200);

        presenter.Bounds.X.Should().Be(0);
        plain.Bounds.X.Should().Be(60);
    }

    /// <summary>
    /// Verifies children that are left out are marked as off screen for automation and visible ones are not.
    /// </summary>
    [Fact]
    public void Arrange_NotAllFit_MarksHiddenChildrenOffscreen()
    {
        var (panel, children) = CreatePanel(3);

        Layout(panel, 140);

        AutomationProperties.GetIsOffscreenBehavior(children[0]).Should().Be(IsOffscreenBehavior.Default);
        AutomationProperties.GetIsOffscreenBehavior(children[1]).Should().Be(IsOffscreenBehavior.Offscreen);
        AutomationProperties.GetIsOffscreenBehavior(children[2]).Should().Be(IsOffscreenBehavior.Offscreen);
    }

    /// <summary>
    /// Verifies a child that becomes visible again after widening is no longer marked off screen.
    /// </summary>
    [Fact]
    public void Arrange_WidenedAfterOverflow_ClearsOffscreenMark()
    {
        var (panel, children) = CreatePanel(3);
        Layout(panel, 140);

        Layout(panel, 200);

        AutomationProperties.GetIsOffscreenBehavior(children[2]).Should().Be(IsOffscreenBehavior.Default);
        panel.HiddenCount.Should().Be(0);
    }

    /// <summary>
    /// Creates a panel with equally sized children: 50 pixels wide, a 10 pixel gap and a 30 pixel overflow indicator.
    /// </summary>
    /// <param name="count">
    /// The number of children.
    /// </param>
    /// <returns>
    /// The panel and its children.
    /// </returns>
    private static (OverflowPanel Panel, List<Border> Children) CreatePanel(int count)
    {
        var panel = new OverflowPanel { Spacing = 10, ReservedWidth = 30 };
        return (panel, AddChildren(panel, count));
    }
}
