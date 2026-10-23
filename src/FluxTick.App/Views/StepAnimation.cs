using System.Diagnostics;
using Avalonia.Threading;

namespace FluxTick.App.Views;

/// <summary>
/// Runs one animation at a time in small steps on the UI thread. Starting a new one replaces the one that is running.
/// </summary>
internal sealed class StepAnimation
{
    /// <summary>
    /// Fires the steps.
    /// </summary>
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(15) };

    /// <summary>
    /// The handler of the current animation, kept so it can be detached before the next one starts.
    /// </summary>
    private EventHandler _step = (_, _) => { };

    /// <summary>
    /// Starts an animation.
    /// </summary>
    /// <param name="duration">
    /// How long the animation takes.
    /// </param>
    /// <param name="apply">
    /// Called on every step with the elapsed share of the duration, which reaches or passes 1 on the last step.
    /// </param>
    /// <param name="done">
    /// Called once after the last step. It is not called when another animation replaces this one.
    /// </param>
    public void Run(TimeSpan duration, Action<double> apply, Action? done = null)
    {
        _timer.Stop();
        _timer.Tick -= _step;
        var clock = Stopwatch.StartNew();
        _step = (_, _) =>
        {
            var progress = clock.Elapsed / duration;
            apply(progress);
            if (progress >= 1)
            {
                _timer.Stop();
                done?.Invoke();
            }
        };
        _timer.Tick += _step;
        _timer.Start();
    }

    /// <summary>
    /// Stops the running animation, if any, without calling its completion.
    /// </summary>
    public void Stop() => _timer.Stop();
}
