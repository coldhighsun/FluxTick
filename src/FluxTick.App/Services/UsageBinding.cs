using FluxTick.Core.Application;

namespace FluxTick.App.Services;

/// <summary>
/// Tells whether something shows prices to the user, such as a window.
/// </summary>
internal interface IShownSource
{
    /// <summary>
    /// Gets a value indicating whether the prices can be seen now.
    /// </summary>
    bool IsShown { get; }

    /// <summary>
    /// Raised when <see cref="IsShown"/> may have changed.
    /// </summary>
    event EventHandler? ShownChanged;

    /// <summary>
    /// Raised when the source is gone for good, which ends any usage.
    /// </summary>
    event EventHandler? Closed;
}

/// <summary>
/// Holds a usage of the market data for as long as a source shows prices, so that the data sources only stay connected
/// while somebody can see them.
/// </summary>
internal sealed class UsageBinding : IDisposable
{
    /// <summary>
    /// The market data service.
    /// </summary>
    private readonly MarketDataService _market;

    /// <summary>
    /// The source that tells whether prices are shown.
    /// </summary>
    private readonly IShownSource _source;

    /// <summary>
    /// The held usage, or <see langword="null"/> while no prices are shown.
    /// </summary>
    private IDisposable? _usage;

    /// <summary>
    /// Whether the source is gone, so that a late change does not take a usage again.
    /// </summary>
    private bool _closed;

    /// <summary>
    /// Initializes a new instance of the <see cref="UsageBinding"/> class and takes the usage when prices are shown.
    /// </summary>
    /// <param name="market">
    /// The market data service.
    /// </param>
    /// <param name="source">
    /// The source that tells whether prices are shown.
    /// </param>
    public UsageBinding(MarketDataService market, IShownSource source)
    {
        _market = market;
        _source = source;
        source.ShownChanged += (_, _) => Update();
        source.Closed += (_, _) =>
        {
            _closed = true;
            Dispose();
        };
        Update();
    }

    /// <summary>
    /// Gets a value indicating whether a usage is held.
    /// </summary>
    public bool IsHeld => _usage is not null;

    /// <summary>
    /// Tells whether a window shows prices to the user: it is visible, not minimized and not parked out of sight at a
    /// screen edge.
    /// </summary>
    /// <param name="isVisible">
    /// Whether the window is shown.
    /// </param>
    /// <param name="isMinimized">
    /// Whether the window is minimized.
    /// </param>
    /// <param name="isDockedAway">
    /// Whether the window rests in its hidden position at a screen edge.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the prices can be seen.
    /// </returns>
    public static bool IsShown(bool isVisible, bool isMinimized, bool isDockedAway) => isVisible && !isMinimized && !isDockedAway;

    /// <summary>
    /// Gives back the usage, if held.
    /// </summary>
    public void Dispose()
    {
        _usage?.Dispose();
        _usage = null;
    }

    /// <summary>
    /// Takes or gives back the usage to match whether prices are shown. Repeating the same state does nothing.
    /// </summary>
    private void Update()
    {
        var shown = !_closed && _source.IsShown;
        if (shown && _usage is null)
        {
            _usage = _market.AcquireUsage();
        }
        else if (!shown && _usage is not null)
        {
            Dispose();
        }
    }
}
