namespace FluxTick.App.Views;

/// <summary>
/// Pure geometry helpers used by the watchlist drag-to-reorder interaction.
/// </summary>
internal static class RowReorderMath
{
    /// <summary>
    /// Finds the slot a dragged row would drop into.
    /// </summary>
    /// <param name="rows">
    /// The top and height of every row in its original, undisplaced position, in display order.
    /// </param>
    /// <param name="draggedIndex">
    /// The original index of the dragged row.
    /// </param>
    /// <param name="draggedCenterY">
    /// The current vertical centre of the dragged row, in the same coordinate space as <paramref name="rows"/>.
    /// </param>
    /// <returns>
    /// The target index. It moves past a neighbouring row once the dragged row's leading edge crosses that
    /// row's midpoint, so the first and last slots stay reachable when the drag is clamped to the list.
    /// </returns>
    public static int ComputeTargetIndex(IReadOnlyList<(double Top, double Height)> rows, int draggedIndex, double draggedCenterY)
    {
        if (rows.Count == 0)
        {
            return 0;
        }

        var target = Math.Clamp(draggedIndex, 0, rows.Count - 1);
        var halfHeight = rows[target].Height / 2;
        var top = draggedCenterY - halfHeight;
        var bottom = draggedCenterY + halfHeight;

        while (target < rows.Count - 1 && bottom > Midpoint(rows[target + 1]))
        {
            target++;
        }

        while (target > 0 && top < Midpoint(rows[target - 1]))
        {
            target--;
        }

        return target;
    }

    /// <summary>
    /// Gets the vertical midpoint of a row slot.
    /// </summary>
    /// <param name="row">
    /// The row slot.
    /// </param>
    /// <returns>
    /// The midpoint.
    /// </returns>
    private static double Midpoint((double Top, double Height) row) => row.Top + (row.Height / 2);
}
