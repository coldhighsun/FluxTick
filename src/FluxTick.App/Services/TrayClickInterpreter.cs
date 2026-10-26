namespace FluxTick.App.Services;

/// <summary>
/// Decides what a click on the tray icon does: one click shows the list of instruments, a second click within the
/// double-click time opens the main window. The list opens at once, without waiting to see whether a second click follows,
/// so a single click never feels slow; the second click then closes the list again.
/// </summary>
public sealed class TrayClickInterpreter
{
    /// <summary>
    /// The time source.
    /// </summary>
    private readonly TimeProvider _time;

    /// <summary>
    /// The longest time between two clicks that still make a double-click.
    /// </summary>
    private readonly TimeSpan _doubleClickTime;

    /// <summary>
    /// How long after the list closed by itself a click on the icon is taken as the one that closed it.
    /// </summary>
    private readonly TimeSpan _reopenGuard;

    /// <summary>
    /// The timestamp of the previous click, or <see langword="null"/> when a click is no longer part of a double-click.
    /// </summary>
    private long? _lastClick;

    /// <summary>
    /// The timestamp at which the list last closed because it lost the focus, or <see langword="null"/> for never.
    /// </summary>
    private long? _dismissedAt;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrayClickInterpreter"/> class.
    /// </summary>
    /// <param name="time">
    /// The time source.
    /// </param>
    /// <param name="doubleClickTime">
    /// The longest time between two clicks that still make a double-click.
    /// </param>
    /// <param name="reopenGuard">
    /// How long after the list closed by itself a click on the icon does not open it again.
    /// </param>
    public TrayClickInterpreter(TimeProvider time, TimeSpan doubleClickTime, TimeSpan reopenGuard)
    {
        _time = time;
        _doubleClickTime = doubleClickTime;
        _reopenGuard = reopenGuard;
    }

    /// <summary>
    /// What a click asks for.
    /// </summary>
    public enum TrayClickAction
    {
        /// <summary>
        /// Nothing changes.
        /// </summary>
        None,

        /// <summary>
        /// The list opens.
        /// </summary>
        ShowList,

        /// <summary>
        /// The list closes.
        /// </summary>
        HideList,

        /// <summary>
        /// The list closes and the main window opens.
        /// </summary>
        OpenWindow,
    }

    /// <summary>
    /// Takes one click on the icon.
    /// </summary>
    /// <param name="listVisible">
    /// Whether the list is shown now.
    /// </param>
    /// <returns>
    /// What the click asks for.
    /// </returns>
    public TrayClickAction Click(bool listVisible)
    {
        var now = _time.GetTimestamp();
        var isDouble = _lastClick is { } last && _time.GetElapsedTime(last, now) <= _doubleClickTime;

        // A double-click is used up by the window it opens, so a third quick click starts over with the list.
        _lastClick = isDouble ? null : now;
        if (isDouble)
        {
            return TrayClickAction.OpenWindow;
        }

        // Pressing the icon takes the focus from the list, which closes it before the click arrives; that click must not
        // bring the list straight back.
        if (_dismissedAt is { } dismissed && _time.GetElapsedTime(dismissed, now) <= _reopenGuard)
        {
            _dismissedAt = null;
            return TrayClickAction.None;
        }

        return listVisible ? TrayClickAction.HideList : TrayClickAction.ShowList;
    }

    /// <summary>
    /// Records that the list closed because it lost the focus.
    /// </summary>
    public void NotifyDismissed() => _dismissedAt = _time.GetTimestamp();
}
