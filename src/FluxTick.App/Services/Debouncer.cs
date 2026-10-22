namespace FluxTick.App.Services;

/// <summary>
/// Runs only the latest of rapidly repeated actions, after a quiet period. Used to avoid saving settings on every
/// step of a slider drag.
/// </summary>
/// <param name="delay">
/// The quiet period to wait before running the action.
/// </param>
/// <param name="timeProvider">
/// The clock used for the delay.
/// </param>
public sealed class Debouncer(TimeSpan delay, TimeProvider timeProvider)
{
    /// <summary>
    /// Cancels the previously scheduled action.
    /// </summary>
    private CancellationTokenSource? _pending;

    /// <summary>
    /// Schedules an action, replacing any action that has not run yet.
    /// </summary>
    /// <param name="action">
    /// The action to run after the quiet period.
    /// </param>
    public void Run(Func<Task> action)
    {
        _pending?.Cancel();
        var cts = new CancellationTokenSource();
        _pending = cts;
        _ = RunAfterDelayAsync(action, cts.Token);
    }

    /// <summary>
    /// Waits for the quiet period and runs the action unless it was superseded.
    /// </summary>
    /// <param name="action">
    /// The action to run.
    /// </param>
    /// <param name="cancellationToken">
    /// A token cancelled when a newer action replaces this one.
    /// </param>
    /// <returns>
    /// A task that completes when the action ran or was superseded.
    /// </returns>
    private async Task RunAfterDelayAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, timeProvider, cancellationToken);
            await action();
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer action.
        }
    }
}
