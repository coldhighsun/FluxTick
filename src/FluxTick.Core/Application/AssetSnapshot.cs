using FluxTick.Abstractions;

namespace FluxTick.Core.Application;

/// <summary>
/// How trustworthy the displayed price of an instrument currently is.
/// </summary>
public enum AssetHealth
{
    /// <summary>
    /// No quote received yet.
    /// </summary>
    Waiting,

    /// <summary>
    /// Fresh data is arriving.
    /// </summary>
    Live,

    /// <summary>
    /// The last quote is older than the staleness threshold.
    /// </summary>
    Stale,

    /// <summary>
    /// The provider lost its source and is retrying.
    /// </summary>
    Reconnecting,

    /// <summary>
    /// The provider reports a failure that needs attention.
    /// </summary>
    Failed,

    /// <summary>
    /// The provider is switched off in settings.
    /// </summary>
    ProviderDisabled,

    /// <summary>
    /// The provider is not installed in this build.
    /// </summary>
    ProviderMissing,
}

/// <summary>
/// An immutable view of one tracked instrument.
/// </summary>
/// <param name="Asset">
/// The instrument.
/// </param>
/// <param name="Quote">
/// The latest quote, or <see langword="null"/> when none arrived yet.
/// </param>
/// <param name="Sparkline">
/// The recent prices, oldest first.
/// </param>
/// <param name="Health">
/// The data health.
/// </param>
/// <param name="HealthDetail">
/// Optional detail about the health, such as the last provider error.
/// </param>
public sealed record AssetSnapshot(
    Asset Asset,
    Quote? Quote,
    IReadOnlyList<decimal> Sparkline,
    AssetHealth Health,
    string? HealthDetail = null);
