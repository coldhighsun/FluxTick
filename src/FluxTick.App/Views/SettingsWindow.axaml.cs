namespace FluxTick.App.Views;

/// <summary>
/// The window that hosts the settings page, so it is not limited by the size of the main window.
/// </summary>
public sealed partial class SettingsWindow : ToolWindow
{
    /// <summary>
    /// The height the window opens with, taller than the other tool windows because the Appearance page is long. On a
    /// small screen the tool window limits it to its share of the screen.
    /// </summary>
    private const double InitialHeight = 560;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsWindow"/> class.
    /// </summary>
    public SettingsWindow()
    {
        InitializeComponent();
        Height = InitialHeight;
    }
}
