using System.Runtime;

namespace FluxTick.App.Services;

/// <summary>
/// Gives memory back after the watchlist changed. The collector grows its heap while instruments are added and does not
/// collect the oldest generation, nor return its pages, until the process is short of memory, so removing instruments
/// would leave the figure the system shows where it was.
/// </summary>
/// <param name="timeProvider">
/// The clock used for the quiet period.
/// </param>
/// <param name="trim">
/// What gives the memory back; <see langword="null"/> collects the heaps with <see cref="Collect"/>.
/// </param>
public sealed class MemoryTrimmer(TimeProvider timeProvider, Action? trim = null)
{
    /// <summary>
    /// The quiet period after the last change before memory is given back, so that a burst of changes costs one collection.
    /// </summary>
    public static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Waits out the quiet period and runs the trim once.
    /// </summary>
    private readonly Debouncer _pending = new(QuietPeriod, timeProvider);

    /// <summary>
    /// What gives the memory back.
    /// </summary>
    private readonly Action _trim = trim ?? Collect;

    /// <summary>
    /// Schedules a trim after a quiet period; a call that follows before it runs replaces it.
    /// </summary>
    public void Schedule() => _pending.Run(() =>
    {
        _trim();
        return Task.CompletedTask;
    });

    /// <summary>
    /// Collects every generation, compacting the heaps and returning the freed pages to the system. The working set is left
    /// alone, so the pages the app still uses are not faulted back in on the next interaction.
    /// </summary>
    public static void Collect()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;

        // Aggressive also decommits the memory the compaction freed.
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    }
}
