using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Tests;

/// <summary>
/// A logger that keeps the level and the formatted message of every entry, for tests that check what was logged.
/// </summary>
internal sealed class ListLogger : ILogger
{
    /// <summary>
    /// The entries logged so far.
    /// </summary>
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    /// <summary>
    /// Gets a snapshot of the entries logged so far.
    /// </summary>
    public IReadOnlyList<(LogLevel Level, string Message)> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_entries)
        {
            _entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
