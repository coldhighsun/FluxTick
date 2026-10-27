namespace FluxTick.App.Services;

/// <summary>
/// What the boss key does to a window that is visible when it is pressed.
/// </summary>
public enum BossKeyRole
{
    /// <summary>
    /// The window is hidden and shown again by the next press.
    /// </summary>
    Remember,

    /// <summary>
    /// The window is closed, because hiding it would close it anyway and a closed window cannot be shown again.
    /// </summary>
    Close,

    /// <summary>
    /// The window is hidden and not brought back, because it was only there for a moment.
    /// </summary>
    Drop,
}

/// <summary>
/// What the boss key does to the visible windows.
/// </summary>
/// <typeparam name="T">
/// What stands for a window.
/// </typeparam>
/// <param name="Close">
/// The windows to close.
/// </param>
/// <param name="Drop">
/// The windows to hide for good.
/// </param>
/// <param name="Hide">
/// The windows to hide and show again.
/// </param>
public sealed record BossKeyPlan<T>(IReadOnlyList<T> Close, IReadOnlyList<T> Drop, IReadOnlyList<T> Hide);

/// <summary>
/// Remembers which windows the boss key hid, so the next press brings back those and no others, and holds back what
/// would show itself in the meantime.
/// </summary>
/// <typeparam name="T">
/// What stands for a window.
/// </typeparam>
public sealed class BossKeyState<T>
{
    /// <summary>
    /// The windows that were visible when the boss key hid them.
    /// </summary>
    private List<T> _hidden = [];

    /// <summary>
    /// What was asked to show while the windows were hidden.
    /// </summary>
    private List<(string Key, Action Show)> _deferred = [];

    /// <summary>
    /// Gets a value indicating whether the windows are hidden by the boss key now.
    /// </summary>
    public bool IsHidden { get; private set; }

    /// <summary>
    /// Runs something that shows itself, such as a notification, now; or, while the windows are hidden, keeps it until
    /// they are back.
    /// </summary>
    /// <param name="key">
    /// What the thing is about. Asking again with the same key while hidden replaces what was kept, so a repeating alert
    /// shows once when the windows are back, with its latest state, and not once for every time it fired.
    /// </param>
    /// <param name="show">
    /// What shows.
    /// </param>
    public void RunOrDefer(string key, Action show)
    {
        if (!IsHidden)
        {
            show();
            return;
        }

        _deferred.RemoveAll(d => d.Key == key);
        _deferred.Add((key, show));
    }

    /// <summary>
    /// Takes what was held back while the windows were hidden.
    /// </summary>
    /// <returns>
    /// What to run now, in the order it was asked for.
    /// </returns>
    public IReadOnlyList<Action> TakeDeferred()
    {
        var deferred = _deferred.Select(d => d.Show).ToList();
        _deferred = [];
        return deferred;
    }

    /// <summary>
    /// Starts hiding.
    /// </summary>
    /// <param name="visible">
    /// The windows that are visible now.
    /// </param>
    /// <param name="roleOf">
    /// Tells what happens to a window.
    /// </param>
    /// <returns>
    /// What to do with the windows.
    /// </returns>
    public BossKeyPlan<T> Hide(IEnumerable<T> visible, Func<T, BossKeyRole> roleOf)
    {
        var windows = visible.ToList();
        _hidden = [.. windows.Where(w => roleOf(w) == BossKeyRole.Remember)];
        IsHidden = true;
        return new BossKeyPlan<T>(
            [.. windows.Where(w => roleOf(w) == BossKeyRole.Close)],
            [.. windows.Where(w => roleOf(w) == BossKeyRole.Drop)],
            [.. _hidden]);
    }

    /// <summary>
    /// Forgets a window that was closed while hidden, as it cannot be shown again.
    /// </summary>
    /// <param name="window">
    /// The window.
    /// </param>
    public void Forget(T window) => _hidden.Remove(window);

    /// <summary>
    /// Ends hiding.
    /// </summary>
    /// <returns>
    /// The windows to show again, in the order they were hidden.
    /// </returns>
    public IReadOnlyList<T> Restore()
    {
        var restore = _hidden;
        _hidden = [];
        IsHidden = false;
        return restore;
    }
}
