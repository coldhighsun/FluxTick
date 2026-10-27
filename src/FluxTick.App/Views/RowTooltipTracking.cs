using Avalonia.Controls;
using Avalonia.Interactivity;
using FluxTick.App.ViewModels;

namespace FluxTick.App.Views;

/// <summary>
/// Tells the tooltip of a row or card whether it is open, so that it is only kept up to date while it is shown.
/// </summary>
internal static class RowTooltipTracking
{
    /// <summary>
    /// Follows the tooltip of the control that carries it. The tooltip events are direct events: they are raised on that
    /// control and do not bubble, so the handlers must sit on it and not on the view around it.
    /// </summary>
    /// <param name="view">
    /// The control with the tooltip, inside a row or card view, whose data context is the row.
    /// </param>
    public static void Attach(Control view)
    {
        view.AddHandler(ToolTip.ToolTipOpeningEvent, (_, _) => Set(view, true), RoutingStrategies.Direct | RoutingStrategies.Bubble);
        view.AddHandler(ToolTip.ToolTipClosingEvent, (object? sender, RoutedEventArgs e) => Set(view, false), RoutingStrategies.Direct | RoutingStrategies.Bubble);

        // A view that leaves the tree takes its open tooltip with it, possibly without a closing event.
        view.DetachedFromVisualTree += (_, _) => Set(view, false);
    }

    /// <summary>
    /// Sets whether the tooltip of the row of a view is open.
    /// </summary>
    /// <param name="view">
    /// The row or card view.
    /// </param>
    /// <param name="open">
    /// Whether the tooltip is open.
    /// </param>
    private static void Set(Control view, bool open)
    {
        if (view.DataContext is AssetRowViewModel row)
        {
            row.TooltipContent.IsOpen = open;
        }
    }
}
