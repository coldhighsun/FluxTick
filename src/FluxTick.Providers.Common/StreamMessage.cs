using FluxTick.Abstractions;

namespace FluxTick.Providers.Common;

/// <summary>
/// What one streamed message carried: the quotes it contained and the error it reported, found with a single parse.
/// </summary>
/// <param name="Quotes">
/// The quotes in the message; empty for pongs, control replies and other channels.
/// </param>
/// <param name="Error">
/// The error text when the message rejects a request, otherwise <see langword="null"/>.
/// </param>
public sealed record StreamMessage(IReadOnlyList<Quote> Quotes, string? Error)
{
    /// <summary>
    /// Gets the message that carried nothing of interest.
    /// </summary>
    public static StreamMessage Empty { get; } = new([], null);
}
