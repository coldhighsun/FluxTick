using FluxTick.Core.Application;

namespace FluxTick.App.Services;

/// <summary>
/// What the app does just before the setup program of an update starts. The setup program ends the app without its own
/// shutdown, so whatever is still waiting to be saved has to be saved first.
/// </summary>
public static class InstallPreparation
{
    /// <summary>
    /// Creates the preparation: it runs the geometry save that is still waiting for its quiet period, then waits for
    /// the settings writes that are under way, among them the ones started without anyone waiting for them.
    /// </summary>
    /// <param name="geometrySave">
    /// The debouncer of the window position and size, or <see langword="null"/> when the window does not exist yet.
    /// </param>
    /// <param name="settings">
    /// The settings store.
    /// </param>
    /// <returns>
    /// The preparation, to be awaited before the setup program is started.
    /// </returns>
    public static Func<Task> Create(Debouncer? geometrySave, SettingsManager settings) => async () =>
    {
        if (geometrySave is not null)
        {
            await geometrySave.FlushAsync();
        }

        await settings.WhenIdleAsync();
    };
}
