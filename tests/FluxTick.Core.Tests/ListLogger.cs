using Microsoft.Extensions.Logging;

namespace FluxTick.Core.Tests;

/// <summary>
/// A logger that keeps the formatted message of every entry, for tests that check what was logged.
/// </summary>
/// <typeparam name="T">
/// The category.
/// </typeparam>
internal sealed class ListLogger<T> : ILogger<T>
{
    /// <summary>
    /// The messages logged so far.
    /// </summary>
    private readonly List<string> _messages = [];

    /// <summary>
    /// Gets a snapshot of the messages logged so far.
    /// </summary>
    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_messages)
            {
                return [.. _messages];
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
        lock (_messages)
        {
            _messages.Add(formatter(state, exception));
        }
    }
}
