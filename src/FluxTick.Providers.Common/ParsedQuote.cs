namespace FluxTick.Providers.Common;

/// <summary>
/// A price line as a batch quote service answers it.
/// </summary>
/// <param name="Key">
/// The code the line answers; matched to the requested codes without regard to case.
/// </param>
/// <param name="Price">
/// The last price.
/// </param>
/// <param name="PreviousClose">
/// The previous close, or <see langword="null"/> when unknown.
/// </param>
/// <param name="Time">
/// The quote time, or <see langword="null"/> when the line carries none that can be read.
/// </param>
public sealed record ParsedQuote(string Key, decimal Price, decimal? PreviousClose, DateTimeOffset? Time);
