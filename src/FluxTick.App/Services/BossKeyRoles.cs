using FluxTick.App.Views;

namespace FluxTick.App.Services;

/// <summary>
/// Tells what the boss key does to each kind of window. A window class is named here as one that comes back or one that
/// is only there for a moment, or it is a tool window. A class that is none of these is hidden and not brought back, which
/// is the safe side; a test fails for such a class, so it is decided on when it is added.
/// </summary>
internal static class BossKeyRoles
{
    /// <summary>
    /// Gets the windows that are hidden and shown again.
    /// </summary>
    public static IReadOnlyList<Type> Remembered { get; } = [typeof(MainWindow), typeof(AboutWindow), typeof(UpdatePromptWindow)];

    /// <summary>
    /// Gets the windows that are only there for a moment and are not brought back.
    /// </summary>
    public static IReadOnlyList<Type> Momentary { get; } = [typeof(AlertToastWindow), typeof(TrayPopupWindow)];

    /// <summary>
    /// Tells what the boss key does to a window.
    /// </summary>
    /// <param name="window">
    /// The class of the window.
    /// </param>
    /// <returns>
    /// The role of the window.
    /// </returns>
    public static BossKeyRole Of(Type window)
    {
        if (typeof(ToolWindow).IsAssignableFrom(window))
        {
            return BossKeyRole.Close;
        }

        return Remembered.Contains(window) ? BossKeyRole.Remember : BossKeyRole.Drop;
    }
}
