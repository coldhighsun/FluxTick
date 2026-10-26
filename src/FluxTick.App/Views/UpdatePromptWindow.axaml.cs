using Avalonia.Controls;
using Avalonia.Interactivity;

namespace FluxTick.App.Views;

/// <summary>
/// A small window that tells the user a newer version exists and offers to install it. It does not take focus when
/// it opens.
/// </summary>
public sealed partial class UpdatePromptWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UpdatePromptWindow"/> class.
    /// </summary>
    public UpdatePromptWindow() => InitializeComponent();

    /// <summary>
    /// Closes the window, leaving the update for later.
    /// </summary>
    /// <param name="sender">
    /// The button.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnLaterClick(object? sender, RoutedEventArgs e) => Close();
}
