using FluxTick.App.Views;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for the count of drags going on inside the main window.
/// </summary>
public sealed class DragActivityTests
{
    /// <summary>
    /// A drag that starts and ends is reported once each way.
    /// </summary>
    [Fact]
    public void BeginThenEnd_RaisesTrueThenFalse()
    {
        var activity = new DragActivity();
        var seen = new List<bool>();
        activity.Changed += seen.Add;

        activity.Begin();
        activity.End();

        seen.Should().Equal(true, false);
    }

    /// <summary>
    /// Only the first start and the last end are reported when drags overlap.
    /// </summary>
    [Fact]
    public void NestedDrags_RaiseOnlyForTheOutermost()
    {
        var activity = new DragActivity();
        var seen = new List<bool>();
        activity.Changed += seen.Add;

        activity.Begin();
        activity.Begin();
        activity.End();
        seen.Should().Equal(true);

        activity.End();
        seen.Should().Equal(true, false);
    }

    /// <summary>
    /// An end without a start raises nothing and does not make the next start go unreported.
    /// </summary>
    [Fact]
    public void EndWithoutBegin_IsIgnored()
    {
        var activity = new DragActivity();
        var seen = new List<bool>();
        activity.Changed += seen.Add;

        activity.End();
        activity.Begin();

        seen.Should().Equal(true);
    }

    /// <summary>
    /// Cancelling ends the drags at once, the end a controller reports later is ignored, and the next drag is reported
    /// again.
    /// </summary>
    [Fact]
    public void Cancel_EndsEveryDragAndIgnoresTheLateEnd()
    {
        var activity = new DragActivity();
        var seen = new List<bool>();
        activity.Changed += seen.Add;
        activity.Begin();

        activity.Cancel();
        activity.End();
        activity.Begin();

        seen.Should().Equal(true, false, true);
    }

    /// <summary>
    /// Cancelling when nothing is dragged raises nothing.
    /// </summary>
    [Fact]
    public void Cancel_WithoutDrag_RaisesNothing()
    {
        var activity = new DragActivity();
        var seen = new List<bool>();
        activity.Changed += seen.Add;

        activity.Cancel();

        seen.Should().BeEmpty();
    }
}
