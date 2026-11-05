namespace FluxTick.App.Views;

/// <summary>
/// Tells whether the user is dragging something inside the window: shared by the controllers that drag cards, groups
/// and list rows, so the window can tell when the first drag starts and the last one ends.
/// </summary>
internal sealed class DragActivity
{
    /// <summary>
    /// How many drags are going on; at most one in practice.
    /// </summary>
    private int _active;

    /// <summary>
    /// Raised with <see langword="true"/> when the first drag starts and with <see langword="false"/> when the last one
    /// ends.
    /// </summary>
    public event Action<bool>? Changed;

    /// <summary>
    /// Records that a drag started.
    /// </summary>
    public void Begin()
    {
        if (++_active == 1)
        {
            Changed?.Invoke(true);
        }
    }

    /// <summary>
    /// Records that a drag ended. Call it once the drag is fully cleaned up, so that whoever listens sees the final state
    /// of the pointer.
    /// </summary>
    public void End()
    {
        if (_active > 0 && --_active == 0)
        {
            Changed?.Invoke(false);
        }
    }

    /// <summary>
    /// Ends every drag at once, for when the window is taken away mid-drag and the controllers may never report the end.
    /// An end they report later is ignored.
    /// </summary>
    public void Cancel()
    {
        if (_active > 0)
        {
            _active = 0;
            Changed?.Invoke(false);
        }
    }
}
