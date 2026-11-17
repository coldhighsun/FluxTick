using Avalonia;
using Avalonia.Controls;
using FluxTick.App.Controls;

namespace FluxTick.App.Tests;

/// <summary>
/// Helpers shared by the tests of the overflow panel and its items control.
/// </summary>
internal static class OverflowTestHelpers
{
    /// <summary>
    /// Adds children that are 50 pixels wide and 10 pixels high.
    /// </summary>
    /// <param name="panel">
    /// The panel.
    /// </param>
    /// <param name="count">
    /// The number of children.
    /// </param>
    /// <returns>
    /// The new children.
    /// </returns>
    public static List<Border> AddChildren(OverflowPanel panel, int count)
    {
        var children = Enumerable.Range(0, count).Select(_ => new Border { Width = 50, Height = 10 }).ToList();
        foreach (var child in children)
        {
            panel.Children.Add(child);
        }

        return children;
    }

    /// <summary>
    /// Runs a measure and arrange pass with the given width.
    /// </summary>
    /// <param name="panel">
    /// The panel to lay out.
    /// </param>
    /// <param name="width">
    /// The width available to the panel.
    /// </param>
    public static void Layout(OverflowPanel panel, double width)
    {
        panel.Measure(new Size(width, 20));
        panel.Arrange(new Rect(0, 0, width, 20));
    }
}
