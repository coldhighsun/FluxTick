using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using FluxTick.App.Services;
using FluxTick.App.ViewModels;

namespace FluxTick.App.Views;

/// <summary>
/// The small window that lists the watched instruments after the tray icon was clicked. It takes the focus and closes
/// itself as soon as it loses it, or when Escape is pressed.
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
        _outsideWatch = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, OnOutsideWatchTick) { IsEnabled = false };
        Deactivated += (_, _) => Dismiss();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Dismiss();
            }
        };
    }

    /// <summary>
    /// Occurs when the popup closed itself, because it lost the focus or Escape was pressed.
    /// </summary>
    public event EventHandler? Dismissed;

    /// <summary>
    /// The tray icon bounds in screen pixels the popup sits next to, or <see langword="null"/> before the first show.
    /// </summary>
    private PixelRect? _icon;

    /// <summary>
    /// The margin around the card of the popup in device independent pixels, which holds the shadow. It is the margin of the
    /// border in <c>TrayPopupWindow.axaml</c>.
    /// </summary>
    private const double CardMargin = 6;

    /// <summary>
    /// Looks at the mouse while the popup is shown, to close it when the user clicks elsewhere although the popup never got
    /// the focus.
    /// </summary>
    private readonly DispatcherTimer _outsideWatch;

    /// <summary>
    /// Hides the popup on its own initiative and says so.
    /// </summary>
    private void Dismiss()
    {
        if (IsVisible)
        {
            _outsideWatch.Stop();
            Hide();
            Dismissed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Closes the popup when a mouse button is pressed outside it while it does not have the focus.
    /// </summary>
    /// <param name="sender">
    /// The timer.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnOutsideWatchTick(object? sender, EventArgs e)
    {
        // With the focus, losing it closes the popup, so there is nothing left to watch.
        if (!IsVisible || IsActive)
        {
            _outsideWatch.Stop();
            return;
        }

        // Only the card counts as the popup; the margin around it holds the transparent shadow.
        var margin = (int)Math.Round(CardMargin * RenderScaling);
        var size = PixelSize.FromSize(Bounds.Size, RenderScaling);
        var card = new PixelRect(Position.X + margin, Position.Y + margin, Math.Max(0, size.Width - (2 * margin)), Math.Max(0, size.Height - (2 * margin)));
        if (OperatingSystem.IsWindows() && WindowsTrayCursor.TryGetPosition(out var pointer)
            && TrayPopupPlacement.ShouldDismiss(WindowsTrayCursor.IsButtonDown, pointer, card))
        {
            Dismiss();
        }
    }

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

        // Losing the focus is what closes the popup, so it has to have it first. Where the system refuses to hand it over,
        // the mouse is watched instead.
        Activate();
        _outsideWatch.Start();
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
            Measure(new Size(MaxWidth, double.PositiveInfinity));
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
