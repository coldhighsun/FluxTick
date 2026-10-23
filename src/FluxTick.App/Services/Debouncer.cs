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
    /// The action that waits for the quiet period to pass, or <see langword="null"/> when none does.
    /// </summary>
    private Func<Task>? _scheduled;

    /// <summary>
    /// Completes when the action that is running ended, or <see langword="null"/> when none runs.
    /// </summary>
    private Task? _running;

    /// <summary>
    /// Guards the scheduled action, which the delayed run and a flush may both take.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    /// Schedules an action, replacing any action that has not run yet.
    /// </summary>
    /// <param name="action">
    /// The action to run after the quiet period.
    /// </param>
    public void Run(Func<Task> action)
    {
        CancellationTokenSource cts;
        lock (_gate)
        {
            _pending?.Cancel();
            cts = new CancellationTokenSource();
            _pending = cts;
            _scheduled = action;
        }

        _ = RunAfterDelayAsync(action, cts.Token);
    }

    /// <summary>
    /// Runs the scheduled action now instead of after the quiet period, so that it is not lost when the app ends first.
    /// </summary>
    /// <returns>
    /// A task that completes when the action ran, and when an action that had already started ended; completed at once
    /// when nothing was waiting or running.
    /// </returns>
    public async Task FlushAsync()
    {
        Func<Task>? action;
        Task? running;
        lock (_gate)
        {
            _pending?.Cancel();
            action = _scheduled;
            _scheduled = null;
            running = _running;
        }

        // An action that already started must finish too, or the app could end in the middle of it.
        if (running is not null)
        {
            await running;
        }

        if (action is not null)
        {
            await action();
        }
    }

    /// <summary>
    /// Takes the scheduled action if it is still the given one, and records that it is running.
    /// </summary>
    /// <param name="action">
    /// The action that waited.
    /// </param>
    /// <returns>
    /// The source to complete when the action ended, or <see langword="null"/> when the action was replaced or flushed
    /// and must not run.
    /// </returns>
    private TaskCompletionSource? TryTake(Func<Task> action)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_scheduled, action))
            {
                return null;
            }

            _scheduled = null;
            var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _running = running.Task;
            return running;
        }
    }

    /// <summary>
    /// Records that a running action ended.
    /// </summary>
    /// <param name="running">
    /// The source that was returned when the action was taken.
    /// </param>
    private void Finish(TaskCompletionSource running)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_running, running.Task))
            {
                _running = null;
            }
        }

        running.TrySetResult();
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
            if (TryTake(action) is { } running)
            {
                try
                {
                    await action();
                }
                finally
                {
                    Finish(running);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer action.
        }
    }
}
