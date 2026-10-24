using Avalonia;
using Avalonia.Controls;
using FluxTick.App.Services;
using FluxTick.App.ViewModels;

namespace FluxTick.App.Views;

/// <summary>
/// The small window that lists the watched instruments while the pointer rests on the tray icon. It never takes focus.
/// </summary>
public sealed partial class TrayPopupWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TrayPopupWindow"/> class.
    /// </summary>
    public TrayPopupWindow() => InitializeComponent();

    /// <summary>
    /// Shows the popup next to the tray icon.
    /// </summary>
    /// <param name="icon">
    /// The tray icon bounds in screen pixels.
    /// </param>
    public void ShowAt(PixelRect icon)
    {
        (DataContext as TrayPopupViewModel)?.Refresh();
        var screen = Screens.ScreenFromPoint(icon.Center) ?? Screens.Primary;
        if (screen is null)
        {
            return;
        }

        // The height is not known before the first layout, so the popup is measured first.
        Measure(new Size(Width, double.PositiveInfinity));
        var size = PixelSize.FromSize(DesiredSize, screen.Scaling);
        Position = TrayPopupPlacement.Place(icon, size, screen.WorkingArea);
        if (!IsVisible)
        {
            Show();
        }
    }
}
