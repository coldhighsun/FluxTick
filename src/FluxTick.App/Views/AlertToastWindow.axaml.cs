using Avalonia.Controls;
using Avalonia.Input;

namespace FluxTick.App.Views;

/// <summary>
/// A small window that announces an alert that fired. It never takes focus; a click on it asks for the main window.
/// </summary>
public sealed partial class AlertToastWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AlertToastWindow"/> class.
    /// </summary>
    public AlertToastWindow() => InitializeComponent();

    /// <summary>
    /// Raised when the notification was clicked.
    /// </summary>
    public event EventHandler? Clicked;

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton == MouseButton.Left)
        {
            Clicked?.Invoke(this, EventArgs.Empty);
        }
    }
}
