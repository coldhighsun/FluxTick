namespace FluxTick.App.Services;

/// <summary>
/// What the boss key does to the windows and the tray icon, given to <see cref="BossKeyController{T}"/> so that the order of
/// the steps can be tested without real windows.
/// </summary>
/// <typeparam name="T">
/// What stands for a window.
/// </typeparam>
/// <param name="Visible">
/// Lists the windows that are visible now.
/// </param>
/// <param name="RoleOf">
/// Tells what happens to a window.
/// </param>
/// <param name="IsVisible">
/// Tells whether a window is still visible.
/// </param>
/// <param name="Close">
/// Closes a window.
/// </param>
/// <param name="HideForGood">
/// Hides a window that is not brought back.
/// </param>
/// <param name="HideToRestore">
/// Hides a window that is brought back.
/// </param>
/// <param name="Restore">
/// Shows a window that was hidden to be restored.
/// </param>
/// <param name="SetTrayVisible">
/// Shows or hides the tray icon.
/// </param>
/// <param name="OnError">
/// Reports a step that failed. The other steps still run.
/// </param>
public sealed record BossKeyActions<T>(
    Func<IEnumerable<T>> Visible,
    Func<T, BossKeyRole> RoleOf,
    Func<T, bool> IsVisible,
    Action<T> Close,
    Action<T> HideForGood,
    Action<T> HideToRestore,
    Action<T> Restore,
    Action<bool> SetTrayVisible,
    Action<Exception> OnError);

/// <summary>
/// Hides every window and the tray icon, or brings back what was hidden. A step that fails does not stop the others: the
/// tray icon is the way back to the app, so it is shown first on the way back and hidden last on the way out.
/// </summary>
/// <typeparam name="T">
/// What stands for a window.
/// </typeparam>
/// <param name="state">
/// What the boss key hid.
/// </param>
/// <param name="actions">
/// What the steps do.
/// </param>
public sealed class BossKeyController<T>(BossKeyState<T> state, BossKeyActions<T> actions)
{
    /// <summary>
    /// Hides everything, or brings back what was hidden.
    /// </summary>
    public void Toggle()
    {
        if (state.IsHidden)
        {
            var windows = state.Restore();
            Run(() => actions.SetTrayVisible(true));
            foreach (var window in windows)
            {
                Run(() => actions.Restore(window));
            }

            foreach (var show in state.TakeDeferred())
            {
                Run(show);
            }

            return;
        }

        BossKeyPlan<T> plan;
        try
        {
            plan = state.Hide(actions.Visible(), actions.RoleOf);
        }
        catch (Exception ex)
        {
            // The windows could not be listed, for example because one closed at that moment; nothing is hidden yet, so
            // the key press is skipped and the next one tries again.
            actions.OnError(ex);
            return;
        }

        foreach (var window in plan.Close)
        {
            // A window may refuse to close, and must not stay on screen then.
            Run(() =>
            {
                actions.Close(window);
                if (actions.IsVisible(window))
                {
                    actions.HideForGood(window);
                }
            });
        }

        foreach (var window in plan.Drop)
        {
            Run(() => actions.HideForGood(window));
        }

        foreach (var window in plan.Hide)
        {
            Run(() => actions.HideToRestore(window));
        }

        Run(() => actions.SetTrayVisible(false));
    }

    /// <summary>
    /// Runs one step and reports it when it fails.
    /// </summary>
    /// <param name="step">
    /// The step.
    /// </param>
    private void Run(Action step)
    {
        try
        {
            step();
        }
        catch (Exception ex)
        {
            // One failed step must not leave the app half hidden with no way back.
            actions.OnError(ex);
        }
    }
}
