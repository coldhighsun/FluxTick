using System.Diagnostics;
using Avalonia.Interactivity;
using FluxTick.App.ViewModels;

namespace FluxTick.App.Views;

/// <summary>
/// The window that shows the version, author, license and project home page of the app.
/// </summary>
public sealed partial class AboutWindow : ToolWindow
{
    /// <summary>
    /// The height the window opens with, which is also its only height. It is not below the tool window minimum.
    /// </summary>
    private const double FixedHeight = 440;

    /// <summary>
    /// Initializes a new instance of the <see cref="AboutWindow"/> class.
    /// </summary>
    public AboutWindow()
    {
        InitializeComponent();
        Height = FixedHeight;
        CanResize = false;
    }

    /// <summary>
    /// Opens the project home page in the default browser.
    /// </summary>
    /// <param name="sender">
    /// The button.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnHomepageClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AboutViewModel about)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(about.HomepageUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No browser is registered; there is nothing the window can do about it.
        }
    }
}
