using Avalonia.Controls;
using Avalonia.Interactivity;
using FluxTick.Abstractions;
using FluxTick.App.ViewModels;
using FluxTick.App.Views;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for the wiring between the tooltip events of a row or card view and the tooltip of its row.
/// </summary>
public class RowTooltipTrackingTests
{
    /// <summary>
    /// Verifies that the opening and the closing of the tooltip reach the row, when the events are raised on the control
    /// that carries the tooltip, as the framework does: they are direct events and do not bubble up to the view.
    /// </summary>
    [Fact]
    public void Attach_TooltipOpensAndCloses_SetsIsOpenOfTheRow()
    {
        var row = new AssetRowViewModel(new Asset("p", "S", "Name", AssetClass.Crypto), "p");
        var tipHost = new Border { DataContext = row };
        RowTooltipTracking.Attach(tipHost);

        tipHost.RaiseEvent(new CancelRoutedEventArgs(ToolTip.ToolTipOpeningEvent));
        row.TooltipContent.IsOpen.Should().BeTrue();

        tipHost.RaiseEvent(new RoutedEventArgs(ToolTip.ToolTipClosingEvent));
        row.TooltipContent.IsOpen.Should().BeFalse();
    }
}
