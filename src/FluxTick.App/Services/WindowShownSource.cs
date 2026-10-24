using Avalonia;
using Avalonia.Controls;

namespace FluxTick.App.Services;

/// <summary>
/// Reports a window as showing prices while it is visible, not minimized and not parked out of sight.
/// </summary>
internal sealed class WindowShownSource : IShownSource
{
    /// <summary>
    /// The window.
    /// </summary>
    private readonly Window _window;

    /// <summary>
    /// Tells whether the window is parked out of sight, or <see langword="null"/> for never.
    /// </summary>
    private readonly Func<bool>? _isAway;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowShownSource"/> class.
    /// </summary>
    /// <param name="window">
    /// The window that shows prices.
    /// </param>
    /// <param name="isAway">
    /// Tells whether the window is shown but parked out of sight, such as at a screen edge; <see langword="null"/> for never.
    /// </param>
    /// <param name="subscribeAway">
    /// Subscribes a handler to the changes of <paramref name="isAway"/>.
    /// </param>
    public WindowShownSource(Window window, Func<bool>? isAway = null, Action<EventHandler>? subscribeAway = null)
    {
        _window = window;
        _isAway = isAway;
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.IsVisibleProperty || e.Property == Window.WindowStateProperty)
            {
                ShownChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        subscribeAway?.Invoke((_, _) => ShownChanged?.Invoke(this, EventArgs.Empty));
        window.Closed += (_, _) => Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public event EventHandler? ShownChanged;

    /// <inheritdoc />
    public event EventHandler? Closed;

    /// <inheritdoc />
    public bool IsShown => UsageBinding.IsShown(_window.IsVisible, _window.WindowState == WindowState.Minimized, _isAway?.Invoke() == true);
}
