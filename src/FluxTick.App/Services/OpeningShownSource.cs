namespace FluxTick.App.Services;

/// <summary>
/// Counts a window that is about to open as showing prices until it does, and from then on reports the real state. So
/// the data sources are not told to stay disconnected by the first settings only to connect a moment later.
/// </summary>
internal sealed class OpeningShownSource : IShownSource
{
    /// <summary>
    /// The source of the real state.
    /// </summary>
    private readonly IShownSource _inner;

    /// <summary>
    /// Whether the window has not opened yet.
    /// </summary>
    private bool _opening;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpeningShownSource"/> class.
    /// </summary>
    /// <param name="inner">
    /// The source of the real state.
    /// </param>
    /// <param name="opening">
    /// Whether the window is about to open.
    /// </param>
    public OpeningShownSource(IShownSource inner, bool opening)
    {
        _inner = inner;
        _opening = opening;
        inner.ShownChanged += (_, _) => ShownChanged?.Invoke(this, EventArgs.Empty);
        inner.Closed += (_, _) =>
        {
            EndOpening();
            Closed?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <inheritdoc />
    public event EventHandler? ShownChanged;

    /// <inheritdoc />
    public event EventHandler? Closed;

    /// <inheritdoc />
    public bool IsShown => _opening || _inner.IsShown;

    /// <summary>
    /// Ends the opening phase, from when the real state counts. Call it when the window opened, and after a time limit
    /// so a window that never opens does not keep the sources connected.
    /// </summary>
    public void EndOpening()
    {
        if (_opening)
        {
            _opening = false;
            ShownChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
