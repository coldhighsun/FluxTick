namespace FluxTick.App.Views;

/// <summary>
/// The window that hosts the settings page, so it is not limited by the size of the main window.
/// </summary>
public sealed partial class SettingsWindow : ToolWindow
{
    /// <summary>
    /// The fixed height of the window, taller than the other tool windows. Pages that need more, such as General,
    /// Appearance and Data sources, scroll. On a small screen the tool window limits the height to its share of the
    /// screen, and the page scrolls.
    /// </summary>
    private const double InitialHeight = 600;

    /// <summary>
    /// The fixed width, wider than the other tool windows so that the lists of the settings have room.
    /// </summary>
    private const double SettingsWidth = 540;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsWindow"/> class.
    /// </summary>
    public SettingsWindow()
    {
        InitializeComponent();

        // The height is fixed: pages longer than it, such as the data sources, scroll instead.
        CanResize = false;
        Height = InitialHeight;
        Width = MinWidth = MaxWidth = SettingsWidth;
    }
}
