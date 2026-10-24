using Avalonia;

namespace FluxTick.App.Services;

/// <summary>
/// Places the alert notifications in a column at the bottom right of the work area, the newest at the bottom.
/// </summary>
public static class ToastStackMath
{
    /// <summary>
    /// The distance between the column and the edges of the work area, in pixels.
    /// </summary>
    public const int Margin = 12;

    /// <summary>
    /// The distance between two notifications, in pixels.
    /// </summary>
    public const int Gap = 8;

    /// <summary>
    /// Computes the position of every notification.
    /// </summary>
    /// <param name="workArea">
    /// The work area of the screen, in pixels.
    /// </param>
    /// <param name="sizes">
    /// The sizes of the notifications in pixels, the newest first.
    /// </param>
    /// <returns>
    /// The top left corner of each notification, in the order of <paramref name="sizes"/>. A notification that would not
    /// fit above the ones below it stays at the top edge of the work area.
    /// </returns>
    public static PixelPoint[] Place(PixelRect workArea, IReadOnlyList<PixelSize> sizes)
    {
        var positions = new PixelPoint[sizes.Count];
        var bottom = workArea.Bottom - Margin;
        for (var i = 0; i < sizes.Count; i++)
        {
            var top = Math.Max(workArea.Y, bottom - sizes[i].Height);
            positions[i] = new PixelPoint(Math.Max(workArea.X, workArea.Right - Margin - sizes[i].Width), top);
            bottom = top - Gap;
        }

        return positions;
    }
}
