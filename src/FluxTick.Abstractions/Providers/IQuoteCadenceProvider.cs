namespace FluxTick.Abstractions;

/// <summary>
/// Implemented by a provider that fetches prices on a timer, so that the application knows how long a quiet instrument
/// may go without a quote before its price counts as delayed or stale. How often a source delivers follows from its own
/// polling and options, so it belongs to the source and not to the application. A provider that only streams and never
/// polls does not implement it.
/// </summary>
public interface IQuoteCadenceProvider
{
    /// <summary>
    /// Gets how often the provider fetches prices while it polls, or <see langword="null"/> when it does not poll. For a
    /// streaming source this is the fallback poll while the stream is down.
    /// </summary>
    TimeSpan? PollInterval { get; }

    /// <summary>
    /// Gets the longest a quiet instrument of a streaming source can go between two quotes while the source works, because
    /// it is topped up by a poll only now and then (the wait for the poll to be due and for the poll to look included), or
    /// <see langword="null"/> for a source without such a poll. The time without a quote that counts as silence is not
    /// shorter than this.
    /// </summary>
    TimeSpan? QuietPollInterval { get; }
}
