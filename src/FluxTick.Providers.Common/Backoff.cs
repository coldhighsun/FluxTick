namespace FluxTick.Providers.Common;

/// <summary>
/// The wait between attempts that grows while a service keeps failing.
/// </summary>
internal static class Backoff
{
    /// <summary>
    /// Doubles a wait, up to a limit.
    /// </summary>
    /// <param name="current">
    /// The wait that was just used.
    /// </param>
    /// <param name="max">
    /// The longest wait there may be.
    /// </param>
    /// <returns>
    /// Twice <paramref name="current"/>, or <paramref name="max"/> if that is less.
    /// </returns>
    public static TimeSpan Double(TimeSpan current, TimeSpan max) => TimeSpan.FromTicks(Math.Min(current.Ticks * 2, max.Ticks));
}
