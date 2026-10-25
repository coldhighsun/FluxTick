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
    public TrayPopupWindow()
    {
        InitializeComponent();

        // The list is only laid out once the window is shown, and it changes while shown, so the position follows the
        // real size. The size changes during layout, before anything is drawn, so the popup is never seen misplaced.
        SizeChanged += (_, _) => Reposition();
    }

    /// <summary>
    /// The tray icon bounds in screen pixels the popup sits next to, or <see langword="null"/> before the first show.
    /// </summary>
    private PixelRect? _icon;

    /// <summary>
    /// Shows the popup next to the tray icon.
    /// </summary>
    /// <param name="icon">
    /// The tray icon bounds in screen pixels.
    /// </param>
    public void ShowAt(PixelRect icon)
    {
        (DataContext as TrayPopupViewModel)?.Refresh();
        _icon = icon;

        // The list is laid out only once the window is shown, so this first placement is an estimate that the layout
        // pass corrects as soon as the real size is known. Without a screen there is nowhere to place it.
        if (!Reposition())
        {
            return;
        }

        if (!IsVisible)
        {
            Show();
        }
    }

    /// <summary>
    /// Places the popup next to the tray icon according to its current size.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when there is no icon position or no screen to place it on.
    /// </returns>
    private bool Reposition()
    {
        if (_icon is not { } icon)
        {
            return false;
        }

        var screen = Screens.ScreenFromPoint(icon.Center) ?? Screens.Primary;
        if (screen is null)
        {
            return false;
        }

        Size size;
        if (IsVisible && FrameSize is { } frame && frame.Height > 0)
        {
            size = frame;
        }
        else
        {
            Measure(new Size(Width, double.PositiveInfinity));
            size = DesiredSize;
        }

        var placed = TrayPopupPlacement.Place(icon, PixelSize.FromSize(size, screen.Scaling), screen.WorkingArea);
        if (placed != Position)
        {
            Position = placed;
        }

        return true;
    }
}
