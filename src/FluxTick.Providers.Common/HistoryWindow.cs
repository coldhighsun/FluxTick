namespace FluxTick.Providers.Common;

/// <summary>
/// How far back a source needs to look to seed a sparkline. The sparkline of the app shows the last 60 minutes only (60
/// points of a minute each), so earlier prices are never shown and asking for them is wasted traffic.
/// </summary>
public static class HistoryWindow
{
    /// <summary>
    /// Gets the span to request: the 60 minutes of the sparkline plus five more, so that the edge of the window and the
    /// delay of a source do not leave a gap.
    /// </summary>
    public static TimeSpan Span { get; } = TimeSpan.FromMinutes(65);
}
