using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;

namespace FluxTick.App.Views;

/// <summary>
/// The base of the windows that open modally beside the main window, such as settings and add instrument.
/// They have a fixed width, a resizable height and only a close button.
/// </summary>
public class ToolWindow : Window
{
    /// <summary>
    /// The fixed width in device independent pixels.
    /// </summary>
    private const double FixedWidth = 420;

    /// <summary>
    /// The share of the work area height the window may use.
    /// </summary>
    private const double MaxHeightShare = 0.9;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolWindow"/> class.
    /// </summary>
    public ToolWindow()
    {
        Width = FixedWidth;
        MinWidth = FixedWidth;
        MaxWidth = FixedWidth;
        Height = 520;
        MinHeight = 400;
        CanMaximize = false;
        CanMinimize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        // No system title bar and a see-through window: ToolWindowFrame draws the rounded, themed frame itself.
        WindowDecorations = WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
    }

    /// <summary>
    /// Shows the window modally beside the owner, limited to the screen the owner is on.
    /// </summary>
    /// <param name="owner">
    /// The main window, which cannot be used until this window is closed.
    /// </param>
    /// <returns>
    /// A task that completes when the window is closed.
    /// </returns>
    public Task ShowBesideAsync(Window owner)
    {
        var screen = owner.Screens.ScreenFromWindow(owner) ?? owner.Screens.Primary;
        if (screen is not null)
        {
            LimitToScreen(screen);

            // Place it before it is shown with the expected size so it does not appear elsewhere first,
            // then correct it once the real frame size is known.
            PlaceBeside(owner, screen, new Size(Width, PlacementHeight()));
            Opened += (_, _) => PlaceBeside(owner, screen, FrameSize ?? Bounds.Size);
        }

        return ShowDialog(owner);
    }

    /// <summary>
    /// Shows the window on its own in the middle of the primary screen, without an owner and without blocking anything,
    /// for when there is no main window to open it beside.
    /// </summary>
    public void ShowStandalone()
    {
        if (Screens.Primary is { } screen)
        {
            LimitToScreen(screen);
            Position = ToolWindowPlacement.Center(PixelSize.FromSize(new Size(Width, PlacementHeight()), screen.Scaling), screen.WorkingArea);

            // A window that sizes itself to its content only knows its height once it is shown.
            Opened += (_, _) => Position = ToolWindowPlacement.Center(PixelSize.FromSize(FrameSize ?? Bounds.Size, screen.Scaling), screen.WorkingArea);
        }

        Show();
        Activate();
    }

    /// <summary>
    /// Limits the height of the window to the screen. The limit wins over the preferred minimum so a small display never
    /// pushes the window off-screen.
    /// </summary>
    /// <param name="screen">
    /// The screen the window opens on.
    /// </param>
    private void LimitToScreen(Screen screen)
    {
        var limit = screen.WorkingArea.Height / screen.Scaling * MaxHeightShare;
        MinHeight = Math.Min(MinHeight, limit);
        MaxHeight = limit;
    }

    /// <summary>
    /// Gets the height the window is placed with before it is shown. A window that is as tall as its content has no height
    /// yet, so the content is measured.
    /// </summary>
    /// <returns>
    /// The height in device independent pixels.
    /// </returns>
    private double PlacementHeight()
    {
        var desired = 0.0;
        if (double.IsNaN(Height))
        {
            Measure(new Size(Width, double.PositiveInfinity));
            desired = DesiredSize.Height;
        }

        return ToolWindowPlacement.PlacementHeight(Height, desired, MaxHeight);
    }

    /// <summary>
    /// Closes the window when escape is pressed.
    /// </summary>
    /// <param name="e">
    /// The key event data.
    /// </param>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }

        base.OnKeyDown(e);
    }

    /// <summary>
    /// Moves the window beside the owner.
    /// </summary>
    /// <param name="owner">
    /// The main window.
    /// </param>
    /// <param name="screen">
    /// The screen the owner is on.
    /// </param>
    /// <param name="size">
    /// The size of the window in device independent pixels.
    /// </param>
    private void PlaceBeside(Window owner, Screen screen, Size size)
    {
        var scale = screen.Scaling;
        var pixelSize = PixelSize.FromSize(size, scale);

        // A full screen owner fills the screen, so there is no side to open on: the window goes to the middle instead.
        if (owner.WindowState == WindowState.FullScreen)
        {
            Position = ToolWindowPlacement.Center(pixelSize, screen.Bounds);
            return;
        }

        var ownerBounds = new PixelRect(owner.Position, PixelSize.FromSize(owner.Bounds.Size, scale));
        Position = ToolWindowPlacement.Place(ownerBounds, pixelSize, screen.WorkingArea);
    }
}
