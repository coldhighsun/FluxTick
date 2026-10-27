namespace FluxTick.App.Views;

/// <summary>
/// The window that hosts the settings page, so it is not limited by the size of the main window.
/// </summary>
public sealed partial class SettingsWindow : ToolWindow
{
    /// <summary>
    /// The height the window opens with, taller than the other tool windows because the General and Appearance pages are
    /// long. Measured in the real window, the General page needs about 650 in English and 600 in Chinese, and 700 leaves
    /// room for the message that can appear under the folder buttons. The Data sources page needs about 745 and scrolls.
    /// On a small screen the tool window limits the height to its share of the screen, and the page scrolls.
    /// </summary>
    private const double InitialHeight = 700;

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
        Height = InitialHeight;
        Width = MinWidth = MaxWidth = SettingsWidth;
    }
}
